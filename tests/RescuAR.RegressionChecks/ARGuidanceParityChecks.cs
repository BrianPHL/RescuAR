using System.Globalization;
using System.Net;
using System.Text.Json;
using RescuAR.AR;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

internal static class ARGuidanceParityChecks
{
    private static readonly GeoCoordinate Base = new(14.7, 121.08);
    private static readonly double LongitudeScale = 111_195 * Math.Cos(Base.Latitude * Math.PI / 180);

    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var graph = Graph();
        using var handler = new MldHandler();
        using var client = new HttpClient(handler);
        var mld = new MLDRoutingService(client, "https://routing.unit.test");
        var astar = new AStarRoutingService(graph);
        var onlineRoute = await mld.FindRouteAsync(C(0, 3), C(100, 100));
        var offlineRoute = await astar.FindRouteAsync(C(0, 3), C(100, 100));
        check(onlineRoute is { Points.Count: > 10 } && offlineRoute is { Points.Count: >= 3 } &&
            onlineRoute.Points.Count > offlineRoute.Points.Count,
            "provider parity uses the production MLD HTTP/parser path and real A* with different vertex densities");

        var generation = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        ARRenderGenerationBridge.Activate(new(970, 971, 972));
        try
        {
            foreach (var route in new[] { onlineRoute!, offlineRoute! })
                CheckGuidance(route, now, check);
            await CheckReplanning(graph, onlineRoute!, offlineRoute!, now, check);
        }
        finally
        {
            ARRouteBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(generation);
            else ARRenderGenerationBridge.Invalidate();
        }
    }

    private static void CheckGuidance(RouteResult route, DateTimeOffset now, Action<bool, string> check)
    {
        string provider = route.Algorithm;
        check(Math.Abs(route.Points[^1].DistanceFromStartMeters - route.TotalDistanceMeters) < 0.01 &&
            Math.Abs(route.TotalDistanceMeters - 200) < 0.2,
            $"{provider}: total distance follows mapped geometry despite the MLD fixture's differing server distance");
        var tracker = new RouteProgressTracker();
        tracker.SetRoute(route);
        var gps = tracker.Update(C(37.5, 5.4), 11);
        var fused = tracker.ApplyFusionCorrection(58.8, gps, "GPS_PDR_PARITY_FIXTURE");
        check(RouteDisplayProgressPolicy.TryResolveGps(route, fused, out var display) &&
            Math.Abs(display.ProgressMeters - 37.5) < 0.15 && fused.CommittedProgressMeters == 58.8,
            $"{provider}: physical placement and smoothed distance remain independent");
        var publisher = new MLDARIntegrationService(new UnusedRouter());
        check(publisher.PublishProgressWindow(route, display.ProgressMeters, display.RoadCoordinate,
            0, 0, 0, arWindowMeters: 40, sourceSegmentIndex: display.SegmentIndex,
            userCoordinate: display.AlignmentCoordinate),
            $"{provider}: a good physical GPS match publishes mapped cyan geometry");
        var snapshot = ARRouteBridge.Current;
        check(Math.Abs(snapshot.Points[0].Z - 5.4) < 0.15 &&
            snapshot.Algorithm == provider && snapshot.NavigationState.VisualKind == RouteVisualKind.RouteWindow,
            $"{provider}: cyan geometry preserves the lateral road offset and actual provider identity");

        var physical = new RouteDisplayProgressPolicy();
        physical.ObserveGps(route, fused, now);
        check(physical.TryResolvePdr(route, 60.8, now.AddSeconds(2), out var step) &&
            Math.Abs(step.ProgressMeters - 39.5) < 0.15 &&
            !physical.TryResolvePdr(route, 60.8, now.AddSeconds(9), out _) &&
            !physical.TryResolvePdr(route, 67, now.AddSeconds(2), out _),
            $"{provider}: step-based guidance shares the eight-second/eight-metre GPS limits");
        check(!physical.TryResolvePdr(new RouteResult(route.Points, route.TotalDistanceMeters, provider),
            60.8, now.AddSeconds(2), out _),
            $"{provider}: a replacement route cannot inherit an older route's display anchor");

        tracker.SetRoute(route);
        var previewGps = tracker.Update(C(10, 8.2), 12);
        bool Preview(bool access = true, double age = 0) =>
            RouteDisplayProgressPolicy.TryResolveGps(route, previewGps, out var preview) &&
            MappedRoutePreviewPolicy.CanPublish(previewGps.IsAccepted, previewGps.IsOffRoute,
                previewGps.MatchConfidence, previewGps.AccuracyMeters, previewGps.GpsCoordinate,
                preview.RoadCoordinate, now, now.AddSeconds(age), access);
        check(Preview() && !Preview(access: false) && !Preview(age: 6),
            $"{provider}: mapped preview remains eligible only with fresh GPS and verified road access");
        var entry = new RoadEntryConfirmationPolicy();
        var entryGps = tracker.Update(C(20, 3), 7);
        bool Enter(int second) => entry.Observe(entryGps.IsAccepted, entryGps.IsOffRoute,
            entryGps.CrossTrackErrorMeters, entryGps.CorridorRadiusMeters, entryGps.MatchConfidence,
            entryGps.AccuracyMeters, now.AddSeconds(second), now.AddSeconds(second));
        check(!Enter(0) && !Enter(0) && Enter(2),
            $"{provider}: physical road entry requires independent GPS fixes and ignores duplicate timestamps");
        entry.Reset();
        check(!Enter(3), $"{provider}: new road-entry confirmation does not reuse the previous trip's count");

        tracker.SetRoute(route);
        var reverse = new RouteBacktrackingPolicy();
        RouteBacktrackingPolicy.BacktrackingDecision Reverse(int seconds, double east) =>
            reverse.Evaluate(route, tracker.Update(C(east, 3), 5, 270, 1),
                now.AddSeconds(seconds), now.AddSeconds(seconds));
        Reverse(0, 80);
        check(!Reverse(4, 75).ShouldReplan && Reverse(8, 64).ConfirmationCount == 1 &&
            Reverse(10, 60).ShouldReplan && !Reverse(12, 56).ShouldReplan,
            $"{provider}: sustained reverse walking requests one current-position replan with cooldown");

        tracker.SetRoute(route);
        tracker.Update(C(60), 5);
        var offRoad = tracker.Update(C(60, -45), 5);
        var deviation = new OffRouteReroutePolicy();
        check(!deviation.Evaluate(offRoad, now).ShouldReroute &&
            deviation.Evaluate(offRoad, now).ConfirmationCount == 1 &&
            !deviation.Evaluate(offRoad, now.AddSeconds(2)).ShouldReroute &&
            deviation.Evaluate(offRoad, now.AddSeconds(4)).ShouldReroute,
            $"{provider}: off-route confirmation uses reliable distinct fixes instead of one jump");
        deviation.MarkRerouteCompleted(now.AddSeconds(4));
        check(!deviation.Evaluate(offRoad, now.AddSeconds(6)).ShouldReroute &&
            !deviation.Evaluate(offRoad, now.AddSeconds(8), destinationProximityUncertain: true).IsCandidate,
            $"{provider}: reroute cooldown and destination proximity prevent request loops");

        var beforeBend = tracker.Update(C(80, 3), 7);
        var afterBend = tracker.ApplyFusionCorrection(130, beforeBend, "GPS_PDR_PARITY_CORNER");
        check(RouteDisplayProgressPolicy.TryResolveGps(route, afterBend, out var corner) &&
            publisher.PublishProgressWindow(route, corner.ProgressMeters, corner.RoadCoordinate,
                0, 0, 0, arWindowMeters: 40, sourceSegmentIndex: corner.SegmentIndex,
                userCoordinate: corner.AlignmentCoordinate) &&
            ARRouteBridge.Current.Points.Any(p => Math.Abs(p.X - 20) < 0.2 && Math.Abs(p.Z - 3) < 0.2) &&
            ARRouteBridge.Current.Points.Any(p => Math.Abs(p.X - 20) < 0.2 && p.Z < -10),
            $"{provider}: the physical window preserves the mapped corner before smoothed progress reaches it");

        var arrival = new SafeZoneConfirmationService();
        var first = arrival.Evaluate(C(100, 90), C(100, 100), 30, 5, 10, now,
            evaluatedAt: now, destinationVersion: 1);
        var arrived = arrival.Evaluate(C(100, 95), C(100, 100), 30, 5, 5, now.AddSeconds(4),
            evaluatedAt: now.AddSeconds(4), destinationVersion: 1);
        check(first.IsCandidate && !first.IsConfirmed && arrived.IsConfirmed,
            $"{provider}: near-destination guidance and automatic arrival share the same evidence policy");
        check(!arrival.Evaluate(C(100, 95), C(100, 100), 30, 5, 5, now.AddSeconds(5),
            evaluatedAt: now.AddSeconds(5), majorRoadBarrier: true, destinationVersion: 2).IsConfirmed,
            $"{provider}: a new trip across a major-road barrier cannot inherit arrival");

        CheckVisibility(provider, now, check);
        ARRouteBridge.Clear();
    }

    private static void CheckVisibility(string provider, DateTimeOffset now, Action<bool, string> check)
    {
        var live = new ARCameraSpatialController.SpatialContinuitySnapshot(
            ARCameraSpatialController.SpatialContinuityState.Live, 100, 100, "unit fixture");
        ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot Visibility(bool fresh = true,
            bool identity = false, bool heading = true, bool rerouting = false,
            ARCameraSpatialController.SpatialContinuitySnapshot? spatial = null) =>
            ARGuidanceConfidencePolicy.Evaluate(true, true, RouteVisualKind.RouteWindow, true,
                fresh, GpsPdrFusionPolicy.GpsConfidence.High, RouteMatchConfidence.High,
                identity, heading, spatial ?? live, rerouting, false);
        check(Visibility().AllowsRouteGeometry && !Visibility(fresh: false).AllowsRouteGeometry &&
            !Visibility(identity: true).AllowsRouteGeometry && !Visibility(heading: false).AllowsRouteGeometry &&
            !Visibility(rerouting: true).AllowsRouteGeometry,
            $"{provider}: fresh GPS, route identity, heading, and rerouting remain independent visibility gates");
        var shortLoss = new ARCameraSpatialController.SpatialContinuitySnapshot(
            ARCameraSpatialController.SpatialContinuityState.ShortHold, 100, 100, "unit hold");
        var longLoss = new ARCameraSpatialController.SpatialContinuitySnapshot(
            ARCameraSpatialController.SpatialContinuityState.LongLoss, 100, 100, "unit loss");
        check(Visibility(spatial: shortLoss).AllowsRouteGeometry &&
            !Visibility(spatial: longLoss).AllowsRouteGeometry && Visibility().AllowsRouteGeometry,
            $"{provider}: brief trusted tracking hold, long-loss hiding, and recovery use the same visibility policy");

        var hold = new VerifiedRouteWindowHoldPolicy();
        hold.ObserveVerified(10, 20, C(40), now);
        bool Hold(long route = 10, long ground = 20, double second = 2,
            bool identity = false, double accuracy = 22, double east = 41) =>
            hold.CanHold(route, ground, C(east), accuracy, now.AddSeconds(second), identity);
        check(Hold() && !Hold(identity: true),
            $"{provider}: a bounded GPS wobble can retain the verified path but cannot override suspended route identity");
        check(!Hold(route: 11) && !Hold(ground: 21) && !Hold(second: 7) && !Hold(second: -1),
            $"{provider}: a changed route, changed anchor, expired hold, or reversed clock rejects old geometry");
        check(!Hold(accuracy: 26) && !Hold(accuracy: double.NaN) && !Hold(east: 60),
            $"{provider}: GPS hold preserves accuracy and physical-displacement limits");
        hold.Reset();
        check(!Hold(), $"{provider}: restarting guidance clears the verified-window hold");
    }

    private static async Task CheckReplanning(RoadGraph graph, RouteResult mldRoute,
        RouteResult astarRoute, DateTimeOffset now, Action<bool, string> check)
    {
        using var handler = new MldHandler();
        using var client = new HttpClient(handler);
        var mld = new MLDRoutingService(client, "https://routing.unit.test");
        bool internet = true;
        int graphLoads = 0;
        var hybrid = new HybridRoutingService(mld, _ =>
        {
            graphLoads++;
            return Task.FromResult(graph);
        }, () => internet);
        Task<AStarRoutingService.RoadApproachTarget?> NoAccess(CancellationToken _) =>
            Task.FromResult<AStarRoutingService.RoadApproachTarget?>(null);
        check(new MLDARIntegrationService(new UnusedRouter()).PublishInitialRoute(
            astarRoute, 0, arWindowMeters: 40, userCoordinate: C(0)) && ARRouteBridge.Current.IsAvailable,
            "replacement tests begin with an actual published mapped route to protect");
        var snapshot = ARRouteBridge.Current;
        foreach (string trigger in new[] { "deviation", "reverse walking" })
        {
            var replacement = await hybrid.FindReplacementRouteAsync(astarRoute, C(40, 3), C(100, 100), NoAccess);
            check(replacement is not null && HybridRoutingService.IsOfflineRoute(replacement) &&
                handler.Origins.Count == 0 && replacement.Points[0].Coordinate.DistanceTo(C(40)) < 0.2,
                $"A* {trigger}: restored Internet cannot switch providers and the new route starts at current GPS projection");
        }
        check(graphLoads == 1 && ARRouteBridge.Current.Version == snapshot.Version,
            "replacement calculations reuse the offline graph and cannot mutate published AR geometry");
        var online = await hybrid.FindReplacementRouteAsync(mldRoute, C(45, 3), C(100, 100), NoAccess);
        check(online is not null && !HybridRoutingService.IsOfflineRoute(online) &&
            handler.Origins[^1].DistanceTo(C(45, 3)) < 0.2 && online.Points[0].Coordinate.DistanceTo(C(45)) < 0.2,
            "MLD replanning uses the same current-position origin with its production routing adapter");
        internet = false;
        int onlineCalls = handler.Origins.Count;
        var failover = await hybrid.FindReplacementRouteAsync(mldRoute, C(50, 3), C(100, 100), NoAccess);
        check(failover is not null && HybridRoutingService.IsOfflineRoute(failover) &&
            handler.Origins.Count == onlineCalls,
            "an actual MLD deviation without Internet uses offline A* without an online request");
        internet = true;
        check(await hybrid.FindReplacementRouteAsync(failover!, C(55, 3), C(100, 100), NoAccess) is { } retained &&
            HybridRoutingService.IsOfflineRoute(retained) && handler.Origins.Count == onlineCalls,
            "the offline replacement remains the active provider after Internet returns");

        int accessLookups = 0;
        var access = new AStarRoutingService(graph).FindRoadApproachTarget(C(30, -35), C(100, 100));
        foreach (var previous in new[] { astarRoute, mldRoute })
        {
            handler.RejectDistantOrigins = true;
            var approached = await hybrid.FindReplacementRouteAsync(previous, C(30, -35), C(100, 100), _ =>
            {
                accessLookups++;
                return Task.FromResult(access);
            });
            check(approached is not null && approached.Points[0].Coordinate.DistanceTo(C(30)) < 0.2 &&
                HybridRoutingService.IsOfflineRoute(approached) == HybridRoutingService.IsOfflineRoute(previous),
                $"{previous.Algorithm}: a bounded road-access retry uses the same trip provider");
        }
        check(accessLookups == 2, "both providers defer road-access lookup until the direct request cannot route");

        handler.RejectDistantOrigins = false;
        handler.Failure = new HttpRequestException("unit transport failure");
        check(await hybrid.FindReplacementRouteAsync(mldRoute, C(40, 3), C(100, 100), NoAccess) is { } recovered &&
            HybridRoutingService.IsOfflineRoute(recovered),
            "MLD transport failure retains independently validated offline fallback");
        handler.Failure = null;
        handler.NoRoute = true;
        check(await hybrid.FindReplacementRouteAsync(mldRoute, C(40, 3), C(100, 100), NoAccess) is null &&
            ARRouteBridge.Current.Version == snapshot.Version,
            "authoritative MLD NoRoute cannot silently become A* or erase active AR geometry");
        handler.NoRoute = false;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        int callsBeforeCancellation = handler.Origins.Count;
        bool cancelledBeforeWork = false;
        try { await hybrid.FindReplacementRouteAsync(astarRoute, C(40), C(100, 100), NoAccess,
            cancellationToken: cancelled.Token); }
        catch (OperationCanceledException) { cancelledBeforeWork = true; }
        check(cancelledBeforeWork && handler.Origins.Count == callsBeforeCancellation &&
            ARRouteBridge.Current.Version == snapshot.Version,
            "cancelled trip replacement performs no provider work or AR publication");

        handler.HazardDetour = true;
        RouteHazard[] hazards = [new("fixture", C(70), 10, "Flood", "High", "unit hazard", "unit", now)];
        foreach (var previous in new[] { astarRoute, mldRoute })
        {
            var safe = await hybrid.FindReplacementRouteAsync(previous, C(40), C(100, 100), NoAccess, hazards);
            check(safe is not null && RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(safe, C(40), hazards),
                $"{previous.Algorithm}: the shared hazard planner verifies a safe replacement independently of trip provider");
        }
        var unsafeOnline = new UnsafeHazardRouter(mldRoute);
        var unsafeHybrid = new HybridRoutingService(unsafeOnline, _ => Task.FromResult(graph), () => true);
        check(await unsafeHybrid.FindReplacementRouteAsync(astarRoute, C(40), C(100, 100), NoAccess, hazards) is null,
            "an unsafe online hazard result and blocked offline graph cannot produce an unverified replacement");

        var replacementPolicy = new RouteReplacementPolicy();
        var tracker = new RouteProgressTracker();
        tracker.SetRoute(astarRoute);
        tracker.Update(C(40), 5);
        var invalid = new RouteResult([new(C(40), 0), new(C(100, 100), -1)], 100, "MLD");
        check(!replacementPolicy.Evaluate(astarRoute, tracker.Current, invalid, C(40), C(100, 100),
            false, now).IsAccepted && ARRouteBridge.Current.Version == snapshot.Version,
            "invalid replacement geometry cannot replace active guidance or bypass validation");
    }

    private static GeoCoordinate C(double east, double north = 0) =>
        new(Base.Latitude + north / 111_195, Base.Longitude + east / LongitudeScale);

    private static (double East, double North) Offset(GeoCoordinate c) =>
        ((c.Longitude - Base.Longitude) * LongitudeScale, (c.Latitude - Base.Latitude) * 111_195);

    private static RoadGraph Graph()
    {
        var nodes = new Dictionary<int, RoadNode>
        {
            [1] = new(1, C(0)), [2] = new(2, C(100)), [3] = new(3, C(100, 100))
        };
        var edges = new List<RoadEdge>();
        foreach (var (from, to) in new[] { (1, 2), (2, 1), (2, 3), (3, 2) })
        {
            var a = nodes[from]; var b = nodes[to];
            var edge = new RoadEdge(edges.Count + 1, a, b, a.Coordinate.DistanceTo(b.Coordinate),
                null, null, "footway", new Dictionary<string, string>());
            edges.Add(edge);
            a.Edges.Add(edge);
        }
        return new(nodes, edges);
    }

    // Representative OSRM protocol data with dense vertices and duplicate
    // points. Every request stays inside this in-memory HTTP handler.
    private sealed class MldHandler : HttpMessageHandler
    {
        public List<GeoCoordinate> Origins { get; } = [];
        public bool RejectDistantOrigins { get; set; }
        public bool NoRoute { get; set; }
        public bool HazardDetour { get; set; }
        public Exception? Failure { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string first = request.RequestUri!.AbsolutePath.Split('/')[^1].Split(';')[0];
            var pair = first.Split(',');
            var origin = new GeoCoordinate(double.Parse(pair[1], CultureInfo.InvariantCulture),
                double.Parse(pair[0], CultureInfo.InvariantCulture));
            Origins.Add(origin);
            if (Failure is not null) return Task.FromException<HttpResponseMessage>(Failure);
            var offset = Offset(origin);
            var points = new List<GeoCoordinate> { C(Math.Clamp(offset.East, 0, 100)) };
            if (HazardDetour)
                points.AddRange([C(offset.East, 40), C(100, 40), C(100, 100)]);
            else
            {
                for (int x = (int)Math.Floor(offset.East / 5) * 5 + 5; x < 100; x += 5)
                    points.Add(C(x));
                points.Add(C(100)); points.Add(C(100));
                for (int y = 5; y <= 100; y += 5) points.Add(C(100, y));
            }
            string body = NoRoute || RejectDistantOrigins && Math.Abs(offset.North) > 20
                ? "{\"code\":\"NoRoute\",\"routes\":[]}"
                : JsonSerializer.Serialize(new { code = "Ok", routes = new[] { new {
                    distance = 999, geometry = new { coordinates = points.Select(p => new[] { p.Longitude, p.Latitude }) }
                } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class UnusedRouter : IRoutingService
    {
        public string AlgorithmName => "no requests during guidance";
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected route request");
    }

    private sealed class UnsafeHazardRouter(RouteResult unsafeRoute) : IHazardAwareRoutingService
    {
        public string AlgorithmName => "unsafe unit provider";
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
            CancellationToken cancellationToken = default) => Task.FromResult<RouteResult?>(unsafeRoute);
        public Task<RouteResult?> FindRouteAvoidingHazardsAsync(GeoCoordinate origin, GeoCoordinate destination,
            IReadOnlyList<RouteHazard> hazards, CancellationToken cancellationToken = default) =>
            Task.FromResult<RouteResult?>(unsafeRoute);
    }
}
