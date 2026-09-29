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
Console.WriteLine($"{passed} regression checks passed.");

sealed class FixedRoutingService(RouteResult route) : IRoutingService
{
    public string AlgorithmName => "AStar";
    public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin,
        GeoCoordinate destination, CancellationToken cancellationToken = default) =>
        Task.FromResult<RouteResult?>(route);
}
