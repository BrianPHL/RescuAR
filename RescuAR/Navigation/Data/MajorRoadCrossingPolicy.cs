using System;
using System.Collections.Generic;
using System.Globalization;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Retains walkable ways ending at major roads, but separates the two sides
/// of an unmarked junction. Interior crossings and overlaps remain blocked.
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
    private readonly HashSet<(double Lat, double Lon)> markedCrossingNodes = new();

    public MajorRoadCrossingPolicy(IReadOnlyList<GeoJsonRoadFeature> features,
        IReadOnlyList<GeoJsonPointFeature> points)
    {
        int nextSegmentId = 0;
        foreach (GeoJsonRoadFeature feature in features)
        {
            if (feature.Highway is null || !MajorTypes.Contains(feature.Highway))
                continue;
            int layer = LayerOf(feature);
            for (int i = 0; i + 1 < feature.Coordinates.Count; i++)
            {
                GeoCoordinate a = feature.Coordinates[i], b = feature.Coordinates[i + 1];
                if (!a.IsValid || !b.IsValid || a == b) continue;
                var segment = new MajorSegment(++nextSegmentId, a, b, layer);
                foreach (var key in CellsFor(a, b))
                {
                    if (!cells.TryGetValue(key, out List<MajorSegment>? bucket))
                        cells[key] = bucket = new List<MajorSegment>();
                    bucket.Add(segment);
                }
            }
        }
        foreach (GeoJsonRoadFeature feature in features)
        {
            if (!IsMarkedPedestrianCrossing(feature)) continue;
            int layer = LayerOf(feature);
            foreach (GeoCoordinate coordinate in feature.Coordinates)
                if (TouchesMajorRoad(coordinate, layer))
                    markedCrossingNodes.Add(Key(coordinate));
        }
        foreach (GeoJsonPointFeature point in points)
            if (IsMarkedPedestrianCrossing(point) &&
                TouchesMajorRoad(point.Coordinate, 0))
                markedCrossingNodes.Add(Key(point.Coordinate));
    }

    public bool BlocksSegment(GeoJsonRoadFeature feature,
        GeoCoordinate a, GeoCoordinate b)
    {
        bool marked = IsMarkedPedestrianCrossing(feature);
        int layer = LayerOf(feature);
        var visited = new HashSet<MajorSegment>();
        foreach (var key in CellsFor(a, b))
        {
            if (!cells.TryGetValue(key, out List<MajorSegment>? bucket)) continue;
            foreach (MajorSegment road in bucket)
            {
                if (road.Layer != layer || !visited.Add(road) ||
                    !Intersects(a, b, road.A, road.B)) continue;
                bool aOnRoad = OnSegment(road.A, road.B, a);
                bool bOnRoad = OnSegment(road.A, road.B, b);
                // A crossing tag does not turn a road-aligned segment into a
                // sidewalk. Marked crossing ways may cross, not follow it.
                bool collinear = Math.Abs(Cross(a, b, road.A)) <= IntersectionEpsilon &&
                    Math.Abs(Cross(a, b, road.B)) <= IntersectionEpsilon;
                if (collinear || (aOnRoad && bOnRoad) ||
                    (!marked && !(aOnRoad ^ bOnRoad))) return true;
            }
        }
        return false;
    }

    // Both ends of the same mapped side share a graph node. Opposite sides
    // get different keys unless an explicit marked crossing exists there.
    public string EndpointSide(GeoCoordinate coordinate,
        GeoCoordinate other, int layer)
    {
        if (markedCrossingNodes.Contains(Key(coordinate))) return string.Empty;
        var sides = new List<string>();
        var visited = new HashSet<MajorSegment>();
        foreach (var key in CellsFor(coordinate, coordinate))
        {
            if (!cells.TryGetValue(key, out List<MajorSegment>? bucket)) continue;
            foreach (MajorSegment road in bucket)
            {
                if (road.Layer != layer || !visited.Add(road) ||
                    !OnSegment(road.A, road.B, coordinate)) continue;
                double cross = Cross(road.A, road.B, other);
                int side = cross > IntersectionEpsilon ? 1 :
                    cross < -IntersectionEpsilon ? -1 : 0;
                sides.Add($"{road.Id}:{side}");
            }
        }
        sides.Sort(StringComparer.Ordinal);
        return string.Join(";", sides);
    }

    private bool TouchesMajorRoad(GeoCoordinate coordinate, int layer)
    {
        var visited = new HashSet<MajorSegment>();
        foreach (var key in CellsFor(coordinate, coordinate))
        {
            if (!cells.TryGetValue(key, out List<MajorSegment>? bucket)) continue;
            foreach (MajorSegment road in bucket)
                if (road.Layer == layer && visited.Add(road) &&
                    OnSegment(road.A, road.B, coordinate)) return true;
        }
        return false;
    }

    private static (double Lat, double Lon) Key(GeoCoordinate coordinate) =>
        (Math.Round(coordinate.Latitude, 7),
         Math.Round(coordinate.Longitude, 7));

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

    private static bool IsMarkedPedestrianCrossing(GeoJsonPointFeature point)
    {
        if (point.Highway is not ("crossing" or "traffic_signals") ||
            point.Barrier is not null ||
            (point.Tags.TryGetValue("foot", out string? foot) &&
             (foot is "no" or "private")) ||
            (point.Tags.TryGetValue("access", out string? access) &&
             (access is "no" or "private"))) return false;
        return (point.Tags.TryGetValue("crossing", out string? crossing) &&
            (crossing is "marked" or "zebra" or "traffic_signals")) ||
            (point.Tags.TryGetValue("crossing:markings", out string? markings) &&
             (markings is "zebra" or "yes"));
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

    private readonly record struct MajorSegment(int Id, GeoCoordinate A,
        GeoCoordinate B, int Layer);
}
