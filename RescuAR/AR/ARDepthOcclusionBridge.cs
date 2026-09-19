using RescuAR.Diagnostics;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Thread-safe handoff for ARCore's smoothed 16-bit depth image.
/// The publisher transfers ownership of a rented depth buffer to this bridge.
/// Superseded buffers stay retired beyond the maximum reader age before they
/// return to the pool, preventing consumers from observing recycled memory.
/// </summary>
public static class ARDepthOcclusionBridge
{
    public const long MaximumDepthAgeMilliseconds = 750;

    private const long RetiredBufferSafetyMilliseconds =
        MaximumDepthAgeMilliseconds * 3;

    private const string LogTag = "RescuAR-DepthBuffers";

    private static readonly object sync = new();
    private static readonly Queue<RetiredBuffer> retiredBuffers = new();

    private static long version;
    private static long rentedBufferCount;
    private static long returnedBufferCount;
    private static long peakRetiredBufferCount;
    private static long lastMetricsLogTimestamp = long.MinValue;

    private static DepthSnapshot current = DepthSnapshot.Unavailable;

    public static DepthSnapshot Current
    {
        get
        {
            DepthSnapshot snapshot;

            lock (sync)
            {
                ReclaimRetiredBuffersLocked(Environment.TickCount64);
                snapshot = current;
            }

            if (!snapshot.IsAvailable)
            {
                return snapshot;
            }

            if (!ARRenderGenerationBridge.IsCurrent(snapshot.Generation) ||
                !snapshot.IsFresh)
            {
                Clear();

                lock (sync)
                {
                    return current;
                }
            }

            return snapshot;
        }
    }

    /// <summary>
    /// Publishes a rented depth array. Returns true only when ownership was
    /// accepted; on false the caller remains responsible for returning it.
    /// </summary>
    public static bool Publish(
        ARFrameMetadata metadata,
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
        int textureHeight,
        ARGroundStateBridge.GroundStateSnapshot groundState,
        float groundWorldY)
    {
        ArgumentNullException.ThrowIfNull(rentedDepthMillimeters);
        ArgumentNullException.ThrowIfNull(viewToTextureUv);

        if (!metadata.IsValid ||
            !ARRenderGenerationBridge.TryAcceptCallback(
                metadata.Generation,
                "depth-publish") ||
            width <= 0 ||
            height <= 0 ||
            rentedDepthMillimeters.Length < width * height ||
            viewToTextureUv.Length < 8 ||
            !float.IsFinite(focalLengthX) ||
            !float.IsFinite(focalLengthY) ||
            focalLengthX <= 0.0f ||
            focalLengthY <= 0.0f ||
            textureWidth <= 0 ||
            textureHeight <= 0)
        {
            return false;
        }

        long nextVersion = Interlocked.Increment(ref version);
        long now = Environment.TickCount64;

        DepthSnapshot next = new(
            nextVersion,
            metadata,
            now,
            true,
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

        lock (sync)
        {
            RetireCurrentBufferLocked(now);
            current = next;
            rentedBufferCount++;
            ReclaimRetiredBuffersLocked(now);
            LogBufferMetricsIfNeededLocked(now);
        }

        return true;
    }

    public static void Clear()
    {
        long nextVersion = Interlocked.Increment(ref version);
        long now = Environment.TickCount64;

        lock (sync)
        {
            RetireCurrentBufferLocked(now);
            current = DepthSnapshot.UnavailableWithVersion(nextVersion);
            ReclaimRetiredBuffersLocked(now);
            LogBufferMetricsIfNeededLocked(now);
        }
    }

    private static void RetireCurrentBufferLocked(long now)
    {
        if (!current.OwnsPooledDepthBuffer ||
            current.DepthMillimeters.Length == 0)
        {
            return;
        }

        retiredBuffers.Enqueue(
            new RetiredBuffer(
                current.DepthMillimeters,
                now + RetiredBufferSafetyMilliseconds));

        peakRetiredBufferCount = Math.Max(
            peakRetiredBufferCount,
            retiredBuffers.Count);
    }

    private static void ReclaimRetiredBuffersLocked(long now)
    {
        while (retiredBuffers.Count > 0 &&
               retiredBuffers.Peek().ReturnAfterMonotonicMilliseconds <= now)
        {
            RetiredBuffer retired = retiredBuffers.Dequeue();
            ArrayPool<ushort>.Shared.Return(retired.Buffer, clearArray: false);
            returnedBufferCount++;
        }
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
            $"retired={retiredBuffers.Count}; peakRetired={peakRetiredBufferCount}; " +
            $"active={(current.OwnsPooledDepthBuffer ? 1 : 0)}.");
    }

    private readonly record struct RetiredBuffer(
        ushort[] Buffer,
        long ReturnAfterMonotonicMilliseconds);

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
                ARGroundStateBridge.GroundStateSnapshot.Unavailable,
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
            ARGroundStateBridge.GroundStateSnapshot groundState,
            float groundWorldY)
        {
            Version = version;
            Metadata = metadata;
            PublishedAtMonotonicMilliseconds = publishedAtMonotonicMilliseconds;
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
            GroundState = groundState;
            GroundWorldY = groundWorldY;
            OwnsPooledDepthBuffer = isAvailable;
        }

        public long Version { get; }
        public ARFrameMetadata Metadata { get; }
        public ARRenderGenerationToken Generation => Metadata.Generation;
        public long FrameTimestamp => Metadata.FrameTimestamp;
        public ARDisplayGeometrySnapshot DisplayGeometry => Metadata.DisplayGeometry;
        public long PublishedAtMonotonicMilliseconds { get; }
        public bool IsAvailable { get; }
        public int Width { get; }
        public int Height { get; }
        public ushort[] DepthMillimeters { get; }
        public float[] ViewToTextureUv { get; }
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
        internal bool OwnsPooledDepthBuffer { get; }

        public bool IsFresh =>
            PublishedAtMonotonicMilliseconds != long.MinValue &&
            Environment.TickCount64 - PublishedAtMonotonicMilliseconds >= 0 &&
            Environment.TickCount64 - PublishedAtMonotonicMilliseconds <=
                MaximumDepthAgeMilliseconds;
    }
}
