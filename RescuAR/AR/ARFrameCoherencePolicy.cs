using RescuAR.Diagnostics;
using System;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Rejects camera, pose, Depth, and display-geometry combinations that do not
/// belong to the same render generation or are too far apart in ARCore time.
/// </summary>
public static class ARFrameCoherencePolicy
{
    private const string LogTag = "RescuAR-ARCoherence";

    // Two 30 FPS frame periods plus scheduling allowance.
    public const long MaximumCameraPoseSkewNanoseconds = 75_000_000L;

    // Route-only Depth is intentionally capped at 5 Hz, so one 200 ms Depth
    // period plus scheduling allowance is accepted.
    public const long MaximumDepthPoseSkewNanoseconds = 250_000_000L;

    private const long StatisticsLogIntervalMilliseconds = 5_000;

    private static long acceptedCameraPosePairs;
    private static long rejectedCameraPosePairs;
    private static long acceptedDepthPosePairs;
    private static long rejectedDepthPosePairs;
    private static long lastCameraPoseSkewNanoseconds = long.MinValue;
    private static long lastDepthPoseSkewNanoseconds = long.MinValue;
    private static long lastStatisticsLogTimestamp = long.MinValue;

    public static bool TryGetSpatialFrameForCurrentCamera(
        out ARCameraPoseBridge.SpatialSnapshot frame)
    {
        ARCameraTextureBridge.TextureSnapshot camera =
            ARCameraTextureBridge.Current;

        if (camera.Texture is null ||
            !camera.Metadata.IsValid)
        {
            frame = ARCameraPoseBridge.SpatialSnapshot.Unavailable;
            Interlocked.Increment(ref rejectedCameraPosePairs);
            LogStatisticsIfDue("camera texture unavailable or stale");
            return false;
        }

        bool accepted =
            ARCameraPoseBridge.TryGetFrameForMetadata(
                camera.Metadata,
                MaximumCameraPoseSkewNanoseconds,
                out frame,
                out long skewNanoseconds);

        Interlocked.Exchange(
            ref lastCameraPoseSkewNanoseconds,
            skewNanoseconds);

        if (accepted)
        {
            Interlocked.Increment(ref acceptedCameraPosePairs);
        }
        else
        {
            Interlocked.Increment(ref rejectedCameraPosePairs);
            frame = ARCameraPoseBridge.SpatialSnapshot.Unavailable;
        }

        LogStatisticsIfDue(
            accepted
                ? "camera/pose coherent"
                : "camera/pose timestamp or generation mismatch");

        return accepted;
    }

    public static ARDepthOcclusionBridge.DepthSnapshot
        GetDepthForSpatialFrame(
            ARCameraPoseBridge.SpatialSnapshot frame)
    {
        ARDepthOcclusionBridge.DepthSnapshot depth =
            ARDepthOcclusionBridge.Current;

        if (!frame.Metadata.IsValid ||
            !depth.IsAvailable ||
            !depth.Metadata.IsValid)
        {
            Interlocked.Increment(ref rejectedDepthPosePairs);
            LogStatisticsIfDue("Depth or pose unavailable");
            return ARDepthOcclusionBridge.DepthSnapshot.Unavailable;
        }

        long skewNanoseconds =
            AbsoluteTimestampDifference(
                frame.FrameTimestamp,
                depth.FrameTimestamp);

        bool accepted =
            frame.Generation == depth.Generation &&
            frame.DisplayGeometry == depth.DisplayGeometry &&
            skewNanoseconds <= MaximumDepthPoseSkewNanoseconds;

        Interlocked.Exchange(
            ref lastDepthPoseSkewNanoseconds,
            skewNanoseconds);

        if (accepted)
        {
            Interlocked.Increment(ref acceptedDepthPosePairs);
        }
        else
        {
            Interlocked.Increment(ref rejectedDepthPosePairs);
        }

        LogStatisticsIfDue(
            accepted
                ? "Depth/pose coherent"
                : "Depth/pose timestamp or generation mismatch");

        return accepted
            ? depth
            : ARDepthOcclusionBridge.DepthSnapshot.Unavailable;
    }

    private static void LogStatisticsIfDue(
        string reason)
    {
        long now = Environment.TickCount64;
        long previous =
            Interlocked.Read(ref lastStatisticsLogTimestamp);

        if (previous != long.MinValue &&
            now - previous < StatisticsLogIntervalMilliseconds)
        {
            return;
        }

        if (Interlocked.CompareExchange(
                ref lastStatisticsLogTimestamp,
                now,
                previous) != previous)
        {
            return;
        }

        AndroidLog.Info(
            LogTag,
            "ARCORE_FRAME_COHERENCE_STATS " +
            $"cameraPoseAccepted={Interlocked.Read(ref acceptedCameraPosePairs)}, " +
            $"cameraPoseRejected={Interlocked.Read(ref rejectedCameraPosePairs)}, " +
            $"depthPoseAccepted={Interlocked.Read(ref acceptedDepthPosePairs)}, " +
            $"depthPoseRejected={Interlocked.Read(ref rejectedDepthPosePairs)}, " +
            $"lastCameraPoseSkewNs={Interlocked.Read(ref lastCameraPoseSkewNanoseconds)}, " +
            $"lastDepthPoseSkewNs={Interlocked.Read(ref lastDepthPoseSkewNanoseconds)}, " +
            $"reason='{reason}'.");
    }

    private static long AbsoluteTimestampDifference(
        long first,
        long second)
    {
        if (first <= 0 || second <= 0)
        {
            return long.MaxValue;
        }

        return first >= second
            ? first - second
            : second - first;
    }
}
