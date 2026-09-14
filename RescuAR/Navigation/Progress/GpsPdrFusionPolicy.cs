using System;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Conservative GPS/PDR confidence policy for pedestrian route progress.
///
/// This class does not know about AR rendering and does not estimate a free
/// 2D inertial position. It only decides how strongly a route-matched GPS fix
/// should influence the already-monotonic along-route progress state.
///
/// Design goals:
/// - good GPS can correct/anchor PDR;
/// - one GPS fix cannot cause a large route jump;
/// - poor GPS updates reliability without moving progress;
/// - ambiguous matches suspend GPS and PDR corrections until repeated fixes
///   identify the same route area;
/// - ordinary GPS jitter cannot erase reasonable PDR progress;
/// - repeated HIGH-confidence GPS disagreement can slowly correct PDR drift;
/// - the policy remains small enough for the capstone schedule.
/// </summary>
public sealed class GpsPdrFusionPolicy
{
    private const double HighAccuracyMeters =
        15.0;

    private const double HighCrossTrackMeters =
        10.0;

    private const double MediumAccuracyMeters =
        30.0;

    private const double MediumCrossTrackMeters =
        20.0;

    /*
     * Maximum amount a perfect-reliability GPS poll may move progress FORWARD.
     * Actual correction is multiplied by the accuracy/cross-track/course/match
     * reliability weight, so large jumps are approached over multiple polls.
     */
    private const double HighMaximumForwardCorrectionMeters =
        15.0;

    private const double MinimumGpsReliabilityForProgress =
        0.35;

    private const double MinimumGpsReliabilityForBackwardCorrection =
        0.75;

    private const double BestExpectedAccuracyMeters =
        5.0;

    private const double MaximumWeightedAccuracyMeters =
        50.0;

    private const double BestExpectedCrossTrackMeters =
        3.0;

    private const double MaximumWeightedCrossTrackMeters =
        35.0;

    private const double BestCourseAlignmentErrorDegrees =
        30.0;

    private const double MaximumWeightedCourseErrorDegrees =
        100.0;

    private const double UnknownCourseReliabilityWeight =
        0.80;

    /*
     * Do not treat small GPS-behind-PDR disagreement as drift. This cushion
     * lets several normal walking steps remain ahead of a lagging GPS fix.
     */
    private const double BackwardCorrectionDeadbandMeters =
        5.0;

    /*
     * A backward correction requires three consecutive HIGH-confidence
     * contradictions. This prevents one multipath/noisy fix from undoing PDR.
     */
    private const int RequiredHighConfidenceBackwardConfirmations =
        3;

    private const double BackwardCandidateAgreementMeters =
        5.0;

    /*
     * Even once confirmed, correct slowly. At the normal ~2 s GPS cadence this
     * limits correction to about 0.75 m/s.
     */
    private const double MaximumBackwardCorrectionPerFixMeters =
        1.50;

    /*
     * Keep a small PDR lead cushion instead of forcing progress exactly onto a
     * potentially lagging GPS projection.
     */
    private const double BackwardGpsLeadCushionMeters =
        2.0;

    /*
     * PDR directional-confidence bands.
     *
     * <=25° : high confidence, full 0.70 m stride
     * <=45° : medium confidence, 85% stride
     * <=70° : low confidence, 55% stride
     * >70°  : reject
     */
    private const double PdrHighHeadingErrorDegrees =
        25.0;

    private const double PdrMediumHeadingErrorDegrees =
        45.0;

    private const double PdrMaximumHeadingErrorDegrees =
        70.0;

    private const double LargeGpsPdrDisagreementMeters =
        25.0;

    private const int HighConfidenceIdentityConfirmations =
        2;

    private const int MediumConfidenceIdentityConfirmations =
        3;

    private const int IdentitySegmentTolerance =
        1;

    private const double IdentityProgressAgreementMeters =
        20.0;

    private int highConfidenceBackwardConfirmations;

    private double? lastBackwardCandidateProgressMeters;

    private volatile bool routeIdentitySuspended;

    private int routeIdentityConfirmationCount;

    private int lastRouteIdentityCandidateSegment =
        -1;

    private double? lastRouteIdentityCandidateProgressMeters;

    private bool hasResolvedRouteIdentity;

    private int lastResolvedRouteIdentitySegment =
        -1;

    private double? lastResolvedRouteIdentityProgressMeters;

    public void Reset()
    {
        highConfidenceBackwardConfirmations =
            0;

        lastBackwardCandidateProgressMeters =
            null;

        ResetRouteIdentitySuspension();
    }

    public GpsFusionDecision EvaluateGps(
        double previousCommittedProgressMeters,
        bool hasPreviousProgress,
        RouteProgressTracker.RouteProgressUpdate gpsUpdate)
    {
        if (!gpsUpdate.IsAccepted ||
            gpsUpdate.IsOffRoute)
        {
            ResetBackwardConfirmation();

            if (gpsUpdate.IsOffRoute ||
                gpsUpdate.MatchConfidence ==
                    RouteMatchConfidence.Low)
            {
                SuspendRouteIdentity();
            }
            else if (routeIdentitySuspended)
            {
                ResetRouteIdentityCandidate();
            }

            GpsConfidence observedConfidence =
                ClassifyGps(
                    gpsUpdate.AccuracyMeters,
                    gpsUpdate.CrossTrackErrorMeters);

            double observedReliability =
                CalculateReliabilityWeight(
                    gpsUpdate.AccuracyMeters,
                    gpsUpdate.CrossTrackErrorMeters,
                    gpsUpdate.CourseAlignmentErrorDegrees,
                    gpsUpdate.MatchConfidence);

            double observedDivergence =
                double.IsFinite(
                    gpsUpdate.RawProgressMeters)
                    ? gpsUpdate.RawProgressMeters -
                        previousCommittedProgressMeters
                    : 0.0;

            return new GpsFusionDecision(
                observedConfidence,
                gpsUpdate.IsOffRoute
                    ? GpsFusionAction.HoldAmbiguousMatch
                    : GpsFusionAction.HoldLowReliability,
                previousCommittedProgressMeters,
                observedDivergence,
                0,
                routeIdentityConfirmationCount,
                observedReliability,
                gpsUpdate.IsOffRoute
                    ? "Off-route GPS cannot change progress until route identity is resolved."
                    : "Rejected GPS only updates confidence; route progress is held.");
        }

        if (gpsUpdate.MatchConfidence <
            RouteMatchConfidence.Medium)
        {
            ResetBackwardConfirmation();

            SuspendRouteIdentity();

            return new GpsFusionDecision(
                GpsConfidence.Low,
                GpsFusionAction.HoldAmbiguousMatch,
                previousCommittedProgressMeters,
                gpsUpdate.RawProgressMeters -
                    previousCommittedProgressMeters,
                0,
                0,
                0.0,
                "GPS segment identity is ambiguous; route progress is held.");
        }

        GpsConfidence confidence =
            ClassifyGps(
                gpsUpdate.AccuracyMeters,
                gpsUpdate.CrossTrackErrorMeters);

        double reliabilityWeight =
            CalculateReliabilityWeight(
                gpsUpdate.AccuracyMeters,
                gpsUpdate.CrossTrackErrorMeters,
                gpsUpdate.CourseAlignmentErrorDegrees,
                gpsUpdate.MatchConfidence);

        double rawProgress =
            gpsUpdate.RawProgressMeters;

        double divergence =
            rawProgress -
            previousCommittedProgressMeters;

        if (!double.IsFinite(
                divergence))
        {
            ResetBackwardConfirmation();

            if (routeIdentitySuspended)
            {
                ResetRouteIdentityCandidate();
            }

            return new GpsFusionDecision(
                GpsConfidence.Unavailable,
                GpsFusionAction.Ignore,
                previousCommittedProgressMeters,
                0.0,
                0,
                routeIdentityConfirmationCount,
                0.0,
                "GPS/PDR progress divergence is invalid.");
        }

        if (confidence ==
                GpsConfidence.Low ||
            reliabilityWeight <
                MinimumGpsReliabilityForProgress)
        {
            ResetBackwardConfirmation();

            if (routeIdentitySuspended)
            {
                ResetRouteIdentityCandidate();
            }

            return new GpsFusionDecision(
                confidence,
                GpsFusionAction.HoldLowReliability,
                previousCommittedProgressMeters,
                divergence,
                0,
                routeIdentityConfirmationCount,
                reliabilityWeight,
                "GPS reliability is too low to change route progress.");
        }

        bool largeAmbiguousDisagreement =
            hasPreviousProgress &&
            Math.Abs(divergence) >=
                LargeGpsPdrDisagreementMeters &&
            gpsUpdate.MatchConfidence <
                RouteMatchConfidence.High;

        bool agreesWithResolvedIdentity =
            DoesMatchResolvedRouteIdentity(
                gpsUpdate);

        if (largeAmbiguousDisagreement &&
            !agreesWithResolvedIdentity &&
            !routeIdentitySuspended)
        {
            SuspendRouteIdentity();
        }

        if (routeIdentitySuspended &&
            !TryConfirmRouteIdentity(
                gpsUpdate))
        {
            ResetBackwardConfirmation();

            return new GpsFusionDecision(
                confidence,
                GpsFusionAction.AwaitRouteIdentity,
                previousCommittedProgressMeters,
                divergence,
                0,
                routeIdentityConfirmationCount,
                reliabilityWeight,
                "Route identity recovery requires repeated nearby GPS matches.");
        }

        RememberResolvedRouteIdentity(
            gpsUpdate);

        if (divergence >=
            0.0)
        {
            ResetBackwardConfirmation();

            double maximumAdvance =
                HighMaximumForwardCorrectionMeters *
                reliabilityWeight;

            double target =
                previousCommittedProgressMeters +
                Math.Min(
                    divergence,
                    maximumAdvance);

            bool limited =
                divergence >
                maximumAdvance +
                    0.001;

            return new GpsFusionDecision(
                confidence,
                limited
                    ? GpsFusionAction.LimitForwardJump
                    : GpsFusionAction.AcceptForward,
                target,
                divergence,
                0,
                0,
                reliabilityWeight,
                limited
                    ? $"Reliability-weighted GPS forward correction limited to {maximumAdvance:F1} m."
                    : "Reliability-weighted GPS forward progress accepted.");
        }

        double gpsBehindMeters =
            -divergence;

        if (gpsBehindMeters <=
            BackwardCorrectionDeadbandMeters)
        {
            ResetBackwardConfirmation();

            return new GpsFusionDecision(
                confidence,
                GpsFusionAction.KeepPdrLead,
                previousCommittedProgressMeters,
                divergence,
                0,
                0,
                reliabilityWeight,
                $"GPS is only {gpsBehindMeters:F1} m behind PDR; keeping PDR progress.");
        }

        if (confidence !=
                GpsConfidence.High ||
            reliabilityWeight <
                MinimumGpsReliabilityForBackwardCorrection)
        {
            ResetBackwardConfirmation();

            return new GpsFusionDecision(
                confidence,
                GpsFusionAction.KeepPdrLead,
                previousCommittedProgressMeters,
                divergence,
                0,
                0,
                reliabilityWeight,
                "GPS is behind PDR but is not HIGH confidence; no backward correction.");
        }

        bool agreesWithPreviousCandidate =
            lastBackwardCandidateProgressMeters.HasValue &&
            Math.Abs(
                lastBackwardCandidateProgressMeters.Value -
                rawProgress) <=
                    BackwardCandidateAgreementMeters;

        highConfidenceBackwardConfirmations =
            agreesWithPreviousCandidate
                ? highConfidenceBackwardConfirmations +
                    1
                : 1;

        lastBackwardCandidateProgressMeters =
            rawProgress;

        if (highConfidenceBackwardConfirmations <
            RequiredHighConfidenceBackwardConfirmations)
        {
            return new GpsFusionDecision(
                confidence,
                GpsFusionAction.WaitForBackwardConfirmation,
                previousCommittedProgressMeters,
                divergence,
                highConfidenceBackwardConfirmations,
                0,
                reliabilityWeight,
                "HIGH-confidence GPS is behind PDR, but repeated confirmation is required.");
        }

        double gpsCushionedTarget =
            rawProgress +
            BackwardGpsLeadCushionMeters;

        double maximumStepTarget =
            previousCommittedProgressMeters -
            MaximumBackwardCorrectionPerFixMeters;

        double correctedTarget =
            Math.Max(
                gpsCushionedTarget,
                maximumStepTarget);

        correctedTarget =
            Math.Max(
                0.0,
                correctedTarget);

        if (correctedTarget >=
            previousCommittedProgressMeters -
                0.001)
        {
            return new GpsFusionDecision(
                confidence,
                GpsFusionAction.KeepPdrLead,
                previousCommittedProgressMeters,
                divergence,
                highConfidenceBackwardConfirmations,
                0,
                reliabilityWeight,
                "Confirmed GPS disagreement falls inside the retained PDR lead cushion.");
        }

        return new GpsFusionDecision(
            confidence,
            GpsFusionAction.CorrectBackward,
            correctedTarget,
            divergence,
            highConfidenceBackwardConfirmations,
            0,
            reliabilityWeight,
            $"Applying bounded backward correction of " +
            $"{previousCommittedProgressMeters - correctedTarget:F1} m.");
    }

    public PdrConfidenceDecision EvaluatePdrHeading(
        double headingErrorDegrees)
    {
        if (!double.IsFinite(
                headingErrorDegrees) ||
            headingErrorDegrees <
                0.0)
        {
            return new PdrConfidenceDecision(
                PdrConfidence.Rejected,
                0.0,
                false,
                "invalid direction error");
        }

        if (headingErrorDegrees <=
            PdrHighHeadingErrorDegrees)
        {
            return new PdrConfidenceDecision(
                PdrConfidence.High,
                1.00,
                true,
                "strong route-direction agreement");
        }

        if (headingErrorDegrees <=
            PdrMediumHeadingErrorDegrees)
        {
            return new PdrConfidenceDecision(
                PdrConfidence.Medium,
                0.85,
                true,
                "moderate route-direction agreement");
        }

        if (headingErrorDegrees <=
            PdrMaximumHeadingErrorDegrees)
        {
            return new PdrConfidenceDecision(
                PdrConfidence.Low,
                0.55,
                true,
                "weak but plausible route-direction agreement");
        }

        return new PdrConfidenceDecision(
            PdrConfidence.Rejected,
            0.0,
            false,
            $"direction error exceeds {PdrMaximumHeadingErrorDegrees:F0} degrees");
    }

    private static GpsConfidence ClassifyGps(
        double? accuracyMeters,
        double crossTrackErrorMeters)
    {
        double accuracy =
            accuracyMeters.HasValue &&
            double.IsFinite(
                accuracyMeters.Value)
                ? accuracyMeters.Value
                : double.PositiveInfinity;

        double crossTrack =
            double.IsFinite(
                crossTrackErrorMeters)
                ? Math.Max(
                    0.0,
                    crossTrackErrorMeters)
                : double.PositiveInfinity;

        if (accuracy <=
                HighAccuracyMeters &&
            crossTrack <=
                HighCrossTrackMeters)
        {
            return GpsConfidence.High;
        }

        if (accuracy <=
                MediumAccuracyMeters &&
            crossTrack <=
                MediumCrossTrackMeters)
        {
            return GpsConfidence.Medium;
        }

        return GpsConfidence.Low;
    }

    private static double CalculateReliabilityWeight(
        double? accuracyMeters,
        double crossTrackErrorMeters,
        double courseAlignmentErrorDegrees,
        RouteMatchConfidence matchConfidence)
    {
        if (!accuracyMeters.HasValue ||
            !double.IsFinite(
                accuracyMeters.Value) ||
            accuracyMeters.Value <
                0.0 ||
            !double.IsFinite(
                crossTrackErrorMeters))
        {
            return 0.0;
        }

        double accuracyWeight =
            GetDescendingWeight(
                accuracyMeters.Value,
                BestExpectedAccuracyMeters,
                MaximumWeightedAccuracyMeters);

        double crossTrackWeight =
            GetDescendingWeight(
                Math.Max(
                    0.0,
                    crossTrackErrorMeters),
                BestExpectedCrossTrackMeters,
                MaximumWeightedCrossTrackMeters);

        double matchWeight =
            matchConfidence switch
            {
                RouteMatchConfidence.High =>
                    1.0,

                RouteMatchConfidence.Medium =>
                    0.70,

                _ =>
                    0.0
            };

        double courseWeight =
            double.IsFinite(
                courseAlignmentErrorDegrees)
                ? GetDescendingWeight(
                    Math.Max(
                        0.0,
                        courseAlignmentErrorDegrees),
                    BestCourseAlignmentErrorDegrees,
                    MaximumWeightedCourseErrorDegrees)
                : UnknownCourseReliabilityWeight;

        return Math.Min(
            accuracyWeight,
            Math.Min(
                crossTrackWeight,
                Math.Min(
                    courseWeight,
                    matchWeight)));
    }

    private static double GetDescendingWeight(
        double value,
        double bestValue,
        double worstValue)
    {
        if (value <=
            bestValue)
        {
            return 1.0;
        }

        if (value >=
            worstValue)
        {
            return 0.0;
        }

        return 1.0 -
            (value -
             bestValue) /
            (worstValue -
             bestValue);
    }

    private void SuspendRouteIdentity()
    {
        routeIdentitySuspended =
            true;

        hasResolvedRouteIdentity =
            false;

        lastResolvedRouteIdentitySegment =
            -1;

        lastResolvedRouteIdentityProgressMeters =
            null;

        ResetRouteIdentityCandidate();
    }

    private void ResetRouteIdentityCandidate()
    {
        routeIdentityConfirmationCount =
            0;

        lastRouteIdentityCandidateSegment =
            -1;

        lastRouteIdentityCandidateProgressMeters =
            null;
    }

    private bool TryConfirmRouteIdentity(
        RouteProgressTracker.RouteProgressUpdate gpsUpdate)
    {
        bool agreesWithPreviousCandidate =
            lastRouteIdentityCandidateSegment >=
                0 &&
            Math.Abs(
                gpsUpdate.SegmentIndex -
                lastRouteIdentityCandidateSegment) <=
                    IdentitySegmentTolerance &&
            lastRouteIdentityCandidateProgressMeters.HasValue &&
            Math.Abs(
                gpsUpdate.RawProgressMeters -
                lastRouteIdentityCandidateProgressMeters.Value) <=
                    IdentityProgressAgreementMeters;

        routeIdentityConfirmationCount =
            agreesWithPreviousCandidate
                ? routeIdentityConfirmationCount +
                    1
                : 1;

        lastRouteIdentityCandidateSegment =
            gpsUpdate.SegmentIndex;

        lastRouteIdentityCandidateProgressMeters =
            gpsUpdate.RawProgressMeters;

        int requiredConfirmations =
            gpsUpdate.MatchConfidence ==
                RouteMatchConfidence.High
                ? HighConfidenceIdentityConfirmations
                : MediumConfidenceIdentityConfirmations;

        if (routeIdentityConfirmationCount <
            requiredConfirmations)
        {
            return false;
        }

        routeIdentitySuspended =
            false;

        RememberResolvedRouteIdentity(
            gpsUpdate);

        ResetRouteIdentityCandidate();

        return true;
    }

    private bool DoesMatchResolvedRouteIdentity(
        RouteProgressTracker.RouteProgressUpdate gpsUpdate)
    {
        return hasResolvedRouteIdentity &&
            lastResolvedRouteIdentitySegment >=
                0 &&
            Math.Abs(
                gpsUpdate.SegmentIndex -
                lastResolvedRouteIdentitySegment) <=
                    IdentitySegmentTolerance &&
            lastResolvedRouteIdentityProgressMeters.HasValue &&
            Math.Abs(
                gpsUpdate.RawProgressMeters -
                lastResolvedRouteIdentityProgressMeters.Value) <=
                    IdentityProgressAgreementMeters;
    }

    private void RememberResolvedRouteIdentity(
        RouteProgressTracker.RouteProgressUpdate gpsUpdate)
    {
        hasResolvedRouteIdentity =
            true;

        lastResolvedRouteIdentitySegment =
            gpsUpdate.SegmentIndex;

        lastResolvedRouteIdentityProgressMeters =
            gpsUpdate.RawProgressMeters;
    }

    private void ResetRouteIdentitySuspension()
    {
        routeIdentitySuspended =
            false;

        ResetRouteIdentityCandidate();

        hasResolvedRouteIdentity =
            false;

        lastResolvedRouteIdentitySegment =
            -1;

        lastResolvedRouteIdentityProgressMeters =
            null;
    }

    private void ResetBackwardConfirmation()
    {
        highConfidenceBackwardConfirmations =
            0;

        lastBackwardCandidateProgressMeters =
            null;
    }

    public bool IsRouteIdentitySuspended =>
        routeIdentitySuspended;

    public enum GpsConfidence
    {
        Unavailable,
        Low,
        Medium,
        High
    }

    public enum GpsFusionAction
    {
        Ignore,
        HoldLowReliability,
        HoldAmbiguousMatch,
        AwaitRouteIdentity,
        AcceptForward,
        LimitForwardJump,
        KeepPdrLead,
        WaitForBackwardConfirmation,
        CorrectBackward
    }

    public enum PdrConfidence
    {
        Rejected,
        Low,
        Medium,
        High
    }

    public readonly record struct GpsFusionDecision(
        GpsConfidence Confidence,
        GpsFusionAction Action,
        double TargetProgressMeters,
        double RawGpsMinusPreviousProgressMeters,
        int BackwardConfirmationCount,
        int RouteIdentityConfirmationCount,
        double ReliabilityWeight,
        string Reason);

    public readonly record struct PdrConfidenceDecision(
        PdrConfidence Confidence,
        double StrideScale,
        bool IsAccepted,
        string Reason);
}
