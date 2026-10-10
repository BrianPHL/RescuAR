using System;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Conservative repeated-GPS confirmation policy for dynamic rerouting.
///
/// RouteProgressTracker remains responsible for geometric route matching.
/// This policy only decides when several trustworthy off-route matches are
/// strong enough to justify another route request.
/// </summary>
public sealed class OffRouteReroutePolicy
{
    private const double MaximumAccuracyForRerouteMeters =
        30.0;

    private const int RequiredConsecutiveOffRouteSamples =
        3;

    private static readonly TimeSpan RerouteCooldown =
        TimeSpan.FromSeconds(
            20);

    private static readonly TimeSpan FailedAttemptCooldown =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ConfirmationRetryCooldown =
        TimeSpan.FromSeconds(3);

    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinimumObservationSpacing = TimeSpan.FromSeconds(1);

    private int consecutiveOffRouteSamples;

    private DateTimeOffset? nextRerouteAllowedUtc;
    private DateTimeOffset? lastObservationUtc;
    private DateTimeOffset? lastCountedObservationUtc;
    private int failedAttempts;

    public void Reset()
    {
        consecutiveOffRouteSamples =
            0;

        nextRerouteAllowedUtc =
            null;
        lastObservationUtc = null;
        lastCountedObservationUtc = null;
        failedAttempts = 0;
    }

    public void ResetConfirmation()
    {
        consecutiveOffRouteSamples =
            0;
        lastCountedObservationUtc = null;
    }

    public void MarkRerouteCompleted(
        DateTimeOffset timestampUtc)
    {
        ResetConfirmation();
        failedAttempts = 0;

        nextRerouteAllowedUtc = timestampUtc + RerouteCooldown;
    }

    public void MarkRerouteFailed(DateTimeOffset timestampUtc,
        bool awaitingConfirmation = false)
    {
        ResetConfirmation();
        if (!awaitingConfirmation) failedAttempts = Math.Min(4, failedAttempts + 1);
        nextRerouteAllowedUtc = timestampUtc + (awaitingConfirmation
            ? ConfirmationRetryCooldown
            : TimeSpan.FromSeconds(Math.Min(60,
                FailedAttemptCooldown.TotalSeconds * Math.Pow(2, failedAttempts - 1))));
    }

    public OffRouteDecision Evaluate(
        RouteProgressTracker.RouteProgressUpdate update,
        DateTimeOffset timestampUtc)
    {
        return EvaluateCore(
            update.IsOffRoute && (!double.IsFinite(update.NearestRouteDistanceMeters) ||
                update.NearestRouteDistanceMeters > update.CorridorRadiusMeters),
            update.CrossTrackErrorMeters,
            update.AccuracyMeters,
            update.MatchConfidence,
            timestampUtc,
            clearlyOutsideWholeRoute: double.IsFinite(update.NearestRouteDistanceMeters) &&
                update.AccuracyMeters is double accuracy && double.IsFinite(accuracy) &&
                update.NearestRouteDistanceMeters > update.CorridorRadiusMeters + accuracy + 5.0);
    }

#if RESCUAR_DIAGNOSTICS
    /// <summary>
    /// Developer-validation hook used only by the temporary CameraPage test
    /// harness. It deliberately bypasses geometric route matching while still
    /// exercising the SAME confirmation/cooldown policy used by real GPS.
    ///
    /// It does not alter the production accuracy-aware route corridor.
    /// The caller should use the device's real GPS coordinate as the reroute
    /// origin when the third simulated confirmation triggers.
    /// </summary>
    public OffRouteDecision EvaluateDeveloperSimulation(
        double simulatedCrossTrackErrorMeters,
        double simulatedAccuracyMeters,
        DateTimeOffset timestampUtc)
    {
        if (!double.IsFinite(simulatedCrossTrackErrorMeters) ||
            simulatedCrossTrackErrorMeters <
                0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulatedCrossTrackErrorMeters));
        }

        if (!double.IsFinite(simulatedAccuracyMeters) ||
            simulatedAccuracyMeters <
                0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(simulatedAccuracyMeters));
        }

        return EvaluateCore(
            isOffRoute: true,
            crossTrackErrorMeters: simulatedCrossTrackErrorMeters,
            accuracyMeters: simulatedAccuracyMeters,
            matchConfidence: RouteMatchConfidence.High,
            timestampUtc);
    }
#endif

    private OffRouteDecision EvaluateCore(
        bool isOffRoute,
        double crossTrackErrorMeters,
        double? accuracyMeters,
        RouteMatchConfidence matchConfidence,
        DateTimeOffset timestampUtc,
        bool clearlyOutsideWholeRoute = false)
    {
        bool accuracyUsable =
            accuracyMeters.HasValue &&
            double.IsFinite(
                accuracyMeters.Value) &&
            accuracyMeters.Value >= 0.0 &&
            accuracyMeters.Value <=
                MaximumAccuracyForRerouteMeters;

        bool matchTrustworthy =
            matchConfidence >=
                RouteMatchConfidence.Medium || clearlyOutsideWholeRoute;

        if (lastObservationUtc.HasValue && timestampUtc <= lastObservationUtc.Value)
            return new(false, false, consecutiveOffRouteSamples,
                RequiredConsecutiveOffRouteSamples, crossTrackErrorMeters,
                accuracyMeters, matchConfidence, "duplicate or older GPS observation ignored");
        lastObservationUtc = timestampUtc;
        if (lastCountedObservationUtc.HasValue &&
            timestampUtc - lastCountedObservationUtc.Value > ConfirmationWindow)
            ResetConfirmation();

        if (!isOffRoute ||
            !accuracyUsable ||
            !matchTrustworthy || !double.IsFinite(crossTrackErrorMeters) ||
            crossTrackErrorMeters < 0)
        {
            // A brief accuracy/identity outage supplies no contrary evidence.
            // Only a trustworthy on-route fix or expiry clears the sequence.
            if (!isOffRoute && accuracyUsable && matchTrustworthy)
                ResetConfirmation();

            return new OffRouteDecision(
                false,
                false,
                consecutiveOffRouteSamples,
                RequiredConsecutiveOffRouteSamples,
                crossTrackErrorMeters,
                accuracyMeters,
                matchConfidence,
                isOffRoute
                    ? !accuracyUsable
                        ? "off-route match ignored because GPS accuracy is too weak"
                        : "off-route match ignored because segment identity is ambiguous"
                    : "GPS match is not off-route");
        }

        if (nextRerouteAllowedUtc.HasValue &&
            timestampUtc < nextRerouteAllowedUtc.Value)
        {
            ResetConfirmation();

            double remainingSeconds = Math.Max(0.0,
                (nextRerouteAllowedUtc.Value - timestampUtc).TotalSeconds);

            return new OffRouteDecision(
                true,
                false,
                0,
                RequiredConsecutiveOffRouteSamples,
                crossTrackErrorMeters,
                accuracyMeters,
                matchConfidence,
                $"reroute cooldown active for another {remainingSeconds:F0} s");
        }

        if (lastCountedObservationUtc.HasValue &&
            timestampUtc - lastCountedObservationUtc.Value < MinimumObservationSpacing)
            return new(true, false, consecutiveOffRouteSamples,
                RequiredConsecutiveOffRouteSamples, crossTrackErrorMeters,
                accuracyMeters, matchConfidence, "waiting for an independent GPS observation");

        lastCountedObservationUtc = timestampUtc;
        consecutiveOffRouteSamples++;

        bool shouldReroute =
            consecutiveOffRouteSamples >=
                RequiredConsecutiveOffRouteSamples;

        int confirmationCount =
            consecutiveOffRouteSamples;

        if (shouldReroute)
        {
            /*
             * Mark the trigger time immediately so another GPS poll cannot
             * start a duplicate reroute while the network request is active.
             */
            nextRerouteAllowedUtc = timestampUtc + FailedAttemptCooldown;

            ResetConfirmation();
        }

        string reason =
            shouldReroute
                ? "repeated trustworthy off-route GPS matches confirmed"
                : "waiting for repeated off-route confirmation";

        return new OffRouteDecision(
            true,
            shouldReroute,
            confirmationCount,
            RequiredConsecutiveOffRouteSamples,
            crossTrackErrorMeters,
            accuracyMeters,
            matchConfidence,
            reason);
    }

    public readonly record struct OffRouteDecision(
        bool IsCandidate,
        bool ShouldReroute,
        int ConfirmationCount,
        int RequiredConfirmationCount,
        double CrossTrackErrorMeters,
        double? AccuracyMeters,
        RouteMatchConfidence MatchConfidence,
        string Reason);
}
