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

    private bool HasArrivalNoticePriority()
    {
        LocationReading? reading;
        lock (routeProgressFusionSync) reading = latestRouteStartupReading;
        return CanOfferUserArrival() && reading is not null && UserArrivalConfirmationPolicy.CanShowNotice(
            reading.Coordinate, NavigationDestinationBridge.Current.Coordinate, reading.AccuracyMeters,
            reading.Timestamp, DateTimeOffset.UtcNow);
    }

    private void SetTurnGuidanceVisible(bool visible) =>
        turnGuidancePanel.IsVisible = visible && !HasArrivalNoticePriority();

    private bool CanOfferUserArrival()
    {
        var destination = NavigationDestinationBridge.Current;
        LocationReading? reading;
        lock (routeProgressFusionSync) reading = latestRouteStartupReading;
        return pageIsVisible && !safeZoneConfirmed && !emergencyAdvisoryVisible &&
            currentCameraModuleView == CameraModuleViewMode.ArCamera &&
            destination.IsAvailable && activeDestinationVersion == destination.Version &&
            activeDestinationCoordinate == destination.Coordinate &&
            reading is not null && UserArrivalConfirmationPolicy.CanOffer(
                reading.Coordinate, destination.Coordinate, reading.AccuracyMeters,
                reading.Timestamp, DateTimeOffset.UtcNow);
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
                    latestRouteStartupReading is not { } reading) return;
                var progress = _routeProgressTracker.Current;
                var decision = new SafeZoneConfirmationService.SafeZoneDecision(
                    IsAvailable: true, IsCandidate: false, IsConfirmed: true,
                    ConfirmationCount: 0,
                    RequiredConfirmationCount: SafeZoneConfirmationService.RequiredConfirmationCount,
                    DistanceToDestinationMeters: reading.Coordinate.DistanceTo(destination.Coordinate),
                    ArrivalRadiusMeters: SafeZoneConfirmationService.ArrivalRadiusMeters,
                    RemainingRouteMeters: progress.RemainingMeters,
                    MaximumRemainingRouteMeters: SafeZoneConfirmationService.MaximumRemainingRouteMeters,
                    AccuracyMeters: reading.AccuracyMeters, ObservedAt: reading.Timestamp,
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
