using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Read-only map presentation of dev's accepted navigation state; never a second route/GPS owner.</summary>
public static class CameraMapPresentationPolicy
{
    public static bool CanProject(GeoCoordinate coordinate) => coordinate.IsValid && Math.Abs(coordinate.Latitude) <= 85.05112878;

    public static bool CanShowCurrent(LocationReading? reading, DateTimeOffset now, bool held) =>
        !held && reading is not null && CanProject(reading.Coordinate) &&
        RouteStartupLocationPolicy.CanPlan(reading.Coordinate, reading.AccuracyMeters, reading.Timestamp, now, cached: false);

    // A measured GPS observation can be displayed with its uncertainty even
    // when matching it to this route is held. It is not accepted route progress.
    public static bool CanShowObservation(LocationReading? reading, DateTimeOffset now) =>
        CanShowCurrent(reading, now, held: false);

    public static RouteResult? ForTrip(RouteResult? route, long routeTrip, long destinationTrip, bool hasDestination)
    {
        if (!hasDestination || destinationTrip <= 0 || routeTrip != destinationTrip || route is null ||
            route.Points.Count < 2 || !double.IsFinite(route.TotalDistanceMeters) || route.TotalDistanceMeters <= 0) return null;
        double previous = double.NegativeInfinity;
        foreach (var point in route.Points)
        {
            if (!CanProject(point.Coordinate) || !double.IsFinite(point.DistanceFromStartMeters) ||
                point.DistanceFromStartMeters < 0 || point.DistanceFromStartMeters < previous) return null;
            previous = point.DistanceFromStartMeters;
        }
        return route;
    }

    public static PedestrianTurnGuidanceService.TurnGuidanceSnapshot Guidance(RouteResult route,
        RouteProgressTracker.ProgressSnapshot progress, bool held)
    {
        if (held || !progress.HasRoute || !progress.HasProgress || progress.IsOffRoute ||
            !double.IsFinite(progress.CommittedProgressMeters) || !double.IsFinite(progress.RemainingMeters) ||
            progress.CommittedProgressMeters < 0 || progress.RemainingMeters < 0)
            return PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable;
        return new PedestrianTurnGuidanceService().Evaluate(route, progress.CommittedProgressMeters, progress.SegmentIndex)
            with { RemainingRouteMeters = progress.RemainingMeters };
    }

    public static string DistanceTo(GeoCoordinate destination, LocationReading? current, DateTimeOffset now) =>
        CanProject(destination) && CanShowCurrent(current, now, false)
            ? $"~{current!.Coordinate.DistanceTo(destination):0} m straight-line" : "Distance unknown";

    public static string DirectoryStatus(string? status) => string.IsNullOrWhiteSpace(status)
        ? "Directory entry · current availability unknown"
        : $"Directory status: {status.Trim()} · live availability unverified";

    public static string WalkEstimate(double meters) => double.IsFinite(meters) && meters >= 0
        ? $"~{Math.Max(1, Math.Round(meters / 80)):0} min estimated walk; pace/conditions vary"
        : "Walking time unknown";
}

public sealed class CameraMapViewportPolicy
{
    private (long Trip, string Geometry)? framed;
    public bool ShouldFrameRoute(long trip, RouteResult route)
    {
        var key = (trip, route.GeometryFingerprint);
        if (framed == key) return false;
        framed = key; return true;
    }
}
