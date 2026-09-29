using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
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

        return PublishInitialRoute(
                route,
                mapToArYawDegrees,
                arWindowMeters,
                userCoordinate: origin)
            ? route
            : null;
    }

    /// <summary>
    /// Publishes geometry calculated alongside heading acquisition. Until
    /// both are ready, no route is published with an unverified yaw.
    /// </summary>
    public bool PublishInitialRoute(
        RouteResult? route,
        double mapToArYawDegrees,
        double arWindowMeters = 7.5,
        GeoCoordinate? userCoordinate = null,
        float arOriginOffsetX = 0.0f,
        float arOriginOffsetZ = 0.0f)
    {
        if (!IsCameraOffsetAcceptable(arOriginOffsetX, arOriginOffsetZ))
            return false;
        if (route is null || route.Points.Count < 2)
        {
            AndroidLog.Warn(
                LogTag,
                "Routing -> AR integration produced no usable route; " +
                "clearing AR route.");

            ARRouteBridge.Clear();

            return false;
        }

        GeoCoordinate initialReference =
            route.Points[0]
                .Coordinate;

        if (userCoordinate.HasValue &&
            !TryApplyGpsToRouteOffset(userCoordinate.Value, initialReference,
                mapToArYawDegrees, ref arOriginOffsetX, ref arOriginOffsetZ))
        {
            ARRouteBridge.Clear();
            return false;
        }

        bool published =
            PublishWindow(
                route,
                route.Points[0]
                    .DistanceFromStartMeters,
                initialReference,
                mapToArYawDegrees,
                arOriginOffsetX:
                    arOriginOffsetX,
                arOriginOffsetZ:
                    arOriginOffsetZ,
                arWindowMeters:
                    arWindowMeters,
                sourceSegmentIndex:
                    0,
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

        return published;
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
        bool clearRouteOnFailure = true,
        int sourceSegmentIndex = -1,
        GeoCoordinate? userCoordinate = null)
    {
        ArgumentNullException.ThrowIfNull(
            route);
        if (!IsCameraOffsetAcceptable(arOriginOffsetX, arOriginOffsetZ))
            return false;
        if (userCoordinate.HasValue &&
            !TryApplyGpsToRouteOffset(userCoordinate.Value, snappedReference,
                mapToArYawDegrees, ref arOriginOffsetX, ref arOriginOffsetZ))
        {
            if (clearRouteOnFailure) ARRouteBridge.Clear();
            return false;
        }

        bool published =
            PublishWindow(
                route,
                progressMeters,
                snappedReference,
                mapToArYawDegrees,
                arOriginOffsetX,
                arOriginOffsetZ,
                arWindowMeters,
                ResolveSourceSegmentIndex(
                    route,
                    progressMeters,
                    sourceSegmentIndex),
                ProgressLogTag,
                clearRouteOnFailure);

        if (published)
        {
            AndroidLog.Debug(
                ProgressLogTag,
                "Moving AR route window published: " +
                $"progress={progressMeters:F1} m, " +
                $"sourceSegment=" +
                $"{ResolveSourceSegmentIndex(route, progressMeters, sourceSegmentIndex)}, " +
                $"window={arWindowMeters:F1} m, " +
                $"arOriginOffset=({arOriginOffsetX:F2}," +
                $"{arOriginOffsetZ:F2}) m");
        }

        return published;
    }

    /// <summary>
    /// Deliberately rejects straight GPS-to-graph connectors. The source data
    /// does not prove that the space between the user and the mapped line is
    /// walkable; a connector can cross a building or live traffic.
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
        AndroidLog.Warn(ProgressLogTag,
            "Unsurveyed GPS-to-route cyan connector refused; use 2D map guidance.");
        return false;
    }

    public void ClearRoute()
    {
        AndroidLog.Debug(
            LogTag,
            "Routing -> AR route clear requested.");

        ARRouteBridge.Clear();
    }

    private static bool TryApplyGpsToRouteOffset(
        GeoCoordinate user, GeoCoordinate routePoint, double yawDegrees,
        ref float arOffsetX, ref float arOffsetZ)
    {
        if (!user.IsValid || !routePoint.IsValid ||
            !double.IsFinite(yawDegrees) ||
            user.DistanceTo(routePoint) > 20.0)
        {
            AndroidLog.Warn(ProgressLogTag,
                "GPS-to-graph displacement too uncertain for an AR route origin.");
            return false;
        }
        double latitude = user.Latitude * Math.PI / 180.0;
        double east = (routePoint.Longitude - user.Longitude) *
            Math.PI / 180.0 * 6371008.8 * Math.Cos(latitude);
        double north = (routePoint.Latitude - user.Latitude) *
            Math.PI / 180.0 * 6371008.8;
        double yaw = yawDegrees * Math.PI / 180.0;
        arOffsetX += (float)(east * Math.Cos(yaw) + north * Math.Sin(yaw));
        arOffsetZ += (float)(-east * Math.Sin(yaw) + north * Math.Cos(yaw));
        return LocalArNavigationPolicy.IsRouteOriginOffsetAcceptable(
            arOffsetX, arOffsetZ, out _);
    }

    private static bool IsCameraOffsetAcceptable(float x, float z) =>
        float.IsFinite(x) && float.IsFinite(z) &&
        MathF.Sqrt(x * x + z * z) <=
            LocalArNavigationPolicy.MaximumCameraToAnchorOffsetMeters;

    private static bool PublishWindow(
        RouteResult route,
        double startDistanceMeters,
        GeoCoordinate reference,
        double mapToArYawDegrees,
        float arOriginOffsetX,
        float arOriginOffsetZ,
        double arWindowMeters,
        int sourceSegmentIndex,
        string logTag,
        bool clearRouteOnFailure)
    {
        if (!ValidateLocalRouteOriginOffset(
                arOriginOffsetX,
                arOriginOffsetZ,
                logTag,
                clearRouteOnFailure))
        {
            return false;
        }

        if (!double.IsFinite(
                arWindowMeters) ||
            arWindowMeters <=
                0.0)
        {
            AndroidLog.Warn(
                logTag,
                $"AR route publication rejected an invalid local window: " +
                $"{arWindowMeters} m.");

            if (clearRouteOnFailure)
            {
                ARRouteBridge.Clear();
            }

            return false;
        }

        double boundedWindowMeters =
            Math.Min(
                arWindowMeters,
                LocalArNavigationPolicy.MaximumVisibleWindowMeters);

        if (boundedWindowMeters <
            arWindowMeters)
        {
            AndroidLog.Warn(
                logTag,
                "AR route window was capped by the moving-local-frame policy: " +
                $"requested={arWindowMeters:F1} m, " +
                $"applied={boundedWindowMeters:F1} m.");
        }

        ArHorizontalRoutePoint[]? shifted = null;
        double attemptedWindowMeters = boundedWindowMeters;
        while (true)
        {
            IReadOnlyList<LocalRoutePoint> localPoints =
                LocalRouteProjector.ProjectWindow(
                    route, startDistanceMeters, reference,
                    attemptedWindowMeters);
            if (localPoints.Count < 2) break;

            IReadOnlyList<ArHorizontalRoutePoint> aligned =
                ArRouteAlignment.Rotate(localPoints, mapToArYawDegrees);
            if (aligned.Count < 2) break;

            ArHorizontalRoutePoint[] candidate = new ArHorizontalRoutePoint[aligned.Count];
            for (int i = 0; i < aligned.Count; i++)
            {
                ArHorizontalRoutePoint point = aligned[i];
                candidate[i] = new ArHorizontalRoutePoint(
                    point.X + arOriginOffsetX,
                    point.Z + arOriginOffsetZ,
                    point.DistanceFromWindowStartMeters);
            }

            var prepared = ARRouteGeometrySanitizer.Prepare(
                candidate, ARRouteRenderer.MaximumRouteSegments + 1);
            if (prepared.Points.Count >= 2 &&
                prepared.Points.Count <= ARRouteRenderer.MaximumRouteSegments + 1 &&
                prepared.FirstPointPreserved && prepared.FinalPointPreserved)
            {
                shifted = candidate;
                break;
            }

            if (attemptedWindowMeters <=
                LocalArNavigationPolicy.ApproachWindowMeters + 0.01)
                break;
            attemptedWindowMeters = Math.Max(
                LocalArNavigationPolicy.ApproachWindowMeters,
                attemptedWindowMeters / 2.0);
        }

        if (shifted is null)
        {
            AndroidLog.Warn(logTag,
                "No source-preserving route window fits the AR renderer; use the 2D map.");
            if (clearRouteOnFailure) ARRouteBridge.Clear();
            return false;
        }

        if (attemptedWindowMeters < boundedWindowMeters)
            AndroidLog.Warn(logTag,
                $"Dense mapped route shortened to {attemptedWindowMeters:F1} m to preserve every corner.");

        AndroidLog.Debug(
            logTag,
            "AR route alignment completed: " +
            $"arPoints={shifted.Length}, " +
            $"mapToArYaw={mapToArYawDegrees:F2} deg");

        ARRouteBridge.Publish(
            shifted,
            route.Algorithm,
            route.TotalDistanceMeters,
            RouteVisualKind.RouteWindow,
            startDistanceMeters,
            sourceSegmentIndex);

        return true;
    }

    private static int ResolveSourceSegmentIndex(
        RouteResult route,
        double progressMeters,
        int requestedSegmentIndex)
    {
        int maximumSegmentIndex =
            route.Points.Count -
            2;

        if (maximumSegmentIndex <
            0)
        {
            return -1;
        }

        if (requestedSegmentIndex >=
                0 &&
            requestedSegmentIndex <=
                maximumSegmentIndex)
        {
            return requestedSegmentIndex;
        }

        for (int i = 0;
             i <=
                maximumSegmentIndex;
             i++)
        {
            if (progressMeters <=
                route.Points[i + 1]
                    .DistanceFromStartMeters)
            {
                return i;
            }
        }

        return maximumSegmentIndex;
    }

    private static bool ValidateLocalRouteOriginOffset(
        float arOriginOffsetX,
        float arOriginOffsetZ,
        string logTag,
        bool clearRouteOnFailure)
    {
        if (LocalArNavigationPolicy.IsRouteOriginOffsetAcceptable(
                arOriginOffsetX,
                arOriginOffsetZ,
                out float offsetDistanceMeters))
        {
            return true;
        }

        AndroidLog.Warn(
            logTag,
            "AR route publication blocked by the moving-local-frame guard: " +
            $"originOffset=({arOriginOffsetX:F2},{arOriginOffsetZ:F2}) m, " +
            $"horizontalDistance={offsetDistanceMeters:F2} m, " +
            $"maximum=" +
            $"{LocalArNavigationPolicy.MaximumRouteOriginOffsetMeters:F1} m. " +
            "Waiting for a nearby ground-anchor replacement instead of " +
            "publishing a city-scale local offset.");

        if (clearRouteOnFailure)
        {
            ARRouteBridge.Clear();
        }

        return false;
    }
}
