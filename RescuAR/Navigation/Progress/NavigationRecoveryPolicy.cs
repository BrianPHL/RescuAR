using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Progress;

[Flags]
public enum NavigationBlockers
{
    None = 0, Route = 1, Tracking = 2, Gps = 4, Heading = 8,
    Ground = 16, Registration = 32, Geometry = 64, Position = 128
}

/// <summary>Reports recovery without authorizing geometry, changing location,
/// or turning a temporary good frame into a fresh positioning baseline.</summary>
public sealed class NavigationRecoveryPolicy
{
    public const int ActionAfterSeconds = 15;
    private long trip = -1;
    private long? startedAt, healthyAt;

    public readonly record struct Status(NavigationBlockers Blockers, double Seconds,
        bool RecommendMap, string Message);

    public void Reset() { trip = -1; startedAt = healthyAt = null; }

    public Status Evaluate(long selectedTrip, NavigationBlockers blockers, long monotonicNow)
    {
        if (trip != selectedTrip || startedAt > monotonicNow)
        { Reset(); trip = selectedTrip; }
        if (blockers == NavigationBlockers.None)
        {
            healthyAt ??= monotonicNow;
            if (monotonicNow - healthyAt >= 2000) startedAt = null;
            return new(blockers, 0, false, "");
        }
        healthyAt = null;
        startedAt ??= monotonicNow;
        double seconds = Math.Max(0, (monotonicNow - startedAt.Value) / 1000d);
        string message = blockers.HasFlag(NavigationBlockers.Route) ? "Finding a walking route"
            : blockers.HasFlag(NavigationBlockers.Tracking) ? "Camera tracking paused — keep the street in view"
            : blockers.HasFlag(NavigationBlockers.Gps) ? "Waiting for recent GPS — check device location settings"
            : blockers.HasFlag(NavigationBlockers.Heading) ? "Direction not confirmed — hold the phone upright and steady"
            : blockers.HasFlag(NavigationBlockers.Position) ? "Position disagrees with tracking — waiting for it to settle"
            : blockers.HasFlag(NavigationBlockers.Ground) ? "Ground placement not ready — keep the street in view"
            : blockers.HasFlag(NavigationBlockers.Registration) ? "Checking route alignment"
            : "Route display is recovering";
        bool recommend = seconds >= ActionAfterSeconds;
        return new(blockers, seconds, recommend, recommend ? message + ". Open 2D Map to continue." : message);
    }

    public static string ProviderLabel(RouteResult? route) => route is null ? "Routing"
        : AStarRouteDisplayPolicy.AppliesTo(route) ? "A-STAR"
        : route.Algorithm.Contains("MLD", StringComparison.OrdinalIgnoreCase) ? "MLD" : route.Algorithm;
}
