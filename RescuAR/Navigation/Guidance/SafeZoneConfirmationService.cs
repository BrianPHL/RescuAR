using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Guidance;

/// <summary>
/// Conservative destination-arrival confirmation for pedestrian guidance.
///
/// A safe zone is not considered reached from one GPS sample. The service
/// requires repeated good-quality fixes that agree with both geographic
/// facility proximity and the retained route-progress state.
///
/// Evacuation centers are areas, not mathematical points. Callers may provide
/// a facility-specific arrival radius that covers the evacuation building /
/// compound vicinity. The legacy 30 m radius remains the fallback.
/// </summary>
public sealed class SafeZoneConfirmationService
{
    public const int RequiredConfirmationCount =
        3;

    /// <summary>
    /// Legacy/default radius used when no facility-specific profile exists.
    /// Kept under the original name for compatibility with the Stage 5
    /// developer-validation harness.
    /// </summary>
    public const double ArrivalRadiusMeters =
        30.0;

    public const double MaximumAcceptedAccuracyMeters =
        30.0;

    /// <summary>
    /// Legacy minimum route-progress gate. Larger facilities automatically
    /// receive a proportionally larger route-near-destination allowance.
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

    private const double CandidateRouteResetMarginMeters =
        30.0;

    private int confirmationCount;
    private bool confirmed;

    private DateTimeOffset? lastCountedObservationTime;

    private SafeZoneDecision current =
        SafeZoneDecision.Unavailable;

    public SafeZoneDecision Current =>
        current;

    /// <summary>
    /// Compatibility overload retaining the original 30 m behavior.
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

        double effectiveCandidateResetRemainingRouteMeters =
            Math.Max(
                CandidateResetRemainingRouteMeters,
                effectiveMaximumRemainingRouteMeters +
                    CandidateRouteResetMarginMeters);

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

        bool remainingIsFinite =
            double.IsFinite(
                remainingRouteMeters) &&
            remainingRouteMeters >=
                0.0;

        bool withinArrivalRadius =
            double.IsFinite(
                distanceToDestinationMeters) &&
            distanceToDestinationMeters <=
                effectiveArrivalRadiusMeters;

        bool routeIsNearDestination =
            remainingIsFinite &&
            remainingRouteMeters <=
                effectiveMaximumRemainingRouteMeters;

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
                    Reason: "Safe-zone arrival is already confirmed.");

            return current;
        }

        if (accuracyIsAcceptable &&
            withinArrivalRadius &&
            routeIsNearDestination)
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
                        ? "Repeated GPS and route-progress checks confirm entry into the evacuation-center safe-zone vicinity."
                        : isNewGpsObservation
                            ? "GPS and route progress both indicate the user is inside the evacuation-center safe-zone vicinity."
                            : "Duplicate or stale GPS observation held; waiting for a newer fix.");

            return current;
        }

        bool clearlyOutsideDestination =
            double.IsFinite(
                distanceToDestinationMeters) &&
            distanceToDestinationMeters >
                effectiveCandidateResetDistanceMeters;

        bool clearlyTooMuchRouteRemaining =
            remainingIsFinite &&
            remainingRouteMeters >
                effectiveCandidateResetRemainingRouteMeters;

        if (clearlyOutsideDestination ||
            clearlyTooMuchRouteRemaining)
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
                        ? $"User is outside the {effectiveArrivalRadiusMeters:F0} m safe-zone radius."
                        : !routeIsNearDestination
                            ? $"Route has more than {effectiveMaximumRemainingRouteMeters:F0} m remaining."
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
