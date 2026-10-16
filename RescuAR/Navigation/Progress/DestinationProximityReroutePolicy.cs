using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.Navigation.Progress;

/// <summary>Bounds reroute suppression while nearby destination GPS is uncertain.</summary>
public sealed class DestinationProximityReroutePolicy
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(30);
    private DateTimeOffset? lastNearbyFix;
    private DateTimeOffset? lastObservation;
    private int outsideConfirmations;
    private GeoCoordinate? destination;

    public void Reset()
    {
        lastNearbyFix = null;
        lastObservation = null;
        outsideConfirmations = 0;
        destination = null;
    }

    public bool ShouldHold(GeoCoordinate gps, GeoCoordinate target, double? accuracy,
        DateTimeOffset fix, DateTimeOffset now, bool accessVerified = true)
    {
        if (destination != target) { Reset(); destination = target; }
        if (!accessVerified) { Reset(); return false; }
        bool active = lastNearbyFix.HasValue && now >= lastNearbyFix.Value &&
            now - lastNearbyFix.Value <= HoldDuration;
        if (!target.IsValid || !RouteStartupLocationPolicy.CanPlan(gps, accuracy, fix, now, cached: false))
            return active;
        if (lastObservation.HasValue && fix <= lastObservation.Value) return active;
        bool independent = !lastObservation.HasValue || fix - lastObservation.Value >= TimeSpan.FromSeconds(1);
        lastObservation = fix;
        double distance = gps.DistanceTo(target);
        if (distance <= 45 && distance - accuracy!.Value <= 30)
        {
            lastNearbyFix = fix;
            outsideConfirmations = 0;
            return true;
        }
        if (accuracy <= 15 && distance - accuracy!.Value > 45 && independent)
        {
            outsideConfirmations++;
            if (outsideConfirmations >= 2) { lastNearbyFix = null; return false; }
        }
        else outsideConfirmations = 0;
        return active;
    }
}
