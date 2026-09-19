using RescuAR.Diagnostics;
using System;

namespace RescuAR.AR;

/// <summary>
/// Generation-, frame-, mode-, and ground-trust-aware flood publication from
/// MAUI to the Evergine draw thread.
/// </summary>
public static class ARFloodDepthBridge
{
    private const string LogTag = "RescuAR-FloodDepth";

    public const float DefaultHorizontalExtentMeters = 60.0f;
    public const float MaximumSupportedDepthMeters = 3.0f;
    public const long MaximumPublicationAgeMilliseconds = 2_500;

    private static readonly object sync = new();

    private static long version;
    private static long acknowledgedDrawThreadVersion = -1;
    private static FloodDepthSnapshot current = FloodDepthSnapshot.Unavailable;

    public static long AcknowledgedDrawThreadVersion
    {
        get
        {
            lock (sync)
            {
                return acknowledgedDrawThreadVersion;
            }
        }
    }

    public static FloodDepthSnapshot Current
    {
        get
        {
            FloodDepthSnapshot snapshot;

            lock (sync)
            {
                snapshot = current;
            }

            if (!snapshot.IsAvailable)
            {
                return snapshot;
            }

            string? invalidReason = GetInvalidReason(snapshot);

            if (invalidReason is null)
            {
                return snapshot;
            }

            Clear(invalidReason);

            lock (sync)
            {
                return current;
            }
        }
    }

    public static void PublishLocalDepth(
        double localDepthMeters,
        bool modeActive,
        string source = "local flood depth",
        float horizontalExtentMeters = DefaultHorizontalExtentMeters)
    {
        if (!modeActive)
        {
            Clear("Flood Depth mode is not active");
            return;
        }

        if (!double.IsFinite(localDepthMeters) || localDepthMeters <= 0.0)
        {
            Clear("invalid/non-positive local depth");
            return;
        }

        if (!ARFrameCoherencePolicy.TryGetSpatialFrameForCurrentCamera(
                out ARCameraPoseBridge.SpatialSnapshot spatialFrame) ||
            !spatialFrame.IsTracking ||
            !spatialFrame.Pose.IsTracking ||
            !spatialFrame.Anchor.IsAvailable ||
            spatialFrame.Anchor.Trust == ARGroundTrust.None ||
            spatialFrame.Anchor.GroundState.RenderGeneration !=
                spatialFrame.Generation)
        {
            Clear("no fresh coherent tracked frame with a trusted ground reference");
            return;
        }

        float depth = (float)Math.Clamp(
            localDepthMeters,
            0.01,
            MaximumSupportedDepthMeters);

        float extent = Math.Clamp(
            horizontalExtentMeters,
            2.0f,
            DefaultHorizontalExtentMeters);

        string normalizedSource = string.IsNullOrWhiteSpace(source)
            ? "local flood depth"
            : source.Trim();

        long nextVersion;
        FloodDepthSnapshot next;

        lock (sync)
        {
            nextVersion = ++version;

            next = new FloodDepthSnapshot(
                nextVersion,
                spatialFrame.Metadata,
                spatialFrame.Anchor.GroundState,
                Environment.TickCount64,
                true,
                true,
                depth,
                extent,
                normalizedSource);

            current = next;
        }

        AndroidLog.Debug(
            LogTag,
            "AR flood-depth bridge published: " +
            $"version={nextVersion}; frameTimestamp={next.FrameTimestamp}; " +
            $"groundTrust={next.GroundTrust}; " +
            $"groundReferenceGeneration={next.GroundReferenceGeneration}; " +
            $"depth={depth:F2}m; source='{next.Source}'.");
    }

    public static long Clear(string reason = "cleared")
    {
        long nextVersion;

        lock (sync)
        {
            if (!current.IsAvailable &&
                current.Generation == ARRenderGenerationBridge.Current)
            {
                return current.Version;
            }

            nextVersion = ++version;
            current = FloodDepthSnapshot.UnavailableWithVersion(
                nextVersion,
                ARRenderGenerationBridge.Current);
        }

        AndroidLog.Debug(
            LogTag,
            "AR flood-depth bridge cleared: " +
            $"version={nextVersion}; reason='{reason}'.");

        return nextVersion;
    }

    public static void AcknowledgeDrawThreadVersion(long acknowledgedVersion)
    {
        lock (sync)
        {
            if (current.Version != acknowledgedVersion ||
                acknowledgedDrawThreadVersion == acknowledgedVersion)
            {
                return;
            }

            acknowledgedDrawThreadVersion = acknowledgedVersion;
        }

        AndroidLog.Info(
            LogTag,
            "AR flood-depth state acknowledged on the draw thread: " +
            $"version={acknowledgedVersion}.");
    }

    private static string? GetInvalidReason(FloodDepthSnapshot snapshot)
    {
        if (!snapshot.ModeActive)
        {
            return "published flood mode became inactive";
        }

        if (!ARRenderGenerationBridge.IsCurrent(snapshot.Generation))
        {
            return "flood publication belongs to an inactive render generation";
        }

        long age = Environment.TickCount64 -
            snapshot.PublishedAtMonotonicMilliseconds;

        if (age < 0 || age > MaximumPublicationAgeMilliseconds)
        {
            return "flood publication expired";
        }

        if (!ARFrameCoherencePolicy.TryGetSpatialFrameForCurrentCamera(
                out ARCameraPoseBridge.SpatialSnapshot spatialFrame) ||
            !spatialFrame.IsTracking ||
            !spatialFrame.Pose.IsTracking)
        {
            return "fresh tracked camera/pose frame unavailable";
        }

        if (!spatialFrame.Anchor.IsAvailable ||
            spatialFrame.Anchor.Trust == ARGroundTrust.None ||
            spatialFrame.Anchor.GroundState.RenderGeneration !=
                spatialFrame.Generation)
        {
            return "ground reference unavailable";
        }

        if (spatialFrame.Anchor.ReferenceGeneration !=
                snapshot.GroundReferenceGeneration ||
            spatialFrame.Anchor.Trust != snapshot.GroundTrust)
        {
            return "ground trust/reference generation changed";
        }

        return null;
    }

    public readonly struct FloodDepthSnapshot
    {
        public static FloodDepthSnapshot Unavailable =>
            UnavailableWithVersion(0, ARRenderGenerationToken.Invalid);

        public static FloodDepthSnapshot UnavailableWithVersion(
            long version,
            ARRenderGenerationToken generation) =>
            new(
                version,
                new ARFrameMetadata(
                    generation,
                    long.MinValue,
                    ARDisplayGeometrySnapshot.Invalid),
                ARGroundStateBridge.GroundStateSnapshot.Unavailable,
                long.MinValue,
                false,
                false,
                0.0f,
                DefaultHorizontalExtentMeters,
                string.Empty);

        public FloodDepthSnapshot(
            long version,
            ARFrameMetadata metadata,
            ARGroundStateBridge.GroundStateSnapshot groundState,
            long publishedAtMonotonicMilliseconds,
            bool isAvailable,
            bool modeActive,
            float localDepthMeters,
            float horizontalExtentMeters,
            string source)
        {
            Version = version;
            Metadata = metadata;
            GroundState = groundState;
            PublishedAtMonotonicMilliseconds = publishedAtMonotonicMilliseconds;
            IsAvailable = isAvailable;
            ModeActive = modeActive;
            LocalDepthMeters = localDepthMeters;
            HorizontalExtentMeters = horizontalExtentMeters;
            Source = source;
        }

        public long Version { get; }
        public ARFrameMetadata Metadata { get; }
        public ARRenderGenerationToken Generation => Metadata.Generation;
        public long FrameTimestamp => Metadata.FrameTimestamp;
        public ARGroundStateBridge.GroundStateSnapshot GroundState { get; }
        public ARGroundTrust GroundTrust => GroundState.Trust;
        public long GroundReferenceGeneration => GroundState.ReferenceGeneration;
        public long PublishedAtMonotonicMilliseconds { get; }
        public bool IsAvailable { get; }
        public bool ModeActive { get; }
        public float LocalDepthMeters { get; }
        public float HorizontalExtentMeters { get; }
        public string Source { get; }
    }
}
