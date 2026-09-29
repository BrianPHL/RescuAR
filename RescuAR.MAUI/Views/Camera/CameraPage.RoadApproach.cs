using RescuAR.AR;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;
using RescuAR.Navigation.State;
using System.Numerics;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private AStarRoutingService? roadApproachPlanner;
    private RoadApproachCue? roadApproachCue;
    private DateTimeOffset nextRoadApproachRetryAt;
    private sealed record RoadApproachCue(LocationReading Reading, GeoCoordinate Target,
        GeoCoordinate Destination, double Yaw, long Session, bool ReturningToRoute);

    private static bool IsRoadApproachFixUsable(LocationReading? reading) =>
        reading is not null && RouteStartupLocationPolicy.CanPlace(reading.Coordinate,
            reading.AccuracyMeters, reading.Timestamp, DateTimeOffset.UtcNow);

    private void ClearRoadApproachCue()
    {
        lock (routeProgressFusionSync) roadApproachCue = null;
    }

    private async Task UpdateRoadApproachAsync(LocationReading? reading,
        RoadGraph graph, CancellationToken token)
    {
        ClearRoadApproachCue();
        var destination = NavigationDestinationBridge.Current;
        if (!destination.IsAvailable || !IsRoadApproachFixUsable(reading) ||
            currentCameraModuleView != CameraModuleViewMode.ArCamera) return;

        GeoCoordinate? target = null;
        if (pendingInitialRoute is { Points.Count: >= 2 } &&
            pendingInitialDestination == destination.Coordinate)
        {
            GeoCoordinate candidate = pendingInitialRoute.Points[0].Coordinate;
            if (reading!.Coordinate.DistanceTo(candidate) <= 50 &&
                !graph.AccessCrossesMajorRoad(reading.Coordinate, candidate)) target = candidate;
        }
        target ??= (roadApproachPlanner ??= new AStarRoutingService(graph))
            .FindApproachCoordinate(reading!.Coordinate, destination.Coordinate);
        if (!target.HasValue) return;

        long session = ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration;
        if (session <= 0) return;
        var heading = await _headingAlignmentService.CaptureAsync(
            reading!.Coordinate, reading.AltitudeMeters, token);
        token.ThrowIfCancellationRequested();
        if (!heading.HasValue || !heading.Value.IsAvailable || !heading.Value.IsStable ||
            !IsRoadApproachFixUsable(reading) || !pageIsVisible || _arCoreService.IsSessionPaused ||
            currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration != session ||
            !NavigationDestinationBridge.Current.IsAvailable ||
            NavigationDestinationBridge.Current.Coordinate != destination.Coordinate) return;

        lock (routeProgressFusionSync)
            roadApproachCue = new(reading, target.Value, destination.Coordinate,
                heading.Value.MapToArYawDegrees, session, false);
        routeStartupFailureMessage = "Approach the mapped road — check access";
    }

    private bool TrySetRoadRecoveryCue(RouteProgressTracker.RouteProgressUpdate update)
    {
        var frame = ARCameraPoseBridge.CurrentFrame;
        var destination = NavigationDestinationBridge.Current;
        LocationReading reading;
        lock (routeProgressFusionSync)
            reading = new(update.GpsCoordinate, update.AccuracyMeters, null, null, null,
                latestGpsTimestampForRouting ?? DateTimeOffset.MinValue);
        if (!recoveryConnectorVerified || !destination.IsAvailable ||
            !RouteCorridorPolicy.CanPublishRecoveryConnector(update.AccuracyMeters,
                update.CrossTrackErrorMeters, update.CorridorRadiusMeters, update.MatchConfidence) ||
            !IsRoadApproachFixUsable(reading) || !update.SnappedCoordinate.IsValid ||
            !_headingAlignmentService.HasSessionCalibration ||
            lastHeadingAlignment is not { IsAvailable: true, IsStable: true } ||
            !frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking ||
            arrivalRoadGraph is null ||
            arrivalRoadGraph.AccessCrossesMajorRoad(update.GpsCoordinate, update.SnappedCoordinate))
            return false;
        lock (routeProgressFusionSync)
            roadApproachCue = new(reading, update.SnappedCoordinate, destination.Coordinate,
                activeMapToArYawDegrees, frame.Generation.SessionGeneration, true);
        routeStartupFailureMessage = "Return to the mapped route — check access";
        return true;
    }

    private bool TryShowRoadApproachCue()
    {
        RoadApproachCue? cue;
        lock (routeProgressFusionSync) cue = roadApproachCue;
        var destination = NavigationDestinationBridge.Current;
        var frame = ARCameraPoseBridge.CurrentFrame;
        if (cue is null || !pageIsVisible || safeZoneConfirmed || emergencyAdvisoryVisible ||
            dynamicRerouteInProgress || _arCoreService.IsSessionPaused ||
            currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            !destination.IsAvailable || destination.Coordinate != cue.Destination ||
            (activeRoute is not null && (!cue.ReturningToRoute || !recoveryConnectorVerified)) ||
            !IsRoadApproachFixUsable(cue.Reading) || !frame.IsFresh ||
            !frame.IsTracking || !frame.Pose.IsTracking ||
            frame.Generation.SessionGeneration != cue.Session) return false;

        var rotation = new Quaternion(frame.Pose.RotationX, frame.Pose.RotationY,
            frame.Pose.RotationZ, frame.Pose.RotationW);
        if (!RoadApproachCuePolicy.TryGetAngle(cue.Reading.Coordinate, cue.Target,
                cue.Yaw, rotation, out double angle, out double distance)) return false;
        routeLocatorIcon.Source = "lucide_arrow_up_teal.png";
        routeLocatorIcon.Rotation = angle;
        routeLocatorLabel.Text = cue.ReturningToRoute
            ? $"Return to mapped route (~{distance:0} m). Check access."
            : $"Approach mapped road (~{distance:0} m). Check access.";
        routeLocatorPanel.IsVisible = true;
        turnGuidancePanel.IsVisible = false;
        return true;
    }

    private void RetryRouteFromRoadApproachIfReady(CancellationToken token)
    {
        RoadApproachCue? cue;
        lock (routeProgressFusionSync) cue = roadApproachCue;
        if (cue is null || pendingInitialRoute is not null ||
            !IsRoadApproachFixUsable(cue.Reading) ||
            cue.Reading.Coordinate.DistanceTo(cue.Target) > 20 ||
            DateTimeOffset.UtcNow < nextRoadApproachRetryAt) return;
        nextRoadApproachRetryAt = DateTimeOffset.UtcNow.AddSeconds(10);
        Dispatcher.Dispatch(() =>
        {
            if (!token.IsCancellationRequested && pageIsVisible && !safeZoneConfirmed &&
                !routeRequestInProgress && activeRoute is null &&
                currentCameraModuleView == CameraModuleViewMode.ArCamera &&
                NavigationDestinationBridge.Current.IsAvailable &&
                NavigationDestinationBridge.Current.Coordinate == cue.Destination)
                StartRouteRequestIfPossible();
        });
    }
}
