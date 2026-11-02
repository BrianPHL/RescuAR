using Android.Util;
using RescuAR.AR;
using RescuAR.Navigation.Timing;

namespace RescuAR.MAUI.Platforms.Android.Services;

public sealed partial class ArCoreService
{
    private long frameMetadataDropCount;
    private bool? frameAcquisitionClockMapped;
    private long frameClockSession, lastClockFrame = -1, lastFrameAcquiredAt;
    private bool nativeFrameClockEstablished;

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

        long receivedAt = System.Environment.TickCount64;
        if (frameClockSession != renderGeneration.SessionGeneration)
        {
            frameClockSession = renderGeneration.SessionGeneration;
            frameAcquisitionClockMapped = null;
            nativeFrameClockEstablished = false;
            lastClockFrame = -1;
        }
        bool mapped = NavigationClock.TryMapElapsedNanoseconds(frameTimestamp,
            global::Android.OS.SystemClock.ElapsedRealtimeNanos(), receivedAt, out long acquiredAt) &&
            (nativeFrameClockEstablished || receivedAt - acquiredAt <= 250);
        if (mapped) nativeFrameClockEstablished = true;
        else acquiredAt = receivedAt;
        // A reused frame cannot refresh evidence even when this device's
        // camera timestamp domain requires the explicit receipt-time fallback.
        if (lastClockFrame == frameTimestamp) acquiredAt = lastFrameAcquiredAt;
        else { lastClockFrame = frameTimestamp; lastFrameAcquiredAt = acquiredAt; }
        if (frameAcquisitionClockMapped != mapped)
        {
            frameAcquisitionClockMapped = mapped;
            Log.Info(Tag, $"NAV_FRAME_CLOCK nativeAcquisitionMapped={mapped}; " +
                $"deliveryAgeMs={(mapped ? receivedAt - acquiredAt : -1)}; " +
                $"fallback={(mapped ? "none" : "FRAME_RECEIPT")}; session={renderGeneration.SessionGeneration}.");
        }
        return new ARFrameMetadata(
            renderGeneration,
            frameTimestamp,
            new ARDisplayGeometrySnapshot(
                geometryGeneration,
                rotation,
                checked((uint)width),
                checked((uint)height)))
        {
            AcquiredAtMonotonicMilliseconds = acquiredAt
        };
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
