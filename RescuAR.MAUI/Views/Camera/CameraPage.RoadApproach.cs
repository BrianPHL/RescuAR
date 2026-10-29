using RescuAR.MAUI.Services.Navigation;
using RescuAR.AR;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Guidance;
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
    private bool approachRoutePreviewPublished;
    private readonly RoadEntryConfirmationPolicy roadEntryConfirmation = new();

    private bool CompleteApproachAnchorRecovery(ARCameraPoseBridge.SpatialSnapshot frame)
    {
        var action = RouteAnchorRecoveryPolicy.Evaluate(
            arRouteVisualMode == ArRouteVisualMode.RoadFollowingLong,
            frame.IsFresh && frame.IsTracking && frame.Pose.IsTracking,
            frame.Anchor.IsAvailable && frame.Anchor.Trust != ARGroundTrust.None &&
                frame.Anchor.GroundState.RenderGeneration == frame.Generation);
        if (action != RouteAnchorRecoveryAction.DiscardPreview) return false;
        if (TryRetainWindowAcrossGroundHandoff(frame)) return true;
        ARRouteBridge.Clear();
        approachRoutePreviewPublished = false;
        ClearRoadApproachCue();
        AndroidLog.Info("RescuAR-AnchorRecovery",
            "Tracked anchor handoff completed during route approach; stale preview cleared. " +
            "The next fresh, access-checked GPS observation may publish mapped geometry.");
        return true;
    }
    private Task? roadApproachUpdateTask;
    private long roadApproachEpoch;
    private sealed record RoadApproachCue(LocationReading Reading, GeoCoordinate Target,
        GeoCoordinate Destination, double Yaw, long Session, bool ReturningToRoute,
        bool ConnectsToDestination, ARRoadApproachBridge.Ownership Owner);

    private ARRoadApproachBridge.Ownership CurrentCueOwnership(string role) =>
        new(NavigationDestinationBridge.Current.Version, activeRoute?.GeometryFingerprint ?? "ACCESS",
            _headingAlignmentService.CalibrationRevision, ARCameraPoseBridge.CurrentFrame.TrackingEpoch,
            ARCameraPoseBridge.CurrentFrame.WorldFrame, role);

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
        LocationReading reading, RoadGraph graph, GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        if (pendingInitialRoute is { Points.Count: >= 2 } && pendingInitialDestination == destination)
        {
            GeoCoordinate first = pendingInitialRoute.Points[0].Coordinate;
            if (reading.Coordinate.DistanceTo(first) <= 50 &&
                !graph.AccessCrossesMajorRoad(reading.Coordinate, first)) return new(first, true);
        }
        return (roadApproachPlanner ??= new AStarRoutingService(graph))
            .FindRoadApproachTarget(reading.Coordinate, destination, cancellationToken);
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
        var requestedOwner = CurrentCueOwnership("ACCESS_ESTIMATE");
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
            CurrentCueOwnership("ACCESS_ESTIMATE") != requestedOwner ||
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
        var target = await Task.Run(() => FindRoadAccess(reading!, graph, destination.Coordinate, token), token);
        token.ThrowIfCancellationRequested();
        if (!pageIsVisible || _arCoreService.IsSessionPaused ||
            ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration != session ||
            !NavigationDestinationBridge.Current.IsAvailable ||
            NavigationDestinationBridge.Current.Coordinate != destination.Coordinate ||
            CurrentCueOwnership("ACCESS_ESTIMATE") != requestedOwner ||
            (noRouteAtStart && activeRoute is not null)) return;
        if (!target.HasValue)
        {
            ClearRoadApproachCue();
            routeStartupFailureMessage = "Road guidance unavailable here - use the map and check local access";
            AndroidLog.Info("RescuAR-Routing", "ROAD APPROACH: no barrier-free pedestrian edge within 50m.");
            return;
        }
        lock (routeProgressFusionSync)
        {
            if (epoch != roadApproachEpoch) return;
            latestRouteStartupReading = reading;
            roadApproachCue = new(reading!, target.Value.Coordinate, destination.Coordinate,
                heading.Value.MapToArYawDegrees, session, false, target.Value.ConnectsToDestination, requestedOwner);
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
        if (activeRoute is { } computed && IsMldRoute(computed)) return false;
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
            !HasCurrentRouteHeading() || routeHazardBlocksGuidance ||
            lastHeadingAlignment is not { IsAvailable: true, IsStable: true } ||
            lastHeadingAlignment.Value.SessionGeneration != frame.Generation.SessionGeneration ||
            !frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking ||
            arrivalRoadGraph is null ||
            arrivalRoadGraph.AccessCrossesMajorRoad(update.GpsCoordinate, update.SnappedCoordinate) ||
            reading.Coordinate.DistanceTo(update.SnappedCoordinate) > 50) return false;
        lock (routeProgressFusionSync)
            roadApproachCue = new(reading, update.SnappedCoordinate, destination.Coordinate,
                activeMapToArYawDegrees, frame.Generation.SessionGeneration, returningToRoute, true,
                CurrentCueOwnership("MAPPED_TARGET"));
        return true;
    }

    private bool TryShowArrivalStatus()
    {
        var destination = NavigationDestinationBridge.Current;
        var decision = lastSafeZoneDecision;
        if (!HasArrivalNoticePriority())
        {
            arrivalNoticeCategory = "HIDDEN";
            LogNavigationUiState();
            return false;
        }
        LocationReading? reading;
        lock (routeProgressFusionSync) reading = latestRouteStartupReading;
        TimeSpan age = reading is null ? TimeSpan.MaxValue : DateTimeOffset.UtcNow - reading.Timestamp;
        bool fresh = age >= TimeSpan.FromSeconds(-2) && age <= TimeSpan.FromSeconds(10);
        if (!fresh || reading is null) return false;
        routeLocatorIcon.IsVisible = false;
        bool uncertain = positionObservationHeld || decision.IsObservationHeld ||
            reading.AccuracyMeters > SafeZoneConfirmationService.MaximumAcceptedAccuracyMeters;
        arrivalNoticeCategory = uncertain ? "NEAR_UNCONFIRMED_POSITION_UNCERTAIN" :
            decision.IsCandidate ? "NEAR_CONFIRMING" : "NEAR_UNCONFIRMED";
        string detail = uncertain
            ? "Arrival is unconfirmed. GPS position is uncertain; check the entrance before confirming below."
            : decision.IsCandidate && !decision.IsObservationHeld
            ? $"Confirming arrival ({decision.ConfirmationCount}/{decision.RequiredConfirmationCount}). Stay near the entrance."
            : decision.Reason?.StartsWith("A mapped major road", StringComparison.Ordinal) == true
                ? "Check the mapped road crossing before entering."
                : "Look for the entrance. If you are there, confirm your arrival below.";
        routeLocatorLabel.Text = (uncertain ? "Location suggests you're near " : "You're near ") +
            $"{destination.Name}.\n{detail}";
        routeLocatorPanel.IsVisible = true;
        SetTurnGuidanceVisible(false);
        LogNavigationUiState();
        return true;
    }

    private bool TryShowRoadApproachCue()
    {
        if (activeRoute is { } computed && IsMldRoute(computed)) return false;
        RoadApproachCue? cue;
        lock (routeProgressFusionSync) cue = roadApproachCue;
        var destination = NavigationDestinationBridge.Current;
        var frame = ARCameraPoseBridge.CurrentFrame;
        if (cue is null || !pageIsVisible || safeZoneConfirmed || emergencyAdvisoryVisible ||
            dynamicRerouteInProgress || routeHazardBlocksGuidance || !HasCurrentRouteHeading() || _arCoreService.IsSessionPaused ||
            currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            !destination.IsAvailable || destination.Coordinate != cue.Destination ||
            (cue.ReturningToRoute && !recoveryConnectorVerified) ||
            !IsRoadApproachFixUsable(cue.Reading) || !frame.IsFresh ||
            !frame.IsTracking || !frame.Pose.IsTracking ||
            frame.Generation.SessionGeneration != cue.Session || CurrentCueOwnership(cue.Owner.Role) != cue.Owner)
        {
            ARRoadApproachBridge.Clear();
            return false;
        }
        var rotation = new Quaternion(frame.Pose.RotationX, frame.Pose.RotationY,
            frame.Pose.RotationZ, frame.Pose.RotationW);
        double distance = cue.Reading.Coordinate.DistanceTo(cue.Target);
        if (!RoadApproachCuePolicy.IsAccessDirectionReliable(distance, cue.Reading.AccuracyMeters))
        {
            ARRoadApproachBridge.Clear();
            routeLocatorIcon.IsVisible = false;
            routeLocatorLabel.Text = cue.ConnectsToDestination
                ? $"Mapped route nearby (~{distance:0} m). Check walking access."
                : "Mapped road nearby. Route connection pending.";
            routeLocatorPanel.IsVisible = true;
            if (activeRoute is null)
                SetTurnGuidanceVisible(false);
            return true;
        }
        if (!RoadApproachCuePolicy.TryGetAngle(cue.Reading.Coordinate, cue.Target,
                cue.Yaw, rotation, out double angle, out distance))
        {
            ARRoadApproachBridge.Clear();
            return false;
        }
        Vector2? registeredDirection = null;
        Vector2? worldTarget = null;
        if (cue.Owner.Role == "MAPPED_TARGET")
        {
            if (activeRoute is not { } mappedRoute || GetTrackedRouteMotion() is not { } motion ||
                !_routeDisplayProgressPolicy.TryGetRegisteredReference(mappedRoute, cue.Target,
                DateTimeOffset.UtcNow, motion, out var targetReference))
            {
                ARRoadApproachBridge.Clear("mapped target registration unavailable");
                return false;
            }
            var direction = new Vector2(targetReference.WorldX - frame.Pose.PositionX,
                targetReference.WorldZ - frame.Pose.PositionZ);
            if (direction.LengthSquared() < 1) { ARRoadApproachBridge.Clear(); return false; }
            registeredDirection = direction;
            worldTarget = new Vector2(targetReference.WorldX, targetReference.WorldZ);
            // The panel and the floor arrow use the same mapped target.
            var localDirection = Vector3.Transform(new Vector3(direction.X, 0, direction.Y),
                Quaternion.Inverse(Quaternion.Normalize(rotation)));
            angle = Math.Atan2(localDirection.X, -localDirection.Z) * 180 / Math.PI;
        }
        ARRoadApproachBridge.SetOwnership(cue.Owner);
        if (!ARRoadApproachBridge.Publish(cue.Reading.Coordinate, cue.Target, cue.Yaw,
            cue.Reading.AccuracyMeters, cue.Reading.Timestamp, cue.Session, cue.Owner, registeredDirection, worldTarget))
            return false;
        routeLocatorIcon.IsVisible = true;
        routeLocatorIcon.Source = "lucide_arrow_up_teal.png";
        routeLocatorIcon.Rotation = angle;
        routeLocatorLabel.Text = cue.ReturningToRoute
            ? $"Return to mapped route (~{distance:0} m). Check access."
            : cue.ConnectsToDestination
                ? $"Approach mapped route (~{distance:0} m). Check access."
                : $"Nearest mapped road (~{distance:0} m). Route connection pending.";
        routeLocatorPanel.IsVisible = true;
        if (activeRoute is null)
            SetTurnGuidanceVisible(false);
        return true;
    }

    private bool TryShowNavigationFallback()
    {
        // An accepted geographic route owns persistent instructions for either
        // algorithm while independent AR placement is recovering.
        if (activeRoute is not null || routeRequestInProgress) return false;
        var destination = NavigationDestinationBridge.Current;
        if (!pageIsVisible || safeZoneConfirmed || emergencyAdvisoryVisible || routeHazardBlocksGuidance ||
            currentCameraModuleView != CameraModuleViewMode.ArCamera ||
            !destination.IsAvailable || lastArGuidanceConfidence.AllowsRouteGeometry)
            return false;
        LocationReading? reading;
        lock (routeProgressFusionSync) reading = latestRouteStartupReading;
        bool fresh = reading is not null && RouteStartupLocationPolicy.CanPlan(
            reading.Coordinate, reading.AccuracyMeters, reading.Timestamp,
            DateTimeOffset.UtcNow, cached: true);
        // A distance summary survives brief AR/floor acquisition interruptions.
        // A direction-only cue additionally requires fresh GPS and camera pose;
        // it never advances progress or constructs walking geometry.
        routeLocatorIcon.IsVisible = false;
        routeLocatorIcon.Rotation = 0;
        routeLocatorLabel.Text = lastSafeZoneDecision.IsCandidate
            ? "Confirming destination proximity. Remain near the entrance."
            : fresh
                ? $"Destination ~{reading!.Coordinate.DistanceTo(destination.Coordinate):0} m away. Use the map to check walking access."
                : "Waiting for accurate GPS. Use the map to check walking access.";
        var frame = ARCameraPoseBridge.CurrentFrame;
        TimeSpan age = reading is null ? TimeSpan.MaxValue : DateTimeOffset.UtcNow - reading.Timestamp;
        if (fresh && reading is not null && reading.AccuracyMeters is double accuracy &&
            accuracy <= 30 && age >= TimeSpan.FromSeconds(-2) && age <= TimeSpan.FromSeconds(10) &&
            HasCurrentRouteHeading() && frame.IsFresh && frame.IsTracking && frame.Pose.IsTracking &&
            lastHeadingAlignment!.Value.SessionGeneration == frame.Generation.SessionGeneration &&
            DestinationBearingCuePolicy.TryGetAngle(reading.Coordinate, destination.Coordinate,
                activeMapToArYawDegrees, new Quaternion(frame.Pose.RotationX, frame.Pose.RotationY,
                    frame.Pose.RotationZ, frame.Pose.RotationW), out double angle))
        {
            routeLocatorIcon.IsVisible = true;
            routeLocatorIcon.Source = "lucide_arrow_up_teal.png";
            routeLocatorIcon.Rotation = angle;
            routeLocatorLabel.Text = $"Destination direction • ~{reading.Coordinate.DistanceTo(destination.Coordinate):0} m. Check walking access.";
        }
        routeLocatorPanel.IsVisible = true;
        SetTurnGuidanceVisible(false);
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
