using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.Navigation.Diagnostics;

/// <summary>Session-relative diagnostic coordinates, never a navigation input.</summary>
public sealed class NavigationGeometryTrace(GeoCoordinate origin)
{
    public GeoCoordinate Origin { get; } = origin;

    public (double East, double North) Project(GeoCoordinate point) =>
        ((point.Longitude - Origin.Longitude) * Math.PI / 180 * 6371008.8 * Math.Cos(Origin.Latitude * Math.PI / 180),
            (point.Latitude - Origin.Latitude) * Math.PI / 180 * 6371008.8);

    public string Point(GeoCoordinate point)
    {
        if (!point.IsValid) return "unavailable";
        var (east, north) = Project(point);
        return FormattableString.Invariant($"({east:F3},{north:F3})");
    }

    // Small complete chunks avoid Logcat's per-record truncation. No source
    // vertices are simplified, skipped or rounded to a geographic grid.
    public IEnumerable<string> RouteChunks(IReadOnlyList<RoutePoint> points) => points
        .Select((p, i) => $"{i}:{Point(p.Coordinate)},{p.DistanceFromStartMeters.ToString("F3", CultureInfo.InvariantCulture)}")
        .Chunk(16).Select(chunk => string.Join("|", chunk));

    public static IEnumerable<string> ArChunks(IReadOnlyList<ArHorizontalRoutePoint> points) => points
        .Select((p, i) => FormattableString.Invariant($"{i}:({p.X:F3},{p.Z:F3}),{p.DistanceFromWindowStartMeters:F3}"))
        .Chunk(16).Select(chunk => string.Join("|", chunk));
}
