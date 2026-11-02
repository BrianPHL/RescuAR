using RescuAR.AR;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

internal static class AStarRouteDisplayChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(
        14.64 + north / 6371008.8 * 180 / Math.PI,
        121.095 + east / (6371008.8 * Math.Cos(14.64 * Math.PI / 180)) * 180 / Math.PI);

    public static async Task RunAsync(Action<bool, string> check)
    {
        var graph = new RoadGraphBuilder().Build(new[] { new GeoJsonRoadFeature {
            OsmId = "accuracy-street", Highway = "footway", Coordinates = new[] { C(0), C(120) } } });
        var router = new AStarRoutingService(graph, "astar-accuracy-fixture");
        var route = await router.FindRouteAsync(C(0), C(120));
        check(route is { Points.Count: 2, Provenance.Provider: "ASTAR" },
            "A* accuracy: the production offline router supplies the sparse straight-street route");
        string geometry = route!.GeometryFingerprint;
        var generation = new ARRenderGenerationToken(2700, 2701, 2702);
        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        ARRenderGenerationBridge.Activate(generation);
        try
        {
            foreach (double yaw in new[] { 0d, 37d, -78d })
            {
                var at = DateTimeOffset.UtcNow.AddSeconds(-3);
                RouteBacktrackingPolicy.TrackedMotion M(double east = 0)
                {
                    var offset = MapToArCoordinates.Rotate(east, 0, yaw);
                    return new((float)offset.X, (float)offset.Z, yaw, generation.SessionGeneration, 1, 2, 0);
                }
                var tracker = new RouteProgressTracker();
                tracker.SetRoute(route);
                var registration = new RouteDisplayProgressPolicy();
                var seed = tracker.Update(C(10, 3), 8);
                registration.ObserveGps(route, seed, at);
                registration.ObserveRegistration(route, seed, at, at, M());

                // A plausible GPS wobble can be accepted geographically while
                // stationary motion keeps trip progress held. The old A* display
                // selected this raw match, despite its retained world reference.
                var noisyGps = tracker.Update(C(17, 3), 8);
                var held = tracker.ApplyFusionCorrection(seed.CommittedProgressMeters, noisyGps, "GPS_PDR_HoldStationary");
                registration.ObserveGps(route, held, at.AddSeconds(2));
                registration.ObserveRegistration(route, held, at.AddSeconds(2), at.AddSeconds(2), M());
                var progress = tracker.Current;
                check(registration.TryGetObservedPosition(route, at.AddSeconds(3), out var raw) &&
                    Math.Abs(raw.ProgressMeters - 17) < .02,
                    $"A* accuracy/{yaw}: fixture reproduces raw GPS moving seven metres without tracked walking");
                check(AStarRouteDisplayPolicy.TryGetDisplayPosition(route, registration, at.AddSeconds(3), M(), out var stationary) &&
                    Math.Abs(stationary.ProgressMeters - 10) < .02,
                    $"A* accuracy/{yaw}: stationary GPS noise cannot slide the registered visual window");
                check(AStarRouteDisplayPolicy.TryGetDisplayPosition(route, registration, at.AddSeconds(3), M(5), out var walking) &&
                    Math.Abs(walking.ProgressMeters - 15) < .02 &&
                    Math.Abs(walking.AlignmentCoordinate.DistanceTo(walking.RoadCoordinate) - 3) < .02,
                    $"A* accuracy/{yaw}: five metres of tracked walking advances the display five metres and preserves lateral road offset");
                check(tracker.Current == progress && held.GpsCoordinate == noisyGps.GpsCoordinate &&
                    route.GeometryFingerprint == geometry,
                    $"A* accuracy/{yaw}: display correction cannot change GPS observations, navigation progress, arrival evidence or map geometry");

                check(!AStarRouteDisplayPolicy.TryGetDisplayPosition(route, registration, at.AddSeconds(3), null, out _) &&
                    !AStarRouteDisplayPolicy.TryGetDisplayPosition(route, registration, at.AddSeconds(3), M(5) with { TrackingEpoch = 3 }, out _) &&
                    !AStarRouteDisplayPolicy.TryGetDisplayPosition(route, registration, at.AddSeconds(3), M(5) with { WorldFrame = 1 }, out _) &&
                    !AStarRouteDisplayPolicy.TryGetDisplayPosition(route, registration, at.AddSeconds(3), M(5) with { YawDegrees = yaw + 2 }, out _),
                    $"A* accuracy/{yaw}: missing tracking and changed frame or heading cannot fall back to raw GPS placement");
                check(!AStarRouteDisplayPolicy.TryGetDisplayPosition(route, registration, at.AddSeconds(11), M(5), out _) &&
                    !AStarRouteDisplayPolicy.TryGetDisplayPosition(new(route.Points, route.TotalDistanceMeters, "AStar"),
                        registration, at.AddSeconds(3), M(5), out _) &&
                    !AStarRouteDisplayPolicy.TryGetDisplayPosition(route, registration, at.AddSeconds(3), M(90), out _),
                    $"A* accuracy/{yaw}: expired support, another route and a camera jump reject the display");

                registration.TryGetRegisteredReference(route, walking.RoadCoordinate, at.AddSeconds(3), M(5), out var reference);
                var placement = new ARRoutePlacement(generation, 1, 1, Environment.TickCount64,
                    0, 0, M(5).X, M(5).Z, at.AddSeconds(2), "ASTAR_ACCURACY", yaw, TrackingEpoch: 2);
                double window = AStarRouteDisplayPolicy.LimitWindow(route, 40);
                check(new MLDARIntegrationService(router).PublishProgressWindow(route, walking.ProgressMeters,
                    walking.RoadCoordinate, yaw, 0, 0, window, sourceSegmentIndex: walking.SegmentIndex,
                    placement: placement, registeredReference: reference),
                    $"A* accuracy/{yaw}: the registered short window publishes through the production AR projector");
                var drawn = ARRouteBridge.Current;
                var first = drawn.Points[0]; var last = drawn.Points[^1];
                double length = Math.Sqrt(Math.Pow(last.X - first.X, 2) + Math.Pow(last.Z - first.Z, 2));
                check(Math.Abs(length - 7.5) < .02 &&
                    Math.Abs(first.X + placement.AnchorX - reference.WorldX) < .01 &&
                    Math.Abs(first.Z + placement.AnchorZ - reference.WorldZ) < .01,
                    $"A* accuracy/{yaw}: nearby geometry retains its registered origin and metre scale");
            }

            check(AStarRouteDisplayPolicy.LimitWindow(route, 40) == 7.5 &&
                AStarRouteDisplayPolicy.LimitWindow(route, 3) == 3,
                "A* accuracy: local look-ahead is bounded without extending a shorter hazard-safe prefix");
            double headingError = 5 * Math.PI / 180;
            check(AStarRouteDisplayPolicy.LimitWindow(route, 40) * Math.Sin(headingError) < .66 &&
                40 * Math.Sin(headingError) > 3.48,
                "A* accuracy: a five-degree heading error spans under 0.66m over the new horizon instead of over 3.48m");
            var mld = new RouteResult(route.Points, route.TotalDistanceMeters, "MLD (Railway OSRM)", new RouteProvenance("MLD"));
            check(!AStarRouteDisplayPolicy.AppliesTo(mld) && AStarRouteDisplayPolicy.LimitWindow(mld, 40) == 40 &&
                !AStarRouteDisplayPolicy.TryGetDisplayPosition(mld, new(), DateTimeOffset.UtcNow, null, out _),
                "MLD lock: A* display policy cannot alter MLD's window or position path");
        }
        finally
        {
            ARRouteBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(previous);
            else ARRenderGenerationBridge.Invalidate();
        }
    }
}
