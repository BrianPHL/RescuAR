using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

internal static class NavigationStabilityChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(north / 111195, east / 111195);
    private static RouteBacktrackingPolicy.TrackedMotion M(float x, float z = 0, long epoch = 0) => new(x, z, 0, 1, 1, epoch);
    private static RouteResult Road(string algorithm) => new(new[] {
        new RoutePoint(C(0), 0), new RoutePoint(C(120), 120), new RoutePoint(C(120, 40), 160) }, 160, algorithm);

    public static void Run(Action<bool, string> check)
    {
        foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            var route = Road(algorithm);
            var policy = new NavigationStabilityPolicy();
            var at = DateTimeOffset.UtcNow;
            NavigationStabilityPolicy.Decision Observe(double x, double accuracy, int seconds, bool held = false) =>
                policy.Observe(route, C(x), accuracy, at.AddSeconds(seconds), at.AddSeconds(seconds), M((float)x), held);
            check(Observe(0, 5, 0) is { Reliable: true, AssessDeviation: true }, $"{algorithm}: initial qualified assessment is due");
            check(Observe(1, 5, 1) is { Reliable: true, AssessDeviation: false }, $"{algorithm}: fresh position updates do not continuously assess rerouting");
            check(Observe(3, 5, 3).AssessDeviation, $"{algorithm}: configured assessment interval releases the next assessment");
            check(!Observe(3, 5, 3).AssessDeviation, $"{algorithm}: duplicate location cannot add deviation evidence");
            check(!Observe(80, 40, 5).Reliable, $"{algorithm}: weak lateral GPS cannot become an authoritative correction");
            check(!policy.Observe(route, C(80), 5, at.AddSeconds(6), at.AddSeconds(6), M(3), false).Reliable,
                $"{algorithm}: a precise-looking GPS jump with no tracked walking is held");
            check(!policy.Observe(route, C(4), 5, at.AddSeconds(7), at.AddSeconds(11), M(4), false).Reliable,
                $"{algorithm}: stale GPS cannot trigger a correction");
            check(!Observe(6, 5, 13, held: true).Reliable, $"{algorithm}: an upstream held observation remains non-authoritative");

            policy.Reset(); Observe(0, 5, 0);
            var prolonged = Observe(70, 40, 16);
            check(prolonged is { ProlongedUncertainty: true, AssessDeviation: false },
                $"{algorithm}: prolonged uncertainty is explicit and never permits a route request");
            check(policy.TryEstimate(route, C(0), 0, at, at.AddSeconds(10), M(0), M(4), out var hold) && hold.ProgressMeters == 0,
                $"{algorithm}: the pre-timeout bridge retains fixed mapped geometry");
            check(policy.TryEstimate(route, C(0), 0, at, at.AddSeconds(16), M(0), M(4), out var estimate) &&
                Math.Abs(estimate.ProgressMeters - 4) < .01 && !policy.Current.Reliable && !policy.Current.AssessDeviation,
                $"{algorithm}: prolonged recovery estimates only along the existing road without promoting GPS authority");
            check(!policy.TryEstimate(route, C(0), 0, at, at.AddSeconds(31), M(0), M(4), out _),
                $"{algorithm}: recovery lifetime expires instead of extrapolating indefinitely");
            check(!policy.TryEstimate(route, C(0), 0, at, at.AddSeconds(16), M(0), M(9), out _) &&
                !policy.TryEstimate(route, C(0), 0, at, at.AddSeconds(16), M(0), M(4, 3), out _) &&
                !policy.TryEstimate(route, C(0), 0, at, at.AddSeconds(16), M(0), M(-4), out _),
                $"{algorithm}: distance, lateral and reverse bounds reject implausible recovery estimates");
            check(!policy.TryEstimate(route, C(118), 118, at, at.AddSeconds(16), M(0), M(4), out _) &&
                !policy.TryEstimate(route, C(0), 0, at, at.AddSeconds(16), M(0), M(4, epoch: 1), out _) &&
                !policy.TryEstimate(Road(algorithm), C(0), 0, at, at.AddSeconds(16), M(0), M(4), out _),
                $"{algorithm}: uncertainty cannot cross a turn, tracking epoch or trip identity");
            check(!Observe(20, 5, 40).Reliable && !Observe(22, 5, 44).Reliable && Observe(24, 5, 48).Reliable,
                $"{algorithm}: recovery from a long outage requires three spaced plausible reliable fixes");

            var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
            var matched = tracker.Update(C(12), 5, observeOnly: true);
            check(matched.IsAccepted && !tracker.Current.HasProgress,
                $"{algorithm}: heading recovery can inspect a match without moving geographic progress");
        }
        var continuity = new HeadingContinuityPolicy();
        var time = DateTimeOffset.UtcNow;
        check(continuity.Observe(116.79, -6, time, 1, 18, 0, 1) == HeadingContinuityPolicy.Verdict.Pending &&
            continuity.Observe(116.79, -7, time.AddSeconds(1), 1, 18, 0, 1) == HeadingContinuityPolicy.Verdict.Pending &&
            continuity.Observe(116.79, -6, time.AddSeconds(2), 1, 18, 0, 1) == HeadingContinuityPolicy.Verdict.Contradicted,
            "captured MLD heading contradiction requires spaced independent observations before trust is withheld");
        check(continuity.Observe(116.79, -6, time.AddSeconds(3), 1, 19, 0, 1) == HeadingContinuityPolicy.Verdict.Pending,
            "heading continuity evidence cannot cross tracking epochs");
        var healthy = new HeadingContinuityPolicy();
        healthy.Observe(10, 11, time, 1, 2, 0, 1); healthy.Observe(10, 10, time.AddSeconds(1), 1, 2, 0, 1);
        check(healthy.Observe(10, 9, time.AddSeconds(2), 1, 2, 0, 1) == HeadingContinuityPolicy.Verdict.SupportsRetained,
            "a healthy retained yaw can requalify after a pause without rotating it");

        var recovery = new HeadingRevalidationPolicy();
        var uncorroborated = new HeadingRevalidationPolicy();
        HeadingRevalidationDecision corrected = default, refused = default;
        for (int i = 0; i <= 3; i++)
        {
            var ar = MapToArCoordinates.Rotate(12 * i, 0, -6);
            corrected = recovery.Evaluate(116.79, C(12 * i), 5, 1.5, 90,
                RouteMatchConfidence.High, 90, (float)ar.X, (float)ar.Z, time.AddSeconds(8 * i),
                1, 0, 18, requiresValidation: true, independentYawDegrees: -6);
            refused = uncorroborated.Evaluate(116.79, C(12 * i), 5, 1.5, 90,
                RouteMatchConfidence.High, 90, (float)ar.X, (float)ar.Z, time.AddSeconds(8 * i),
                1, 0, 18, requiresValidation: true);
        }
        check(corrected.ShouldApplyCorrection && Math.Abs(corrected.AppliedCorrectionDegrees + 20) < .01 &&
            Math.Abs(corrected.CandidateYawDegrees + 6) < .1,
            "the captured severe heading contradiction can recover through repeated corroborated movement, in bounded steps");
        check(!refused.ShouldApplyCorrection && refused.Disposition == HeadingRevalidationDisposition.SignalsDisagree,
            "an uncorroborated opposite GPS/AR direction still cannot rotate the route");
    }
}
