using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Optional routing capability for providers that can calculate a route while
/// respecting RescuAR geographic hazard exclusion zones.
/// </summary>
public interface IHazardAwareRoutingService : IRoutingService
{
    Task<RouteResult?> FindRouteAvoidingHazardsAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        IReadOnlyList<RouteHazard> hazards,
        CancellationToken cancellationToken = default);
}
