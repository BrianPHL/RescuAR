using RescuAR.Navigation.Models;
using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Revalidates map-to-AR yaw from sustained physical movement. It requires
/// agreement between GPS displacement, the matched route tangent, optional GPS
/// course, and ARCore displacement before proposing a bounded correction.
/// </summary>
public sealed class HeadingRevalidationPolicy
{
    private const double MaximumGpsAccuracyMeters =
        25.0;

    private const double MinimumReportedSpeedMetersPerSecond =
        0.75;

    private const double MinimumDerivedSpeedMetersPerSecond =
        0.60;

    private const double MinimumGpsDisplacementMeters =
        4.0;

    private const double MinimumArDisplacementMeters =
        3.0;

    private const double MaximumSignalAgreementErrorDegrees =
        35.0;

    private const double MaximumCandidateDeviationDegrees =
        12.0;

    private const double AlignmentStableErrorDegrees =
        8.0;

    private const double SevereAlignmentErrorDegrees =
        45.0;

    private const double MaximumOrdinaryCorrectionDegrees =
        8.0;

    private const double MaximumSevereCorrectionDegrees =
        20.0;

    private const int RequiredConsistentCandidates =
        3;

    private static readonly TimeSpan MinimumMovementWindow =
        TimeSpan.FromSeconds(
            2.0);

    private static readonly TimeSpan MaximumMovementWindow =
        TimeSpan.FromSeconds(
            30.0);

    private static readonly TimeSpan CorrectionCooldown =
        TimeSpan.FromSeconds(
            8.0);

    private MovementBaseline? baseline;

    private double candidateSinSum;

    private double candidateCosSum;

    private int candidateCount;

    private DateTimeOffset? lastCorrectionUtc;

    public void Reset()
    {
        baseline =
            null;

        lastCorrectionUtc =
            null;

        ResetCandidateEvidence();
    }

    public void MarkCorrectionApplied(
        DateTimeOffset timestampUtc)
    {
        lastCorrectionUtc =
            timestampUtc;
    }

    public HeadingRevalidationDecision Evaluate(
        double currentMapToArYawDegrees,
        GeoCoordinate gpsCoordinate,
        double? gpsAccuracyMeters,
        double? gpsSpeedMetersPerSecond,
        double? gpsCourseDegrees,
        RouteMatchConfidence matchConfidence,
        double routeTangentBearingDegrees,
        float arPositionX,
        float arPositionZ,
        DateTimeOffset timestampUtc)
    {
        bool trustworthyInput =
            double.IsFinite(
                currentMapToArYawDegrees) &&
            gpsCoordinate.IsValid &&
            gpsAccuracyMeters.HasValue &&
            double.IsFinite(
                gpsAccuracyMeters.Value) &&
            gpsAccuracyMeters.Value <=
                MaximumGpsAccuracyMeters &&
            matchConfidence >=
                RouteMatchConfidence.Medium &&
            double.IsFinite(
                routeTangentBearingDegrees) &&
            float.IsFinite(
                arPositionX) &&
            float.IsFinite(
                arPositionZ);

        if (!trustworthyInput)
        {
            ResetCandidateEvidence();

            return CreateDecision(
                HeadingRevalidationDisposition.Unavailable,
                currentMapToArYawDegrees,
                reason:
                    "heading revalidation requires accurate GPS, a trustworthy route match, and tracked AR position");
        }

        MovementBaseline current =
            new(
                gpsCoordinate,
                arPositionX,
                arPositionZ,
                timestampUtc);

        if (!baseline.HasValue)
        {
            baseline =
                current;

            return CreateDecision(
                HeadingRevalidationDisposition.CollectingMovement,
                currentMapToArYawDegrees,
                reason:
                    "movement baseline captured");
        }

        TimeSpan elapsed =
            timestampUtc -
            baseline.Value.TimestampUtc;

        if (elapsed <
            TimeSpan.Zero ||
            elapsed >
                MaximumMovementWindow)
        {
            baseline =
                current;

            ResetCandidateEvidence();

            return CreateDecision(
                HeadingRevalidationDisposition.CollectingMovement,
                currentMapToArYawDegrees,
                reason:
                    "movement baseline refreshed because its time window expired");
        }

        double gpsDisplacementMeters =
            baseline.Value.GpsCoordinate.DistanceTo(
                gpsCoordinate);

        double arDeltaX =
            arPositionX -
            baseline.Value.ArPositionX;

        double arDeltaZ =
            arPositionZ -
            baseline.Value.ArPositionZ;

        double arDisplacementMeters =
            Math.Sqrt(
                arDeltaX *
                    arDeltaX +
                arDeltaZ *
                    arDeltaZ);

        if (elapsed <
                MinimumMovementWindow ||
            gpsDisplacementMeters <
                MinimumGpsDisplacementMeters ||
            arDisplacementMeters <
                MinimumArDisplacementMeters)
        {
            return CreateDecision(
                HeadingRevalidationDisposition.CollectingMovement,
                currentMapToArYawDegrees,
                gpsDisplacementMeters,
                arDisplacementMeters,
                reason:
                    "waiting for a long enough GPS and ARCore movement baseline");
        }

        double derivedSpeedMetersPerSecond =
            gpsDisplacementMeters /
            Math.Max(
                0.001,
                elapsed.TotalSeconds);

        bool reportedSpeedUsable =
            gpsSpeedMetersPerSecond.HasValue &&
            double.IsFinite(
                gpsSpeedMetersPerSecond.Value) &&
            gpsSpeedMetersPerSecond.Value >=
                MinimumReportedSpeedMetersPerSecond;

        if (!reportedSpeedUsable &&
            derivedSpeedMetersPerSecond <
                MinimumDerivedSpeedMetersPerSecond)
        {
            baseline =
                current;

            ResetCandidateEvidence();

            return CreateDecision(
                HeadingRevalidationDisposition.SignalsDisagree,
                currentMapToArYawDegrees,
                gpsDisplacementMeters,
                arDisplacementMeters,
                reason:
                    "movement was too slow for a reliable walking heading");
        }

        double gpsDisplacementBearingDegrees =
            CalculateInitialBearingDegrees(
                baseline.Value.GpsCoordinate,
                gpsCoordinate);

        double arMovementAzimuthDegrees =
            Normalize360Degrees(
                RadiansToDegrees(
                    Math.Atan2(
                        arDeltaX,
                        arDeltaZ)));

        bool gpsCourseUsable =
            gpsCourseDegrees.HasValue &&
            double.IsFinite(
                gpsCourseDegrees.Value) &&
            (reportedSpeedUsable ||
             derivedSpeedMetersPerSecond >=
                MinimumReportedSpeedMetersPerSecond);

        double displacementRouteErrorDegrees =
            AngularErrorDegrees(
                gpsDisplacementBearingDegrees,
                routeTangentBearingDegrees);

        double courseDisplacementErrorDegrees =
            gpsCourseUsable
                ? AngularErrorDegrees(
                    gpsCourseDegrees!.Value,
                    gpsDisplacementBearingDegrees)
                : double.NaN;

        double courseRouteErrorDegrees =
            gpsCourseUsable
                ? AngularErrorDegrees(
                    gpsCourseDegrees!.Value,
                    routeTangentBearingDegrees)
                : double.NaN;

        baseline =
            current;

        if (displacementRouteErrorDegrees >
                MaximumSignalAgreementErrorDegrees ||
            (gpsCourseUsable &&
             (courseDisplacementErrorDegrees >
                    MaximumSignalAgreementErrorDegrees ||
              courseRouteErrorDegrees >
                    MaximumSignalAgreementErrorDegrees)))
        {
            ResetCandidateEvidence();

            return CreateDecision(
                HeadingRevalidationDisposition.SignalsDisagree,
                currentMapToArYawDegrees,
                gpsDisplacementMeters,
                arDisplacementMeters,
                gpsDisplacementBearingDegrees,
                gpsCourseUsable
                    ? gpsCourseDegrees!.Value
                    : double.NaN,
                routeTangentBearingDegrees,
                arMovementAzimuthDegrees,
                reason:
                    "GPS movement, GPS course, and matched route direction do not agree");
        }

        List<double> geographicSignals =
            new()
            {
                gpsDisplacementBearingDegrees,
                routeTangentBearingDegrees
            };

        if (gpsCourseUsable)
        {
            geographicSignals.Add(
                gpsCourseDegrees!.Value);
        }

        double geographicMovementBearingDegrees =
            CircularMeanDegrees(
                geographicSignals);

        double candidateMapToArYawDegrees =
            NormalizeSignedDegrees(
                arMovementAzimuthDegrees -
                geographicMovementBearingDegrees);

        double currentAlignmentErrorDegrees =
            NormalizeSignedDegrees(
                candidateMapToArYawDegrees -
                currentMapToArYawDegrees);

        if (Math.Abs(
                currentAlignmentErrorDegrees) <=
            AlignmentStableErrorDegrees)
        {
            ResetCandidateEvidence();

            return CreateDecision(
                HeadingRevalidationDisposition.AlignmentStable,
                currentMapToArYawDegrees,
                gpsDisplacementMeters,
                arDisplacementMeters,
                gpsDisplacementBearingDegrees,
                gpsCourseUsable
                    ? gpsCourseDegrees!.Value
                    : double.NaN,
                routeTangentBearingDegrees,
                arMovementAzimuthDegrees,
                candidateMapToArYawDegrees,
                currentAlignmentErrorDegrees,
                reason:
                    "movement-derived alignment agrees with the active yaw");
        }

        double existingCandidateMean =
            candidateCount >
                0
                ? NormalizeSignedDegrees(
                    RadiansToDegrees(
                        Math.Atan2(
                            candidateSinSum,
                            candidateCosSum)))
                : candidateMapToArYawDegrees;

        if (candidateCount >
                0 &&
            AngularErrorDegrees(
                existingCandidateMean,
                candidateMapToArYawDegrees) >
                MaximumCandidateDeviationDegrees)
        {
            ResetCandidateEvidence();
        }

        double candidateRadians =
            candidateMapToArYawDegrees *
            Math.PI /
            180.0;

        candidateSinSum +=
            Math.Sin(
                candidateRadians);

        candidateCosSum +=
            Math.Cos(
                candidateRadians);

        candidateCount++;

        double confirmedCandidateYawDegrees =
            NormalizeSignedDegrees(
                RadiansToDegrees(
                    Math.Atan2(
                        candidateSinSum,
                        candidateCosSum)));

        double confirmedErrorDegrees =
            NormalizeSignedDegrees(
                confirmedCandidateYawDegrees -
                currentMapToArYawDegrees);

        if (candidateCount <
            RequiredConsistentCandidates)
        {
            return CreateDecision(
                HeadingRevalidationDisposition.AwaitingConfirmation,
                currentMapToArYawDegrees,
                gpsDisplacementMeters,
                arDisplacementMeters,
                gpsDisplacementBearingDegrees,
                gpsCourseUsable
                    ? gpsCourseDegrees!.Value
                    : double.NaN,
                routeTangentBearingDegrees,
                arMovementAzimuthDegrees,
                confirmedCandidateYawDegrees,
                confirmedErrorDegrees,
                candidateCount,
                reason:
                    "movement-derived yaw requires three consistent observations");
        }

        if (lastCorrectionUtc.HasValue &&
            timestampUtc -
                lastCorrectionUtc.Value <
                CorrectionCooldown)
        {
            return CreateDecision(
                HeadingRevalidationDisposition.CorrectionCooldown,
                currentMapToArYawDegrees,
                gpsDisplacementMeters,
                arDisplacementMeters,
                gpsDisplacementBearingDegrees,
                gpsCourseUsable
                    ? gpsCourseDegrees!.Value
                    : double.NaN,
                routeTangentBearingDegrees,
                arMovementAzimuthDegrees,
                confirmedCandidateYawDegrees,
                confirmedErrorDegrees,
                candidateCount,
                reason:
                    "heading correction cooldown is active");
        }

        double maximumCorrectionDegrees =
            Math.Abs(
                confirmedErrorDegrees) >=
                SevereAlignmentErrorDegrees
                ? MaximumSevereCorrectionDegrees
                : MaximumOrdinaryCorrectionDegrees;

        double appliedCorrectionDegrees =
            Math.Clamp(
                confirmedErrorDegrees,
                -maximumCorrectionDegrees,
                maximumCorrectionDegrees);

        double correctedYawDegrees =
            NormalizeSignedDegrees(
                currentMapToArYawDegrees +
                appliedCorrectionDegrees);

        int confirmations =
            candidateCount;

        ResetCandidateEvidence();

        return CreateDecision(
            HeadingRevalidationDisposition.CorrectionReady,
            currentMapToArYawDegrees,
            gpsDisplacementMeters,
            arDisplacementMeters,
            gpsDisplacementBearingDegrees,
            gpsCourseUsable
                ? gpsCourseDegrees!.Value
                : double.NaN,
            routeTangentBearingDegrees,
            arMovementAzimuthDegrees,
            confirmedCandidateYawDegrees,
            confirmedErrorDegrees,
            confirmations,
            correctedYawDegrees,
            appliedCorrectionDegrees,
            "three movement windows confirmed a bounded heading correction");
    }

    private void ResetCandidateEvidence()
    {
        candidateSinSum =
            0.0;

        candidateCosSum =
            0.0;

        candidateCount =
            0;
    }

    private static HeadingRevalidationDecision CreateDecision(
        HeadingRevalidationDisposition disposition,
        double currentYawDegrees,
        double gpsDisplacementMeters = double.NaN,
        double arDisplacementMeters = double.NaN,
        double gpsDisplacementBearingDegrees = double.NaN,
        double gpsCourseDegrees = double.NaN,
        double routeTangentBearingDegrees = double.NaN,
        double arMovementAzimuthDegrees = double.NaN,
        double candidateYawDegrees = double.NaN,
        double alignmentErrorDegrees = double.NaN,
        int confirmationCount = 0,
        double correctedYawDegrees = double.NaN,
        double appliedCorrectionDegrees = 0.0,
        string reason = "")
    {
        return new HeadingRevalidationDecision(
            disposition,
            currentYawDegrees,
            gpsDisplacementMeters,
            arDisplacementMeters,
            gpsDisplacementBearingDegrees,
            gpsCourseDegrees,
            routeTangentBearingDegrees,
            arMovementAzimuthDegrees,
            candidateYawDegrees,
            alignmentErrorDegrees,
            confirmationCount,
            RequiredConsistentCandidates,
            correctedYawDegrees,
            appliedCorrectionDegrees,
            reason);
    }

    private static double CircularMeanDegrees(
        IReadOnlyList<double> values)
    {
        double sinSum =
            0.0;

        double cosSum =
            0.0;

        for (int index = 0;
             index < values.Count;
             index++)
        {
            double radians =
                values[index] *
                Math.PI /
                180.0;

            sinSum +=
                Math.Sin(
                    radians);

            cosSum +=
                Math.Cos(
                    radians);
        }

        return Normalize360Degrees(
            RadiansToDegrees(
                Math.Atan2(
                    sinSum,
                    cosSum)));
    }

    private static double CalculateInitialBearingDegrees(
        GeoCoordinate from,
        GeoCoordinate to)
    {
        double fromLatitudeRadians =
            DegreesToRadians(
                from.Latitude);

        double toLatitudeRadians =
            DegreesToRadians(
                to.Latitude);

        double longitudeDeltaRadians =
            DegreesToRadians(
                to.Longitude -
                from.Longitude);

        double y =
            Math.Sin(
                longitudeDeltaRadians) *
            Math.Cos(
                toLatitudeRadians);

        double x =
            Math.Cos(
                fromLatitudeRadians) *
            Math.Sin(
                toLatitudeRadians) -
            Math.Sin(
                fromLatitudeRadians) *
            Math.Cos(
                toLatitudeRadians) *
            Math.Cos(
                longitudeDeltaRadians);

        return Normalize360Degrees(
            RadiansToDegrees(
                Math.Atan2(
                    y,
                    x)));
    }

    private static double AngularErrorDegrees(
        double firstDegrees,
        double secondDegrees)
    {
        return Math.Abs(
            NormalizeSignedDegrees(
                firstDegrees -
                secondDegrees));
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

    private static double Normalize360Degrees(
        double degrees)
    {
        double normalized =
            degrees %
            360.0;

        return normalized <
            0.0
            ? normalized +
                360.0
            : normalized;
    }

    private static double NormalizeSignedDegrees(
        double degrees)
    {
        double normalized =
            Normalize360Degrees(
                degrees);

        return normalized >
            180.0
            ? normalized -
                360.0
            : normalized;
    }

    private readonly record struct MovementBaseline(
        GeoCoordinate GpsCoordinate,
        float ArPositionX,
        float ArPositionZ,
        DateTimeOffset TimestampUtc);
}

public enum HeadingRevalidationDisposition
{
    Unavailable = 0,
    CollectingMovement = 1,
    SignalsDisagree = 2,
    AlignmentStable = 3,
    AwaitingConfirmation = 4,
    CorrectionCooldown = 5,
    CorrectionReady = 6
}

public readonly record struct HeadingRevalidationDecision(
    HeadingRevalidationDisposition Disposition,
    double CurrentYawDegrees,
    double GpsDisplacementMeters,
    double ArDisplacementMeters,
    double GpsDisplacementBearingDegrees,
    double GpsCourseDegrees,
    double RouteTangentBearingDegrees,
    double ArMovementAzimuthDegrees,
    double CandidateYawDegrees,
    double AlignmentErrorDegrees,
    int ConfirmationCount,
    int RequiredConfirmationCount,
    double CorrectedYawDegrees,
    double AppliedCorrectionDegrees,
    string Reason)
{
    public bool ShouldApplyCorrection =>
        Disposition ==
            HeadingRevalidationDisposition.CorrectionReady;
}
