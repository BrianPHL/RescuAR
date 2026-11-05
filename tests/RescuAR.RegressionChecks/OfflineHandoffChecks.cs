using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Routing;
using RescuAR.Navigation.Hazards;

internal static class OfflineHandoffChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(north / 111_195, east / 111_195);
    private static RouteResult Route(string algorithm, double start = 0) => new(
        [new(C(start), 0), new(C(100), C(start).DistanceTo(C(100)))], C(start).DistanceTo(C(100)),
        algorithm, new(algorithm, "fixture", "v1"));

    public static async Task RunAsync(Action<bool, string> check)
    {
        var route = Route("MLD");
        var policy = new OfflineRouteHandoffPolicy();
        var tracker = new RouteProgressTracker();
        tracker.SetRoute(route);
        tracker.Update(C(20), 4);
        var progress = tracker.Current;
        var geometry = route.GeometryFingerprint;
        long bridge = ARRouteBridge.Current.Version;
        check(!policy.Observe(route, 7, true, 1000) && policy.ProviderLabel(route) == "MLD",
            "online MLD keeps its existing controller");
        check(policy.Observe(route, 7, false, 2000) && policy.OfflineSelected &&
            policy.ProviderLabel(route) == "A-STAR · saved MLD path",
            "network loss immediately selects A* while disclosing saved MLD geometry");
        check(route.GeometryFingerprint == geometry && route.Provenance.Provider == "MLD" &&
            tracker.Current == progress && ARRouteBridge.Current.Version == bridge,
            "offline selection leaves geometry provenance, progress and AR publication untouched");
        for (long t = 3000; t < 302_000; t += 1000)
        {
            policy.Observe(route, 7, false, t);
            if (policy.TryBeginRefresh(route, 7, t, true)) throw new InvalidOperationException("Saved route expired early");
        }
        check(!policy.Observe(route, 7, true, 302_000) && policy.OfflineSelected,
            "reconnection and repeated offline polls neither reset route age nor switch trip provider");
        check(!policy.TryBeginRefresh(route, 7, 302_000, false) &&
            policy.TryBeginRefresh(route, 7, 302_000, true),
            "five-minute expiry waits for qualified measured position before background refresh");
        check(!policy.TryBeginRefresh(route, 7, 303_000, true) &&
            policy.TryBeginRefresh(route, 7, 332_000, true),
            "failed or rejected refreshes keep the old route with a thirty-second retry interval");
        var offline = Route("AStar");
        policy.Observe(offline, 7, true, 333_000);
        check(policy.ProviderLabel(offline) == "A-STAR" &&
            !policy.TryBeginRefresh(offline, 7, 900_000, true),
            "accepted local geometry completes saved-route expiry while offline control stays selected");
        policy.Observe(route, 8, true, 900_000);
        check(!policy.OfflineSelected && policy.ProviderLabel(route) == "MLD",
            "a different destination trip cannot inherit the previous offline controller");
        policy.Reset();
        check(!policy.OfflineSelected, "stopping guidance clears offline ownership");

        var nodes = new Dictionary<int, RoadNode>();
        var a = new RoadNode(1, C(0)); var b = new RoadNode(2, C(100));
        nodes.Add(1, a); nodes.Add(2, b);
        var edges = new List<RoadEdge>();
        foreach (var (from, to) in new[] { (a, b), (b, a) })
        {
            var edge = new RoadEdge(edges.Count + 1, from, to, 100, null, null, "footway", new Dictionary<string, string>());
            edges.Add(edge); from.Edges.Add(edge);
        }
        var remote = new ForbiddenRemote();
        var hybrid = new HybridRoutingService(remote, _ => Task.FromResult(new RoadGraph(nodes, edges)), () => true);
        var next = await hybrid.FindReplacementRouteResultAsync(route, C(20), C(100), _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(null),
            forceOffline: true);
        check(next.Route is not null && HybridRoutingService.IsOfflineRoute(next.Route) && remote.Calls == 0,
            "saved MLD route replans with local A* even after the Internet returns");
        var hazard = new RouteHazard("unrelated", C(50, 100), 5, "flood", "high", "fixture", "fixture", DateTimeOffset.UtcNow);
        next = await hybrid.FindReplacementRouteResultAsync(route, C(20), C(100), _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(null),
            [hazard], forceOffline: true);
        check(next.Route is not null && remote.Calls == 0 &&
            RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(next.Route, C(20), [hazard]),
            "offline trip hazard replanning also keeps local provider ownership and safety validation");
        var blocking = new RouteHazard("blocked", C(50), 10, "flood", "high", "fixture", "fixture", DateTimeOffset.UtcNow);
        next = await hybrid.FindReplacementRouteResultAsync(route, C(20), C(100), _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(null),
            [blocking], forceOffline: true);
        check(next.Route is null && remote.Calls == 0, "a blocked offline path cannot be replaced by unsafe geometry or a remote request");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool observedCancellation = false;
        try { await hybrid.FindReplacementRouteResultAsync(route, C(20), C(100), _ => throw new InvalidOperationException(), cancellationToken: cancelled.Token, forceOffline: true); }
        catch (OperationCanceledException) { observedCancellation = true; }
        check(observedCancellation && remote.Calls == 0, "cancelled offline replanning cannot contact a provider");
        CheckRegistrationTransfer(check);
    }

    private static void CheckRegistrationTransfer(Action<bool, string> check)
    {
        var route = Route("MLD"); var next = Route("AStar", 20);
        var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
        var update = tracker.Update(C(30, 3), 5);
        var policy = new RouteDisplayProgressPolicy();
        var now = DateTimeOffset.UtcNow;
        var motion = new RouteBacktrackingPolicy.TrackedMotion(10, 20, 0, 1, 1, 2, 3);
        policy.ObserveGps(route, update, now);
        policy.ObserveRegistration(route, update, now, now, motion);
        check(policy.TryGetRegisteredReference(route, C(50), now, motion, out var before),
            "handoff fixture establishes a pose-paired map-to-AR registration");
        tracker.SetRoute(next);
        var replacement = tracker.Update(C(30, 3), 5);
        foreach (var changed in new[] { motion with { Session = 2 }, motion with { TrackingEpoch = 3 },
            motion with { WorldFrame = 4 }, motion with { YawDegrees = 1 } })
            check(!policy.TryTransferRegistration(route, next, replacement, now, now, changed),
                "replacement cannot inherit registration across changed session, tracking, world or calibration");
        check(!policy.TryTransferRegistration(route, next, replacement, now, now.AddSeconds(4), motion) &&
            !policy.TryTransferRegistration(route, next, replacement with { IsOffRoute = true }, now, now, motion),
            "stale GPS and unqualified route matches cannot carry a registration");
        check(!policy.TryTransferRegistration(route, next, replacement, now.AddSeconds(31), now.AddSeconds(31), motion),
            "an unsupported old transform cannot transfer to replacement geometry");
        check(policy.TryGetRegisteredReference(route, C(50), now, motion, out var unchanged) && unchanged == before,
            "rejected transfers leave the current route's placement intact");
        check(policy.TryTransferRegistration(route, next, replacement, now, now, motion) &&
            policy.TryGetRegisteredReference(next, C(50), now, motion, out var after) && before == after,
            "accepted replacement retains exact world coordinates and original support time instead of reseeding at noisy GPS");
        check(policy.TryGetObservedPosition(next, now, out var display) && Math.Abs(display.ProgressMeters - 10) < .1 &&
            !policy.TryGetRegisteredReference(route, C(50), now, motion, out _),
            "replacement uses its own progress and exclusively owns the transferred transform");
        check(policy.CanRetainRoadEntry(next, replacement, now, motion),
            "confirmed entry can follow a replacement in the same physical road corridor");
        var parallel = new RouteResult([new(C(20, 11), 0), new(C(100, 11), 80)], 80, "AStar");
        tracker.SetRoute(parallel);
        var shifted = tracker.Update(C(30, 11), 5);
        check(policy.TryTransferRegistration(next, parallel, shifted, now, now, motion) &&
            !policy.CanRetainRoadEntry(parallel, shifted, now, motion),
            "a neighboring road inside GPS uncertainty cannot inherit physical road entry more than five metres from the tracked pose");
    }

    private sealed class ForbiddenRemote : IHazardAwareRoutingService
    {
        public int Calls { get; private set; }
        public string AlgorithmName => "MLD";
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Offline trip contacted MLD"); }
        public Task<RouteResult?> FindRouteAvoidingHazardsAsync(GeoCoordinate origin, GeoCoordinate destination,
            IReadOnlyList<RouteHazard> hazards, CancellationToken cancellationToken = default) => FindRouteAsync(origin, destination, cancellationToken);
    }
}
