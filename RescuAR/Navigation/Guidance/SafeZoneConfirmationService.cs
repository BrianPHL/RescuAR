using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Guidance;

/// <summary>
/// Conservative destination-arrival confirmation for pedestrian guidance.
///
/// A safe zone is not considered reached from one GPS sample. The service
/// requires repeated good-quality fixes within the facility vicinity.
///
/// Evacuation centers are areas, not mathematical points. Callers may provide
/// a facility-specific arrival radius that covers the evacuation building /
/// compound vicinity. The default radius is 75 m.
/// </summary>
public sealed class SafeZoneConfirmationService
{
    public const int RequiredConfirmationCount =
        2;

    /// <summary>
    /// Default radius used when no facility-specific profile exists.
    /// </summary>
    public const double ArrivalRadiusMeters =
        75.0;

    public const double MaximumAcceptedAccuracyMeters =
        15.0;

    /// <summary>
    /// Retained in the diagnostic decision for legacy callers; route progress
    /// does not gate proximity confirmation.
    /// </summary>
    public const double MaximumRemainingRouteMeters =
        45.0;

    public const double CandidateResetDistanceMeters =
        45.0;

    public const double CandidateResetRemainingRouteMeters =
        70.0;

    public const double MinimumSupportedArrivalRadiusMeters =
        20.0;

    public const double MaximumSupportedArrivalRadiusMeters =
        150.0;

    private const double RouteAllowanceBeyondSafeZoneMeters =
        30.0;

    private const double CandidateResetMarginMeters =
        20.0;

    private int confirmationCount;
    private bool confirmed;

    private DateTimeOffset? lastCountedObservationTime;

    private SafeZoneDecision current =
        SafeZoneDecision.Unavailable;

    public SafeZoneDecision Current =>
        current;

    /// <summary>
    /// Compatibility overload using the default facility radius.
    /// </summary>
    public SafeZoneDecision Evaluate(
        GeoCoordinate currentCoordinate,
        GeoCoordinate destinationCoordinate,
        double? accuracyMeters,
        double remainingRouteMeters,
        DateTimeOffset observedAt)
    {
        return Evaluate(
            currentCoordinate,
            destinationCoordinate,
            ArrivalRadiusMeters,
            accuracyMeters,
            remainingRouteMeters,
            observedAt);
    }

    /// <summary>
    /// Evaluates arrival against a facility-specific safe-zone radius.
    ///
    /// The radius should represent the usable evacuation facility vicinity
    /// around destinationCoordinate, not just a doorway or map pin.
    /// </summary>
    public SafeZoneDecision Evaluate(
        GeoCoordinate currentCoordinate,
        GeoCoordinate destinationCoordinate,
        double arrivalRadiusMeters,
        double? accuracyMeters,
        double remainingRouteMeters,
        DateTimeOffset observedAt)
    {
        double effectiveArrivalRadiusMeters =
            NormalizeArrivalRadius(
                arrivalRadiusMeters);

        double effectiveMaximumRemainingRouteMeters =
            Math.Max(
                MaximumRemainingRouteMeters,
                effectiveArrivalRadiusMeters +
                    RouteAllowanceBeyondSafeZoneMeters);

        double effectiveCandidateResetDistanceMeters =
            Math.Max(
                CandidateResetDistanceMeters,
                effectiveArrivalRadiusMeters +
                    CandidateResetMarginMeters);

        if (!currentCoordinate.IsValid ||
            !destinationCoordinate.IsValid)
        {
            current =
                new SafeZoneDecision(
                    IsAvailable: false,
                    IsCandidate: false,
                    IsConfirmed: confirmed,
                    ConfirmationCount: confirmationCount,
                    RequiredConfirmationCount: RequiredConfirmationCount,
                    DistanceToDestinationMeters: double.PositiveInfinity,
                    ArrivalRadiusMeters: effectiveArrivalRadiusMeters,
                    RemainingRouteMeters: remainingRouteMeters,
                    MaximumRemainingRouteMeters: effectiveMaximumRemainingRouteMeters,
                    AccuracyMeters: accuracyMeters,
                    ObservedAt: observedAt,
                    Reason: "Current or destination coordinate is invalid.");

            return current;
        }

        double distanceToDestinationMeters =
            currentCoordinate.DistanceTo(
                destinationCoordinate);

        bool accuracyIsFinite =
            accuracyMeters.HasValue &&
            double.IsFinite(
                accuracyMeters.Value) &&
            accuracyMeters.Value >=
                0.0;

        bool accuracyIsAcceptable =
            accuracyIsFinite &&
            accuracyMeters!.Value <=
                MaximumAcceptedAccuracyMeters;

        // The accuracy margin keeps a GPS fix on the outer boundary from
        // confirming a facility on the opposite side of that boundary.
        bool withinArrivalRadius =
            accuracyIsAcceptable &&
            double.IsFinite(distanceToDestinationMeters) &&
            distanceToDestinationMeters + accuracyMeters!.Value <=
                effectiveArrivalRadiusMeters;

        if (confirmed)
        {
            current =
                new SafeZoneDecision(
                    IsAvailable: true,
                    IsCandidate: true,
                    IsConfirmed: true,
                    ConfirmationCount: RequiredConfirmationCount,
                    RequiredConfirmationCount: RequiredConfirmationCount,
                    DistanceToDestinationMeters: distanceToDestinationMeters,
                    ArrivalRadiusMeters: effectiveArrivalRadiusMeters,
                    RemainingRouteMeters: remainingRouteMeters,
                    MaximumRemainingRouteMeters: effectiveMaximumRemainingRouteMeters,
                    AccuracyMeters: accuracyMeters,
                    ObservedAt: observedAt,
                    Reason: "Evacuation-center vicinity has already been confirmed; entry is unverified.");

            return current;
        }

        if (withinArrivalRadius)
        {
            bool isNewGpsObservation =
                !lastCountedObservationTime.HasValue ||
                observedAt >
                    lastCountedObservationTime.Value;

            if (isNewGpsObservation)
            {
                confirmationCount =
                    Math.Min(
                        RequiredConfirmationCount,
                        confirmationCount +
                            1);

                lastCountedObservationTime =
                    observedAt;
            }

            confirmed =
                confirmationCount >=
                    RequiredConfirmationCount;

            current =
                new SafeZoneDecision(
                    IsAvailable: true,
                    IsCandidate: true,
                    IsConfirmed: confirmed,
                    ConfirmationCount: confirmationCount,
                    RequiredConfirmationCount: RequiredConfirmationCount,
                    DistanceToDestinationMeters: distanceToDestinationMeters,
                    ArrivalRadiusMeters: effectiveArrivalRadiusMeters,
                    RemainingRouteMeters: remainingRouteMeters,
                    MaximumRemainingRouteMeters: effectiveMaximumRemainingRouteMeters,
                    AccuracyMeters: accuracyMeters,
                    ObservedAt: observedAt,
                    Reason: confirmed
                        ? "Repeated accurate GPS fixes confirm the evacuation-center vicinity; facility entry has not been verified."
                        : isNewGpsObservation
                            ? "Accurate GPS indicates the evacuation-center vicinity; waiting for a second fix."
                            : "Duplicate or stale GPS observation held; waiting for a newer fix.");

            return current;
        }

        bool clearlyOutsideDestination =
            double.IsFinite(
                distanceToDestinationMeters) &&
            distanceToDestinationMeters >
                effectiveCandidateResetDistanceMeters;

        if (clearlyOutsideDestination)
        {
            confirmationCount =
                0;

            lastCountedObservationTime =
                null;
        }

        string reason =
            !accuracyIsFinite
                ? "GPS accuracy is unavailable."
                : !accuracyIsAcceptable
                    ? $"GPS accuracy exceeds {MaximumAcceptedAccuracyMeters:F0} m."
                    : !withinArrivalRadius
                        ? $"User and GPS accuracy margin extend beyond the {effectiveArrivalRadiusMeters:F0} m arrival radius."
                            : "Arrival conditions are not satisfied.";

        current =
            new SafeZoneDecision(
                IsAvailable: true,
                IsCandidate: false,
                IsConfirmed: false,
                ConfirmationCount: confirmationCount,
                RequiredConfirmationCount: RequiredConfirmationCount,
                DistanceToDestinationMeters: distanceToDestinationMeters,
                ArrivalRadiusMeters: effectiveArrivalRadiusMeters,
                RemainingRouteMeters: remainingRouteMeters,
                MaximumRemainingRouteMeters: effectiveMaximumRemainingRouteMeters,
                AccuracyMeters: accuracyMeters,
                ObservedAt: observedAt,
                Reason: reason);

        return current;
    }

    public void Reset()
    {
        confirmationCount =
            0;

        confirmed =
            false;

        lastCountedObservationTime =
            null;

        current =
            SafeZoneDecision.Unavailable;
    }

    private static double NormalizeArrivalRadius(
        double arrivalRadiusMeters)
    {
        if (!double.IsFinite(
                arrivalRadiusMeters) ||
            arrivalRadiusMeters <=
                0.0)
        {
            return ArrivalRadiusMeters;
        }

        return Math.Clamp(
            arrivalRadiusMeters,
            MinimumSupportedArrivalRadiusMeters,
            MaximumSupportedArrivalRadiusMeters);
    }

    public readonly record struct SafeZoneDecision(
        bool IsAvailable,
        bool IsCandidate,
        bool IsConfirmed,
        int ConfirmationCount,
        int RequiredConfirmationCount,
        double DistanceToDestinationMeters,
        double ArrivalRadiusMeters,
        double RemainingRouteMeters,
        double MaximumRemainingRouteMeters,
        double? AccuracyMeters,
        DateTimeOffset ObservedAt,
        string Reason)
    {
        public static SafeZoneDecision Unavailable =>
            new(
                IsAvailable: false,
                IsCandidate: false,
                IsConfirmed: false,
                ConfirmationCount: 0,
                RequiredConfirmationCount: SafeZoneConfirmationService.RequiredConfirmationCount,
                DistanceToDestinationMeters: double.PositiveInfinity,
                ArrivalRadiusMeters: SafeZoneConfirmationService.ArrivalRadiusMeters,
                RemainingRouteMeters: double.PositiveInfinity,
                MaximumRemainingRouteMeters: SafeZoneConfirmationService.MaximumRemainingRouteMeters,
                AccuracyMeters: null,
                ObservedAt: default,
                Reason: "No safe-zone evaluation has been performed.");
    }
}
