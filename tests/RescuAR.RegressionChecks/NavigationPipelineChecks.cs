using RescuAR.AR;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

internal static class NavigationPipelineChecks
{
    private static GeoCoordinate C(double east) => new(14.644778,
        121.095031 + east / (6371008.8 * Math.Cos(14.644778 * Math.PI / 180)) * 180 / Math.PI);
    private static RouteBacktrackingPolicy.TrackedMotion M(float x, long epoch = 1) => new(x, 0, 0, 1, 1, epoch);

    public static void Run(Action<bool, string> check)
    {
        foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            var route = new RouteResult(new[] { new RoutePoint(C(0), 0), new RoutePoint(C(250), 250) }, 250, algorithm);
            var policy = new RouteProgressMotionPolicy();
            var at = DateTimeOffset.UtcNow;
            double Filter(double x, double progress, int seconds, RouteBacktrackingPolicy.TrackedMotion? motion,
                out bool held, double accuracy = 7) => policy.Filter(route, C(x), accuracy, at.AddSeconds(seconds),
                    10, progress, motion, out held);
            check(Filter(0, 10, 0, M(0), out _) == 10, $"{algorithm}: trip distance establishes its own fixed motion anchor");
            for (int i = 1; i <= 16; i++)
                check(Filter(8 * Math.Sin(i), i % 2 == 0 ? 4 : 19, i * 5, M((i % 3) * .1f), out bool held) == 10 && held,
                    $"{algorithm}: stationary sample {i} cannot ratchet or reverse trip distance");
            check(Filter(3, 13, 85, M(3), out bool walking) == 13 && !walking,
                $"{algorithm}: accumulated physical walking releases the distance hold");
            check(Filter(3, 40, 85, M(4), out bool duplicate) == 10 && duplicate,
                $"{algorithm}: duplicate acquisitions cannot alter trip distance");
            policy.Reset();
            Filter(0, 10, 0, null, out _);
            check(Filter(7, 18, 5, null, out bool gpsNoise) == 10 && gpsNoise,
                $"{algorithm}: GPS-only displacement inside combined accuracy is held");
            check(Filter(8, 19, 10, null, out bool repeated) == 10 && repeated,
                $"{algorithm}: GPS-only holds retain the original anchor rather than accumulating noise");
            check(Filter(15, 25, 15, null, out bool moved) == 25 && !moved,
                $"{algorithm}: GPS-only movement outside uncertainty remains usable");
            policy.Reset(); Filter(0, 10, 0, null, out _);
            Filter(0, 10, 5, M(0), out _);
            check(Filter(1, 13, 10, M(3), out bool acquired) == 13 && !acquired,
                $"{algorithm}: acquiring AR motion does not leave distance stuck behind GPS uncertainty");
            check(Filter(1, 50, 15, M(3), out bool bad, double.NaN) == 10 && bad,
                $"{algorithm}: invalid accuracy cannot change trip distance");
            policy.Reset();
            policy.Filter(route, C(0), 6.5, at, 20.757, 20.757, M(0), out _);
            double capturedForward = policy.Filter(route, C(-9.926), 14.07, at.AddMilliseconds(4449),
                20.757, 30.816, new(.036f, -.026f, 0, 1, 1, 1), out bool forwardHeld);
            check(forwardHeld && capturedForward == 20.757,
                $"{algorithm}: captured 10.059m forward jump with 4.4cm camera movement is held");
            policy.Reset();
            policy.Filter(route, C(0), 6.5, at, 30.816, 30.816, M(0), out _);
            double capturedBackward = policy.Filter(route, C(3.328), 6, at.AddMilliseconds(3708),
                30.816, 27.816, new(.141f, -.082f, 0, 1, 1, 1), out bool backwardHeld);
            check(backwardHeld && capturedBackward == 30.816,
                $"{algorithm}: captured 3m backward correction with 16.3cm camera movement is held");
            ARRouteBridge.Clear();
            var guidance = new PedestrianTurnGuidanceService().Evaluate(route, 10, 0);
            check(guidance.IsAvailable && Math.Abs(guidance.RemainingRouteMeters - 240) < .1,
                $"{algorithm}: geographic text remains available without an AR window");
        }

    }
}
