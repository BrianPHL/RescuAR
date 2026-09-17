using System;
using System.Collections.Generic;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.Navigation.Guidance;

/// <summary>
/// Lightweight pedestrian turn-state inference from the canonical route
/// polyline.
///
/// OSRM maneuver/step metadata is not required. The service samples the
/// retained route ahead of committed progress and classifies the first
/// meaningful change in geographic bearing.
/// </summary>
public sealed class PedestrianTurnGuidanceService
{
    private const double SampleSpacingMeters =
        5.0;

    private const double LookAheadMeters =
        35.0;

    private const double MinimumTurnAngleDegrees =
        30.0;

    private const double ArrivalDistanceMeters =
        6.0;

    public TurnGuidanceSnapshot Evaluate(
        RouteResult route,
        double progressMeters)
    {
        return Evaluate(
            route,
            progressMeters,
            sourceSegmentIndex:
                -1);
    }

    /// <summary>
    /// Evaluates guidance from the same progress and matched segment used by
    /// the currently published AR route window.
    /// </summary>
    public TurnGuidanceSnapshot Evaluate(
        RouteResult route,
        double progressMeters,
        int sourceSegmentIndex)
    {
        ArgumentNullException.ThrowIfNull(
            route);

        if (route.Points.Count <
            2)
        {
            return TurnGuidanceSnapshot.Unavailable;
        }

        double routeEnd =
            route.Points[^1]
                .DistanceFromStartMeters;

        double progress =
            Math.Clamp(
                progressMeters,
                route.Points[0]
                    .DistanceFromStartMeters,
                routeEnd);

        if (sourceSegmentIndex >=
                0 &&
            sourceSegmentIndex +
                1 <
                route.Points.Count)
        {
            progress =
                Math.Clamp(
                    progress,
                    route.Points[sourceSegmentIndex]
                        .DistanceFromStartMeters,
                    route.Points[sourceSegmentIndex + 1]
                        .DistanceFromStartMeters);
        }

        double remaining =
            Math.Max(
                0.0,
                route.TotalDistanceMeters -
                    progress);

        double remainingGeometry =
            Math.Max(
                0.0,
                routeEnd -
                    progress);

        if (remainingGeometry <=
            ArrivalDistanceMeters)
        {
            return new TurnGuidanceSnapshot(
                true,
                TurnInstruction.Arrive,
                remainingGeometry,
                0.0,
                remaining,
                "Destination ahead");
        }

        List<double> distances =
            new();

        List<GeoCoordinate> samples =
            new();

        double firstDistance =
            Math.Min(
                routeEnd,
                progress +
                    1.0);

        distances.Add(
            firstDistance);

        samples.Add(
            GetCoordinateAtDistance(
                route,
                firstDistance));

        for (double offset = SampleSpacingMeters;
             offset <= LookAheadMeters + SampleSpacingMeters;
             offset += SampleSpacingMeters)
        {
            double distance =
                Math.Min(
                    routeEnd,
                    progress +
                        offset);

            if (distance <=
                distances[^1] +
                    0.25)
            {
                break;
            }

            distances.Add(
                distance);

            samples.Add(
                GetCoordinateAtDistance(
                    route,
                    distance));

            if (distance >=
                routeEnd -
                    0.01)
            {
                break;
            }
        }

        if (samples.Count <
            3)
        {
            return new TurnGuidanceSnapshot(
                true,
                TurnInstruction.Continue,
                double.NaN,
                0.0,
                remaining,
                "Continue straight");
        }

        double baselineBearing =
            BearingDegrees(
                samples[0],
                samples[1]);

        for (int i = 1;
             i < samples.Count - 1;
             i++)
        {
            double futureBearing =
                BearingDegrees(
                    samples[i],
                    samples[i + 1]);

            double signedDelta =
                NormalizeSignedDegrees(
                    futureBearing -
                    baselineBearing);

            double absoluteDelta =
                Math.Abs(
                    signedDelta);

            if (absoluteDelta <
                MinimumTurnAngleDegrees)
            {
                continue;
            }

            TurnInstruction instruction =
                ClassifyTurn(
                    signedDelta);

            double distanceToTurn =
                Math.Max(
                    0.0,
                    distances[i] -
                        progress);

            return new TurnGuidanceSnapshot(
                true,
                instruction,
                distanceToTurn,
                signedDelta,
                remaining,
                ToDisplayText(
                    instruction));
        }

        return new TurnGuidanceSnapshot(
            true,
            TurnInstruction.Continue,
            double.NaN,
            0.0,
            remaining,
            "Continue straight");
    }

    /// <summary>
    /// Evaluates the cyan route that is actually published to AR. Sampling
    /// intentionally mirrors the geographic classifier above so both systems
    /// use the same spacing, threshold, sign convention, and turn labels.
    ///
    /// AR horizontal azimuth uses 0 degrees = +Z and +90 degrees = +X,
    /// matching geographic north/east before map-to-AR rotation. A yaw
    /// rotation therefore cannot reverse left and right.
    /// </summary>
    public VisibleTurnGuidanceSnapshot EvaluateVisibleRoute(
        IReadOnlyList<ArHorizontalRoutePoint> points)
    {
        ArgumentNullException.ThrowIfNull(
            points);

        if (points.Count <
            2)
        {
            return VisibleTurnGuidanceSnapshot.Unavailable;
        }

        double routeStart =
            points[0]
                .DistanceFromWindowStartMeters;

        double routeEnd =
            points[^1]
                .DistanceFromWindowStartMeters;

        if (!double.IsFinite(
                routeStart) ||
            !double.IsFinite(
                routeEnd) ||
            routeEnd -
                routeStart <
                    0.50)
        {
            return VisibleTurnGuidanceSnapshot.Unavailable;
        }

        double horizonMeters =
            Math.Max(
                0.0,
                routeEnd -
                    routeStart);

        List<double> distances =
            new();

        List<ArSamplePoint> samples =
            new();

        double firstDistance =
            Math.Min(
                routeEnd,
                routeStart +
                    1.0);

        distances.Add(
            firstDistance);

        samples.Add(
            GetArPointAtDistance(
                points,
                firstDistance));

        for (double offset = SampleSpacingMeters;
             offset <= horizonMeters + SampleSpacingMeters;
             offset += SampleSpacingMeters)
        {
            double distance =
                Math.Min(
                    routeEnd,
                    routeStart +
                        offset);

            if (distance <=
                distances[^1] +
                    0.25)
            {
                break;
            }

            distances.Add(
                distance);

            samples.Add(
                GetArPointAtDistance(
                    points,
                    distance));

            if (distance >=
                routeEnd -
                    0.01)
            {
                break;
            }
        }

        if (samples.Count <
            3)
        {
            return new VisibleTurnGuidanceSnapshot(
                true,
                TurnInstruction.Continue,
                double.NaN,
                0.0,
                horizonMeters);
        }

        double baselineBearing =
            ArBearingDegrees(
                samples[0],
                samples[1]);

        if (!double.IsFinite(
                baselineBearing))
        {
            return VisibleTurnGuidanceSnapshot.Unavailable;
        }

        for (int i = 1;
             i < samples.Count - 1;
             i++)
        {
            double futureBearing =
                ArBearingDegrees(
                    samples[i],
                    samples[i + 1]);

            if (!double.IsFinite(
                    futureBearing))
            {
                continue;
            }

            double signedDelta =
                NormalizeSignedDegrees(
                    futureBearing -
                        baselineBearing);

            if (Math.Abs(
                    signedDelta) <
                MinimumTurnAngleDegrees)
            {
                continue;
            }

            return new VisibleTurnGuidanceSnapshot(
                true,
                ClassifyTurn(
                    signedDelta),
                Math.Max(
                    0.0,
                    distances[i] -
                        routeStart),
                signedDelta,
                horizonMeters);
        }

        return new VisibleTurnGuidanceSnapshot(
            true,
            TurnInstruction.Continue,
            double.NaN,
            0.0,
            horizonMeters);
    }

    private static ArSamplePoint GetArPointAtDistance(
        IReadOnlyList<ArHorizontalRoutePoint> points,
        double distanceMeters)
    {
        if (distanceMeters <=
            points[0]
                .DistanceFromWindowStartMeters)
        {
            return new ArSamplePoint(
                points[0].X,
                points[0].Z);
        }

        for (int i = 0;
             i < points.Count - 1;
             i++)
        {
            ArHorizontalRoutePoint start =
                points[i];

            ArHorizontalRoutePoint end =
                points[i + 1];

            if (distanceMeters >
                end.DistanceFromWindowStartMeters)
            {
                continue;
            }

            double span =
                end.DistanceFromWindowStartMeters -
                    start.DistanceFromWindowStartMeters;

            if (span <=
                0.001)
            {
                return new ArSamplePoint(
                    end.X,
                    end.Z);
            }

            double t =
                Math.Clamp(
                    (distanceMeters -
                     start.DistanceFromWindowStartMeters) /
                        span,
                    0.0,
                    1.0);

            return new ArSamplePoint(
                Lerp(
                    start.X,
                    end.X,
                    t),
                Lerp(
                    start.Z,
                    end.Z,
                    t));
        }

        return new ArSamplePoint(
            points[^1].X,
            points[^1].Z);
    }

    private static double ArBearingDegrees(
        ArSamplePoint from,
        ArSamplePoint to)
    {
        return Normalize360Degrees(
            RadiansToDegrees(
                Math.Atan2(
                    to.X -
                        from.X,
                    to.Z -
                        from.Z)));
    }

    private static TurnInstruction ClassifyTurn(
        double signedDeltaDegrees)
    {
        double magnitude =
            Math.Abs(
                signedDeltaDegrees);

        if (magnitude >=
            165.0)
        {
            return TurnInstruction.UTurn;
        }

        bool right =
            signedDeltaDegrees >
                0.0;

        if (magnitude <
            50.0)
        {
            return right
                ? TurnInstruction.SlightRight
                : TurnInstruction.SlightLeft;
        }

        if (magnitude <
            120.0)
        {
            return right
                ? TurnInstruction.Right
                : TurnInstruction.Left;
        }

        return right
            ? TurnInstruction.SharpRight
            : TurnInstruction.SharpLeft;
    }

    private static string ToDisplayText(
        TurnInstruction instruction)
    {
        return instruction switch
        {
            TurnInstruction.FollowRoute =>
                "Follow the cyan route",

            TurnInstruction.SlightLeft =>
                "Slight left",

            TurnInstruction.Left =>
                "Turn left",

            TurnInstruction.SharpLeft =>
                "Sharp left",

            TurnInstruction.SlightRight =>
                "Slight right",

            TurnInstruction.Right =>
                "Turn right",

            TurnInstruction.SharpRight =>
                "Sharp right",

            TurnInstruction.UTurn =>
                "Make a U-turn",

            TurnInstruction.Arrive =>
                "Destination ahead",

            _ =>
                "Continue straight"
        };
    }

    private static GeoCoordinate GetCoordinateAtDistance(
        RouteResult route,
        double distanceMeters)
    {
        IReadOnlyList<RoutePoint> points =
            route.Points;

        if (distanceMeters <=
            points[0]
                .DistanceFromStartMeters)
        {
            return points[0]
                .Coordinate;
        }

        for (int i = 0;
             i < points.Count - 1;
             i++)
        {
            RoutePoint start =
                points[i];

            RoutePoint end =
                points[i + 1];

            if (distanceMeters >
                end.DistanceFromStartMeters)
            {
                continue;
            }

            double span =
                end.DistanceFromStartMeters -
                start.DistanceFromStartMeters;

            if (span <=
                0.001)
            {
                return end.Coordinate;
            }

            double t =
                Math.Clamp(
                    (distanceMeters -
                     start.DistanceFromStartMeters) /
                        span,
                    0.0,
                    1.0);

            return new GeoCoordinate(
                Lerp(
                    start.Coordinate.Latitude,
                    end.Coordinate.Latitude,
                    t),
                LerpLongitude(
                    start.Coordinate.Longitude,
                    end.Coordinate.Longitude,
                    t));
        }

        return points[^1]
            .Coordinate;
    }

    private static double BearingDegrees(
        GeoCoordinate from,
        GeoCoordinate to)
    {
        double latitude1 =
            DegreesToRadians(
                from.Latitude);

        double latitude2 =
            DegreesToRadians(
                to.Latitude);

        double longitudeDelta =
            DegreesToRadians(
                NormalizeLongitudeDelta(
                    to.Longitude -
                    from.Longitude));

        double y =
            Math.Sin(
                longitudeDelta) *
            Math.Cos(
                latitude2);

        double x =
            Math.Cos(
                latitude1) *
            Math.Sin(
                latitude2) -
            Math.Sin(
                latitude1) *
            Math.Cos(
                latitude2) *
            Math.Cos(
                longitudeDelta);

        return Normalize360Degrees(
            RadiansToDegrees(
                Math.Atan2(
                    y,
                    x)));
    }

    private static double Lerp(
        double a,
        double b,
        double t)
    {
        return a +
            (b - a) *
            t;
    }

    private static double LerpLongitude(
        double longitude1,
        double longitude2,
        double t)
    {
        double delta =
            NormalizeLongitudeDelta(
                longitude2 -
                longitude1);

        double longitude =
            longitude1 +
            delta *
            t;

        if (longitude >
            180.0)
        {
            longitude -=
                360.0;
        }
        else if (longitude <
            -180.0)
        {
            longitude +=
                360.0;
        }

        return longitude;
    }

    private static double NormalizeLongitudeDelta(
        double longitudeDelta)
    {
        if (longitudeDelta >
            180.0)
        {
            return longitudeDelta -
                360.0;
        }

        if (longitudeDelta <
            -180.0)
        {
            return longitudeDelta +
                360.0;
        }

        return longitudeDelta;
    }

    private static double Normalize360Degrees(
        double degrees)
    {
        double normalized =
            degrees %
            360.0;

        if (normalized <
            0.0)
        {
            normalized +=
                360.0;
        }

        return normalized;
    }

    private static double NormalizeSignedDegrees(
        double degrees)
    {
        double normalized =
            Normalize360Degrees(
                degrees);

        if (normalized >
            180.0)
        {
            normalized -=
                360.0;
        }

        return normalized;
    }

    private static double DegreesToRadians(
        double degrees)
    {
        return degrees *
            Math.PI /
            180.0;
    }

    private static double RadiansToDegrees(
        double radians)
    {
        return radians *
            180.0 /
            Math.PI;
    }

    public enum TurnInstruction
    {
        Continue,
        SlightLeft,
        Left,
        SharpLeft,
        SlightRight,
        Right,
        SharpRight,
        UTurn,
        Arrive,
        FollowRoute
    }

    public readonly record struct VisibleTurnGuidanceSnapshot(
        bool IsAvailable,
        TurnInstruction Instruction,
        double DistanceToTurnMeters,
        double TurnAngleDegrees,
        double VisibleHorizonMeters)
    {
        public static VisibleTurnGuidanceSnapshot Unavailable =>
            new(
                false,
                TurnInstruction.Continue,
                double.NaN,
                0.0,
                double.NaN);
    }

    private readonly record struct ArSamplePoint(
        double X,
        double Z);

    public readonly record struct TurnGuidanceSnapshot(
        bool IsAvailable,
        TurnInstruction Instruction,
        double DistanceToTurnMeters,
        double TurnAngleDegrees,
        double RemainingRouteMeters,
        string DisplayText)
    {
        public static TurnGuidanceSnapshot Unavailable =>
            new(
                false,
                TurnInstruction.Continue,
                double.NaN,
                0.0,
                0.0,
                string.Empty);
    }
}
