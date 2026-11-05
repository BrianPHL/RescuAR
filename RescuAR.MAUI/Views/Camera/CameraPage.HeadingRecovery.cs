using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Timing;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private void TryResetExpiredNavigationHeading()
    {
        if (!pageIsVisible || cameraMapWindowStopped || safeZoneConfirmed || dynamicRerouteInProgress ||
            routeRequestInProgress || currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            !_arCoreService.IsInitialized || _arCoreService.IsSessionPaused ||
            activeRoute is not { } route || navigationSessionStartedAt is not { } startedAt) return;

        lock (routeProgressFusionSync)
        {
            if (!ReferenceEquals(activeRoute, route) || navigationSessionStartedAt != startedAt ||
                !CanApplyRouteRequest(activeDestinationVersion, CancellationToken.None)) return;
            var previous = _headingAlignmentService.LastResult;
            if (!_headingAlignmentService.TryPrepareForNavigationRoute(route, startedAt,
                    routeStartupPublishedVersion >= 0)) return;
            lastHeadingAlignment = null;
            _headingRevalidationPolicy.Reset("navigation frame heading acquisition");
            _pdrHeadingSmoother.Reset();
            _routeDisplayProgressPolicy.Reset();
            LogReplayPolicyReset("navigation heading reacquisition", registration: true);
            verifiedPosition = null;
            lastRegisteredPublication = null;
            _verifiedRouteWindowHoldPolicy.Reset();
            approachRoutePreviewPublished = false;
            MldGuidanceBridge.Suppress("navigation heading acquisition");
            ARRouteBridge.Clear();
            ARCameraSpatialController.SetRouteRenderingEnabled(false, "acquiring a fresh navigation heading");
            nextHeadingRetryAt = NavigationClock.UtcNow;
            AndroidLog.Info(HeadingLogTag,
                $"NAV_HEADING_REACQUIRE trip={activeDestinationVersion}; algorithm='{route.Algorithm}'; " +
                $"previousSource={previous?.Source}; previousCalibrationAt={previous?.Timestamp:O}; " +
                $"tripStartedAt={startedAt:O}; geographicProgressRetained=True.");
        }
    }
}
