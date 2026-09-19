using Android.Hardware;
using Evergine.Common.Graphics;
using Evergine.Vulkan;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Narrow service-facing contract for camera-frame import. ARCore lifecycle
/// code does not depend on the internal Vulkan objects owned by the concrete
/// Evergine adapter.
/// </summary>
internal interface IArCameraFrameImporter : IDisposable
{
    Texture Import(
        HardwareBuffer hardwareBuffer,
        float[] cameraUv,
        uint outputWidth,
        uint outputHeight);
}

/// <summary>
/// Owns the Evergine/Vulkan-specific importer for one graphics-context
/// generation. Construction, import, and disposal must occur on that
/// generation's draw thread.
/// </summary>
internal sealed class EvergineArCameraFrameImporter : IArCameraFrameImporter
{
    private readonly ARCoreVulkanImporter inner;

    public EvergineArCameraFrameImporter(
        VKGraphicsContext graphicsContext)
    {
        inner =
            new ARCoreVulkanImporter(
                graphicsContext);
    }

    public Texture Import(
        HardwareBuffer hardwareBuffer,
        float[] cameraUv,
        uint outputWidth,
        uint outputHeight) =>
        inner.ImportHardwareBuffer(
            hardwareBuffer,
            cameraUv,
            outputWidth,
            outputHeight);

    public void Dispose() =>
        inner.Dispose();

    public static void WaitForGraphicsDeviceIdle(
        VKGraphicsContext graphicsContext) =>
        ARCoreVulkanImporter.WaitForGraphicsDeviceIdle(
            graphicsContext);
}
