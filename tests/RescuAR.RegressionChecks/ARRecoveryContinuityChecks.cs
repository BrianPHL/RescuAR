using RescuAR.AR;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

internal static class ARRecoveryContinuityChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(north / 111_195, east / 111_195);
    private static RouteResult Route(string algorithm, params GeoCoordinate[] coordinates)
    {
        double distance = 0;
        var points = new List<RoutePoint>();
        foreach (var coordinate in coordinates)
        {
            if (points.Count > 0) distance += points[^1].Coordinate.DistanceTo(coordinate);
            points.Add(new(coordinate, distance));
        }
        return new(points, distance, algorithm);
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var turns = new PedestrianTurnGuidanceService();
        foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            foreach (double bearing in new[] { 0d, 90, 180, 270 })
            foreach (double turn in new[] { -90d, 90, 180 })
            {
                double radians = bearing * Math.PI / 180, after = (bearing + turn) * Math.PI / 180;
                var corner = C(15 * Math.Sin(radians), 15 * Math.Cos(radians));
                var end = C(15 * Math.Sin(radians) + 30 * Math.Sin(after),
                    15 * Math.Cos(radians) + 30 * Math.Cos(after));
                var route = Route(algorithm, C(0), corner, end);
                var geographic = turns.Evaluate(route, 0);
                var expected = turn == 180 ? PedestrianTurnGuidanceService.TurnInstruction.UTurn :
                    turn > 0 ? PedestrianTurnGuidanceService.TurnInstruction.Right : PedestrianTurnGuidanceService.TurnInstruction.Left;
                foreach (double yaw in new[] { -151.09, 0, 90, 151.45, 179 })
                {
                    var ar = ArRouteAlignment.Rotate(LocalRouteProjector.ProjectWindow(route, C(0), 40), yaw);
                    var visible = turns.EvaluateVisibleRoute(ar);
                    check(geographic.IsAvailable && visible.IsAvailable && geographic.Instruction == expected &&
                        visible.Instruction == expected,
                        $"{algorithm}: geographic and projected turns agree for bearing {bearing}, turn {turn}, yaw {yaw}");
                }
            }

            var straight = Route(algorithm, C(0), C(200));
            RouteBacktrackingPolicy.TrackedMotion Motion(float x, long ground = 1, long epoch = 0, long world = 0) =>
                new(x, 0, 0, 1, ground, epoch, world);
            var gate = new RoutePositionObservationPolicy();
            var seed = gate.Evaluate(straight, C(0), 15.5, now, now, Motion(0));
            var jumped = gate.Evaluate(straight, C(58.73), 14.5, now.AddSeconds(3.7), now.AddSeconds(3.7), Motion(0, 2));
            check(!seed.IsHeld && jumped.IsHeld && Math.Abs(jumped.ResidualMeters - 58.73) < 0.1,
                $"{algorithm}: the recorded 58.73 m GPS jump cannot relocate a stationary tracked window");
            check(gate.Evaluate(straight, C(58.73), 39.95, now.AddSeconds(5), now.AddSeconds(5), Motion(0, 3)).IsHeld,
                $"{algorithm}: a weak interleaved fix contributes no relocation or identity-reset evidence");
            var corrected = gate.Evaluate(straight, C(60), 14, now.AddSeconds(7), now.AddSeconds(7), Motion(0, 4));
            check(!corrected.IsHeld && corrected.IsConfirmedCorrection,
                $"{algorithm}: independent coherent fixes can promptly correct an earlier wrong position");
            check(gate.Evaluate(straight, C(60), 14, now.AddSeconds(7), now.AddSeconds(7), Motion(0)).IsHeld &&
                gate.Evaluate(straight, C(0), 5, now.AddSeconds(50), now.AddSeconds(7), Motion(0)).IsHeld &&
                !gate.Evaluate(straight, C(60), 14, now.AddSeconds(8), now.AddSeconds(8), Motion(0)).IsHeld,
                $"{algorithm}: duplicate/future samples cannot confirm a jump or poison the next real observation");
            gate.Reset();

            // Compound sequence: alternating distant/weak fixes never reach the
            // tracker or fusion policy; real walking retains route identity.
            var tracker = new RouteProgressTracker();
            tracker.SetRoute(straight);
            var fusion = new GpsPdrFusionPolicy();
            gate.Evaluate(straight, C(50), 6.5, now, now, Motion(0));
            fusion.EvaluateGps(50, true, tracker.Update(C(50), 6.5), now);
            var far = gate.Evaluate(straight, C(130), 15, now.AddSeconds(2), now.AddSeconds(2), Motion(2, 2));
            var weak = gate.Evaluate(straight, C(130), 40, now.AddSeconds(4), now.AddSeconds(4), Motion(4, 3));
            check(far.IsHeld && weak.IsHeld && Math.Abs(tracker.Current.CommittedProgressMeters - 50) < 0.1,
                $"{algorithm}: alternating segment jumps and weak fixes cannot advance the accepted trip");
            var walking = gate.Evaluate(straight, C(56), 6.5, now.AddSeconds(6), now.AddSeconds(6), Motion(6, 4));
            var real = tracker.Update(C(56), 6.5);
            var fused = fusion.EvaluateGps(50, true, real, now.AddSeconds(6));
            check(!walking.IsHeld && !fusion.IsRouteIdentitySuspended && fused.TargetProgressMeters > 55,
                $"{algorithm}: continued walking recovers without replaying the rejected segment hypotheses");
            check(!gate.Evaluate(straight, C(56, 40), 6.5, now.AddSeconds(7), now.AddSeconds(7),
                    Motion(6, 5) with { Z = -40 }).IsHeld && tracker.Update(C(56, 40), 6.5).IsOffRoute,
                $"{algorithm}: actual tracked departure still reaches ordinary off-route matching");
            check(!gate.Evaluate(straight, C(130), 6.5, now.AddSeconds(8), now.AddSeconds(8),
                    Motion(0, 6, epoch: 1)).IsHeld &&
                !gate.Evaluate(straight, C(10), 6.5, now.AddSeconds(9), now.AddSeconds(9),
                    Motion(0, 7, epoch: 1, world: 1)).IsHeld,
                $"{algorithm}: tracking loss and world correction start new motion comparisons");

            var reverse = new RouteBacktrackingPolicy();
            tracker.SetRoute(straight);
            reverse.Evaluate(straight, tracker.Update(C(100), 6), now, now, trackedMotion: Motion(0));
            var one = reverse.Evaluate(straight, tracker.Update(C(88), 6), now.AddSeconds(8), now.AddSeconds(8),
                trackedMotion: Motion(-12, 2));
            var two = reverse.Evaluate(straight, tracker.Update(C(84), 6), now.AddSeconds(10), now.AddSeconds(10),
                trackedMotion: Motion(-16, 3));
            check(one.ConfirmationCount == 1 && two.ShouldReplan && two.EvidenceSource == "GPS_AND_AR",
                $"{algorithm}: genuine reverse walking survives ordinary floor-reference swaps");
            reverse.Reset();
            tracker.SetRoute(straight);
            reverse.Evaluate(straight, tracker.Update(C(100), 6), now, now, trackedMotion: Motion(0));
            check(!reverse.Evaluate(straight, tracker.Update(C(70), 6), now.AddSeconds(8), now.AddSeconds(8),
                    trackedMotion: Motion(0, 2)).IsCandidate,
                $"{algorithm}: stationary tracked poses veto a GPS-only reverse displacement");

            await CheckDestinationExclusion(check, straight, now);
            CheckGroundAndPreview(check, straight, now);
        }
    }

    private static async Task CheckDestinationExclusion(Action<bool, string> check, RouteResult route, DateTimeOffset now)
    {
        var hazard = new RouteHazard("end", C(200), 20, "Flood Warning", "Medium", "fixture", "fixture", now);
        var hazards = new[] { hazard };
        int providerCalls = 0;
        var hybrid = new HybridRoutingService(new EmptyRouter(() => providerCalls++),
            _ => throw new InvalidOperationException("An excluded destination must not initialize the graph."), () => true);
        var outcome = await hybrid.FindReplacementRouteResultAsync(route, C(0), C(200),
            _ => throw new InvalidOperationException("No access lookup for an excluded destination."), hazards);
        check(outcome.Route is null && outcome.Failure == RouteReplacementFailure.DestinationExcluded && providerCalls == 0,
            $"{route.Algorithm}: hazardous destinations return an invariant typed failure before either provider runs");
        var monitor = new RouteHazardMonitor();
        var twoSegments = Route(route.Algorithm, C(0), C(100), C(200));
        monitor.AcceptRoute(twoSegments, C(0));
        var failed = monitor.Assess(twoSegments, 10, hazards, true);
        monitor.MarkNoRoute(failed.Retry, outcome.Failure);
        monitor.AllowRetryForPositionChange(twoSegments);
        var moved = monitor.Assess(twoSegments, 110, hazards, false);
        check(!monitor.CanRetry(moved.Retry) && monitor.IsDestinationExcluded(moved.Retry),
            $"{route.Algorithm}: movement, new segments and connectivity cannot retry the same excluded destination");
        var changed = monitor.Assess(twoSegments, 110, [new RouteHazard("end", C(200), 5,
            "Flood Warning", "Medium", "fixture", "fixture", now)], false);
        check(monitor.CanRetry(changed.Retry) && !monitor.MarkNoRoute(failed.Retry, outcome.Failure),
            $"{route.Algorithm}: hazard revisions reconsider the destination and reject stale failures");
        monitor.AcceptRoute(twoSegments, C(0));
        check(monitor.CanRetry(monitor.Assess(twoSegments, 10, hazards, true).Retry),
            $"{route.Algorithm}: a newly accepted trip does not inherit a previous destination failure");
        var noRoute = new HybridRoutingService(new EmptyRouter(() => providerCalls++),
            _ => Task.FromResult(new RoadGraph(new Dictionary<int, RoadNode>(), new List<RoadEdge>())), () => true);
        var transient = await noRoute.FindReplacementRouteResultAsync(route, C(0), C(200),
            _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(null));
        check(transient.Failure == RouteReplacementFailure.NoRoute,
            $"{route.Algorithm}: ordinary unavailable routing remains distinct from destination exclusion");
    }

    private static void CheckGroundAndPreview(Action<bool, string> check, RouteResult route, DateTimeOffset now)
    {
        var previousGeneration = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        var generation = new ARRenderGenerationToken(1500, 1501, 1502);
        ARRenderGenerationBridge.Activate(generation);
        try
        {
            var publisher = new MLDARIntegrationService(new EmptyRouter(() => { }));
            var placement = new ARRoutePlacement(generation, 1, 1, Environment.TickCount64,
                10, 0, 12, 0, now, "GPS", 0);
            var tracker = new RouteProgressTracker();
            tracker.SetRoute(route);
            var update = tracker.Update(C(0, 13), 7);
            check(MappedRoutePreviewPolicy.CanPublish(update.IsAccepted, update.IsOffRoute, update.MatchConfidence,
                    update.AccuracyMeters, update.GpsCoordinate, update.SnappedCoordinate, now, now, true,
                    initialApproachPending: false, returningToRoute: true) &&
                publisher.PublishProgressWindow(route, update.RawProgressMeters, update.SnappedCoordinate, 0,
                    2, 0, arWindowMeters: 7.5, userCoordinate: update.GpsCoordinate, placement: placement),
                $"{route.Algorithm}: recovery can publish mapped geometry 13 m from the road without initial-trip state");
            var original = ARRouteBridge.Current;
            var referenceMotion = new RouteBacktrackingPolicy.TrackedMotion(12, 0, 0, generation.SessionGeneration, 1);
            ARCameraPoseBridge.SpatialSnapshot Frame(long reference, float cameraX, float anchorX)
            {
                var ground = new ARGroundStateBridge.GroundStateSnapshot(reference, generation, reference,
                    ARGroundLifecycleState.Provisional, ARGroundTrust.Provisional, Environment.TickCount64, "fixture");
                return new(reference, new(generation, 1, new(1502, 0, 1080, 1798)), Environment.TickCount64,
                    true, "NONE", new(true, cameraX, 1.35f, 0, 0, 0, 0, 1, 1), default, new(ground, anchorX, 0, 0));
            }
            var frame = Frame(2, 16, 15);
            var to = referenceMotion with { X = 16, GroundReference = 2 };
            check(RouteAnchorRecoveryPolicy.TryRebaseWindow(original, frame, referenceMotion, to, now.AddSeconds(4),
                out var points, out var rebased), $"{route.Algorithm}: a floor handoff uses a bounded same-world observation");
            ARRouteBridge.Publish(points, route.Algorithm, route.TotalDistanceMeters, RouteVisualKind.RouteWindow,
                original.NavigationState.WindowStartProgressMeters, original.NavigationState.SourceSegmentIndex, rebased);
            check(points.Count == original.Points.Count && points.Select((point, i) =>
                    Math.Abs(point.X + rebased.AnchorX - original.Points[i].X - placement.AnchorX) < 0.001 &&
                    Math.Abs(point.Z + rebased.AnchorZ - original.Points[i].Z - placement.AnchorZ) < 0.001).All(x => x) &&
                rebased.ObservationTime == now && rebased.IsFor(frame) && !original.Placement.IsFor(frame),
                $"{route.Algorithm}: rebasing preserves mapped world coordinates and original GPS age, without reviving the old reference");
            var second = Frame(3, 18, 18);
            check(RouteAnchorRecoveryPolicy.TryRebaseWindow(ARRouteBridge.Current, second, referenceMotion,
                    to with { X = 18, GroundReference = 3 }, now.AddSeconds(6), out _, out _) &&
                !RouteAnchorRecoveryPolicy.TryRebaseWindow(ARRouteBridge.Current, second, referenceMotion, to,
                    now.AddSeconds(9), out _, out _) &&
                !RouteAnchorRecoveryPolicy.TryRebaseWindow(ARRouteBridge.Current, second, referenceMotion,
                    to with { X = 21 }, now.AddSeconds(6), out _, out _),
                $"{route.Algorithm}: repeated handoffs never refresh the existing eight-second or eight-metre budgets");
            check(!RouteAnchorRecoveryPolicy.TryRebaseWindow(original, frame, referenceMotion, to with { TrackingEpoch = 1 },
                    now.AddSeconds(4), out _, out _) &&
                !RouteAnchorRecoveryPolicy.TryRebaseWindow(original, frame, referenceMotion, to with { WorldFrame = 1 },
                    now.AddSeconds(4), out _, out _) &&
                !RouteAnchorRecoveryPolicy.TryRebaseWindow(original, frame, referenceMotion, to with { Session = 99 },
                    now.AddSeconds(4), out _, out _),
                $"{route.Algorithm}: a tracking break, world correction or new session cannot reuse the old window");
            check(!MappedRoutePreviewPolicy.CanPublish(true, false, RouteMatchConfidence.High, 7, C(0,13), C(0), now, now,
                    false, false, true) &&
                !MappedRoutePreviewPolicy.CanPublish(true, true, RouteMatchConfidence.High, 7, C(0,40), C(0), now, now,
                    true, false, true),
                $"{route.Algorithm}: recovery previews still require barrier-free access and a current on-route match");
            var roadEntry = new RoadEntryConfirmationPolicy();
            check(!roadEntry.Observe(true, false, 13, 15, RouteMatchConfidence.High, 7, now, now) &&
                !roadEntry.Observe(true, false, 3, 15, RouteMatchConfidence.High, 7, now.AddSeconds(2), now.AddSeconds(2)) &&
                roadEntry.Observe(true, false, 3, 15, RouteMatchConfidence.High, 7, now.AddSeconds(4), now.AddSeconds(4)),
                $"{route.Algorithm}: a short recovery preview does not bypass two close fixes for the long corridor");
        }
        finally
        {
            ARRouteBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(previousGeneration);
            else ARRenderGenerationBridge.Invalidate();
        }
    }

    private sealed class EmptyRouter(Action onCall) : IRoutingService
    {
        public string AlgorithmName => "MLD";
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
            CancellationToken cancellationToken = default)
        {
            onCall();
            return Task.FromResult<RouteResult?>(null);
        }
    }
}
