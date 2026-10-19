using RescuAR.AR;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Guidance;
using RescuAR.Diagnostics;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    // Physical display references are separate from monotonic trip progress.
    // They never become routing origins, device locations or arrival evidence.
    private VerifiedPosition? verifiedPosition;
    private readonly RoutePositionObservationPolicy positionObservationPolicy = new();
    private volatile bool positionObservationHeld;

    private ARRoutePlacement CaptureRoutePlacement(ARCameraPoseBridge.SpatialSnapshot frame,
        string source, bool retainedObservation)
    {
        DateTimeOffset? observation;
        lock (routeProgressFusionSync)
            observation = retainedObservation ? verifiedPosition?.At : latestRouteStartupReading?.Timestamp;
        var tracking = ARTrackingStateBridge.Current;
        return new(frame.Generation, frame.Anchor.ReferenceGeneration, frame.Version,
            Environment.TickCount64, frame.Anchor.PositionX, frame.Anchor.PositionZ,
            frame.Pose.PositionX, frame.Pose.PositionZ, observation, source, activeMapToArYawDegrees,
            PosePublishedAt: frame.PublishedAtMonotonicMilliseconds, FrameTimestamp: frame.FrameTimestamp,
            TrackingEpoch: tracking.ActivePauseTransitionCount + tracking.LifecyclePauseTransitionCount,
            WorldFrame: ARCameraSpatialController.WorldFrameGeneration);
    }

    private sealed record VerifiedPosition(RouteResult Route, DateTimeOffset At,
        double Accuracy, double Progress, RouteBacktrackingPolicy.TrackedMotion? Motion,
        GeoCoordinate Coordinate);

    private static RouteProgressTracker.RouteProgressUpdate HeldGpsObservation(
        RouteProgressTracker.ProgressSnapshot previous, LocationReading reading) =>
        new(false, false, false, previous.SegmentIndex, previous.RawProgressMeters,
            previous.CommittedProgressMeters, previous.RemainingMeters, previous.CrossTrackErrorMeters,
            reading.AccuracyMeters, reading.Coordinate, previous.SnappedCoordinate, "GPS_OBSERVATION_HELD",
            RouteCorridorPolicy.GetCorridorRadiusMeters(reading.AccuracyMeters, false),
            RouteMatchConfidence.Unavailable, double.NaN);

    private RouteBacktrackingPolicy.TrackedMotion? GetTrackedRouteMotion()
    {
        var frame = ARCameraPoseBridge.CurrentFrame;
        var tracking = ARTrackingStateBridge.Current;
        if (!frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking ||
            !HasCurrentRouteHeading() || !tracking.IsRenderableFor(frame.Generation.SessionGeneration) ||
            !ARRenderGenerationBridge.IsCurrentSession(frame.Generation) ||
            ARCameraSpatialController.CurrentRouteWorldCorrectionRequest.IsPending)
            return null;

        var motion = new RouteBacktrackingPolicy.TrackedMotion(frame.Pose.PositionX,
            frame.Pose.PositionZ, activeMapToArYawDegrees, frame.Generation.SessionGeneration,
            frame.Anchor.ReferenceGeneration,
            tracking.ActivePauseTransitionCount + tracking.LifecyclePauseTransitionCount,
            ARCameraSpatialController.WorldFrameGeneration);
        return motion.IsValid ? motion : null;
    }

    // The caller first applies the existing fusion confidence/identity gates.
    private void ObserveVerifiedGpsPosition(RouteResult route,
        RouteProgressTracker.RouteProgressUpdate update, LocationReading reading)
    {
        lock (routeProgressFusionSync)
        {
            if (!ReferenceEquals(route, activeRoute) ||
                (verifiedPosition is { } previous && ReferenceEquals(previous.Route, route) &&
                 reading.Timestamp <= previous.At) ||
                !RouteDisplayProgressPolicy.TryResolveGps(route, update, out _)) return;
            _routeDisplayProgressPolicy.ObserveGps(route, update, reading.Timestamp);
            TimeSpan age = DateTimeOffset.UtcNow - reading.Timestamp;
            var motion = age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(3) ? GetTrackedRouteMotion() : null;
            positionObservationPolicy.Seed(route, reading.Coordinate, reading.AccuracyMeters,
                reading.Timestamp, DateTimeOffset.UtcNow, motion);
            LogPositionInnovation(verifiedPosition, route, reading, motion);
            verifiedPosition = new(route, reading.Timestamp, reading.AccuracyMeters!.Value,
                update.CommittedProgressMeters, motion, reading.Coordinate);
        }
    }

    private bool TryGetBoundedDisplayPosition(RouteResult route, double committedProgress,
        DateTimeOffset now, out RouteDisplayProgressPolicy.DisplayPosition position,
        out double accuracy)
    {
        lock (routeProgressFusionSync)
        {
            position = default;
            accuracy = double.NaN;
            if (!ReferenceEquals(activeRoute, route) || verifiedPosition is not { } previous ||
                !ReferenceEquals(previous.Route, route) || now < previous.At ||
                now - previous.At > MaximumPdrRouteWindowGpsAge ||
                committedProgress < previous.Progress ||
                committedProgress - previous.Progress > MaximumPdrRouteWindowAdvanceMeters ||
                dynamicRerouteInProgress || safeZoneConfirmed ||
                _gpsPdrFusionPolicy.IsRouteIdentitySuspended || lastOffRouteCandidate ||
                previous.Motion is not { } from || GetTrackedRouteMotion() is not { } to ||
                !from.IsContinuousWith(to))
                return false;

            double dx = to.X - from.X, dz = to.Z - from.Z;
            if (dx * dx + dz * dz > MaximumPdrRouteWindowAdvanceMeters * MaximumPdrRouteWindowAdvanceMeters ||
                !_routeDisplayProgressPolicy.TryResolvePdr(route, committedProgress, now, out position))
                return false;
            accuracy = previous.Accuracy;
            return true;
        }
    }

    private bool TryRetainWindowAcrossGroundHandoff(ARCameraPoseBridge.SpatialSnapshot frame)
    {
        lock (routeProgressFusionSync)
        {
            if (activeRoute is not { } route || verifiedPosition is not { Motion: { } from } verified ||
                !ReferenceEquals(verified.Route, route) || GetTrackedRouteMotion() is not { } to ||
                !TryGetBoundedDisplayPosition(route, _routeProgressTracker.Current.CommittedProgressMeters,
                    DateTimeOffset.UtcNow, out _, out _)) return false;
            var window = ARRouteBridge.Current;
            if (!string.Equals(window.Algorithm, route.Algorithm, StringComparison.Ordinal) ||
                Math.Abs(window.TotalDistanceMeters - route.TotalDistanceMeters) > 0.5 ||
                !RouteAnchorRecoveryPolicy.TryRebaseWindow(window, frame, from, to,
                    DateTimeOffset.UtcNow, out var points, out var placement)) return false;
            ARRouteBridge.Publish(points, window.Algorithm, window.TotalDistanceMeters,
                window.NavigationState.VisualKind, window.NavigationState.WindowStartProgressMeters,
                window.NavigationState.SourceSegmentIndex, placement);
            AndroidLog.Info("RescuAR-AnchorRecovery",
                $"GROUND_HANDOFF_RETAINED oldReference={window.Placement.GroundReference}; " +
                $"newReference={placement.GroundReference}; worldPositionsPreserved=True; " +
                $"observationAgeMs={(DateTimeOffset.UtcNow - verified.At).TotalMilliseconds:F0}.");
            return true;
        }
    }

    private bool CompleteGroundHandoffAwaitingPosition()
    {
        ARRouteBridge.Clear();
        approachRoutePreviewPublished = false;
        AndroidLog.Info("RescuAR-AnchorRecovery",
            "GROUND_HANDOFF_COMPLETE: floor is ready; stale geometry cleared; waiting for a verified route position.");
        return true;
    }

    private bool TryShowGeographicRecoveryGuidance(RouteResult? route,
        RouteProgressTracker.ProgressSnapshot progress)
    {
        PedestrianTurnGuidanceService.TurnGuidanceSnapshot guidance;
        DateTimeOffset observedAt;
        long trip = activeDestinationVersion;
        lock (routeProgressFusionSync)
        {
            if (route is null || !ReferenceEquals(route, activeRoute) ||
                verifiedPosition is not { } verified || !ReferenceEquals(verified.Route, route) ||
                DateTimeOffset.UtcNow - verified.At > TimeSpan.FromSeconds(5) ||
                lastGpsConfidence < GpsPdrFusionPolicy.GpsConfidence.Medium ||
                _gpsPdrFusionPolicy.IsRouteIdentitySuspended || positionObservationHeld || lastOffRouteCandidate ||
                !progress.HasProgress || progress.IsOffRoute ||
                !_routeDisplayProgressPolicy.TryResolvePdr(route, progress.CommittedProgressMeters,
                    DateTimeOffset.UtcNow, out var position)) return false;
            observedAt = verified.At;
            guidance = _turnGuidanceService.Evaluate(route, position.ProgressMeters, position.SegmentIndex)
                with { RemainingRouteMeters = progress.RemainingMeters };
        }
        if (!guidance.IsAvailable) return false;
        Dispatcher.Dispatch(() =>
        {
            if (!pageIsVisible || !ReferenceEquals(activeRoute, route) || activeDestinationVersion != trip ||
                DateTimeOffset.UtcNow - observedAt > TimeSpan.FromSeconds(5) ||
                positionObservationHeld || _gpsPdrFusionPolicy.IsRouteIdentitySuspended ||
                currentCameraModuleView != CameraModuleViewMode.ArCamera ||
                emergencyAdvisoryVisible || safeZoneConfirmed || lastSafeZoneDecision.IsCandidate) return;
            ApplyPrototypeTurnGuidance(guidance);
            turnDistanceLabel.Text = $"{guidance.RemainingRouteMeters:F0} m remaining · AR placement recovering";
            turnGuidancePanel.IsVisible = true;
        });
        return true;
    }
}
