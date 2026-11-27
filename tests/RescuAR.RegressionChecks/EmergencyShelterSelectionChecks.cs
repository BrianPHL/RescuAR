using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.State;

internal static class EmergencyShelterSelectionChecks
{
    private static GeoCoordinate Point(double northMeters) => new(14 + northMeters / 111_195, 121);

    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-11-27T09:00:00Z");
        check(EmergencyShelterSelection.HasCurrentDirectory(true, now.AddSeconds(-30), now) &&
            !EmergencyShelterSelection.HasCurrentDirectory(false, now.AddSeconds(-1), now) &&
            !EmergencyShelterSelection.HasCurrentDirectory(true, now.AddMinutes(-3), now) &&
            !EmergencyShelterSelection.HasCurrentDirectory(true, now.AddSeconds(1), now),
            "emergency selection requires a recent live directory");

        var open = new EmergencyShelterSelection.Shelter("eligible", "Open shelter",
            Point(100), "Open", 30, 20);
        var farther = open with
            { Id = "far", Name = "Farther shelter", Coordinate = Point(130), Status = "operational", Occupancy = 0 };
        var closerFull = open with { Id = "full", Coordinate = Point(50), Occupancy = 30 };
        var closerStandby = open with { Id = "standby", Coordinate = Point(70), Status = "Standby" };
        var unknown = open with { Id = "unknown", Coordinate = Point(60), Capacity = 0, Occupancy = 0 };
        var unreportedOccupancy = open with { Id = "unreported", Coordinate = Point(65), Occupancy = null };
        var invalidOccupancy = open with { Id = "invalid", Coordinate = Point(68), Occupancy = -1 };
        var unmapped = open with { Id = "unmapped", Coordinate = default };
        var listedFull = open with { Id = "listed-full", Coordinate = Point(80), Status = "Full" };
        var candidates = EmergencyShelterSelection.NearbyEligible(
            [farther, closerFull, unknown, unmapped, closerStandby, open, listedFull, unreportedOccupancy, invalidOccupancy],
            Point(0)).ToArray();
        check(candidates.Select(candidate => candidate.Id).SequenceEqual(["eligible", "far"]),
            "emergency selection ranks open mapped shelters by distance when valid occupancy confirms spare capacity");

        RouteResult Route(double start, double end) => new(
            [new(Point(start), 0), new(Point(end), end - start)], end - start, "fixture");
        RouteHazard Hazard(double center) => new("hazard", Point(center), 2, "Flood", "High",
            "Fixture hazard", "Fixture", now);
        check(EmergencyShelterSelection.IsRouteUsable(Route(1, 90), Point(0), open.Coordinate, []),
            "emergency selection accepts a connected route with bounded endpoints");
        check(!EmergencyShelterSelection.IsRouteUsable(null, Point(0), open.Coordinate, []) &&
            !EmergencyShelterSelection.IsRouteUsable(Route(55, 100), Point(0), open.Coordinate, []) &&
            !EmergencyShelterSelection.IsRouteUsable(Route(1, 40), Point(0), open.Coordinate, []),
            "emergency selection rejects absent or mismatched pedestrian routes");
        check(!EmergencyShelterSelection.IsRouteUsable(Route(10, 90), Point(0), open.Coordinate, [Hazard(5)]) &&
            !EmergencyShelterSelection.IsRouteUsable(Route(10, 90), Point(0), open.Coordinate, [Hazard(50)]) &&
            !EmergencyShelterSelection.IsRouteUsable(Route(10, 90), Point(0), open.Coordinate, [Hazard(95)]) &&
            !EmergencyShelterSelection.IsRouteUsable(Route(10, 90), Point(0), open.Coordinate, [Hazard(100)]),
            "emergency selection checks the origin approach, walk, destination approach, and shelter for current hazards");

        try
        {
            NavigationDestinationBridge.Clear();
            var empty = NavigationDestinationBridge.Current;
            NavigationDestinationBridge.Set("Manual", Point(130));
            check(!NavigationDestinationBridge.TrySetIfUnselected(empty.Version, open.Name, open.Coordinate) &&
                NavigationDestinationBridge.Current.Name == "Manual",
                "emergency publication preserves the destination selected during route calculation");

            NavigationDestinationBridge.Clear();
            check(!NavigationDestinationBridge.TrySetIfUnselected(empty.Version, open.Name, open.Coordinate) &&
                !NavigationDestinationBridge.Current.IsAvailable,
                "emergency publication cannot resume after a newer destination was selected and cleared");

            empty = NavigationDestinationBridge.Current;
            var announced = NavigationDestinationBridge.DestinationSnapshot.Unavailable;
            EventHandler handler = (_, _) => announced = NavigationDestinationBridge.Current;
            NavigationDestinationBridge.DestinationChanged += handler;
            try
            {
                check(NavigationDestinationBridge.TrySetIfUnselected(empty.Version, open.Name, open.Coordinate) &&
                    announced.Version == empty.Version + 1 && announced.Coordinate == open.Coordinate,
                    "emergency publication announces the committed destination version for prepared route reuse");
            }
            finally
            {
                NavigationDestinationBridge.DestinationChanged -= handler;
            }
        }
        finally
        {
            NavigationDestinationBridge.Clear();
        }
    }
}
