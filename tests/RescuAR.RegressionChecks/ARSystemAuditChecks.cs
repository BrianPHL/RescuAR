using System.Buffers;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.AR;
using RescuAR.MAUI.Services;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;

internal static class ARSystemAuditChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (bool activityFirst in new[] { true, false })
        {
            var intent = new ArCoreRunIntent();
            intent.Request(true);
            intent.SetSurfaceReady(true);
            check(intent.ShouldRun, "requested camera has both prerequisites");
            intent.SetActivityResumed(false);
            intent.SetActivityResumed(false);
            intent.SetSurfaceReady(false);
            intent.SetSurfaceReady(false);
            check(intent.Requested && !intent.ShouldRun, "duplicate pauses retain camera intent");
            if (activityFirst) intent.SetActivityResumed(true);
            else intent.SetSurfaceReady(true);
            check(!intent.ShouldRun, "one returning prerequisite cannot resume alone");
            if (activityFirst) intent.SetSurfaceReady(true);
            else intent.SetActivityResumed(true);
            check(intent.ShouldRun, "both resume callback orders work");
            intent.SetSurfaceReady(false);
            intent.Request(false);
            intent.SetActivityResumed(true);
            intent.SetSurfaceReady(true);
            check(!intent.ShouldRun, "late surface cannot undo Camera-tab exit");
            intent.Request(true);
            check(intent.ShouldRun, "explicit camera re-entry resumes");
            intent.Shutdown();
            intent.SetActivityResumed(true);
            intent.SetSurfaceReady(true);
            intent.Request(true);
            check(intent.IsShutdown && !intent.ShouldRun, "late callbacks cannot revive shutdown");
        }

        var pin = new GeoCoordinate(14.697875, 121.078949);
        NavigationDestinationBridge.Set("Trip A", pin, 30);
        var firstTrip = NavigationDestinationBridge.Current;
        NavigationDestinationBridge.Clear();
        NavigationDestinationBridge.Set("Trip A", pin, 30);
        var nextTrip = NavigationDestinationBridge.Current;
        check(firstTrip.Version < nextTrip.Version && firstTrip != nextTrip,
            "reselecting the same destination starts a distinct trip");

        var arrival = new SafeZoneConfirmationService();
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        SafeZoneConfirmationService.SafeZoneDecision Observe(long trip, int seconds,
            GeoCoordinate? destination = null) => arrival.Evaluate(pin, destination ?? pin,
                30, 5, 0, start.AddSeconds(seconds), evaluatedAt: start.AddSeconds(seconds),
                destinationVersion: trip);
        Observe(firstTrip.Version, 0);
        Observe(firstTrip.Version, 3);
        check(Observe(firstTrip.Version, 6).IsConfirmed, "arrival confirms repeated accurate observations for its trip");
        check(!Observe(nextTrip.Version, 7).IsConfirmed && arrival.Current.ConfirmationCount == 1,
            "a previous arrival cannot auto-confirm a new trip to the same pin");
        check(!Observe(firstTrip.Version, 8).IsAvailable && arrival.Current.DestinationVersion == nextTrip.Version &&
            arrival.Current.ConfirmationCount == 1, "an old trip callback cannot reset or confirm current arrival");
        check(Observe(nextTrip.Version, 10, new GeoCoordinate(14.7, 121.08)).ConfirmationCount == 0,
            "moving the selected pin resets arrival observations");
        NavigationDestinationBridge.Clear();

        var hold = new ARCameraSpatialController.SpatialContinuitySnapshot(
            ARCameraSpatialController.SpatialContinuityState.ShortHold, 100, 100, "test hold");
        ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot Guidance(bool fresh = true,
            bool identitySuspended = false, bool heading = true, bool rerouting = false,
            int trust = 100) => ARGuidanceConfidencePolicy.Evaluate(true, true, RouteVisualKind.RouteWindow,
                true, fresh, GpsPdrFusionPolicy.GpsConfidence.High, RouteMatchConfidence.High,
                identitySuspended, heading,
                trust == 100 ? hold : new(hold.State, 100, trust, "test hold"), rerouting, false);
        check(Guidance().AllowsRouteGeometry, "brief tracking loss preserves guidance when every independent input is trusted");
        check(!Guidance(fresh: false).AllowsRouteGeometry, "short tracking hold cannot bypass stale GPS");
        check(!Guidance(identitySuspended: true).AllowsRouteGeometry, "short tracking hold cannot bypass uncertain route identity");
        check(!Guidance(heading: false).AllowsRouteGeometry, "short tracking hold cannot bypass unverified heading");
        check(!Guidance(rerouting: true).AllowsRouteGeometry, "short tracking hold cannot bypass route replacement");
        check(!Guidance(trust: 0).AllowsRouteGeometry, "short tracking hold requires enough placement trust");

        var budget = new ARGroundProbeBudget();
        for (int i = 0; i < ARGroundProbeBudget.Capacity; i++)
            if (!budget.TryConsume(0)) throw new InvalidOperationException("probe budget exhausted early");
        check(!budget.TryConsume(1) && !budget.HasCapacity(ARGroundProbeBudget.WindowMilliseconds - 1),
            "ground probing remains bounded throughout its budget window");
        check(budget.TryConsume(ARGroundProbeBudget.WindowMilliseconds) && budget.Used == 1,
            "a retained session can reacquire ground after its budget renews");
        check(budget.TryConsume(0) && budget.Used == 1, "a monotonic clock reset cannot permanently lock ground recovery");
        budget.Reset();
        check(budget.Used == 0 && budget.TryConsume(0), "a new session starts with a clean probe budget");

        var generation = new ARRenderGenerationToken(900, 901, 902);
        ARRenderGenerationBridge.Activate(generation);
        var ground = ARGroundStateBridge.PublishProvisional(generation, "audit fixture");
        var oldGround = ARGroundStateBridge.Current;
        ARGroundStateBridge.PublishVerified(generation with { GraphicsGeneration = 899 }, "old graphics");
        check(ARGroundStateBridge.Current == oldGround, "old graphics cannot overwrite ground in the same retained session");
        ARGroundStateBridge.BeginSearch(generation with { GeometryGeneration = 899 }, false, "old geometry");
        check(ARGroundStateBridge.Current == oldGround, "old display geometry cannot invalidate current ground");

        var metadata = new ARFrameMetadata(generation, 1_000_000_000,
            new ARDisplayGeometrySnapshot(902, 0, 1080, 1920));
        long now = Environment.TickCount64;
        var delayed = metadata with { AcquiredAtMonotonicMilliseconds = now - 1_000 };
        check(!delayed.IsFresh(750, now) && metadata.IsFresh(750, now),
            "queueing a camera frame does not reset its acquisition age");
        check(!(metadata with { AcquiredAtMonotonicMilliseconds = now + 1 }).IsFresh(750, now),
            "future camera acquisition times are rejected");
        float[] projection = [1,0,0,0, 0,1,0,0, 0,0,-1,-1, 0,0,-0.2f,0];
        void Pose(ARFrameMetadata frame, float x = 0, float w = 1, float[]? matrix = null,
            float anchorY = 0) => ARCameraPoseBridge.PublishFrame(true, "", x, 1.5f, 0,
                0, 0, 0, w, matrix ?? projection, 0.1f, 100, ground, 0, anchorY, 0, frame);
        Pose(metadata);
        check(ARCameraPoseBridge.CurrentFrame.IsTracking && ARCameraPoseBridge.CurrentFrame.Anchor.IsAvailable,
            "valid coherent pose and ground remain available");
        Pose(delayed);
        check(!ARCameraPoseBridge.CurrentFrame.IsTracking, "delayed pose publication does not refresh stale tracking");
        Pose(metadata, x: float.NaN);
        check(!ARCameraPoseBridge.CurrentFrame.IsTracking, "non-finite camera position cannot reach rendering");
        Pose(metadata, w: 0);
        check(!ARCameraPoseBridge.CurrentFrame.IsTracking, "a zero camera quaternion is unavailable");
        var invalidProjection = (float[])projection.Clone(); invalidProjection[7] = float.NaN;
        Pose(metadata, matrix: invalidProjection);
        check(!ARCameraPoseBridge.CurrentFrame.IsTracking, "non-finite camera projection cannot reach rendering");
        Pose(metadata, anchorY: float.NaN);
        check(ARCameraPoseBridge.CurrentFrame.IsTracking && !ARCameraPoseBridge.CurrentFrame.Anchor.IsAvailable,
            "invalid ground coordinates withhold floor placement without discarding the valid camera pose");

        ARCameraPoseBridge.SpatialSnapshot TrackedDepthPose(ARFrameMetadata frame)
        {
            ARTrackingStateBridge.PublishObservation(generation.SessionGeneration, "TRACKING", "NONE", frame.FrameTimestamp, true);
            Pose(frame);
            return ARCameraPoseBridge.CurrentFrame;
        }

        bool Depth(ARCameraPoseBridge.SpatialSnapshot frame, int width = 1, int height = 1, float principal = 0,
            float[]? uv = null, long? imageTimestamp = null, ushort sample = 750)
        {
            var buffer = ArrayPool<ushort>.Shared.Rent(1);
            buffer[0] = sample;
            bool accepted = ARDepthOcclusionBridge.Publish(imageTimestamp ?? frame.FrameTimestamp, frame, width, height, buffer,
                uv ?? [0,0, 1,0, 0,1, 1,1], 0,1.5f,0, 0,0,0,1,
                1,1, principal,0, 1,1);
            if (!accepted) ArrayPool<ushort>.Shared.Return(buffer);
            return accepted;
        }

        ARDepthOcclusionBridge.Clear();
        long outstanding = ARDepthOcclusionBridge.OutstandingBufferCount;
        var depthPose = TrackedDepthPose(metadata);
        check(!Depth(depthPose, imageTimestamp: 0) && !Depth(depthPose, imageTimestamp: depthPose.FrameTimestamp - 1) &&
            !Depth(depthPose, imageTimestamp: depthPose.FrameTimestamp + 1),
            "the native depth timestamp must exactly match the camera optics and pose before taking ownership");
        check(Depth(depthPose), "valid depth takes ownership of its pooled buffer");
        using var renderRead = ARFrameCoherencePolicy.AcquireDepthForSpatialFrame(depthPose) ??
            throw new InvalidOperationException("Depth renderer fixture did not acquire the snapshot.");
        using var floodRead = ARFrameCoherencePolicy.AcquireDepthForSpatialFrame(depthPose) ??
            throw new InvalidOperationException("Depth flood fixture did not acquire the snapshot.");
        long oldDepthVersion = renderRead.Snapshot.Version;

        var laterPose = TrackedDepthPose(metadata with { FrameTimestamp = 1_100_000_000 });
        using (var latestRead = ARFrameCoherencePolicy.AcquireDepthForSpatialFrame(laterPose))
            check(latestRead?.Snapshot.Version == oldDepthVersion, "a bounded older depth sample can use its own tracked origin");
        var earlyZoomedPose = TrackedDepthPose(metadata with { FrameTimestamp = 1_125_000_000, CameraZoomRatio = 2.0f });
        using (var wrongZoomRead = ARFrameCoherencePolicy.AcquireDepthForSpatialFrame(earlyZoomedPose))
            check(wrongZoomRead is null, "a pre-zoom depth crop cannot be applied to a post-zoom camera pose");
        var zoomedPose = TrackedDepthPose(metadata with { FrameTimestamp = 1_150_000_000, CameraZoomRatio = 2.0f });
        check(Depth(zoomedPose, sample: 1200), "matching post-zoom depth replaces the active publication");
        using (var futureDepthRead = ARFrameCoherencePolicy.AcquireDepthForSpatialFrame(earlyZoomedPose))
            check(futureDepthRead is null, "depth from a later frame must wait for its camera presentation");

        long newDepthVersion;
        using (var latestRead = ARFrameCoherencePolicy.AcquireDepthForSpatialFrame(zoomedPose) ??
               throw new InvalidOperationException("Replacement depth fixture did not acquire the snapshot."))
        {
            newDepthVersion = latestRead.Snapshot.Version;
            ARDepthOcclusionBridge.Clear(oldDepthVersion);
            check(ARDepthOcclusionBridge.HasCurrentDepth && latestRead.Snapshot.DepthMillimeters[0] == 1200,
                "a stale reader cannot clear a newer depth publication");
        }

        var pausedPose = zoomedPose.Metadata with { FrameTimestamp = 1_160_000_000 };
        ARTrackingStateBridge.PublishObservation(generation.SessionGeneration, "PAUSED", "INSUFFICIENT_FEATURES",
            pausedPose.FrameTimestamp, true);
        ARCameraPoseBridge.PublishTrackingUnavailable("INSUFFICIENT_FEATURES", pausedPose);
        var recoveredPose = TrackedDepthPose(zoomedPose.Metadata with { FrameTimestamp = 1_175_000_000 });
        using (var previousEpochRead = ARFrameCoherencePolicy.AcquireDepthForSpatialFrame(recoveredPose))
            check(previousEpochRead is null && recoveredPose.TrackingEpoch != zoomedPose.TrackingEpoch,
                "a bounded depth image from the previous tracking epoch cannot compose over a recovered pose");

        check(ARDepthOcclusionBridge.OutstandingBufferCount == outstanding + 2 &&
            renderRead.Snapshot.DepthMillimeters[0] == 750 && floodRead.Snapshot.DepthMillimeters[0] == 750,
            "replacing depth preserves the previous image until both concurrent readers release it");
        renderRead.Dispose();
        renderRead.Dispose();
        check(!renderRead.Snapshot.IsAvailable && ARDepthOcclusionBridge.OutstandingBufferCount == outstanding + 2 &&
            floodRead.Snapshot.DepthMillimeters[0] == 750,
            "a reader's duplicate disposal cannot release another reader's buffer");
        ARDepthOcclusionBridge.Clear(newDepthVersion);
        check(!ARDepthOcclusionBridge.HasCurrentDepth && ARDepthOcclusionBridge.OutstandingBufferCount == outstanding + 1,
            "clear returns unread current depth immediately and keeps a retired image that a reader owns");
        floodRead.Dispose();
        check(ARDepthOcclusionBridge.OutstandingBufferCount == outstanding,
            "the last release returns depth even when no further frames are published");

        check(!Depth(TrackedDepthPose(delayed)), "delayed depth cannot appear newly acquired");
        var qualifiedPose = TrackedDepthPose(metadata with { FrameTimestamp = 1_200_000_000 });
        check(!Depth(qualifiedPose, width: 65_536, height: 65_536), "depth dimensions cannot overflow buffer validation");
        check(!Depth(qualifiedPose, principal: float.NaN), "depth rejects invalid optical intrinsics");
        check(!Depth(qualifiedPose, uv: [0,0, 1,0, 0,float.NaN, 1,1]), "depth rejects invalid texture coordinates");
        if (!Depth(qualifiedPose)) throw new InvalidOperationException("Depth geometry-change fixture did not publish.");
        using (var beforeResize = ARFrameCoherencePolicy.AcquireDepthForSpatialFrame(qualifiedPose) ??
               throw new InvalidOperationException("Depth geometry-change fixture did not acquire the snapshot."))
        {
            ARRenderGenerationBridge.Activate(generation with { GeometryGeneration = generation.GeometryGeneration + 1 });
            check(!ARDepthOcclusionBridge.HasCurrentDepth && ARDepthOcclusionBridge.OutstandingBufferCount == outstanding + 1 &&
                beforeResize.Snapshot.DepthMillimeters[0] == 750,
                "display generation change stops admitting a snapshot while its existing reader retains the pixels");
        }
        check(ARDepthOcclusionBridge.OutstandingBufferCount == outstanding,
            "disposing the last reader reclaims depth after a generation change");
        ARRenderGenerationBridge.Activate(generation);
        ARGroundStateBridge.Clear("audit fixture complete");
        ARCameraPoseBridge.Clear();
        ARTrackingStateBridge.Clear();

        check(!ArCoreLifecyclePolicy.CanApplyState(ArCoreLifecycleState.Running, ArCoreLifecycleState.Faulted, 1, 2),
            "a superseded lifecycle timeout cannot fault a newer request");
        check(!ArCoreLifecyclePolicy.CanApplyState(ArCoreLifecycleState.Disposed, ArCoreLifecycleState.Running, 2, 2) &&
            !ArCoreLifecyclePolicy.CanApplyState(ArCoreLifecycleState.Disposed, ArCoreLifecycleState.Faulted, null, 2),
            "late resume or failure cannot revive a disposed service");
        check(!ArCoreLifecyclePolicy.CanApplyState(ArCoreLifecycleState.Disposing, ArCoreLifecycleState.Running, 2, 2) &&
            ArCoreLifecyclePolicy.CanApplyState(ArCoreLifecycleState.Disposing, ArCoreLifecycleState.Disposed, 2, 2),
            "shutdown allows completion while rejecting reactivation");
        check(ArCoreLifecyclePolicy.CanApplyState(ArCoreLifecycleState.Paused, ArCoreLifecycleState.Running, 2, 2),
            "the current request can still resume a retained session");

        using var heading = new ArHeadingAlignmentService();
        using (var external = SharedMotionSensorLeaseManager.AcquireOrientation("other consumer", (_, _) => { }))
        {
            heading.Start(); heading.Start(); heading.Stop();
            check(!heading.IsStarted && OrientationSensor.Default.IsMonitoring,
                "heading releases exactly its own lease and preserves another orientation consumer");
        }
        check(!OrientationSensor.Default.IsMonitoring, "orientation stops after the last consumer releases it");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        OrientationSensor.Default.BeforeStart = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("fake sensor start");
        };
        Task starting = Task.Run(heading.Start);
        if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("heading start was not scheduled");
        Task stopping = Task.Run(heading.Stop);
        // Force overlap while sensor acquisition is held; this is a bounded
        // concurrency unit test, not a hardware timing assertion.
        stopping.Wait(100);
        release.Set();
        await Task.WhenAll(starting, stopping);
        OrientationSensor.Default.BeforeStart = null;
        check(!heading.IsStarted && !OrientationSensor.Default.IsMonitoring,
            "stopping during orientation acquisition cannot leave an orphaned sensor lease");
        heading.Dispose();
        bool disposedRejected = false;
        try { heading.Start(); } catch (ObjectDisposedException) { disposedRejected = true; }
        check(disposedRejected && !OrientationSensor.Default.IsMonitoring, "a disposed heading service cannot reacquire a sensor");

        var pdr = new PedestrianDeadReckoningService();
        using (var external = SharedMotionSensorLeaseManager.AcquireAccelerometer("other consumer", (_, _) => { }))
        {
            pdr.Start(); pdr.Start(); pdr.Stop();
            check(!pdr.IsRunning && Accelerometer.Default.IsMonitoring,
                "PDR releases exactly its own accelerometer lease");
            pdr.Start();
        }
        check(pdr.IsRunning && Accelerometer.Default.IsMonitoring, "PDR restart retains the new lease when another consumer stops");
        pdr.Stop(); pdr.Stop();
        check(!Accelerometer.Default.IsMonitoring, "repeated PDR stop releases the last lease once");
    }
}
