using RescuAR.Navigation.Data;
using RescuAR.AR;
using RescuAR.MAUI.Services;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    passed++;
    Console.WriteLine($"PASS: {name}");
}
foreach (bool activityFirst in new[] { true, false })
{
    var intent = new ArCoreRunIntent();
    intent.Request(true);
    intent.SetSurfaceReady(true);
    Check(intent.ShouldRun, "requested camera has both prerequisites");
    intent.SetActivityResumed(false);
    intent.SetActivityResumed(false);
    intent.SetSurfaceReady(false);
    intent.SetSurfaceReady(false);
    Check(intent.Requested && !intent.ShouldRun, "duplicate pauses retain camera intent");
    if (activityFirst) intent.SetActivityResumed(true);
    else intent.SetSurfaceReady(true);
    Check(!intent.ShouldRun, "one returning prerequisite cannot resume alone");
    if (activityFirst) intent.SetSurfaceReady(true);
    else intent.SetActivityResumed(true);
    Check(intent.ShouldRun, "both resume callback orders work");
    intent.SetSurfaceReady(false);
    intent.Request(false);
    intent.SetActivityResumed(true);
    intent.SetSurfaceReady(true);
    Check(!intent.ShouldRun, "late surface cannot undo Camera-tab exit");
    intent.Request(true);
    Check(intent.ShouldRun, "explicit camera re-entry resumes");
    intent.Shutdown();
    intent.SetActivityResumed(true);
    intent.SetSurfaceReady(true);
    intent.Request(true);
    Check(!intent.ShouldRun, "late callbacks cannot revive shutdown");
}
ArHorizontalRoutePoint P(float x, float z, double d) => new(x, z, d);
Check(ARRouteGeometrySanitizer.Prepare(
    new[] { P(0,0,0), P(float.NaN,1,1), P(2,0,2) }, 65).Points.Count == 0,
    "invalid interior vertex cannot create a shortcut");
Check(ARRouteGeometrySanitizer.Prepare(
    new[] { P(0,0,0), P(1,0,2), P(2,0,1) }, 65).Points.Count == 0,
    "non-monotonic geometry rejected");
var corner = ARRouteGeometrySanitizer.Prepare(
    new[] { P(0,0,0), P(8,0,8), P(8,8,16) }, 65);
Check(corner.Points.Any(p => p.X == 8 && p.Z == 0) &&
    corner.FirstPointPreserved && corner.FinalPointPreserved,
    "subdivision preserves mapped corners and endpoints");

GeoCoordinate C(double x, double y = 0) => new(y / 111195.0, x / 111195.0);
RouteResult Route(params GeoCoordinate[] coordinates)
{
    double distance = 0;
    var points = new List<RoutePoint>();
    for (int i = 0; i < coordinates.Length; i++)
    {
        if (i > 0) distance += coordinates[i-1].DistanceTo(coordinates[i]);
        points.Add(new RoutePoint(coordinates[i], distance));
    }
    return new RouteResult(points, distance, "AStar");
}
var nodes = new Dictionary<int, RoadNode>();
var edges = new List<RoadEdge>();
RoadNode Node(int id, double x, double y)
{
    var node = new RoadNode(id, C(x,y));
    nodes.Add(id,node);
    return node;
}
void Join(RoadNode a, RoadNode b)
{
    foreach (var (from,to) in new[] { (a,b), (b,a) })
    {
        var edge = new RoadEdge(edges.Count+1, from, to,
            from.Coordinate.DistanceTo(to.Coordinate), null, null,
            "footway", new Dictionary<string,string>());
        edges.Add(edge);
        from.Edges.Add(edge);
    }
}
Join(Node(1,0,0), Node(2,20,0));
var start = Node(3,0,8);
var middle = Node(4,40,8);
var end = Node(5,100,8);
Join(start,middle);
Join(middle,end);
var route = await new AStarRoutingService(new RoadGraph(nodes,edges))
    .FindRouteAsync(C(0,1), C(100,8));
Check(route is not null && route.Points.Count >= 2 &&
    route.Points[0].Coordinate.DistanceTo(start.Coordinate) < 1,
    "origin snap avoids the nearest disconnected fragment");
Check(await new AStarRoutingService(new RoadGraph(nodes,edges,(_,_) => true))
    .FindRouteAsync(C(0,1),C(100,8)) is null, "road barrier remains enforced");

// An unmarked junction keeps walkable approaches on each side without
// inventing a crossing over the major road.
GeoJsonRoadFeature Way(string highway, GeoCoordinate a, GeoCoordinate b,
    IReadOnlyDictionary<string,string>? tags = null) => new()
{
    Highway = highway, Coordinates = new[] { a, b },
    Tags = tags ?? new Dictionary<string,string>()
};
bool CanReach(RoadGraph graph, GeoCoordinate from, GeoCoordinate to)
{
    RoadNode startNode = graph.Nodes.Values.Single(n => n.Coordinate == from);
    RoadNode endNode = graph.Nodes.Values.Single(n => n.Coordinate == to);
    var seen = new HashSet<int> { startNode.Id };
    var queue = new Queue<RoadNode>();
    queue.Enqueue(startNode);
    while (queue.Count > 0)
    {
        RoadNode node = queue.Dequeue();
        if (node.Id == endNode.Id) return true;
        foreach (RoadEdge edge in node.Edges)
            if (seen.Add(edge.To.Id)) queue.Enqueue(edge.To);
    }
    return false;
}
var junctionWays = new[]
{
    Way("primary", C(0,-20), C(0,20)),
    Way("residential", C(-20,0), C(0,0)),
    Way("residential", C(-10,10), C(0,0)),
    Way("residential", C(0,0), C(20,0))
};
var junctionGraph = new RoadGraphBuilder().Build(junctionWays);
Check(junctionGraph.Edges.Count == 6 &&
    CanReach(junctionGraph,C(-20,0),C(-10,10)) &&
    !CanReach(junctionGraph,C(-20,0),C(20,0)),
    "endpoint junctions retain same-side ways without an unmarked crossing");
GeoJsonPointFeature crossingPoint = new()
{
    Highway = "crossing", Coordinate = C(0,0),
    Tags = new Dictionary<string,string> { ["crossing:markings"] = "zebra" }
};
Check(CanReach(new RoadGraphBuilder().Build(junctionWays,
    new[] { crossingPoint }), C(-20,0), C(20,0)),
    "marked crossing point joins both mapped approaches");
Check(!CanReach(new RoadGraphBuilder().Build(junctionWays,
    new[] { new GeoJsonPointFeature { Highway="crossing", Barrier="gate",
        Coordinate=C(0,0), Tags=crossingPoint.Tags } }), C(-20,0), C(20,0)),
    "barrier point cannot authorize crossing");
Check(new RoadGraphBuilder().Build(new[]
    { junctionWays[0], Way("residential",C(-20,5),C(20,5)) }).Edges.Count == 0,
    "unmarked interior crossing stays blocked");
Check(new RoadGraphBuilder().Build(new[]
    { junctionWays[0], Way("footway",C(0,-10),C(0,10)) }).Edges.Count == 0,
    "overlap with major-road centerline stays blocked");
Check(new RoadGraphBuilder().Build(new[]
    { junctionWays[0], Way("footway",C(-20,5),C(20,5),
        new Dictionary<string,string> { ["footway"]="crossing",
            ["crossing:markings"]="zebra" }) }).Edges.Count == 2,
    "marked crossing way remains routable");
Check(new RoadGraphBuilder().Build(new[]
    { junctionWays[0], Way("footway",C(0,-10),C(0,10),
        new Dictionary<string,string> { ["footway"]="crossing",
            ["crossing:markings"]="zebra" }) }).Edges.Count == 0,
    "crossing tag cannot authorize a road-aligned segment");
var duplicatePrimary = new GeoJsonRoadFeature
    { OsmId="qc-overlap-1", Highway="residential",
      Coordinates=new[] { C(30,0),C(40,0) } };
var duplicateSecondary = new GeoJsonRoadFeature
    { OsmId="qc-overlap-1", Highway="residential",
      Coordinates=new[] { C(40,0),C(30,0) } };
Check(new RoadGraphBuilder().Build(new[]
    { duplicatePrimary, duplicateSecondary }).Edges.Count == 2,
    "overlapping QC geometry does not duplicate a Marikina road segment");
var actualNetwork = await NavigationDataBootstrap.ValidateOnceAsync();
Check(actualNetwork.DirectedEdgeCount > 0 &&
    actualNetwork.AcceptedRoadFeatureCount > 0,
    "available datasets build a usable pedestrian graph");
async Task<int> CountEmbeddedFeatures(string kind)
{
    int total = 0;
    foreach (string resourceName in NavigationDataBootstrap.GetGeoJsonResourceNames(kind))
    {
        using Stream stream = NavigationDataBootstrap.OpenGeoJsonResource(resourceName);
        using var document = await System.Text.Json.JsonDocument.ParseAsync(stream);
        total += document.RootElement.GetProperty("features").GetArrayLength();
    }
    return total;
}
Check(actualNetwork.SourceRoadFeatureCount == await CountEmbeddedFeatures("ROADS") &&
    actualNetwork.PointFeatureCount == await CountEmbeddedFeatures("POINTS"),
    "ordinary build loads every embedded road and point dataset");
var serviceUrls = (string[])typeof(MLDRoutingService)
    .GetField("baseUrls", System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Instance)!
    .GetValue(new MLDRoutingService())!;
Check(serviceUrls.SequenceEqual(new[] { MLDRoutingService.PrimaryBaseUrl }),
    "ordinary MLD uses the primary regional server without stale fallback");
var separateRegions = new RoadGraphBuilder().Build(new[]
{
    Way("residential", C(0), C(20)),
    Way("residential", C(10000), C(10020))
});
Check(CanReach(separateRegions, C(0), C(20)) &&
    CanReach(separateRegions, C(10000), C(10020)) &&
    !CanReach(separateRegions, C(0), C(10000)),
    "separate regional extracts retain local connections without invented links");
var dense = Route(Enumerable.Range(0,220)
    .Select(i => C(i*0.2, i%2 == 0 ? 0 : 0.2)).ToArray());
ARRouteBridge.Clear();
Check(new MLDARIntegrationService(new FixedRoutingService(dense))
    .PublishInitialRoute(dense,0,40,userCoordinate:C(0)),
    "dense route publishes a smaller valid window");
var geometry = ARRouteGeometrySanitizer.Prepare(
    ARRouteBridge.Current.Points, ARRouteRenderer.MaximumRouteSegments+1);
Check(geometry.Points.Count <= ARRouteRenderer.MaximumRouteSegments+1 &&
    geometry.FirstPointPreserved && geometry.FinalPointPreserved,
    "published window fits renderer without shortcuts");

var now = DateTimeOffset.UtcNow;
var arrival = new SafeZoneConfirmationService();
Check(!arrival.Evaluate(C(0),C(1),75,5,100,now).IsConfirmed,
    "arrival needs a second observation");
Check(!arrival.Evaluate(C(0),C(1),75,5,100,now).IsConfirmed,
    "duplicate GPS observation cannot confirm arrival");
var blocked = arrival.Evaluate(C(0),C(1),75,5,100,now.AddSeconds(1),
    majorRoadBarrier:true);
Check(!blocked.IsConfirmed && blocked.ConfirmationCount == 0,
    "major road cancels proximity-only arrival");
arrival.Evaluate(C(0),C(1),75,5,100,now.AddSeconds(2));
Check(arrival.Evaluate(C(0),C(1),75,5,100,now.AddSeconds(3)).IsConfirmed,
    "two unobstructed new observations confirm vicinity");
var sample = default(RouteProgressTracker.RouteProgressUpdate) with
{
    IsAccepted=true, IsOffRoute=true, CrossTrackErrorMeters=35,
    AccuracyMeters=5, MatchConfidence=RouteMatchConfidence.High
};
bool Trigger(OffRouteReroutePolicy policy, DateTimeOffset at)
{
    policy.Evaluate(sample,at);
    policy.Evaluate(sample,at.AddMilliseconds(100));
    return policy.Evaluate(sample,at.AddMilliseconds(200)).ShouldReroute;
}
var reroute = new OffRouteReroutePolicy();
Check(Trigger(reroute,now), "three reliable off-route observations trigger");
reroute.MarkRerouteFailed(now,awaitingConfirmation:true);
Check(!Trigger(reroute,now.AddSeconds(2)) && Trigger(reroute,now.AddSeconds(4)),
    "pending confirmation retries after three seconds");
reroute.MarkRerouteFailed(now);
Check(!Trigger(reroute,now.AddSeconds(5)) && Trigger(reroute,now.AddSeconds(11)),
    "failed route retries after ten seconds");
reroute.MarkRerouteCompleted(now);
Check(!Trigger(reroute,now.AddSeconds(11)) && Trigger(reroute,now.AddSeconds(46)),
    "successful route retains forty-five second cooldown");

var previous = Route(C(0),C(100));
var progress = default(RouteProgressTracker.ProgressSnapshot) with
    { HasRoute=true, RemainingMeters=40 };
var candidateA = Route(C(0),C(0,150),C(100,150),C(100));
var candidateB = Route(C(17),C(17,150),C(100,150),C(100));
var replacement = new RouteReplacementPolicy();
Check(!replacement.Evaluate(previous,progress,candidateA,C(0),C(100),false,now).IsAccepted,
    "large detour awaits another route");
Check(!replacement.Evaluate(previous,progress,candidateB,C(0),C(100),false,
    now.AddSeconds(4)).IsAccepted, "different origin cannot confirm first detour");
replacement.Reset();
replacement.Evaluate(previous,progress,candidateA,C(0),C(100),false,now);
Check(replacement.Evaluate(previous,progress,candidateA,C(0),C(100),false,
    now.AddSeconds(4)).IsAccepted, "consistent second detour can replace route");
Check(BundledEvacuationCenterCatalog.Centers.Count == 9 &&
    BundledEvacuationCenterCatalog.Centers.All(c =>
        BundledEvacuationCenterCatalog.IsUsableCoordinate(c.Coordinate)) &&
    BundledEvacuationCenterCatalog.Centers.Select(c => c.Name).Distinct().Count() == 9,
    "first offline launch has nine distinct usable historical locations");
var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
try
{
    System.Globalization.CultureInfo.CurrentCulture =
        System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
    Check(BundledEvacuationCenterCatalog.TryParseCoordinate("14.650283", "121.094409",
        out var parsed) && Math.Abs(parsed.Latitude - 14.650283) < 0.000001,
        "server coordinates parse consistently on comma-decimal devices");
}
finally { System.Globalization.CultureInfo.CurrentCulture = originalCulture; }
Check(!BundledEvacuationCenterCatalog.TryParseCoordinate("invalid", "121.1", out _) &&
    !BundledEvacuationCenterCatalog.TryParseCoordinate("NaN", "121.1", out _) &&
    !BundledEvacuationCenterCatalog.TryParseCoordinate("91", "121.1", out _) &&
    !BundledEvacuationCenterCatalog.TryParseCoordinate("0", "0", out _),
    "malformed center coordinates cannot manufacture a destination pin");
// Field-test follow-up: planning quality must not bypass precise AR placement.
bool Plan(double? accuracy, double age, bool cached = false) =>
    RouteStartupLocationPolicy.CanPlan(C(0), accuracy, now.AddSeconds(-age), now, cached);
bool Place(double? accuracy, double age) =>
    RouteStartupLocationPolicy.CanPlace(C(0), accuracy, now.AddSeconds(-age), now);
Check(Plan(30, 0) && !Place(30, 0), "30 m fix plans a route but cannot place cyan geometry");
Check(Plan(30, 7, cached: true) && !Plan(30, 7), "recent cache supports planning without being a current fix");
Check(Place(20, 5) && !Place(20.1, 0), "AR placement retains the 20 m limit");
Check(!Plan(10, 9, cached: true) && !Plan(10, 107, cached: true), "stale field-test cache cannot start a route");
Check(Plan(11.5, -1.7) && !Plan(11.5, -2.1), "observed phone clock skew accepted within two seconds");
Check(!Plan(null, 0) && !Plan(double.NaN, 0) && !Plan(-1, 0) && !Plan(100, 0), "unknown and poor planning accuracy rejected");
Check(!RouteStartupLocationPolicy.CanPlan(new GeoCoordinate(91,0),5,now,now,false), "invalid planning coordinate rejected");

var approachPlanner = new AStarRoutingService(new RoadGraph(nodes, edges));
GeoCoordinate? approach = approachPlanner.FindApproachCoordinate(C(0,-30),C(100,8));
Check(approach.HasValue && approach.Value.DistanceTo(C(0,8)) < 1 &&
    await approachPlanner.FindRouteAsync(C(0,-30),C(100,8)) is null,
    "38 m approach selects a connected road without relaxing route snapping");
Check(approachPlanner.FindApproachCoordinate(C(0,-100),C(100,8)) is null,
    "approach arrow cannot exceed 50 m");
Check(new AStarRoutingService(new RoadGraph(nodes,edges,(_,_)=>true))
    .FindApproachCoordinate(C(0,-30),C(100,8)) is null,
    "approach cannot cross a major-road barrier");
Check(approachPlanner.FindApproachCoordinate(C(0,-30),C(1000,8)) is null,
    "approach cannot claim a disconnected facility");

var onlineSpy = new CountingRoutingService(Route(C(0),C(100,8)));
var hybridOnline = new HybridRoutingService(onlineSpy,
    _ => Task.FromResult(new RoadGraph(nodes,edges)), () => true);
Check(await hybridOnline.FindRouteAsync(C(0),C(100,8)) is not null && onlineSpy.Calls == 1,
    "online MLD provider is selected when internet is available");
var offlineSpy = new CountingRoutingService(Route(C(0),C(100,8)));
var hybridOffline = new HybridRoutingService(offlineSpy,
    _ => Task.FromResult(new RoadGraph(nodes,edges)), () => false);
Check(await hybridOffline.FindRouteAsync(C(0,1),C(100,8)) is not null && offlineSpy.Calls == 0,
    "offline route skips the online provider");

bool Angle(GeoCoordinate target, System.Numerics.Quaternion rotation, double expected) =>
    RoadApproachCuePolicy.TryGetAngle(C(0),target,0,rotation,out double angle,out _) &&
    Math.Abs(angle - expected) < 0.01;
Check(Angle(C(10),System.Numerics.Quaternion.Identity,90) &&
    Angle(C(-10),System.Numerics.Quaternion.Identity,-90), "approach arrows use camera-relative left and right");
Check(Angle(C(0,10),System.Numerics.Quaternion.Identity,0) &&
    Angle(C(0,-10),System.Numerics.Quaternion.Identity,180), "approach front and behind are distinct");
Check(Angle(C(-10),System.Numerics.Quaternion.CreateFromAxisAngle(
    System.Numerics.Vector3.UnitY,MathF.PI/2),0), "rotating the phone changes the screen arrow");
Check(!RoadApproachCuePolicy.TryGetAngle(C(0),C(10),0,default,out _,out _) &&
    !RoadApproachCuePolicy.TryGetAngle(C(0),C(51),0,System.Numerics.Quaternion.Identity,out _,out _),
    "invalid pose and distant approach directions are withheld");

var floodService = new RescuAR.App.Services.Flood.FloodDepthVisualizationService();
var simulation = floodService.FromSimulation(0.6);
Check(simulation.HasRenderableHeight && simulation.Mode ==
    RescuAR.App.Services.Flood.FloodDepthVisualizationService.FloodVisualizationMode.Simulation &&
    simulation.PrimaryText.Contains("Simulated") && simulation.ReportedRiverLevelMeters is null,
    "user-selected flood height is explicitly a simulation");
Check(!floodService.FromSimulation(double.NaN).IsAvailable &&
    !floodService.FromSimulation(-0.1).IsAvailable &&
    !floodService.FromSimulation(0).HasRenderableHeight &&
    floodService.FromSimulation(20).LocalDepthMeters == 3,
    "invalid and out-of-range simulated flood heights are bounded");
var river = floodService.FromAdvisory(new RescuAR.App.Models.DisasterAdvisory
    { Category="Flood", WaterLevel=16.5 });
Check(river.IsAvailable && !river.HasRenderableHeight && river.LocalDepthMeters is null,
    "river gauge readings never become local flood geometry");

var projection = new ARCameraPoseBridge.ProjectionSnapshot(true,
    1,0,0,0, 0,1,0,0, 0,0,-1.002002f,-0.2002002f, 0,0,-1,0, 0.1f,100);
bool Project(System.Numerics.Vector3 a, System.Numerics.Vector3 b,
    out System.Numerics.Vector2 sa, out System.Numerics.Vector2 sb) =>
    FloodSimulationProjection.TryProjectSegment(a,b,System.Numerics.Vector3.Zero,
        System.Numerics.Quaternion.Identity,projection,out sa,out sb);
Check(Project(new(-1,0,-2),new(1,0,-2),out var sa,out var sb) &&
    Math.Abs(sa.X-0.25)<0.001 && Math.Abs(sb.X-0.75)<0.001 && Math.Abs(sa.Y-0.5)<0.001,
    "ARCore projection places a metric outline at correct screen coordinates");
Check(!Project(new(-1,0,2),new(1,0,2),out _,out _), "behind-camera flood outlines are clipped");
Check(Project(new(-10,0,-2),new(10,0,-2),out sa,out sb) &&
    Math.Abs(sa.X)<0.001 && Math.Abs(sb.X-1)<0.001,
    "flood outline clips to viewport rather than overflowing");
Check(Project(new(0,0,1),new(0,0,-2),out sa,out sb) && float.IsFinite(sa.X),
    "near-plane crossing has no division by zero or inverted outline");
Check(FloodSimulationProjection.TryProjectSegment(new(4,1,8),new(6,1,8),new(5,1,10),
    System.Numerics.Quaternion.Identity,projection,out sa,out sb) && Math.Abs(sa.X-0.25)<0.001,
    "outline remains correct after translating the camera world origin");


// Batch 13: both algorithms retain a road approach before route publication.
var connectedAccess = approachPlanner.FindRoadApproachTarget(C(0,-30), C(100,8));
Check(connectedAccess is { ConnectsToDestination: true }, "road entry retains verified destination connectivity");
var pendingAccess = approachPlanner.FindRoadApproachTarget(C(0,-30), C(1000,8));
Check(pendingAccess is { ConnectsToDestination: false } &&
    pendingAccess.Value.Coordinate.DistanceTo(C(0,0)) < 1,
    "nearest road access remains available without claiming a connected facility route");
Check(new AStarRoutingService(new RoadGraph(nodes,edges,(_,_)=>true))
    .FindRoadApproachTarget(C(0,-30),C(1000,8)) is null,
    "disconnected road approach still rejects a major-road barrier");
var roadEntryRoute = await new MLDARIntegrationService(approachPlanner)
    .RequestRouteWithRoadApproachAsync(C(0,-30),C(100,8),connectedAccess);
Check(roadEntryRoute is { Points.Count: >= 2 } &&
    roadEntryRoute.Points[0].Coordinate.DistanceTo(C(0,8)) < 1,
    "AStar plans from a mapped road when the actual GPS origin is outside 20 m");
Check(!new MLDARIntegrationService(approachPlanner).PublishInitialRoute(roadEntryRoute,0,
    userCoordinate:C(0,-30)), "planning from the road never snaps the AR camera onto that road");
var entryOnline = new RoadEntryRoutingService(C(0,8), Route(C(0,8),C(100,8)));
Check(await new MLDARIntegrationService(entryOnline).RequestRouteWithRoadApproachAsync(
    C(0,-30),C(100,8),connectedAccess) is not null && entryOnline.Origins.Count == 2 &&
    entryOnline.Origins[0] == C(0,-30) && entryOnline.Origins[1].DistanceTo(C(0,8)) < 1,
    "online provider uses the same road-entry retry as offline AStar");
var entryUnconnected = new RoadEntryRoutingService(C(0,8), Route(C(0,8),C(100,8)));
Check(await new MLDARIntegrationService(entryUnconnected).RequestRouteWithRoadApproachAsync(
    C(0,-30),C(1000,8),pendingAccess) is null && entryUnconnected.Origins.Count == 1,
    "unconnected nearest road does not manufacture a full route");
var directOnline = new CountingRoutingService(Route(C(0),C(100,8)));
Check(await new MLDARIntegrationService(directOnline).RequestRouteWithRoadApproachAsync(
    C(0,-30),C(100,8),connectedAccess) is not null && directOnline.Calls == 1,
    "a valid direct route is not requested again from the road");

Check(RoadApproachCuePolicy.TryGetDirection(C(0),C(10),90,out var forwardDirection,out _) &&
    Math.Abs(forwardDirection.X)<0.001 && Math.Abs(forwardDirection.Y+1)<0.001,
    "floor arrow uses the same geographic-to-AR yaw as the screen cue");
var arrowGeometry = RoadApproachArrowGeometry.Create(new(0,-1));
Check(arrowGeometry.Length == 3 && arrowGeometry.All(part =>
    part.End == new System.Numerics.Vector2(0,-2)) &&
    Math.Abs(arrowGeometry[1].Start.X + arrowGeometry[2].Start.X)<0.001,
    "ground arrow has one shaft and two symmetric wings aimed at the road");
Check(RoadApproachArrowGeometry.Create(new(float.NaN,0)).Length == 0 &&
    RoadApproachArrowGeometry.Create(default).Length == 0,
    "invalid floor arrow directions create no geometry");
Check(RoadApproachArrowGeometry.Create(new(8,0)).All(part => part.End.X == 2 &&
    part.End.Y == 0), "direction magnitude cannot stretch the floor arrow into a connector line");

Check(arrowGeometry.All(part =>
{
    var transform = new Evergine.Framework.Graphics.Transform3D
    {
        LocalRotation = new Evergine.Mathematics.Vector3(0, part.YawRadians, 0)
    };
    var forward = Evergine.Mathematics.Vector3.Transform(
        Evergine.Mathematics.Vector3.UnitZ, transform.LocalOrientation);
    var expected = System.Numerics.Vector2.Normalize(part.End - part.Start);
    return Math.Abs(forward.X - expected.X) < 0.001 &&
        Math.Abs(forward.Z - expected.Y) < 0.001;
}), "Evergine arrow parts point toward their endpoints using radians");

var arrowGeneration = new ARRenderGenerationToken(1,2,3);
var arrowState = new ARRoadApproachBridge.Snapshot(true,new(0,-1),C(0),12,now,
    arrowGeneration,7,ARGroundTrust.Verified,1000);
bool CanRenderArrow(ARRoadApproachBridge.Snapshot state, ARRenderGenerationToken generation,
    long ground = 7, ARGroundTrust trust = ARGroundTrust.Verified, bool tracking = true,
    long at = 1000) => ARRoadApproachBridge.CanRender(state,generation,ground,trust,tracking,now,at);
Check(CanRenderArrow(arrowState,arrowGeneration), "current tracked floor can render the independent approach arrow");
Check(!CanRenderArrow(arrowState,new(2,2,3)) && !CanRenderArrow(arrowState,new(1,3,3)) &&
    !CanRenderArrow(arrowState,new(1,2,4)), "session, graphics and display changes hide the previous floor arrow");
Check(!CanRenderArrow(arrowState,arrowGeneration,ground:8) &&
    !CanRenderArrow(arrowState,arrowGeneration,trust:ARGroundTrust.None) &&
    !CanRenderArrow(arrowState,arrowGeneration,tracking:false),
    "floor replacement and tracking loss hide stale approach geometry");
Check(!CanRenderArrow(arrowState,arrowGeneration,at:3501) &&
    !CanRenderArrow(arrowState with { FixTimestamp=now.AddSeconds(-6) },arrowGeneration) &&
    !CanRenderArrow(arrowState with { Accuracy=30 },arrowGeneration),
    "expired and inaccurate GPS cannot keep a floor arrow visible");
Check(!CanRenderArrow(arrowState with { Active=false },arrowGeneration) &&
    !CanRenderArrow(arrowState with { Direction=new(float.NaN,0) },arrowGeneration),
    "cleared or invalid arrow publications are hidden");
Check(!RescuAR.Diagnostics.DiagnosticPrivacyPolicy.DiagnosticRouteVisibilityOverrideEnabled,
    "road diagnostics do not bypass normal route confidence gates");

// Batch 14: expected directions come from camera quaternions, independently
// of the conversion formulas. These checks catch reflected geographic maps.
foreach (float worldYaw in new[] { 0f, 0.53f, -1.2f, MathF.PI })
{
    var worldRotation = System.Numerics.Quaternion.CreateFromAxisAngle(
        System.Numerics.Vector3.UnitY, worldYaw);
    foreach (var cardinal in new (double Bearing,double E,double N)[] { (0,0,10), (90,10,0),
                                    (180,0,-10), (270,-10,0) })
    {
        var cameraRotation = System.Numerics.Quaternion.CreateFromAxisAngle(
            System.Numerics.Vector3.UnitY, worldYaw - (float)(cardinal.Bearing * Math.PI / 180));
        var cameraForward = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ,cameraRotation);
        double azimuth = Math.Atan2(cameraForward.X,cameraForward.Z) * 180 / Math.PI;
        double yaw = MapToArCoordinates.CalculateYawDegrees(azimuth,cardinal.Bearing);
        Check(Math.Abs(Math.IEEERemainder(yaw - worldYaw * 180 / Math.PI,360)) < 0.001,
            $"camera turns preserve alignment: worldYaw={worldYaw}, bearing={cardinal.Bearing}");
        Check(Math.Abs(Math.IEEERemainder(
            MapToArCoordinates.CalculateArAzimuthDegrees(cardinal.Bearing,yaw)-azimuth,360))<0.001,
            $"direction diagnostic predicts the physical AR azimuth: {worldYaw}/{cardinal.Bearing}");
        var expected = System.Numerics.Vector3.Transform(
            new((float)cardinal.E,0,(float)-cardinal.N),worldRotation);
        var target = C(cardinal.E,cardinal.N);
        var projectedRoute = ArRouteAlignment.Rotate(
            new[] { new LocalRoutePoint(target,cardinal.E,cardinal.N,0) },yaw)[0];
        Check(Math.Abs(projectedRoute.X - expected.X)<0.001 && Math.Abs(projectedRoute.Z - expected.Z)<0.001,
            $"mapped route points follow physical cardinal directions: {worldYaw}/{cardinal.Bearing}");
        var rawRoad = new GeoJsonRoadFeature { Highway="residential", Coordinates=new[] { C(0),target } };
        var projectedRoad = NearbyRoadLineProjector.Project(new[] { rawRoad },C(0),40,yaw)[0].End;
        Check(Math.Abs(projectedRoad.X-expected.X)<0.001 && Math.Abs(projectedRoad.Z-expected.Z)<0.001,
            $"raw GeoJSON roads follow camera coordinates: {worldYaw}/{cardinal.Bearing}");
        Check(RoadApproachCuePolicy.TryGetDirection(C(0),target,yaw,out var direction,out _) &&
            Math.Abs(direction.X - expected.X/10)<0.001 && Math.Abs(direction.Y - expected.Z/10)<0.001,
            $"floor arrow follows the road without reflection: {worldYaw}/{cardinal.Bearing}");
        Check(RoadApproachCuePolicy.TryGetAngle(C(0),target,yaw,cameraRotation,out double angle,out _) &&
            Math.Abs(angle)<0.001,
            $"cardinal target in front of camera produces a forward screen cue: {worldYaw}/{cardinal.Bearing}");
        var offsetRoute = Route(target,C(cardinal.E*2,cardinal.N*2));
        var offsetPublisher = new MLDARIntegrationService(new FixedRoutingService(offsetRoute));
        bool initialOffsetPublished = offsetPublisher.PublishInitialRoute(offsetRoute,yaw,userCoordinate:C(0));
        var initialOffset = ARRouteBridge.Current;
        Check(initialOffsetPublished && initialOffset.Points.Count>=2 &&
            Math.Abs(initialOffset.Points[0].X-expected.X)<0.001 &&
            Math.Abs(initialOffset.Points[0].Z-expected.Z)<0.001,
            $"initial GPS-to-road origin uses the physical frame: {worldYaw}/{cardinal.Bearing}");
        bool progressOffsetPublished = offsetPublisher.PublishProgressWindow(offsetRoute,0,target,yaw,0,0,
            userCoordinate:C(0));
        var progressOffset = ARRouteBridge.Current;
        Check(progressOffsetPublished && progressOffset.Points.Count>=2 &&
            Math.Abs(progressOffset.Points[0].X-expected.X)<0.001 &&
            Math.Abs(progressOffset.Points[0].Z-expected.Z)<0.001,
            $"moving GPS-to-road origin uses the physical frame: {worldYaw}/{cardinal.Bearing}");
        var headingPolicy = new HeadingRevalidationPolicy();
        headingPolicy.Evaluate(yaw,C(0),5,1,cardinal.Bearing,RouteMatchConfidence.High,
            cardinal.Bearing,0,0,now);
        var headingDecision = headingPolicy.Evaluate(yaw,target,5,1,cardinal.Bearing,
            RouteMatchConfidence.High,cardinal.Bearing,expected.X,expected.Z,now.AddSeconds(5));
        Check(headingDecision.Disposition == HeadingRevalidationDisposition.AlignmentStable,
            $"movement revalidation uses the same physical frame: {worldYaw}/{cardinal.Bearing}");
    }
}
Check(MapToArCoordinates.CalculateCameraRelativeAngle(90,180)==90 &&
    MapToArCoordinates.CalculateCameraRelativeAngle(270,180)==-90,
    "east is right and west is left in north-facing mapped-route screen guidance");

Check(ARDepthCompatibilityPolicy.DisableAutomaticDepth("SM-A546E",34),
    "Android 14 field-crash phone uses the plane fallback");
Check(ARDepthCompatibilityPolicy.DisableAutomaticDepth("sm-a546e",36) &&
    ARDepthCompatibilityPolicy.DisableAutomaticDepth("SM-A156E",36),
    "previous Android 16 depth quarantines remain covered");
Check(!ARDepthCompatibilityPolicy.DisableAutomaticDepth("SM-A156E",34) &&
    !ARDepthCompatibilityPolicy.DisableAutomaticDepth("other",34) &&
    !ARDepthCompatibilityPolicy.DisableAutomaticDepth(null,36),
    "native depth quarantine is limited to field-confirmed profiles");

Check(!RouteCorridorPolicy.CanEnterRoadFollowing(true,false,8.2,25,RouteMatchConfidence.Medium,20) &&
    RoadApproachCuePolicy.TryGetAngle(C(0),C(0,8.2),0,System.Numerics.Quaternion.Identity,out _,out _),
    "return arrow remains possible inside GPS tolerance while mapped road entry is unconfirmed");

var entryConfirmation = new RoadEntryConfirmationPolicy();
bool ObserveEntry(double distance,double? accuracy,DateTimeOffset at,DateTimeOffset clock,
    bool offRoute=false,RouteMatchConfidence confidence=RouteMatchConfidence.Medium) =>
    entryConfirmation.Observe(true,offRoute,distance,25,confidence,accuracy,at,clock);
Check(!ObserveEntry(15.5,20,now,now) && entryConfirmation.ConfirmationCount==0,
    "15.5 m field match inside a 25 m corridor retains the approach arrow");
Check(!ObserveEntry(8.2,20,now.AddSeconds(1),now.AddSeconds(1)) &&
    entryConfirmation.ConfirmationCount==0,
    "later 8.2 m corridor match still does not establish road entry");
Check(!ObserveEntry(4,20,now.AddSeconds(2),now.AddSeconds(2)) &&
    entryConfirmation.ConfirmationCount==1,
    "first fresh near-road sample begins confirmation at the existing placement accuracy limit");
Check(!ObserveEntry(4,20,now.AddSeconds(2),now.AddSeconds(3)) &&
    entryConfirmation.ConfirmationCount==1,
    "cached duplicate GPS cannot count as a second road-entry observation");
Check(ObserveEntry(3,12,now.AddSeconds(4),now.AddSeconds(4)),
    "second independent recent near-road sample confirms entry");
entryConfirmation.Reset();
Check(!ObserveEntry(3,21,now,now) && !ObserveEntry(3,null,now.AddSeconds(1),now.AddSeconds(1)) &&
    !ObserveEntry(3,double.NaN,now.AddSeconds(2),now.AddSeconds(2)),
    "poor or missing accuracy cannot confirm road entry");
entryConfirmation.Reset();
Check(!ObserveEntry(3,5,now,now.AddSeconds(6)) &&
    !ObserveEntry(3,5,now.AddSeconds(3),now) && entryConfirmation.ConfirmationCount==0,
    "expired and future fixes cannot establish entry");
entryConfirmation.Reset();
ObserveEntry(3,5,now,now);
Check(!ObserveEntry(3,5,now.AddSeconds(1),now.AddSeconds(1),offRoute:true) &&
    entryConfirmation.ConfirmationCount==0,
    "off-route observation resets road-entry evidence");
ObserveEntry(3,5,now.AddSeconds(2),now.AddSeconds(2));
Check(!ObserveEntry(3,5,now.AddSeconds(12),now.AddSeconds(12)) &&
    entryConfirmation.ConfirmationCount==1,
    "long gaps cannot reuse old entry evidence");
Check(!ObserveEntry(3,5,now.AddSeconds(13),now.AddSeconds(13),confidence:RouteMatchConfidence.Low) &&
    entryConfirmation.ConfirmationCount==0,
    "ambiguous route matches do not establish entry");
Check(connectedAccess is { RoadComponentNodeCount:3, DestinationComponentCount:1 } &&
    pendingAccess is { RoadComponentNodeCount:2, DestinationComponentCount:0 },
    "road approach reports local connectivity without inventing a connection");

Console.WriteLine($"{passed} regression checks passed.");

sealed class FixedRoutingService(RouteResult route) : IRoutingService
{
    public string AlgorithmName => "AStar";
    public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin,
        GeoCoordinate destination, CancellationToken cancellationToken = default) =>
        Task.FromResult<RouteResult?>(route);
}

sealed class CountingRoutingService(RouteResult route) : IRoutingService
{
    public int Calls { get; private set; }
    public string AlgorithmName => "MLD";
    public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult<RouteResult?>(route);
    }
}


sealed class RoadEntryRoutingService(GeoCoordinate road, RouteResult route) : IRoutingService
{
    public List<GeoCoordinate> Origins { get; } = [];
    public string AlgorithmName => "MLD test provider";
    public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Origins.Add(origin);
        return Task.FromResult<RouteResult?>(origin.DistanceTo(road) < 1 ? route : null);
    }
}
