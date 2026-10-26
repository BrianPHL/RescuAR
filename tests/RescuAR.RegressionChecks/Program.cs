using RescuAR.Navigation.Data;
using RescuAR.AR;
using RescuAR.MAUI.Services;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;
using RescuAR.Navigation.Hazards;
using System.Text.Json;
using RescuAR.MAUI.Platforms.Android.Services;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    passed++;
    Console.WriteLine($"PASS: {name}");
}
if (args.Contains("--multi-device-recovery"))
{
    ARMultiDeviceRecoveryChecks.Run(Check);
    Console.WriteLine($"{passed} multi-device recovery regression checks passed.");
    return;
}
if (args.Contains("--navigation-pipeline"))
{
    NavigationPipelineChecks.Run(Check);
    Console.WriteLine($"{passed} navigation pipeline regression checks passed.");
    return;
}
if (args.Contains("--mld-guidance"))
{
    await MldGuidanceOwnershipChecks.RunAsync(Check);
    Console.WriteLine($"{passed} MLD guidance ownership regression checks passed.");
    return;
}
if (args.Contains("--road-surface"))
{
    VisualRoadAlignmentChecks.Run(Check);
    Console.WriteLine($"{passed} visual road alignment regression checks passed.");
    return;
}
if (args.Contains("--startup-scene"))
{
    StartupSceneChecks.Run(Check);
    Console.WriteLine($"{passed} startup-scene regression checks passed.");
    return;
}
if (args.Contains("--ground-flood"))
{
    ARGroundFloodChecks.Run(Check);
    Console.WriteLine($"{passed} ground/flood regression checks passed.");
    return;
}
if (args.Contains("--field-validation") || args.Contains("--ar-guidance") || args.Contains("--placement-hazards") || args.Contains("--guidance-recovery") || args.Contains("--alignment-recovery"))
{
    if (args.Contains("--alignment-recovery") || args.Contains("--ar-guidance"))
    {
        await MldGuidanceOwnershipChecks.RunAsync(Check);
        VisualRoadAlignmentChecks.Run(Check);
        ARRegistrationChecks.Run(Check);
        ARMultiDeviceRecoveryChecks.Run(Check);
        NavigationStabilityChecks.Run(Check);
        NavigationPipelineChecks.Run(Check);
        await ARHeadingRecoveryChecks.RunAsync(Check);
    }
    await ARRecoveryContinuityChecks.RunAsync(Check);
    ARPlacementHazardChecks.Run(Check);
    ARGroundFloodChecks.Run(Check);
    ARFieldValidationChecks.Run(Check);
    RouteBacktrackingChecks.Run(Check);
    RouteDisplayProgressChecks.Run(Check);
    StartupSceneChecks.Run(Check);
    if (args.Contains("--ar-guidance") || args.Contains("--placement-hazards") || args.Contains("--guidance-recovery") || args.Contains("--alignment-recovery"))
    {
        await ARGuidanceParityChecks.RunAsync(Check);
        if (args.Contains("--ar-guidance") || args.Contains("--alignment-recovery")) await ARSystemAuditChecks.RunAsync(Check);
    }
    Console.WriteLine($"{passed} {(args.Contains("--alignment-recovery") ? "alignment-recovery" : args.Contains("--ar-guidance") ? "AR guidance" : args.Contains("--guidance-recovery") ? "guidance-recovery" : args.Contains("--placement-hazards") ? "placement/hazard" : "field-validation")} regression checks passed.");
    return;
}
ARRegistrationChecks.Run(Check);
await MldGuidanceOwnershipChecks.RunAsync(Check);
VisualRoadAlignmentChecks.Run(Check);
ARMultiDeviceRecoveryChecks.Run(Check);
NavigationStabilityChecks.Run(Check);
await ARHeadingRecoveryChecks.RunAsync(Check);
await ARRecoveryContinuityChecks.RunAsync(Check);
ARPlacementHazardChecks.Run(Check);
ARGroundFloodChecks.Run(Check);
ARFieldValidationChecks.Run(Check);
RouteBacktrackingChecks.Run(Check);
RouteDisplayProgressChecks.Run(Check);
StartupSceneChecks.Run(Check);
await ARGuidanceParityChecks.RunAsync(Check);
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

// Mapped shared junctions remain usable without zebra markings. Geometric
// intersections alone never manufacture a source vertex.
GeoJsonRoadFeature Way(string highway, GeoCoordinate a, GeoCoordinate b,
    IReadOnlyDictionary<string,string>? tags = null) => new()
{
    Highway = highway, Coordinates = new[] { a, b },
    Tags = tags ?? new Dictionary<string,string>()
};
var privateAccessTags = new Dictionary<string,string> { ["access"] = "private" };
var pedestrianFilter = new PedestrianRoadFilter();
Check(pedestrianFilter.IsWalkable(Way("residential", C(0), C(20), privateAccessTags)),
    "evacuation routing accepts private residential roads");
Check(pedestrianFilter.IsWalkable(Way("service", C(0), C(20),
    new Dictionary<string,string> { ["access"] = "PRIVATE" })),
    "private access matching remains case insensitive");
foreach (var (key, value) in new[] { ("foot", "no"), ("foot", "private"), ("access", "no") })
{
    var deniedTags = new Dictionary<string,string>(privateAccessTags) { [key] = value };
    Check(!pedestrianFilter.IsWalkable(Way("residential", C(0), C(20), deniedTags)),
        $"private ownership does not override {key}={value}");
}
Check(pedestrianFilter.IsWalkable(Way("primary", C(0), C(20), privateAccessTags)),
    "mapped primary roads accept evacuation walking and private access");
var privateExitGraph = new RoadGraphBuilder().Build(new[]
{
    Way("residential", C(0), C(40), privateAccessTags),
    Way("residential", C(40), C(80))
});
Check(await new AStarRoutingService(privateExitGraph).FindRouteAsync(C(0), C(80)) is not null,
    "offline routing can exit a private road onto a connected public road");
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
Check(junctionGraph.Edges.Count == 8 &&
    CanReach(junctionGraph,C(-20,0),C(-10,10)) &&
    CanReach(junctionGraph,C(-20,0),C(20,0)),
    "mapped shared junctions do not require crossing markings");
GeoJsonPointFeature crossingPoint = new()
{
    Highway = "crossing", Coordinate = C(0,0),
    Tags = new Dictionary<string,string> { ["crossing:markings"] = "zebra" }
};
Check(CanReach(new RoadGraphBuilder().Build(junctionWays,
    new[] { crossingPoint }), C(-20,0), C(20,0)),
    "marked crossing point joins both mapped approaches");
Check(CanReach(new RoadGraphBuilder().Build(junctionWays,
    new[] { new GeoJsonPointFeature { Highway="crossing", Barrier="gate",
        Coordinate=C(0,0), Tags=crossingPoint.Tags } }), C(-20,0), C(20,0)),
    "an unlocked pedestrian gate does not discard a mapped junction");
var privateCrossingTags = new Dictionary<string,string>(crossingPoint.Tags)
    { ["access"] = "private" };
Check(CanReach(new RoadGraphBuilder().Build(junctionWays,
    new[] { new GeoJsonPointFeature { Highway="crossing", Coordinate=C(0),
        Tags=privateCrossingTags } }), C(-20), C(20)),
    "private access does not discard an otherwise marked crossing point");
Check(!CanReach(new RoadGraphBuilder().Build(junctionWays,
    new[] { new GeoJsonPointFeature { Highway="crossing", Coordinate=C(0),
        Tags=new Dictionary<string,string>(privateCrossingTags) { ["foot"]="no" } } }),
    C(-20), C(20)), "foot=no crossing point stays blocked despite private access");
Check(!pedestrianFilter.IsWalkable(Way("footway", C(-20), C(20),
    new Dictionary<string,string> { ["footway"]="crossing", ["foot"]="no" })),
    "a mapped crossing does not override an explicit pedestrian prohibition");
Check(new RoadGraphBuilder().Build(new[]
    { junctionWays[0], Way("residential",C(-20,5),C(20,5)) }).Edges.Count == 4,
    "geometric intersection retains both roads without inventing a junction");
Check(new RoadGraphBuilder().Build(new[]
    { junctionWays[0], Way("footway",C(0,-10),C(0,10)) }).Edges.Count == 4,
    "a mapped footway is not deleted merely for overlapping a mapped street");
Check(new RoadGraphBuilder().Build(new[]
    { junctionWays[0], Way("footway",C(-20,5),C(20,5),
        new Dictionary<string,string> { ["footway"]="crossing",
            ["crossing:markings"]="zebra" }) }).Edges.Count == 4,
    "marked crossing way remains routable");
Check(new RoadGraphBuilder().Build(new[]
    { junctionWays[0], Way("footway",C(0,-10),C(0,10),
        new Dictionary<string,string> { ["footway"]="crossing",
            ["crossing:markings"]="zebra" }) }).Edges.Count == 4,
    "shared policy preserves mapped crossing geometry");
var duplicatePrimary = new GeoJsonRoadFeature
    { OsmId="qc-overlap-1", Highway="residential",
      Coordinates=new[] { C(30,0),C(40,0) } };
var duplicateSecondary = new GeoJsonRoadFeature
    { OsmId="qc-overlap-1", Highway="residential",
      Coordinates=new[] { C(40,0),C(30,0) } };
Check(new RoadGraphBuilder().Build(new[]
    { duplicatePrimary, duplicateSecondary }).Edges.Count == 2,
    "overlapping QC geometry does not duplicate a Marikina road segment");
using (var cases = JsonDocument.Parse(File.ReadAllText(
    Path.Combine(AppContext.BaseDirectory, "evacuation-policy-cases.json"))))
{
    foreach (var test in cases.RootElement.EnumerateArray())
    {
        string name = test.GetProperty("name").GetString()!;
        var tags = test.GetProperty("tags").EnumerateObject().ToDictionary(
            item => item.Name, item => item.Value.GetString()!, StringComparer.OrdinalIgnoreCase);
        if (test.TryGetProperty("node", out var nodeCase) && nodeCase.GetBoolean())
        {
            Check(EvacuationRoutingPolicy.IsBlockedPoint(tags.GetValueOrDefault("barrier"), tags) ==
                test.GetProperty("blocked").GetBoolean(), "shared policy node: " + name);
            continue;
        }
        var way = Way(tags.GetValueOrDefault("highway")!, C(0), C(100), tags);
        Check(EvacuationRoutingPolicy.IsDirectionAllowed(way, true) == test.GetProperty("forward").GetBoolean() &&
            EvacuationRoutingPolicy.IsDirectionAllowed(way, false) == test.GetProperty("backward").GetBoolean(),
            "shared policy directions: " + name);
        if (test.TryGetProperty("factor", out var factor))
            Check(Math.Abs(EvacuationRoutingPolicy.CostFactor(way.Highway ?? "", tags) - factor.GetDouble()) < 1e-9,
                "shared policy cost: " + name);
    }
}
var reverseGraph = new RoadGraphBuilder().Build(new[] { Way("footway", C(0), C(200),
    new Dictionary<string,string> { ["oneway:foot"]="-1" }) });
var reverseService = new AStarRoutingService(reverseGraph);
var reverseRoute = await reverseService.FindRouteAsync(C(100), C(0));
Check(reverseRoute is not null && Math.Abs(reverseRoute.TotalDistanceMeters - 100) < 1,
    "reverse-only partial edge seeds only its reachable endpoint");
Check(await reverseService.FindRouteAsync(C(100), C(200)) is null,
    "partial edge cannot bypass pedestrian one-way restrictions");
var sparseService = new AStarRoutingService(new RoadGraphBuilder().Build(new[] {
    Way("residential", C(0), C(400)) }));
var midDestination = await sparseService.FindRouteAsync(C(0), C(200));
Check(midDestination is not null && midDestination.Points[^1].Coordinate.DistanceTo(C(200)) < 0.1 &&
    Math.Abs(midDestination.TotalDistanceMeters - 200) < 1,
    "AStar reaches a destination midway along a sparse segment without requiring a nearby vertex");
var sameSegment = await sparseService.FindRouteAsync(C(100), C(200));
Check(sameSegment is not null && Math.Abs(sameSegment.TotalDistanceMeters - 100) < 1,
    "two partial endpoints on the same segment do not detour through its vertices");
Check(await sparseService.FindRouteAsync(C(0),C(200,80)) is null &&
    sparseService.FindRoadApproachTarget(C(0),C(200,80)) is { ConnectsToDestination: false, DestinationComponentCount: 0 },
    "a custom pin without mapped access inside fifty metres cannot turn a nearest-road arrow into an evacuation route");
var deadEndService = new AStarRoutingService(new RoadGraphBuilder().Build(new[] {
    Way("residential", C(0), C(50), new Dictionary<string,string> { ["oneway:foot"]="yes" }),
    new GeoJsonRoadFeature { Highway="residential", Coordinates=new[] { C(-100,10),C(0,10),C(100,10) } },
    Way("residential", C(0,10), C(0), new Dictionary<string,string> { ["oneway:foot"]="yes" }) }));
var alternativeStart = await deadEndService.FindRouteAsync(C(20), C(-100,10));
Check(alternativeStart is not null && alternativeStart.Points[0].Coordinate.DistanceTo(C(20,10)) < 0.1,
    "nearby reachable segment is considered when the closest one-way segment is a directed dead end");
var reachableAccess = deadEndService.FindRoadApproachTarget(C(20), C(-100,10));
Check(reachableAccess is { ConnectsToDestination: true } && reachableAccess.Value.Coordinate.DistanceTo(C(20,10)) < 0.1,
    "road approach proves directed reachability rather than just weak connectivity");
var wallGraph = new RoadGraphBuilder().Build(new[] { new GeoJsonRoadFeature
    { Highway="path", Coordinates=new[] { C(0),C(100),C(200) } } },
    new[] { new GeoJsonPointFeature { Barrier="wall", Coordinate=C(100) } });
Check(!CanReach(wallGraph,C(0),C(200)), "mapped wall splits approaches without deleting either side");
var gradeGraph = new RoadGraphBuilder().Build(new[] {
    new GeoJsonRoadFeature { Highway="residential", Coordinates=new[] { C(0,-100),C(0),C(0,100) } },
    new GeoJsonRoadFeature { Highway="path", Coordinates=new[] { C(-100),C(0),C(100) },
        Tags=new Dictionary<string,string> { ["bridge"]="yes" } } });
Check(!CanReach(gradeGraph,C(-100),C(0,100)), "bridge interior does not connect to the road beneath it");
GeoJsonRoadFeature SourceWay(GeoCoordinate[] coordinates, string[] identifiers) =>
    new() { Highway="path", Coordinates=coordinates, OsmNodeIds=identifiers };
var distinctSourceNodes = new RoadGraphBuilder().Build(new[] {
    SourceWay(new[] { C(-100),C(0),C(100) }, new[] { "1","2","3" }),
    SourceWay(new[] { C(0,-100),C(0),C(0,100) }, new[] { "4","5","6" }) });
Check(!CanReach(distinctSourceNodes,C(-100),C(0,100)),
    "different OSM nodes at the same coordinate cannot manufacture a junction");
var sharedSourceNodes = new RoadGraphBuilder().Build(new[] {
    SourceWay(new[] { C(-100),C(0),C(100) }, new[] { "1","2","3" }),
    SourceWay(new[] { C(0,-100),C(0),C(0,100) }, new[] { "4","2","6" }) });
Check(CanReach(sharedSourceNodes,C(-100),C(0,100)),
    "matching OSM node IDs retain an actual mapped junction");
var sourceBarrierGraph = new RoadGraphBuilder().Build(new[] {
    SourceWay(new[] { C(-100),C(0),C(100) }, new[] { "1","2","3" }),
    SourceWay(new[] { C(0,-100),C(0),C(0,100) }, new[] { "4","5","6" }) },
    new[] { new GeoJsonPointFeature { OsmId="2", Barrier="gate", Coordinate=C(0),
        Tags=new Dictionary<string,string> { ["locked"]="yes" } } });
Check(!CanReach(sourceBarrierGraph,C(-100),C(100)) && CanReach(sourceBarrierGraph,C(0,-100),C(0,100)),
    "central barrier restrictions split their source node without blocking a distinct colocated node");
var preferenceGraph = new RoadGraphBuilder().Build(new[] {
    Way("primary",C(0),C(200)),
    Way("residential",C(0),C(0,20)), Way("residential",C(0,20),C(200,20)),
    Way("residential",C(200,20),C(200)) });
var preferredRoute = await new AStarRoutingService(preferenceGraph).FindRouteAsync(C(0),C(200));
Check(preferredRoute is not null && preferredRoute.Points.Any(point => point.Coordinate == C(0,20)) &&
    Math.Abs(preferredRoute.TotalDistanceMeters - preferredRoute.Points[^1].DistanceFromStartMeters) < 0.01,
    "street preferences preserve actual route distances for AR");
var detourGraph = new RoadGraphBuilder().Build(new[] {
    Way("residential",C(0),C(100)), Way("residential",C(0),C(0,400)),
    Way("residential",C(0,400),C(100,400)), Way("residential",C(100,400),C(100)) });
RouteHazard Hazard(string id, GeoCoordinate center, double radius) =>
    new(id,center,radius,"test","high","test hazard","regression",DateTimeOffset.UtcNow);
var hazard = Hazard("test-block", C(50), 25);
var safeDetour = await new AStarRoutingService(detourGraph).FindRouteAvoidingHazardsAsync(
    C(0),C(100),new[] { hazard });
Check(safeDetour is not null && safeDetour.TotalDistanceMeters > 800 &&
    RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(safeDetour,C(0),new[] { hazard }),
    "hazard avoidance accepts a valid route longer than three times direct distance");
var middleStartGraph = new RoadGraphBuilder().Build(new[] {
    Way("residential",C(0),C(200)), Way("residential",C(0),C(0,400)),
    Way("residential",C(0,400),C(200,400)), Way("residential",C(200,400),C(200)) });
var middleHazards = new[] { Hazard("mid-road-block",C(150),10) };
var middleDetour = await new AStarRoutingService(middleStartGraph).FindRouteAvoidingHazardsAsync(
    C(100),C(200),middleHazards);
Check(middleDetour is not null && middleDetour.Points[0].Coordinate.DistanceTo(C(100)) < 0.1 &&
    RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(middleDetour,C(100),middleHazards),
    "hazard routing can start midway along a sparse road without crossing the blocked partial segment");
Check(!RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(Route(C(15),C(5),C(100)), C(15),
    new[] { Hazard("escape",C(0),20) }), "both engines reject moving deeper into a starting hazard");
Check(RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(Route(C(5),C(15),C(100)), C(5),
    new[] { Hazard("escape",C(0),20) }), "both engines allow a mapped outward hazard escape");
var urlMethod = typeof(MLDRoutingService).GetMethod("BuildRouteUrl",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
string endpointUrl = (string)urlMethod.Invoke(null,new object[] { "http://test", new[] { C(0),C(100) },0 })!;
string bypassUrl = (string)urlMethod.Invoke(null,new object[] { "http://test", new[] { C(0),C(50,80),C(100) },0 })!;
Check(endpointUrl.Contains("radiuses=20;50") && bypassUrl.Contains("radiuses=20;unlimited;50"),
    "MLD endpoints use shared bounds while hazard bypasses remain steering hints");
var parseRoutes = typeof(MLDRoutingService).GetMethod("ParseRoutes",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
bool endpointUnmatched = false;
try { parseRoutes.Invoke(null,new object[] { "{\"code\":\"NoSegment\"}","MLD" }); }
catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is RoutingEndpointUnmatchedException)
{ endpointUnmatched = true; }
Check(endpointUnmatched, "MLD NoSegment is distinguished from a disconnected NoRoute result");
Check(((IReadOnlyList<RouteResult>)parseRoutes.Invoke(null,new object[] { "{\"code\":\"NoRoute\"}","MLD" })!).Count == 0,
    "MLD NoRoute remains an authoritative disconnected result");
var geometryRoute = ((IReadOnlyList<RouteResult>)parseRoutes.Invoke(null,new object[] {
    "{\"code\":\"Ok\",\"routes\":[{\"distance\":999,\"geometry\":{\"coordinates\":[[121,14],[121.001,14]]}}]}","MLD" })!)[0];
Check(Math.Abs(geometryRoute.TotalDistanceMeters - geometryRoute.Points[^1].DistanceFromStartMeters) < 0.01,
    "MLD progress and total distance use the same physical geometry");
bool invalidGeometryRejected = false;
try { parseRoutes.Invoke(null,new object[] {
    "{\"code\":\"Ok\",\"routes\":[{\"geometry\":{\"coordinates\":[[0,0],[0,95],[0.001,0]]}}]}","MLD" }); }
catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
{ invalidGeometryRejected = true; }
Check(invalidGeometryRejected, "MLD invalid interior geometry is an invalid response, not a disconnected route");
var readOsrmCode = typeof(MLDRoutingService).GetMethod("TryReadOsrmCode",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
Check(new[] { "[]", "{\"code\":123}", "broken JSON" }.All(body =>
    !(bool)readOsrmCode.Invoke(null, new object?[] { body, null })!),
    "malformed HTTP error bodies cannot crash MLD outcome classification");
using (var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
    "{\"type\":\"FeatureCollection\",\"features\":[{\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[0,0],[0.001,0]]},\"properties\":{\"highway\":\"service\",\"foot\":\"yes\",\"access\":\"no\",\"tunnel\":\"building_passage\"}}]}")))
{
    var exportedRoads = await new GeoJsonRoadLoader().LoadRoadsAsync(json);
    Check(exportedRoads.Count == 1 && pedestrianFilter.IsWalkable(exportedRoads[0]),
        "flat exported tags preserve pedestrian permission and building passage access");
}
using (var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
    "{\"type\":\"FeatureCollection\",\"features\":[{\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[121,14],[121.001,14]]},\"properties\":{\"highway\":\"path\",\"osm_node_ids\":[\"123\",\"456\"]}}]}")))
    Check((await new GeoJsonRoadLoader().LoadRoadsAsync(json))[0].OsmNodeIds.SequenceEqual(new[] { "123", "456" }),
        "central export node identifiers survive parsing into the routing graph");
bool incompleteIdsRejected = false;
using (var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
    "{\"type\":\"FeatureCollection\",\"features\":[{\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[121,14],[121.001,14]]},\"properties\":{\"highway\":\"path\",\"osm_node_ids\":[\"123\"]}}]}")))
{
    try { await new GeoJsonRoadLoader().LoadRoadsAsync(json); }
    catch (InvalidDataException) { incompleteIdsRejected = true; }
}
Check(incompleteIdsRejected, "incomplete central source IDs fail instead of reverting to invented coordinate junctions");
using (var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
    "{\"type\":\"FeatureCollection\",\"features\":[{\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[0,0],[0,95],[0.001,0]]},\"properties\":{\"highway\":\"path\"}}]}")))
    Check((await new GeoJsonRoadLoader().LoadRoadsAsync(json)).Count == 0,
        "offline invalid interior geometry cannot manufacture a shortcut");
var actualNetwork = await NavigationDataBootstrap.ValidateOnceAsync();
Check(actualNetwork.DirectedEdgeCount > 0 &&
    actualNetwork.AcceptedRoadFeatureCount > 0,
    "available datasets build a usable pedestrian graph");
var actualRoads = await NavigationDataBootstrap.GetRoadFeaturesAsync();
var regionalService = new AStarRoutingService(await NavigationDataBootstrap.GetRoadGraphAsync());
var qcLocal = await regionalService.FindRouteAsync(new(14.71374502,121.01964361),new(14.724923,121.024763));
var marikinaLocal = await regionalService.FindRouteAsync(new(14.644776,121.094967),new(14.660172,121.118353));
Check(qcLocal is not null && qcLocal.Points.Count > 1, "QC tester request routes on the expanded offline network");
Check(marikinaLocal is not null && marikinaLocal.Points.Count > 1, "Marikina request routes on the expanded offline network");
var qc2Street = actualRoads.First(road => road.OsmId == "13845789");
var qc2Local = await regionalService.FindRouteAsync(qc2Street.Coordinates[0],qc2Street.Coordinates[^1]);
Check(qc2Local is not null && qc2Local.Points.Count > 1,
    "QC_TEST2 Hilario Moncado Street provides an actual local AStar route");
Check(await regionalService.FindRouteAsync(new(14.644776,121.094967), new(14.647648,121.103974)) is not null,
    "embedded Concepcion Elementary destination can match the nearby road segment");
string tipPointsResource = NavigationDataBootstrap.GeoJsonResourcePrefix + "QC_TIP_POINTS.GEOJSON";
if (NavigationDataBootstrap.GetGeoJsonResourceNames("POINTS").Contains(tipPointsResource))
{
    using Stream tipPoints = NavigationDataBootstrap.OpenGeoJsonResource(tipPointsResource);
    var restrictions = await new GeoJsonRoadLoader().LoadPointsAsync(tipPoints);
    string[] missingBarrierIds = { "8678367374", "8931729696", "11489648941", "246784995",
        "11484349473", "10783437609", "12497591656", "8628789127" };
    Check(restrictions.Count == 1309 && missingBarrierIds.All(id => restrictions.Any(p => p.OsmId == id &&
        EvacuationRoutingPolicy.IsBlockedPoint(p.Barrier, p.Tags))),
        "TIP points preserve all eight previously missing source access barriers");
}
Check(NavigationDataBootstrap.DatasetId.StartsWith("regional-") || NavigationDataBootstrap.DatasetId.StartsWith("central-"),
    "routing diagnostics identify the exact embedded policy and source data");
var balboaBend = actualRoads.Where(road => road.OsmId == "48381228").ToArray();
if (balboaBend.Length > 0)
{
    var actualGraph = await NavigationDataBootstrap.GetRoadGraphAsync();
    Check(balboaBend.All(pedestrianFilter.IsWalkable) &&
        actualGraph.Edges.Any(edge => edge.OsmId == "48381228"),
        "QC Balboa Bend private road contributes actual routing edges");
}
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
SafeZoneConfirmationService.SafeZoneDecision ObserveArrival(double seconds,
    double distance = 1, double? accuracy = 5, bool barrier = false,
    double? fixAge = null) => arrival.Evaluate(C(distance), C(0), 75, accuracy,
        double.PositiveInfinity, now.AddSeconds(seconds - (fixAge ?? 0)),
        majorRoadBarrier: barrier, evaluatedAt: now.AddSeconds(seconds));
Check(!ObserveArrival(0).IsConfirmed, "arrival starts a proximity observation sequence");
Check(!ObserveArrival(0).IsConfirmed,
    "duplicate GPS observation cannot confirm arrival");
var blocked = ObserveArrival(1, barrier: true);
Check(!blocked.IsConfirmed && blocked.ConfirmationCount == 0,
    "major road cancels proximity-only arrival");
ObserveArrival(2);
ObserveArrival(5);
Check(ObserveArrival(8).IsConfirmed,
    "repeated fresh observations confirm vicinity without requiring a mapped route");
arrival.Reset();
Check(!ObserveArrival(0, distance: 60, accuracy: 8.5).IsCandidate &&
    !ObserveArrival(4, distance: 60, accuracy: 8.5).IsConfirmed,
    "field-test fixes sixty metres from the pin cannot confirm arrival inside a broad facility profile");
Check(ObserveArrival(5, distance: 24, accuracy: 5).IsCandidate &&
    ObserveArrival(6, distance: 41, accuracy: 5).IsObservationHeld &&
    !arrival.Current.IsConfirmed && arrival.Current.ConfirmationCount == 1,
    "boundary uncertainty pauses arrival without counting a fix outside the automatic radius");
ObserveArrival(7);
Check(ObserveArrival(8, accuracy: 30).ConfirmationCount == 0,
    "weak arrival fixes reset the consecutive sequence");
ObserveArrival(9);
Check(ObserveArrival(10, fixAge: 6).ConfirmationCount == 0 &&
    !ObserveArrival(11, fixAge: -3).IsCandidate,
    "stale and future arrival fixes cannot check in");
arrival.Reset();
ObserveArrival(0);
Check(ObserveArrival(13).ConfirmationCount == 1,
    "arrival evidence expires across a long GPS outage");
arrival.Reset();
ObserveArrival(0);
ObserveArrival(0.1);
Check(!ObserveArrival(0.2).IsConfirmed,
    "a burst of GPS fixes cannot satisfy arrival dwell time");
Check(SafeZoneFacilityCatalog.GetArrivalRadiusMeters("SV Tennis Court") == 30,
    "unprofiled test destinations default to thirty metres");
var sample = default(RouteProgressTracker.RouteProgressUpdate) with
{
    IsAccepted=true, IsOffRoute=true, CrossTrackErrorMeters=35,
    AccuracyMeters=5, MatchConfidence=RouteMatchConfidence.High,
    NearestRouteDistanceMeters=double.NaN, CorridorRadiusMeters=15
};
bool Trigger(OffRouteReroutePolicy policy, DateTimeOffset at)
{
    policy.Evaluate(sample,at);
    policy.Evaluate(sample,at.AddSeconds(2));
    return policy.Evaluate(sample,at.AddSeconds(4)).ShouldReroute;
}
var reroute = new OffRouteReroutePolicy();
Check(Trigger(reroute,now), "three reliable off-route observations trigger");
reroute.Reset();
reroute.Evaluate(sample, now);
Check(reroute.Evaluate(sample, now).ConfirmationCount == 1 &&
    !reroute.Evaluate(sample, now.AddMilliseconds(100)).ShouldReroute,
    "duplicate and burst GPS observations do not satisfy reroute confirmation");
reroute.Reset();
reroute.Evaluate(sample, now);
reroute.Evaluate(sample with { AccuracyMeters = 50, MatchConfidence = RouteMatchConfidence.Unavailable }, now.AddSeconds(2));
reroute.Evaluate(sample, now.AddSeconds(4));
Check(reroute.Evaluate(sample, now.AddSeconds(6)).ShouldReroute,
    "one weak fix does not discard recent reliable deviation evidence");
reroute.Reset();
reroute.Evaluate(sample, now);
reroute.Evaluate(sample with { IsOffRoute = false }, now.AddSeconds(2));
Check(reroute.Evaluate(sample, now.AddSeconds(4)).ConfirmationCount == 1,
    "reliable return to the route clears deviation evidence");
reroute.Reset();
reroute.Evaluate(sample, now);
Check(reroute.Evaluate(sample, now.AddSeconds(16)).ConfirmationCount == 1,
    "old deviation evidence expires after a GPS outage");
reroute.Reset();
reroute.MarkRerouteFailed(now,awaitingConfirmation:true);
Check(!reroute.Evaluate(sample,now.AddSeconds(2)).ShouldReroute && Trigger(reroute,now.AddSeconds(4)),
    "pending confirmation retries after three seconds");
reroute.Reset();
reroute.MarkRerouteFailed(now);
Check(!reroute.Evaluate(sample,now.AddSeconds(9)).ShouldReroute && Trigger(reroute,now.AddSeconds(10)),
    "failed route retries after ten seconds");
reroute.MarkRerouteFailed(now.AddSeconds(15));
Check(!reroute.Evaluate(sample,now.AddSeconds(34)).ShouldReroute && Trigger(reroute,now.AddSeconds(35)),
    "repeated route failures back off instead of repeatedly rerouting");
reroute.MarkRerouteCompleted(now.AddSeconds(40));
Check(!reroute.Evaluate(sample,now.AddSeconds(59)).ShouldReroute && Trigger(reroute,now.AddSeconds(60)),
    "a successful replacement permits a new confirmed deviation after twenty seconds");
var ambiguousDeviation = sample with { MatchConfidence = RouteMatchConfidence.Low,
    NearestRouteDistanceMeters = 80, CrossTrackErrorMeters = 85, CorridorRadiusMeters = 15 };
reroute.Reset();
reroute.Evaluate(ambiguousDeviation, now);
reroute.Evaluate(ambiguousDeviation, now.AddSeconds(2));
Check(reroute.Evaluate(ambiguousDeviation, now.AddSeconds(4)).ShouldReroute,
    "clear distance from the whole route can confirm deviation despite ambiguous segment identity");
reroute.Reset();
Check(!reroute.Evaluate(ambiguousDeviation with { NearestRouteDistanceMeters = 5 }, now).IsCandidate,
    "continuity scoring cannot reroute a user already near another segment of the route");

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
var roadStartReplacement = Route(C(38), C(100));
replacement.Reset();
Check(!replacement.Evaluate(previous, progress, roadStartReplacement, C(0), C(100),
    false, now).IsAccepted && replacement.Evaluate(previous, progress, roadStartReplacement,
    C(0), C(100), false, now, allowRoadApproach: true).IsAccepted,
    "a checked thirty-eight metre road approach can update navigation without permitting AR placement");
Check(!replacement.Evaluate(previous, progress, Route(C(51), C(100)), C(0), C(100),
    false, now, allowRoadApproach: true).IsAccepted,
    "replacement routes cannot silently snap beyond the fifty metre access limit");
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
var androidTransport = new HybridRoutingService(new ThrowingRoutingService(
    new System.Net.WebException("network unavailable", System.Net.WebExceptionStatus.ConnectFailure)),
    _ => Task.FromResult(new RoadGraph(nodes,edges)), () => true);
Check(await androidTransport.FindRouteAsync(C(0,1), C(100,8)) is not null,
    "Android WebException transport failures use the existing offline graph");
var androidTimeout = new HybridRoutingService(new ThrowingRoutingService(
    new System.Net.WebException("request timed out", System.Net.WebExceptionStatus.RequestCanceled)),
    _ => Task.FromResult(new RoadGraph(nodes,edges)), () => true);
Check(await androidTimeout.FindRouteAsync(C(0,1), C(100,8)) is not null,
    "Android transport cancellation falls back when the caller has not cancelled");
var cancelledTransport = new HybridRoutingService(new ThrowingRoutingService(
    new OperationCanceledException()), _ => Task.FromResult(new RoadGraph(nodes,edges)), () => true);
Check(await cancelledTransport.FindRouteAsync(C(0,1), C(100,8)) is not null,
    "an online timeout falls back to offline routing");
bool callerCancellationPropagated = false;
using (var cancelledRequest = new CancellationTokenSource())
{
    cancelledRequest.Cancel();
    try { await androidTransport.FindRouteAsync(C(0,1), C(100,8), cancelledRequest.Token); }
    catch (OperationCanceledException) { callerCancellationPropagated = true; }
}
Check(callerCancellationPropagated, "caller cancellation never launches an offline fallback");
var noRouteGraphLoads = 0;
var onlineNoRoute = new HybridRoutingService(new EmptyRoutingService(),
    _ => { noRouteGraphLoads++; return Task.FromResult(new RoadGraph(nodes,edges)); }, () => true);
Check(await onlineNoRoute.FindRouteAsync(C(0,1),C(100,8)) is null && noRouteGraphLoads == 0,
    "an authoritative online NoRoute result still preserves the routing policy");
var unmatchedOnline = new HybridRoutingService(new ThrowingRoutingService(new RoutingEndpointUnmatchedException()),
    _ => Task.FromResult(new RoadGraph(nodes,edges)), () => true);
Check(await unmatchedOnline.FindRouteAsync(C(0,1),C(100,8)) is not null &&
    await unmatchedOnline.FindRouteAsync(C(0,100),C(100,8)) is null,
    "MLD NoSegment tries offline matching without widening its origin radius");
var invalidOnline = new HybridRoutingService(new ThrowingRoutingService(new InvalidDataException("bad geometry")),
    _ => Task.FromResult(new RoadGraph(nodes,edges)), () => true);
Check(await invalidOnline.FindRouteAsync(C(0,1),C(100,8)) is not null,
    "invalid online responses can recover with an independently checked offline route");
int deferredAccessCalls = 0;
var deferredRoute = await new MLDARIntegrationService(new FixedRoutingService(Route(C(0),C(100))))
    .RequestRouteWithDeferredRoadApproachAsync(C(0),C(100), _ =>
    { deferredAccessCalls++; return Task.FromResult<AStarRoutingService.RoadApproachTarget?>(null); });
Check(deferredRoute is not null && deferredAccessCalls == 0,
    "a valid direct route does not wait for offline road-access graph initialization");
Check(await new MLDARIntegrationService(new FixedRoutingService(Route(C(100), C(200))))
    .RequestRouteWithRoadApproachAsync(C(0), C(200), null) is null,
    "unknown road coverage cannot silently route from a pin one hundred metres away");
Check(await new MLDARIntegrationService(new FixedRoutingService(Route(C(100), C(200))))
    .RequestRouteWithRoadApproachAsync(C(0), C(200), new(C(0), true)) is null,
    "an unusable road retry cannot return the rejected distant route");
Check(await new MLDARIntegrationService(new FixedRoutingService(Route(C(100), C(200))))
    .RequestRouteWithRoadApproachAsync(C(0), C(200), new(C(38), true)) is null,
    "a road retry cannot return a route outside the bounded approach area");

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

bool CanPreview(GeoCoordinate gps, GeoCoordinate matched, double? accuracy = 12,
    bool accepted = true, bool offRoute = false, bool access = true,
    RouteMatchConfidence confidence = RouteMatchConfidence.Medium,
    DateTimeOffset? fix = null) => MappedRoutePreviewPolicy.CanPublish(
        accepted, offRoute, confidence, accuracy, gps, matched, fix ?? now, now, access);
Check(CanPreview(C(0,-8.2), C(0)) &&
    !RouteCorridorPolicy.CanEnterRoadFollowing(true,false,8.2,25,RouteMatchConfidence.Medium,12),
    "nearby mapped route can be previewed without widening physical road-entry confirmation");
Check(!CanPreview(C(0,-21),C(0)) && !CanPreview(C(0),C(0),accuracy:21) &&
    !CanPreview(C(0),C(0),accuracy:null) && !CanPreview(C(0),C(0),accuracy:double.NaN),
    "mapped preview rejects excessive displacement and uncertain GPS");
Check(!CanPreview(C(0),C(0),accepted:false) && !CanPreview(C(0),C(0),offRoute:true) &&
    !CanPreview(C(0),C(0),access:false) &&
    !CanPreview(C(0),C(0),confidence:RouteMatchConfidence.Low),
    "no route match, confirmed deviation, blocked access and ambiguous matches cannot produce a preview");
Check(!CanPreview(C(0),C(0),fix:now.AddSeconds(-6)) &&
    !CanPreview(C(0),C(0),fix:now.AddSeconds(3)) &&
    !CanPreview(C(0),new GeoCoordinate(121,14)),
    "stale, future and invalid positions cannot display a mapped preview");
Check(!RoadApproachCuePolicy.IsAccessDirectionReliable(1.6,12) &&
    !RoadApproachCuePolicy.IsAccessDirectionReliable(8.2,20) &&
    RoadApproachCuePolicy.IsAccessDirectionReliable(8.2,3) &&
    RoadApproachCuePolicy.IsAccessDirectionReliable(35,20),
    "access arrows distinguish a supported lateral direction from GPS or map offset uncertainty");
Check(!RoadApproachCuePolicy.IsAccessDirectionReliable(35,null) &&
    !RoadApproachCuePolicy.IsAccessDirectionReliable(double.NaN,12) &&
    !RoadApproachCuePolicy.IsAccessDirectionReliable(51,12),
    "invalid or distant access targets cannot create an arrow");
var mappedPreviewGeometry = Route(C(0),C(3),C(3,20));
foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
{
    var previewRoute = new RouteResult(mappedPreviewGeometry.Points,
        mappedPreviewGeometry.TotalDistanceMeters, algorithm);
    var previewTracker = new RouteProgressTracker();
    previewTracker.SetRoute(previewRoute);
    var match = previewTracker.Update(C(0,-8.2),12);
    Check(MappedRoutePreviewPolicy.CanPublish(match.IsAccepted,match.IsOffRoute,
        match.MatchConfidence,match.AccuracyMeters,match.GpsCoordinate,
        match.SnappedCoordinate,now,now,true),
        $"{algorithm} route match permits actual geometry while road entry is still pending");
    bool published = new MLDARIntegrationService(new EmptyRoutingService()).PublishProgressWindow(
        previewRoute,match.CommittedProgressMeters,match.SnappedCoordinate,0,0,0,
        arWindowMeters:LocalArNavigationPolicy.ApproachWindowMeters,
        sourceSegmentIndex:match.SegmentIndex,userCoordinate:match.GpsCoordinate);
    var preview = ARRouteBridge.Current;
    Check(published && preview.IsAvailable && preview.Algorithm == algorithm &&
        preview.NavigationState.VisualKind == RouteVisualKind.RouteWindow && preview.Points.Count == 3,
        $"{algorithm} pending-entry preview publishes a cyan polyline with its mapped corner");
    Check(Math.Abs(preview.Points[0].X)<0.01 && Math.Abs(preview.Points[0].Z+8.2)<0.01 &&
        Math.Abs(preview.Points[1].X-3)<0.01 && Math.Abs(preview.Points[1].Z+8.2)<0.01,
        $"{algorithm} preview starts on mapped geometry and never draws a GPS-to-road connector");
}
ARRouteBridge.Clear();

arrival.Reset();
ObserveArrival(0,distance:14.5,accuracy:11);
var tipBoundaryNoise = ObserveArrival(4.935,distance:41,accuracy:9);
Check(tipBoundaryNoise.IsObservationHeld && tipBoundaryNoise.ConfirmationCount == 1 &&
    !tipBoundaryNoise.IsConfirmed,
    "TIP boundary jitter holds earlier arrival evidence without confirming the uncertain position");
Check(ObserveArrival(9.490,distance:2.8,accuracy:14).IsConfirmed &&
    ObserveArrival(13.964,distance:8.1,accuracy:10).IsConfirmed,
    "two qualifying observations complete arrival across a brief uncertain boundary sample");
arrival.Reset();
ObserveArrival(0);
Check(ObserveArrival(4,distance:9.7,accuracy:25.5).IsObservationHeld &&
    !ObserveArrival(13,distance:9.7,accuracy:25.5).IsCandidate &&
    ObserveArrival(14).ConfirmationCount == 1,
    "weak nearby fixes cannot extend the arrival evidence timeout");
arrival.Reset();
ObserveArrival(0);
Check(!ObserveArrival(4,distance:52.7,accuracy:27.9).IsCandidate &&
    ObserveArrival(5).ConfirmationCount == 1,
    "a large GPS departure resets arrival instead of bridging separate visits");
arrival.Reset();
ObserveArrival(0);
Check(!ObserveArrival(4,barrier:true).IsCandidate && arrival.Current.ConfirmationCount == 0,
    "a mapped major-road barrier cancels held arrival evidence immediately");

var nearDestination = new DestinationProximityReroutePolicy();
bool HoldNear(double seconds,double distance,double accuracy=12,bool access=true) =>
    nearDestination.ShouldHold(C(distance),C(0),accuracy,now.AddSeconds(seconds),
        now.AddSeconds(seconds),access);
Check(HoldNear(0,17.5,21.5) && HoldNear(4,52.7,27.9),
    "a nearby destination observation suppresses a brief TIP GPS jump");
var nearOffRoute = new RouteProgressTracker.RouteProgressUpdate(true,true,false,0,0,0,10,
    52.8,10,C(52.8),C(0),"OFF_ROUTE",15,RouteMatchConfidence.High,double.NaN,52.8);
var nearRerouteDecision = new OffRouteReroutePolicy().Evaluate(nearOffRoute,now,true);
Check(!nearRerouteDecision.ShouldReroute && nearRerouteDecision.ConfirmationCount == 0,
    "near-destination drift cannot build an off-route rerouting sequence");
Check(HoldNear(8,80,10) && !HoldNear(10,85,10),
    "two independent accurate departures restore normal routing away from the destination");
nearDestination.Reset();
HoldNear(0,20);
Check(!HoldNear(31,50,29), "destination proximity hold expires without new nearby evidence");
nearDestination.Reset();
HoldNear(0,20);
Check(!HoldNear(2,20,12,access:false), "major-road access uncertainty cannot suppress necessary rerouting");
nearDestination.Reset();
HoldNear(0,20);
Check(!nearDestination.ShouldHold(C(20),C(500),12,now.AddSeconds(1),now.AddSeconds(1)),
    "changing the destination discards the previous proximity hold");

Check(RouteAnchorRecoveryPolicy.Evaluate(false,true,true) == RouteAnchorRecoveryAction.DiscardPreview &&
    RouteAnchorRecoveryPolicy.Evaluate(true,true,true) == RouteAnchorRecoveryAction.RebaseRoute,
    "a tracked replacement anchor releases approach recovery without waiting for physical road entry");
Check(RouteAnchorRecoveryPolicy.Evaluate(false,false,true) == RouteAnchorRecoveryAction.WaitForFloor &&
    RouteAnchorRecoveryPolicy.Evaluate(false,true,false) == RouteAnchorRecoveryAction.WaitForFloor,
    "stale tracking or mismatched ground generation cannot complete an approach anchor handoff");

Check(UserArrivalConfirmationPolicy.CanOffer(C(0),C(29.8),27,now,now),
    "the indoor screenshot's nearby uncertain fix can offer user-reported arrival");
arrival.Reset();
Check(!ObserveArrival(0,distance:29.8,accuracy:27).IsConfirmed &&
    !ObserveArrival(8,distance:29.8,accuracy:27).IsConfirmed,
    "manual arrival eligibility does not relax automatic GPS confirmation");
Check(!UserArrivalConfirmationPolicy.CanOffer(C(0),C(76),5,now,now) &&
    !UserArrivalConfirmationPolicy.CanOffer(C(0),C(10),51,now,now) &&
    !UserArrivalConfirmationPolicy.CanOffer(C(0),C(10),null,now,now),
    "manual arrival is unavailable for distant pins or unbounded GPS uncertainty");
Check(!UserArrivalConfirmationPolicy.CanOffer(C(0),C(10),27,now.AddSeconds(-121),now) &&
    !UserArrivalConfirmationPolicy.CanOffer(C(0),C(10),27,now.AddSeconds(3),now) &&
    !UserArrivalConfirmationPolicy.CanOffer(new(double.NaN,0),C(10),27,now,now),
    "manual arrival rejects stale, future and invalid location evidence");

var cameraTarget = ARCameraConversionPolicy.GetTargetSize(1080,1901);
Check(cameraTarget.Width == 727 && cameraTarget.Height == 1280 &&
    (long)cameraTarget.Width * cameraTarget.Height < 1080L * 1901 / 2,
    "TIP camera conversion uses less than half the former output pixels");
Check(Math.Abs(cameraTarget.Width/(double)cameraTarget.Height - 1080.0/1901) < 0.001 &&
    ARCameraConversionPolicy.GetTargetSize(1901,1080) == (1280u,727u) &&
    ARCameraConversionPolicy.GetTargetSize(640,480) == (640u,480u),
    "conversion preserves portrait and landscape proportions without upscaling");
Check(ArCameraImportFailurePolicy.Classify(new TimeoutException(
    "ARCORE_CAMERA_GPU_TIMEOUT: pending conversion")) == ArCameraImportFailureDisposition.TerminalRenderer &&
    ArCameraImportFailurePolicy.Classify(new InvalidOperationException(
        "ARCORE_CAMERA_GPU_FAILURE: vkWaitForFences failed")) == ArCameraImportFailureDisposition.TerminalRenderer &&
    ArCameraImportFailurePolicy.Classify(new InvalidOperationException("VK_ERROR_DEVICE_LOST")) ==
        ArCameraImportFailureDisposition.TerminalRenderer &&
    ArCameraImportFailurePolicy.Classify(new TimeoutException("unrelated transient timeout")) ==
        ArCameraImportFailureDisposition.RetryWithBackoff,
    "uncertain GPU ownership is terminal while unrelated transient import failures retain bounded retries");

bool conversionPending = true;
int cameraCallbacks = 0;
ARCameraTextureBridge.SetDrawThreadProcessor(987, () => { cameraCallbacks++; return conversionPending; });
ARCameraTextureBridge.ResumeProcessing(987);
Check(ARCameraTextureBridge.ProcessDrawThreadWork(987) && cameraCallbacks == 1,
    "a pending conversion skips drawing its unfinished target");
conversionPending = false;
Check(!ARCameraTextureBridge.ProcessDrawThreadWork(987) && cameraCallbacks == 2,
    "drawing resumes after the conversion callback reports completion");
Check(ARCameraTextureBridge.ProcessDrawThreadWork(986) && cameraCallbacks == 2,
    "an old draw generation cannot consume current camera work");
ARCameraTextureBridge.SetDrawThreadProcessor(987, () =>
{
    ARCameraTextureBridge.SuspendProcessing(TimeSpan.Zero);
    return false;
});
ARCameraTextureBridge.ResumeProcessing(987);
Check(ARCameraTextureBridge.ProcessDrawThreadWork(987),
    "terminal suspension inside import prevents presentation in the same draw callback");
var cameraTeardown = ARCameraTextureBridge.RequestDrawThreadTeardownAsync(987, () => { });
Check(ARCameraTextureBridge.ProcessDrawThreadWork(987) && !cameraTeardown.IsCompleted,
    "pending conversion support preserves the draw-boundary teardown barrier");
ARCameraTextureBridge.CompleteDrawThreadFrame(987);
Check(cameraTeardown.IsCompletedSuccessfully,
    "graphics teardown completes only after the skipped draw is acknowledged");

await ARSystemAuditChecks.RunAsync(Check);
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

sealed class ThrowingRoutingService(Exception failure) : IRoutingService
{
    public string AlgorithmName => "MLD transport fixture";
    public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
        CancellationToken cancellationToken = default) => Task.FromException<RouteResult?>(failure);
}

sealed class EmptyRoutingService : IRoutingService
{
    public string AlgorithmName => "MLD NoRoute fixture";
    public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
        CancellationToken cancellationToken = default) => Task.FromResult<RouteResult?>(null);
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
