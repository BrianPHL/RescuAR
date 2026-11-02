using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.Navigation.Progress;

/// <summary>A* display uses tracked position in the qualified registration,
/// rather than allowing raw GPS progress to select a separate visual origin.</summary>
public static class AStarRouteDisplayPolicy
{
    public static bool AppliesTo(RouteResult route) =>
        string.Equals(route.Provenance.Provider, "ASTAR", StringComparison.OrdinalIgnoreCase) ||
        (route.Provenance == RouteProvenance.Unreported &&
         (string.Equals(route.Algorithm, "AStar", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(route.Algorithm, "AStar (Hazard-Aware)", StringComparison.OrdinalIgnoreCase)));

    // A long horizon multiplies small yaw errors into multi-metre lateral
    // offsets. Keep the near-field geometry useful without changing its yaw,
    // map coordinates, GPS accuracy or the complete geographic route.
    public static double LimitWindow(RouteResult route, double requestedMeters) =>
        AppliesTo(route) ? Math.Min(requestedMeters, LocalArNavigationPolicy.ApproachWindowMeters) : requestedMeters;

    public static bool TryGetDisplayPosition(RouteResult route, RouteDisplayProgressPolicy registration,
        DateTimeOffset now, RouteBacktrackingPolicy.TrackedMotion? motion,
        out RouteDisplayProgressPolicy.DisplayPosition position)
    {
        position = default;
        return AppliesTo(route) && motion is { IsValid: true } tracked &&
            registration.TryGetRegisteredDisplayPosition(route, now, tracked, out position);
    }
}
