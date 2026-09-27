using System;
using System.Collections.Generic;
using System.Globalization;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Denies at-grade graph edges touching a major road centerline unless the
/// source is explicitly marked as a pedestrian crossing. A road centerline
/// alone cannot establish a safe pedestrian crossing.
/// </summary>
internal sealed class MajorRoadCrossingPolicy
{
    private const double CellDegrees = 0.002;
    private const double IntersectionEpsilon = 1e-12;
    private static readonly HashSet<string> MajorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "motorway", "trunk", "primary", "secondary", "tertiary",
        "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link"
    };
    private readonly Dictionary<(int Lat, int Lon), List<MajorSegment>> cells = new();

    public MajorRoadCrossingPolicy(IReadOnlyList<GeoJsonRoadFeature> features)
    {
        foreach (GeoJsonRoadFeature feature in features)
        {
            if (feature.Highway is null || !MajorTypes.Contains(feature.Highway))
                continue;
            int layer = LayerOf(feature);
            for (int i = 0; i + 1 < feature.Coordinates.Count; i++)
            {
                GeoCoordinate a = feature.Coordinates[i], b = feature.Coordinates[i + 1];
                if (!a.IsValid || !b.IsValid || a == b) continue;
                var segment = new MajorSegment(a, b, layer);
                foreach (var key in CellsFor(a, b))
                {
                    if (!cells.TryGetValue(key, out List<MajorSegment>? bucket))
                        cells[key] = bucket = new List<MajorSegment>();
                    bucket.Add(segment);
                }
            }
        }
    }

    public bool IsUnverifiedCrossing(GeoJsonRoadFeature feature,
        GeoCoordinate a, GeoCoordinate b)
    {
        if (IsMarkedPedestrianCrossing(feature)) return false;
        int layer = LayerOf(feature);
        var visited = new HashSet<MajorSegment>();
        foreach (var key in CellsFor(a, b))
        {
            if (!cells.TryGetValue(key, out List<MajorSegment>? bucket)) continue;
            foreach (MajorSegment road in bucket)
            {
                if (road.Layer == layer && visited.Add(road) &&
                    Intersects(a, b, road.A, road.B))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The gap from a graph endpoint to a facility map pin is never drawn as
    /// a walking segment. A major-road barrier in that gap means this node
    /// cannot establish a safe approach to the facility.
    /// </summary>
    public bool CrossesMajorRoadBetween(GeoCoordinate a, GeoCoordinate b)
    {
        if (!a.IsValid || !b.IsValid || a == b) return false;
        var visited = new HashSet<MajorSegment>();
        foreach (var key in CellsFor(a, b))
        {
            if (!cells.TryGetValue(key, out List<MajorSegment>? bucket)) continue;
            foreach (MajorSegment road in bucket)
            {
                if (road.Layer != 0 || !visited.Add(road) ||
                    !Intersects(a, b, road.A, road.B)) continue;
                bool aOnRoad = OnSegment(road.A, road.B, a);
                bool bOnRoad = OnSegment(road.A, road.B, b);
                // Touching the centerline at only the access endpoint is not
                // a traverse. A coincident access segment remains unsafe.
                if ((aOnRoad && bOnRoad) || (!aOnRoad && !bOnRoad))
                    return true;
            }
        }
        return false;
    }

    private static bool OnSegment(GeoCoordinate a, GeoCoordinate b,
        GeoCoordinate point) =>
        Math.Abs(Cross(a, b, point)) <= IntersectionEpsilon &&
        point.Latitude >= Math.Min(a.Latitude, b.Latitude) - IntersectionEpsilon &&
        point.Latitude <= Math.Max(a.Latitude, b.Latitude) + IntersectionEpsilon &&
        point.Longitude >= Math.Min(a.Longitude, b.Longitude) - IntersectionEpsilon &&
        point.Longitude <= Math.Max(a.Longitude, b.Longitude) + IntersectionEpsilon;

    public static int LayerOf(GeoJsonRoadFeature feature)
    {
        if (feature.Tags.TryGetValue("layer", out string? value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int layer))
            return layer;
        if (feature.Tags.TryGetValue("bridge", out value) &&
            !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return 1;
        if (feature.Tags.TryGetValue("tunnel", out value) &&
            !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return -1;
        return 0;
    }

    private static bool IsMarkedPedestrianCrossing(GeoJsonRoadFeature feature)
    {
        if (feature.Highway is not ("footway" or "path" or "pedestrian"))
            return false;
        if (!feature.Tags.TryGetValue("footway", out string? kind) ||
            !string.Equals(kind, "crossing", StringComparison.OrdinalIgnoreCase))
            return false;
        if (feature.Tags.TryGetValue("crossing", out string? crossing) &&
            (string.Equals(crossing, "marked", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(crossing, "zebra", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(crossing, "traffic_signals", StringComparison.OrdinalIgnoreCase)))
            return true;
        // This export uses crossing=uncontrolled plus an explicit zebra
        // marking for many mapped pedestrian crossings. Uncontrolled alone
        // remains insufficient evidence of a marked crossing.
        return feature.Tags.TryGetValue("crossing:markings", out string? markings) &&
            (string.Equals(markings, "zebra", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(markings, "yes", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<(int Lat, int Lon)> CellsFor(GeoCoordinate a,
        GeoCoordinate b)
    {
        int minLat = (int)Math.Floor(Math.Min(a.Latitude, b.Latitude) / CellDegrees);
        int maxLat = (int)Math.Floor(Math.Max(a.Latitude, b.Latitude) / CellDegrees);
        int minLon = (int)Math.Floor(Math.Min(a.Longitude, b.Longitude) / CellDegrees);
        int maxLon = (int)Math.Floor(Math.Max(a.Longitude, b.Longitude) / CellDegrees);
        for (int lat = minLat; lat <= maxLat; lat++)
            for (int lon = minLon; lon <= maxLon; lon++)
                yield return (lat, lon);
    }

    private static bool Intersects(GeoCoordinate a, GeoCoordinate b,
        GeoCoordinate c, GeoCoordinate d)
    {
        double abC = Cross(a, b, c), abD = Cross(a, b, d);
        double cdA = Cross(c, d, a), cdB = Cross(c, d, b);
        // A walkable centerline on top of a motor-road centerline is equally
        // unverified. Shared geometry is not evidence of a sidewalk.
        if (Math.Abs(abC) <= IntersectionEpsilon &&
            Math.Abs(abD) <= IntersectionEpsilon)
            return Math.Max(Math.Min(a.Latitude, b.Latitude),
                       Math.Min(c.Latitude, d.Latitude)) <=
                       Math.Min(Math.Max(a.Latitude, b.Latitude),
                           Math.Max(c.Latitude, d.Latitude)) + IntersectionEpsilon &&
                   Math.Max(Math.Min(a.Longitude, b.Longitude),
                       Math.Min(c.Longitude, d.Longitude)) <=
                       Math.Min(Math.Max(a.Longitude, b.Longitude),
                           Math.Max(c.Longitude, d.Longitude)) + IntersectionEpsilon;
        return ((abC <= IntersectionEpsilon && abD >= -IntersectionEpsilon) ||
                (abD <= IntersectionEpsilon && abC >= -IntersectionEpsilon)) &&
               ((cdA <= IntersectionEpsilon && cdB >= -IntersectionEpsilon) ||
                (cdB <= IntersectionEpsilon && cdA >= -IntersectionEpsilon));
    }

    private static double Cross(GeoCoordinate a, GeoCoordinate b, GeoCoordinate c) =>
        (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) -
        (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);

    private readonly record struct MajorSegment(GeoCoordinate A,
        GeoCoordinate B, int Layer);
}
