using RescuAR.MAUI.Services.Location;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

internal static class CameraMapChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
        var fix = new LocationReading(C(0, 0), 10, null, null, null, now);
        check(CameraMapPresentationPolicy.CanShowCurrent(fix, now, false), "Camera map accepts a current GPS fix under dev planning accuracy rules");
        check(!CameraMapPresentationPolicy.CanShowCurrent(null, now, false), "Camera map cannot invent a user location when no fix exists");
        check(!CameraMapPresentationPolicy.CanShowCurrent(fix, now, true), "dev's held position cannot become a live Camera map pin");
        check(!CameraMapPresentationPolicy.CanShowCurrent(fix with { Timestamp = now.AddSeconds(-6) }, now, false), "an old fix cannot remain Current Location or supply nearby distances");
        check(!CameraMapPresentationPolicy.CanShowCurrent(fix with { Timestamp = now.AddSeconds(3) }, now, false), "future-dated GPS outside dev's clock tolerance remains unavailable");
        check(!CameraMapPresentationPolicy.CanShowCurrent(fix with { AccuracyMeters = null }, now, false), "unknown GPS accuracy cannot supply a current Camera map pin");
        check(!CameraMapPresentationPolicy.CanShowCurrent(fix with { AccuracyMeters = 31 }, now, false), "Camera display does not loosen dev's 30m planning accuracy limit");
        check(!CameraMapPresentationPolicy.CanShowCurrent(fix with { Coordinate = new(double.NaN, 121) }, now, false), "invalid coordinates cannot produce a Camera map pin");
        check(!CameraMapPresentationPolicy.CanProject(new(89, 121)), "unprojectable polar coordinates cannot create infinite map geometry");
        check(CameraMapPresentationPolicy.DistanceTo(C(100, 0), fix, now).Contains("straight-line"), "nearby shelter distance is explicitly a straight-line estimate");
        check(CameraMapPresentationPolicy.DistanceTo(C(100, 0), null, now) == "Distance unknown", "preset browsing centers are never substituted for missing nearby-distance origins");
        check(CameraMapPresentationPolicy.DistanceTo(C(100, 0), fix, now.AddSeconds(6)) == "Distance unknown", "nearby-distance estimates expire with the same current-fix rule as user pins");
        check(CameraMapPresentationPolicy.DirectoryStatus(null).Contains("unknown"), "missing directory status remains current availability unknown");
        check(CameraMapPresentationPolicy.DirectoryStatus("Open").Contains("Open") && CameraMapPresentationPolicy.DirectoryStatus("Open").Contains("unverified"),
            "existing directory status is retained without claiming verified live availability");
        check(CameraMapPresentationPolicy.WalkEstimate(160).Contains("~2 min") && CameraMapPresentationPolicy.WalkEstimate(160).Contains("pace/conditions vary"),
            "walking time is presented as an estimate with its practical limitation");
        check(CameraMapPresentationPolicy.WalkEstimate(double.NaN).Contains("unknown"), "missing route length cannot yield a fabricated walking time");

        var route = Route(C(0, 0), C(50, 0), C(50, -80));
        string geometry = route.GeometryFingerprint;
        check(ReferenceEquals(CameraMapPresentationPolicy.ForTrip(route, 7, 7, true), route), "AR and Camera map consume the exact accepted route rather than a recomputed route");
        check(CameraMapPresentationPolicy.ForTrip(route, 7, 8, true) is null, "an old destination's route cannot be drawn for a new destination");
        check(CameraMapPresentationPolicy.ForTrip(route, 7, 7, false) is null, "ending navigation removes the trip route from the Camera map");
        check(CameraMapPresentationPolicy.ForTrip(null, 7, 7, true) is null, "a pending or failed route request is not displayed as a successful route");
        var invalid = new RouteResult(new[] { new RoutePoint(C(0, 0), 0), new RoutePoint(new(double.NaN, 121), 5), new RoutePoint(C(20, 0), 20) }, 20, "MLD");
        check(CameraMapPresentationPolicy.ForTrip(invalid, 7, 7, true) is null, "an invalid interior vertex hides the entire route instead of drawing a shortcut across it");
        invalid = new(new[] { new RoutePoint(C(0, 0), 0), new RoutePoint(C(20, 0), 20), new RoutePoint(C(30, 0), 10) }, 30, "MLD");
        check(CameraMapPresentationPolicy.ForTrip(invalid, 7, 7, true) is null, "non-monotonic route geometry cannot be presented as an accepted mapped corridor");
        invalid = new(route.Points, double.NaN, "MLD");
        check(CameraMapPresentationPolicy.ForTrip(invalid, 7, 7, true) is null, "nonfinite route length cannot create route stats or geometry");

        var viewport = new CameraMapViewportPolicy();
        check(viewport.ShouldFrameRoute(7, route), "a newly accepted route can be framed once");
        check(!viewport.ShouldFrameRoute(7, route), "ordinary GPS and UI refreshes cannot reframe a browsed route");
        check(!viewport.ShouldFrameRoute(7, Route(C(0, 0), C(50, 0), C(50, -80))), "an identical geometry replacement preserves pan and zoom");
        check(viewport.ShouldFrameRoute(7, Route(C(0, 0), C(70, 0), C(70, -80))), "a genuinely changed accepted route can receive its own initial frame");
        check(viewport.ShouldFrameRoute(8, route), "a new destination version may frame even if it shares previous route geometry");

        var progress = new RouteProgressTracker.ProgressSnapshot(true, true, false, 0, 35, 35, route.TotalDistanceMeters - 35, 0, 10, C(35, 0), C(35, 0));
        var guidance = CameraMapPresentationPolicy.Guidance(route, progress, false);
        check(guidance.IsAvailable && guidance.Instruction == PedestrianTurnGuidanceService.TurnInstruction.Right && guidance.DistanceToTurnMeters is > 0 and < 25,
            "Camera map turns use dev's classifier at accepted progress rather than nearest-vertex shortcuts");
        check(guidance.RemainingRouteMeters == progress.RemainingMeters, "Camera map remaining distance comes from accepted tracker progress");
        check(!CameraMapPresentationPolicy.Guidance(route, progress, true).IsAvailable, "held navigation keeps turn distances unavailable instead of advancing them");
        check(!CameraMapPresentationPolicy.Guidance(route, progress with { IsOffRoute = true }, false).IsAvailable, "off-route progress cannot provide an ordinary mapped turn instruction");
        check(!CameraMapPresentationPolicy.Guidance(route, progress with { HasProgress = false }, false).IsAvailable, "unaccepted progress cannot produce a turn instruction");
        progress = progress with { SegmentIndex = 1, CommittedProgressMeters = route.TotalDistanceMeters, RemainingMeters = 0 };
        guidance = CameraMapPresentationPolicy.Guidance(route, progress, false);
        check(guidance.Instruction == PedestrianTurnGuidanceService.TurnInstruction.Arrive && RouteDirectionsService.Text(guidance.Instruction) == "Destination ahead",
            "being near the route end says destination ahead and cannot claim confirmed arrival");
        check(route.GeometryFingerprint == geometry && route.Points.Count == 3, "map presentation leaves dev's route geometry and identity unchanged");

        var directions = new RouteDirectionsService();
        var steps = directions.Build(route, "Selected center");
        check(steps.Count >= 3 && steps.Any(step => step.Instruction == PedestrianTurnGuidanceService.TurnInstruction.Right), "route overview includes meaningful turns from dev's shared classifier");
        check(steps[0].AtProgressMeters == 0 && steps[^1].AtProgressMeters == route.Points[^1].DistanceFromStartMeters, "overview steps retain canonical route-start and route-end distances");
        check(steps[^1].InstructionText.Contains("Selected center") && steps[^1].SubtitleText.Contains("arrival requires location confirmation"),
            "the final overview step preserves the selected destination and existing arrival-confirmation requirement");
        check(steps.All(step => !step.InstructionText.Contains("Arrived", StringComparison.OrdinalIgnoreCase)), "an overview cannot announce that the user has already arrived");
        check(directions.Build(invalid, "Unavailable").Count == 0, "invalid route metadata cannot produce an apparently valid directions sheet");
        var repeated = Route(C(0, 0), C(0, 0), C(50, 0), C(50, 0), C(50, -80));
        check(directions.Build(repeated, "Selected center").Count(step => RouteDirectionsService.IsTurn(step.Instruction)) == steps.Count(step => RouteDirectionsService.IsTurn(step.Instruction)),
            "repeated polyline vertices do not invent extra route-overview turns");
    }

    private static GeoCoordinate C(double east, double north) => new(14.6507 + north / 111195, 121.1029 + east / (111195 * Math.Cos(14.6507 * Math.PI / 180)));
    private static RouteResult Route(params GeoCoordinate[] coordinates)
    {
        double distance = 0; var points = new List<RoutePoint>();
        for (int index = 0; index < coordinates.Length; index++)
        {
            if (index > 0) distance += coordinates[index - 1].DistanceTo(coordinates[index]);
            points.Add(new(coordinates[index], distance));
        }
        return new(points, distance, "MLD");
    }
}
