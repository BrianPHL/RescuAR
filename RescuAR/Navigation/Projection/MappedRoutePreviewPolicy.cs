using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

namespace RescuAR.Navigation.Projection;

/// <summary>Shows mapped geometry nearby without claiming physical road entry.</summary>
public static class MappedRoutePreviewPolicy
{
    public static bool CanPublish(bool accepted, bool offRoute,
        RouteMatchConfidence confidence, double? accuracy,
        GeoCoordinate gps, GeoCoordinate matched,
        DateTimeOffset fixTimestamp, DateTimeOffset now, bool accessVerified,
        bool initialApproachPending = true, bool returningToRoute = false)
    {
        if ((!initialApproachPending && !returningToRoute) || !accepted || offRoute || !accessVerified ||
            confidence < RouteMatchConfidence.Medium || !matched.IsValid ||
            !RouteStartupLocationPolicy.CanPlace(gps, accuracy, fixTimestamp, now))
            return false;

        double distance = gps.DistanceTo(matched);
        return double.IsFinite(distance) &&
            distance <= RouteStartupLocationPolicy.MaximumPlacementAccuracyMeters;
    }
}
