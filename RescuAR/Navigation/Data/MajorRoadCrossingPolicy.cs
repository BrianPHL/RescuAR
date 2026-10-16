using System;
using System.Collections.Generic;
using System.Globalization;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Checks off-network GPS/facility access gaps. Mapped junctions and crossing
/// ways use source topology, like OSRM; missing zebra markings do not remove edges.
/// </summary>
internal sealed class MajorRoadCrossingPolicy
{
    private const double CellDegrees = 0.002;
    private const double Epsilon = 1e-12;
    private static readonly HashSet<string> MajorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "motorway", "trunk", "primary", "secondary", "tertiary", "busway",
        "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link"
    };
    private readonly Dictionary<(int Lat, int Lon), List<Segment>> cells = new();

    public MajorRoadCrossingPolicy(IReadOnlyList<GeoJsonRoadFeature> features)
    {
        foreach (var feature in features)
        {
            if (feature.Highway is null || !MajorTypes.Contains(feature.Highway)) continue;
            for (int i = 0; i + 1 < feature.Coordinates.Count; i++)
            {
                var a = feature.Coordinates[i];
                var b = feature.Coordinates[i + 1];
                if (!a.IsValid || !b.IsValid || a == b) continue;
                var segment = new Segment(a, b, LayerOf(feature));
                foreach (var key in CellsFor(a, b))
                {
                    if (!cells.TryGetValue(key, out var bucket)) cells[key] = bucket = new();
                    bucket.Add(segment);
                }
            }
        }
    }

    public bool CrossesMajorRoadBetween(GeoCoordinate a, GeoCoordinate b)
    {
        if (!a.IsValid || !b.IsValid || a == b) return false;
        var visited = new HashSet<Segment>();
        foreach (var key in CellsFor(a, b))
        {
            if (!cells.TryGetValue(key, out var bucket)) continue;
            foreach (var road in bucket)
            {
                if (road.Layer != 0 || !visited.Add(road) || !Intersects(a, b, road.A, road.B)) continue;
                bool aOn = OnSegment(road.A, road.B, a), bOn = OnSegment(road.A, road.B, b);
                // A gap following a road centerline is still on that mapped road.
                // Touching at one access endpoint also does not cross the road.
                if (!aOn && !bOn) return true;
            }
        }
        return false;
    }

    public static int LayerOf(GeoJsonRoadFeature feature)
    {
        if (feature.Tags.TryGetValue("layer", out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int layer)) return layer;
        if (feature.Tags.TryGetValue("bridge", out value) && value is not ("no" or "false")) return 1;
        if (feature.Tags.TryGetValue("tunnel", out value) && value is not ("no" or "false")) return -1;
        return 0;
    }

    private static IEnumerable<(int Lat, int Lon)> CellsFor(GeoCoordinate a, GeoCoordinate b)
    {
        int minLat = (int)Math.Floor(Math.Min(a.Latitude, b.Latitude) / CellDegrees);
        int maxLat = (int)Math.Floor(Math.Max(a.Latitude, b.Latitude) / CellDegrees);
        int minLon = (int)Math.Floor(Math.Min(a.Longitude, b.Longitude) / CellDegrees);
        int maxLon = (int)Math.Floor(Math.Max(a.Longitude, b.Longitude) / CellDegrees);
        for (int lat = minLat; lat <= maxLat; lat++)
            for (int lon = minLon; lon <= maxLon; lon++) yield return (lat, lon);
    }

    private static double Cross(GeoCoordinate a, GeoCoordinate b, GeoCoordinate c) =>
        (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) -
        (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);

    private static bool OnSegment(GeoCoordinate a, GeoCoordinate b, GeoCoordinate p) =>
        Math.Abs(Cross(a, b, p)) <= Epsilon &&
        p.Latitude >= Math.Min(a.Latitude, b.Latitude) - Epsilon &&
        p.Latitude <= Math.Max(a.Latitude, b.Latitude) + Epsilon &&
        p.Longitude >= Math.Min(a.Longitude, b.Longitude) - Epsilon &&
        p.Longitude <= Math.Max(a.Longitude, b.Longitude) + Epsilon;

    private static bool Intersects(GeoCoordinate a, GeoCoordinate b, GeoCoordinate c, GeoCoordinate d)
    {
        double abC = Cross(a, b, c), abD = Cross(a, b, d);
        double cdA = Cross(c, d, a), cdB = Cross(c, d, b);
        if (Math.Abs(abC) <= Epsilon && Math.Abs(abD) <= Epsilon)
            return Math.Max(Math.Min(a.Latitude, b.Latitude), Math.Min(c.Latitude, d.Latitude)) <=
                       Math.Min(Math.Max(a.Latitude, b.Latitude), Math.Max(c.Latitude, d.Latitude)) + Epsilon &&
                   Math.Max(Math.Min(a.Longitude, b.Longitude), Math.Min(c.Longitude, d.Longitude)) <=
                       Math.Min(Math.Max(a.Longitude, b.Longitude), Math.Max(c.Longitude, d.Longitude)) + Epsilon;
        return ((abC <= Epsilon && abD >= -Epsilon) || (abD <= Epsilon && abC >= -Epsilon)) &&
               ((cdA <= Epsilon && cdB >= -Epsilon) || (cdB <= Epsilon && cdA >= -Epsilon));
    }
    private readonly record struct Segment(GeoCoordinate A, GeoCoordinate B, int Layer);
}
