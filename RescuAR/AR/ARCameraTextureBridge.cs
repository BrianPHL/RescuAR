using Evergine.Common.Graphics;
using System;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Thread-safe handoff used by the AR camera pipeline.
///
/// The Android ARCore worker publishes only a pending-frame callback target;
/// it does not submit Vulkan work itself. MyApplication.DrawFrame() invokes
/// <see cref="ProcessDrawThreadWork"/> immediately before Evergine performs
/// its own draw cycle. The Android callback can then convert the newest
/// pending ARCore HardwareBuffer on Evergine's draw thread and publish the
/// resulting persistent Evergine Texture here.
///
/// The Texture remains owned by ARCoreVulkanImporter/VulkanRgbaTarget. This
/// bridge never disposes it.
/// </summary>
public static class ARCameraTextureBridge
{
    private static readonly object processingSync =
        new();

    private static readonly ManualResetEventSlim processorIdle =
        new(initialState: true);

    private static Texture? currentTexture;
    private static long version;
    private static Action? drawThreadProcessor;

    private static bool processingSuspended;
    private static int activeProcessorCalls;

    public static Texture? CurrentTexture =>
        Volatile.Read(ref currentTexture);

    public static long Version =>
        Interlocked.Read(ref version);

    /// <summary>
    /// Registers the platform-specific callback that consumes the newest
    /// pending camera frame. Android supplies this from ArCoreService after
    /// Evergine's Vulkan graphics context becomes available.
    /// </summary>
    public static void SetDrawThreadProcessor(
        Action? processor)
    {
        Volatile.Write(
            ref drawThreadProcessor,
            processor);
    }

    /// <summary>
    /// Prevents new platform camera conversions and waits for a conversion
    /// already running on the Evergine draw thread to finish. Camera-tab
    /// teardown uses this barrier before Android can detach the Vulkan surface.
    /// </summary>
    public static bool SuspendProcessing(
        TimeSpan timeout)
    {
        lock (processingSync)
        {
            processingSuspended =
                true;

            if (activeProcessorCalls ==
                0)
            {
                processorIdle.Set();
            }
        }

        return processorIdle.Wait(
            timeout);
    }

    /// <summary>
    /// Re-enables draw-thread camera conversion after the retained Camera
    /// surface and ARCore Session are ready to resume.
    /// </summary>
    public static void ResumeProcessing()
    {
        lock (processingSync)
        {
            processingSuspended =
                false;
        }
    }

    /// <summary>
    /// Called from MyApplication.DrawFrame(). If Android has registered a
    /// processor, it is executed on the same application draw thread that
    /// invoked this method.
    /// </summary>
    public static void ProcessDrawThreadWork()
    {
        Action? processor;

        lock (processingSync)
        {
            if (processingSuspended)
            {
                return;
            }

            processor =
                Volatile.Read(
                    ref drawThreadProcessor);

            if (processor is null)
            {
                return;
            }

            activeProcessorCalls++;

            processorIdle.Reset();
        }

        try
        {
            processor();
        }
        finally
        {
            lock (processingSync)
            {
                activeProcessorCalls--;

                if (activeProcessorCalls ==
                    0)
                {
                    processorIdle.Set();
                }
            }
        }
    }

    public static void Publish(
        Texture texture)
    {
        ArgumentNullException.ThrowIfNull(
            texture);

        Texture? previous =
            Volatile.Read(ref currentTexture);

        if (ReferenceEquals(
            previous,
            texture))
        {
            return;
        }

        Volatile.Write(
            ref currentTexture,
            texture);

        Interlocked.Increment(
            ref version);
    }

    public static void Clear()
    {
        Volatile.Write(
            ref currentTexture,
            null);

        Interlocked.Increment(
            ref version);
    }
}
