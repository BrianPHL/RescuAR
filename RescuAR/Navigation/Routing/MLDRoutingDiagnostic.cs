using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Optional one-shot diagnostic for proving Railway MLD connectivity.
///
/// All visible diagnostics go through Android.Util.Log at runtime.
/// </summary>
public static class MLDRoutingDiagnostic
{
    private const string LogTag =
        "RescuAR-MLD";

    public static async Task<RouteResult?> RunAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        MLDRoutingService service =
            new();

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        AndroidLog.Debug(
            LogTag,
            "MLD diagnostic started.");

        try
        {
            RouteResult? route =
                await service.FindRouteAsync(
                    origin,
                    destination,
                    cancellationToken);

            stopwatch.Stop();

            if (route is null)
            {
                AndroidLog.Warn(
                    LogTag,
                    $"MLD diagnostic completed with NO ROUTE. " +
                    $"elapsed={stopwatch.ElapsedMilliseconds} ms");
            }
            else
            {
                AndroidLog.Debug(
                    LogTag,
                    "MLD diagnostic completed: " +
                    $"algorithm='{route.Algorithm}', " +
                    $"points={route.Points.Count}, " +
                    $"distance={route.TotalDistanceMeters:F1} m, " +
                    $"elapsed={stopwatch.ElapsedMilliseconds} ms");
            }

            return route;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            AndroidLog.Error(
                LogTag,
                $"MLD diagnostic FAILED after " +
                $"{stopwatch.ElapsedMilliseconds} ms: {ex}");

            throw;
        }
    }
}
