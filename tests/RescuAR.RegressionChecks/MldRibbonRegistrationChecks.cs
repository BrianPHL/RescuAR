using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

internal static class MldRibbonRegistrationChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(
        14.64 + north / 6371008.8 * 180 / Math.PI,
        121.095 + east / (6371008.8 * Math.Cos(14.64 * Math.PI / 180)) * 180 / Math.PI);

    public static void Run(Action<bool, string> check)
    {
        check(ARRouteVisualPolicy.ShouldRenderForwardArrow(RouteVisualKind.RouteWindow) &&
            !ARRouteVisualPolicy.ShouldRenderForwardArrow(RouteVisualKind.Unavailable),
            "solid route: qualified mapped windows retain their ribbon and a forward arrowhead");
        check(ARRouteVisualPolicy.ShouldRenderForwardArrow(RouteVisualKind.ApproachConnector),
            "solid route: a verified access connector retains its directional arrow");
        check(ARRouteVisualPolicy.GetRouteWidthMeters(0) == .25f &&
            ARRouteVisualPolicy.GetRouteWidthMeters(30) == .25f,
            "solid route: the quarter-metre ribbon keeps one width across near and far segments");
        foreach (string algorithm in new[] { "MLD (Railway OSRM)", "AStar" })
        foreach (double yaw in new[] { 0.0, 90.0, -77.75 })
        {
            var route = new RouteResult(new[] { new RoutePoint(C(0), 0), new RoutePoint(C(100), 100),
                new RoutePoint(C(100, 50), 150) }, 150, algorithm);
            var at = DateTimeOffset.UtcNow.AddSeconds(-5);
            var policy = new RouteDisplayProgressPolicy();
            var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
            RouteBacktrackingPolicy.TrackedMotion M(double east, double north = 0)
            {
                var ar = MapToArCoordinates.Rotate(east, north, yaw);
                return new((float)ar.X, (float)ar.Z, yaw, 1, 2, 3, 4);
            }
            var first = tracker.Update(C(10, 3), 7);
            policy.ObserveGps(route, first, at);
            policy.ObserveRegistration(route, first, at, at, M(0));
            check(policy.TryGetRegisteredDisplayPosition(route, at, M(0), out var start) &&
                Math.Abs(start.ProgressMeters - 10) < .02,
                $"{algorithm}/{yaw}: acquisition establishes display progress without centering the road on the camera");
            policy.TryGetRegisteredReference(route, C(50), at, M(0), out var worldBefore);
            var noise = tracker.Update(C(18, 2.7), 7);
            policy.ObserveGps(route, noise, at.AddSeconds(3));
            policy.ObserveRegistration(route, noise, at.AddSeconds(3), at.AddSeconds(3), M(.04));
            check(policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(3), M(.04), out var stationary) &&
                Math.Abs(stationary.ProgressMeters - 10.04) < .02 && noise.RawProgressMeters > 17.9,
                $"{algorithm}/{yaw}: an 8m GPS change with 4cm tracked movement cannot drag the visible window");
            check(policy.TryGetRegisteredReference(route, C(50), at.AddSeconds(3), M(.04), out var worldAfter) &&
                Math.Abs(worldBefore.WorldX - worldAfter.WorldX) < .001 &&
                Math.Abs(worldBefore.WorldZ - worldAfter.WorldZ) < .001,
                $"{algorithm}/{yaw}: the same future road point stays in the same world position across a noisy fix");
            check(policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(5), out var walked) &&
                Math.Abs(walked.ProgressMeters - 15) < .02 &&
                Math.Abs(walked.AlignmentCoordinate.DistanceTo(walked.RoadCoordinate) - 3) < .02,
                $"{algorithm}/{yaw}: walking slides the ribbon along the existing route while retaining lateral registration");
            check(policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(5) with { GroundReference = 9 }, out var floor) &&
                Math.Abs(floor.ProgressMeters - walked.ProgressMeters) < .001,
                $"{algorithm}/{yaw}: a floor handoff in the same camera world does not jump display progress");
            check(!policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(90, 17), out _),
                $"{algorithm}/{yaw}: a discontinuous camera jump cannot select a distant route leg");
            for (int east = 10; east <= 90; east += 5)
                policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(east), out _);
            for (int north = 4; north <= 16; north += 4)
                policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(90, north), out _);
            check(policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(90, 17), out var bend) &&
                Math.Abs(bend.ProgressMeters - 120) < .03,
                $"{algorithm}/{yaw}: projection preserves the mapped corner instead of creating a direct destination arrow");
            check(!policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(5) with { TrackingEpoch = 8 }, out _) &&
                !policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(5) with { WorldFrame = 8 }, out _) &&
                !policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(4), M(5) with { YawDegrees = yaw + 2 }, out _),
                $"{algorithm}/{yaw}: epoch, world and yaw changes cannot reuse the old registration");
            check(!policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(12), M(5), out _) &&
                !policy.TryGetRegisteredDisplayPosition(new(route.Points, 150, algorithm), at.AddSeconds(4), M(5), out _),
                $"{algorithm}/{yaw}: stale support and replaced routes require their own qualified reference");
            check(Math.Abs(tracker.Current.CommittedProgressMeters - noise.CommittedProgressMeters) < .001,
                $"{algorithm}/{yaw}: display projection never advances trip progress or manufactures arrival evidence");
        }
        var time = DateTimeOffset.UtcNow;
        var heading = new HeadingContinuityPolicy();
        check(heading.Observe(0, 18, time, 1, 1, 0, 1) == HeadingContinuityPolicy.Verdict.Pending &&
            heading.Observe(0, 19, time.AddSeconds(1), 1, 1, 0, 1) == HeadingContinuityPolicy.Verdict.Pending &&
            heading.Observe(0, 18, time.AddSeconds(2), 1, 1, 0, 1) == HeadingContinuityPolicy.Verdict.Contradicted,
            "alignment: repeated moderate heading disagreement withholds geometry before a 45-degree error develops");
        var noisy = new HeadingContinuityPolicy();
        noisy.Observe(0, 18, time, 1, 1, 0, 1);
        noisy.Observe(0, -18, time.AddSeconds(1), 1, 1, 0, 1);
        check(noisy.Observe(0, 18, time.AddSeconds(2), 1, 1, 0, 1) == HeadingContinuityPolicy.Verdict.Pending,
            "alignment: isolated inconsistent compass changes cannot rotate or immediately invalidate a route");
    }
}
