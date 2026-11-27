using Microsoft.Maui.Networking;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;
using RescuAR.AR;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;
using RescuAR.Navigation.Timing;
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
    private (RouteResult Route, long Version, RouteHazardCoverage Coverage, bool Online)? lastHazardWatch;
    private volatile bool hazardRecheckAfterRouteOperation;

    private void SubscribeRouteHazards()
    {
        _hazardReroutingService.SnapshotChanged += RefreshRouteHazards;
    }

    private void UnsubscribeRouteHazards()
    {
        _hazardReroutingService.SnapshotChanged -= RefreshRouteHazards;
        lastHazardWatch = null;
        hazardRecheckAfterRouteOperation = false;
    }

    private void RefreshRouteHazards()
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(RefreshRouteHazards);
            return;
        }
        if (!pageIsVisible || cameraMapWindowStopped || safeZoneConfirmed || activeRoute is not { } route ||
            activeDestinationVersion != NavigationDestinationBridge.Current.Version) return;

        bool online = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        _hazardReroutingService.ScheduleRemoteRefreshIfDue(online);
        var status = _hazardReroutingService.GetSnapshotStatus(online);
        var watch = (route, status.Version, status.Coverage, online);
        bool previousRouteOperationEnded = hazardRecheckAfterRouteOperation && !routeRequestInProgress && !dynamicRerouteInProgress;
        if (lastHazardWatch == watch && !previousRouteOperationEnded) return;
        lastHazardWatch = watch;
        if (previousRouteOperationEnded) hazardRecheckAfterRouteOperation = false;

        RouteProgressTracker.ProgressSnapshot progress;
        LocationReading? reading;
        lock (routeProgressFusionSync)
        {
            if (!ReferenceEquals(activeRoute, route)) return;
            progress = _routeProgressTracker.Current;
            reading = latestRouteStartupReading;
        }
        StartHazardRerouteIfNeeded(route, progress.HasProgress ? progress.CommittedProgressMeters : 0,
            reading?.Coordinate);
        RefreshEmergencyStatusBanner();
    }

    private bool StartHazardRerouteIfNeeded(RouteResult route, double progressMeters, GeoCoordinate? origin)
    {
        if (safeZoneConfirmed || !pageIsVisible || cameraMapWindowStopped ||
            !ReferenceEquals(route, activeRoute) || !activeDestinationCoordinate.HasValue ||
            activeDestinationVersion != NavigationDestinationBridge.Current.Version) return false;

        var assessment = _hazardReroutingService.AssessRoute(route, progressMeters,
            Connectivity.Current.NetworkAccess == NetworkAccess.Internet);
        UpdateRouteHazardNotice(route, assessment);
        if (!assessment.IsUnsafe || assessment.PrimaryHazard is not { } hazard) return false;
        if (_hazardReroutingService.IsDestinationExcluded(assessment)) return true;

        if (routeRequestInProgress || dynamicRerouteInProgress)
        {
            hazardRecheckAfterRouteOperation = true;
            return true;
        }

        // A new hazard changes guidance even during a GPS gap. Only a
        // measured fix qualified for this route may request a detour.
        if (origin is not { IsValid: true } candidate) return true;
        lock (routeProgressFusionSync)
        {
            if (latestRouteStartupReading is not { } reading || candidate != reading.Coordinate ||
                !IsQualifiedBackgroundRerouteReading(route, reading)) return true;
        }

        if (!_hazardReroutingService.TryReserveReroute(assessment, out string reservationReason))
        {
            LogDetailedDebug(HazardRerouteLogTag, $"Hazard reroute not repeated yet: {reservationReason}.");
            return true;
        }

        AndroidLog.Warn(HazardRerouteLogTag,
            "HAZARD-DRIVEN REROUTE TRIGGERED: " +
            $"id='{DiagnosticPrivacyPolicy.FormatRouteLabel(hazard.Id)}', " +
            $"distanceAhead={assessment.DistanceAheadMeters:F1} m, hazardsToAvoid={assessment.ActiveHazards.Count}.");
        _ = TryDynamicRerouteAsync(candidate, $"ROUTE_HAZARD:{hazard.Id}",
            hazardsToAvoid: assessment.ActiveHazards, triggeringHazard: hazard, hazardAssessment: assessment);
        return true;
    }

    private string GetRouteHazardCoverageDescription()
    {
        var status = _hazardReroutingService.GetSnapshotStatus(Connectivity.Current.NetworkAccess == NetworkAccess.Internet);
        return status.Coverage switch
        {
            RouteHazardCoverage.Live => $"Known hazards updated {status.RefreshedAt!.Value.ToLocalTime():HH:mm}",
            RouteHazardCoverage.Saved => $"Saved hazards · {status.RefreshedAt!.Value.ToLocalTime():MMM d, HH:mm}",
            RouteHazardCoverage.Expired => "Hazard coverage expired. Check current local reports.",
            _ => "Hazard data unavailable"
        };
    }

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
            hazardRecheckAfterRouteOperation = false;
            RefreshEmergencyStatusBanner();
        });
    }

    private void UpdateRouteHazardNotice(RouteResult route,
        HazardReroutingService.HazardRouteAssessment assessment, bool findingAlternative = false)
    {
        if (!_hazardReroutingService.IsCurrentAssessment(assessment)) return;
        bool destinationExcluded = _hazardReroutingService.IsDestinationExcluded(assessment);
        string? notice = assessment.IsUnsafe
            ? findingAlternative || dynamicRerouteInProgress && lastRerouteResult == "Requesting"
                ? "Finding a safer route. Stop before the reported hazard."
                : destinationExcluded
                    ? "Destination is in a reported hazard area. Choose another safe center."
                : _hazardReroutingService.IsUnavailable(assessment)
                    ? "No safer route found. Stop before the hazard; check the map."
                    : "Hazard on this route. Check the map."
            : assessment.EscapingHazard is not null
                ? "Leaving a reported hazard area. Stay alert." : null;
        long destinationVersion = activeDestinationVersion;
        double safePrefixEnd = assessment.SafePrefixEndProgressMeters - RoutePositionObservationPolicy.MotionAllowanceMeters;
        string key = $"{destinationVersion}|{route.GeometryFingerprint}|{assessment.RetryContext.Snapshot}|{safePrefixEnd:R}|{notice}";
        // An identical 2-second assessment must not enqueue another UI rewrite.
        if (ReferenceEquals(lastHazardPresentationRoute, route) && lastHazardPresentation == key) return;
        long version = Interlocked.Increment(ref routeHazardNoticeVersion);
        void ApplyNotice()
        {
            if (version != routeHazardNoticeVersion || !pageIsVisible || cameraMapWindowStopped ||
                !ReferenceEquals(activeRoute, route) || activeDestinationVersion != destinationVersion ||
                NavigationDestinationBridge.Current.Version != destinationVersion || safeZoneConfirmed ||
                !_hazardReroutingService.IsCurrentAssessment(assessment)) return;
            lastHazardPresentation = key;
            lastHazardPresentationRoute = route;
            routeHazardNotice = notice;
            bool wasBlocked = routeHazardBlocksGuidance;
            routeHazardBlocksGuidance = assessment.IsUnsafe;
            hazardPrefixRoute = assessment.IsUnsafe ? route : null;
            hazardSafePrefixEnd = safePrefixEnd;
            if (routeHazardBlocksGuidance)
            {
                ClearRoadApproachCue();
                SetTurnGuidanceVisible(false);
                CancelCameraInstructionSpeech();
                if (!IsCurrentHazardPrefix(ARRouteBridge.Current))
                {
                    ARCameraSpatialController.SetRouteRenderingEnabled(false, "clipping route before the reported hazard");
                    ARRouteBridge.Clear();
                }
                if (TryGetObservedDisplayPosition(route, out var position))
                    PublishRegisteredRouteWindow(route, position, ARCameraPoseBridge.CurrentFrame, "HAZARD_SAFE_PREFIX");
            }
            else if (wasBlocked)
            {
                TryPublishReadyRouteWindow(route, "HAZARD_CLEARED");
                UpdateTurnGuidance();
            }
            AndroidLog.Info("RescuAR-HazardReroute", $"NAV_HAZARD_STATE trip={destinationVersion}; " +
                $"snapshot={assessment.RetryContext.Snapshot}; unsafe={assessment.IsUnsafe}; " +
                $"destinationExcluded={destinationExcluded}; " +
                $"safePrefixEnd={hazardSafePrefixEnd:F2}; sourceCount={assessment.ActiveHazards.Count}; " +
                $"refreshedAt={assessment.LastRemoteRefreshSuccessUtc:O}; status='{notice}'.");
            RefreshEmergencyStatusBanner();
            RefreshCameraMapNavigation();
        }
        if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(ApplyNotice);
        else ApplyNotice();
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
        string? notice = routeHazardNotice;
        if (activeRoute is not null)
        {
            var coverage = _hazardReroutingService.GetSnapshotStatus(
                Connectivity.Current.NetworkAccess == NetworkAccess.Internet).Coverage;
            if (coverage is RouteHazardCoverage.Expired or RouteHazardCoverage.Unavailable)
            {
                string description = GetRouteHazardCoverageDescription();
                notice = notice is null ? description : $"{notice} {description}";
            }
        }
        if (notice is null) return false;
        cameraModeStatusBanner.BackgroundColor = Color.FromArgb("#FFF4DE");
        cameraModeStatusBanner.Stroke = new SolidColorBrush(Color.FromArgb("#E7B85D"));
        cameraModeStatusTitleLabel.TextColor = Color.FromArgb("#8A4B00");
        cameraModeStatusSubtitleLabel.TextColor = Color.FromArgb("#7B5700");
        cameraModeStatusTitleLabel.Text = routeHazardNotice is null ? "Route hazard coverage" : "Route hazard reported";
        cameraModeStatusSubtitleLabel.Text = notice;
        cameraModeStatusSubtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
        return true;
    }
}
