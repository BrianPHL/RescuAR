using System.Numerics;
using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

internal static class VisualRoadAlignmentChecks
{
    private static readonly ARRenderGenerationToken Generation = new(8100, 8101, 8102);
    private static GeoCoordinate C(double east, double north = 0) => new(north / 6371008.8 * 180 / Math.PI,
        east / 6371008.8 * 180 / Math.PI);

    public static void Run(Action<bool, string> check)
    {
        var old = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        ARRenderGenerationBridge.Activate(Generation);
        try
        {
            var boundaries = new List<RoadSurfaceAlignment.Boundary>();
            for (float x = 4.1f; x < 24; x += .4f)
            {
                boundaries.Add(new(new(x, -6)));
                boundaries.Add(new(new(x, 0)));
            }
            check(RoadSurfaceAlignment.TryFit(boundaries, Vector2.Zero, Vector2.UnitX, out var road) &&
                Math.Abs(road.Center.Y + 3) < .01 && Math.Abs(road.Width - 6) < .01,
                "local road: paired complete curbs recover the observed centerline without a walking sample");
            check(!RoadSurfaceAlignment.TryFit(boundaries.Where(b => b.Position.Y == 0).ToArray(),
                Vector2.Zero, Vector2.UnitX, out _), "local road: one visible curb cannot certify a road center");
            check(!RoadSurfaceAlignment.TryFit(boundaries.Select(b => b with { Cropped = true }).ToArray(),
                Vector2.Zero, Vector2.UnitX, out _), "local road: cropped road sections cannot invent the hidden edge");
            check(!RoadSurfaceAlignment.TryFit(boundaries.Where(b => b.Position.X < 10).ToArray(),
                Vector2.Zero, Vector2.UnitX, out _), "local road: insufficient ground-plane span remains unqualified");
            check(!RoadSurfaceAlignment.TryFit(boundaries.Select(b => b with {
                Position = b.Position + new Vector2(0, .05f * b.Position.X * b.Position.X) }).ToArray(),
                Vector2.Zero, Vector2.UnitX, out _), "local road: a curved or intersecting corridor cannot be forced into a straight fit");
            check(RoadSurfaceAlignment.TryAlign(road, new(0, 3), Vector2.UnitX, 0, 7,
                out var reference, out var yaw) && Vector2.Distance(reference, new(0, -3)) < .01 && Math.Abs(yaw) < .01,
                "local road: observed geometry corrects a six-metre synthetic GPS-side error rather than snapping to the camera");
            check(!RoadSurfaceAlignment.TryAlign(road, new(0, 30), Vector2.UnitX, 0, 7, out _, out _) &&
                !RoadSurfaceAlignment.TryAlign(road, Vector2.Zero, Vector2.UnitY, 0, 7, out _, out _) &&
                !RoadSurfaceAlignment.TryAlign(default, Vector2.Zero, Vector2.UnitX, 0, 7, out _, out _),
                "local road: another street, incompatible direction and missing geometry cannot authorize a correction");

            foreach (float cameraYaw in new[] { -80f, -90f, -100f })
            {
                var frame = Frame(1, Quaternion.CreateFromAxisAngle(Vector3.UnitY, cameraYaw * MathF.PI / 180));
                var target = new Vector3(15, 0, -3);
                check(FloodSimulationProjection.TryProjectSegment(target, target + new Vector3(.1f, 0, 0),
                    new(0, 1.4f, 0), new(frame.Pose.RotationX, frame.Pose.RotationY, frame.Pose.RotationZ, frame.Pose.RotationW),
                    frame.Projection, out var pixel, out _) &&
                    RoadSurfaceAlignment.TryGroundPoint(pixel.X, pixel.Y, frame, 1, out var recovered) &&
                    Vector2.Distance(recovered, new(15, -3)) < .005,
                    $"local road: a stationary phone turn to {cameraYaw} degrees preserves the ground point");
            }
            var normal = Frame(1, Quaternion.Identity);
            var zoomed = Frame(1, Quaternion.Identity, zoom: 2);
            check(RoadSurfaceAlignment.TryGroundPoint(.55f, .7f, normal, 1, out var basePoint) &&
                RoadSurfaceAlignment.TryGroundPoint(.55f, .7f, zoomed, 2, out var zoomPoint) &&
                Vector2.Distance(basePoint, zoomPoint) < .001,
                "local road: presentation zoom cannot move semantic ground observations");
            check(!RoadSurfaceAlignment.TryGroundPoint(.5f, .1f, normal, 1, out _) &&
                !RoadSurfaceAlignment.TryGroundPoint(float.NaN, .7f, normal, 1, out _),
                "local road: sky-facing and invalid rays cannot become road evidence");

            const int width = 64, height = 64;
            var labels = Enumerable.Repeat((byte)6, width * height).ToArray();
            var confidence = Enumerable.Repeat((byte)255, labels.Length).ToArray();
            for (int y = 4; y < 60; y++)
            for (int x = 4; x < 60; x++)
                if (x < 22 || x > 40) labels[y * width + x] = 4;
            check(SemanticRoadBoundaries.Extract(labels, confidence, width, height, width, width, 4).Count == 2,
                "local road: separate parallel surfaces stay separate instead of producing an averaged road through an obstacle");
            Array.Fill(confidence, (byte)220);
            check(SemanticRoadBoundaries.Extract(labels, confidence, width, height, width, width, 4).Count == 0,
                "local road: low-confidence labels cannot certify physical alignment");
            check(SemanticRoadBoundaries.Extract(labels, confidence, int.MaxValue, height, width, width, 4).Count == 0,
                "local road: malformed image dimensions fail before allocation or indexing");

            var route = new RouteResult(new[] { new RoutePoint(C(0), 0), new RoutePoint(C(100), 100) }, 100, "MLD");
            var owner = new ARRoadSurfaceBridge.Request(1, route.GeometryFingerprint, Vector2.UnitX, 4);
            ARRoadSurfaceBridge.SetRequest(owner);
            long nowMono = Environment.TickCount64;
            var frame1 = Frame(1, Quaternion.Identity);
            var frame2 = Frame(2, Quaternion.Identity);
            var frame3 = Frame(3, Quaternion.Identity);
            ARRoadSurfaceBridge.Observe(owner, road, frame1, nowMono - 1000);
            ARRoadSurfaceBridge.Observe(owner, road, frame2, nowMono - 500);
            check(ARRoadSurfaceBridge.Current is null,
                "local road: two frames cannot promote the physical reference");
            ARRoadSurfaceBridge.Observe(owner, road, frame3, nowMono);
            var proof = ARRoadSurfaceBridge.Current!;
            check(proof is { Evidence: >= 3 } && proof.IsFor(frame3),
                "local road: three coherent stationary frames establish the reference without detected steps");
            ARRoadSurfaceBridge.Observe(owner, road, frame3, nowMono);
            check(ARRoadSurfaceBridge.Current!.Evidence == proof.Evidence,
                "local road: a duplicate frame cannot count as new alignment evidence");
            check(!proof.IsFor(frame3.AsUnavailable("tracking lost")) &&
                !proof.IsFor(Frame(4, Quaternion.Identity, epoch: 5)) &&
                !proof.IsFor(Frame(4, Quaternion.Identity, world: 1)) &&
                !proof.IsFor(Frame(4, Quaternion.Identity, ground: 4)),
                "local road: tracking, epoch, world and floor changes invalidate the observation");

            var at = DateTimeOffset.UtcNow;
            var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
            var fix = tracker.Update(C(0, 3), 7);
            var policy = new RouteDisplayProgressPolicy(); policy.ObserveGps(route, fix, at);
            var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 0, Generation.SessionGeneration, 3, 4, 0);
            policy.ObserveRegistration(route, fix, at, at, motion);
            check(policy.TrySetVisualReference(route, C(0), reference, motion, proof, frame3, DateTimeOffset.UtcNow),
                "local road: qualified physical evidence replaces the display transform without changing the GPS reading");
            check(policy.TryGetRegisteredReference(route, C(40), at, motion, out var mapped) &&
                Math.Abs(mapped.WorldX - 40) < .01 && Math.Abs(mapped.WorldZ + 3) < .01 &&
                mapped.Source == "VISUAL_ROAD_CORRIDOR" && policy.RegistrationAccuracyMeters == 7,
                "local road: later mapped points share one observed transform while the original GPS uncertainty remains explicit");
            check(policy.ConstrainVisualWindow(route, 0, 40) <= road.Span,
                "local road: the displayed window cannot exceed the observed straight-corridor span");
            tracker.ApplyFusionCorrection(6.7, fix, "CAPTURED_STATIONARY_STEPS");
            var progressBeforeVisual = tracker.Current.CommittedProgressMeters;
            check(policy.TryGetVisualDisplayPosition(route, at, motion, out var stationary) &&
                Math.Abs(stationary.ProgressMeters) < .01 &&
                policy.TryGetVisualDisplayPosition(route, at, motion with { X = 5 }, out var walked) &&
                Math.Abs(walked.ProgressMeters - 5) < .01 && tracker.Current.CommittedProgressMeters == progressBeforeVisual,
                "local road: AR displacement advances display only; a stationary camera does not inherit fictitious step progress");
            check(policy.TryGetVisualReference(route, C(0), at.AddSeconds(4), motion, out var retained) &&
                retained.RegisteredAt == mapped.RegisteredAt,
                "local road: looking away does not erase a physical reference in the same continuous live AR frame");
            check(!policy.TryGetVisualReference(new(route.Points, 100, "MLD"), C(0), at, motion, out _) &&
                !policy.TryGetVisualReference(route, C(0), at, motion with { WorldFrame = 1 }, out _),
                "local road: route replacement and changed world cannot reuse the physical reference");
            check(policy.RenewVisualSupport(route, proof, frame3, C(0), Vector2.UnitX, at.AddSeconds(2), motion) &&
                policy.TryGetVisualReference(route, C(0), at.AddSeconds(4), motion, out var repeated) &&
                repeated.RegisteredAt == mapped.RegisteredAt,
                "local road: reading the same proof again cannot change its acquisition time");
            var wrongCorridor = proof with { Line = road with { Center = road.Center + new Vector2(0, 2) } };
            check(!policy.RenewVisualSupport(route, wrongCorridor, frame3, C(0), Vector2.UnitX, at, motion) &&
                !policy.TryGetVisualReference(route, C(0), at, motion, out _) &&
                policy.RenewVisualSupport(route, proof, frame3, C(0), Vector2.UnitX, at, motion),
                "local road: contradictory visible geometry withholds the retained reference without dragging it to another corridor");
            ARRoadSurfaceBridge.SetRequest(owner with { Trip = 2 });
            ARRoadSurfaceBridge.Observe(owner, road, Frame(4, Quaternion.Identity), nowMono);
            check(ARRoadSurfaceBridge.Current is null,
                "local road: an old trip's native observation cannot populate a replacement request");
        }
        finally
        {
            ARRoadSurfaceBridge.SetRequest(null);
            if (accepting) ARRenderGenerationBridge.Activate(old); else ARRenderGenerationBridge.Invalidate();
        }
    }

    private static ARCameraPoseBridge.SpatialSnapshot Frame(long timestamp, Quaternion rotation,
        long epoch = 4, long world = 0, long ground = 3, float zoom = 1)
    {
        var projection = new ARCameraPoseBridge.ProjectionSnapshot(true,
            2 * zoom, 0, .07f * zoom, 0, 0, 2 * zoom, -.04f * zoom, 0,
            0, 0, -1.0002f, -.020002f, 0, 0, -1, 0, .01f, 100);
        var floor = new ARGroundStateBridge.GroundStateSnapshot(1, Generation, ground,
            ARGroundLifecycleState.Verified, ARGroundTrust.Verified, Environment.TickCount64, "fixture");
        return new(timestamp, new(Generation, timestamp, new(Generation.GeometryGeneration, 0, 1080, 1798)),
            Environment.TickCount64, true, "NONE", new(true, 0, 1.4f, 0, rotation.X, rotation.Y, rotation.Z, rotation.W, timestamp),
            projection, new(floor, 0, 0, 0), epoch, world);
    }
}
