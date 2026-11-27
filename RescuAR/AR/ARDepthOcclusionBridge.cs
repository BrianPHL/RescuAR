using RescuAR.Diagnostics;
using System;
using System.Buffers;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Thread-safe handoff for ARCore's smoothed 16-bit depth image.
/// The publisher transfers ownership of a rented depth buffer to this bridge.
/// A superseded buffer returns to the pool after its last reader releases it.
/// </summary>
public static class ARDepthOcclusionBridge
{
    public const long MaximumDepthAgeMilliseconds = 750;

    private const string LogTag = "RescuAR-DepthBuffers";

    private static readonly object sync = new();

    private static long version;
    private static long rentedBufferCount;
    private static long returnedBufferCount;
    private static long retiredBufferCount;
    private static long peakRetiredBufferCount;
    private static long lastMetricsLogTimestamp = long.MinValue;

    private static PublishedDepth? current;

    public static bool HasCurrentDepth
    {
        get
        {
            lock (sync)
                return GetCurrentLocked() is not null;
        }
    }

    /// <summary>The number of published pooled arrays still owned by the bridge or a reader.</summary>
    public static long OutstandingBufferCount
    {
        get { lock (sync) return rentedBufferCount - returnedBufferCount; }
    }

    internal static DepthReadLease? Acquire()
    {
        lock (sync)
        {
            var publication = GetCurrentLocked();
            if (publication is null) return null;
            publication.ReaderCount++;
            return new DepthReadLease(publication);
        }
    }

    /// <summary>
    /// Publishes a rented depth array. Returns true only when ownership was
    /// accepted; on false the caller remains responsible for returning it.
    /// The native timestamp must identify the exact pose and optics of the image.
    /// </summary>
    public static bool Publish(
        long imageTimestamp,
        in ARCameraPoseBridge.SpatialSnapshot spatial,
        int width,
        int height,
        ushort[] rentedDepthMillimeters,
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
        int textureHeight)
    {
        ArgumentNullException.ThrowIfNull(rentedDepthMillimeters);
        ArgumentNullException.ThrowIfNull(viewToTextureUv);

        var metadata = spatial.Metadata;
        var groundState = spatial.Anchor.GroundState;
        float groundWorldY = spatial.Anchor.IsAvailable ? spatial.Anchor.PositionY : 0.0f;
        if (imageTimestamp <= 0 || imageTimestamp != metadata.FrameTimestamp ||
            !ARCameraPoseBridge.IsCurrentTrackingFrame(spatial) ||
            width <= 0 ||
            height <= 0 ||
            rentedDepthMillimeters.LongLength < (long)width * height ||
            viewToTextureUv.Length < 8 ||
            !float.IsFinite(cameraX) || !float.IsFinite(cameraY) || !float.IsFinite(cameraZ) ||
            !float.IsFinite(rotationX) || !float.IsFinite(rotationY) ||
            !float.IsFinite(rotationZ) || !float.IsFinite(rotationW) ||
            (double)rotationX * rotationX + (double)rotationY * rotationY +
                (double)rotationZ * rotationZ + (double)rotationW * rotationW < 0.0001 ||
            !float.IsFinite(principalPointX) || !float.IsFinite(principalPointY) ||
            !float.IsFinite(groundWorldY) ||
            (groundState.HasGroundReference && groundState.RenderGeneration != metadata.Generation) ||
            !float.IsFinite(focalLengthX) ||
            !float.IsFinite(focalLengthY) ||
            focalLengthX <= 0.0f ||
            focalLengthY <= 0.0f ||
            textureWidth <= 0 ||
            textureHeight <= 0)
        {
            return false;
        }

        for (int i = 0; i < 8; i++)
            if (!float.IsFinite(viewToTextureUv[i])) return false;

        long now = Environment.TickCount64;
        lock (sync)
        {
            if (!metadata.IsFresh(MaximumDepthAgeMilliseconds, now) ||
                !ARRenderGenerationBridge.IsCurrent(metadata.Generation)) return false;

            DepthSnapshot next = new(
                ++version,
                metadata,
                spatial.TrackingEpoch,
                spatial.WorldFrame,
                width,
                height,
                rentedDepthMillimeters,
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
                groundState,
                groundWorldY);

            RetireCurrentBufferLocked();
            current = new PublishedDepth(next);
            rentedBufferCount++;
            LogBufferMetricsIfNeededLocked(now);
        }

        return true;
    }

    public static void Clear(long? expectedVersion = null)
    {
        long now = Environment.TickCount64;

        lock (sync)
        {
            if (expectedVersion.HasValue && current?.Snapshot.Version != expectedVersion.Value) return;
            RetireCurrentBufferLocked();
            LogBufferMetricsIfNeededLocked(now);
        }
    }

    private static PublishedDepth? GetCurrentLocked()
    {
        if (current is { } publication &&
            (!publication.Snapshot.IsFresh || !ARRenderGenerationBridge.IsCurrent(publication.Snapshot.Generation)))
        {
            RetireCurrentBufferLocked();
        }
        return current;
    }

    private static void RetireCurrentBufferLocked()
    {
        if (current is not { } publication) return;
        current = null;
        publication.Retired = true;
        if (publication.ReaderCount == 0)
            ReturnBufferLocked(publication);
        else
        {
            retiredBufferCount++;
            peakRetiredBufferCount = Math.Max(peakRetiredBufferCount, retiredBufferCount);
        }
    }

    private static void ReturnBufferLocked(PublishedDepth publication)
    {
        ArrayPool<ushort>.Shared.Return(publication.Snapshot.RentedDepthMillimeters!, clearArray: false);
        returnedBufferCount++;
    }

    private static void LogBufferMetricsIfNeededLocked(long now)
    {
        if (lastMetricsLogTimestamp != long.MinValue &&
            now - lastMetricsLogTimestamp < 30_000)
        {
            return;
        }

        lastMetricsLogTimestamp = now;

        AndroidLog.Info(
            LogTag,
            "ARCORE_DEPTH_BUFFER_METRICS " +
            $"rented={rentedBufferCount}; returned={returnedBufferCount}; " +
            $"retired={retiredBufferCount}; peakRetired={peakRetiredBufferCount}; " +
            $"active={(current is not null ? 1 : 0)}.");
    }

    internal sealed class PublishedDepth(DepthSnapshot snapshot)
    {
        internal DepthSnapshot Snapshot { get; } = snapshot;
        internal int ReaderCount;
        internal bool Retired;
    }

    /// <summary>Keep this lease alive for the whole operation that reads its snapshot's pixels.</summary>
    public sealed class DepthReadLease : IDisposable
    {
        private PublishedDepth? publication;

        internal DepthReadLease(PublishedDepth publication) => this.publication = publication;

        public DepthSnapshot Snapshot => Volatile.Read(ref publication)?.Snapshot ?? DepthSnapshot.Unavailable;

        public void Dispose()
        {
            var released = Interlocked.Exchange(ref publication, null);
            if (released is null) return;

            lock (sync)
            {
                released.ReaderCount--;
                if (released.Retired && released.ReaderCount == 0)
                {
                    retiredBufferCount--;
                    ReturnBufferLocked(released);
                }
            }
        }
    }

    public readonly struct DepthSnapshot
    {
        private readonly float[]? viewToTextureUv;

        public static DepthSnapshot Unavailable => default;

        internal DepthSnapshot(
            long version,
            ARFrameMetadata metadata,
            long trackingEpoch,
            long worldFrame,
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
            ARGroundStateBridge.GroundStateSnapshot groundState,
            float groundWorldY)
        {
            Version = version;
            Metadata = metadata;
            TrackingEpoch = trackingEpoch;
            WorldFrame = worldFrame;
            Width = width;
            Height = height;
            RentedDepthMillimeters = depthMillimeters;
            this.viewToTextureUv = viewToTextureUv;
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
            GroundState = groundState;
            GroundWorldY = groundWorldY;
        }

        public long Version { get; }
        public ARFrameMetadata Metadata { get; }
        public ARRenderGenerationToken Generation => Metadata.Generation;
        public long FrameTimestamp => Metadata.FrameTimestamp;
        public ARDisplayGeometrySnapshot DisplayGeometry => Metadata.DisplayGeometry;
        public long TrackingEpoch { get; }
        public long WorldFrame { get; }
        public bool IsAvailable => RentedDepthMillimeters is not null;
        public int Width { get; }
        public int Height { get; }
        internal ushort[]? RentedDepthMillimeters { get; }
        public ReadOnlySpan<ushort> DepthMillimeters => RentedDepthMillimeters.AsSpan(0, Width * Height);
        public ReadOnlySpan<float> ViewToTextureUv => viewToTextureUv;
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
        public ARGroundStateBridge.GroundStateSnapshot GroundState { get; }
        public ARGroundTrust GroundTrust => GroundState.Trust;
        public bool GroundAvailable => GroundState.HasGroundReference;
        public bool GroundIsProvisional => GroundTrust == ARGroundTrust.Provisional;
        public bool GroundIsVerified => GroundTrust == ARGroundTrust.Verified;
        public long GroundReferenceGeneration => GroundState.ReferenceGeneration;
        public float GroundWorldY { get; }
        public bool IsFresh => Metadata.IsFresh(MaximumDepthAgeMilliseconds, Environment.TickCount64);
    }
}
