using System;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Shared pedestrian route-corridor and map-match confidence policy.
///
/// The routing line is a navigational centerline, not a surveyed sidewalk.
/// Corridor width therefore expands with reported GPS uncertainty while
/// remaining bounded for safe AR guidance and rerouting decisions.
/// </summary>
public static class RouteCorridorPolicy
{
    public const double MinimumCorridorRadiusMeters =
        15.0;

    public const double MaximumCorridorRadiusMeters =
        35.0;

    public const double MaximumRecoveryConnectorMeters =
        50.0;

    private const double BaseGeometryAllowanceMeters =
        10.0;

    private const double AccuracyRadiusWeight =
        0.75;

    private const double UnknownAccuracyFallbackMeters =
        20.0;

    private const double MaximumAccuracyForMediumConfidenceMeters =
        30.0;

    private const double MaximumAccuracyForRecoveryConnectorMeters =
        20.0;

    private const double MinimumSpeedForCourseMetersPerSecond =
        0.75;

    private const double MinimumScoreGapForMediumConfidence =
        2.0;

    private const double MinimumScoreGapForHighConfidence =
        4.0;

    public static double GetCorridorRadiusMeters(
        double? accuracyMeters,
        bool indoorTestMode)
    {
        if (indoorTestMode)
        {
            return 100.0;
        }

        double usableAccuracy =
            accuracyMeters.HasValue &&
            double.IsFinite(
                accuracyMeters.Value) &&
            accuracyMeters.Value >=
                0.0
                ? accuracyMeters.Value
                : UnknownAccuracyFallbackMeters;

        double requestedRadius =
            BaseGeometryAllowanceMeters +
            usableAccuracy *
                AccuracyRadiusWeight;

        return Math.Clamp(
            requestedRadius,
            MinimumCorridorRadiusMeters,
            MaximumCorridorRadiusMeters);
    }

    public static double GetRoadFollowingEntryRadiusMeters(
        double corridorRadiusMeters)
    {
        if (!double.IsFinite(
                corridorRadiusMeters) ||
            corridorRadiusMeters <=
                0.0)
        {
            return MinimumCorridorRadiusMeters;
        }

        return Math.Min(
            20.0,
            corridorRadiusMeters *
                0.70);
    }

    public static bool IsCourseUsable(
        double? courseDegrees,
        double? speedMetersPerSecond)
    {
        return courseDegrees.HasValue &&
            double.IsFinite(
                courseDegrees.Value) &&
            speedMetersPerSecond.HasValue &&
            double.IsFinite(
                speedMetersPerSecond.Value) &&
            speedMetersPerSecond.Value >=
                MinimumSpeedForCourseMetersPerSecond;
    }

    public static RouteMatchConfidence ClassifyMatch(
        double? accuracyMeters,
        double courseAlignmentErrorDegrees,
        double matchScoreGap)
    {
        if (!accuracyMeters.HasValue ||
            !double.IsFinite(
                accuracyMeters.Value) ||
            accuracyMeters.Value <
                0.0)
        {
            return RouteMatchConfidence.Low;
        }

        bool directionSupportsMatch =
            !double.IsFinite(
                courseAlignmentErrorDegrees) ||
            courseAlignmentErrorDegrees <=
                90.0;

        if (!directionSupportsMatch ||
            matchScoreGap <
                MinimumScoreGapForMediumConfidence)
        {
            return RouteMatchConfidence.Low;
        }

        bool highDirectionAgreement =
            !double.IsFinite(
                courseAlignmentErrorDegrees) ||
            courseAlignmentErrorDegrees <=
                60.0;

        if (accuracyMeters.Value <=
                15.0 &&
            highDirectionAgreement &&
            matchScoreGap >=
                MinimumScoreGapForHighConfidence)
        {
            return RouteMatchConfidence.High;
        }

        if (accuracyMeters.Value <=
            MaximumAccuracyForMediumConfidenceMeters)
        {
            return RouteMatchConfidence.Medium;
        }

        return RouteMatchConfidence.Low;
    }

    public static bool CanEnterRoadFollowing(
        bool isAccepted,
        bool isOffRoute,
        double crossTrackErrorMeters,
        double corridorRadiusMeters,
        RouteMatchConfidence confidence)
    {
        return isAccepted &&
            !isOffRoute &&
            confidence >=
                RouteMatchConfidence.Medium &&
            double.IsFinite(
                crossTrackErrorMeters) &&
            crossTrackErrorMeters <=
                GetRoadFollowingEntryRadiusMeters(
                    corridorRadiusMeters);
    }

    public static bool CanPublishRecoveryConnector(
        double? accuracyMeters,
        double crossTrackErrorMeters,
        double corridorRadiusMeters,
        RouteMatchConfidence confidence)
    {
        return accuracyMeters.HasValue &&
            double.IsFinite(
                accuracyMeters.Value) &&
            accuracyMeters.Value <=
                MaximumAccuracyForRecoveryConnectorMeters &&
            confidence >=
                RouteMatchConfidence.Medium &&
            double.IsFinite(
                crossTrackErrorMeters) &&
            crossTrackErrorMeters >
                corridorRadiusMeters &&
            crossTrackErrorMeters <=
                MaximumRecoveryConnectorMeters;
    }
}

public enum RouteMatchConfidence
{
    Unavailable = 0,
    Low = 1,
    Medium = 2,
    High = 3
}
