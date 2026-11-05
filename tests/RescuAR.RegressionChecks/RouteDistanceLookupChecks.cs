using System.Collections;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

internal static class RouteDistanceLookupChecks
{
    private static GeoCoordinate C(double east) => new(0, east / 6371008.8 * 180 / Math.PI);

    public static void Run(Action<bool, string> check)
    {
        foreach (string provider in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            var duplicateRoute = new RouteResult(new[]
            {
                new RoutePoint(C(0), 0), new RoutePoint(C(0), 0),
                new RoutePoint(C(5), 5), new RoutePoint(C(5), 5),
                new RoutePoint(C(10), 10), new RoutePoint(C(10), 10)
            }, 10, provider);
            check(RouteDisplayProgressPolicy.TryAtDistance(duplicateRoute, 0, out var beginning) &&
                beginning.SegmentIndex == 1 && beginning.RoadCoordinate == C(0),
                $"{provider}: leading zero-length points do not replace the first usable display segment");
            check(RouteDisplayProgressPolicy.TryAtDistance(duplicateRoute, 5, out var junction) &&
                junction.SegmentIndex == 1 && junction.RoadCoordinate == C(5) &&
                RouteDisplayProgressPolicy.TryAtDistance(duplicateRoute, 5.25, out var after) &&
                after.SegmentIndex == 3 && after.RoadCoordinate.DistanceTo(C(5.25)) < 1e-8,
                $"{provider}: exact vertices retain the incoming segment while movement passes duplicate distances");
            check(RouteDisplayProgressPolicy.TryAtDistance(duplicateRoute, 10.005, out var terminal) &&
                terminal.ProgressMeters == 10 && terminal.SegmentIndex == 3 &&
                !RouteDisplayProgressPolicy.TryAtDistance(duplicateRoute, 10.02, out _),
                $"{provider}: terminal distance tolerance and trailing duplicate ownership remain bounded");
            var clipped = LocalRouteProjector.ProjectWindow(duplicateRoute, 2.5, C(2.5), 5);
            check(clipped.Count == 4 && Math.Abs(clipped[0].EastMeters) < 1e-8 &&
                Math.Abs(clipped[^1].EastMeters - 5) < 1e-8 &&
                clipped[1].Coordinate == C(5) && clipped[2].Coordinate == C(5),
                $"{provider}: local windows retain mapped duplicate vertices and interpolate both clipped boundaries");
            check(LocalRouteProjector.ProjectWindow(duplicateRoute, 10, C(10), 5).Count == 1,
                $"{provider}: a terminal window remains a single endpoint");

            var tinyRoute = new RouteResult(new[] { new RoutePoint(C(0), 0),
                new RoutePoint(C(.00005), .00005), new RoutePoint(C(10), 10) }, 10, provider);
            check(RouteDisplayProgressPolicy.TryAtDistance(tinyRoute, .000025, out var tiny) &&
                tiny.SegmentIndex == 1 && tiny.RoadCoordinate == C(.00005),
                $"{provider}: sub-tolerance segments retain the established display skip policy");

            DenseRoute(check, provider);
        }
    }

    private static void DenseRoute(Action<bool, string> check, string provider)
    {
        var points = new CountingPoints(Enumerable.Range(0, 20_001)
            .Select(i => new RoutePoint(C(i), i)).ToArray());
        var route = new RouteResult(points, 20_000, provider);
        const double progress = 19_000.25;
        points.Reads = 0;
        var window = LocalRouteProjector.ProjectWindow(route, progress, C(progress), 7.5);
        check(window.Count == 9 && Math.Abs(window[0].EastMeters) < 1e-7 &&
            Math.Abs(window[^1].EastMeters - 7.5) < 1e-7 && points.Reads < 100,
            $"{provider}: a late 7.5-metre window reads fewer than 100 of 20,001 points and retains metre scale");
        points.Reads = 0;
        check(RouteDisplayProgressPolicy.TryAtDistance(route, progress, out var display) &&
            display.SegmentIndex == 19_000 && display.RoadCoordinate.DistanceTo(C(progress)) < 1e-7 &&
            points.Reads < 40,
            $"{provider}: late display interpolation uses logarithmic lookup without changing segment identity");

        var fix = new RouteProgressTracker.RouteProgressUpdate(true, false, true, 19_000,
            progress, progress, route.TotalDistanceMeters - progress, 0, 5, C(progress), C(progress),
            string.Empty, 15, RouteMatchConfidence.High, double.NaN);
        var tracker = new RouteProgressTracker();
        tracker.SetRoute(route);
        points.Reads = 0;
        var correction = tracker.ApplyFusionCorrection(progress + .5, fix, "DISTANCE_LOOKUP_CHECK");
        check(correction.SegmentIndex == 19_000 &&
            correction.SnappedCoordinate.DistanceTo(C(progress + .5)) < 1e-7 && points.Reads < 80,
            $"{provider}: GPS/PDR correction locates its late segment and coordinate without full-route scans");

        var policy = new RouteDisplayProgressPolicy();
        var at = DateTimeOffset.UtcNow;
        var acquisition = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 0, 1, 1, 1, 1);
        policy.ObserveGps(route, fix, at);
        policy.ObserveRegistration(route, fix, at, at, acquisition);
        points.Reads = 0;
        check(policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(1),
            acquisition with { X = 1 }, out var moved) &&
            Math.Abs(moved.ProgressMeters - progress - 1) < .01 && points.Reads < 160,
            $"{provider}: tracked placement searches only its existing sixteen-metre progress interval");
        check(policy.TryGetRegisteredDisplayPosition(route, at.AddSeconds(2),
            acquisition with { X = 9 }, out moved) && Math.Abs(moved.ProgressMeters - progress - 9) < .01,
            $"{provider}: the inclusive eight-metre tracked boundary remains eligible");
    }

    private sealed class CountingPoints(RoutePoint[] points) : IReadOnlyList<RoutePoint>
    {
        public int Reads { get; set; }
        public int Count => points.Length;
        public RoutePoint this[int index] { get { Reads++; return points[index]; } }
        public IEnumerator<RoutePoint> GetEnumerator() => ((IEnumerable<RoutePoint>)points).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
