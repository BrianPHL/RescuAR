using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Detects sustained backward walking inside the existing route corridor.
/// Uses raw GPS matches and, when available, independently tracked AR movement.
/// Neither monotonic GPS/PDR progress nor phone pointing counts as walking.
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
    private DateTimeOffset? lastReliableObservationAt;
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
        lastReliableObservationAt = null;
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
        DateTimeOffset evaluatedAt, bool nearDestination = false, TrackedMotion? trackedMotion = null)
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
        if (lastReliableObservationAt.HasValue &&
            observedAt - lastReliableObservationAt.Value > MaximumObservationGap)
            ResetEvidence();

        if (nearDestination)
        {
            ResetEvidence();
            return Decision("near destination; arrival takes precedence");
        }
        // A weak fix contributes no evidence. A brief precision interruption
        // must not repeatedly erase qualified same-street movement, either.
        if (update.AccuracyMeters is not double accuracy || !double.IsFinite(accuracy) ||
            accuracy < 0 || accuracy > 20)
            return Decision("weak GPS held; waiting for an accurate observation");
        // A competing branch or actual departure still invalidates evidence.
        // A single-corridor match legitimately has a positive infinite score gap.
        if (!update.IsAccepted || update.IsOffRoute ||
            update.MatchConfidence < RouteMatchConfidence.Medium ||
            !(update.IndependentCorridorScoreGap >= 2) ||
            !update.GpsCoordinate.IsValid ||
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
            accuracy, observedAt, trackedMotion);
        if (trackedMotion is { IsValid: true } newMotion && anchor is Observation existing &&
            (existing.Motion is not TrackedMotion oldMotion ||
             !oldMotion.IsContinuousWith(newMotion)))
            ResetEvidence();
        double arRetreat = anchor is Observation previous
            ? GetArRetreat(route, previous, update.RawProgressMeters, trackedMotion) : double.NaN;
        if (anchor is not Observation baseline || observedAt - baseline.At > EvidenceLifetime ||
            (update.RawProgressMeters > baseline.Progress + 2 && !(arRetreat >= 3)))
        {
            ResetEvidence();
            anchor = observation;
            lastReliableObservationAt = observedAt;
            return Decision("collecting walking displacement");
        }
        lastReliableObservationAt = observedAt;

        double moved = baseline.Coordinate.DistanceTo(update.GpsCoordinate);
        double retreat = baseline.Progress - update.RawProgressMeters;
        // At the start of a route, projection clamps to zero. Measure movement
        // against the first geographic segment to also catch walking away there.
        if (baseline.Progress <= 2 && update.RawProgressMeters <= 2 && route.Points.Count >= 2)
            retreat = Math.Max(retreat, ReverseDistanceFromStart(route, baseline.Coordinate,
                update.GpsCoordinate));
        if (arRetreat <= -3 && retreat >= 3)
        {
            ResetEvidence();
            anchor = observation;
            lastReliableObservationAt = observedAt;
            return Decision("GPS retreat contradicts forward AR motion", moved, retreat,
                arRetreat: arRetreat);
        }
        bool corroborated = arRetreat >= 12 && retreat >= 3 && moved >= 3;
        double evidenceRetreat = corroborated ? arRetreat : retreat;
        double requiredDistance = Math.Max(12, baseline.Accuracy + accuracy);
        if (baseline.Motion is { } stationaryFrom && trackedMotion is { } stationaryTo &&
            stationaryFrom.IsContinuousWith(stationaryTo) && moved >= requiredDistance &&
            Math.Sqrt(Math.Pow(stationaryTo.X - stationaryFrom.X, 2) +
                Math.Pow(stationaryTo.Z - stationaryFrom.Z, 2)) < 3)
        {
            ResetEvidence();
            anchor = observation;
            lastReliableObservationAt = observedAt;
            return Decision("GPS displacement has no tracked walking corroboration", moved, retreat);
        }
        if ((!corroborated && (moved < requiredDistance || retreat < requiredDistance)) ||
            observedAt - baseline.At < MinimumMovementTime)
        {
            lastCountedAt = null;
            confirmations = 0;
            return Decision("movement has not exceeded GPS uncertainty", moved, retreat,
                arRetreat: arRetreat);
        }
        if (confirmations > 0 && evidenceRetreat < lastCountedRetreat - 2)
        {
            lastCountedAt = null;
            confirmations = 0;
        }
        if (lastCountedAt.HasValue && (observedAt - lastCountedAt.Value < MinimumSampleSpacing ||
            evidenceRetreat < lastCountedRetreat + 2))
            return Decision("waiting for continued backward movement", moved, retreat, true,
                arRetreat: arRetreat, corroborated: corroborated);

        lastCountedAt = observedAt;
        lastCountedRetreat = evidenceRetreat;
        confirmations++;
        bool shouldReplan = confirmations >= RequiredConfirmations;
        var decision = Decision(shouldReplan
            ? "sustained backward walking confirmed; replan from current GPS position"
            : "confirming backward walking", moved, retreat, true, shouldReplan,
            arRetreat, corroborated);
        if (shouldReplan)
        {
            nextReplanAt = observedAt.AddSeconds(20);
            ResetEvidence();
        }
        return decision;
    }

    private BacktrackingDecision Decision(string reason, double moved = 0, double retreat = 0,
        bool candidate = false, bool replan = false, double arRetreat = double.NaN,
        bool corroborated = false) =>
        new(candidate, replan, confirmations, RequiredConfirmations, moved, retreat, reason,
            arRetreat, corroborated ? "GPS_AND_AR" : "GPS");

    private static double GetArRetreat(RouteResult route, Observation baseline,
        double currentProgress, TrackedMotion? current)
    {
        if (baseline.Motion is not TrackedMotion from || current is not TrackedMotion to ||
            !from.IsContinuousWith(to) ||
            !RouteDisplayProgressPolicy.TryAtDistance(route, baseline.Progress, out var position) ||
            !RouteDisplayProgressPolicy.TryAtDistance(route, currentProgress, out var currentPosition))
            return double.NaN;
        var a = route.Points[position.SegmentIndex].Coordinate;
        var b = route.Points[position.SegmentIndex + 1].Coordinate;
        double east = (b.Longitude - a.Longitude) * Math.Cos(a.Latitude * Math.PI / 180);
        double north = b.Latitude - a.Latitude;
        double length = Math.Sqrt(east * east + north * north);
        if (length <= 0.000001) return double.NaN;
        var currentA = route.Points[currentPosition.SegmentIndex].Coordinate;
        var currentB = route.Points[currentPosition.SegmentIndex + 1].Coordinate;
        double currentEast = (currentB.Longitude - currentA.Longitude) *
            Math.Cos(currentA.Latitude * Math.PI / 180);
        double currentNorth = currentB.Latitude - currentA.Latitude;
        double currentLength = Math.Sqrt(currentEast * currentEast + currentNorth * currentNorth);
        // A turn or hairpin invalidates straight-street AR corroboration;
        // retain the ordinary geographic uncertainty checks in that case.
        if (currentLength <= 0.000001 ||
            (east * currentEast + north * currentNorth) / (length * currentLength) <
                Math.Cos(35 * Math.PI / 180)) return double.NaN;
        var direction = MapToArCoordinates.Rotate(east / length, north / length, from.YawDegrees);
        return -((to.X - from.X) * direction.X + (to.Z - from.Z) * direction.Z);
    }

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
        double Accuracy, DateTimeOffset At, TrackedMotion? Motion);

    public readonly record struct TrackedMotion(float X, float Z, double YawDegrees,
        long Session, long GroundReference, long TrackingEpoch = 0, long WorldFrame = 0)
    {
        public bool IsValid => float.IsFinite(X) && float.IsFinite(Z) &&
            double.IsFinite(YawDegrees) && Session > 0 && GroundReference > 0;

        // Floor references share camera-world coordinates. Tracking loss,
        // lifecycle changes and world corrections do not share that guarantee.
        public bool IsContinuousWith(TrackedMotion other) => IsValid && other.IsValid &&
            Session == other.Session && TrackingEpoch == other.TrackingEpoch && WorldFrame == other.WorldFrame &&
            Math.Abs(Math.IEEERemainder(YawDegrees - other.YawDegrees, 360)) <= 8;
    }

    public readonly record struct BacktrackingDecision(bool IsCandidate, bool ShouldReplan,
        int ConfirmationCount, int RequiredConfirmationCount, double DisplacementMeters,
        double RetreatMeters, string Reason, double ArRetreatMeters = double.NaN,
        string EvidenceSource = "GPS");
}
