using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Projection;

/// <summary>Planning quality is independent of precise AR geometry placement.</summary>
public static class RouteStartupLocationPolicy
{
    public const double MaximumPlanningAccuracyMeters = 30.0;
    public const double MaximumPlacementAccuracyMeters = 20.0;

    public static bool CanPlan(GeoCoordinate coordinate, double? accuracy,
        DateTimeOffset timestamp, DateTimeOffset now, bool cached)
    {
        if (!coordinate.IsValid || accuracy is not double meters ||
            !double.IsFinite(meters) || meters < 0 || meters > MaximumPlanningAccuracyMeters)
            return false;
        TimeSpan age = now - timestamp;
        return age >= TimeSpan.FromSeconds(-2) &&
            age <= TimeSpan.FromSeconds(cached ? 8 : 5);
    }

    public static bool CanPlace(GeoCoordinate coordinate, double? accuracy,
        DateTimeOffset timestamp, DateTimeOffset now) =>
        CanPlan(coordinate, accuracy, timestamp, now, cached: false) &&
        accuracy <= MaximumPlacementAccuracyMeters;
}
