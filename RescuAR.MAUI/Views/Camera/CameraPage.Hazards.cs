using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Models;
using RescuAR.AR;
using RescuAR.Navigation.Progress;
using RescuAR.Diagnostics;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private string? routeHazardNotice;
    private long routeHazardNoticeVersion;
    private volatile bool routeHazardBlocksGuidance;
    private string? lastHazardPresentation;
    private RouteResult? lastHazardPresentationRoute;
    private RouteResult? hazardPrefixRoute;
    private double hazardSafePrefixEnd = double.NaN;

    private bool IsCurrentHazardPrefix(ARRouteBridge.RouteSnapshot window) =>
        ReferenceEquals(hazardPrefixRoute, activeRoute) && window.IsAvailable && double.IsFinite(hazardSafePrefixEnd) &&
        hazardSafePrefixEnd > _routeProgressTracker.Current.CommittedProgressMeters + .5 &&
        window.NavigationState.WindowStartProgressMeters + window.Points[^1].DistanceFromWindowStartMeters <= hazardSafePrefixEnd + .01;

    private void ClearRouteHazardNotice()
    {
        long version = Interlocked.Increment(ref routeHazardNoticeVersion);
        Dispatcher.Dispatch(() =>
        {
            if (version != routeHazardNoticeVersion) return;
            routeHazardNotice = null;
            routeHazardBlocksGuidance = false;
            lastHazardPresentation = null;
            lastHazardPresentationRoute = null;
            hazardPrefixRoute = null;
            hazardSafePrefixEnd = double.NaN;
            RefreshEmergencyStatusBanner();
        });
    }

    private void UpdateRouteHazardNotice(RouteResult route,
        HazardReroutingService.HazardRouteAssessment assessment, bool findingAlternative = false)
    {
        if (!_hazardReroutingService.IsCurrentAssessment(assessment)) return;
        string? notice = assessment.IsUnsafe
            ? findingAlternative || dynamicRerouteInProgress && lastRerouteResult == "Requesting"
                ? "Finding a safer route. Stop before the reported hazard."
                : _hazardReroutingService.IsDestinationExcluded(assessment)
                    ? "Destination is in a reported hazard area. Choose another safe center."
                : _hazardReroutingService.IsUnavailable(assessment)
                    ? "No safer route found. Stop before the hazard; check the map."
                    : "Hazard on this route. Check the map."
            : assessment.EscapingHazard is not null
                ? "Leaving a reported hazard area. Stay alert." : null;
        long destinationVersion = activeDestinationVersion;
        string key = $"{destinationVersion}|{route.GeometryFingerprint}|{assessment.RetryContext.Snapshot}|{notice}";
        // An identical 2-second assessment must not enqueue another UI rewrite.
        if (ReferenceEquals(lastHazardPresentationRoute, route) && lastHazardPresentation == key) return;
        long version = Interlocked.Increment(ref routeHazardNoticeVersion);
        Dispatcher.Dispatch(() =>
        {
            if (version != routeHazardNoticeVersion || !ReferenceEquals(activeRoute, route) ||
                activeDestinationVersion != destinationVersion || safeZoneConfirmed ||
                !_hazardReroutingService.IsCurrentAssessment(assessment)) return;
            lastHazardPresentation = key;
            lastHazardPresentationRoute = route;
            routeHazardNotice = notice;
            routeHazardBlocksGuidance = assessment.IsUnsafe;
            hazardPrefixRoute = assessment.IsUnsafe ? route : null;
            hazardSafePrefixEnd = assessment.SafePrefixEndProgressMeters - RoutePositionObservationPolicy.MotionAllowanceMeters;
            if (routeHazardBlocksGuidance)
            {
                ClearRoadApproachCue();
                SetTurnGuidanceVisible(false);
                if (!IsCurrentHazardPrefix(ARRouteBridge.Current))
                {
                    ARCameraSpatialController.SetRouteRenderingEnabled(false, "clipping route before the reported hazard");
                    ARRouteBridge.Clear();
                }
                if (TryGetObservedDisplayPosition(route, out var position))
                    PublishRegisteredRouteWindow(route, position, ARCameraPoseBridge.CurrentFrame, "HAZARD_SAFE_PREFIX");
            }
            AndroidLog.Info("RescuAR-HazardReroute", $"NAV_HAZARD_STATE trip={destinationVersion}; " +
                $"snapshot={assessment.RetryContext.Snapshot}; unsafe={assessment.IsUnsafe}; " +
                $"destinationExcluded={_hazardReroutingService.IsDestinationExcluded(assessment)}; " +
                $"safePrefixEnd={hazardSafePrefixEnd:F2}; sourceCount={assessment.ActiveHazards.Count}; " +
                $"refreshedAt={assessment.LastRemoteRefreshSuccessUtc:O}; status='{notice}'.");
            RefreshEmergencyStatusBanner();
        });
    }

    private bool TryShowRouteHazardStatus()
    {
        if (!routeHazardBlocksGuidance || routeHazardNotice is null || safeZoneConfirmed) return false;
        ARRoadApproachBridge.Clear();
        routeLocatorIcon.IsVisible = false;
        routeLocatorLabel.Text = routeHazardNotice;
        routeLocatorPanel.IsVisible = !cameraModeStatusBanner.IsVisible;
        SetTurnGuidanceVisible(false);
        return true;
    }

    private bool ApplyRouteHazardStatusBanner()
    {
        if (routeHazardNotice is null) return false;
        cameraModeStatusBanner.BackgroundColor = Color.FromArgb("#FFF4DE");
        cameraModeStatusBanner.Stroke = new SolidColorBrush(Color.FromArgb("#E7B85D"));
        cameraModeStatusTitleLabel.TextColor = Color.FromArgb("#8A4B00");
        cameraModeStatusSubtitleLabel.TextColor = Color.FromArgb("#7B5700");
        cameraModeStatusTitleLabel.Text = "Route hazard reported";
        cameraModeStatusSubtitleLabel.Text = routeHazardNotice;
        cameraModeStatusSubtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
        return true;
    }
}
