using System;

namespace RescuAR.Navigation.Progress;

/// <summary>Two independent, fresh proximity fixes establish mapped road entry.</summary>
public sealed class RoadEntryConfirmationPolicy
{
    public int ConfirmationCount { get; private set; }
    private DateTimeOffset? lastFix;

    public void Reset()
    {
        ConfirmationCount = 0;
        lastFix = null;
    }

    public bool Observe(bool isAccepted, bool isOffRoute, double crossTrackErrorMeters,
        double corridorRadiusMeters, RouteMatchConfidence confidence, double? accuracyMeters,
        DateTimeOffset fixTimestamp, DateTimeOffset now)
    {
        TimeSpan age = now - fixTimestamp;
        if (age < TimeSpan.FromSeconds(-2) || age > TimeSpan.FromSeconds(5))
        {
            ConfirmationCount = 0;
            return false;
        }
        if (lastFix.HasValue && fixTimestamp <= lastFix.Value) return false;
        if (lastFix.HasValue && fixTimestamp - lastFix.Value > TimeSpan.FromSeconds(8))
            ConfirmationCount = 0;
        lastFix = fixTimestamp;
        if (!RouteCorridorPolicy.CanEnterRoadFollowing(isAccepted, isOffRoute,
                crossTrackErrorMeters, corridorRadiusMeters, confidence, accuracyMeters))
        {
            ConfirmationCount = 0;
            return false;
        }
        ConfirmationCount = Math.Min(2, ConfirmationCount + 1);
        return ConfirmationCount >= 2;
    }
}
