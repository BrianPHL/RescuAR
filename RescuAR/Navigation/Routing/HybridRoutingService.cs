using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Resilient routing boundary for RescuAR.
///
/// MLD/OSRM remains the preferred online provider. When Internet connectivity
/// is unavailable, or an online request fails specifically because the remote
/// service cannot be reached, the existing on-device AStarRoutingService is
/// used against the embedded pedestrian RoadGraph.
///
/// A normal online "NoRoute" result is not silently replaced with A*: that is
/// a routing result, not a connectivity failure.
/// </summary>
public sealed class HybridRoutingService : IRoutingService
{
    private const string LogTag =
        "RescuAR-HybridRouting";

    private readonly IRoutingService onlineRoutingService;
    private readonly Func<CancellationToken, Task<RoadGraph>> roadGraphProvider;
    private readonly Func<bool> isInternetAvailable;

    private readonly SemaphoreSlim offlineInitializationGate =
        new(
            1,
            1);

    private AStarRoutingService? offlineRoutingService;

    public string AlgorithmName =>
        "Hybrid (MLD/AStar)";

    public HybridRoutingService(
        IRoutingService onlineRoutingService,
        Func<CancellationToken, Task<RoadGraph>> roadGraphProvider,
        Func<bool> isInternetAvailable)
    {
        this.onlineRoutingService =
            onlineRoutingService ??
            throw new ArgumentNullException(
                nameof(onlineRoutingService));

        this.roadGraphProvider =
            roadGraphProvider ??
            throw new ArgumentNullException(
                nameof(roadGraphProvider));

        this.isInternetAvailable =
            isInternetAvailable ??
            throw new ArgumentNullException(
                nameof(isInternetAvailable));
    }

    /// <summary>
    /// Normal navigation entry point. Prefer MLD only while Internet access is
    /// actually available; otherwise route locally with A* immediately.
    /// </summary>
    public async Task<RouteResult?> FindRouteAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!GetInternetAvailabilitySafely())
        {
            AndroidLog.Warn(
                LogTag,
                "Internet access is unavailable. Skipping MLD and routing " +
                "immediately with the embedded offline A* graph.");

            return await FindOfflineRouteAsync(
                origin,
                destination,
                cancellationToken);
        }

        AndroidLog.Debug(
            LogTag,
            $"Internet access is available. Attempting preferred online provider " +
            $"'{onlineRoutingService.AlgorithmName}'.");

        try
        {
            RouteResult? route =
                await onlineRoutingService.FindRouteAsync(
                    origin,
                    destination,
                    cancellationToken);

            if (route is null)
            {
                AndroidLog.Warn(
                    LogTag,
                    "Online routing completed with no route. This is not treated " +
                    "as a connectivity failure, so A* fallback was not invoked.");

                return null;
            }

            AndroidLog.Debug(
                LogTag,
                "Hybrid routing selected ONLINE route: " +
                $"algorithm='{route.Algorithm}', " +
                $"points={route.Points.Count}, " +
                $"distance={route.TotalDistanceMeters:F1} m.");

            return route;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            AndroidLog.Warn(
                LogTag,
                "Online routing timed out. Falling back to offline A*. ");
        }
        catch (TimeoutException ex)
        {
            AndroidLog.Warn(
                LogTag,
                "Online routing timed out. Falling back to offline A*: " +
                ex.Message);
        }
        catch (HttpRequestException ex)
        {
            AndroidLog.Warn(
                LogTag,
                "Online routing transport failed. Falling back to offline A*: " +
                ex.Message);
        }

        return await FindOfflineRouteAsync(
            origin,
            destination,
            cancellationToken);
    }

    /// <summary>
    /// Explicit offline route entry point used when an already-active MLD trip
    /// receives a confirmed Internet-loss event. This guarantees that the
    /// transition itself is MLD -> A* even if connectivity changes again while
    /// the replacement calculation is being prepared.
    /// </summary>
    public async Task<RouteResult?> FindOfflineRouteAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AStarRoutingService offline =
            await GetOfflineRoutingServiceAsync(
                cancellationToken);

        AndroidLog.Warn(
            LogTag,
            "OFFLINE A* route request started: " +
            $"origin=({origin.Latitude:F7},{origin.Longitude:F7}), " +
            $"destination=({destination.Latitude:F7},{destination.Longitude:F7}).");

        RouteResult? route =
            await offline.FindRouteAsync(
                origin,
                destination,
                cancellationToken);

        if (route is null)
        {
            AndroidLog.Warn(
                LogTag,
                "Offline A* returned no usable route from the embedded graph.");

            return null;
        }

        AndroidLog.Warn(
            LogTag,
            "Hybrid routing selected OFFLINE route: " +
            $"algorithm='{route.Algorithm}', " +
            $"points={route.Points.Count}, " +
            $"distance={route.TotalDistanceMeters:F1} m.");

        return route;
    }

    private async Task<AStarRoutingService> GetOfflineRoutingServiceAsync(
        CancellationToken cancellationToken)
    {
        AStarRoutingService? existing =
            offlineRoutingService;

        if (existing is not null)
        {
            return existing;
        }

        await offlineInitializationGate.WaitAsync(
            cancellationToken);

        try
        {
            existing =
                offlineRoutingService;

            if (existing is not null)
            {
                return existing;
            }

            RoadGraph graph =
                await roadGraphProvider(
                    cancellationToken);

            existing =
                new AStarRoutingService(
                    graph);

            offlineRoutingService =
                existing;

            AndroidLog.Debug(
                LogTag,
                "Offline A* provider READY: " +
                $"nodes={graph.Nodes.Count}, " +
                $"directedEdges={graph.Edges.Count}.");

            return existing;
        }
        finally
        {
            offlineInitializationGate.Release();
        }
    }

    private bool GetInternetAvailabilitySafely()
    {
        try
        {
            return isInternetAvailable();
        }
        catch (Exception ex)
        {
            AndroidLog.Warn(
                LogTag,
                "Connectivity-state query failed; treating the device as offline " +
                "for resilient routing. " +
                $"{ex.GetType().Name}: {ex.Message}");

            return false;
        }
    }
}
