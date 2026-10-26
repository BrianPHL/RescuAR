using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace RescuAR.AR;

/// <summary>Fits a visible straight road from paired semantic curb observations.
/// These observations are local AR geometry, never GPS or routing evidence.</summary>
public static class RoadSurfaceAlignment
{
    public readonly record struct Boundary(Vector2 Position, bool Cropped = false);
    public readonly record struct RoadLine(Vector2 Center, Vector2 Direction, float Width,
        float Span, float Residual, int Sections);

    // Conservative observation-quality criteria for the local straight-road
    // fit, not claims about model accuracy or a surveyed map position.
    public const float MaximumResidualMeters = .35f;
    public const float MinimumSpanMeters = 8;
    public const float MaximumHeadingCorrectionDegrees = 30;

    public static bool IsQualified(RoadLine line) => line.Sections >= 4 && line.Span >= MinimumSpanMeters &&
        float.IsFinite(line.Residual) && line.Residual <= MaximumResidualMeters &&
        line.Width is >= .8f and <= 15 && float.IsFinite(line.Center.X + line.Center.Y) &&
        float.IsFinite(line.Direction.LengthSquared()) && Math.Abs(line.Direction.LengthSquared() - 1) < .01;

    public static bool TryGroundPoint(float u, float v, ARCameraPoseBridge.SpatialSnapshot frame,
        float zoom, out Vector2 point)
    {
        point = default;
        var p = frame.Projection;
        if (!frame.IsTracking || !frame.Pose.IsTracking || !frame.Anchor.IsAvailable ||
            !p.IsAvailable || !float.IsFinite(u + v + zoom) || u < 0 || u > 1 || v < 0 || v > 1 ||
            zoom < 1 || Math.Abs(p.M11) < .001 || Math.Abs(p.M22) < .001) return false;
        // Semantic images use the unzoomed view. Undo presentation zoom in
        // the camera projection; the resulting world ray is zoom independent.
        var ray = new Vector3((2 * u - 1 + p.M13 / zoom) / (p.M11 / zoom),
            (1 - 2 * v + p.M23 / zoom) / (p.M22 / zoom), -1);
        var pose = frame.Pose;
        var q = new Quaternion(pose.RotationX, pose.RotationY, pose.RotationZ, pose.RotationW);
        if (!float.IsFinite(q.LengthSquared()) || q.LengthSquared() < .0001f) return false;
        ray = Vector3.Transform(ray, Quaternion.Normalize(q));
        if (ray.Y >= -.04f) return false;
        float t = (frame.Anchor.PositionY - pose.PositionY) / ray.Y;
        if (!float.IsFinite(t) || t <= 0) return false;
        var delta = new Vector2(ray.X * t, ray.Z * t);
        if (delta.LengthSquared() < 4 || delta.LengthSquared() > 625) return false;
        point = new Vector2(pose.PositionX, pose.PositionZ) + delta;
        return float.IsFinite(point.X) && float.IsFinite(point.Y);
    }

    public static bool TryFit(IReadOnlyList<Boundary> boundaries, Vector2 camera,
        Vector2 expectedDirection, out RoadLine line, float minimumWidth = 2, float maximumWidth = 15)
    {
        line = default;
        float directionLength = expectedDirection.LengthSquared();
        if (!float.IsFinite(directionLength) || directionLength < .5f || boundaries.Count < 16) return false;
        var forward = Vector2.Normalize(expectedDirection);
        var right = new Vector2(-forward.Y, forward.X);
        var bins = new List<float>[24];
        var clipped = new bool[bins.Length];
        foreach (var boundary in boundaries)
        {
            var delta = boundary.Position - camera;
            float along = Vector2.Dot(delta, forward);
            if (!float.IsFinite(along) || along < -24 || along >= 24) continue;
            int bin = (int)MathF.Floor((along + 24) / 2);
            float lateral = Vector2.Dot(delta, right);
            if (!float.IsFinite(lateral)) continue;
            (bins[bin] ??= new()).Add(lateral);
            clipped[bin] |= boundary.Cropped;
        }
        var centers = new List<Vector2>();
        var widths = new List<float>();
        for (int i = 0; i < bins.Length; i++)
        {
            if (clipped[i] || bins[i] is not { Count: >= 4 } values) continue;
            values.Sort();
            float left = values[values.Count / 10], rightEdge = values[values.Count - 1 - values.Count / 10];
            float width = rightEdge - left;
            if (width < minimumWidth || width > maximumWidth) continue;
            centers.Add(camera + forward * (-23 + i * 2) + right * ((left + rightEdge) * .5f));
            widths.Add(width);
        }
        if (centers.Count < 4) return false;
        float meanWidth = widths.Average();
        if (widths.Max() - widths.Min() > Math.Max(1, meanWidth * .25f)) return false;
        var center = new Vector2(centers.Average(p => p.X), centers.Average(p => p.Y));
        double xx = 0, xy = 0, yy = 0;
        foreach (var p in centers)
        {
            var d = p - center; xx += d.X * d.X; xy += d.X * d.Y; yy += d.Y * d.Y;
        }
        double angle = .5 * Math.Atan2(2 * xy, xx - yy);
        var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        if (Vector2.Dot(direction, forward) < 0) direction = -direction;
        var normal = new Vector2(-direction.Y, direction.X);
        float residual = MathF.Sqrt(centers.Average(p => MathF.Pow(Vector2.Dot(p - center, normal), 2)));
        float span = centers.Max(p => Vector2.Dot(p - center, direction)) -
            centers.Min(p => Vector2.Dot(p - center, direction));
        if (residual > MaximumResidualMeters || span < MinimumSpanMeters ||
            AngleDegrees(forward, direction) > MaximumHeadingCorrectionDegrees) return false;
        line = new(center, direction, meanWidth, span, residual, centers.Count);
        return true;
    }

    public static bool Agrees(RoadLine a, RoadLine b) =>
        AngleDegrees(a.Direction, b.Direction) <= 3 &&
        Math.Abs(Vector2.Dot(b.Center - a.Center, new Vector2(-a.Direction.Y, a.Direction.X))) <= .5f &&
        Math.Abs(a.Width - b.Width) <= Math.Max(1, a.Width * .2f);

    public static bool TryAlign(RoadLine observed, Vector2 mappedWorldReference,
        Vector2 mapEastNorthDirection, double initialYaw, double gpsAccuracy,
        out Vector2 alignedWorldReference, out double alignedYaw)
    {
        alignedWorldReference = default; alignedYaw = double.NaN;
        if (!IsQualified(observed) || !double.IsFinite(initialYaw) || !double.IsFinite(gpsAccuracy) || gpsAccuracy < 0 || gpsAccuracy > 20 ||
            !float.IsFinite(mapEastNorthDirection.LengthSquared()) || mapEastNorthDirection.LengthSquared() < .5f)
            return false;
        double a = initialYaw * Math.PI / 180;
        var map = Vector2.Normalize(mapEastNorthDirection);
        var rough = new Vector2((float)(map.X * Math.Cos(a) - map.Y * Math.Sin(a)),
            (float)(-map.X * Math.Sin(a) - map.Y * Math.Cos(a)));
        var direction = Vector2.Dot(rough, observed.Direction) < 0 ? -observed.Direction : observed.Direction;
        if (AngleDegrees(rough, direction) > MaximumHeadingCorrectionDegrees) return false;
        var normal = new Vector2(-direction.Y, direction.X);
        float offset = Vector2.Dot(mappedWorldReference - observed.Center, normal);
        // A detected road outside the known GPS corridor cannot be silently
        // selected as a different street or used to create a connector.
        if (Math.Abs(offset) > gpsAccuracy + Navigation.Progress.RoutePositionObservationPolicy.MotionAllowanceMeters)
            return false;
        alignedWorldReference = mappedWorldReference - normal * offset;
        alignedYaw = Math.IEEERemainder((Math.Atan2(-map.Y, map.X) -
            Math.Atan2(direction.Y, direction.X)) * 180 / Math.PI, 360);
        return float.IsFinite(alignedWorldReference.X) && float.IsFinite(alignedWorldReference.Y);
    }

    private static double AngleDegrees(Vector2 a, Vector2 b) => Math.Acos(Math.Clamp(
        Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)), -1, 1)) * 180 / Math.PI;
}
