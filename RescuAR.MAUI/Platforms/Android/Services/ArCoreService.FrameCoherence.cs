using Android.Util;
using RescuAR.AR;

namespace RescuAR.MAUI.Platforms.Android.Services;

public sealed partial class ArCoreService
{
    private long frameMetadataDropCount;

    private ARFrameMetadata CaptureFrameMetadata(
        ARRenderGenerationToken renderGeneration,
        long frameTimestamp)
    {
        int rotation;
        int width;
        int height;
        long geometryGeneration;

        lock (displayGeometryLock)
        {
            rotation = appliedDisplayRotation;
            width = appliedDisplayWidth;
            height = appliedDisplayHeight;
            geometryGeneration = appliedDisplayGeometryGeneration;
        }

        if (geometryGeneration != renderGeneration.GeometryGeneration ||
            width <= 0 ||
            height <= 0)
        {
            return ARFrameMetadata.Invalid;
        }

        return new ARFrameMetadata(
            renderGeneration,
            frameTimestamp,
            new ARDisplayGeometrySnapshot(
                geometryGeneration,
                rotation,
                checked((uint)width),
                checked((uint)height)));
    }

    private void RecordFrameCoherenceDrop(
        string reason,
        ARRenderGenerationToken generation,
        long frameTimestamp)
    {
        long dropped =
            Interlocked.Increment(
                ref frameMetadataDropCount);

        if (dropped == 1 ||
            dropped % 25 == 0)
        {
            Log.Warn(
                Tag,
                "ARCORE_FRAME_METADATA_REJECTED " +
                $"count={dropped}; reason='{reason}'; " +
                $"frameTimestamp={frameTimestamp}; " +
                $"generation={generation}.");
        }
    }
}
