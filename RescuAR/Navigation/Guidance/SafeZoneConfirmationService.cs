using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Guidance;

/// <summary>
/// Conservative destination-arrival confirmation for pedestrian guidance.
///
/// A safe zone is not considered reached from one GPS sample. The service
/// requires repeated good-quality fixes that agree with both the geographic
/// destination and the retained route-progress state.
///
/// This class is intentionally platform-independent. CameraPage owns UI and
/// Android logging; the navigation project owns the arrival decision.
/// </summary>
public sealed class SafeZoneConfirmationService
{
    public const int RequiredConfirmationCount =
        3;

    public const double ArrivalRadiusMeters =
        30.0;

    public const double MaximumAcceptedAccuracyMeters =
        30.0;

    public const double MaximumRemainingRouteMeters =
        45.0;

    /*
     * Hysteresis: once a candidate sequence has started, a single weak GPS
     * sample near the destination should not immediately erase all progress.
     * A clearly distant fix does reset the sequence.
     */
    public const double CandidateResetDistanceMeters =
        45.0;

    public const double CandidateResetRemainingRouteMeters =
        70.0;

    private int confirmationCount;
    private bool confirmed;

    private DateTimeOffset? lastCountedObservationTime;

    private SafeZoneDecision current =
        SafeZoneDecision.Unavailable;

    public SafeZoneDecision Current =>
        current;

    public SafeZoneDecision Evaluate(
        GeoCoordinate currentCoordinate,
        GeoCoordinate destinationCoordinate,
        double? accuracyMeters,
        double remainingRouteMeters,
        DateTimeOffset observedAt)
    {
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
                    RemainingRouteMeters: remainingRouteMeters,
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
                ArrivalRadiusMeters;

        bool routeIsNearDestination =
            remainingIsFinite &&
            remainingRouteMeters <=
                MaximumRemainingRouteMeters;

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
                    RemainingRouteMeters: remainingRouteMeters,
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
                    RemainingRouteMeters: remainingRouteMeters,
                    AccuracyMeters: accuracyMeters,
                    ObservedAt: observedAt,
                    Reason: confirmed
                        ? "Repeated GPS and route-progress checks confirm arrival."
                        : isNewGpsObservation
                            ? "GPS and route progress both indicate the user is near the destination."
                            : "Duplicate or stale GPS observation held; waiting for a newer fix.");

            return current;
        }

        bool clearlyOutsideDestination =
            double.IsFinite(
                distanceToDestinationMeters) &&
            distanceToDestinationMeters >
                CandidateResetDistanceMeters;

        bool clearlyTooMuchRouteRemaining =
            remainingIsFinite &&
            remainingRouteMeters >
                CandidateResetRemainingRouteMeters;

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
                        ? $"User is outside the {ArrivalRadiusMeters:F0} m arrival radius."
                        : !routeIsNearDestination
                            ? $"Route has more than {MaximumRemainingRouteMeters:F0} m remaining."
                            : "Arrival conditions are not satisfied.";

        current =
            new SafeZoneDecision(
                IsAvailable: true,
                IsCandidate: false,
                IsConfirmed: false,
                ConfirmationCount: confirmationCount,
                RequiredConfirmationCount: RequiredConfirmationCount,
                DistanceToDestinationMeters: distanceToDestinationMeters,
                RemainingRouteMeters: remainingRouteMeters,
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

    public readonly record struct SafeZoneDecision(
        bool IsAvailable,
        bool IsCandidate,
        bool IsConfirmed,
        int ConfirmationCount,
        int RequiredConfirmationCount,
        double DistanceToDestinationMeters,
        double RemainingRouteMeters,
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
                RemainingRouteMeters: double.PositiveInfinity,
                AccuracyMeters: null,
                ObservedAt: default,
                Reason: "No safe-zone evaluation has been performed.");
    }
}
