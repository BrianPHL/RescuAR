using RescuAR.AR;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    // Physical display references are separate from monotonic trip progress.
    // They never become routing origins, device locations or arrival evidence.
    private VerifiedPosition? verifiedPosition;

    private sealed record VerifiedPosition(RouteResult Route, DateTimeOffset At,
        double Accuracy, double Progress, RouteBacktrackingPolicy.TrackedMotion? Motion);

    private RouteBacktrackingPolicy.TrackedMotion? GetTrackedRouteMotion()
    {
        var frame = ARCameraPoseBridge.CurrentFrame;
        if (!frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking ||
            !frame.Anchor.IsAvailable || !HasCurrentRouteHeading() ||
            !ARRenderGenerationBridge.IsCurrentSession(frame.Generation) ||
            ARCameraSpatialController.CurrentSpatialContinuity.State !=
                ARCameraSpatialController.SpatialContinuityState.Live ||
            ARCameraSpatialController.CurrentRouteWorldCorrectionRequest.IsPending)
            return null;

        var motion = new RouteBacktrackingPolicy.TrackedMotion(frame.Pose.PositionX,
            frame.Pose.PositionZ, activeMapToArYawDegrees, frame.Generation.SessionGeneration,
            frame.Anchor.ReferenceGeneration);
        return motion.IsValid ? motion : null;
    }

    // The caller first applies the existing fusion confidence/identity gates.
    private void ObserveVerifiedGpsPosition(RouteResult route,
        RouteProgressTracker.RouteProgressUpdate update, LocationReading reading)
    {
        lock (routeProgressFusionSync)
        {
            if (!ReferenceEquals(route, activeRoute) ||
                (verifiedPosition is { } previous && ReferenceEquals(previous.Route, route) &&
                 reading.Timestamp <= previous.At) ||
                !RouteDisplayProgressPolicy.TryResolveGps(route, update, out _)) return;
            _routeDisplayProgressPolicy.ObserveGps(route, update, reading.Timestamp);
            TimeSpan age = DateTimeOffset.UtcNow - reading.Timestamp;
            verifiedPosition = new(route, reading.Timestamp, reading.AccuracyMeters!.Value,
                update.CommittedProgressMeters,
                age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(3) ? GetTrackedRouteMotion() : null);
        }
    }

    private bool TryGetBoundedDisplayPosition(RouteResult route, double committedProgress,
        DateTimeOffset now, out RouteDisplayProgressPolicy.DisplayPosition position,
        out double accuracy)
    {
        lock (routeProgressFusionSync)
        {
            position = default;
            accuracy = double.NaN;
            if (!ReferenceEquals(activeRoute, route) || verifiedPosition is not { } previous ||
                !ReferenceEquals(previous.Route, route) || now < previous.At ||
                now - previous.At > MaximumPdrRouteWindowGpsAge ||
                committedProgress < previous.Progress ||
                committedProgress - previous.Progress > MaximumPdrRouteWindowAdvanceMeters ||
                dynamicRerouteInProgress || safeZoneConfirmed ||
                _gpsPdrFusionPolicy.IsRouteIdentitySuspended || lastOffRouteCandidate ||
                previous.Motion is not { } from || GetTrackedRouteMotion() is not { } to ||
                from.Session != to.Session || from.GroundReference != to.GroundReference ||
                Math.Abs(Math.IEEERemainder(from.YawDegrees - to.YawDegrees, 360)) > 8)
                return false;

            double dx = to.X - from.X, dz = to.Z - from.Z;
            if (dx * dx + dz * dz > MaximumPdrRouteWindowAdvanceMeters * MaximumPdrRouteWindowAdvanceMeters ||
                !_routeDisplayProgressPolicy.TryResolvePdr(route, committedProgress, now, out position))
                return false;
            accuracy = previous.Accuracy;
            return true;
        }
    }
}
