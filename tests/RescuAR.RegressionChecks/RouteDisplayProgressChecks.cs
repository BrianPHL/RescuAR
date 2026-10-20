using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

internal static class RouteDisplayProgressChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        GeoCoordinate C(double east, double north = 0) => new(north / 111_195, east / 111_195);
        RouteResult Route(string algorithm)
        {
            var a = C(0); var b = C(100); var c = C(100, 100);
            return new(new[] { new RoutePoint(a, 0), new RoutePoint(b, a.DistanceTo(b)),
                new RoutePoint(c, a.DistanceTo(b) + b.DistanceTo(c)) }, a.DistanceTo(b) + b.DistanceTo(c), algorithm);
        }
        var previousGeneration = ARRenderGenerationBridge.Current;
        bool wasAccepting = ARRenderGenerationBridge.IsAcceptingFrames;
        ARRenderGenerationBridge.Activate(new(1, 1, 1));
        try
        {
            foreach (string algorithm in new[] { "MLD (Railway OSRM)", "AStar" })
            {
                var route = Route(algorithm);
                var tracker = new RouteProgressTracker();
                tracker.SetRoute(route);
                var physical = tracker.Update(C(37.5, 5.4), 11);
                var ahead = tracker.ApplyFusionCorrection(58.8, physical, "GPS_PDR_FIXTURE");
                check(ahead.GpsCoordinate.DistanceTo(ahead.SnappedCoordinate) > 20,
                    $"{algorithm}: the field fixture reproduces the false GPS-to-smoothed-progress offset");
                check(RouteDisplayProgressPolicy.TryResolveGps(route, ahead, out var display) &&
                    Math.Abs(display.ProgressMeters - 37.5) < 0.1 &&
                    display.RoadCoordinate.DistanceTo(ahead.GpsCoordinate) < 5.5 &&
                    tracker.Current.CommittedProgressMeters == 58.8,
                    $"{algorithm}: AR uses the physical match while trip distance retains its smoothed progress");
                var publisher = new MLDARIntegrationService(new UnusedRouter());
                check(publisher.PublishProgressWindow(route, display.ProgressMeters, display.RoadCoordinate,
                    0, 0, 0, arWindowMeters: 40, sourceSegmentIndex: display.SegmentIndex,
                    userCoordinate: display.AlignmentCoordinate) && ARRouteBridge.Current.IsAvailable,
                    $"{algorithm}: the reproduced field sample publishes a real mapped window instead of clearing guidance");
                var points = ARRouteBridge.Current.Points;
                check(Math.Abs(points[0].X) < 0.1 && Math.Abs(points[0].Z - 5.4) < 0.1,
                    $"{algorithm}: the road keeps the measured lateral offset instead of being centered on the camera");

                var policy = new RouteDisplayProgressPolicy();
                policy.ObserveGps(route, ahead, now);
                check(policy.TryResolvePdr(route, 59.5, now.AddSeconds(1), out var step) &&
                    Math.Abs(step.ProgressMeters - 38.2) < 0.1,
                    $"{algorithm}: a PDR step adds only its increment to the physical display anchor");
                check(Math.Abs(step.AlignmentCoordinate.DistanceTo(step.RoadCoordinate) - 5.4) < 0.1 &&
                    publisher.PublishProgressWindow(route, step.ProgressMeters, step.RoadCoordinate,
                        0, 0, 0, userCoordinate: step.AlignmentCoordinate),
                    $"{algorithm}: PDR retains the same GPS-to-road offset through its projection");
                check(!policy.TryResolvePdr(route, 59.5, now.AddSeconds(9), out _) &&
                    !policy.TryResolvePdr(route, 67.8, now.AddSeconds(1), out _),
                    $"{algorithm}: PDR still stops after eight seconds or eight metres without a trusted GPS anchor");
                check(!policy.TryResolvePdr(route, 58, now.AddSeconds(1), out _) &&
                    !policy.TryResolvePdr(Route(algorithm), 59, now.AddSeconds(1), out _),
                    $"{algorithm}: unexplained backward progress and a replacement route cannot reuse the display anchor");

                var corrected = tracker.ApplyFusionCorrection(55.8, physical, "GPS_PDR_CorrectBackward");
                policy.ObserveGps(route, corrected, now.AddSeconds(3));
                policy.ObserveGps(route, ahead with { RawProgressMeters = 120 }, now.AddSeconds(3));
                check(policy.TryResolvePdr(route, 56.5, now.AddSeconds(4), out step) &&
                    Math.Abs(step.ProgressMeters - 38.2) < 0.1,
                    $"{algorithm}: a fresh fusion correction reanchors steps and a duplicate GPS fix cannot move it again");
                policy.Reset();
                check(!policy.TryResolvePdr(route, 59, now.AddSeconds(1), out _),
                    $"{algorithm}: a new trip requires its own physical display anchor");

                var beforeBend = tracker.Update(C(80, 3), 7);
                var afterBendProgress = tracker.ApplyFusionCorrection(130, beforeBend, "GPS_PDR_FIXTURE");
                check(RouteDisplayProgressPolicy.TryResolveGps(route, afterBendProgress, out display) &&
                    display.SegmentIndex == 0 && afterBendProgress.SegmentIndex == 1,
                    $"{algorithm}: the displayed road tangent follows the actual position before a corner");
                check(!RouteDisplayProgressPolicy.TryResolveGps(route, ahead with { AccuracyMeters = 30 }, out _) &&
                    !RouteDisplayProgressPolicy.TryResolveGps(route, ahead with { MatchConfidence = RouteMatchConfidence.Low }, out _) &&
                    !RouteDisplayProgressPolicy.TryResolveGps(route, ahead with { IsOffRoute = true }, out _) &&
                    !RouteDisplayProgressPolicy.TryResolveGps(route, ahead with { GpsCoordinate = C(37.5, 25) }, out _),
                    $"{algorithm}: display placement preserves precision, identity, off-route, and twenty-metre offset limits");
            }

            var heading = new HeadingRevalidationPolicy();
            heading.Evaluate(0, C(0), 7, 1, 90, RouteMatchConfidence.High, 90, 0, 0, now);
            var jump = heading.Evaluate(0, C(100), 7, 1, 90, RouteMatchConfidence.High, 90, 5, 0, now.AddSeconds(10));
            check(jump.Disposition == HeadingRevalidationDisposition.SignalsDisagree &&
                !jump.ShouldApplyCorrection && jump.ConfirmationCount == 0,
                "a hundred-metre GPS jump with five metres of AR motion cannot recalibrate or rotate the road data");
            check(!RouteDisplayProgressPolicy.TryAtDistance(Route("AStar"), double.NaN, out _) &&
                !RouteDisplayProgressPolicy.TryAtDistance(Route("AStar"), -10, out _),
                "invalid physical display positions cannot be projected");
        }
        finally
        {
            ARRouteBridge.Clear();
            if (wasAccepting) ARRenderGenerationBridge.Activate(previousGeneration);
            else ARRenderGenerationBridge.Invalidate();
        }
    }

    private sealed class UnusedRouter : IRoutingService
    {
        public string AlgorithmName => "unit fixture";
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No route request is needed for projection checks.");
    }
}
