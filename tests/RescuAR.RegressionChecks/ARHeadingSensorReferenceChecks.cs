using System.Numerics;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.AR;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Routing;
using RescuAR.Navigation.Timing;

internal static class ARHeadingSensorReferenceChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        var generation = new ARRenderGenerationToken(6400, 6401, 6402);
        ARRenderGenerationBridge.Activate(generation);
        ARCameraPoseBridge.Clear(); ARTrackingStateBridge.Clear(); ARRouteBridge.Clear();
        using var heading = new ArHeadingAlignmentService();
        heading.ResetSessionCalibration("field-test compass offset fixture");
        heading.Start();
        long timestamp = 1000;
        GeoCoordinate C(double east) => new(0, east / 111_195);
        void Sample(double sensorYaw = 0)
        {
            ARTrackingStateBridge.PublishObservation(generation.SessionGeneration, "TRACKING", "NONE", ++timestamp, false);
            var metadata = new ARFrameMetadata(generation, timestamp, new(generation.GeometryGeneration, 0, 1080, 1920));
            var ground = new ARGroundStateBridge.GroundStateSnapshot(timestamp, generation, 1,
                ARGroundLifecycleState.Verified, ARGroundTrust.Verified, Environment.TickCount64, "fixture");
            ARCameraPoseBridge.PublishFrame(true, "NONE", 0, 1.35f, 0, 0, 0, 0, 1,
                [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, -1, 0, 0, -.2f, 0], .1f, 100, ground, 0, 0, 0, metadata);
            OrientationSensor.Default.Emit(Quaternion.Concatenate(
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(-sensorYaw * Math.PI / 180))));
        }
        async Task Observe(double sensorYaw)
        {
            for (int i = 0; i < 3; i++)
            {
                // Each group starts after the last observation, too. An
                // immediate first sample would be a duplicate for the policy.
                await Task.Delay(1050);
                Sample(sensorYaw);
                heading.ObserveIndependentContinuity(C(0), null);
            }
        }
        async Task<ArHeadingAlignmentService.HeadingAlignmentResult?> CaptureStable(double sensorYaw)
        {
            Sample(sensorYaw);
            var capture = heading.CaptureAsync(C(0), null);
            for (int i = 0; i < 45 && !capture.IsCompleted; i++)
            {
                Sample(sensorYaw);
                await Task.Delay(110);
            }
            return await capture;
        }
        bool Apply(double residual = 0)
        {
            Sample();
            var frame = ARCameraPoseBridge.CurrentFrame;
            return heading.TryApplyMovementValidatedYaw(40, 3, residual, NavigationClock.UtcNow,
                generation.SessionGeneration, frame.TrackingEpoch, frame.WorldFrame, out _);
        }
        try
        {
            check(Apply(), "heading offset: walking can establish a heading different from the compass");
            await Observe(0);
            check(heading.LastResult is { Source: "MOVEMENT", IsStable: true, RequiresMovementValidation: false },
                "heading offset: an unchanged biased compass cannot revoke independently validated walking alignment");

            foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
            {
                Sample();
                var frame = ARCameraPoseBridge.CurrentFrame;
                var route = new RouteResult([new(C(0), 0), new(C(100), 100)], 100, algorithm);
                var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
                var fix = tracker.Update(C(0), 5);
                var at = NavigationClock.UtcNow;
                var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 40,
                    generation.SessionGeneration, frame.Anchor.ReferenceGeneration, frame.TrackingEpoch, frame.WorldFrame);
                var registration = new RouteDisplayProgressPolicy(requireInitialConfirmation: true);
                registration.ObserveGps(route, fix, at.AddSeconds(-1));
                registration.ObserveRegistration(route, fix, at.AddSeconds(-1), at, motion);
                check(!registration.TryGetRegisteredReference(route, C(0), at, motion, out _),
                    $"{algorithm}: production registration still waits for a second independent GPS observation");
                registration.ObserveGps(route, fix, at);
                registration.ObserveRegistration(route, fix, at, at, motion);
                check(registration.TryGetRegisteredReference(route, C(0), at, motion, out var reference),
                    $"{algorithm}: confirmed GPS and recovered walking alignment produce a physical route reference");
                var placement = new ARRoutePlacement(generation, frame.Anchor.ReferenceGeneration, frame.Version,
                    Environment.TickCount64, 0, 0, 0, 0, at, "SENSOR_REFERENCE_RECOVERY", 40,
                    TrackingEpoch: frame.TrackingEpoch, WorldFrame: frame.WorldFrame);
                check(new MLDARIntegrationService(new RecordedRoute(route)).PublishProgressWindow(route, 0, C(0),
                        40, 0, 0, sourceSegmentIndex: 0, placement: placement, registeredReference: reference) &&
                    ARRouteBridge.Current.IsAvailable && heading.LastResult is { IsStable: true },
                    $"{algorithm}: the production registered publisher receives geometry without compass lockout");
                if (MldGuidanceBridge.IsMld(algorithm))
                {
                    MldGuidanceBridge.SetRouteOwner(route, 6403);
                    MldGuidanceBridge.RebindPublishedWindow(route, 6403, heading.CalibrationRevision,
                        ARRouteBridge.Current, frame, false, null);
                    MldGuidanceBridge.Publish(route, 6403, heading.CalibrationRevision, ARRouteBridge.Current, frame, true, "fixture");
                    check(MldGuidanceBridge.CanRenderPath(ARRouteBridge.Current, frame, 6403),
                        "MLD: recovered route retains the normal render ownership checks");
                }
            }

            await Observe(25);
            check(heading.LastResult is { IsStable: false, RequiresMovementValidation: true },
                "heading offset: a new sustained sensor/frame disagreement still suspends geometry");
            await Observe(0);
            check(heading.LastResult is { Source: "MOVEMENT", MapToArYawDegrees: 40, IsStable: true },
                "heading offset: restored sensor continuity recovers the exact walking heading without compass rotation");
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Paused", "map switch", false);
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Running", "AR return", false);
            Sample();
            check(heading.LastResult is { IsStable: false }, "heading offset: returning from a pause first withholds the old frame");
            await Observe(0);
            check(heading.LastResult is { IsStable: true } resumed && resumed.TrackingEpoch == ARCameraPoseBridge.CurrentFrame.TrackingEpoch,
                "heading offset: unchanged sensor/AR relationship requalifies walking heading after a map switch");
            check(Apply(20), "heading offset: fixture can retain a partial movement correction");
            await Observe(0);
            check(heading.LastResult is { IsStable: false, RequiresMovementValidation: true },
                "heading offset: compass continuity cannot authorize an unfinished walking correction");

            heading.ResetSessionCalibration("noisy compass startup fixture");
            Sample();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var capture = heading.CaptureAsync(C(0), null, cancellation.Token);
            for (int i = 0; i < 45 && !capture.IsCompleted; i++)
            {
                Sample(i * 37);
                await Task.Delay(110);
            }
            var estimate = await capture;
            check(estimate is { IsAvailable: true, IsStable: false } && heading.LastResult == estimate &&
                    heading.CanCaptureAutomatically,
                "heading startup: noisy compass retains an untrusted candidate for walking recovery and fresh capture retries");
            Sample();
            var current = ARCameraPoseBridge.CurrentFrame;
            check(heading.TryApplyMovementValidatedYaw(40, 3, 0, NavigationClock.UtcNow,
                    generation.SessionGeneration, current.TrackingEpoch, current.WorldFrame, out var validated) && validated.IsStable,
                "heading startup: independent walking can replace the untrusted startup estimate");

            heading.ResetSessionCalibration("same-frame magnetic reacquisition fixture");
            check(await CaptureStable(0) is { Source: "MAGNETIC", IsStable: true },
                "heading recovery: a fresh synchronized magnetic calibration can start the trip");
            await Observe(25);
            check(heading.LastResult is { IsStable: false, RequiresMovementValidation: true } && !heading.CanCaptureAutomatically,
                "heading recovery: a magnetic contradiction first withholds geometry and cannot instantly rotate the route");
            for (int i = 0; i < 48; i++) { Sample(25); await Task.Delay(110); }
            Sample(25);
            check(heading.CanCaptureAutomatically && await CaptureStable(25) is { IsStable: true, Source: "MAGNETIC" } recalibrated &&
                    Math.Abs(recalibrated.MapToArYawDegrees - 25) < .1,
                "heading recovery: persistent magnetic contradiction can reacquire from fresh stable paired samples rather than latch a trip off");
        }
        finally
        {
            heading.ResetSessionCalibration("field-test compass offset fixture complete");
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
