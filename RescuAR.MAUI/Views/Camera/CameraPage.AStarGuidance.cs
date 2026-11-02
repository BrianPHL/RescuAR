using RescuAR.AR;
using RescuAR.Diagnostics;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private void TryResetInheritedAStarHeading()
    {
        if (!pageIsVisible || cameraMapWindowStopped || safeZoneConfirmed || dynamicRerouteInProgress ||
            routeRequestInProgress || currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            !_arCoreService.IsInitialized || _arCoreService.IsSessionPaused ||
            activeRoute is not { } route || navigationSessionStartedAt is not { } startedAt)
            return;

        lock (routeProgressFusionSync)
        {
            if (!ReferenceEquals(activeRoute, route) || navigationSessionStartedAt != startedAt ||
                !CanApplyRouteRequest(activeDestinationVersion, CancellationToken.None)) return;
            var inherited = _headingAlignmentService.LastResult;
            if (!_headingAlignmentService.TryPrepareForAStarTrip(route, startedAt,
                    routeStartupPublishedVersion >= 0)) return;

            // Keep the accepted geographic trip, voice, progress and arrival
            // evidence. Only the old calibration's AR display references expire.
            lastHeadingAlignment = null;
            _headingRevalidationPolicy.Reset("A* first-window heading acquisition");
            _pdrHeadingSmoother.Reset();
            _routeDisplayProgressPolicy.Reset();
            verifiedPosition = null;
            lastRegisteredPublication = null;
            _verifiedRouteWindowHoldPolicy.Reset();
            approachRoutePreviewPublished = false;
            ARRouteBridge.Clear();
            ARCameraSpatialController.SetRouteRenderingEnabled(false,
                "A* is acquiring a fresh heading for the new trip");
            nextHeadingRetryAt = DateTimeOffset.UtcNow;

            AndroidLog.Info(HeadingLogTag,
                $"NAV_ASTAR_HEADING_REACQUIRE trip={activeDestinationVersion}; " +
                $"previousSource={inherited?.Source}; previousCalibrationAt={inherited?.Timestamp:O}; " +
                $"tripStartedAt={startedAt:O}; geographicProgressRetained=True.");
        }
    }
}
