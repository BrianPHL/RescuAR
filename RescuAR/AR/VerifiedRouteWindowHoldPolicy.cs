using System;
using System.Threading;
using RescuAR.Navigation.Models;

namespace RescuAR.AR;

/// <summary>
/// Bounds reuse of an already verified window during GPS uncertainty. The
/// caller must still require live tracking, trusted heading, fresh GPS,
/// available matching, and no deviation, reroute, or completed arrival.
/// </summary>
public sealed class VerifiedRouteWindowHoldPolicy
{
    private VerifiedWindow? verified;

    public void Reset() => Volatile.Write(ref verified, null);

    public void ObserveVerified(long routeVersion, long groundGeneration,
        GeoCoordinate gps, DateTimeOffset now)
    {
        if (routeVersion < 0 || groundGeneration <= 0 || !gps.IsValid) return;
        Volatile.Write(ref verified, new VerifiedWindow(routeVersion, groundGeneration, gps, now));
    }

    public bool CanHold(long routeVersion, long groundGeneration, GeoCoordinate? gps,
        double? accuracy, DateTimeOffset now, bool routeIdentitySuspended)
    {
        if (routeIdentitySuspended || Volatile.Read(ref verified) is not VerifiedWindow previous ||
            routeVersion != previous.RouteVersion || groundGeneration != previous.GroundGeneration ||
            now < previous.At || now - previous.At > TimeSpan.FromSeconds(6) ||
            gps is not GeoCoordinate currentGps || !currentGps.IsValid ||
            accuracy is not double meters || !double.IsFinite(meters) || meters < 0 || meters > 25)
            return false;

        return currentGps.DistanceTo(previous.Gps) <= 15;
    }

    private sealed record VerifiedWindow(long RouteVersion, long GroundGeneration,
        GeoCoordinate Gps, DateTimeOffset At);
}
