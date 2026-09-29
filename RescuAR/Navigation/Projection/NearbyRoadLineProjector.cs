using System;
using System.Collections.Generic;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Projection;

/// <summary>Clips every embedded GeoJSON segment to a local diagnostic circle.</summary>
public static class NearbyRoadLineProjector
{
    public readonly record struct RoadLineSegment(
        ArHorizontalRoutePoint Start,
        ArHorizontalRoutePoint End);

    public static IReadOnlyList<RoadLineSegment> Project(
        IReadOnlyList<GeoJsonRoadFeature> roads,
        GeoCoordinate center,
        double radiusMeters,
        double mapToArYawDegrees)
    {
        ArgumentNullException.ThrowIfNull(roads);
        if (!center.IsValid || !double.IsFinite(radiusMeters) ||
            radiusMeters <= 0 || !double.IsFinite(mapToArYawDegrees))
            throw new ArgumentOutOfRangeException(nameof(center));

        double cosLatitude = Math.Cos(center.Latitude * Math.PI / 180.0);
        var lines = new List<RoadLineSegment>();

        foreach (GeoJsonRoadFeature road in roads)
        {
            for (int i = 0; i + 1 < road.Coordinates.Count; i++)
            {
                GeoCoordinate from = road.Coordinates[i], to = road.Coordinates[i + 1];
                if (!from.IsValid || !to.IsValid) continue;
                double ax = (from.Longitude - center.Longitude) * Math.PI / 180.0 * 6371008.8 * cosLatitude;
                double ay = (from.Latitude - center.Latitude) * Math.PI / 180.0 * 6371008.8;
                double bx = (to.Longitude - center.Longitude) * Math.PI / 180.0 * 6371008.8 * cosLatitude;
                double by = (to.Latitude - center.Latitude) * Math.PI / 180.0 * 6371008.8;
                double dx = bx - ax, dy = by - ay;
                double lengthSquared = dx * dx + dy * dy;
                if (lengthSquared < 0.01) continue;

                // Parametric intersection with the circle. Each source segment
                // remains independent, so no imaginary joining line is drawn.
                double b = 2 * (ax * dx + ay * dy);
                double c = ax * ax + ay * ay - radiusMeters * radiusMeters;
                double discriminant = b * b - 4 * lengthSquared * c;
                if (discriminant < 0) continue;
                double root = Math.Sqrt(discriminant);
                double enter = Math.Max(0, (-b - root) / (2 * lengthSquared));
                double exit = Math.Min(1, (-b + root) / (2 * lengthSquared));
                if (Math.Sqrt(lengthSquared) * (exit - enter) <= 0.06)
                    continue;

                double x0 = ax + dx * enter, y0 = ay + dy * enter;
                double x1 = ax + dx * exit, y1 = ay + dy * exit;
                var start = MapToArCoordinates.Rotate(x0, y0, mapToArYawDegrees);
                var end = MapToArCoordinates.Rotate(x1, y1, mapToArYawDegrees);
                lines.Add(new RoadLineSegment(
                    new ArHorizontalRoutePoint((float)start.X, (float)start.Z, 0),
                    new ArHorizontalRoutePoint((float)end.X, (float)end.Z, 0)));
            }
        }
        return lines;
    }
}
