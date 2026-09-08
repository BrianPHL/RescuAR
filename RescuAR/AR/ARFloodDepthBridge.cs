using RescuAR.Diagnostics;
using System;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Thread-safe handoff from the MAUI Flood Depth sub-tab to the Evergine
/// draw thread.
///
/// Only an explicit LOCAL flood depth is published here. River gauge values
/// and generic flood advisories must never be converted into an AR water
/// height because they do not describe the water depth at the phone.
/// </summary>
public static class ARFloodDepthBridge
{
    private const string LogTag =
        "RescuAR-FloodDepth";

    // Figma refinement / device feedback: the original 10 m square made the
    // flood-volume side edges noticeable in the camera. A 60 m square keeps
    // the nearby view visually continuous while still using only two reusable
    // Evergine cube entities.
    public const float DefaultHorizontalExtentMeters =
        60.0f;

    public const float MaximumSupportedDepthMeters =
        3.0f;

    private static readonly object sync =
        new();

    private static long version;

    private static FloodDepthSnapshot current =
        FloodDepthSnapshot.Unavailable;

    public static FloodDepthSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public static void PublishLocalDepth(
        double localDepthMeters,
        string source = "local flood depth",
        float horizontalExtentMeters = DefaultHorizontalExtentMeters)
    {
        if (!double.IsFinite(localDepthMeters) ||
            localDepthMeters <= 0.0)
        {
            Clear(
                "invalid/non-positive local depth");

            return;
        }

        float depth =
            (float)Math.Clamp(
                localDepthMeters,
                0.01,
                MaximumSupportedDepthMeters);

        float extent =
            Math.Clamp(
                horizontalExtentMeters,
                2.0f,
                DefaultHorizontalExtentMeters);

        long nextVersion =
            Interlocked.Increment(
                ref version);

        FloodDepthSnapshot next =
            new(
                nextVersion,
                true,
                depth,
                extent,
                string.IsNullOrWhiteSpace(source)
                    ? "local flood depth"
                    : source.Trim());

        lock (sync)
        {
            current =
                next;
        }

        AndroidLog.Debug(
            LogTag,
            "AR flood-depth bridge published local depth: " +
            $"version={nextVersion}, " +
            $"depth={depth:F2} m, " +
            $"extent={extent:F1} m, " +
            $"source='{next.Source}'.");
    }

    public static void Clear(
        string reason = "cleared")
    {
        long nextVersion =
            Interlocked.Increment(
                ref version);

        lock (sync)
        {
            current =
                new FloodDepthSnapshot(
                    nextVersion,
                    false,
                    0.0f,
                    DefaultHorizontalExtentMeters,
                    string.Empty);
        }

        AndroidLog.Debug(
            LogTag,
            "AR flood-depth bridge cleared: " +
            $"version={nextVersion}, " +
            $"reason='{reason}'.");
    }

    public readonly struct FloodDepthSnapshot
    {
        public static FloodDepthSnapshot Unavailable =>
            new(
                0,
                false,
                0.0f,
                DefaultHorizontalExtentMeters,
                string.Empty);

        public FloodDepthSnapshot(
            long version,
            bool isAvailable,
            float localDepthMeters,
            float horizontalExtentMeters,
            string source)
        {
            Version =
                version;

            IsAvailable =
                isAvailable;

            LocalDepthMeters =
                localDepthMeters;

            HorizontalExtentMeters =
                horizontalExtentMeters;

            Source =
                source;
        }

        public long Version { get; }

        public bool IsAvailable { get; }

        public float LocalDepthMeters { get; }

        public float HorizontalExtentMeters { get; }

        public string Source { get; }
    }
}
