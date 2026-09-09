using System.Threading;
using System.Threading.Tasks;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Common routing boundary.
///
/// The teammate's MLD implementation and the future A* implementation should
/// adapt to this interface so the remainder of RescuAR never depends on a
/// routing algorithm's private result model.
/// </summary>
public interface IRoutingService
{
    string AlgorithmName { get; }

    Task<RouteResult?> FindRouteAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        CancellationToken cancellationToken = default);
}
