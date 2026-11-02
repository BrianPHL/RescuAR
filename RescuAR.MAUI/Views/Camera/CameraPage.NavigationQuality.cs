using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Timing;
using RescuAR.Navigation.State;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private static bool ForceOfflineRouterForFieldTest()
    {
#if ANDROID && RESCUAR_DIAGNOSTICS
        try
        {
            return string.Equals(Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Intent?.GetStringExtra(
                "rescuar.navigation.provider"), "astar", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
#else
        return false;
#endif
    }

    private readonly NavigationRecoveryPolicy navigationRecovery = new();
    private string? lastNavigationQuality;
    private long lastNavigationQualityAt;

    private ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot PresentNavigationQuality(
        ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot confidence,
        ARRouteBridge.RouteSnapshot window, ARCameraPoseBridge.SpatialSnapshot frame,
        bool headingTrusted, bool gpsFresh)
    {
        if (!pageIsVisible || cameraMapWindowStopped || currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            !NavigationDestinationBridge.Current.IsAvailable || safeZoneConfirmed)
        {
            navigationRecovery.Reset();
            return confidence;
        }
        var blockers = NavigationBlockers.None;
        if (activeRoute is null) blockers |= NavigationBlockers.Route;
        if (!frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking) blockers |= NavigationBlockers.Tracking;
        if (!gpsFresh || lastGpsConfidence < GpsPdrFusionPolicy.GpsConfidence.Medium) blockers |= NavigationBlockers.Gps;
        if (!headingTrusted) blockers |= NavigationBlockers.Heading;
        if (!frame.Anchor.IsAvailable) blockers |= NavigationBlockers.Ground;
        if (positionObservationHeld || arPositionObservationHeld || _gpsPdrFusionPolicy.IsRouteIdentitySuspended) blockers |= NavigationBlockers.Position;
        if (!window.IsAvailable || _routeDisplayProgressPolicy.RegistrationSupportedAt is null)
            blockers |= NavigationBlockers.Registration;
        if (!confidence.AllowsRouteGeometry && blockers == NavigationBlockers.None) blockers |= NavigationBlockers.Geometry;
        var recovery = navigationRecovery.Evaluate(activeDestinationVersion,
            confidence.AllowsRouteGeometry ? NavigationBlockers.None : blockers, Environment.TickCount64);
        if (!confidence.AllowsRouteGeometry && recovery.RecommendMap && !routeHazardBlocksGuidance && !lowLightFallbackActive)
            confidence = confidence with { DisplayMessage = recovery.Message };
        // GPS and compass repeatability do not certify a surveyed walking line.
        if (confidence.AllowsRouteGeometry && confidence.State == ARGuidanceConfidencePolicy.GuidanceConfidenceState.Full)
            confidence = confidence with { State = ARGuidanceConfidencePolicy.GuidanceConfidenceState.Degraded,
                DisplayMessage = "Approximate route — follow the street and marked walking access" };

        string signature = $"{activeDestinationVersion}:{confidence.AllowsRouteGeometry}:{blockers}:{frame.TrackingEpoch}:{frame.WorldFrame}";
        long monoNow = Environment.TickCount64;
        if (signature != lastNavigationQuality || monoNow - lastNavigationQualityAt >= 5000)
        {
            lastNavigationQuality = signature;
            lastNavigationQualityAt = monoNow;
            var reading = latestRouteStartupReading;
            long skew = -1;
            bool paired = reading?.AcquisitionMonotonicMilliseconds is { } acquired &&
                ARCameraPoseBridge.TryGetObservationFrame(acquired, monoNow, out _, out skew);
            var rendered = ARRouteRenderer.Visibility;
            AndroidLog.Info("RescuAR-NavigationQuality",
                $"NAV_QUALITY trip={activeDestinationVersion}; algorithm='{activeRoute?.Algorithm ?? "none"}'; " +
                $"allowed={confidence.AllowsRouteGeometry}; blockers={blockers}; recoverySec={recovery.Seconds:F1}; " +
                $"gpsProvider={reading?.Provider ?? "none"}; gpsAccuracy={reading?.AccuracyMeters:F2}; " +
                $"gpsAgeMs={(reading is null ? -1 : (NavigationClock.UtcNow - reading.ObservationTime).TotalMilliseconds):F0}; " +
                $"nativeClock={reading?.AcquisitionMonotonicMilliseconds is not null}; paired={paired}; poseSkewMs={skew}; " +
                $"headingSource={_headingAlignmentService.LastResult?.Source ?? "none"}; headingTrusted={headingTrusted}; " +
                $"registration={_routeDisplayProgressPolicy.LastRegistrationDecision.State}; " +
                $"geographicHeld={positionObservationHeld}; arObservationHeld={arPositionObservationHeld}; " +
                $"epoch={frame.TrackingEpoch}; world={frame.WorldFrame}; ground={frame.Anchor.ReferenceGeneration}; " +
                $"renderRoot={rendered.RootEnabled}; enabledSegments={rendered.EnabledSegments}; " +
                $"depthHidden={rendered.DepthHiddenSegments}; inFront={rendered.InFrontSegments}; " +
                $"viewport={rendered.ViewportMidpoints}; routeVersion={window.Version}.");
        }
        return confidence;
    }
}
