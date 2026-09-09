using System;
using System.Collections.Generic;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Projection;

/// <summary>
/// Converts a short WGS84 route section into a local East/North meter frame.
///
/// The original overload projects from the beginning of the route.
///
/// The progress-aware overload begins at an arbitrary cumulative route
/// distance. This lets GPS progress advance the visible AR window without
/// requesting a new MLD route on every location sample.
/// </summary>
public static class LocalRouteProjector
{
    private const double EarthRadiusMeters =
        6371008.8;

    public static IReadOnlyList<LocalRoutePoint> ProjectWindow(
        RouteResult route,
        GeoCoordinate reference,
        double maxDistanceMeters = 7.5)
    {
        ArgumentNullException.ThrowIfNull(
            route);

        double startDistanceMeters =
            route.Points.Count >
                0
                ? route.Points[0]
                    .DistanceFromStartMeters
                : 0.0;

        return ProjectWindow(
            route,
            startDistanceMeters,
            reference,
            maxDistanceMeters);
    }

    /// <summary>
    /// Projects a short route window beginning at startDistanceMeters.
    ///
    /// reference is normally the route coordinate snapped to the user's
    /// current progress. Therefore the first projected point is approximately
    /// local (0,0), after which map-to-AR alignment and an AR camera-relative
    /// horizontal offset place the window close to the user.
    /// </summary>
    public static IReadOnlyList<LocalRoutePoint> ProjectWindow(
        RouteResult route,
        double startDistanceMeters,
        GeoCoordinate reference,
        double maxDistanceMeters = 7.5)
    {
        ArgumentNullException.ThrowIfNull(
            route);

        if (!reference.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reference));
        }

        if (maxDistanceMeters <=
            0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDistanceMeters));
        }

        if (route.Points.Count ==
            0)
        {
            return [];
        }

        List<RoutePoint> window =
            ExtractWindow(
                route.Points,
                startDistanceMeters,
                maxDistanceMeters);

        if (window.Count ==
            0)
        {
            return [];
        }

        List<LocalRoutePoint> result =
            new(
                window.Count);

        double actualWindowStartDistance =
            window[0]
                .DistanceFromStartMeters;

        double referenceLatitudeRadians =
            DegreesToRadians(
                reference.Latitude);

        double cosReferenceLatitude =
            Math.Cos(
                referenceLatitudeRadians);

        foreach (RoutePoint point in
                 window)
        {
            double deltaLatitudeRadians =
                DegreesToRadians(
                    point.Coordinate.Latitude -
                    reference.Latitude);

            double deltaLongitudeRadians =
                DegreesToRadians(
                    point.Coordinate.Longitude -
                    reference.Longitude);

            double northMeters =
                deltaLatitudeRadians *
                EarthRadiusMeters;

            double eastMeters =
                deltaLongitudeRadians *
                EarthRadiusMeters *
                cosReferenceLatitude;

            result.Add(
                new LocalRoutePoint(
                    point.Coordinate,
                    eastMeters,
                    northMeters,
                    Math.Max(
                        0.0,
                        point.DistanceFromStartMeters -
                        actualWindowStartDistance)));
        }

        return result;
    }

    /// <summary>
    /// Extracts a route window beginning at an arbitrary cumulative distance.
    ///
    /// Both the first point and final point are interpolated when the desired
    /// boundaries fall inside source geometry segments.
    /// </summary>
    private static List<RoutePoint> ExtractWindow(
        IReadOnlyList<RoutePoint> points,
        double startDistanceMeters,
        double maxDistanceMeters)
    {
        List<RoutePoint> result =
            new();

        if (points.Count ==
            0)
        {
            return result;
        }

        double firstDistance =
            points[0]
                .DistanceFromStartMeters;

        double lastDistance =
            points[^1]
                .DistanceFromStartMeters;

        double clampedStart =
            Math.Clamp(
                startDistanceMeters,
                firstDistance,
                lastDistance);

        double targetDistance =
            Math.Min(
                lastDistance,
                clampedStart +
                maxDistanceMeters);

        RoutePoint startPoint =
            InterpolateAtDistance(
                points,
                clampedStart);

        result.Add(
            startPoint);

        const double distanceEpsilon =
            0.001;

        for (int i = 0;
             i < points.Count;
             i++)
        {
            RoutePoint point =
                points[i];

            if (point.DistanceFromStartMeters <=
                clampedStart +
                    distanceEpsilon)
            {
                continue;
            }

            if (point.DistanceFromStartMeters >=
                targetDistance -
                    distanceEpsilon)
            {
                break;
            }

            result.Add(
                point);
        }

        if (targetDistance >
            clampedStart +
                distanceEpsilon)
        {
            RoutePoint endPoint =
                InterpolateAtDistance(
                    points,
                    targetDistance);

            RoutePoint lastAdded =
                result[^1];

            if (endPoint.Coordinate !=
                    lastAdded.Coordinate ||
                Math.Abs(
                    endPoint.DistanceFromStartMeters -
                    lastAdded.DistanceFromStartMeters) >
                    distanceEpsilon)
            {
                result.Add(
                    endPoint);
            }
        }

        return result;
    }

    private static RoutePoint InterpolateAtDistance(
        IReadOnlyList<RoutePoint> points,
        double distanceMeters)
    {
        if (distanceMeters <=
            points[0]
                .DistanceFromStartMeters)
        {
            return points[0];
        }

        for (int i = 1;
             i < points.Count;
             i++)
        {
            RoutePoint end =
                points[i];

            if (end.DistanceFromStartMeters <
                distanceMeters)
            {
                continue;
            }

            RoutePoint start =
                points[i - 1];

            double span =
                end.DistanceFromStartMeters -
                start.DistanceFromStartMeters;

            if (span <=
                0.0001)
            {
                return new RoutePoint(
                    end.Coordinate,
                    distanceMeters);
            }

            double t =
                Math.Clamp(
                    (distanceMeters -
                     start.DistanceFromStartMeters)
                    /
                    span,
                    0.0,
                    1.0);

            GeoCoordinate interpolated =
                new(
                    start.Coordinate.Latitude +
                        (end.Coordinate.Latitude -
                         start.Coordinate.Latitude) *
                        t,
                    start.Coordinate.Longitude +
                        (end.Coordinate.Longitude -
                         start.Coordinate.Longitude) *
                        t);

            return new RoutePoint(
                interpolated,
                distanceMeters);
        }

        return new RoutePoint(
            points[^1]
                .Coordinate,
            points[^1]
                .DistanceFromStartMeters);
    }

    private static double DegreesToRadians(
        double degrees)
    {
        return degrees *
            Math.PI /
            180.0;
    }
}
