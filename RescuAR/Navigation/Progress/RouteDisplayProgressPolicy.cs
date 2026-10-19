using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Places the mapped road at the observed GPS match independently of smoothed
/// trip progress. PDR advances only from that display anchor between GPS fixes.
/// This affects rendering references, never routing origins or arrival evidence.
/// </summary>
public sealed class RouteDisplayProgressPolicy
{
    public const int MaximumPdrGpsAgeSeconds = 8;
    public const double MaximumPdrAdvanceMeters = 8;
    private RouteResult? currentRoute;
    private DateTimeOffset? observedAt;
    private DisplayPosition gpsPosition;
    private double committedAtGps;

    public void Reset()
    {
        currentRoute = null;
        observedAt = null;
        gpsPosition = default;
        committedAtGps = 0;
    }

    public void ObserveGps(RouteResult route, RouteProgressTracker.RouteProgressUpdate update,
        DateTimeOffset timestamp)
    {
        if (!ReferenceEquals(currentRoute, route)) Reset();
        currentRoute = route;
        if (observedAt.HasValue && timestamp <= observedAt.Value) return;
        if (!TryResolveGps(route, update, out var position)) return;
        observedAt = timestamp;
        gpsPosition = position;
        committedAtGps = update.CommittedProgressMeters;
    }

    public bool TryResolvePdr(RouteResult route, double committedProgress, DateTimeOffset now,
        out DisplayPosition position)
    {
        position = default;
        if (!ReferenceEquals(currentRoute, route) || !observedAt.HasValue ||
            now < observedAt.Value || now - observedAt.Value > TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds))
            return false;
        double advance = committedProgress - committedAtGps;
        if (!double.IsFinite(advance) || advance < 0 || advance > MaximumPdrAdvanceMeters ||
            !TryAtDistance(route, Math.Min(route.Points[^1].DistanceFromStartMeters,
                gpsPosition.ProgressMeters + advance), out position)) return false;

        // Retain the last observed GPS-to-road offset rather than centering the
        // road on the camera on every step. This bounded reference is used only
        // by the AR projector; it is not published as a device location.
        position = position with { AlignmentCoordinate = new GeoCoordinate(
            gpsPosition.AlignmentCoordinate.Latitude + position.RoadCoordinate.Latitude - gpsPosition.RoadCoordinate.Latitude,
            gpsPosition.AlignmentCoordinate.Longitude + position.RoadCoordinate.Longitude - gpsPosition.RoadCoordinate.Longitude) };
        return position.AlignmentCoordinate.IsValid;
    }

    public static bool TryResolveGps(RouteResult route, RouteProgressTracker.RouteProgressUpdate update,
        out DisplayPosition position)
    {
        position = default;
        if (!update.IsAccepted || update.IsOffRoute || update.MatchConfidence < RouteMatchConfidence.Medium ||
            update.AccuracyMeters is not double accuracy || !double.IsFinite(accuracy) || accuracy < 0 || accuracy > 20 ||
            !update.GpsCoordinate.IsValid || !TryAtDistance(route, update.RawProgressMeters, out position) ||
            update.GpsCoordinate.DistanceTo(position.RoadCoordinate) > 20) return false;
        position = position with { AlignmentCoordinate = update.GpsCoordinate };
        return true;
    }

    public static bool TryAtDistance(RouteResult route, double progress, out DisplayPosition position)
    {
        position = default;
        if (route.Points.Count < 2 || !double.IsFinite(progress) ||
            progress < route.Points[0].DistanceFromStartMeters - 0.01 ||
            progress > route.Points[^1].DistanceFromStartMeters + 0.01) return false;
        progress = Math.Clamp(progress, route.Points[0].DistanceFromStartMeters,
            route.Points[^1].DistanceFromStartMeters);
        for (int i = 0; i < route.Points.Count - 1; i++)
        {
            var start = route.Points[i];
            var end = route.Points[i + 1];
            double span = end.DistanceFromStartMeters - start.DistanceFromStartMeters;
            if (end.DistanceFromStartMeters < progress || span <= 0.0001) continue;
            double t = Math.Clamp((progress - start.DistanceFromStartMeters) / span, 0, 1);
            var coordinate = new GeoCoordinate(
                start.Coordinate.Latitude + (end.Coordinate.Latitude - start.Coordinate.Latitude) * t,
                start.Coordinate.Longitude + (end.Coordinate.Longitude - start.Coordinate.Longitude) * t);
            if (!coordinate.IsValid) return false;
            position = new(progress, coordinate, i, coordinate);
            return true;
        }
        return false;
    }

    public readonly record struct DisplayPosition(double ProgressMeters, GeoCoordinate RoadCoordinate,
        int SegmentIndex, GeoCoordinate AlignmentCoordinate);
}
