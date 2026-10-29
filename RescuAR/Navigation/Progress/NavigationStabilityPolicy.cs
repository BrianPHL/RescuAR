using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Progress;

/// <summary>Shared geographic confidence and assessment cadence; never changes an AR transform.</summary>
public sealed record NavigationStabilityOptions
{
    // Existing pedestrian evidence budgets: 20 m qualified GPS, 3 s acquisition
    // pairing, 15 s off-route confirmation, 8 m bounded display advance.
    public TimeSpan AssessmentInterval { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaximumFixAge { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan UncertaintyTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan RecoveryLifetime { get; init; } = TimeSpan.FromSeconds(30);
    public double MaximumReliableAccuracyMeters { get; init; } = 20;
    public double MaximumEstimatedAdvanceMeters { get; init; } = RouteDisplayProgressPolicy.MaximumPdrAdvanceMeters;
    public double MaximumWalkingSpeedMetersPerSecond { get; init; } = 3;

    internal void Validate()
    {
        if (AssessmentInterval <= TimeSpan.Zero || MaximumFixAge <= TimeSpan.Zero ||
            UncertaintyTimeout < MaximumFixAge || RecoveryLifetime <= UncertaintyTimeout ||
            !double.IsFinite(MaximumReliableAccuracyMeters) || MaximumReliableAccuracyMeters <= 0 ||
            !double.IsFinite(MaximumEstimatedAdvanceMeters) || MaximumEstimatedAdvanceMeters <= 0 ||
            !double.IsFinite(MaximumWalkingSpeedMetersPerSecond) || MaximumWalkingSpeedMetersPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(NavigationStabilityOptions));
    }
}

public sealed class NavigationStabilityPolicy
{
    public NavigationStabilityOptions Options { get; }
    private RouteResult? route;
    private DateTimeOffset? lastObservation, lastAssessment, lastReliable, uncertainSince;
    private GeoCoordinate reliableCoordinate;
    private double reliableAccuracy;
    private RouteBacktrackingPolicy.TrackedMotion? reliableMotion;
    private GeoCoordinate recoveryCoordinate;
    private DateTimeOffset? recoveryAt;
    private double recoveryAccuracy;
    private int recoverySamples;
    public Decision Current { get; private set; }
    public readonly record struct Decision(bool Reliable, bool AssessDeviation, bool ProlongedUncertainty,
        double ReliableAgeSeconds, string Reason);

    public NavigationStabilityPolicy(NavigationStabilityOptions? options = null)
    {
        Options = options ?? new();
        Options.Validate();
    }

    public void Reset()
    {
        route = null;
        lastObservation = lastAssessment = lastReliable = uncertainSince = null;
        reliableMotion = null;
        recoveryAt = null;
        recoverySamples = 0;
        Current = default;
    }

    public Decision Observe(RouteResult activeRoute, GeoCoordinate coordinate, double? accuracy,
        DateTimeOffset at, DateTimeOffset now, RouteBacktrackingPolicy.TrackedMotion? motion,
        bool observationHeld)
    {
        if (!ReferenceEquals(route, activeRoute)) { Reset(); route = activeRoute; }
        // A timeout is not a device observation. Advancing this watermark with
        // the poll clock rejects real fixes acquired just before that timeout.
        bool hasObservation = coordinate.IsValid && accuracy is double a && double.IsFinite(a) && a >= 0;
        bool unique = hasObservation && (!lastObservation.HasValue || at > lastObservation);
        bool qualified = unique && !observationHeld && now >= at &&
            now - at <= Options.MaximumFixAge && accuracy <= Options.MaximumReliableAccuracyMeters;
        if (unique) lastObservation = at;
        if (qualified && lastReliable is { } previous && at > previous &&
            at - previous <= Options.RecoveryLifetime)
        {
            double distance = reliableCoordinate.DistanceTo(coordinate);
            double budget = reliableAccuracy + accuracy!.Value + RoutePositionObservationPolicy.MotionAllowanceMeters;
            // Distance comparison is yaw independent: a bad retained yaw must
            // not prevent the independent heading observer from recovering.
            if (reliableMotion is { } from && motion is { } to && from.IsContinuousWith(to))
                qualified = Math.Abs(distance - Math.Sqrt(Math.Pow(to.X - from.X, 2) +
                    Math.Pow(to.Z - from.Z, 2))) <= budget;
            else
                qualified = distance <= Options.MaximumWalkingSpeedMetersPerSecond *
                    (at - previous).TotalSeconds + budget;
        }
        if (qualified && lastReliable.HasValue && at - lastReliable.Value >= Options.UncertaintyTimeout)
        {
            if (!recoveryAt.HasValue || at - recoveryAt.Value > Options.UncertaintyTimeout ||
                recoveryCoordinate.DistanceTo(coordinate) > recoveryAccuracy + accuracy!.Value +
                    Options.MaximumWalkingSpeedMetersPerSecond * (at - recoveryAt.Value).TotalSeconds)
            {
                recoverySamples = 0;
                recoveryAt = null;
            }
            if (!recoveryAt.HasValue || at - recoveryAt.Value >= Options.AssessmentInterval)
            {
                recoverySamples++;
                recoveryAt = at;
                recoveryCoordinate = coordinate;
                recoveryAccuracy = accuracy!.Value;
            }
            qualified = recoverySamples >= 3;
        }
        if (qualified)
        {
            lastReliable = at;
            reliableCoordinate = coordinate;
            reliableAccuracy = accuracy!.Value;
            reliableMotion = motion;
            uncertainSince = null;
            recoveryAt = null;
            recoverySamples = 0;
        }
        else uncertainSince ??= now;
        double age = lastReliable.HasValue ? Math.Max(0, (now - lastReliable.Value).TotalSeconds) : double.PositiveInfinity;
        bool prolonged = now - (lastReliable ?? uncertainSince!.Value) >= Options.UncertaintyTimeout;
        bool due = qualified && (!lastAssessment.HasValue || now - lastAssessment >= Options.AssessmentInterval);
        if (due) lastAssessment = now;
        return Current = new(qualified, due, prolonged, age,
            qualified ? due ? "reliable scheduled assessment" : "reliable position; assessment not due" :
            prolonged ? "position uncertain; bounded route estimate only" : "holding last reliable route position");
    }

    /// <summary>Display-only recovery along the accepted polyline. Never a location, arrival or reroute origin.</summary>
    public bool TryEstimate(RouteResult activeRoute, GeoCoordinate fromCoordinate, double fromProgress,
        DateTimeOffset observedAt, DateTimeOffset now, RouteBacktrackingPolicy.TrackedMotion from,
        RouteBacktrackingPolicy.TrackedMotion to, out RouteDisplayProgressPolicy.DisplayPosition estimate)
    {
        estimate = default;
        var age = now - observedAt;
        if (!ReferenceEquals(route, activeRoute) || age <= TimeSpan.FromSeconds(RouteDisplayProgressPolicy.MaximumPdrGpsAgeSeconds) ||
            age > Options.RecoveryLifetime ||
            !from.IsContinuousWith(to) ||
            !RouteDisplayProgressPolicy.TryAtDistance(activeRoute, fromProgress, out var start) ||
            !RouteDisplayProgressPolicy.TryAtDistance(activeRoute, Math.Min(fromProgress + 1,
                activeRoute.Points[^1].DistanceFromStartMeters), out var next)) return false;
        var tangent = RescuAR.Navigation.Projection.MapToArCoordinates.Rotate(
            (next.RoadCoordinate.Longitude - start.RoadCoordinate.Longitude) * Math.Cos(start.RoadCoordinate.Latitude * Math.PI / 180),
            next.RoadCoordinate.Latitude - start.RoadCoordinate.Latitude, to.YawDegrees);
        double length = Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z);
        double dx = to.X - from.X, dz = to.Z - from.Z;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1e-10 || distance > Options.MaximumEstimatedAdvanceMeters ||
            distance > Options.MaximumWalkingSpeedMetersPerSecond * age.TotalSeconds) return false;
        double forward = (dx * tangent.X + dz * tangent.Z) / length;
        double lateral = Math.Abs(dx * tangent.Z - dz * tangent.X) / length;
        // Between ordinary PDR expiry and prolonged uncertainty, keep the
        // mapped reference fixed. Only after the timeout estimate along-road progress.
        double advance = age >= Options.UncertaintyTimeout ? Math.Max(0, forward) : 0;
        double progress = Math.Min(activeRoute.Points[^1].DistanceFromStartMeters, fromProgress + advance);
        if (forward < -RoutePositionObservationPolicy.MotionAllowanceMeters ||
            lateral > RoutePositionObservationPolicy.MotionAllowanceMeters ||
            !RouteDisplayProgressPolicy.TryAtDistance(activeRoute, progress, out estimate) ||
            estimate.SegmentIndex != start.SegmentIndex) return false; // No estimated turn or branch choice.
        estimate = estimate with { AlignmentCoordinate = new GeoCoordinate(
            fromCoordinate.Latitude + estimate.RoadCoordinate.Latitude - start.RoadCoordinate.Latitude,
            fromCoordinate.Longitude + estimate.RoadCoordinate.Longitude - start.RoadCoordinate.Longitude) };
        return true;
    }
}
