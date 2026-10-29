using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot ResolveMldPresentation(
        ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot confidence, ARRouteBridge.RouteSnapshot window,
        ARCameraPoseBridge.SpatialSnapshot frame)
    {
        if (activeRoute is not { } route || !IsMldRoute(route)) return confidence;
        if (positionObservationHeld)
            confidence = confidence with { AllowsRouteGeometry = false,
                State = ARGuidanceConfidencePolicy.GuidanceConfidenceState.Recovery,
                DisplayMessage = "Position uncertain — follow text guidance" };
        else if (confidence.AllowsRouteGeometry)
            confidence = confidence with {
                State = ARGuidanceConfidencePolicy.GuidanceConfidenceState.Degraded,
                DisplayMessage = "Approximate route placement — GPS accuracy applies" };
        var decision = MldGuidanceBridge.Publish(route, activeDestinationVersion,
            _headingAlignmentService.CalibrationRevision, window, frame, confidence.AllowsRouteGeometry &&
                pageIsVisible && currentCameraModuleView == CameraModuleViewMode.ArCamera,
            confidence.DisplayMessage, routeHazardBlocksGuidance, safeZoneConfirmed);
        return confidence with { AllowsRouteGeometry = decision.AllowsPath, DisplayMessage = decision.Message,
            State = confidence.AllowsRouteGeometry && !decision.AllowsPath
                ? ARGuidanceConfidencePolicy.GuidanceConfidenceState.Recovery : confidence.State };
    }

    private void BindMldPublishedWindow(RouteResult route, ARCameraPoseBridge.SpatialSnapshot frame)
    {
        if (!IsMldRoute(route)) return;
        MldGuidanceBridge.RebindPublishedWindow(route, activeDestinationVersion,
            _headingAlignmentService.CalibrationRevision, ARRouteBridge.Current, frame,
            routeHazardBlocksGuidance, routeHazardNotice);
    }

    private bool MldPathIsCurrent() => activeRoute is { } route && IsMldRoute(route) &&
        MldGuidanceBridge.CanRenderPath(ARRouteBridge.Current, ARCameraPoseBridge.CurrentFrame,
            NavigationDestinationBridge.Current.Version);

    // Computed MLD guidance uses the mapped window and its route chevrons.
    // An independent nearest-road/destination arrow must not replace it.
    private bool TryShowMldRecoveryState()
    {
        if (activeRoute is not { } route || !IsMldRoute(route)) return false;
        ARRoadApproachBridge.Clear("computed MLD route owns navigation cues");
        if (MldPathIsCurrent()) return false;
        routeLocatorIcon.IsVisible = false;
        routeLocatorPanel.IsVisible = false;
        return true;
    }

    private void TryPublishReadyMldWindow(RouteResult route, string reason)
    {
        if (!IsMldRoute(route)) return;
        lock (routeProgressFusionSync)
        {
            var accepted = acceptedGpsPosition;
            if (!ReferenceEquals(activeRoute, route) || safeZoneConfirmed || dynamicRerouteInProgress ||
                positionObservationHeld || _gpsPdrFusionPolicy.IsRouteIdentitySuspended ||
                !HasCurrentRouteHeading() || latestRouteStartupReading is not { } reading ||
                (accepted is null || !ReferenceEquals(accepted.Route, route) ||
                    accepted.Reading.Timestamp != reading.Timestamp || latestGpsTimestampForRouting != reading.Timestamp) ||
                    (lastGpsConfidence < GpsPdrFusionPolicy.GpsConfidence.Medium || lastGpsReliabilityWeight < .35))
                return;
            if (accepted is not null)
            {
                if (verifiedPosition?.Motion is { } old && GetTrackedRouteMotion() is { } current &&
                    (!old.IsContinuousWith(current) ||
                     Math.Abs(Math.IEEERemainder(old.YawDegrees - current.YawDegrees, 360)) >= .001))
                {
                    _routeDisplayProgressPolicy.Reset();
                    verifiedPosition = null;
                    lastRegisteredPublication = null;
                }
                ObserveVerifiedGpsPosition(route, accepted.Update, accepted.Reading);
                if (verifiedPosition is not { } verified || verified.At != accepted.Reading.Timestamp) return;
            }
            // Observe-only matching cannot add a second progress/arrival/entry
            // sample. Existing acquisition pairing, age, precision, registration,
            // access, tracking and hazard gates still own publication eligibility.
            var match = _routeProgressTracker.Update(reading.Coordinate, reading.AccuracyMeters,
                reading.CourseDegrees, reading.SpeedMetersPerSecond, observeOnly: true);
            long previousWindow = ARRouteBridge.Current.Version;
            bool published = arRouteVisualMode == ArRouteVisualMode.RoadFollowingLong
                ? TryPublishMovingRouteWindow(route, match, reason)
                : TryPublishApproachToRouteVisual(route, match, reason);
            if (previousWindow != ARRouteBridge.Current.Version)
                LogDetailedDebug(ProgressLogTag, $"NAV_MLD_READY sample={reading.Timestamp.ToUnixTimeMilliseconds()}; " +
                $"trip={activeDestinationVersion}; published={published}; source='{reason}'.");
        }
    }
}
