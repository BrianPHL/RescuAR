using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Progress;

/// <summary>Recognizes an A* replacement that follows one directed subsection
/// of the accepted route. This is geometry identity, not GPS or AR evidence.</summary>
public sealed class AStarReplacementContinuityPolicy
{
    public const double MaximumGeometryDifferenceMeters = 1;
    private const long MaximumGeometryComparisons = 250_000;
    public RouteResult Previous { get; }
    public RouteResult Replacement { get; }
    public double PreviousStartMeters { get; }
    public double PreviousEndMeters { get; }

    private AStarReplacementContinuityPolicy(RouteResult previous, RouteResult replacement,
        double start, double end)
    {
        Previous = previous;
        Replacement = replacement;
        PreviousStartMeters = start;
        PreviousEndMeters = end;
    }

    public static bool TryCreate(RouteResult previous, RouteResult replacement,
        out AStarReplacementContinuityPolicy? continuity)
    {
        continuity = null;
        if (!AStarRouteDisplayPolicy.AppliesTo(replacement) || previous.Points.Count < 2 ||
            replacement.Points.Count < 2 || !Valid(previous) || !Valid(replacement) ||
            2L * previous.Points.Count * replacement.Points.Count > MaximumGeometryComparisons ||
            !TryProjectUnique(previous, replacement.Points[0].Coordinate, out double start) ||
            !TryProjectUnique(previous, replacement.Points[^1].Coordinate, out double end) || end <= start ||
            Math.Abs(end - start - (replacement.Points[^1].DistanceFromStartMeters -
                replacement.Points[0].DistanceFromStartMeters)) > MaximumGeometryDifferenceMeters)
            return false;

        // Check both vertex sets so a sparse chord cannot silently cut a turn.
        double last = start;
        foreach (var point in replacement.Points)
        {
            if (!TryProjectUnique(previous, point.Coordinate, out double progress) || progress < last - .001 ||
                Math.Abs(progress - start - (point.DistanceFromStartMeters -
                    replacement.Points[0].DistanceFromStartMeters)) > MaximumGeometryDifferenceMeters)
                return false;
            last = progress;
        }
        foreach (var point in previous.Points)
            if (point.DistanceFromStartMeters >= start && point.DistanceFromStartMeters <= end &&
                (!TryProjectUnique(replacement, point.Coordinate, out double progress) ||
                 Math.Abs(progress - replacement.Points[0].DistanceFromStartMeters -
                    (point.DistanceFromStartMeters - start)) > MaximumGeometryDifferenceMeters)) return false;

        continuity = new(previous, replacement, start, end);
        return true;
    }

    public bool TryMapProgress(double previousProgress, out double replacementProgress)
    {
        replacementProgress = 0;
        if (!double.IsFinite(previousProgress) || previousProgress < PreviousStartMeters - MaximumGeometryDifferenceMeters ||
            previousProgress > PreviousEndMeters + MaximumGeometryDifferenceMeters ||
            !RouteDisplayProgressPolicy.TryAtDistance(Previous, Math.Clamp(previousProgress,
                Previous.Points[0].DistanceFromStartMeters, Previous.Points[^1].DistanceFromStartMeters), out var position))
            return false;
        return TryProjectUnique(Replacement, position.RoadCoordinate, out replacementProgress);
    }

    public bool TryMapPosition(RouteDisplayProgressPolicy.DisplayPosition previous,
        out RouteDisplayProgressPolicy.DisplayPosition replacement)
    {
        replacement = default;
        if (!TryMapProgress(previous.ProgressMeters, out double progress) ||
            !RouteDisplayProgressPolicy.TryAtDistance(Replacement, progress, out replacement)) return false;
        replacement = replacement with { AlignmentCoordinate = previous.AlignmentCoordinate };
        return true;
    }

    private static bool Valid(RouteResult route)
    {
        double previous = -1;
        foreach (var point in route.Points)
        {
            if (!point.Coordinate.IsValid || !double.IsFinite(point.DistanceFromStartMeters) ||
                point.DistanceFromStartMeters < previous || point.DistanceFromStartMeters < 0) return false;
            previous = point.DistanceFromStartMeters;
        }
        return true;
    }

    private static bool TryProjectUnique(RouteResult route, GeoCoordinate coordinate, out double progress)
    {
        progress = 0;
        double best = double.PositiveInfinity, first = double.NaN;
        double latitude = coordinate.Latitude * Math.PI / 180;
        for (int i = 0; i < route.Points.Count - 1; i++)
        {
            var a = route.Points[i]; var b = route.Points[i + 1];
            double span = b.DistanceFromStartMeters - a.DistanceFromStartMeters;
            if (span <= .0001) continue;
            double ax = (a.Coordinate.Longitude - coordinate.Longitude) * Math.PI / 180 * 6371008.8 * Math.Cos(latitude);
            double az = (a.Coordinate.Latitude - coordinate.Latitude) * Math.PI / 180 * 6371008.8;
            double bx = (b.Coordinate.Longitude - coordinate.Longitude) * Math.PI / 180 * 6371008.8 * Math.Cos(latitude);
            double bz = (b.Coordinate.Latitude - coordinate.Latitude) * Math.PI / 180 * 6371008.8;
            double dx = bx - ax, dz = bz - az, length = dx * dx + dz * dz;
            if (length < .0001) continue;
            double t = Math.Clamp(-(ax * dx + az * dz) / length, 0, 1);
            double error = Math.Pow(ax + t * dx, 2) + Math.Pow(az + t * dz, 2);
            if (error > MaximumGeometryDifferenceMeters * MaximumGeometryDifferenceMeters) continue;
            double candidate = a.DistanceFromStartMeters + t * span;
            // Multiple nearby legs of a loop cannot supply a unique rebase.
            if (double.IsFinite(first) && Math.Abs(candidate - first) > MaximumGeometryDifferenceMeters) return false;
            if (!double.IsFinite(first)) first = candidate;
            if (error < best) { best = error; progress = candidate; }
        }
        return double.IsFinite(best);
    }
}
