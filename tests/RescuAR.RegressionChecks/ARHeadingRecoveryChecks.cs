using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text.Json;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.AR;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Diagnostics;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

internal static class ARHeadingRecoveryChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(north / 111_195, east / 111_195);
    private static readonly float[] Projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, -1, 0, 0, -.2f, 0];

    private static void Pose(ARRenderGenerationToken generation, long timestamp, float x = 0, float z = 0,
        long? acquiredAt = null, long floor = 1, Quaternion? anchorRotation = null)
    {
        ARTrackingStateBridge.PublishObservation(generation.SessionGeneration, "TRACKING", "NONE", timestamp, false);
        var metadata = new ARFrameMetadata(generation, timestamp, new(generation.GeometryGeneration, 0, 1080, 1920))
            { AcquiredAtMonotonicMilliseconds = acquiredAt ?? Environment.TickCount64 };
        var ground = new ARGroundStateBridge.GroundStateSnapshot(timestamp, generation, floor,
            ARGroundLifecycleState.Verified, ARGroundTrust.Verified, Environment.TickCount64, "fixture");
        ARCameraPoseBridge.PublishFrame(true, "NONE", x, 1.35f, z, 0, 0, 0, 1,
            Projection, .1f, 100, ground, 0, 0, 0, metadata, anchorRotation);
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        var generation = new ARRenderGenerationToken(2400, 2401, 2402);
        ARRenderGenerationBridge.Activate(generation);
        try
        {
            await CheckConvergence(check, generation);
            await CheckLateMagneticCapture(check, generation);
            await CheckMagneticTripRecovery(check, generation);
            CheckObservationPairing(check, generation);
            await CheckProvenance(check);
            CheckTrace(check);
        }
        finally
        {
            ARCameraPoseBridge.Clear();
            ARTrackingStateBridge.Clear();
            ARRouteBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(previous);
            else ARRenderGenerationBridge.Invalidate();
        }
    }

    private static async Task CheckConvergence(Action<bool, string> check, ARRenderGenerationToken generation)
    {
        foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            ARTrackingStateBridge.Clear();
            ARCameraPoseBridge.Clear();
            using var alignment = new ArHeadingAlignmentService();
            alignment.ResetSessionCalibration("field recovery fixture");
            var now = DateTimeOffset.UtcNow;
            Pose(generation, 1);
            check(alignment.TryApplyMovementValidatedYaw(-126.07, 3, 0, now,
                generation.SessionGeneration, 0, ARCameraSpatialController.WorldFrameGeneration, out _),
                $"{algorithm}: retained field calibration seeded in its actual tracked frame");
            var policy = new HeadingRevalidationPolicy();
            double yaw = -126.07, target = 168.7;
            int partials = 0;
            for (int i = 0; i <= 12; i++)
            {
                var tracked = MapToArCoordinates.Rotate(12 * i, 0, target);
                // Real floor handoffs must not erase horizontal walking evidence.
                Pose(generation, i + 2, (float)tracked.X, (float)tracked.Z, floor: i + 1);
                var current = alignment.LastResult!.Value;
                var at = now.AddSeconds(i * 8);
                var frame = ARCameraPoseBridge.CurrentFrame;
                var decision = policy.Evaluate(yaw, C(12 * i), 5, 1.5, 90,
                    RouteMatchConfidence.High, 90, (float)tracked.X, (float)tracked.Z, at,
                    generation.SessionGeneration, frame.WorldFrame, frame.TrackingEpoch,
                    current.RequiresMovementValidation);
                if (!decision.ShouldApplyCorrection) continue;
                check(Math.Abs(decision.AppliedCorrectionDegrees) <= (Math.Abs(decision.AlignmentErrorDegrees) >= 45 ? 20 : 8),
                    $"{algorithm}: recovery preserves bounded heading steps");
                double residual = Math.IEEERemainder(decision.CandidateYawDegrees - decision.CorrectedYawDegrees, 360);
                check(alignment.TryApplyMovementValidatedYaw(decision.CorrectedYawDegrees, decision.ConfirmationCount,
                    residual, at, generation.SessionGeneration, frame.TrackingEpoch, frame.WorldFrame, out var corrected),
                    $"{algorithm}: qualified correction commits independently of route geometry");
                yaw = corrected.MapToArYawDegrees;
                policy.MarkCorrectionApplied(at);
                if (!corrected.IsStable)
                {
                    partials++;
                    check(corrected.RequiresMovementValidation &&
                        await alignment.CaptureAsync(C(0), null) is null && alignment.LastResult == corrected,
                        $"{algorithm}: unstable movement calibration is not returned as recovered or replaced by compass");
                }
                else break;
            }
            check(partials > 0 && alignment.LastResult is { IsStable: true, RequiresMovementValidation: false } &&
                Math.Abs(Math.IEEERemainder(yaw - target, 360)) <= 8,
                $"{algorithm}: the field-test severe error converges while earlier partial steps were unrenderable");
            check(await alignment.CaptureAsync(C(0), null) == alignment.LastResult,
                $"{algorithm}: completed movement calibration remains reusable");

            var before = alignment.LastResult;
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Paused", "fixture", false);
            check(!alignment.TryApplyMovementValidatedYaw(0, 3, 0, now,
                    generation.SessionGeneration, 0, 0, out _) && alignment.LastResult == before,
                $"{algorithm}: a callback during lifecycle pause cannot commit a calibration");
            Pose(generation, 100);
            var live = ARCameraPoseBridge.CurrentFrame;
            check(!alignment.TryApplyMovementValidatedYaw(0, 3, 0, now, generation.SessionGeneration,
                    0, live.WorldFrame, out _) && !alignment.TryApplyMovementValidatedYaw(0, 3, 0, now,
                    generation.SessionGeneration, live.TrackingEpoch, live.WorldFrame + 1, out _),
                $"{algorithm}: stale tracking and world identities cannot overwrite retained calibration");

            var reset = policy.Evaluate(yaw, C(200), 5, 1.5, 90, RouteMatchConfidence.High, 90,
                200, 0, now.AddSeconds(150), generation.SessionGeneration, live.WorldFrame, live.TrackingEpoch);
            check(reset.Disposition == HeadingRevalidationDisposition.CollectingMovement,
                $"{algorithm}: recovery cannot compare walking vectors across pause/resume");
            check(policy.Evaluate(yaw, C(212), 5, 1.5, 90, RouteMatchConfidence.High, 90,
                    212, 0, now.AddSeconds(158), generation.SessionGeneration, live.WorldFrame + 1,
                    live.TrackingEpoch).Disposition == HeadingRevalidationDisposition.CollectingMovement,
                $"{algorithm}: a genuine world change restarts evidence even in the same session");
        }
    }

    private static async Task CheckLateMagneticCapture(Action<bool, string> check, ARRenderGenerationToken generation)
    {
        ARTrackingStateBridge.Clear(); ARCameraPoseBridge.Clear();
        using var heading = new ArHeadingAlignmentService();
        heading.ResetSessionCalibration("magnetic race fixture");
        Pose(generation, 200);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var capture = heading.CaptureAsync(C(0), null, cancellation.Token);
        for (int i = 0; i < 10; i++)
        {
            Pose(generation, 201 + i);
            OrientationSensor.Default.Emit(Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
            if (i == 2)
                check(heading.TryApplyMovementValidatedYaw(160, 3, 0, DateTimeOffset.UtcNow,
                    generation.SessionGeneration, 0, ARCameraSpatialController.WorldFrameGeneration, out _),
                    "movement calibration can complete while an earlier magnetic capture is sampling");
            await Task.Delay(110);
        }
        check(await capture is null && heading.LastResult is { MapToArYawDegrees: 160, Source: "MOVEMENT", IsStable: true },
            "late magnetic results cannot overwrite a newer movement-validated heading");
        check(heading.TryGetIndependentObservation(C(0), null, out var observed) &&
            Math.Abs(observed.MapToArYawDegrees) < .1 && heading.LastResult?.MapToArYawDegrees == 160,
            "independent orientation measurement reports the conflict without rotating the route");
    }

    private static async Task CheckMagneticTripRecovery(Action<bool, string> check, ARRenderGenerationToken generation)
    {
        ARTrackingStateBridge.Clear(); ARCameraPoseBridge.Clear();
        using var heading = new ArHeadingAlignmentService();
        heading.ResetSessionCalibration("captured stop/change-destination fixture");
        long timestamp = 500;
        var pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        void Sample(double yaw)
        {
            Pose(generation, ++timestamp);
            OrientationSensor.Default.Emit(Quaternion.Concatenate(pitch,
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(-yaw * Math.PI / 180))));
        }
        async Task<ArHeadingAlignmentService.HeadingAlignmentResult?> Capture(double yaw)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            Pose(generation, ++timestamp);
            var pending = heading.CaptureAsync(C(0), null, cancellation.Token);
            for (int i = 0; i < 12 && !pending.IsCompleted; i++)
            { Sample(yaw); await Task.Delay(110); }
            return await pending;
        }
        var first = await Capture(0);
        check(first is { Source: "MAGNETIC", IsStable: true },
            "restart: initial magnetic calibration is acquired from fresh stationary frames");
        heading.PrepareForNavigationTrip();
        check(heading.LastResult == first,
            "restart: changing destinations preserves a healthy same-frame calibration");

        ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Paused", "captured frame transition", false);
        ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Running", "fixture resume", false);
        Sample(39);
        check(heading.LastResult is { IsStable: false, RequiresMovementValidation: true } && heading.CanCaptureAutomatically,
            "restart: the captured 39-degree/new-epoch transition schedules fresh calibration rather than permanent walking recovery");
        var recovered = await Capture(39);
        check(recovered is { Source: "MAGNETIC", IsStable: true, RequiresMovementValidation: false } &&
            Math.Abs(recovered.Value.MapToArYawDegrees - 39) < .1 &&
            recovered.Value.TrackingEpoch == ARCameraPoseBridge.CurrentFrame.TrackingEpoch,
            "restart: fresh stationary samples replace the old magnetic frame without reusing its yaw");

        bool withheld = false;
        for (int i = 0; i < 3; i++)
        {
            Sample(0);
            withheld |= heading.ObserveIndependentContinuity(C(0), null);
            if (i < 2) await Task.Delay(1050);
        }
        check(withheld && heading.LastResult is { IsStable: false, RequiresMovementValidation: true } &&
            !heading.CanCaptureAutomatically,
            "restart: a same-frame magnetic contradiction still withholds guidance instead of rotating a live route");
        heading.PrepareForNavigationTrip();
        check(heading.LastResult is null && heading.CanCaptureAutomatically,
            "restart: a new trip discards an already invalid magnetic cache");
        var nextTrip = await Capture(0);
        check(nextTrip is { IsStable: true, Source: "MAGNETIC" },
            "restart: the new trip can acquire alignment without restarting the app or requiring movement");
        var frame = ARCameraPoseBridge.CurrentFrame;
        check(heading.TryApplyMovementValidatedYaw(90, 3, 35, DateTimeOffset.UtcNow,
            generation.SessionGeneration, frame.TrackingEpoch, frame.WorldFrame, out var partial),
            "restart: stronger movement-recovery evidence can be retained");
        heading.PrepareForNavigationTrip();
        check(heading.LastResult == partial && !heading.CanCaptureAutomatically,
            "restart: a destination change cannot override partial movement calibration with the compass");
    }

    private static void CheckObservationPairing(Action<bool, string> check, ARRenderGenerationToken generation)
    {
        ARTrackingStateBridge.Clear(); ARCameraPoseBridge.Clear();
        long mono = Environment.TickCount64;
        var utc = DateTimeOffset.UtcNow;
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .4f);
        Pose(generation, 300, 3, -2, mono - 1_000, anchorRotation: rotation);
        Pose(generation, 301, 20, 5, mono, anchorRotation: rotation);
        check(ARCameraPoseBridge.TryGetObservationFrame(utc.AddSeconds(-1), utc, mono, out var paired, out long skew) &&
            paired.Pose.PositionX == 3 && skew == 0 && !paired.IsFresh && ARCameraPoseBridge.CurrentFrame.Pose.PositionX == 20,
            "GPS acquisition pairs with historical motion without making that old pose renderable");
        check(paired.Anchor.HasRotation && Quaternion.Dot(paired.Anchor.Rotation, rotation) > .999f,
            "verified reference orientation travels with its coherent frame");
        check(!ARCameraPoseBridge.TryGetObservationFrame(utc.AddSeconds(1), utc, mono, out _, out _) &&
            !ARCameraPoseBridge.TryGetObservationFrame(utc.AddSeconds(-4), utc, mono, out _, out _),
            "future and over-age acquisition requests cannot select a historical pose");
        ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Paused", "fixture", false);
        Pose(generation, 302, acquiredAt: mono);
        check(!ARCameraPoseBridge.TryGetObservationFrame(utc.AddSeconds(-1), utc, mono, out _, out _),
            "historical pairing cannot bridge an untracked interval");
        ARCameraPoseBridge.Clear();
        Pose(generation, 303, anchorRotation: new Quaternion(float.NaN, 0, 0, 1));
        check(ARCameraPoseBridge.CurrentFrame.IsTracking && ARCameraPoseBridge.CurrentFrame.Anchor.IsAvailable &&
            !ARCameraPoseBridge.CurrentFrame.Anchor.HasRotation,
            "unusable diagnostic anchor orientation cannot invalidate a valid ground acquisition");
    }

    private static async Task CheckProvenance(Action<bool, string> check)
    {
        var graph = new RoadGraphBuilder().Build(new[] { new GeoJsonRoadFeature { OsmId = "fixture-way",
            Highway = "footway", Coordinates = new[] { C(0), C(100) } } });
        var route = await new AStarRoutingService(graph, "fixture-dataset").FindRouteAsync(C(2, 1), C(80));
        check(route is not null && route.Provenance.Provider == "ASTAR" &&
            route.Provenance.DatasetId == "fixture-dataset" && route.Provenance.SourceWayIds!.Contains("fixture-way") &&
            route.Provenance.RequestedCoordinates![0] == C(2, 1) && route.Provenance.SnappedWaypoints![0] == route.Points[0].Coordinate,
            "offline trace preserves raw inputs, selected source way and actual projected endpoint");
        foreach (bool metadata in new[] { true, false })
        {
            using var client = new HttpClient(new TraceHandler(metadata));
            var online = await new MLDRoutingService(client, "https://fixture.invalid").FindRouteAsync(C(0), C(40));
            check(online is { Points.Count: 3 } && online.Points[1].Coordinate == C(20) &&
                online.Provenance.Provider == "MLD" && online.Provenance.RequestedCoordinates![0] == C(0),
                "online provenance does not change returned route vertices or request inputs");
            check(online!.Provenance.DatasetId == (metadata ? "fixture-osrm" : "unreported") &&
                online.Provenance.DataVersion == (metadata ? "fixture-version" : "unreported") &&
                online.Provenance.SnappedWaypoints!.Count == (metadata ? 2 : 0),
                "only observed server metadata identifies online source data; malformed optional metadata is ignored");
        }
    }

    private static void CheckTrace(Action<bool, string> check)
    {
        var trace = new NavigationGeometryTrace(C(0));
        var points = Enumerable.Range(0, 41).Select(i => new RoutePoint(C(i, 3), i)).ToArray();
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var chunks = trace.RouteChunks(points).ToArray();
            check(chunks.Length == 3 && chunks.SelectMany(c => c.Split('|')).Count() == 41 &&
                chunks.All(c => c.Length < 2_000) && chunks[2].Contains("40:") && trace.Point(C(3)).Contains("3.000"),
                "complete metric geometry survives Logcat chunking and device number culture");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        var bounds = default(ARRouteRenderer.ViewportBounds).Include(new(.2f, .3f), new(.8f, .7f));
        check(bounds.Segments == 1 && bounds.Left == .2f && bounds.Bottom == .7f,
            "separate arrow and ribbon viewport measurements retain their own clipped bounds");
    }

    private sealed class TraceHandler(bool metadata) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonSerializer.Serialize(new { code = "Ok", data_version = metadata ? "fixture-version" : null,
                waypoints = metadata ? new[] { new { location = new object[] { 0d, 0d } }, new { location = new object[] { C(40).Longitude, 0d } } }
                    : new[] { new { location = new object[] { "malformed", 0d } } },
                routes = new[] { new { distance = 40, geometry = new { coordinates = new[] {
                    new[] { 0d, 0d }, new[] { C(20).Longitude, 0d }, new[] { C(40).Longitude, 0d } } } } } });
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            if (metadata) response.Headers.Add("X-RescuAR-Dataset-Id", "fixture-osrm");
            return Task.FromResult(response);
        }
    }
}
