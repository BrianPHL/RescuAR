using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Detects sustained backward walking inside the existing route corridor.
/// Uses raw GPS matches and physical displacement, independent of monotonic
/// GPS/PDR progress and the direction in which the phone is pointing.
/// </summary>
public sealed class RouteBacktrackingPolicy
{
    private static readonly TimeSpan EvidenceLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaximumObservationGap = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinimumMovementTime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MinimumSampleSpacing = TimeSpan.FromSeconds(2);
    private const int RequiredConfirmations = 2;
    private RouteResult? currentRoute;
    private Observation? anchor;
    private DateTimeOffset? lastObservationAt;
    private DateTimeOffset? lastCountedAt;
    private DateTimeOffset? nextReplanAt;
    private double lastCountedRetreat;
    private int confirmations;

    public void Reset()
    {
        currentRoute = null;
        lastObservationAt = null;
        nextReplanAt = null;
        ResetEvidence();
    }

    public void ResetEvidence()
    {
        anchor = null;
        lastCountedAt = null;
        lastCountedRetreat = 0;
        confirmations = 0;
    }

    public void MarkRerouteCompleted(DateTimeOffset timestamp)
    {
        ResetEvidence();
        nextReplanAt = timestamp.AddSeconds(20);
    }

    public void MarkRerouteFailed(DateTimeOffset timestamp, bool awaitingConfirmation)
    {
        ResetEvidence();
        nextReplanAt = timestamp.AddSeconds(awaitingConfirmation ? 3 : 10);
    }

    public BacktrackingDecision Evaluate(RouteResult route,
        RouteProgressTracker.RouteProgressUpdate update, DateTimeOffset observedAt,
        DateTimeOffset evaluatedAt, bool nearDestination = false)
    {
        if (!ReferenceEquals(route, currentRoute))
        {
            ResetEvidence();
            lastObservationAt = null;
            currentRoute = route;
        }
        if (observedAt > evaluatedAt.AddSeconds(5) ||
            evaluatedAt - observedAt > MaximumObservationGap)
            return Decision("stale GPS observation ignored");
        if (lastObservationAt.HasValue && observedAt <= lastObservationAt.Value)
            return Decision("duplicate or older GPS observation ignored");
        if (lastObservationAt.HasValue && observedAt - lastObservationAt.Value > MaximumObservationGap)
            ResetEvidence();
        lastObservationAt = observedAt;

        if (nearDestination)
        {
            ResetEvidence();
            return Decision("near destination; arrival takes precedence");
        }
        // A poor fix or a competing branch must not establish a reverse walk.
        // A single-corridor match legitimately has a positive infinite score gap.
        if (!update.IsAccepted || update.IsOffRoute ||
            update.MatchConfidence < RouteMatchConfidence.Medium ||
            !(update.IndependentCorridorScoreGap >= 2) ||
            update.AccuracyMeters is not double accuracy || !double.IsFinite(accuracy) ||
            accuracy < 0 || accuracy > 20 || !update.GpsCoordinate.IsValid ||
            !double.IsFinite(update.RawProgressMeters) || update.RawProgressMeters < 0)
        {
            ResetEvidence();
            return Decision("waiting for an accurate unambiguous route match");
        }
        if (nextReplanAt.HasValue && observedAt < nextReplanAt.Value)
        {
            ResetEvidence();
            return Decision("backtracking replan cooldown active");
        }

        var observation = new Observation(update.GpsCoordinate, update.RawProgressMeters,
            accuracy, observedAt);
        if (anchor is not Observation baseline || observedAt - baseline.At > EvidenceLifetime ||
            update.RawProgressMeters > baseline.Progress + 2)
        {
            ResetEvidence();
            anchor = observation;
            return Decision("collecting walking displacement");
        }

        double moved = baseline.Coordinate.DistanceTo(update.GpsCoordinate);
        double retreat = baseline.Progress - update.RawProgressMeters;
        // At the start of a route, projection clamps to zero. Measure movement
        // against the first geographic segment to also catch walking away there.
        if (baseline.Progress <= 2 && update.RawProgressMeters <= 2 && route.Points.Count >= 2)
            retreat = Math.Max(retreat, ReverseDistanceFromStart(route, baseline.Coordinate,
                update.GpsCoordinate));
        double requiredDistance = Math.Max(12, baseline.Accuracy + accuracy);
        if (moved < requiredDistance || retreat < requiredDistance ||
            observedAt - baseline.At < MinimumMovementTime)
        {
            lastCountedAt = null;
            confirmations = 0;
            return Decision("movement has not exceeded GPS uncertainty", moved, retreat);
        }
        if (confirmations > 0 && retreat < lastCountedRetreat - 2)
        {
            lastCountedAt = null;
            confirmations = 0;
        }
        if (lastCountedAt.HasValue && (observedAt - lastCountedAt.Value < MinimumSampleSpacing ||
            retreat < lastCountedRetreat + 2))
            return Decision("waiting for continued backward movement", moved, retreat, true);

        lastCountedAt = observedAt;
        lastCountedRetreat = retreat;
        confirmations++;
        bool shouldReplan = confirmations >= RequiredConfirmations;
        var decision = Decision(shouldReplan
            ? "sustained backward walking confirmed; replan from current GPS position"
            : "confirming backward walking", moved, retreat, true, shouldReplan);
        if (shouldReplan)
        {
            nextReplanAt = observedAt.AddSeconds(20);
            ResetEvidence();
        }
        return decision;
    }

    private BacktrackingDecision Decision(string reason, double moved = 0, double retreat = 0,
        bool candidate = false, bool replan = false) =>
        new(candidate, replan, confirmations, RequiredConfirmations, moved, retreat, reason);

    private static double ReverseDistanceFromStart(RouteResult route,
        GeoCoordinate from, GeoCoordinate to)
    {
        double cosLatitude = Math.Cos(from.Latitude * Math.PI / 180);
        double east = (to.Longitude - from.Longitude) * cosLatitude;
        double north = to.Latitude - from.Latitude;
        for (int i = 1; i < route.Points.Count; i++)
        {
            GeoCoordinate start = route.Points[i - 1].Coordinate;
            GeoCoordinate end = route.Points[i].Coordinate;
            double segmentEast = (end.Longitude - start.Longitude) * cosLatitude;
            double segmentNorth = end.Latitude - start.Latitude;
            double length = Math.Sqrt(segmentEast * segmentEast + segmentNorth * segmentNorth);
            if (length > 0.000001)
                return -(east * segmentEast + north * segmentNorth) / length * 111_195;
        }
        return 0;
    }

    private readonly record struct Observation(GeoCoordinate Coordinate, double Progress,
        double Accuracy, DateTimeOffset At);

    public readonly record struct BacktrackingDecision(bool IsCandidate, bool ShouldReplan,
        int ConfirmationCount, int RequiredConfirmationCount, double DisplacementMeters,
        double RetreatMeters, string Reason);
}
