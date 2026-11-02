using System.Numerics;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.AR;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Routing;

internal static class AStarHeadingRecoveryChecks
{
    private static GeoCoordinate C(double east) => new(0, east / 111_195);
    private static readonly float[] Projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, -1, 0, 0, -.2f, 0];

    public static async Task RunAsync(Action<bool, string> check)
    {
        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        var generation = new ARRenderGenerationToken(2600, 2601, 2602);
        ARRenderGenerationBridge.Activate(generation);
        ARCameraPoseBridge.Clear();
        ARTrackingStateBridge.Clear();
        using var heading = new ArHeadingAlignmentService();
        heading.ResetSessionCalibration("A* return-trip fixture");
        long timestamp = 1000;
        void Pose(long? acquiredAt = null)
        {
            ARTrackingStateBridge.PublishObservation(generation.SessionGeneration, "TRACKING", "NONE", ++timestamp, false);
            var metadata = new ARFrameMetadata(generation, timestamp, new(generation.GeometryGeneration, 0, 1080, 1920))
                { AcquiredAtMonotonicMilliseconds = acquiredAt ?? Environment.TickCount64 };
            var ground = new ARGroundStateBridge.GroundStateSnapshot(timestamp, generation, 1,
                ARGroundLifecycleState.Verified, ARGroundTrust.Verified, Environment.TickCount64, "fixture");
            ARCameraPoseBridge.PublishFrame(true, "NONE", 0, 1.35f, 0, 0, 0, 0, 1,
                Projection, .1f, 100, ground, 0, 0, 0, metadata);
        }
        try
        {
            var graph = new RoadGraphBuilder().Build(new[] { new GeoJsonRoadFeature {
                OsmId = "return-street", Highway = "footway", Coordinates = new[] { C(0), C(300) } } });
            var router = new AStarRoutingService(graph, "astar-return-fixture");
            var route = await router.FindRouteAsync(C(250), C(0));
            check(route is { Points.Count: 2, Provenance.Provider: "ASTAR" },
                "A*: return walk uses the real offline router and its sparse straight-street geometry");
            var tracker = new RouteProgressTracker();
            tracker.SetRoute(route!);
            tracker.Update(C(245), 5);
            var progress = tracker.Current;
            string geometry = route!.GeometryFingerprint;
            Pose();
            var oldAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            var frame = ARCameraPoseBridge.CurrentFrame;
            check(heading.TryApplyMovementValidatedYaw(120, 3, 0, oldAt,
                    generation.SessionGeneration, frame.TrackingEpoch, frame.WorldFrame, out _),
                "A*: prior trip can leave a movement-validated heading in the retained AR session");
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Paused", "previous trip ended", false);
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Running", "offline trip started", false);
            Pose();
            var inherited = heading.LastResult;
            long revision = heading.CalibrationRevision;
            check(inherited is { IsStable: false, RequiresMovementValidation: true } &&
                !heading.CanCaptureAutomatically && await heading.CaptureAsync(C(245), null) is null,
                "A*: reproduces the inherited heading that blocks automatic capture while GPS guidance continues");

            var startedAt = DateTimeOffset.UtcNow;
            var mld = new RouteResult(route.Points, route.TotalDistanceMeters, "MLD (Railway OSRM)", new RouteProvenance("MLD"));
            check(!heading.TryPrepareForAStarTrip(mld, startedAt, false) &&
                heading.LastResult == inherited && heading.CalibrationRevision == revision,
                "MLD lock: A* recovery cannot change MLD's heading, revision or acquisition behavior");
            check(!heading.TryPrepareForAStarTrip(route, startedAt, true) && heading.LastResult == inherited,
                "A*: an already published trip cannot discard its calibration through the startup recovery");
            check(!heading.TryPrepareForAStarTrip(route, oldAt, false) && heading.LastResult == inherited,
                "A*: a partial or invalid calibration from the current trip still requires normal validation");
            Pose(Environment.TickCount64 - 1000);
            check(!heading.TryPrepareForAStarTrip(route, startedAt, false) && heading.LastResult == inherited,
                "A*: stale AR tracking cannot trigger heading reacquisition");
            Pose();
            check(heading.TryPrepareForAStarTrip(route, startedAt, false) && heading.LastResult is null &&
                heading.CanCaptureAutomatically && !ARRouteBridge.Current.IsAvailable,
                "A*: an untrusted previous-trip heading releases fresh acquisition without publishing a guessed route");
            check(tracker.Current == progress && route.GeometryFingerprint == geometry,
                "A*: heading reacquisition preserves the accepted route and geographic trip progress");

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var capture = heading.CaptureAsync(C(245), null, cancellation.Token);
            for (int i = 0; i < 12 && !capture.IsCompleted; i++)
            {
                Pose();
                OrientationSensor.Default.Emit(Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
                if (i == 1) check(!capture.IsCompleted && heading.LastResult is null,
                    "A*: initial compass samples cannot prematurely authorize the route");
                await Task.Delay(110);
            }
            var recovered = await capture;
            frame = ARCameraPoseBridge.CurrentFrame;
            check(recovered is { IsStable: true, RequiresMovementValidation: false, Source: "MAGNETIC" } fresh &&
                fresh.Timestamp >= startedAt && fresh.TrackingEpoch == frame.TrackingEpoch &&
                Math.Abs(fresh.MapToArYawDegrees) < .1,
                "A*: fresh synchronized samples establish the new heading in the current frame");
            check(!heading.TryPrepareForAStarTrip(route, startedAt, false) && heading.LastResult == recovered,
                "A*: later timer ticks retain the newly acquired heading");

            var at = DateTimeOffset.UtcNow;
            var fix = tracker.Update(C(245), 5);
            var physical = new RouteDisplayProgressPolicy();
            physical.ObserveGps(route, fix, at);
            var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, recovered!.Value.MapToArYawDegrees,
                generation.SessionGeneration, frame.Anchor.ReferenceGeneration, frame.TrackingEpoch, frame.WorldFrame);
            physical.ObserveRegistration(route, fix, at, at, motion);
            check(RouteDisplayProgressPolicy.TryResolveGps(route, fix, out var position) &&
                physical.TryGetRegisteredReference(route, position.RoadCoordinate, at, motion, out _),
                "A*: the recovered heading can register a fresh physical GPS observation");
            physical.TryGetRegisteredReference(route, position.RoadCoordinate, at, motion, out var reference);
            var placement = new ARRoutePlacement(generation, frame.Anchor.ReferenceGeneration, frame.Version,
                Environment.TickCount64, 0, 0, 0, 0, at, "ASTAR_HEADING_RECOVERY", recovered.Value.MapToArYawDegrees,
                TrackingEpoch: frame.TrackingEpoch, WorldFrame: frame.WorldFrame);
            check(new MLDARIntegrationService(router).PublishProgressWindow(route, position.ProgressMeters,
                position.RoadCoordinate, recovered.Value.MapToArYawDegrees, 0, 0,
                sourceSegmentIndex: position.SegmentIndex, placement: placement, registeredReference: reference) &&
                ARRouteBridge.Current is { IsAvailable: true, Algorithm: "AStar" },
                "A*: the recovered trip publishes its mapped AR route using the existing registered publisher");
        }
        finally
        {
            heading.ResetSessionCalibration("A* fixture completed");
            ARRouteBridge.Clear();
            ARCameraPoseBridge.Clear();
            ARTrackingStateBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(previous);
            else ARRenderGenerationBridge.Invalidate();
        }
    }
}
