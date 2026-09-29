using RescuAR.AR;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;
using RescuAR.Navigation.State;
using RescuAR.Diagnostics;
using System.Numerics;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private AStarRoutingService? roadApproachPlanner;
    private RoadApproachCue? roadApproachCue;
    private DateTimeOffset nextRoadApproachRetryAt;
    private bool initialRoadApproachPending;
    private sealed record RoadApproachCue(LocationReading Reading, GeoCoordinate Target,
        GeoCoordinate Destination, double Yaw, long Session, bool ReturningToRoute,
        bool ConnectsToDestination);

    private static bool IsRoadApproachFixUsable(LocationReading? reading) =>
        reading is not null && RouteStartupLocationPolicy.CanPlace(reading.Coordinate,
            reading.AccuracyMeters, reading.Timestamp, DateTimeOffset.UtcNow);

    private void ClearRoadApproachCue()
    {
        lock (routeProgressFusionSync) roadApproachCue = null;
        ARRoadApproachBridge.Clear();
    }

    private AStarRoutingService.RoadApproachTarget? FindRoadAccess(
        LocationReading reading, RoadGraph graph, GeoCoordinate destination)
    {
        if (pendingInitialRoute is { Points.Count: >= 2 } && pendingInitialDestination == destination)
        {
            GeoCoordinate first = pendingInitialRoute.Points[0].Coordinate;
            if (reading.Coordinate.DistanceTo(first) <= 50 &&
                !graph.AccessCrossesMajorRoad(reading.Coordinate, first)) return new(first, true);
        }
        return (roadApproachPlanner ??= new AStarRoutingService(graph))
            .FindRoadApproachTarget(reading.Coordinate, destination);
    }

    private async Task UpdateRoadApproachAsync(LocationReading? reading,
        RoadGraph graph, CancellationToken token)
    {
        ClearRoadApproachCue();
        var destination = NavigationDestinationBridge.Current;
        if (!destination.IsAvailable || !IsRoadApproachFixUsable(reading) ||
            currentCameraModuleView != CameraModuleViewMode.ArCamera) return;
        var target = FindRoadAccess(reading!, graph, destination.Coordinate);
        if (!target.HasValue)
        {
            routeStartupFailureMessage = "No accessible mapped road within 50 m — inspect the road data";
            AndroidLog.Info("RescuAR-Routing", "ROAD APPROACH: no barrier-free pedestrian edge within 50m.");
            return;
        }
        long session = ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration;
        if (session <= 0) return;
        var heading = await _headingAlignmentService.CaptureAsync(
            reading!.Coordinate, reading.AltitudeMeters, token);
        if (!IsRoadApproachFixUsable(reading))
        {
            reading = await TryGetRouteLocationAsync(token);
            if (!IsRoadApproachFixUsable(reading)) return;
            target = FindRoadAccess(reading!, graph, destination.Coordinate);
            if (!target.HasValue) return;
        }
        token.ThrowIfCancellationRequested();
        if (!heading.HasValue || !heading.Value.IsAvailable || !heading.Value.IsStable ||
            !pageIsVisible || _arCoreService.IsSessionPaused ||
            currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration != session ||
            !NavigationDestinationBridge.Current.IsAvailable ||
            NavigationDestinationBridge.Current.Coordinate != destination.Coordinate) return;
        lock (routeProgressFusionSync)
        {
            latestRouteStartupReading = reading;
            roadApproachCue = new(reading!, target.Value.Coordinate, destination.Coordinate,
                heading.Value.MapToArYawDegrees, session, false, target.Value.ConnectsToDestination);
        }
        routeStartupFailureMessage = target.Value.ConnectsToDestination
            ? "Approach the mapped road — check access"
            : "Approach the nearest mapped road — route connection pending";
        AndroidLog.Info("RescuAR-Routing",
            $"ROAD APPROACH: distance={reading!.Coordinate.DistanceTo(target.Value.Coordinate):F1}m, connected={target.Value.ConnectsToDestination}.");
    }

    private bool TrySetRouteRoadApproachCue(RouteProgressTracker.RouteProgressUpdate update,
        bool returningToRoute)
    {
        var frame = ARCameraPoseBridge.CurrentFrame;
        var destination = NavigationDestinationBridge.Current;
        LocationReading reading;
        lock (routeProgressFusionSync)
            reading = new(update.GpsCoordinate, update.AccuracyMeters, null, null, null,
                latestGpsTimestampForRouting ?? DateTimeOffset.MinValue);
        if ((returningToRoute && (!recoveryConnectorVerified ||
                !RouteCorridorPolicy.CanPublishRecoveryConnector(update.AccuracyMeters,
                    update.CrossTrackErrorMeters, update.CorridorRadiusMeters, update.MatchConfidence))) ||
            !destination.IsAvailable || update.MatchConfidence < RouteMatchConfidence.Medium ||
            !IsRoadApproachFixUsable(reading) || !update.SnappedCoordinate.IsValid ||
            !_headingAlignmentService.HasSessionCalibration ||
            lastHeadingAlignment is not { IsAvailable: true, IsStable: true } ||
            !frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking ||
            arrivalRoadGraph is null ||
            arrivalRoadGraph.AccessCrossesMajorRoad(update.GpsCoordinate, update.SnappedCoordinate) ||
            !RoadApproachCuePolicy.TryGetDirection(reading.Coordinate, update.SnappedCoordinate,
                activeMapToArYawDegrees, out _, out _)) return false;
        lock (routeProgressFusionSync)
            roadApproachCue = new(reading, update.SnappedCoordinate, destination.Coordinate,
                activeMapToArYawDegrees, frame.Generation.SessionGeneration, returningToRoute, true);
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
            (cue.ReturningToRoute && !recoveryConnectorVerified) ||
            !IsRoadApproachFixUsable(cue.Reading) || !frame.IsFresh ||
            !frame.IsTracking || !frame.Pose.IsTracking ||
            frame.Generation.SessionGeneration != cue.Session)
        {
            ARRoadApproachBridge.Clear();
            return false;
        }
        var rotation = new Quaternion(frame.Pose.RotationX, frame.Pose.RotationY,
            frame.Pose.RotationZ, frame.Pose.RotationW);
        if (!RoadApproachCuePolicy.TryGetAngle(cue.Reading.Coordinate, cue.Target,
                cue.Yaw, rotation, out double angle, out double distance))
        {
            ARRoadApproachBridge.Clear();
            return false;
        }
        ARRoadApproachBridge.Publish(cue.Reading.Coordinate, cue.Target, cue.Yaw,
            cue.Reading.AccuracyMeters, cue.Reading.Timestamp, cue.Session);
        routeLocatorIcon.Source = "lucide_arrow_up_teal.png";
        routeLocatorIcon.Rotation = angle;
        routeLocatorLabel.Text = cue.ReturningToRoute
            ? $"Return to mapped route (~{distance:0} m). Check access."
            : cue.ConnectsToDestination
                ? $"Approach mapped road (~{distance:0} m). Check access."
                : $"Nearest mapped road (~{distance:0} m). Route connection pending.";
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
