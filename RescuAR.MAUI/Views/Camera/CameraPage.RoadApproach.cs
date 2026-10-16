using RescuAR.MAUI.Services.Navigation;
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
    private readonly RoadEntryConfirmationPolicy roadEntryConfirmation = new();
    private Task? roadApproachUpdateTask;
    private long roadApproachEpoch;
    private sealed record RoadApproachCue(LocationReading Reading, GeoCoordinate Target,
        GeoCoordinate Destination, double Yaw, long Session, bool ReturningToRoute,
        bool ConnectsToDestination);

    private static bool IsRoadApproachFixUsable(LocationReading? reading) =>
        reading is not null && RouteStartupLocationPolicy.CanPlace(reading.Coordinate,
            reading.AccuracyMeters, reading.Timestamp, DateTimeOffset.UtcNow);

    private void ClearRoadApproachCue()
    {
        lock (routeProgressFusionSync)
        {
            roadApproachEpoch++;
            roadApproachCue = null;
        }
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

    // Heading sampling must not hold up fresh GPS acquisition. One observed
    // task owns calibration; a valid existing cue remains until its fix expires.
    private void BeginRoadApproachUpdate(LocationReading? reading, RoadGraph graph,
        CancellationToken token)
    {
        if (roadApproachUpdateTask is { IsCompleted: false }) return;
        roadApproachUpdateTask = ObserveRoadApproachUpdateAsync(reading, graph, token);
    }

    private async Task ObserveRoadApproachUpdateAsync(LocationReading? reading,
        RoadGraph graph, CancellationToken token)
    {
        try { await UpdateRoadApproachAsync(reading, graph, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AndroidLog.Warn("RescuAR-Routing", "Road approach update deferred: " +
                DiagnosticPrivacyPolicy.FormatException(exception));
        }
    }

    private async Task UpdateRoadApproachAsync(LocationReading? reading,
        RoadGraph graph, CancellationToken token,
        ArHeadingAlignmentService.HeadingAlignmentResult? existingHeading = null,
        bool captureHeading = true)
    {
        token.ThrowIfCancellationRequested();
        var destination = NavigationDestinationBridge.Current;
        if (!destination.IsAvailable || currentCameraModuleView != CameraModuleViewMode.ArCamera)
        {
            ClearRoadApproachCue();
            return;
        }
        if (!IsRoadApproachFixUsable(reading)) return;
        long session = ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration;
        long epoch;
        lock (routeProgressFusionSync) epoch = roadApproachEpoch;
        bool noRouteAtStart = activeRoute is null;
        if (session <= 0) return;
        var heading = existingHeading ?? (captureHeading
            ? await _headingAlignmentService.CaptureAsync(reading!.Coordinate,
                reading.AltitudeMeters, token) : null);
        token.ThrowIfCancellationRequested();
        if (!heading.HasValue || !heading.Value.IsAvailable || !heading.Value.IsStable ||
            heading.Value.SessionGeneration != session ||
            !pageIsVisible || _arCoreService.IsSessionPaused ||
            currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration != session ||
            !NavigationDestinationBridge.Current.IsAvailable ||
            NavigationDestinationBridge.Current.Coordinate != destination.Coordinate ||
            (noRouteAtStart && activeRoute is not null)) return;
        // The GPS loop can produce a newer fix while the heading is sampling.
        lock (routeProgressFusionSync)
        {
            if (epoch != roadApproachEpoch) return;
            if (IsRoadApproachFixUsable(latestRouteStartupReading) &&
                latestRouteStartupReading!.Timestamp > reading!.Timestamp)
                reading = latestRouteStartupReading;
        }
        if (!IsRoadApproachFixUsable(reading)) return;
        var target = FindRoadAccess(reading!, graph, destination.Coordinate);
        if (!target.HasValue)
        {
            ClearRoadApproachCue();
            routeStartupFailureMessage = "No accessible mapped road within 50 m - inspect the road data";
            AndroidLog.Info("RescuAR-Routing", "ROAD APPROACH: no barrier-free pedestrian edge within 50m.");
            return;
        }
        lock (routeProgressFusionSync)
        {
            if (epoch != roadApproachEpoch) return;
            latestRouteStartupReading = reading;
            roadApproachCue = new(reading!, target.Value.Coordinate, destination.Coordinate,
                heading.Value.MapToArYawDegrees, session, false, target.Value.ConnectsToDestination);
        }
        routeStartupFailureMessage = target.Value.ConnectsToDestination
            ? "Approach the mapped road - check access"
            : "Approach the nearest mapped road - route connection pending";
        AndroidLog.Info("RescuAR-Routing",
            $"ROAD APPROACH: distance={reading!.Coordinate.DistanceTo(target.Value.Coordinate):F1}m, connected={target.Value.ConnectsToDestination}, roadComponentNodes={target.Value.RoadComponentNodeCount}, destinationComponents={target.Value.DestinationComponentCount}.");
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
        // A verified return arrow remains useful inside the GPS uncertainty
        // corridor until road entry is confirmed. It is never a walking connector.
        if ((returningToRoute && !recoveryConnectorVerified) ||
            !destination.IsAvailable || update.MatchConfidence < RouteMatchConfidence.Medium ||
            !IsRoadApproachFixUsable(reading) || !update.SnappedCoordinate.IsValid ||
            !_headingAlignmentService.HasSessionCalibration ||
            lastHeadingAlignment is not { IsAvailable: true, IsStable: true } ||
            lastHeadingAlignment.Value.SessionGeneration != frame.Generation.SessionGeneration ||
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
        bool computedRouteReady = pendingInitialRoute is not null &&
            pendingInitialDestination == cue?.Destination &&
            _headingAlignmentService.LastResult is { IsAvailable: true, IsStable: true };
        if (cue is null || !IsRoadApproachFixUsable(cue.Reading) ||
            (pendingInitialRoute is not null && !computedRouteReady) ||
            (!computedRouteReady && cue.Reading.Coordinate.DistanceTo(cue.Target) > 20) ||
            DateTimeOffset.UtcNow < (computedRouteReady ? nextHeadingRetryAt : nextRoadApproachRetryAt)) return;
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
