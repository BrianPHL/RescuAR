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
    private readonly NavigationStabilityPolicy navigationStability;
    private RegisteredPublication? lastRegisteredPublication;
    private string? lastRegistrationRejection;
    private long lastRegistrationRejectionAt;
    private long routeWindowPublishedCount, routeWindowReusedCount;
    private sealed record RegisteredPublication(RouteResult Route, long BridgeVersion, long Ground,
        long Epoch, long World, long Calibration, string Source, double Progress, double Window,
        GeoCoordinate Coordinate, float X, float Z);

    private void ResetGeographicPositionEvidence()
    {
        lock (routeProgressFusionSync)
        {
            navigationStability.Reset();
            positionObservationPolicy.Reset();
            verifiedPosition = null;
            lastRegisteredPublication = null;
            lastRegistrationRejection = null;
            Interlocked.Exchange(ref routeWindowPublishedCount, 0);
            Interlocked.Exchange(ref routeWindowReusedCount, 0);
        }
    }

    private void ObserveHeadingContinuity(LocationReading reading)
    {
        if (!_headingAlignmentService.ObserveIndependentContinuity(reading.Coordinate, reading.AltitudeMeters)) return;
        var calibration = _headingAlignmentService.LastResult;
        lastHeadingAlignment = calibration;
        ClearRoadApproachCue();
        AndroidLog.Info(HeadingLogTag,
            $"NAV_HEADING_TRUST revision={_headingAlignmentService.CalibrationRevision}; " +
            $"stable={calibration?.IsStable}; requiresMovement={calibration?.RequiresMovementValidation}; " +
            $"session={calibration?.SessionGeneration}; epoch={calibration?.TrackingEpoch}; world={calibration?.WorldFrame}; " +
            "reason=independent orientation continuity; compassRotationApplied=False.");
    }

    private ARRoutePlacement CaptureRoutePlacement(ARCameraPoseBridge.SpatialSnapshot frame,
        string source, bool retainedObservation, DateTimeOffset? capturedObservation = null)
    {
        DateTimeOffset? observation;
        lock (routeProgressFusionSync)
            observation = capturedObservation ?? (retainedObservation ? verifiedPosition?.At : latestRouteStartupReading?.Timestamp);
        var observed = ARCameraPoseBridge.SpatialSnapshot.Unavailable;
        long skew = long.MaxValue;
        bool paired = observation.HasValue && ARCameraPoseBridge.TryGetObservationFrame(observation.Value,
            DateTimeOffset.UtcNow, Environment.TickCount64, out observed, out skew);
        return new(frame.Generation, frame.Anchor.ReferenceGeneration, frame.Version,
            Environment.TickCount64, frame.Anchor.PositionX, frame.Anchor.PositionZ,
            frame.Pose.PositionX, frame.Pose.PositionZ, observation, source, activeMapToArYawDegrees,
            PosePublishedAt: frame.PublishedAtMonotonicMilliseconds, FrameTimestamp: frame.FrameTimestamp,
            TrackingEpoch: frame.TrackingEpoch,
            WorldFrame: frame.WorldFrame,
            AnchorRotation: frame.Anchor.HasRotation ? frame.Anchor.Rotation : null,
            ObservationPoseFrame: paired ? observed.FrameTimestamp : 0,
            ObservationPoseSkewMilliseconds: skew,
            ObservationCameraX: paired ? observed.Pose.PositionX : float.NaN,
            ObservationCameraZ: paired ? observed.Pose.PositionZ : float.NaN);
    }

    private sealed record VerifiedPosition(RouteResult Route, DateTimeOffset At,
        double Accuracy, double Progress, RouteBacktrackingPolicy.TrackedMotion? Motion,
        GeoCoordinate Coordinate, double DisplayProgress);

    private static RouteProgressTracker.RouteProgressUpdate HeldGpsObservation(
        RouteProgressTracker.ProgressSnapshot previous, LocationReading reading) =>
        new(false, false, false, previous.SegmentIndex, previous.RawProgressMeters,
            previous.CommittedProgressMeters, previous.RemainingMeters, previous.CrossTrackErrorMeters,
            reading.AccuracyMeters, reading.Coordinate, previous.SnappedCoordinate, "GPS_OBSERVATION_HELD",
            RouteCorridorPolicy.GetCorridorRadiusMeters(reading.AccuracyMeters, false),
            RouteMatchConfidence.Unavailable, double.NaN);

    private RouteBacktrackingPolicy.TrackedMotion? GetTrackedRouteMotion(bool requireTrustedHeading = true)
    {
        var frame = ARCameraPoseBridge.CurrentFrame;
        var tracking = ARTrackingStateBridge.Current;
        if (!frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking ||
            !(requireTrustedHeading ? HasCurrentRouteHeading() : HasCurrentHeadingCandidate()) ||
            !tracking.IsRenderableFor(frame.Generation.SessionGeneration) ||
            frame.TrackingEpoch != tracking.ActivePauseTransitionCount + tracking.LifecyclePauseTransitionCount ||
            frame.WorldFrame != ARCameraSpatialController.WorldFrameGeneration ||
            !ARRenderGenerationBridge.IsCurrentSession(frame.Generation) ||
            ARCameraSpatialController.CurrentRouteWorldCorrectionRequest.IsPending)
            return null;

        var motion = new RouteBacktrackingPolicy.TrackedMotion(frame.Pose.PositionX,
            frame.Pose.PositionZ, activeMapToArYawDegrees, frame.Generation.SessionGeneration,
            frame.Anchor.ReferenceGeneration,
            frame.TrackingEpoch, frame.WorldFrame);
        return motion.IsValid ? motion : null;
    }

    private RouteBacktrackingPolicy.TrackedMotion? GetAcquisitionMotion(LocationReading reading,
        bool requireTrustedHeading = true)
    {
        if (GetTrackedRouteMotion(requireTrustedHeading) is not { } current ||
            !ARCameraPoseBridge.TryGetObservationFrame(reading.Timestamp, DateTimeOffset.UtcNow,
                Environment.TickCount64, out var observed, out _)) return null;
        var motion = new RouteBacktrackingPolicy.TrackedMotion(observed.Pose.PositionX, observed.Pose.PositionZ,
            activeMapToArYawDegrees, observed.Generation.SessionGeneration, observed.Anchor.ReferenceGeneration,
            observed.TrackingEpoch, observed.WorldFrame);
        return motion.IsContinuousWith(current) ? motion : null;
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
            if (GetAcquisitionMotion(reading) is not { } motion) return;
            _routeDisplayProgressPolicy.ObserveGps(route, update, reading.Timestamp);
            var registration = _routeDisplayProgressPolicy.ObserveRegistration(route, update, reading.Timestamp,
                DateTimeOffset.UtcNow, motion);
            LogDetailedDebug(ProgressLogTag,
                $"NAV_REGISTRATION_DECISION sample={reading.Timestamp.ToUnixTimeMilliseconds()}; " +
                $"trip={activeDestinationVersion}; state={registration.State}; innovation={registration.InnovationMeters:F3}m; " +
                $"correctionAR=({registration.CorrectionX:F3},{registration.CorrectionZ:F3}); evidence={registration.EvidenceCount}; " +
                $"changed={registration.Changed}; " +
                $"supportAgeMs={(DateTimeOffset.UtcNow - _routeDisplayProgressPolicy.RegistrationSupportedAt)?.TotalMilliseconds:F0}; " +
                $"candidateAgeMs={(reading.Timestamp - _routeDisplayProgressPolicy.PendingRegistrationAt)?.TotalMilliseconds:F0}; " +
                $"seedAccuracy={_routeDisplayProgressPolicy.RegistrationAccuracyMeters:F2}m; " +
                $"requalificationLifetimeSec={navigationStability.Options.RecoveryLifetime.TotalSeconds:F1}; " +
                $"session={motion.Session}; epoch={motion.TrackingEpoch}; world={motion.WorldFrame}.");
            positionObservationPolicy.Seed(route, reading.Coordinate, reading.AccuracyMeters,
                reading.Timestamp, DateTimeOffset.UtcNow, motion);
            LogPositionInnovation(verifiedPosition, route, reading, motion);
            verifiedPosition = new(route, reading.Timestamp, reading.AccuracyMeters!.Value,
                update.CommittedProgressMeters, motion, reading.Coordinate, update.RawProgressMeters);
        }
    }

    private bool PublishRegisteredRouteWindow(RouteResult route, RouteDisplayProgressPolicy.DisplayPosition position,
        ARCameraPoseBridge.SpatialSnapshot frame, string source, bool boundedRecovery = false)
    {
        lock (routeProgressFusionSync)
        {
            if (!ReferenceEquals(route, activeRoute)) return RejectRegisteredWindow("route identity changed", source);
            if (GetTrackedRouteMotion() is not { } current)
                return RejectRegisteredWindow("tracking or heading not qualified", source);
            if (current.Session != frame.Generation.SessionGeneration || current.TrackingEpoch != frame.TrackingEpoch ||
                current.WorldFrame != frame.WorldFrame)
                return RejectRegisteredWindow("publication frame changed", source);
            if (verifiedPosition is not { } verified || !ReferenceEquals(verified.Route, route))
                return RejectRegisteredWindow("no verified geographic observation", source);
            if (!_routeDisplayProgressPolicy.TryGetRegisteredReference(route, position.RoadCoordinate,
                    DateTimeOffset.UtcNow, current, out var reference,
                    boundedRecovery ? navigationStability.Options.RecoveryLifetime : null))
                return RejectRegisteredWindow("registration unsupported or context changed", source);
            double window = GetCurrentArRouteVisualWindowMeters();
            if (routeHazardBlocksGuidance)
            {
                if (!ReferenceEquals(hazardPrefixRoute, route) || !double.IsFinite(hazardSafePrefixEnd))
                    return RejectRegisteredWindow("hazard has no validated forward prefix", source);
                window = Math.Min(window, hazardSafePrefixEnd - position.ProgressMeters);
                if (window < .5) return RejectRegisteredWindow("at reported hazard boundary", source);
            }
            if (lastRegisteredPublication is { } cached && ReferenceEquals(cached.Route, route) &&
                cached.BridgeVersion == ARRouteBridge.Current.Version &&
                cached.Ground == frame.Anchor.ReferenceGeneration && cached.Epoch == frame.TrackingEpoch &&
                cached.World == frame.WorldFrame && cached.Calibration == _headingAlignmentService.CalibrationRevision &&
                cached.Source == source && cached.Window == window &&
                Math.Abs(cached.Progress - position.ProgressMeters) < 3)
            {
                var delta = RescuAR.Navigation.Projection.MapToArCoordinates.Rotate(
                    (position.RoadCoordinate.Longitude - cached.Coordinate.Longitude) * Math.PI / 180 *
                        6371008.8 * Math.Cos(cached.Coordinate.Latitude * Math.PI / 180),
                    (position.RoadCoordinate.Latitude - cached.Coordinate.Latitude) * Math.PI / 180 * 6371008.8,
                    activeMapToArYawDegrees);
                if (Math.Abs(reference.WorldX - cached.X - delta.X) < .02 &&
                    Math.Abs(reference.WorldZ - cached.Z - delta.Z) < .02)
                {
                    Interlocked.Increment(ref routeWindowReusedCount);
                    return true;
                }
            }
            bool published = _mldArIntegrationService.PublishProgressWindow(route, position.ProgressMeters,
                position.RoadCoordinate, activeMapToArYawDegrees,
                frame.Pose.PositionX - frame.Anchor.PositionX, frame.Pose.PositionZ - frame.Anchor.PositionZ,
                arWindowMeters: window, clearRouteOnFailure: false,
                sourceSegmentIndex: position.SegmentIndex, userCoordinate: position.AlignmentCoordinate,
                placement: CaptureRoutePlacement(frame, source, true, verified.At), registeredReference: reference,
                maximumRegistrationAge: boundedRecovery ? navigationStability.Options.RecoveryLifetime : null);
            if (published) lastRegisteredPublication = new(route, ARRouteBridge.Current.Version,
                frame.Anchor.ReferenceGeneration, frame.TrackingEpoch, frame.WorldFrame,
                _headingAlignmentService.CalibrationRevision, source, position.ProgressMeters, window,
                position.RoadCoordinate, reference.WorldX, reference.WorldZ);
            if (published)
            {
                Interlocked.Increment(ref routeWindowPublishedCount);
                lastRegistrationRejection = null;
            }
            else RejectRegisteredWindow("projector rejected local window", source);
            return published;
        }
    }

    private bool RejectRegisteredWindow(string reason, string source)
    {
        long now = Environment.TickCount64;
        if (lastRegistrationRejection != reason || now - lastRegistrationRejectionAt >= 5000)
        {
            lastRegistrationRejection = reason;
            lastRegistrationRejectionAt = now;
            var frame = ARCameraPoseBridge.CurrentFrame;
            AndroidLog.Info(ProgressLogTag, $"NAV_PUBLICATION_REJECTED trip={activeDestinationVersion}; " +
                $"source={source}; reason='{reason}'; registration={_routeDisplayProgressPolicy.LastRegistrationDecision.State}; " +
                $"revision={_headingAlignmentService.CalibrationRevision}; session={frame.Generation.SessionGeneration}; " +
                $"epoch={frame.TrackingEpoch}; world={frame.WorldFrame}.");
        }
        return false;
    }

    private bool TryPublishUncertainRouteEstimate(RouteResult route)
    {
        lock (routeProgressFusionSync)
        {
            if (safeZoneConfirmed || dynamicRerouteInProgress || routeHazardBlocksGuidance ||
                lastOffRouteCandidate || _gpsPdrFusionPolicy.IsRouteIdentitySuspended ||
                arRouteVisualMode != ArRouteVisualMode.RoadFollowingLong ||
                verifiedPosition is not { Motion: { } from } verified || !ReferenceEquals(verified.Route, route) ||
                GetTrackedRouteMotion() is not { } to ||
                !navigationStability.TryEstimate(route, verified.Coordinate, verified.DisplayProgress,
                    verified.At, DateTimeOffset.UtcNow, from, to, out var estimate)) return false;
            return PublishRegisteredRouteWindow(route, estimate, ARCameraPoseBridge.CurrentFrame,
                "BOUNDED_UNCERTAIN_ESTIMATE", boundedRecovery: true);
        }
    }

    private bool HasSupportedRouteRegistration(RouteResult route, double progress, DateTimeOffset now,
        TimeSpan? maximumSupportAge = null)
    {
        lock (routeProgressFusionSync)
            return ReferenceEquals(activeRoute, route) && GetTrackedRouteMotion() is { } motion &&
                RouteDisplayProgressPolicy.TryAtDistance(route, progress, out var position) &&
                _routeDisplayProgressPolicy.TryGetRegisteredReference(route, position.RoadCoordinate,
                    now, motion, out _, maximumSupportAge);
    }

    private bool TryGetObservedDisplayPosition(RouteResult route, out RouteDisplayProgressPolicy.DisplayPosition position)
    {
        lock (routeProgressFusionSync)
            return _routeDisplayProgressPolicy.TryGetObservedPosition(route, DateTimeOffset.UtcNow, out position);
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
            SetTurnGuidanceVisible(true);
        });
        return true;
    }
}
