using System;
using System.Collections.Generic;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

namespace RescuAR.AR;

public enum RouteAnchorRecoveryAction { WaitForFloor, DiscardPreview, RebaseRoute }

/// <summary>An unpublished approach route must not block a tracked anchor handoff.</summary>
public static class RouteAnchorRecoveryPolicy
{
    public static RouteAnchorRecoveryAction Evaluate(bool roadFollowingConfirmed,
        bool freshTracking, bool coherentGround) => !freshTracking || !coherentGround
            ? RouteAnchorRecoveryAction.WaitForFloor
            : roadFollowingConfirmed ? RouteAnchorRecoveryAction.RebaseRoute
            : RouteAnchorRecoveryAction.DiscardPreview;

    /// <summary>Changes the local origin while preserving every world-space X/Z point.</summary>
    public static bool TryRebaseWindow(ARRouteBridge.RouteSnapshot route,
        ARCameraPoseBridge.SpatialSnapshot frame, RouteBacktrackingPolicy.TrackedMotion from,
        RouteBacktrackingPolicy.TrackedMotion to, DateTimeOffset now,
        out IReadOnlyList<ArHorizontalRoutePoint> points, out ARRoutePlacement placement)
    {
        points = Array.Empty<ArHorizontalRoutePoint>();
        placement = default;
        var source = route.Placement;
        if (!route.IsAvailable || route.NavigationState.VisualKind != RouteVisualKind.RouteWindow ||
            !source.IsAvailable || !frame.IsFresh || !frame.IsTracking ||
            !frame.Pose.IsTracking || !frame.Anchor.IsAvailable || !from.IsContinuousWith(to) ||
            to.Session != frame.Generation.SessionGeneration || source.Generation.SessionGeneration != to.Session ||
            source.TrackingEpoch != to.TrackingEpoch || source.WorldFrame != to.WorldFrame ||
            !source.ObservationTime.HasValue || now < source.ObservationTime.Value ||
            now - source.ObservationTime.Value > TimeSpan.FromSeconds(RouteDisplayProgressPolicy.MaximumPdrGpsAgeSeconds) ||
            !double.IsFinite(source.YawDegrees) ||
            Math.Abs(Math.IEEERemainder(source.YawDegrees - to.YawDegrees, 360)) > 0.01 ||
            LocalArNavigationPolicy.GetHorizontalDistanceMeters(to.X - from.X, to.Z - from.Z) >
                RouteDisplayProgressPolicy.MaximumPdrAdvanceMeters ||
            !LocalArNavigationPolicy.IsGroundReferenceLocal(frame.Pose.PositionX, frame.Pose.PositionZ,
                frame.Anchor.PositionX, frame.Anchor.PositionZ) ||
            !LocalArNavigationPolicy.IsCameraHeightAboveGroundPlausible(frame.Pose.PositionY, frame.Anchor.PositionY, out _))
            return false;
        var copy = new ArHorizontalRoutePoint[route.Points.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            var point = route.Points[i];
            copy[i] = point with { X = point.X + source.AnchorX - frame.Anchor.PositionX,
                Z = point.Z + source.AnchorZ - frame.Anchor.PositionZ };
            if (!float.IsFinite(copy[i].X) || !float.IsFinite(copy[i].Z)) return false;
        }
        points = copy;
        placement = source with { Generation = frame.Generation, GroundReference = frame.Anchor.ReferenceGeneration,
            SpatialVersion = frame.Version, PublishedAt = Environment.TickCount64,
            AnchorX = frame.Anchor.PositionX, AnchorZ = frame.Anchor.PositionZ,
            CameraX = frame.Pose.PositionX, CameraZ = frame.Pose.PositionZ,
            Source = "GROUND-HANDOFF", PosePublishedAt = frame.PublishedAtMonotonicMilliseconds,
            FrameTimestamp = frame.FrameTimestamp,
            AnchorRotation = frame.Anchor.HasRotation ? frame.Anchor.Rotation : null };
        return true;
    }
}
