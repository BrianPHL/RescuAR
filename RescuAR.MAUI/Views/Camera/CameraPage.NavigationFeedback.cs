using RescuAR.MAUI.Services.Navigation;
using RescuAR.MAUI.Services.Settings;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.State;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private NavigationFeedbackController? navigationFeedback;
    private bool navigationFeedbackObserved;
    private string? navigationFeedbackPreferenceError;
    private CapturedFeedbackGuidance? capturedFeedbackGuidance;
    private sealed record CapturedFeedbackGuidance(RouteResult Route, long Trip,
        PedestrianTurnGuidanceService.TurnGuidanceSnapshot Guidance, string Text, DateTimeOffset At);

    private void InitializeNavigationFeedback()
    {
        navigationFeedback = new(new MauiNavigationSpeech(), duration => Vibration.Default.Vibrate(duration));
        turnGuidancePanel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VisualElement.IsVisible) && !turnGuidancePanel.IsVisible &&
                currentCameraModuleView == CameraModuleViewMode.ArCamera && !safeZoneConfirmed) navigationFeedback.Stop();
        };
        ApplyNavigationFeedbackSettings();
    }
    private void ObserveNavigationFeedbackSettings()
    {
        if (!navigationFeedbackObserved)
        {
            AppPreferences.Current.Changed += NavigationFeedbackSettingChanged;
            navigationFeedbackObserved = true;
        }
        ApplyNavigationFeedbackSettings();
    }
    private void StopNavigationFeedback(bool unsubscribe)
    {
        navigationFeedback?.Stop(); capturedFeedbackGuidance = null;
        if (unsubscribe && navigationFeedbackObserved)
        {
            AppPreferences.Current.Changed -= NavigationFeedbackSettingChanged;
            navigationFeedbackObserved = false;
        }
    }
    private void NavigationFeedbackSettingChanged(AppSetting setting)
    {
        if (setting is not (AppSetting.NavigationVoice or AppSetting.HapticFeedback)) return;
        if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(ApplyNavigationFeedbackSettings);
        else ApplyNavigationFeedbackSettings();
    }
    private void ApplyNavigationFeedbackSettings()
    {
        bool voice = AppPreferences.IsEnabled(AppSetting.NavigationVoice);
        navigationFeedback?.Configure(voice, AppPreferences.IsEnabled(AppSetting.HapticFeedback));
        cameraVoiceGuidanceIcon.Source = voice ? "lucide_volume_2_black.png" : "lucide_volume_x_black.png";
        SemanticProperties.SetDescription(cameraVoiceGuidanceButton, voice ? "Turn spoken directions off" : "Turn spoken directions on");
        UpdateNavigationFeedbackStatus();
    }
    private void OnVoiceGuidanceToggleClicked(object? sender, TappedEventArgs args)
    {
        try
        {
            AppPreferences.Current.Set(AppSetting.NavigationVoice, !AppPreferences.IsEnabled(AppSetting.NavigationVoice));
            navigationFeedbackPreferenceError = null;
        }
        catch { navigationFeedbackPreferenceError = "The voice setting was not saved. Your previous choice is retained."; }
        ApplyNavigationFeedbackSettings(); RefreshNavigationFeedback();
    }
    private void UpdateNavigationFeedbackStatus()
    {
        string text = navigationFeedbackPreferenceError ?? navigationFeedback?.LastError ?? "";
        navigationFeedbackStatusLabel.Text = text; navigationFeedbackStatusLabel.IsVisible = text.Length > 0;
    }

    private void ObserveNavigationFeedbackGuidance(PedestrianTurnGuidanceService.TurnGuidanceSnapshot guidance)
    {
        if (currentCameraModuleView != CameraModuleViewMode.ArCamera || activeRoute is not { } route) return;
        capturedFeedbackGuidance = new(route, activeDestinationVersion, guidance, turnInstructionLabel.Text ?? "", DateTimeOffset.UtcNow);
        RefreshNavigationFeedback();
    }
    private void RefreshNavigationFeedback()
    {
        if (navigationFeedback is null) return;
        if (Dispatcher.IsDispatchRequired) { Dispatcher.Dispatch(RefreshNavigationFeedback); return; }
        var destination = NavigationDestinationBridge.Current;
        RouteResult? route; PedestrianTurnGuidanceService.TurnGuidanceSnapshot guidance;
        double progress; bool qualified; var now = DateTimeOffset.UtcNow;
        lock (routeProgressFusionSync)
        {
            route = CameraMapPresentationPolicy.ForTrip(activeRoute, activeDestinationVersion, destination.Version, destination.IsAvailable);
            var tracker = _routeProgressTracker.Current; progress = tracker.CommittedProgressMeters;
            bool goodFix = latestGpsCoordinateForRouting is { } coordinate && latestGpsTimestampForRouting is { } at &&
                RescuAR.Navigation.Projection.RouteStartupLocationPolicy.CanPlan(coordinate, latestGpsAccuracyForRouting, at, now, false);
            qualified = route is not null && goodFix && !positionObservationHeld && !lastOffRouteCandidate &&
                !_gpsPdrFusionPolicy.IsRouteIdentitySuspended && lastGpsConfidence >= RescuAR.Navigation.Progress.GpsPdrFusionPolicy.GpsConfidence.Medium;
            guidance = route is null ? PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable : CameraMapPresentationPolicy.Guidance(route, tracker, !qualified);
        }
        string text = guidance.IsAvailable ? RouteDirectionsService.Text(guidance.Instruction) +
            (double.IsFinite(guidance.DistanceToTurnMeters) ? $" in approximately {guidance.DistanceToTurnMeters:0} metres." : ".") : "";
        if (currentCameraModuleView == CameraModuleViewMode.ArCamera)
        {
            var captured = capturedFeedbackGuidance;
            bool current = captured is not null && ReferenceEquals(captured.Route, route) && captured.Trip == destination.Version &&
                now - captured.At <= TimeSpan.FromSeconds(2.5) && turnGuidancePanel.IsVisible && !cameraPipelineTerminalFailureVisible;
            qualified &= current;
            if (current && captured is not null)
            {
                guidance = captured.Guidance;
                text = captured.Text;
                if (guidance.Instruction == PedestrianTurnGuidanceService.TurnInstruction.FollowRoute)
                    guidance = guidance with { DisplayText = text };
            }
        }
        bool visible = pageIsVisible && !cameraMapWindowStopped && currentCameraModuleView != CameraModuleViewMode.FloodDepth;
        bool interrupted = emergencyAdvisoryVisible || (!safeZoneConfirmed &&
            (routeRequestInProgress || dynamicRerouteInProgress || routeHazardBlocksGuidance || lastSafeZoneDecision.IsCandidate));
        cameraVoiceGuidanceButton.IsVisible = visible && !emergencyAdvisoryVisible;
        _ = PublishNavigationFeedbackAsync(new(destination.Version, route, visible && destination.IsAvailable && !interrupted,
            qualified, safeZoneConfirmed && destination.IsAvailable, guidance, progress, text, destination.Name, now));
    }
    private async Task PublishNavigationFeedbackAsync(NavigationFeedbackFrame frame)
    {
        await navigationFeedback!.UpdateAsync(frame);
        UpdateNavigationFeedbackStatus();
    }
}
