using System;
using System.Collections.Generic;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Guidance;

/// <summary>Qualifies geographic vicinity evidence independently of AR and
/// keeps vicinity detection separate from explicit user-reported arrival.</summary>
public sealed class ArrivalGuidancePolicy
{
    public const double MaximumEvidenceAccuracyMeters = 20;
    public static readonly TimeSpan ProviderReferenceLifetime = TimeSpan.FromSeconds(15);
    private readonly Dictionary<string, Observation> providers = new();
    private Observation? accepted;
    private long destinationVersion;
    private GeoCoordinate? destination;

    public readonly record struct Observation(GeoCoordinate Coordinate, double? AccuracyMeters,
        string Provider, DateTimeOffset ObservedAt);
    public readonly record struct EvidenceDecision(bool IsAccepted, string Reason, string Provider,
        DateTimeOffset ObservedAt);
    public EvidenceDecision LastEvidence { get; private set; }

    public void Reset()
    {
        providers.Clear();
        accepted = null;
        destination = null;
        destinationVersion = 0;
        LastEvidence = default;
    }

    public SafeZoneConfirmationService.SafeZoneDecision Evaluate(SafeZoneConfirmationService vicinity,
        Observation observation, GeoCoordinate target, double radius, double remaining,
        DateTimeOffset now, long trip, bool majorRoadBarrier = false, bool geographicHeld = false)
    {
        if (trip < destinationVersion)
        {
            LastEvidence = new(false, "older destination observation ignored", observation.Provider, observation.ObservedAt);
            return SafeZoneConfirmationService.SafeZoneDecision.Unavailable with { DestinationVersion = trip };
        }
        if (destinationVersion != trip || destination != target)
        {
            Reset();
            destinationVersion = trip;
            destination = target;
            vicinity.Reset(trip);
        }
        LastEvidence = target.IsValid ? Qualify(observation, now, geographicHeld)
            : new(false, "destination coordinate is invalid", observation.Provider, observation.ObservedAt);
        if (!LastEvidence.IsAccepted)
        {
            double effectiveRadius = Math.Min(SafeZoneConfirmationService.MaximumAutomaticConfirmationRadiusMeters,
                double.IsFinite(radius) ? Math.Clamp(radius, SafeZoneConfirmationService.MinimumSupportedArrivalRadiusMeters,
                    SafeZoneConfirmationService.MaximumSupportedArrivalRadiusMeters) : SafeZoneConfirmationService.ArrivalRadiusMeters);
            return vicinity.Current with
            {
                IsAvailable = true, IsCandidate = false, IsConfirmed = false, IsObservationHeld = true,
                DestinationVersion = trip, ObservedAt = observation.ObservedAt,
                AccuracyMeters = observation.AccuracyMeters,
                DistanceToDestinationMeters = observation.Coordinate.IsValid && target.IsValid
                    ? observation.Coordinate.DistanceTo(target) : double.PositiveInfinity,
                ArrivalRadiusMeters = effectiveRadius, RemainingRouteMeters = remaining,
                MaximumRemainingRouteMeters = Math.Max(SafeZoneConfirmationService.MaximumRemainingRouteMeters,
                    effectiveRadius + 30),
                Reason = "Arrival position held: " + LastEvidence.Reason
            };
        }
        // Vicinity is a current observation, not a terminal trip latch. A
        // qualified departure or mapped crossing starts a new proximity vote.
        if (vicinity.Current.IsConfirmed && (majorRoadBarrier ||
            observation.Coordinate.DistanceTo(target) > vicinity.Current.ArrivalRadiusMeters))
            vicinity.Reset(trip);
        return vicinity.Evaluate(observation.Coordinate, target, radius, observation.AccuracyMeters,
            remaining, observation.ObservedAt, majorRoadBarrier, now, trip);
    }

    public static bool ShouldComplete(SafeZoneConfirmationService.SafeZoneDecision decision,
        bool userReported, bool explicitActionEligible) =>
        userReported && explicitActionEligible && decision.IsAvailable &&
        decision.IsConfirmed && !decision.IsObservationHeld;

    private EvidenceDecision Qualify(Observation observation, DateTimeOffset now, bool geographicHeld)
    {
        EvidenceDecision Result(bool allowed, string reason) => new(allowed, reason,
            observation.Provider, observation.ObservedAt);
        TimeSpan age = now - observation.ObservedAt;
        if (!observation.Coordinate.IsValid || observation.AccuracyMeters is not double accuracy ||
            !double.IsFinite(accuracy) || accuracy < 0 || accuracy > MaximumEvidenceAccuracyMeters)
            return Result(false, "a precise geographic observation is required");
        if (age < TimeSpan.FromSeconds(-2) || age > TimeSpan.FromSeconds(5))
            return Result(false, "a fresh geographic observation is required");
        if (geographicHeld) return Result(false, "navigation has not accepted this geographic observation");
        if (accepted is { } last && observation.ObservedAt < last.ObservedAt)
            return Result(false, "older geographic observation ignored");
        string provider = observation.Provider.ToLowerInvariant() switch
        {
            "gps" => "gps", "network" => "network", "fused" => "fused", _ => "other"
        };
        foreach (var reference in providers)
        {
            var previous = reference.Value;
            TimeSpan elapsed = observation.ObservedAt - previous.ObservedAt;
            if (reference.Key == provider || elapsed < TimeSpan.Zero || elapsed > ProviderReferenceLifetime ||
                previous.AccuracyMeters!.Value > accuracy) continue;
            if (previous.Coordinate.DistanceTo(observation.Coordinate) >
                previous.AccuracyMeters.Value + accuracy + 2 + elapsed.TotalSeconds * 3)
                return Result(false, "provider position contradicts a recent more precise geographic observation");
        }
        if (accepted is { } baseline)
        {
            TimeSpan elapsed = observation.ObservedAt - baseline.ObservedAt;
            if (elapsed >= TimeSpan.Zero && elapsed <= ProviderReferenceLifetime &&
                baseline.Coordinate.DistanceTo(observation.Coordinate) >
                    baseline.AccuracyMeters!.Value + accuracy + 2 + elapsed.TotalSeconds * 3)
                return Result(false, "geographic displacement exceeds pedestrian movement and reported uncertainty");
        }
        accepted = observation;
        providers[provider] = observation;
        return Result(true, "fresh consistent geographic observation");
    }
}
