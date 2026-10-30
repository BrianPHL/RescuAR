using System.Numerics;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

internal static class PdrMapMatchingChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(
        14.64 + north / 6371008.8 * 180 / Math.PI,
        121.095 + east / (6371008.8 * Math.Cos(14.64 * Math.PI / 180)) * 180 / Math.PI);

    public static void Run(Action<bool, string> check)
    {
        var at = DateTimeOffset.UtcNow;
        foreach (double age in new[] { 4.9, 5.1, 6.2, 8.0 })
            check(MappedRoutePreviewPolicy.CanDisplay(true, false, RouteMatchConfidence.High, 9,
                C(0, 3), C(0), at, at.AddSeconds(age), true, true, false, true),
                $"shared preview: qualified geometry remains displayed at GPS age {age}s");
        check(!MappedRoutePreviewPolicy.CanDisplay(true, false, RouteMatchConfidence.High, 9,
            C(0), C(0), at, at.AddSeconds(5.1), true, true, false, false) &&
            !MappedRoutePreviewPolicy.CanDisplay(true, false, RouteMatchConfidence.High, 9,
            C(0), C(0), at, at.AddSeconds(8.01), true, true, false, true),
            "shared preview: changed owner/frame or expired support cannot bridge the polling gap");
        foreach (int failure in Enumerable.Range(0, 5))
            check(!MappedRoutePreviewPolicy.CanDisplay(failure != 0, failure == 1,
                failure == 2 ? RouteMatchConfidence.Low : RouteMatchConfidence.High,
                failure == 3 ? 30 : 9, C(0), C(0), at, at.AddSeconds(6), failure != 4, true, false, true),
                $"shared preview: retention cannot bypass acceptance/deviation/matching/accuracy/access ({failure})");

        var heading = new PdrHeadingSmoother();
        heading.Evaluate(1000, 0, 0, 90, 90);
        heading.Evaluate(1600, .7, 0, 0, 90);
        var walking = heading.Evaluate(2200, 1.4, 0, 270, 90);
        check(walking.UsedWalkingVector && walking.HeadingErrorDegrees < .01 &&
            new GpsPdrFusionPolicy().EvaluatePdrHeading(walking.HeadingErrorDegrees).IsAccepted,
            "PDR: route-consistent walking remains accepted with the phone sideways or backward");
        heading.Reset(); heading.Evaluate(1000, 0, 0, 0, 90);
        check(!heading.Evaluate(1600, 0, 0, 90, 90).UsedWalkingVector,
            "PDR: turning a stationary phone does not establish walking displacement");

        foreach (string algorithm in new[] { "MLD (Railway OSRM)", "AStar" })
        {
            CapturedRegistration(check, algorithm, at);
            var loop = new RouteResult(new[] { new RoutePoint(C(0), 0), new RoutePoint(C(100), 100),
                new RoutePoint(C(100, 2), 102), new RoutePoint(C(0, 2), 202) }, 202, algorithm);
            var tracker = new RouteProgressTracker(); tracker.SetRoute(loop);
            // A reliable walking course establishes the outgoing leg; GPS alone
            // correctly withholds ambiguous identity between these parallel legs.
            var policy = new RouteDisplayProgressPolicy(); var fix = tracker.Update(C(10), 7, 90, 1.2);
            var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 0, 1, 1, 1);
            policy.ObserveGps(loop, fix, at); policy.ObserveRegistration(loop, fix, at, at, motion);
            check(policy.TryGetRegisteredDisplayPosition(loop, at.AddSeconds(1), motion with { Z = -1.9f }, out var matched) &&
                matched.SegmentIndex == 0 && Math.Abs(matched.ProgressMeters - 10) < .02,
                $"{algorithm}: a nearby return leg cannot steal the current map-matched window");
            policy.Reset();
            check(!policy.TryGetRegisteredDisplayPosition(loop, at, motion, out _),
                $"{algorithm}: stopping discards filtered registration and matching continuity");
        }
        var filter = new GpsRegistrationCorrectionPolicy();
        filter.Observe(new(-9, 10), 11, 8, at, out _);
        filter.Observe(new(9, -10), 11, 8, at.AddSeconds(5), out _);
        check(!filter.Observe(new(0, 12), 11, 8, at.AddSeconds(10), out _),
            "GPS registration: contradictory residuals cannot translate a stationary route");
        filter.Reset(); filter.Observe(new(0, 10), 11, 8, at, out _);
        filter.Observe(new(0, 10), 11, 8, at, out _);
        check(!filter.Observe(new(0, 10), 11, 8, at.AddSeconds(10), out _),
            "GPS registration: duplicate timestamps cannot manufacture three confirmations");
        foreach (double cadence in new[] { .2, 1, 2, 5 })
        {
            filter.Reset();
            bool corrected = false;
            for (int i = 0; i <= (int)Math.Ceiling(10 / cadence); i++)
            {
                double seconds = i * cadence;
                if (!filter.Observe(new(0, 6), 12, 7, at.AddSeconds(seconds), out var correction)) continue;
                check(seconds >= 8 && Math.Abs(correction.Length() - 2) < .001,
                    $"GPS registration: {cadence}s readings require sustained evidence and retain the 2m correction limit");
                corrected = true;
            }
            check(corrected, $"GPS registration: {cadence}s update cadence cannot starve origin correction");
        }
        filter.Reset();
        filter.Observe(new(0, 6), 12, 7, at, out _);
        filter.Observe(new(0, 6), 12, 7, at.AddSeconds(2), out _);
        check(!filter.Observe(new(0, 6), 12, 7, at.AddSeconds(33), out _),
            "GPS registration: a long observation gap cannot reuse expired correction evidence");
        filter.Reset();
        foreach (int seconds in Enumerable.Range(0, 12))
            check(!filter.Observe(new(0, 6), 7, 7, at.AddSeconds(seconds), out _),
                "GPS registration: repeated fixes without improved accuracy cannot move the origin");
    }

    private static void CapturedRegistration(Action<bool, string> check, string algorithm, DateTimeOffset at)
    {
        // Real EN/GPS/pose observations from the 18:58 capture. This is a replay,
        // not surveyed physical-road accuracy evidence.
        var en = new[] { C(1.590,17.321), C(-91.576,25.883), C(-107.929,27.440),
            C(-191.305,35.113), C(-232.187,38.893), C(-261.019,41.562) };
        var metres = new[] { 0.0, 93.559, 109.986, 193.714, 234.770, 263.725 };
        var route = new RouteResult(en.Select((c, i) => new RoutePoint(c, metres[i])).ToArray(), metres[^1], algorithm);
        var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
        var policy = new RouteDisplayProgressPolicy();
        var pose = new RouteBacktrackingPolicy.TrackedMotion(-3.491f,18.749f,10.841,1,15,19);
        var first = tracker.Update(C(0), 11);
        policy.ObserveGps(route, first, at); policy.ObserveRegistration(route, first, at, at, pose);
        policy.TryGetRegisteredReference(route, en[0], at, pose, out var original);
        double Gap(RouteDisplayProgressPolicy.RegisteredReference value) => Math.Sqrt(
            Math.Pow(value.WorldX - pose.X, 2) + Math.Pow(value.WorldZ - pose.Z, 2));
        check(Math.Abs(Gap(original) - 17.394) < .03, $"{algorithm}: captured initial 17.4m placement is reproduced");
        var samples = new[] {
            (5.011,-6.952,10.532,11.5,-3.633f,18.742f), (10.008,2.363,12.387,8.0,-3.195f,18.563f),
            (15.000,5.474,13.109,8.0,-2.834f,18.631f), (21.015,4.960,23.445,9.0,-3.239f,18.562f),
            (27.020,-.478,12.172,8.0,-3.631f,18.821f), (31.994,2.317,12.829,9.0,-3.504f,18.506f),
            (37.018,6.267,7.610,9.5,-2.756f,17.627f), (43.024,3.012,9.497,7.5,-3.322f,18.423f) };
        int corrections = 0;
        foreach (var (seconds,east,north,accuracy,x,z) in samples)
        {
            var reading = tracker.Update(C(east,north), accuracy);
            var held = tracker.ApplyFusionCorrection(0, reading, "GPS_PDR_HoldStationary");
            var time = at.AddSeconds(seconds);
            policy.ObserveGps(route, held, time);
            var decision = policy.ObserveRegistration(route, held, time, time, pose with { X=x, Z=z });
            if (decision.State == "GPS_FILTERED_BOUNDED_CORRECTION")
            {
                corrections++;
                check(decision.Changed && Math.Sqrt(decision.CorrectionX*decision.CorrectionX+
                    decision.CorrectionZ*decision.CorrectionZ) <= 2.001,
                    $"{algorithm}: captured repeated evidence corrects at most 2m per qualified group");
            }
        }
        check(corrections > 0 && policy.RegistrationAccuracyMeters == 11 &&
            policy.TryGetRegisteredReference(route, en[0], at.AddSeconds(43.024), pose, out var filtered) &&
            Gap(filtered) < Gap(original) - 1 && filtered.YawDegrees == original.YawDegrees &&
            tracker.Current.CommittedProgressMeters == 0,
            $"{algorithm}: captured seed recovers without walking, rotating the route or inventing progress/precision");
    }
}
