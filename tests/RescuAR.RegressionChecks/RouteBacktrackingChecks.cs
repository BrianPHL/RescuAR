using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

internal static class RouteBacktrackingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        GeoCoordinate C(double east, double north = 0) => new(north / 111_195, east / 111_195);
        RouteResult Route(string algorithm, params GeoCoordinate[] coordinates)
        {
            double distance = 0;
            var points = new List<RoutePoint>();
            for (int i = 0; i < coordinates.Length; i++)
            {
                if (i > 0) distance += coordinates[i - 1].DistanceTo(coordinates[i]);
                points.Add(new(coordinates[i], distance));
            }
            return new(points, distance, algorithm);
        }

        var policy = new RouteBacktrackingPolicy();
        var tracker = new RouteProgressTracker();
        RouteResult route = Route("AStar", C(0), C(200));
        void Start(RouteResult next)
        {
            route = next;
            tracker.SetRoute(route);
            policy.Reset();
        }
        RouteBacktrackingPolicy.BacktrackingDecision Observe(int seconds, double x,
            double accuracy = 6, bool near = false)
        {
            var update = tracker.Update(C(x), accuracy);
            return policy.Evaluate(route, update, now.AddSeconds(seconds),
                now.AddSeconds(seconds), near);
        }

        foreach (string algorithm in new[] { "AStar", "MLD" })
        {
            Start(Route(algorithm, Enumerable.Range(0, 201).Select(x => C(x)).ToArray()));
            Observe(0, 100);
            check(!Observe(4, 92).ShouldReplan,
                $"{algorithm}: a brief reversal cannot request a replacement route");
            check(Observe(8, 84).ConfirmationCount == 1 &&
                tracker.Current.RawProgressMeters < 85 && tracker.Current.CommittedProgressMeters >= 99,
                $"{algorithm}: dense road vertices retain raw reverse movement despite monotonic progress");
            check(Observe(10, 80).ShouldReplan,
                $"{algorithm}: continued walking backward on the same road requests a current-position replan");
            check(!Observe(12, 76).ShouldReplan && !Observe(16, 70).ShouldReplan,
                $"{algorithm}: the same reverse walk cannot issue duplicate requests during cooldown");
        }

        Start(Route("AStar", C(0), C(200)));
        check(!Observe(0, 70).ShouldReplan && !Observe(8, 84).ShouldReplan &&
            !Observe(10, 90).ShouldReplan, "normal forward walking retains the existing route");
        Start(route);
        Observe(0, 70);
        check(!Observe(6, 64).ShouldReplan && !Observe(8, 76).ShouldReplan &&
            !Observe(10, 70).ShouldReplan, "stationary GPS jitter cannot establish a reverse walk");
        Start(route);
        Observe(0, 100);
        Observe(8, 80);
        check(!Observe(10, 80).ShouldReplan && !Observe(12, 80).ShouldReplan,
            "one GPS jump followed by stationary fixes cannot complete reverse confirmation");
        check(!Observe(8, 75).ShouldReplan && !Observe(7, 70).ShouldReplan,
            "duplicate and out-of-order provider timestamps cannot complete reverse confirmation");

        Start(route);
        Observe(0, 100, 15);
        check(!Observe(8, 80, 15).IsCandidate && Observe(16, 68, 15).ConfirmationCount == 1 &&
            Observe(18, 64, 15).ShouldReplan,
            "backtracking must exceed the combined uncertainty of both GPS fixes");
        Start(route);
        Observe(0, 100);
        check(!Observe(8, 80, 40).IsCandidate && !Observe(10, 76, 40).ShouldReplan,
            "weak GPS accuracy cannot trigger reverse replanning");
        check(!Observe(12, 72).ShouldReplan,
            "a precision outage discards the old walking baseline");

        Start(route);
        Observe(0, 100);
        Observe(8, 84);
        check(!Observe(10, 80, near: true).ShouldReplan && !Observe(12, 76).ShouldReplan,
            "arrival proximity clears reverse evidence instead of routing away from the destination");
        Start(route);
        Observe(0, 100);
        Observe(8, 84);
        check(!Observe(30, 80).ShouldReplan,
            "a long GPS observation gap expires reverse confirmation");
        var update = tracker.Update(C(76), 6);
        check(!policy.Evaluate(route, update, now.AddSeconds(31), now.AddSeconds(60)).ShouldReplan &&
            !policy.Evaluate(route, update, now.AddSeconds(65), now.AddSeconds(40)).ShouldReplan,
            "stale and future GPS observations cannot trigger a replacement");

        Start(route);
        Observe(0, 100);
        Observe(8, 84);
        var nextRoute = Route("AStar", C(0), C(200));
        tracker.SetRoute(nextRoute);
        check(!policy.Evaluate(nextRoute, tracker.Update(C(80), 6), now.AddSeconds(10),
            now.AddSeconds(10)).ShouldReplan, "replacement route identity clears old reverse evidence");
        policy.MarkRerouteCompleted(now.AddSeconds(10));
        check(!policy.Evaluate(route, tracker.Update(C(76), 6), now.AddSeconds(12),
            now.AddSeconds(12)).IsCandidate, "a successful replan cooldown survives the new route identity");

        Start(Route("AStar", C(0), C(200)));
        Observe(0, 0, 5);
        check(Observe(8, -12, 5).ConfirmationCount == 1 && Observe(10, -14, 5).ShouldReplan,
            "walking away at route start is detected even when matched progress clamps to zero");
        Start(Route("AStar", C(0), C(0), C(0, 200)));
        var northStart = tracker.Update(C(0), 5);
        policy.Evaluate(route, northStart, now, now);
        var south = tracker.Update(C(0, -12), 5);
        policy.Evaluate(route, south, now.AddSeconds(8), now.AddSeconds(8));
        south = tracker.Update(C(0, -14), 5);
        check(policy.Evaluate(route, south, now.AddSeconds(10), now.AddSeconds(10)).ShouldReplan,
            "route-start reverse detection handles north/south travel and duplicate starting vertices");

        Start(Route("AStar", C(0), C(200), C(200, 3), C(0, 3)));
        Observe(0, 100);
        // A fresh matcher has no continuity evidence selecting either branch.
        var ambiguousTracker = new RouteProgressTracker();
        ambiguousTracker.SetRoute(route);
        update = ambiguousTracker.Update(C(80, 1.5), 6);
        check(!policy.Evaluate(route, update, now.AddSeconds(8), now.AddSeconds(8)).IsCandidate,
            "competing parallel branches cannot establish reverse intent");
        Start(Route("AStar", C(0), C(200)));
        Observe(0, 100);
        update = tracker.Update(C(80, 60), 6);
        check(!policy.Evaluate(route, update, now.AddSeconds(8), now.AddSeconds(8)).IsCandidate,
            "actual corridor departures remain the existing off-route policy's responsibility");
    }
}
