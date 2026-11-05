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
    private LocationReading? latestQualifiedArrivalReading;
    private long qualifiedArrivalTrip = -1;
    private string ArrivalStatusSource => safeZoneConfirmed ? "USER_REPORTED" :
        lastSafeZoneDecision.IsConfirmed && !lastSafeZoneDecision.IsObservationHeld
            ? "GPS_VICINITY_GUIDANCE_ACTIVE" : "UNCONFIRMED";

    private SafeZoneConfirmationService.SafeZoneDecision EvaluateArrivalReading(LocationReading reading,
        RescuAR.Navigation.Models.GeoCoordinate destination, double radius, double remaining,
        bool majorRoadBarrier, long trip, bool geographicHeld = false)
    {
        lock (routeProgressFusionSync)
        {
            var evaluatedAt = NavigationClock.UtcNow;
            var decision = arrivalGuidance.Evaluate(_safeZoneConfirmationService,
                new(reading.Coordinate, reading.AccuracyMeters, reading.Provider, reading.ObservationTime),
                destination, radius, remaining, evaluatedAt, trip, majorRoadBarrier, geographicHeld);
            LogReplayArrivalObservation(reading, destination, radius, remaining, majorRoadBarrier,
                trip, geographicHeld, decision, evaluatedAt);
            if (arrivalGuidance.LastEvidence.IsAccepted)
            {
                latestQualifiedArrivalReading = reading;
                qualifiedArrivalTrip = trip;
            }
            return decision;
        }
    }

    private LocationReading? GetQualifiedArrivalReading()
    {
        lock (routeProgressFusionSync)
            return qualifiedArrivalTrip == NavigationDestinationBridge.Current.Version
                ? latestQualifiedArrivalReading : null;
    }

    private void ResetArrivalEvidence()
    {
        lock (routeProgressFusionSync)
        {
            arrivalGuidance.Reset();
            latestQualifiedArrivalReading = null;
            qualifiedArrivalTrip = -1;
        }
    }

    private bool HasArrivalNoticePriority()
    {
        var destination = NavigationDestinationBridge.Current;
        var reading = GetQualifiedArrivalReading();
        if (!pageIsVisible || safeZoneConfirmed || emergencyAdvisoryVisible || !destination.IsAvailable ||
            activeDestinationVersion != destination.Version || reading is null) return false;
        TimeSpan age = NavigationClock.UtcNow - reading.ObservationTime;
        return age >= TimeSpan.FromSeconds(-2) && age <= UserArrivalConfirmationPolicy.MaximumFixAge &&
            reading.Coordinate.DistanceTo(destination.Coordinate) <=
                Math.Min(destination.SafeZoneRadiusMeters, SafeZoneConfirmationService.MaximumAutomaticConfirmationRadiusMeters);
    }

    private void SetTurnGuidanceVisible(bool visible) =>
        turnGuidancePanel.IsVisible = visible && !routeHazardBlocksGuidance;

    private bool CanOfferUserArrival()
    {
        var destination = NavigationDestinationBridge.Current;
        var reading = GetQualifiedArrivalReading();
        return pageIsVisible && !safeZoneConfirmed && !emergencyAdvisoryVisible &&
            currentCameraModuleView != CameraModuleViewMode.FloodDepth &&
            destination.IsAvailable && activeDestinationVersion == destination.Version &&
            activeDestinationCoordinate == destination.Coordinate &&
            reading is not null && UserArrivalConfirmationPolicy.CanOffer(
                reading.Coordinate, destination.Coordinate, reading.AccuracyMeters,
                reading.ObservationTime, NavigationClock.UtcNow,
                majorRoadBarrier: arrivalRoadGraph is null || arrivalRoadGraph.AccessCrossesMajorRoad(
                    reading.Coordinate, destination.Coordinate));
    }

    private async void OnConfirmUserArrivalClicked(object? sender, EventArgs e)
    {
        if (userArrivalPromptOpen || !CanOfferUserArrival()) return;
        var destination = NavigationDestinationBridge.Current;
        long sessionGeneration = Interlocked.Read(ref arrivalSessionGeneration);
        userArrivalPromptOpen = true;
        confirmUserArrivalButton.IsEnabled = false;
        try
        {
            bool accepted = await DisplayAlert("Confirm your arrival",
                $"Are you physically at {destination.Name}? This records your confirmation and stops route guidance. GPS has not verified your arrival.",
                "Confirm arrival", "Keep guiding");
            if (!accepted || sessionGeneration != Interlocked.Read(ref arrivalSessionGeneration) ||
                NavigationDestinationBridge.Current != destination || !CanOfferUserArrival()) return;

            lock (routeProgressFusionSync)
            {
                if (sessionGeneration != Interlocked.Read(ref arrivalSessionGeneration) ||
                    NavigationDestinationBridge.Current != destination || !CanOfferUserArrival() ||
                    GetQualifiedArrivalReading() is not { } reading) return;
                var progress = _routeProgressTracker.Current;
                var decision = new SafeZoneConfirmationService.SafeZoneDecision(
                    IsAvailable: true, IsCandidate: false, IsConfirmed: true,
                    ConfirmationCount: 0,
                    RequiredConfirmationCount: SafeZoneConfirmationService.RequiredConfirmationCount,
                    DistanceToDestinationMeters: reading.Coordinate.DistanceTo(destination.Coordinate),
                    ArrivalRadiusMeters: SafeZoneConfirmationService.ArrivalRadiusMeters,
                    RemainingRouteMeters: progress.RemainingMeters,
                    MaximumRemainingRouteMeters: SafeZoneConfirmationService.MaximumRemainingRouteMeters,
                    AccuracyMeters: reading.AccuracyMeters, ObservedAt: reading.ObservationTime,
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
