using RescuAR.Navigation.Timing;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.State;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private long arrivalSessionGeneration;
    private bool userArrivalPromptOpen;
    private bool arrivalWasUserReported;
    private long observedDestinationVersion = -1;
    private long activeDestinationVersion;
    private string arrivalNoticeCategory = "HIDDEN";
    private readonly ArrivalGuidancePolicy arrivalGuidance = new();
    private string ArrivalStatusSource => safeZoneConfirmed ? "USER_REPORTED" :
        lastSafeZoneDecision.IsConfirmed && !lastSafeZoneDecision.IsObservationHeld
            ? "GPS_VICINITY_GUIDANCE_ACTIVE" : "UNCONFIRMED";

    private SafeZoneConfirmationService.SafeZoneDecision EvaluateArrivalReading(LocationReading reading,
        RescuAR.Navigation.Models.GeoCoordinate destination, double radius, double remaining,
        bool majorRoadBarrier, long trip, bool geographicHeld = false)
    {
        lock (routeProgressFusionSync)
        {
            var selectedDestination = NavigationDestinationBridge.Current;
            if (!selectedDestination.IsAvailable || selectedDestination.Version != trip)
                return SafeZoneConfirmationService.SafeZoneDecision.Unavailable;
            var evaluatedAt = NavigationClock.UtcNow;
            var decision = arrivalGuidance.Evaluate(_safeZoneConfirmationService,
                new(reading.Coordinate, reading.AccuracyMeters, reading.Provider, reading.ObservationTime),
                destination, radius, remaining, evaluatedAt, trip, majorRoadBarrier, geographicHeld);
            LogReplayArrivalObservation(reading, destination, radius, remaining, majorRoadBarrier,
                trip, geographicHeld, decision, evaluatedAt);
            return decision;
        }
    }

    private ArrivalGuidancePolicy.Observation? GetQualifiedArrivalReading(
        NavigationDestinationBridge.DestinationSnapshot destination)
    {
        lock (routeProgressFusionSync)
            return arrivalGuidance.GetConfirmationObservation(destination.Version, destination.Coordinate);
    }

    private void ResetArrivalEvidence()
    {
        lock (routeProgressFusionSync)
            arrivalGuidance.Reset();
    }

    private bool HasArrivalNoticePriority()
    {
        var destination = NavigationDestinationBridge.Current;
        if (!pageIsVisible || cameraMapWindowStopped || safeZoneConfirmed || emergencyAdvisoryVisible || !destination.IsAvailable ||
            activeDestinationVersion != destination.Version ||
            GetQualifiedArrivalReading(destination) is not { } reading) return false;
        TimeSpan age = NavigationClock.UtcNow - reading.ObservedAt;
        return age >= TimeSpan.FromSeconds(-2) && age <= UserArrivalConfirmationPolicy.MaximumFixAge &&
            reading.Coordinate.DistanceTo(destination.Coordinate) <=
                Math.Min(destination.SafeZoneRadiusMeters, SafeZoneConfirmationService.MaximumAutomaticConfirmationRadiusMeters);
    }

    private void SetTurnGuidanceVisible(bool visible) =>
        turnGuidancePanel.IsVisible = visible && !routeHazardBlocksGuidance;

    private bool CanOfferUserArrival() =>
        TryGetUserArrivalReading(NavigationDestinationBridge.Current, out _);

    private bool TryGetUserArrivalReading(NavigationDestinationBridge.DestinationSnapshot destination,
        out ArrivalGuidancePolicy.Observation reading)
    {
        reading = default;
        if (!pageIsVisible || cameraMapWindowStopped || safeZoneConfirmed || emergencyAdvisoryVisible ||
            currentCameraModuleView == CameraModuleViewMode.FloodDepth ||
            !destination.IsAvailable || activeDestinationVersion != destination.Version ||
            activeDestinationCoordinate != destination.Coordinate ||
            GetQualifiedArrivalReading(destination) is not { } observation) return false;

        reading = observation;
        return UserArrivalConfirmationPolicy.CanOffer(reading.Coordinate, destination.Coordinate,
            reading.AccuracyMeters, reading.ObservedAt, NavigationClock.UtcNow,
            majorRoadBarrier: arrivalRoadGraph is null || arrivalRoadGraph.AccessCrossesMajorRoad(
                reading.Coordinate, destination.Coordinate));
    }

    private async void OnConfirmUserArrivalClicked(object? sender, EventArgs e)
    {
        NavigationDestinationBridge.DestinationSnapshot destination;
        long sessionGeneration;
        long confirmationRevision;
        lock (routeProgressFusionSync)
        {
            destination = NavigationDestinationBridge.Current;
            if (userArrivalPromptOpen || !TryGetUserArrivalReading(destination, out _)) return;
            sessionGeneration = Interlocked.Read(ref arrivalSessionGeneration);
            confirmationRevision = arrivalGuidance.ConfirmationRevision;
            userArrivalPromptOpen = true;
        }
        confirmUserArrivalButton.IsEnabled = false;
        try
        {
            bool accepted = await DisplayAlert("Confirm your arrival",
                $"Are you physically at {destination.Name}? This records your confirmation and stops route guidance. GPS has not verified your arrival.",
                "Confirm arrival", "Keep guiding");
            if (!accepted) return;

            lock (routeProgressFusionSync)
            {
                if (sessionGeneration != Interlocked.Read(ref arrivalSessionGeneration) ||
                    confirmationRevision != arrivalGuidance.ConfirmationRevision ||
                    NavigationDestinationBridge.Current != destination ||
                    !TryGetUserArrivalReading(destination, out var reading)) return;
                var progress = _routeProgressTracker.Current;
                var decision = new SafeZoneConfirmationService.SafeZoneDecision(
                    IsAvailable: true, IsCandidate: false, IsConfirmed: true,
                    ConfirmationCount: 0,
                    RequiredConfirmationCount: SafeZoneConfirmationService.RequiredConfirmationCount,
                    DistanceToDestinationMeters: reading.Coordinate.DistanceTo(destination.Coordinate),
                    ArrivalRadiusMeters: SafeZoneConfirmationService.ArrivalRadiusMeters,
                    RemainingRouteMeters: progress.RemainingMeters,
                    MaximumRemainingRouteMeters: SafeZoneConfirmationService.MaximumRemainingRouteMeters,
                    AccuracyMeters: reading.AccuracyMeters, ObservedAt: reading.ObservedAt,
                    Reason: "User reported arrival; GPS proximity and facility entry are unverified.",
                    DestinationVersion: destination.Version);
                HandleSafeZoneDecision(decision, userReported: true);
            }
        }
        catch (Exception exception)
        {
#if ANDROID
            global::Android.Util.Log.Warn(SafeZoneLogTag,
                "User arrival confirmation could not be completed: " + DiagnosticPrivacyPolicy.FormatException(exception));
#endif
        }
        finally
        {
            userArrivalPromptOpen = false;
            confirmUserArrivalButton.IsEnabled = true;
            RefreshCameraModuleDynamicUi();
        }
    }
}
