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
     * Maximum amount one accepted GPS poll may move progress FORWARD.
     * Large map-matched jumps are therefore approached over multiple polls.
     */
    private const double HighMaximumForwardCorrectionMeters =
        15.0;

    private const double MediumMaximumForwardCorrectionMeters =
        7.0;

    private const double LowMaximumForwardCorrectionMeters =
        3.0;

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
     * <=60° : low confidence, 65% stride
     * >60°  : reject
     */
    private const double PdrHighHeadingErrorDegrees =
        25.0;

    private const double PdrMediumHeadingErrorDegrees =
        45.0;

    private const double PdrMaximumHeadingErrorDegrees =
        60.0;

    private int highConfidenceBackwardConfirmations;

    private double? lastBackwardCandidateProgressMeters;

    public void Reset()
    {
        highConfidenceBackwardConfirmations =
            0;

        lastBackwardCandidateProgressMeters =
            null;
    }

    public GpsFusionDecision EvaluateGps(
        double previousCommittedProgressMeters,
        RouteProgressTracker.RouteProgressUpdate gpsUpdate)
    {
        if (!gpsUpdate.IsAccepted ||
            gpsUpdate.IsOffRoute)
        {
            ResetBackwardConfirmation();

            return new GpsFusionDecision(
                GpsConfidence.Unavailable,
                GpsFusionAction.Ignore,
                previousCommittedProgressMeters,
                0.0,
                0,
                "GPS sample was not accepted for route progress.");
        }

        if (gpsUpdate.MatchConfidence <
            RouteMatchConfidence.Medium)
        {
            ResetBackwardConfirmation();

            return new GpsFusionDecision(
                GpsConfidence.Low,
                GpsFusionAction.Ignore,
                previousCommittedProgressMeters,
                gpsUpdate.RawProgressMeters -
                    previousCommittedProgressMeters,
                0,
                "GPS segment identity is ambiguous; route progress is held.");
        }

        GpsConfidence confidence =
            ClassifyGps(
                gpsUpdate.AccuracyMeters,
                gpsUpdate.CrossTrackErrorMeters);

        double rawProgress =
            gpsUpdate.RawProgressMeters;

        double divergence =
            rawProgress -
            previousCommittedProgressMeters;

        if (!double.IsFinite(
                divergence))
        {
            ResetBackwardConfirmation();

            return new GpsFusionDecision(
                GpsConfidence.Unavailable,
                GpsFusionAction.Ignore,
                previousCommittedProgressMeters,
                0.0,
                0,
                "GPS/PDR progress divergence is invalid.");
        }

        if (divergence >=
            0.0)
        {
            ResetBackwardConfirmation();

            double maximumAdvance =
                confidence switch
                {
                    GpsConfidence.High =>
                        HighMaximumForwardCorrectionMeters,

                    GpsConfidence.Medium =>
                        MediumMaximumForwardCorrectionMeters,

                    _ =>
                        LowMaximumForwardCorrectionMeters
                };

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
                limited
                    ? $"GPS forward correction limited to {maximumAdvance:F1} m."
                    : "GPS forward progress accepted.");
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
                $"GPS is only {gpsBehindMeters:F1} m behind PDR; keeping PDR progress.");
        }

        if (confidence !=
            GpsConfidence.High)
        {
            ResetBackwardConfirmation();

            return new GpsFusionDecision(
                confidence,
                GpsFusionAction.KeepPdrLead,
                previousCommittedProgressMeters,
                divergence,
                0,
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
                "Confirmed GPS disagreement falls inside the retained PDR lead cushion.");
        }

        return new GpsFusionDecision(
            confidence,
            GpsFusionAction.CorrectBackward,
            correctedTarget,
            divergence,
            highConfidenceBackwardConfirmations,
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
                0.65,
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

    private void ResetBackwardConfirmation()
    {
        highConfidenceBackwardConfirmations =
            0;

        lastBackwardCandidateProgressMeters =
            null;
    }

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
        string Reason);

    public readonly record struct PdrConfidenceDecision(
        PdrConfidence Confidence,
        double StrideScale,
        bool IsAccepted,
        string Reason);
}
