using System.Numerics;
using RescuAR.AR;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

internal static class ARFieldValidationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var pin = new GeoCoordinate(14.697875, 121.078949);
        GeoCoordinate C(double east, double north = 0) => new(
            pin.Latitude + north / 111_195,
            pin.Longitude + east / (111_195 * Math.Cos(pin.Latitude * Math.PI / 180)));
        var now = DateTimeOffset.UtcNow;
        var arrival = new SafeZoneConfirmationService();
        SafeZoneConfirmationService.SafeZoneDecision Observe(int seconds, double distance = 23,
            double accuracy = 20.5, bool barrier = false, long trip = 1) =>
            arrival.Evaluate(C(distance), pin, 30, accuracy, 14,
                now.AddSeconds(seconds), barrier, now.AddSeconds(seconds), trip);
        check(Observe(0).IsCandidate && !Observe(0).IsConfirmed,
            "field arrival at 23 m with 20.5 m accuracy starts evidence without counting a duplicate");
        check(!Observe(1).IsConfirmed && Observe(3).IsConfirmed,
            "two nearby observations over three seconds confirm vicinity without the old accuracy-circle exclusion");
        check(!Observe(4, trip: 2).IsConfirmed && arrival.Current.ConfirmationCount == 1,
            "a new trip to the same pin still requires its own arrival observations");
        arrival.Reset();
        Observe(0);
        check(!Observe(3, barrier: true).IsCandidate && arrival.Current.ConfirmationCount == 0,
            "a mapped major-road barrier still cancels automatic arrival");
        arrival.Reset();
        check(!Observe(0, accuracy: 30).IsCandidate &&
            !Observe(3, distance: 50, accuracy: 5).IsConfirmed,
            "poor fixes and a position beyond the facility radius cannot confirm arrival");
        arrival.Reset();
        check(!arrival.Evaluate(C(60), pin, 150, 5, 0, now,
            evaluatedAt: now).IsCandidate,
            "a broad facility profile cannot check in a user sixty metres from the pin");

        RouteResult Route(params GeoCoordinate[] coordinates)
        {
            double distance = 0;
            var points = new List<RoutePoint>();
            for (int i = 0; i < coordinates.Length; i++)
            {
                if (i > 0) distance += coordinates[i - 1].DistanceTo(coordinates[i]);
                points.Add(new RoutePoint(coordinates[i], distance));
            }
            return new RouteResult(points, distance, "field fixture");
        }
        var tracker = new RouteProgressTracker();
        tracker.SetRoute(Route(Enumerable.Range(0, 21).Select(i => C(i)).ToArray()));
        var denseMatch = tracker.Update(C(10.5), 6);
        check(denseMatch.IsAccepted && denseMatch.MatchConfidence == RouteMatchConfidence.High,
            "dense consecutive GeoJSON vertices on one straight road do not create false route ambiguity");
        var fusion = new GpsPdrFusionPolicy();
        fusion.EvaluateGps(0, false, denseMatch with { MatchConfidence = RouteMatchConfidence.Low });
        fusion.EvaluateGps(0, false, denseMatch);
        fusion.EvaluateGps(0, false, denseMatch);
        check(!fusion.IsRouteIdentitySuspended,
            "repeated accurate matches recover route identity on the connected road");
        var weakSameRoad = denseMatch with { AccuracyMeters = 43.1,
            MatchConfidence = RouteMatchConfidence.Low, CrossTrackErrorMeters = 2 };
        fusion.EvaluateGps(10, true, denseMatch, now);
        var heldPrecision = fusion.EvaluateGps(10, true, weakSameRoad, now.AddSeconds(2));
        check(!fusion.IsRouteIdentitySuspended && heldPrecision.TargetProgressMeters == 10,
            "a weak but unambiguous same-road fix holds progress without discarding resolved identity");
        fusion.Reset();
        fusion.EvaluateGps(0, false, denseMatch with { MatchConfidence = RouteMatchConfidence.Low }, now);
        fusion.EvaluateGps(0, false, denseMatch, now.AddSeconds(1));
        fusion.EvaluateGps(0, false, weakSameRoad, now.AddSeconds(2));
        fusion.EvaluateGps(0, false, denseMatch, now.AddSeconds(4));
        check(!fusion.IsRouteIdentitySuspended,
            "two good corridor observations can recover across one brief poor-precision fix");
        fusion.Reset();
        fusion.EvaluateGps(0, false, denseMatch with { MatchConfidence = RouteMatchConfidence.Low }, now);
        fusion.EvaluateGps(0, false, denseMatch, now.AddSeconds(1));
        fusion.EvaluateGps(0, false, denseMatch, now.AddSeconds(1));
        check(fusion.IsRouteIdentitySuspended,
            "re-reading one GPS observation cannot complete route identity recovery");
        fusion.EvaluateGps(0, false, weakSameRoad, now.AddSeconds(13));
        fusion.EvaluateGps(0, false, denseMatch, now.AddSeconds(14));
        check(fusion.IsRouteIdentitySuspended,
            "old identity observations expire during a prolonged GPS precision outage");
        tracker.SetRoute(Route(C(0), C(20), C(20, 3), C(0, 3)));
        check(tracker.Update(C(10, 1.5), 6).MatchConfidence == RouteMatchConfidence.Low,
            "a nearby reverse parallel branch remains a genuinely ambiguous route match");

        var heading = new HeadingRevalidationPolicy();
        heading.Evaluate(0, C(0), 5, 0.1, 0, RouteMatchConfidence.High, 270, 0, 0, now);
        var walking = heading.Evaluate(0, C(-12), 5, 0.1, 0,
            RouteMatchConfidence.High, 270, -12, 0, now.AddSeconds(8));
        check(walking.Disposition == HeadingRevalidationDisposition.AlignmentStable,
            "a placeholder zero GPS course at low provider speed cannot veto a consistent walking heading");
        heading.Reset();
        heading.Evaluate(0, C(0), 15, 1, 90, RouteMatchConfidence.High, 90, 0, 0, now);
        check(heading.Evaluate(0, C(8), 15, 1, 90, RouteMatchConfidence.High, 90,
            4, 0, now.AddSeconds(5)).Disposition == HeadingRevalidationDisposition.CollectingMovement,
            "movement shorter than GPS uncertainty cannot recalibrate the road projection");
        heading.Reset();
        heading.Evaluate(20, C(0), 5, 1.5, 90, RouteMatchConfidence.High, 90, 0, 0, now);
        HeadingRevalidationDecision correction = default;
        for (int i = 1; i <= 3; i++)
            correction = heading.Evaluate(20, C(12 * i), 5, 1.5, 90,
                RouteMatchConfidence.High, 90, 12 * i, 0, now.AddSeconds(8 * i));
        check(correction.ShouldApplyCorrection && correction.AppliedCorrectionDegrees < 0 &&
            Math.Abs(correction.AppliedCorrectionDegrees) <= 8,
            "three consistent walking windows still produce a bounded road alignment correction");

        check(DestinationBearingCuePolicy.TryGetAngle(pin, C(0, 156), 0,
            Quaternion.Identity, out double north) && Math.Abs(north) < 0.01,
            "destination direction remains available beyond the short road-approach radius");
        check(DestinationBearingCuePolicy.TryGetAngle(pin, C(156), 0,
            Quaternion.Identity, out double east) && Math.Abs(east - 90) < 0.01,
            "a destination to the east points right in a north-facing camera");
        check(!DestinationBearingCuePolicy.TryGetAngle(pin, C(156), 0, default, out _) &&
            !DestinationBearingCuePolicy.TryGetAngle(pin, C(1), 0, Quaternion.Identity, out _),
            "invalid camera rotation and an indeterminate near-pin bearing are withheld");

        foreach (int api in new[] { 34, 36 })
        {
            var field = ARDepthCompatibilityPolicy.Evaluate("SM-A546E", api, true);
            check(!field.DisableDepth && field.Mode == "DEPTH_FIELD_REVALIDATION",
                $"the API {api} field candidate restores supported Depth for realistic flood rendering");
            check(ARDepthCompatibilityPolicy.Evaluate("SM-A546E", api, false).DisableDepth,
                $"the API {api} production native-crash quarantine remains explicit pending phone validation");
        }
        check(ARDepthCompatibilityPolicy.Evaluate("SM-A546E", 34, true, forceOff: true).DisableDepth,
            "an explicit Depth-off diagnostic experiment is still respected");
        check(!ARDepthCompatibilityPolicy.Evaluate("other", 34, false).DisableDepth,
            "ordinary supported device profiles continue to request automatic Depth");
    }
}
