using System;
using System.Collections.Generic;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Data;

namespace RescuAR.Navigation.Hazards;

/// <summary>
/// Small city-scale geographic helpers shared by route monitoring and the
/// hazard-aware A* edge filter.
/// </summary>
public static class RouteHazardGeometry
{
    private const double EarthRadiusMeters =
        6371008.8;

    public readonly record struct RouteHazardIntersection(
        bool IsAffected,
        RouteHazard? Hazard,
        double MinimumDistanceMeters,
        double DistanceAheadMeters,
        int SegmentIndex)
    {
        public static RouteHazardIntersection None =>
            new(
                false,
                null,
                double.PositiveInfinity,
                double.PositiveInfinity,
                -1);
    }

    /// <summary>
    /// Finds the first hazardous point on the remaining route. Route progress
    /// is used to ignore segments already completed by the user.
    /// </summary>
    public static RouteHazardIntersection FindFirstIntersection(
        RouteResult route,
        double progressMeters,
        IReadOnlyList<RouteHazard> hazards)
    {
        ArgumentNullException.ThrowIfNull(
            route);

        ArgumentNullException.ThrowIfNull(
            hazards);

        if (route.Points.Count < 2 ||
            hazards.Count == 0)
        {
            return RouteHazardIntersection.None;
        }

        double safeProgressMeters =
            double.IsFinite(progressMeters)
                ? Math.Max(0.0, progressMeters)
                : 0.0;

        RouteHazardIntersection best =
            RouteHazardIntersection.None;

        IReadOnlyList<RoutePoint> points =
            route.Points;

        for (int segmentIndex = 1;
             segmentIndex < points.Count;
             segmentIndex++)
        {
            RoutePoint from =
                points[segmentIndex - 1];

            RoutePoint to =
                points[segmentIndex];

            if (to.DistanceFromStartMeters + 0.01 <
                safeProgressMeters)
            {
                continue;
            }

            for (int hazardIndex = 0;
                 hazardIndex < hazards.Count;
                 hazardIndex++)
            {
                RouteHazard hazard =
                    hazards[hazardIndex];

                SegmentDistanceResult distance =
                    DistancePointToSegmentMeters(
                        hazard.Coordinate,
                        from.Coordinate,
                        to.Coordinate);

                if (distance.DistanceMeters >
                    hazard.RadiusMeters)
                {
                    continue;
                }

                double segmentSpanMeters =
                    Math.Max(
                        0.0,
                        to.DistanceFromStartMeters -
                        from.DistanceFromStartMeters);

                double projectedRouteDistance =
                    from.DistanceFromStartMeters +
                    (segmentSpanMeters * distance.SegmentFraction);

                double distanceAheadMeters =
                    Math.Max(
                        0.0,
                        projectedRouteDistance -
                        safeProgressMeters);

                if (!best.IsAffected ||
                    distanceAheadMeters <
                        best.DistanceAheadMeters)
                {
                    best =
                        new RouteHazardIntersection(
                            true,
                            hazard,
                            distance.DistanceMeters,
                            distanceAheadMeters,
                            segmentIndex - 1);
                }
            }
        }

        return best;
    }

    public static bool RouteIntersectsAnyHazard(
        RouteResult route,
        double progressMeters,
        IReadOnlyList<RouteHazard> hazards)
    {
        return FindFirstIntersection(
                route,
                progressMeters,
                hazards)
            .IsAffected;
    }

    /// <summary>
    /// Validates a newly calculated route against all hazards while allowing
    /// a user who is already inside a newly reported hazard to travel outward
    /// through the initial exclusion area. After the route exits that hazard,
    /// any re-entry is treated as unsafe.
    /// </summary>
    public static RouteHazardIntersection FindFirstUnsafeIntersectionFromOrigin(
        RouteResult route, GeoCoordinate origin, IReadOnlyList<RouteHazard> hazards)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(hazards);
        RouteHazardIntersection best = RouteHazardIntersection.None;
        for (int index = 1; index < route.Points.Count; index++)
        {
            RoutePoint from = route.Points[index - 1], to = route.Points[index];
            foreach (var hazard in hazards)
            {
                if (IsSegmentSafeFromOrigin(from.Coordinate, to.Coordinate, origin, new[] { hazard })) continue;
                var nearest = DistancePointToSegmentMeters(hazard.Coordinate, from.Coordinate, to.Coordinate);
                double along = from.DistanceFromStartMeters +
                    Math.Max(0, to.DistanceFromStartMeters - from.DistanceFromStartMeters) * nearest.SegmentFraction;
                if (!best.IsAffected || along < best.DistanceAheadMeters)
                    best = new(true, hazard, nearest.DistanceMeters, along, index - 1);
            }
        }
        return best;
    }

    public static bool RouteAvoidsHazardsFromOrigin(
        RouteResult route,
        GeoCoordinate origin,
        IReadOnlyList<RouteHazard> hazards)
    {
        for (int i = 1; i < route.Points.Count; i++)
            if (!IsSegmentSafeFromOrigin(route.Points[i - 1].Coordinate,
                    route.Points[i].Coordinate, origin, hazards)) return false;
        return true;
    }

    /// <summary>Shared A*/MLD escape rule: move outward, then never re-enter.</summary>
    public static bool IsSegmentSafeFromOrigin(GeoCoordinate from, GeoCoordinate to,
        GeoCoordinate origin, IReadOnlyList<RouteHazard> hazards)
    {
        foreach (var hazard in hazards)
        {
            var nearest = DistancePointToSegmentMeters(hazard.Coordinate, from, to);
            if (nearest.DistanceMeters > hazard.RadiusMeters) continue;
            double startDistance = from.DistanceTo(hazard.Coordinate);
            double endDistance = to.DistanceTo(hazard.Coordinate);
            double epsilon = EvacuationRoutingPolicy.EscapeProgressMeters;
            if (origin.DistanceTo(hazard.Coordinate) <= hazard.RadiusMeters &&
                startDistance <= hazard.RadiusMeters &&
                endDistance > startDistance + epsilon &&
                nearest.DistanceMeters >= startDistance - epsilon) continue;
            if (from.DistanceTo(to) <= 0.01 && startDistance <= hazard.RadiusMeters &&
                origin.DistanceTo(hazard.Coordinate) <= hazard.RadiusMeters) continue;
            return false;
        }
        return true;
    }

    public static bool EdgeIntersectsHazard(
        RoadEdge edge,
        RouteHazard hazard)
    {
        ArgumentNullException.ThrowIfNull(
            edge);

        ArgumentNullException.ThrowIfNull(
            hazard);

        return DistancePointToSegmentMeters(
                hazard.Coordinate,
                edge.From.Coordinate,
                edge.To.Coordinate)
            .DistanceMeters <=
            hazard.RadiusMeters;
    }

    public readonly record struct SegmentDistanceResult(
        double DistanceMeters,
        double SegmentFraction);

    /// <summary>
    /// Distance from a WGS84 point to a short geographic segment using a local
    /// tangent-plane approximation. At Marikina pedestrian-routing scale this
    /// is materially more than sufficient and avoids heavyweight GIS deps.
    /// </summary>
    public static SegmentDistanceResult DistancePointToSegmentMeters(
        GeoCoordinate point,
        GeoCoordinate segmentStart,
        GeoCoordinate segmentEnd)
    {
        double referenceLatitudeRadians =
            DegreesToRadians(
                point.Latitude);

        (double X, double Y) start =
            ToLocalMeters(
                segmentStart,
                point,
                referenceLatitudeRadians);

        (double X, double Y) end =
            ToLocalMeters(
                segmentEnd,
                point,
                referenceLatitudeRadians);

        double dx =
            end.X -
            start.X;

        double dy =
            end.Y -
            start.Y;

        double denominator =
            (dx * dx) +
            (dy * dy);

        double fraction;

        if (denominator <= 0.000001)
        {
            fraction =
                0.0;
        }
        else
        {
            // The query point is local-space (0,0).
            fraction =
                Math.Clamp(
                    -((start.X * dx) +
                      (start.Y * dy)) /
                    denominator,
                    0.0,
                    1.0);
        }

        double closestX =
            start.X +
            (dx * fraction);

        double closestY =
            start.Y +
            (dy * fraction);

        return new SegmentDistanceResult(
            Math.Sqrt(
                (closestX * closestX) +
                (closestY * closestY)),
            fraction);
    }

    private static (double X, double Y) ToLocalMeters(
        GeoCoordinate coordinate,
        GeoCoordinate origin,
        double originLatitudeRadians)
    {
        double longitudeDeltaRadians =
            DegreesToRadians(
                coordinate.Longitude -
                origin.Longitude);

        double latitudeDeltaRadians =
            DegreesToRadians(
                coordinate.Latitude -
                origin.Latitude);

        return (
            longitudeDeltaRadians *
                Math.Cos(originLatitudeRadians) *
                EarthRadiusMeters,
            latitudeDeltaRadians *
                EarthRadiusMeters);
    }

    private static double DegreesToRadians(
        double degrees)
    {
        return degrees *
            (Math.PI / 180.0);
    }
}
