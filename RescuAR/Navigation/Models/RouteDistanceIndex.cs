using System.Collections.Generic;

namespace RescuAR.Navigation.Models;

/// <summary>Searches route points ordered by nondecreasing cumulative distance.</summary>
internal static class RouteDistanceIndex
{
    /// <summary>Returns Count when no point reaches the requested distance.</summary>
    public static int AtOrAfter(IReadOnlyList<RoutePoint> points, double distance) =>
        Find(points, distance, strictlyAfter: false);

    /// <summary>Returns Count when no point follows the requested distance.</summary>
    public static int After(IReadOnlyList<RoutePoint> points, double distance) =>
        Find(points, distance, strictlyAfter: true);

    private static int Find(IReadOnlyList<RoutePoint> points, double distance, bool strictlyAfter)
    {
        int low = 0, high = points.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            double candidate = points[middle].DistanceFromStartMeters;
            if (candidate < distance || (strictlyAfter && candidate == distance))
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }
}
