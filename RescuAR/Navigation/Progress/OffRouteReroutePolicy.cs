using System;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Conservative repeated-GPS confirmation policy for dynamic rerouting.
///
/// RouteProgressTracker remains responsible for geometric route matching.
/// This policy only decides when several trustworthy off-route matches are
/// strong enough to justify another Railway MLD request.
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

    private int consecutiveOffRouteSamples;

    private DateTimeOffset? lastRerouteUtc;

    public void Reset()
    {
        consecutiveOffRouteSamples =
            0;

        lastRerouteUtc =
            null;
    }

    public void ResetConfirmation()
    {
        consecutiveOffRouteSamples =
            0;
    }

    public void MarkRerouteCompleted(
        DateTimeOffset timestampUtc)
    {
        consecutiveOffRouteSamples =
            0;

        lastRerouteUtc =
            timestampUtc;
    }

    public OffRouteDecision Evaluate(
        RouteProgressTracker.RouteProgressUpdate update,
        DateTimeOffset timestampUtc)
    {
        return EvaluateCore(
            update.IsOffRoute,
            update.CrossTrackErrorMeters,
            update.AccuracyMeters,
            timestampUtc,
            developerSimulation: false);
    }

    /// <summary>
    /// Developer-validation hook used only by the temporary CameraPage test
    /// harness. It deliberately bypasses geometric route matching while still
    /// exercising the SAME confirmation/cooldown policy used by real GPS.
    ///
    /// It does not alter the production 35 m RouteProgressTracker threshold.
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
            timestampUtc,
            developerSimulation: true);
    }

    private OffRouteDecision EvaluateCore(
        bool isOffRoute,
        double crossTrackErrorMeters,
        double? accuracyMeters,
        DateTimeOffset timestampUtc,
        bool developerSimulation)
    {
        bool accuracyUsable =
            accuracyMeters.HasValue &&
            double.IsFinite(
                accuracyMeters.Value) &&
            accuracyMeters.Value <=
                MaximumAccuracyForRerouteMeters;

        if (!isOffRoute ||
            !accuracyUsable)
        {
            consecutiveOffRouteSamples =
                0;

            return new OffRouteDecision(
                false,
                false,
                0,
                RequiredConsecutiveOffRouteSamples,
                crossTrackErrorMeters,
                accuracyMeters,
                isOffRoute
                    ? "off-route match ignored because GPS accuracy is too weak"
                    : "GPS match is not off-route");
        }

        if (lastRerouteUtc.HasValue &&
            timestampUtc -
                lastRerouteUtc.Value <
                RerouteCooldown)
        {
            consecutiveOffRouteSamples =
                0;

            double remainingSeconds =
                Math.Max(
                    0.0,
                    (RerouteCooldown -
                     (timestampUtc -
                      lastRerouteUtc.Value))
                        .TotalSeconds);

            return new OffRouteDecision(
                true,
                false,
                0,
                RequiredConsecutiveOffRouteSamples,
                crossTrackErrorMeters,
                accuracyMeters,
                $"reroute cooldown active for another {remainingSeconds:F0} s");
        }

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
            lastRerouteUtc =
                timestampUtc;

            consecutiveOffRouteSamples =
                0;
        }

        return new OffRouteDecision(
            true,
            shouldReroute,
            confirmationCount,
            RequiredConsecutiveOffRouteSamples,
            crossTrackErrorMeters,
            accuracyMeters,
            shouldReroute
                ? developerSimulation
                    ? "DEVELOPER SIMULATION: repeated trustworthy off-route confirmations injected"
                    : "repeated trustworthy off-route GPS matches confirmed"
                : developerSimulation
                    ? "DEVELOPER SIMULATION: waiting for repeated off-route confirmation"
                    : "waiting for repeated off-route confirmation");
    }

    public readonly record struct OffRouteDecision(
        bool IsCandidate,
        bool ShouldReroute,
        int ConfirmationCount,
        int RequiredConfirmationCount,
        double CrossTrackErrorMeters,
        double? AccuracyMeters,
        string Reason);
}
