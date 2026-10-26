using System.Numerics;
using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Progress;
using RescuAR.Diagnostics;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private RouteResult? classifiedSurfaceRoute;
    private int classifiedSurfaceSegment = -1;
    private byte classifiedSurfaceLabel;
    private (long Session, long Epoch, long World) visualBootstrapFrame;

    private bool HasVisualRoadRegistration()
    {
        lock (routeProgressFusionSync)
            return activeRoute is { } route && IsMldRoute(route) &&
                GetTrackedRouteMotion(requireTrustedHeading: false) is { } motion &&
                _routeDisplayProgressPolicy.TryGetVisualReference(route, route.Points[0].Coordinate,
                    DateTimeOffset.UtcNow, motion, out _);
    }

    private void TryApplyVisualRoadAlignment()
    {
        lock (routeProgressFusionSync)
        {
            if (activeRoute is not { } route || !IsMldRoute(route) || safeZoneConfirmed)
            {
                ARRoadSurfaceBridge.SetRequest(null);
                return;
            }
            var frame = ARCameraPoseBridge.CurrentFrame;
            var basis = (frame.Generation.SessionGeneration, frame.TrackingEpoch, frame.WorldFrame);
            if (visualBootstrapFrame != basis && _headingAlignmentService.LastResult is { IsStable: false } &&
                latestRouteStartupReading is { } reading &&
                _headingAlignmentService.TryGetIndependentObservation(reading.Coordinate, reading.AltitudeMeters, out var coarse))
            {
                // One coarse direction per changed AR frame is sufficient to
                // select the road axis. Only visual evidence can restore trust.
                activeMapToArYawDegrees = coarse.MapToArYawDegrees;
                visualBootstrapFrame = basis;
            }
            if (GetTrackedRouteMotion(requireTrustedHeading: false) is not { } motion ||
                !RouteDisplayProgressPolicy.TryAtDistance(route, _routeProgressTracker.Current.RawProgressMeters, out var position) ||
                position.SegmentIndex >= route.Points.Count - 1) return;
            var a = route.Points[position.SegmentIndex].Coordinate;
            var b = route.Points[position.SegmentIndex + 1].Coordinate;
            var mapDirection = Vector2.Normalize(new((float)((b.Longitude - a.Longitude) *
                Math.Cos(a.Latitude * Math.PI / 180)), (float)(b.Latitude - a.Latitude)));
            byte label = GetMappedSurfaceLabel(route, position.SegmentIndex);
            if (label == 0)
            {
                ARRoadSurfaceBridge.SetRequest(null);
                ARRoadSurfaceBridge.SetStatus("Mapped path alignment is unavailable — use the map");
                return;
            }
            var rough = MapToArCoordinates.Rotate(mapDirection.X, mapDirection.Y, motion.YawDegrees);
            var request = new ARRoadSurfaceBridge.Request(activeDestinationVersion, route.GeometryFingerprint,
                Vector2.Normalize(new((float)rough.X, (float)rough.Z)), label);
            ARRoadSurfaceBridge.SetRequest(request);
            var observation = ARRoadSurfaceBridge.Current;
            if (observation is null || observation.Owner.Trip != activeDestinationVersion ||
                observation.Owner.Geometry != route.GeometryFingerprint || observation.Evidence < 3 ||
                !observation.IsFor(frame)) return;
            var now = DateTimeOffset.UtcNow;
            if (_routeDisplayProgressPolicy.RenewVisualSupport(route, observation, frame,
                position.RoadCoordinate, mapDirection, now, motion))
            {
                TryPublishReadyMldWindow(route, "MLD/VISUAL-ROAD-SUPPORTED");
                return;
            }
            // A different visible corridor cannot drag an already registered
            // road around. A new frame/route must establish a new reference.
            if (_routeDisplayProgressPolicy.TryGetVisualReference(route, position.RoadCoordinate,
                now, motion, out _, allowContradicted: true))
            {
                ARRoadSurfaceBridge.SetStatus("Checking the mapped road alignment — use the map");
                return;
            }
            if (!_routeDisplayProgressPolicy.TryGetRegisteredReference(route, position.RoadCoordinate,
                now, motion, out var initial) ||
                _routeDisplayProgressPolicy.RegistrationAccuracyMeters is not { } accuracy ||
                !RoadSurfaceAlignment.TryAlign(observation.Line, new(initial.WorldX, initial.WorldZ),
                    mapDirection, motion.YawDegrees, accuracy, out var point, out double yaw) ||
                !_headingAlignmentService.TryApplyVisualRoadYaw(yaw, observation, frame, out var heading)) return;
            var aligned = motion with { YawDegrees = yaw };
            if (!_routeDisplayProgressPolicy.TrySetVisualReference(route, position.RoadCoordinate,
                point, aligned, observation, frame, now)) return;
            activeMapToArYawDegrees = yaw;
            lastHeadingAlignment = heading;
            MldGuidanceBridge.Suppress("applying observed road alignment");
            ARRouteBridge.Clear();
            lastRegisteredPublication = null;
            AndroidLog.Info("RescuAR-ARRoute", $"NAV_VISUAL_REGISTRATION trip={activeDestinationVersion}; " +
                $"geometry={route.GeometryFingerprint}; yaw={yaw:F3}; " +
                $"reference=({point.X:F3},{point.Y:F3}); evidence={observation.Evidence}; " +
                $"span={observation.Line.Span:F2}; fit={observation.Line.Residual:F3}; " +
                $"session={aligned.Session}; epoch={aligned.TrackingEpoch}; world={aligned.WorldFrame}; " +
                "source=LOCAL_ROAD_CORRIDOR; movementRequired=False; alongRoutePosition=GPS_ESTIMATED.");
            TryPublishReadyMldWindow(route, "MLD/VISUAL-ROAD-READY");
        }
    }

    private byte GetMappedSurfaceLabel(RouteResult route, int segment)
    {
        if (ReferenceEquals(classifiedSurfaceRoute, route) && classifiedSurfaceSegment == segment)
            return classifiedSurfaceLabel;
        if (arrivalRoadGraph is null) return 0;
        classifiedSurfaceRoute = route; classifiedSurfaceSegment = segment;
        classifiedSurfaceLabel = 0;
        var a = route.Points[segment].Coordinate; var b = route.Points[segment + 1].Coordinate;
        var middle = new GeoCoordinate((a.Latitude + b.Latitude) / 2, (a.Longitude + b.Longitude) / 2);
        double scale = 111195 * Math.Cos(middle.Latitude * Math.PI / 180);
        double best = .5 * .5;
        foreach (var edge in arrivalRoadGraph.Edges)
        {
            var from = edge.From.Coordinate; var to = edge.To.Coordinate;
            if (middle.Latitude < Math.Min(from.Latitude, to.Latitude) - .00001 ||
                middle.Latitude > Math.Max(from.Latitude, to.Latitude) + .00001 ||
                middle.Longitude < Math.Min(from.Longitude, to.Longitude) - .00001 ||
                middle.Longitude > Math.Max(from.Longitude, to.Longitude) + .00001) continue;
            double x = (from.Longitude - middle.Longitude) * scale, z = (from.Latitude - middle.Latitude) * 111195;
            double dx = (to.Longitude - from.Longitude) * scale, dz = (to.Latitude - from.Latitude) * 111195;
            double length = dx * dx + dz * dz;
            if (length < .01) continue;
            double t = Math.Clamp(-(x * dx + z * dz) / length, 0, 1);
            double error = Math.Pow(x + t * dx, 2) + Math.Pow(z + t * dz, 2);
            if (error >= best) continue;
            best = error;
            classifiedSurfaceLabel = edge.HighwayType switch
            {
                "residential" or "service" or "living_street" or "unclassified" or
                "tertiary" or "secondary" or "primary" => 4,
                "footway" or "cycleway" or "path" => 5,
                _ => 0
            };
        }
        return classifiedSurfaceLabel;
    }
}
