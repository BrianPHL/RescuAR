using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

namespace RescuAR.Navigation.Projection;

/// <summary>A* owns its preview explicitly; it never borrows MLD cue ownership.</summary>
public static class AStarMappedPreviewPolicy
{
    public readonly record struct Owner(long Trip, string Geometry, long Window, long Session,
        long Ground, long TrackingEpoch, long WorldFrame, long Calibration);

    public static bool CanRetain(RouteResult route, Owner owner, Owner current,
        DateTimeOffset observedAt, DateTimeOffset now, bool registrationQualified,
        bool positionHeld, bool identitySuspended, bool hazardBlocked) =>
        AStarRouteDisplayPolicy.AppliesTo(route) && owner == current && owner.Trip > 0 &&
        owner.Session > 0 && owner.Ground > 0 && owner.Geometry == route.GeometryFingerprint &&
        !positionHeld && !identitySuspended && !hazardBlocked &&
        MappedRoutePreviewPolicy.CanRetain(observedAt, now, registrationQualified);
}
