using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Progress;

/// <summary>Filters trip distance only. Raw fixes still feed matching, heading and arrival.</summary>
public sealed class RouteProgressMotionPolicy
{
    private RouteResult? route;
    private GeoCoordinate anchor;
    private double anchorAccuracy;
    private DateTimeOffset? lastAt;
    private RouteBacktrackingPolicy.TrackedMotion? anchorMotion;

    public void Reset()
    {
        route = null;
        lastAt = null;
        anchorMotion = null;
    }

    public double Filter(RouteResult currentRoute, GeoCoordinate coordinate, double? accuracy,
        DateTimeOffset at, double previousProgress, double proposedProgress,
        RouteBacktrackingPolicy.TrackedMotion? motion, out bool held)
    {
        held = false;
        if (!ReferenceEquals(route, currentRoute)) { Reset(); route = currentRoute; }
        if (accuracy is not double precision || !double.IsFinite(precision) || precision < 0 ||
            !coordinate.IsValid || !double.IsFinite(previousProgress + proposedProgress))
        { held = true; return previousProgress; }
        if (lastAt is { } previousAt && at <= previousAt)
        { held = true; return previousProgress; }
        bool initialized = lastAt.HasValue;
        lastAt = at;
        bool tracked = anchorMotion is { } from && motion is { } to && from.IsContinuousWith(to);
        bool stationary = tracked
            ? Math.Sqrt(Math.Pow(motion!.Value.X - anchorMotion!.Value.X, 2) +
                Math.Pow(motion.Value.Z - anchorMotion.Value.Z, 2)) <= RoutePositionObservationPolicy.MotionAllowanceMeters
            : initialized && coordinate.DistanceTo(anchor) <= Math.Sqrt(
                anchorAccuracy * anchorAccuracy + precision * precision);
        if (!tracked) anchorMotion = motion;
        // Keep the anchor fixed across held samples: successive tiny movements
        // must accumulate, while repeated GPS noise must not ratchet distance.
        if (initialized && stationary)
        { held = true; return previousProgress; }
        anchor = coordinate;
        anchorAccuracy = precision;
        anchorMotion = motion;
        return proposedProgress;
    }
}
