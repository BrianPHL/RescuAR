using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

namespace RescuAR.Navigation.Projection;

/// <summary>Shows mapped geometry nearby without claiming physical road entry.</summary>
public static class MappedRoutePreviewPolicy
{
    // A new publication needs a <=5 s fix. Keeping the already verified same-
    // frame window may bridge to the next poll within the existing 8 s budget.
    // The caller must qualify its route owner, pose, heading and registration.
    public static bool CanRetain(DateTimeOffset observationTime, DateTimeOffset now,
        bool registeredPlacementQualified) => registeredPlacementQualified &&
        now - observationTime > RouteStartupLocationPolicy.MaximumCurrentFixAge &&
        now - observationTime <= TimeSpan.FromSeconds(RouteDisplayProgressPolicy.MaximumPdrGpsAgeSeconds);

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

    /// <summary>Common publication/visibility decision. Retention only relaxes
    /// freshness; it cannot bypass positioning, route identity or access.</summary>
    public static bool CanDisplay(bool accepted, bool offRoute,
        RouteMatchConfidence confidence, double? accuracy, GeoCoordinate gps, GeoCoordinate matched,
        DateTimeOffset fixTimestamp, DateTimeOffset now, bool accessVerified,
        bool initialApproachPending, bool returningToRoute, bool retainedContextQualified)
    {
        if (CanPublish(accepted, offRoute, confidence, accuracy, gps, matched,
                fixTimestamp, now, accessVerified, initialApproachPending, returningToRoute)) return true;
        return CanRetain(fixTimestamp, now, retainedContextQualified) &&
            CanPublish(accepted, offRoute, confidence, accuracy, gps, matched,
                fixTimestamp, fixTimestamp, accessVerified, initialApproachPending, returningToRoute);
    }
}
