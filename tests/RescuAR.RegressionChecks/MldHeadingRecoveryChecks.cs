using System.Numerics;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.AR;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Routing;

internal static class MldHeadingRecoveryChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        var generation = new ARRenderGenerationToken(6200, 6201, 6202);
        const long trip = 6203;
        ARRenderGenerationBridge.Activate(generation);
        ARCameraPoseBridge.Clear(); ARTrackingStateBridge.Clear(); ARRouteBridge.Clear();
        using var heading = new ArHeadingAlignmentService();
        heading.ResetSessionCalibration("MLD inherited-heading fixture");
        long timestamp = 1000;
        GeoCoordinate C(double east) => new(0, east / 111_195);
        var route = new RouteResult([new(C(0), 0), new(C(100), 100)], 100,
            "MLD (Railway OSRM)", new RouteProvenance("MLD"));
        void Pose(long? acquiredAt = null)
        {
            ARTrackingStateBridge.PublishObservation(generation.SessionGeneration, "TRACKING", "NONE", ++timestamp, false);
            var metadata = new ARFrameMetadata(generation, timestamp, new(generation.GeometryGeneration, 0, 1080, 1920))
                { AcquiredAtMonotonicMilliseconds = acquiredAt ?? Environment.TickCount64 };
            var ground = new ARGroundStateBridge.GroundStateSnapshot(timestamp, generation, 1,
                ARGroundLifecycleState.Verified, ARGroundTrust.Verified, Environment.TickCount64, "fixture");
            ARCameraPoseBridge.PublishFrame(true, "NONE", 0, 1.35f, 0, 0, 0, 0, 1,
                [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, -1, 0, 0, -.2f, 0], .1f, 100, ground, 0, 0, 0, metadata);
        }
        void Resume()
        {
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Paused", "fixture", false);
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Running", "fixture", false);
            Pose();
        }
        async Task<ArHeadingAlignmentService.HeadingAlignmentResult?> Capture()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var pending = heading.CaptureAsync(C(0), null, cancellation.Token);
            for (int i = 0; i < 12 && !pending.IsCompleted; i++)
            {
                Pose();
                OrientationSensor.Default.Emit(Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
                await Task.Delay(110);
            }
            return await pending;
        }
        try
        {
            Pose();
            var frame = ARCameraPoseBridge.CurrentFrame;
            var oldAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            check(heading.TryApplyMovementValidatedYaw(90, 3, 0, oldAt,
                    generation.SessionGeneration, frame.TrackingEpoch, frame.WorldFrame, out _),
                "MLD: prior-trip heading can remain in a retained session");
            Resume();
            var inherited = heading.LastResult;
            check(inherited is { IsStable: false, RequiresMovementValidation: true } &&
                    !heading.CanCaptureAutomatically && await heading.CaptureAsync(C(0), null) is null,
                "MLD: reproduces Checking direction with an inherited heading that blocks automatic capture");
            var startedAt = DateTimeOffset.UtcNow;
            long revision = heading.CalibrationRevision;
            var astar = new RouteResult(route.Points, 100, "AStar", new RouteProvenance("ASTAR"));
            check(!heading.TryPrepareForMldTrip(astar, startedAt, false) && heading.CalibrationRevision == revision,
                "A* lock: MLD recovery cannot reset an A* calibration");
            check(!heading.TryPrepareForMldTrip(route, oldAt, false) && heading.LastResult == inherited,
                "MLD: a partial correction belonging to the current trip cannot be discarded");
            check(!heading.TryPrepareForMldTrip(route, startedAt, true) && heading.LastResult == inherited,
                "MLD: published movement alignment is not replaced by startup compass acquisition");
            Pose(Environment.TickCount64 - 1000);
            check(!heading.TryPrepareForMldTrip(route, startedAt, false) && heading.LastResult == inherited,
                "MLD: stale tracking cannot authorize heading reacquisition");
            Pose();
            check(heading.TryPrepareForMldTrip(route, startedAt, false) && heading.LastResult is null &&
                    heading.CanCaptureAutomatically && !ARRouteBridge.Current.IsAvailable,
                "MLD: inherited invalid alignment releases fresh capture without publishing a guessed yaw");
            var recovered = await Capture();
            frame = ARCameraPoseBridge.CurrentFrame;
            check(recovered is { Source: "MAGNETIC", IsStable: true, RequiresMovementValidation: false } fresh &&
                    fresh.Timestamp >= startedAt && fresh.TrackingEpoch == frame.TrackingEpoch,
                "MLD: fresh synchronized orientation and AR samples establish the new heading");
            check(!heading.TryPrepareForMldTrip(route, startedAt, false) && heading.LastResult == recovered,
                "MLD: repeated timer ticks retain a healthy recovered heading");

            var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
            var fix = tracker.Update(C(0), 5);
            var at = DateTimeOffset.UtcNow;
            var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, recovered!.Value.MapToArYawDegrees,
                generation.SessionGeneration, frame.Anchor.ReferenceGeneration, frame.TrackingEpoch, frame.WorldFrame);
            var registration = new RouteDisplayProgressPolicy();
            registration.ObserveGps(route, fix, at);
            registration.ObserveRegistration(route, fix, at, at, motion);
            check(registration.TryGetRegisteredReference(route, C(0), at, motion, out var reference),
                "MLD: recovered alignment registers a fresh GPS observation");
            var placement = new ARRoutePlacement(generation, frame.Anchor.ReferenceGeneration, frame.Version,
                Environment.TickCount64, 0, 0, 0, 0, at, "MLD_HEADING_RECOVERY", recovered.Value.MapToArYawDegrees,
                TrackingEpoch: frame.TrackingEpoch, WorldFrame: frame.WorldFrame);
            MldGuidanceBridge.SetRouteOwner(route, trip);
            check(new MLDARIntegrationService(new RecordedRoute(route)).PublishProgressWindow(route, 0, C(0),
                    recovered.Value.MapToArYawDegrees, 0, 0, sourceSegmentIndex: 0, placement: placement, registeredReference: reference),
                "MLD: the normal registered publisher produces the mapped route after heading recovery");
            MldGuidanceBridge.RebindPublishedWindow(route, trip, heading.CalibrationRevision,
                ARRouteBridge.Current, frame, false, null);
            MldGuidanceBridge.Publish(route, trip, heading.CalibrationRevision, ARRouteBridge.Current, frame, true, "fixture");
            check(MldGuidanceBridge.CanRenderPath(ARRouteBridge.Current, frame, trip),
                "MLD: recovered geometry is renderable only after normal ownership and frame checks");

            Resume();
            check(!MldGuidanceBridge.CanRenderPath(ARRouteBridge.Current, ARCameraPoseBridge.CurrentFrame, trip) &&
                    heading.TryPrepareForMldTrip(route, startedAt, true) && heading.LastResult is null,
                "MLD: an expired magnetic frame can reacquire after a published window without rendering the old frame");
            check(await Capture() is { IsStable: true, RequiresMovementValidation: false },
                "MLD: fresh samples restore heading after a tracking-frame transition");
            frame = ARCameraPoseBridge.CurrentFrame;
            check(heading.TryApplyMovementValidatedYaw(35, 3, 30, DateTimeOffset.UtcNow,
                    generation.SessionGeneration, frame.TrackingEpoch, frame.WorldFrame, out var partial) &&
                    !heading.TryPrepareForMldTrip(route, startedAt, false) && heading.LastResult == partial &&
                    !heading.CanCaptureAutomatically,
                "MLD: current-frame partial movement corrections still require independent validation");
        }
        finally
        {
            heading.ResetSessionCalibration("MLD recovery fixture completed");
            MldGuidanceBridge.SetRouteOwner(null, 0);
            ARRouteBridge.Clear(); ARCameraPoseBridge.Clear(); ARTrackingStateBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(previous); else ARRenderGenerationBridge.Invalidate();
        }
    }

    private sealed class RecordedRoute(RouteResult route) : IRoutingService
    {
        public string AlgorithmName => route.Algorithm;
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
            CancellationToken cancellationToken = default) => Task.FromResult<RouteResult?>(route);
    }
}
