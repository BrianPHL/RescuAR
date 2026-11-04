using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Routing;

namespace RescuAR.Navigation.Progress;

/// <summary>Trip control and geometry provenance are independent. Losing the
/// network selects A* without replacing an already downloaded route.</summary>
public sealed class OfflineRouteHandoffPolicy
{
    public const long SavedRouteLifetimeMilliseconds = 5 * 60 * 1000;
    public const long RefreshRetryMilliseconds = 30 * 1000;
    private long trip = -1;
    private RouteResult? retainedRoute;
    private long retainedAt, nextRefreshAt;
    public bool OfflineSelected { get; private set; }

    public void Reset()
    {
        trip = -1;
        retainedRoute = null;
        retainedAt = nextRefreshAt = 0;
        OfflineSelected = false;
    }

    public bool Observe(RouteResult route, long selectedTrip, bool internetAvailable, long now)
    {
        if (trip != selectedTrip) { Reset(); trip = selectedTrip; }
        bool changed = !OfflineSelected && !internetAvailable;
        OfflineSelected |= !internetAvailable;
        // Reconnection does not silently change the provider of this trip.
        if (OfflineSelected && !HybridRoutingService.IsOfflineRoute(route))
        {
            if (!ReferenceEquals(retainedRoute, route))
            {
                retainedRoute = route;
                retainedAt = now;
                nextRefreshAt = now + SavedRouteLifetimeMilliseconds;
            }
        }
        else retainedRoute = null;
        return changed;
    }

    public bool TryBeginRefresh(RouteResult route, long selectedTrip, long now, bool qualifiedPosition)
    {
        if (!OfflineSelected || trip != selectedTrip || !ReferenceEquals(retainedRoute, route) ||
            !qualifiedPosition || now < retainedAt || now < nextRefreshAt) return false;
        nextRefreshAt = now + RefreshRetryMilliseconds;
        return true;
    }

    public string ProviderLabel(RouteResult? route) => OfflineSelected && route is not null &&
        !HybridRoutingService.IsOfflineRoute(route)
        ? $"A-STAR · saved {NavigationRecoveryPolicy.ProviderLabel(route)} path"
        : NavigationRecoveryPolicy.ProviderLabel(route);
}
