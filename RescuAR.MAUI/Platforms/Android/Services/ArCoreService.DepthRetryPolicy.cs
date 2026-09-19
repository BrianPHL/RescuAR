using Android.Util;
using RescuAR.AR;
using System;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Exponential retry policy for timestamped ARCore Depth acquisition.
/// </summary>
public sealed partial class ArCoreService
{
    private const long InitialDepthRetryMilliseconds = 250;
    private const long MaximumDepthRetryMilliseconds = 8_000;
    private const long DepthRetryMetricsIntervalMilliseconds = 10_000;

    private int depthAcquisitionFailureStreak;
    private long nextDepthAcquisitionAttemptTimestamp = long.MinValue;
    private long lastDepthRetryMetricsTimestamp = long.MinValue;
    private long depthTimestampUnavailableCount;
    private long depthInvalidFrameCount;
    private long depthPublishRejectedCount;
    private long depthAcceptedFrameCount;

    private void ResetDepthRetryPolicy()
    {
        depthAcquisitionFailureStreak = 0;
        nextDepthAcquisitionAttemptTimestamp = long.MinValue;
        lastDepthRetryMetricsTimestamp = long.MinValue;
        depthTimestampUnavailableCount = 0;
        depthInvalidFrameCount = 0;
        depthPublishRejectedCount = 0;
        depthAcceptedFrameCount = 0;
    }

    private bool CanAttemptDepthAcquisition(long now) =>
        nextDepthAcquisitionAttemptTimestamp == long.MinValue ||
        now >= nextDepthAcquisitionAttemptTimestamp;

    private void RegisterDepthAcquisitionFailure(
        long now,
        DepthFailureKind kind,
        string reason)
    {
        switch (kind)
        {
            case DepthFailureKind.TimestampUnavailable:
                depthTimestampUnavailableCount++;
                break;
            case DepthFailureKind.PublishRejected:
                depthPublishRejectedCount++;
                break;
            default:
                depthInvalidFrameCount++;
                break;
        }

        depthAcquisitionFailureStreak = Math.Min(
            depthAcquisitionFailureStreak + 1,
            8);

        int shift = Math.Min(5, depthAcquisitionFailureStreak - 1);
        long retryMilliseconds = Math.Min(
            MaximumDepthRetryMilliseconds,
            InitialDepthRetryMilliseconds << shift);

        retryMilliseconds = Math.Min(
            MaximumDepthRetryMilliseconds,
            retryMilliseconds * Math.Max(
                1,
                powerThermalDecision.DepthIntervalMultiplier));

        nextDepthAcquisitionAttemptTimestamp = now + retryMilliseconds;

        ARDepthOcclusionBridge.Clear();
        LogDepthRetryMetricsIfNeeded(now, retryMilliseconds, reason);
    }

    private void RegisterDepthAcquisitionSuccess(long now)
    {
        depthAcceptedFrameCount++;
        depthAcquisitionFailureStreak = 0;
        nextDepthAcquisitionAttemptTimestamp = long.MinValue;
        LogDepthRetryMetricsIfNeeded(now, 0, "depth frame accepted");
    }

    private void LogDepthRetryMetricsIfNeeded(
        long now,
        long retryMilliseconds,
        string reason)
    {
        if (lastDepthRetryMetricsTimestamp != long.MinValue &&
            now - lastDepthRetryMetricsTimestamp <
                DepthRetryMetricsIntervalMilliseconds)
        {
            return;
        }

        lastDepthRetryMetricsTimestamp = now;

        Log.Info(
            "RescuAR-FloodDepth",
            "ARCORE_DEPTH_RETRY_METRICS " +
            $"accepted={depthAcceptedFrameCount}; " +
            $"timestampUnavailable={depthTimestampUnavailableCount}; " +
            $"invalid={depthInvalidFrameCount}; " +
            $"publishRejected={depthPublishRejectedCount}; " +
            $"failureStreak={depthAcquisitionFailureStreak}; " +
            $"retryMs={retryMilliseconds}; reason='{reason}'.");
    }

    private enum DepthFailureKind
    {
        TimestampUnavailable,
        InvalidFrame,
        PublishRejected
    }
}
