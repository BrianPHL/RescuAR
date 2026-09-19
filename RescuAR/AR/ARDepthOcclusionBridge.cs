using System;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Thread-safe handoff for ARCore's smoothed 16-bit depth image.
///
/// The Android ARCore worker copies the current depth image together with the
/// physical camera pose, texture intrinsics, display-geometry UV transform,
/// and ground Y from the SAME ARCore frame. Ground metadata identifies whether
/// the current baseline is provisional or verified. The MAUI flood
/// visualization reads one immutable snapshot and never touches Android/ARCore
/// objects directly.
/// </summary>
public static class ARDepthOcclusionBridge
{
    public const long MaximumDepthAgeMilliseconds = 750;

    private static readonly object sync = new();

    private static long version;

    private static DepthSnapshot current = DepthSnapshot.Unavailable;

    public static DepthSnapshot Current
    {
        get
        {
            DepthSnapshot snapshot;

            lock (sync)
            {
                snapshot = current;
            }

            if (!ARRenderGenerationBridge.IsCurrent(
                    snapshot.Generation) ||
                !snapshot.IsFresh)
            {
                return DepthSnapshot.UnavailableWithVersion(
                    snapshot.Version);
            }

            return snapshot;
        }
    }

    public static void Publish(
        ARFrameMetadata metadata,
        int width,
        int height,
        ushort[] depthMillimeters,
        float[] viewToTextureUv,
        float cameraX,
        float cameraY,
        float cameraZ,
        float rotationX,
        float rotationY,
        float rotationZ,
        float rotationW,
        float focalLengthX,
        float focalLengthY,
        float principalPointX,
        float principalPointY,
        int textureWidth,
        int textureHeight,
        bool groundAvailable,
        bool groundIsProvisional,
        float groundWorldY)
    {
        ArgumentNullException.ThrowIfNull(depthMillimeters);
        ArgumentNullException.ThrowIfNull(viewToTextureUv);

        if (!ARRenderGenerationBridge.TryAcceptCallback(
                metadata.Generation,
                "depth-publish") ||
            !metadata.IsValid ||
            width <= 0 ||
            height <= 0 ||
            depthMillimeters.Length < width * height ||
            viewToTextureUv.Length < 8 ||
            !float.IsFinite(focalLengthX) ||
            !float.IsFinite(focalLengthY) ||
            focalLengthX <= 0.0f ||
            focalLengthY <= 0.0f ||
            textureWidth <= 0 ||
            textureHeight <= 0)
        {
            return;
        }

        long nextVersion = Interlocked.Increment(ref version);

        DepthSnapshot next = new(
            nextVersion,
            metadata,
            Environment.TickCount64,
            true,
            width,
            height,
            depthMillimeters,
            viewToTextureUv,
            cameraX,
            cameraY,
            cameraZ,
            rotationX,
            rotationY,
            rotationZ,
            rotationW,
            focalLengthX,
            focalLengthY,
            principalPointX,
            principalPointY,
            textureWidth,
            textureHeight,
            groundAvailable,
            groundIsProvisional,
            groundWorldY);

        lock (sync)
        {
            current = next;
        }
    }

    public static void Clear()
    {
        long nextVersion = Interlocked.Increment(ref version);

        lock (sync)
        {
            current = DepthSnapshot.UnavailableWithVersion(nextVersion);
        }
    }

    public readonly struct DepthSnapshot
    {
        public static DepthSnapshot Unavailable => UnavailableWithVersion(0);

        public static DepthSnapshot UnavailableWithVersion(long version) =>
            new(
                version,
                ARFrameMetadata.Invalid,
                long.MinValue,
                false,
                0,
                0,
                Array.Empty<ushort>(),
                Array.Empty<float>(),
                0, 0, 0,
                0, 0, 0, 1,
                0, 0,
                0, 0,
                0, 0,
                false,
                false,
                0);

        public DepthSnapshot(
            long version,
            ARFrameMetadata metadata,
            long publishedAtMonotonicMilliseconds,
            bool isAvailable,
            int width,
            int height,
            ushort[] depthMillimeters,
            float[] viewToTextureUv,
            float cameraX,
            float cameraY,
            float cameraZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW,
            float focalLengthX,
            float focalLengthY,
            float principalPointX,
            float principalPointY,
            int textureWidth,
            int textureHeight,
            bool groundAvailable,
            bool groundIsProvisional,
            float groundWorldY)
        {
            Version = version;
            Metadata = metadata;
            PublishedAtMonotonicMilliseconds =
                publishedAtMonotonicMilliseconds;
            IsAvailable = isAvailable;
            Width = width;
            Height = height;
            DepthMillimeters = depthMillimeters;
            ViewToTextureUv = viewToTextureUv;
            CameraX = cameraX;
            CameraY = cameraY;
            CameraZ = cameraZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
            FocalLengthX = focalLengthX;
            FocalLengthY = focalLengthY;
            PrincipalPointX = principalPointX;
            PrincipalPointY = principalPointY;
            TextureWidth = textureWidth;
            TextureHeight = textureHeight;
            GroundAvailable = groundAvailable;
            GroundIsProvisional = groundIsProvisional;
            GroundWorldY = groundWorldY;
        }

        public long Version { get; }
        public ARFrameMetadata Metadata { get; }
        public ARRenderGenerationToken Generation => Metadata.Generation;
        public long FrameTimestamp => Metadata.FrameTimestamp;
        public ARDisplayGeometrySnapshot DisplayGeometry =>
            Metadata.DisplayGeometry;
        public long PublishedAtMonotonicMilliseconds { get; }
        public bool IsAvailable { get; }
        public int Width { get; }
        public int Height { get; }
        public ushort[] DepthMillimeters { get; }
        public float[] ViewToTextureUv { get; }

        // Physical ARCore camera pose. This intentionally uses Camera.Pose,
        // not DisplayOrientedPose, because the depth/texture intrinsics are in
        // the unrotated camera texture coordinate system.
        public float CameraX { get; }
        public float CameraY { get; }
        public float CameraZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }

        public float FocalLengthX { get; }
        public float FocalLengthY { get; }
        public float PrincipalPointX { get; }
        public float PrincipalPointY { get; }
        public int TextureWidth { get; }
        public int TextureHeight { get; }

        public bool GroundAvailable { get; }
        public bool GroundIsProvisional { get; }
        public float GroundWorldY { get; }

        public bool IsFresh =>
            PublishedAtMonotonicMilliseconds != long.MinValue &&
            Environment.TickCount64 - PublishedAtMonotonicMilliseconds >= 0 &&
            Environment.TickCount64 - PublishedAtMonotonicMilliseconds <=
                MaximumDepthAgeMilliseconds;
    }
}
