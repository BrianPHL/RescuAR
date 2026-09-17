using RescuAR.Navigation.Projection;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace RescuAR.AR;

/// <summary>
/// Bounded visual policy for camera-relative route width and lightweight
/// nearby depth occlusion. Nearby segments use start/mid/end agreement so one
/// noisy depth pixel cannot hide a complete guidance segment.
/// </summary>
public static class ARRouteVisualPolicy
{
    public const float MaximumOcclusionDistanceMeters =
        10.0f;

    public const float NearCameraOcclusionExemptionMeters =
        1.5f;

    private const float MinimumRouteWidthMeters =
        0.30f;

    private const float MaximumRouteWidthMeters =
        0.65f;

    private const float MinimumWidthDistanceMeters =
        1.25f;

    private const float MaximumWidthDistanceMeters =
        9.0f;

    private const float OcclusionClearanceMeters =
        0.30f;

    private const ushort MinimumTrustedDepthMillimeters =
        180;

    private const ushort MaximumTrustedDepthMillimeters =
        10000;

    private const long MaximumDepthAgeNanoseconds =
        400_000_000L;

    public static float GetRouteWidthMeters(
        float horizontalCameraDistanceMeters)
    {
        if (!float.IsFinite(
                horizontalCameraDistanceMeters))
        {
            return MaximumRouteWidthMeters;
        }

        float progress =
            Math.Clamp(
                (horizontalCameraDistanceMeters -
                 MinimumWidthDistanceMeters) /
                    (MaximumWidthDistanceMeters -
                     MinimumWidthDistanceMeters),
                0.0f,
                1.0f);

        // Smoothstep prevents visible width jumps as the user walks.
        progress =
            progress *
            progress *
            (3.0f -
             2.0f * progress);

        return MinimumRouteWidthMeters +
            (MaximumRouteWidthMeters -
             MinimumRouteWidthMeters) *
            progress;
    }

    public static bool HasNearbyRoute(
        IReadOnlyList<ArHorizontalRoutePoint> points)
    {
        ArgumentNullException.ThrowIfNull(
            points);

        float maximumDistanceSquared =
            MaximumOcclusionDistanceMeters *
            MaximumOcclusionDistanceMeters;

        for (int i = 0;
             i < points.Count;
             i++)
        {
            ArHorizontalRoutePoint point =
                points[i];

            if (float.IsFinite(point.X) &&
                float.IsFinite(point.Z) &&
                point.X * point.X +
                    point.Z * point.Z <=
                        maximumDistanceSquared)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsWorldPointOccluded(
        ARDepthOcclusionBridge.DepthSnapshot depth,
        long currentFrameTimestamp,
        Vector3 worldPoint)
    {
        if (!IsDepthSnapshotUsable(
                depth,
                currentFrameTimestamp))
        {
            return false;
        }

        Quaternion cameraRotation =
            Quaternion.Normalize(
                new Quaternion(
                    depth.RotationX,
                    depth.RotationY,
                    depth.RotationZ,
                    depth.RotationW));

        if (!IsFinite(
                cameraRotation))
        {
            return false;
        }

        Vector3 cameraPosition =
            new(
                depth.CameraX,
                depth.CameraY,
                depth.CameraZ);

        Vector3 cameraPoint =
            Vector3.Transform(
                worldPoint -
                    cameraPosition,
                Quaternion.Conjugate(
                    cameraRotation));

        // ARCore camera coordinates look down -Z.
        float routeDepthMeters =
            -cameraPoint.Z;

        if (!float.IsFinite(routeDepthMeters) ||
            routeDepthMeters <= 0.10f ||
            routeDepthMeters >
                MaximumOcclusionDistanceMeters)
        {
            return false;
        }

        float texturePixelX =
            depth.PrincipalPointX +
            depth.FocalLengthX *
                cameraPoint.X /
                routeDepthMeters;

        float texturePixelY =
            depth.PrincipalPointY -
            depth.FocalLengthY *
                cameraPoint.Y /
                routeDepthMeters;

        if (!float.IsFinite(texturePixelX) ||
            !float.IsFinite(texturePixelY) ||
            texturePixelX < 0.0f ||
            texturePixelX >= depth.TextureWidth ||
            texturePixelY < 0.0f ||
            texturePixelY >= depth.TextureHeight)
        {
            return false;
        }

        int centerX =
            Math.Clamp(
                (int)(texturePixelX /
                    depth.TextureWidth *
                    depth.Width),
                0,
                depth.Width -
                    1);

        int centerY =
            Math.Clamp(
                (int)(texturePixelY /
                    depth.TextureHeight *
                    depth.Height),
                0,
                depth.Height -
                    1);

        if (!TryGetMedianDepthMeters(
                depth,
                centerX,
                centerY,
                out float environmentDepthMeters))
        {
            return false;
        }

        return environmentDepthMeters +
                OcclusionClearanceMeters <
            routeDepthMeters;
    }

    public static bool IsDepthSnapshotUsable(
        ARDepthOcclusionBridge.DepthSnapshot depth,
        long currentFrameTimestamp)
    {
        return depth.IsAvailable &&
            currentFrameTimestamp > 0 &&
            depth.FrameTimestamp > 0 &&
            Math.Abs(
                currentFrameTimestamp -
                depth.FrameTimestamp) <=
                    MaximumDepthAgeNanoseconds &&
            depth.Width > 0 &&
            depth.Height > 0 &&
            depth.TextureWidth > 0 &&
            depth.TextureHeight > 0 &&
            depth.DepthMillimeters.Length >=
                depth.Width * depth.Height;
    }

    public static bool IsWorldSegmentOccluded(
        ARDepthOcclusionBridge.DepthSnapshot depth,
        long currentFrameTimestamp,
        Vector3 worldStart,
        Vector3 worldMidpoint,
        Vector3 worldEnd)
    {
        int occludedSampleCount =
            0;

        if (IsWorldPointOccluded(
                depth,
                currentFrameTimestamp,
                worldStart))
        {
            occludedSampleCount++;
        }

        if (IsWorldPointOccluded(
                depth,
                currentFrameTimestamp,
                worldMidpoint))
        {
            occludedSampleCount++;
        }

        if (IsWorldPointOccluded(
                depth,
                currentFrameTimestamp,
                worldEnd))
        {
            occludedSampleCount++;
        }

        return occludedSampleCount >=
            2;
    }

    private static bool TryGetMedianDepthMeters(
        ARDepthOcclusionBridge.DepthSnapshot depth,
        int centerX,
        int centerY,
        out float depthMeters)
    {
        Span<ushort> samples =
            stackalloc ushort[5];

        int count =
            0;

        AddTrustedSample(
            depth,
            centerX,
            centerY,
            samples,
            ref count);

        AddTrustedSample(
            depth,
            centerX -
                1,
            centerY,
            samples,
            ref count);

        AddTrustedSample(
            depth,
            centerX +
                1,
            centerY,
            samples,
            ref count);

        AddTrustedSample(
            depth,
            centerX,
            centerY -
                1,
            samples,
            ref count);

        AddTrustedSample(
            depth,
            centerX,
            centerY +
                1,
            samples,
            ref count);

        if (count < 3)
        {
            depthMeters =
                0.0f;

            return false;
        }

        samples[..count].Sort();

        depthMeters =
            samples[count /
                2] *
            0.001f;

        return true;
    }

    private static void AddTrustedSample(
        ARDepthOcclusionBridge.DepthSnapshot depth,
        int x,
        int y,
        Span<ushort> samples,
        ref int count)
    {
        if (x < 0 ||
            x >= depth.Width ||
            y < 0 ||
            y >= depth.Height)
        {
            return;
        }

        ushort sample =
            depth.DepthMillimeters[
                y * depth.Width +
                x];

        if (sample >=
                MinimumTrustedDepthMillimeters &&
            sample <=
                MaximumTrustedDepthMillimeters)
        {
            samples[count++] =
                sample;
        }
    }

    private static bool IsFinite(
        Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}
