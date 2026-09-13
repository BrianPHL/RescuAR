using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Connects the selected routing provider (online MLD or offline A*) to the AR route bridge.
///
/// The full RouteResult is returned to the caller. Camera/navigation may then
/// keep that full geometry and republish short progress-aware AR windows
/// without making another Railway request for every GPS update.
/// </summary>
public sealed class MLDARIntegrationService
{
    private const string LogTag =
        "RescuAR-Routing";

    private const string ProgressLogTag =
        "RescuAR-NavProgress";

    private readonly IRoutingService routingService;

    public MLDARIntegrationService()
        : this(
            new MLDRoutingService())
    {
    }

    public MLDARIntegrationService(
        IRoutingService routingService)
    {
        this.routingService =
            routingService ??
            throw new ArgumentNullException(
                nameof(routingService));
    }

    public async Task<RouteResult?> RequestAndPublishAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        double mapToArYawDegrees,
        double arWindowMeters = 7.5,
        CancellationToken cancellationToken = default)
    {
        AndroidLog.Debug(
            LogTag,
            "Routing -> AR integration request: " +
            $"provider='{routingService.AlgorithmName}', " +
            $"window={arWindowMeters:F1} m, " +
            $"mapToArYaw={mapToArYawDegrees:F2} deg");

        RouteResult? route =
            await routingService.FindRouteAsync(
                origin,
                destination,
                cancellationToken);

        if (route is null ||
            route.Points.Count <
                2)
        {
            AndroidLog.Warn(
                LogTag,
                "Routing -> AR integration produced no usable route; " +
                "clearing AR route.");

            ARRouteBridge.Clear();

            return route;
        }

        GeoCoordinate initialReference =
            route.Points[0]
                .Coordinate;

        bool published =
            PublishWindow(
                route,
                route.Points[0]
                    .DistanceFromStartMeters,
                initialReference,
                mapToArYawDegrees,
                arOriginOffsetX:
                    0.0f,
                arOriginOffsetZ:
                    0.0f,
                arWindowMeters:
                    arWindowMeters,
                logTag:
                    ProgressLogTag,
                clearRouteOnFailure:
                    true);

        if (published)
        {
            AndroidLog.Debug(
                LogTag,
                "Routing -> AR route published successfully: " +
                $"algorithm='{route.Algorithm}'.");
        }

        return route;
    }

    /// <summary>
    /// Requests route geometry without touching ARRouteBridge.
    ///
    /// Dynamic rerouting uses this two-phase path so the currently visible
    /// route remains intact until a replacement route has been received
    /// and is ready to publish.
    /// </summary>
    public Task<RouteResult?> RequestRouteAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        AndroidLog.Debug(
            LogTag,
            "Route-only request started: " +
            $"provider='{routingService.AlgorithmName}'.");

        return routingService.FindRouteAsync(
            origin,
            destination,
            cancellationToken);
    }

    /// <summary>
    /// Republishes a short AR window from the retained full route.
    ///
    /// arOriginOffsetX/Z are expressed relative to the existing AR route root
    /// / ground anchor. CameraPage supplies the current AR camera horizontal
    /// offset from that anchor so the new window begins near the user's
    /// current physical position rather than staying at the original route
    /// start.
    /// </summary>
    public bool PublishProgressWindow(
        RouteResult route,
        double progressMeters,
        GeoCoordinate snappedReference,
        double mapToArYawDegrees,
        float arOriginOffsetX,
        float arOriginOffsetZ,
        double arWindowMeters = 7.5,
        bool clearRouteOnFailure = true)
    {
        ArgumentNullException.ThrowIfNull(
            route);

        bool published =
            PublishWindow(
                route,
                progressMeters,
                snappedReference,
                mapToArYawDegrees,
                arOriginOffsetX,
                arOriginOffsetZ,
                arWindowMeters,
                ProgressLogTag,
                clearRouteOnFailure);

        if (published)
        {
            AndroidLog.Debug(
                ProgressLogTag,
                "Moving AR route window published: " +
                $"progress={progressMeters:F1} m, " +
                $"window={arWindowMeters:F1} m, " +
                $"arOriginOffset=({arOriginOffsetX:F2}," +
                $"{arOriginOffsetZ:F2}) m");
        }

        return published;
    }

    /// <summary>
    /// Publishes a direct local access connector from the user's current GPS
    /// position to the route-matched road/sidewalk coordinate.
    ///
    /// This is intentionally different from PublishProgressWindow(...): while
    /// the user is not yet verified inside the routed pedestrian corridor, the
    /// cyan visual should point TO the nearest route instead of pretending the
    /// camera is already standing on that road/sidewalk.
    /// </summary>
    public bool PublishApproachToRoute(
        RouteResult route,
        GeoCoordinate userCoordinate,
        GeoCoordinate snappedRouteCoordinate,
        double mapToArYawDegrees,
        float arOriginOffsetX,
        float arOriginOffsetZ,
        bool clearRouteOnFailure = true)
    {
        ArgumentNullException.ThrowIfNull(
            route);

        if (!userCoordinate.IsValid ||
            !snappedRouteCoordinate.IsValid)
        {
            AndroidLog.Warn(
                ProgressLogTag,
                "Approach-to-route connector rejected because GPS or snapped route coordinate is invalid.");

            return false;
        }

        double connectorDistanceMeters =
            userCoordinate.DistanceTo(
                snappedRouteCoordinate);

        if (!double.IsFinite(
                connectorDistanceMeters) ||
            connectorDistanceMeters <=
                0.05)
        {
            return false;
        }

        const double earthRadiusMeters =
            6371008.8;

        double referenceLatitudeRadians =
            userCoordinate.Latitude *
            Math.PI /
            180.0;

        double deltaLatitudeRadians =
            (snappedRouteCoordinate.Latitude -
             userCoordinate.Latitude) *
            Math.PI /
            180.0;

        double deltaLongitudeRadians =
            (snappedRouteCoordinate.Longitude -
             userCoordinate.Longitude) *
            Math.PI /
            180.0;

        double northMeters =
            deltaLatitudeRadians *
            earthRadiusMeters;

        double eastMeters =
            deltaLongitudeRadians *
            earthRadiusMeters *
            Math.Cos(
                referenceLatitudeRadians);

        LocalRoutePoint[] connector =
        [
            new LocalRoutePoint(
                userCoordinate,
                0.0,
                0.0,
                0.0),
            new LocalRoutePoint(
                snappedRouteCoordinate,
                eastMeters,
                northMeters,
                connectorDistanceMeters)
        ];

        IReadOnlyList<ArHorizontalRoutePoint> aligned =
            ArRouteAlignment.Rotate(
                connector,
                mapToArYawDegrees);

        if (aligned.Count <
            2)
        {
            if (clearRouteOnFailure)
            {
                ARRouteBridge.Clear();
            }

            return false;
        }

        ArHorizontalRoutePoint[] shifted =
            new ArHorizontalRoutePoint[
                aligned.Count];

        for (int i = 0;
             i < aligned.Count;
             i++)
        {
            ArHorizontalRoutePoint point =
                aligned[i];

            shifted[i] =
                new ArHorizontalRoutePoint(
                    point.X +
                        arOriginOffsetX,
                    point.Z +
                        arOriginOffsetZ,
                    point.DistanceFromWindowStartMeters);
        }

        ARRouteBridge.Publish(
            shifted,
            route.Algorithm,
            route.TotalDistanceMeters);

        AndroidLog.Debug(
            ProgressLogTag,
            "Approach-to-route AR connector published: " +
            $"distance={connectorDistanceMeters:F1} m, " +
            $"user=({userCoordinate.Latitude:F7},{userCoordinate.Longitude:F7}), " +
            $"route=({snappedRouteCoordinate.Latitude:F7},{snappedRouteCoordinate.Longitude:F7}), " +
            $"arOriginOffset=({arOriginOffsetX:F2},{arOriginOffsetZ:F2}) m");

        return true;
    }

    public void ClearRoute()
    {
        AndroidLog.Debug(
            LogTag,
            "Routing -> AR route clear requested.");

        ARRouteBridge.Clear();
    }

    private static bool PublishWindow(
        RouteResult route,
        double startDistanceMeters,
        GeoCoordinate reference,
        double mapToArYawDegrees,
        float arOriginOffsetX,
        float arOriginOffsetZ,
        double arWindowMeters,
        string logTag,
        bool clearRouteOnFailure)
    {
        IReadOnlyList<LocalRoutePoint> localPoints =
            LocalRouteProjector.ProjectWindow(
                route,
                startDistanceMeters,
                reference,
                arWindowMeters);

        AndroidLog.Debug(
            logTag,
            "Local route window projected: " +
            $"sourcePoints={route.Points.Count}, " +
            $"windowPoints={localPoints.Count}, " +
            $"startDistance={startDistanceMeters:F1} m, " +
            $"reference=({reference.Latitude:F7}," +
            $"{reference.Longitude:F7})");

        if (localPoints.Count <
            2)
        {
            AndroidLog.Warn(
                logTag,
                "Local route window has fewer than two points. " +
                "The user may be at the end of the geometry.");

            if (clearRouteOnFailure)
            {
                ARRouteBridge.Clear();
            }
            else
            {
                AndroidLog.Warn(
                    logTag,
                    "Replacement route window was not publishable; retaining existing AR route.");
            }

            return false;
        }

        IReadOnlyList<ArHorizontalRoutePoint> aligned =
            ArRouteAlignment.Rotate(
                localPoints,
                mapToArYawDegrees);

        if (aligned.Count <
            2)
        {
            if (clearRouteOnFailure)
            {
                ARRouteBridge.Clear();
            }
            else
            {
                AndroidLog.Warn(
                    logTag,
                    "Replacement route window was not publishable; retaining existing AR route.");
            }

            return false;
        }

        ArHorizontalRoutePoint[] shifted =
            new ArHorizontalRoutePoint[
                aligned.Count];

        for (int i = 0;
             i < aligned.Count;
             i++)
        {
            ArHorizontalRoutePoint point =
                aligned[i];

            shifted[i] =
                new ArHorizontalRoutePoint(
                    point.X +
                        arOriginOffsetX,
                    point.Z +
                        arOriginOffsetZ,
                    point.DistanceFromWindowStartMeters);
        }

        AndroidLog.Debug(
            logTag,
            "AR route alignment completed: " +
            $"arPoints={shifted.Length}, " +
            $"mapToArYaw={mapToArYawDegrees:F2} deg");

        ARRouteBridge.Publish(
            shifted,
            route.Algorithm,
            route.TotalDistanceMeters);

        return true;
    }
}
