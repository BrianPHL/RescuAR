using Android.Hardware;
using Android.Util;
using Evergine.Bindings.Vulkan;
using Evergine.Common.Graphics;
using Evergine.Vulkan;
using RescuAR.AR;
using RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;
using System.Reflection;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Coordinates real-time ARCore HardwareBuffer import and conversion into a
/// persistent Evergine-compatible Vulkan texture.
///
/// The low-level Vulkan responsibilities are delegated to the classes under
/// Platforms/Android/Vulkan/ARCore. For external ARCore camera buffers, only
/// VulkanExternalFrame is replaced per frame; the RGBA target, Evergine
/// texture, YCbCr conversion, graphics pipeline, and command infrastructure
/// remain persistent while format and dimensions remain compatible.
///
/// Camera UV coordinates can optionally be supplied per frame. These are
/// forwarded to VulkanCameraCommandContext and ultimately pushed into the
/// fragment shader as Vulkan push constants.
/// </summary>
public unsafe sealed class ARCoreVulkanImporter : IDisposable
{
    private const string Tag =
        "RescuAR-Vulkan";

    /*
     * Default identity mapping used by the existing overload that does not
     * explicitly supply ARCore display-geometry UV coordinates.
     *
     * Layout:
     *
     * [0], [1] = top-left
     * [2], [3] = top-right
     * [4], [5] = bottom-left
     * [6], [7] = bottom-right
     */
    private static readonly float[] IdentityCameraUv =
    [
        0.0f, 0.0f,
        1.0f, 0.0f,
        0.0f, 1.0f,
        1.0f, 1.0f
    ];

    private readonly VKGraphicsContext graphicsContext;
    private readonly ResourceFactory resourceFactory;
    private readonly VulkanExternalFrameImporter externalFrameImporter;
    private int drawThreadId;
    private readonly float[] lastLoggedCameraUv = new float[8];

    private VulkanYcbcrResources? ycbcrResources;
    private VulkanExternalFrame? externalFrame;
    private VulkanRgbaTarget? rgbaTarget;
    private VulkanCameraConversionPipeline? conversionPipeline;
    private VulkanCameraCommandContext? commandContext;

    private bool disposed;
    private bool hasLoggedCameraUv;

    private bool vulkanDeviceLost;

    public Texture? ImportedTexture { get; private set; }

    public ARCoreVulkanImporter(
        VKGraphicsContext graphicsContext)
    {
        this.graphicsContext =
            graphicsContext
            ?? throw new ArgumentNullException(
                nameof(graphicsContext));

        drawThreadId =
            Environment.CurrentManagedThreadId;

        resourceFactory =
            ResolveResourceFactory(
                graphicsContext);

        externalFrameImporter =
            new VulkanExternalFrameImporter(
                graphicsContext);

        Log.Debug(
            Tag,
            $"ResourceFactory resolved: " +
            $"{resourceFactory.GetType().FullName}");
    }

    /// <summary>
    /// Imports the current ARCore HardwareBuffer using an identity UV mapping.
    ///
    /// This overload preserves the previous API and is intentionally kept as
    /// the Stage-A compatibility path. Once ARCore display geometry is wired,
    /// callers should use the overload that supplies cameraUv.
    /// </summary>
    public Texture? ImportHardwareBuffer(
        HardwareBuffer hardwareBuffer)
    {
        return ImportHardwareBuffer(
            hardwareBuffer,
            IdentityCameraUv);
    }

    /// <summary>
    /// Imports the current ARCore HardwareBuffer and converts it into the
    /// persistent Evergine-compatible RGBA texture.
    ///
    /// The cameraUv array must contain exactly eight floats:
    ///
    /// [0], [1] = top-left
    /// [2], [3] = top-right
    /// [4], [5] = bottom-left
    /// [6], [7] = bottom-right
    ///
    /// On the first external-format frame this method creates all persistent
    /// conversion resources. On later frames it replaces only the imported
    /// VulkanExternalFrame, updates the descriptor image view, and redraws
    /// into the same VulkanRgbaTarget / Evergine Texture.
    /// </summary>
    public Texture? ImportHardwareBuffer(
        HardwareBuffer hardwareBuffer,
        float[] cameraUv)
    {
        ArgumentNullException.ThrowIfNull(
            hardwareBuffer);

        return ImportHardwareBuffer(
            hardwareBuffer,
            cameraUv,
            checked((uint)hardwareBuffer.Width),
            checked((uint)hardwareBuffer.Height));
    }

    /// <summary>
    /// Imports the current ARCore HardwareBuffer and converts it into an RGBA
    /// target with the Evergine viewport's aspect ratio and bounded dimensions.
    ///
    /// The source HardwareBuffer can remain 1920x1080 while the output target
    /// is portrait. ARCore's transformed UVs select
    /// the correct crop/orientation, while the output target dimensions
    /// preserve the viewport aspect ratio without stretching.
    /// </summary>
    public Texture? ImportHardwareBuffer(
        HardwareBuffer hardwareBuffer,
        float[] cameraUv,
        uint outputWidth,
        uint outputHeight)
    {
        ThrowIfNotDrawThread();
        ThrowIfDisposed();

        if (commandContext is not null && !commandContext.TryCompleteFrame())
            throw new InvalidOperationException("ARCORE_CAMERA_GPU_FAILURE: complete the pending camera conversion before importing another frame.");

        if (vulkanDeviceLost)
        {
            throw new InvalidOperationException(
                "VK_ERROR_DEVICE_LOST: the Vulkan camera path is unavailable for this graphics generation.");
        }

        ArgumentNullException.ThrowIfNull(
            hardwareBuffer);

        ArgumentNullException.ThrowIfNull(
            cameraUv);

        if (cameraUv.Length != 8)
        {
            throw new ArgumentException(
                "Camera UV data must contain exactly eight floats: " +
                "top-left, top-right, bottom-left, and bottom-right.",
                nameof(cameraUv));
        }

        if (outputWidth == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputWidth),
                "Camera output width must be nonzero.");
        }

        if (outputHeight == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputHeight),
                "Camera output height must be nonzero.");
        }

        (outputWidth, outputHeight) = ARCameraConversionPolicy.GetTargetSize(outputWidth, outputHeight);

        if (hardwareBuffer.IsClosed)
        {
            throw new InvalidOperationException(
                "HardwareBuffer has already been closed.");
        }

        using NativeHardwareBufferReference nativeBuffer =
            NativeHardwareBufferReference.Acquire(
                hardwareBuffer);

        VulkanHardwareBufferQueryResult query =
            externalFrameImporter.QueryProperties(
                hardwareBuffer,
                nativeBuffer.Pointer);

        if (query.UsesExternalFormat)
        {
            return ImportOrUpdateExternalCameraBuffer(
                hardwareBuffer,
                nativeBuffer.Pointer,
                query,
                cameraUv,
                outputWidth,
                outputHeight);
        }

        // The former concrete-format path imported only its first image and
        // returned it as every subsequent frame, without UV conversion. Do not
        // represent that frozen image as live, registered AR passthrough.
        throw new NotSupportedException(
            "Live AR camera conversion requires an external YCbCr hardware buffer; " +
            $"the camera supplied {query.FormatProperties.format} without an external format.");
    }

    private Texture? ImportOrUpdateExternalCameraBuffer(
        HardwareBuffer hardwareBuffer,
        nint nativeHardwareBuffer,
        VulkanHardwareBufferQueryResult query,
        float[] cameraUv,
        uint outputWidth,
        uint outputHeight)
    {
        uint width =
            checked((uint)hardwareBuffer.Width);

        uint height =
            checked((uint)hardwareBuffer.Height);

        if (!HasCompleteExternalPipeline())
        {
            return InitializeExternalCameraPipeline(
                hardwareBuffer,
                nativeHardwareBuffer,
                query,
                cameraUv,
                outputWidth,
                outputHeight);
        }

        if (RequiresExternalPipelineRebuild(
            width,
            height,
            outputWidth,
            outputHeight,
            query))
        {
            Log.Warn(
                Tag,
                "ARCore camera format or dimensions changed. " +
                "Rebuilding persistent Vulkan camera resources.");

            // The conversion fence does not cover Evergine's later sampling
            // of the target. Drain those draws before replacing that texture.
            WaitForGraphicsDeviceIdle(graphicsContext);
            CleanupExternalResources();

            return InitializeExternalCameraPipeline(
                hardwareBuffer,
                nativeHardwareBuffer,
                query,
                cameraUv,
                outputWidth,
                outputHeight);
        }

        try
        {
            return UpdateExternalCameraFrame(
                hardwareBuffer,
                nativeHardwareBuffer,
                query,
                cameraUv);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                "VK_ERROR_DEVICE_LOST",
                StringComparison.Ordinal))
        {
            vulkanDeviceLost =
                true;

            Log.Error(
                Tag,
                "VK_ERROR_DEVICE_LOST detected. " +
                "Disabling further ARCore Vulkan camera imports for the " +
                "remainder of this graphics-device lifetime.");

            throw;
        }
    }

    private Texture? InitializeExternalCameraPipeline(
        HardwareBuffer hardwareBuffer,
        nint nativeHardwareBuffer,
        VulkanHardwareBufferQueryResult query,
        float[] cameraUv,
        uint outputWidth,
        uint outputHeight)
    {
        try
        {
            VkAndroidHardwareBufferFormatPropertiesANDROID formatProperties =
                query.FormatProperties;

            ycbcrResources =
                VulkanYcbcrResources.Create(
                    graphicsContext,
                    ref formatProperties);

            externalFrame =
                externalFrameImporter.Import(
                    hardwareBuffer,
                    nativeHardwareBuffer,
                    query,
                    ycbcrResources);

            rgbaTarget =
                VulkanRgbaTarget.Create(
                    graphicsContext,
                    resourceFactory,
                    outputWidth,
                    outputHeight);

            Log.Debug(
                Tag,
                "Camera conversion target: " +
                $"source={externalFrame.Width}x{externalFrame.Height}, " +
                $"output={outputWidth}x{outputHeight}");

            conversionPipeline =
                VulkanCameraConversionPipeline.Create(
                    graphicsContext,
                    ycbcrResources,
                    rgbaTarget,
                    externalFrame,
                    typeof(ARCoreVulkanImporter).Assembly);

            commandContext =
                VulkanCameraCommandContext.Create(
                    graphicsContext);

            ImportedTexture =
                rgbaTarget.Texture
                ?? throw new InvalidOperationException(
                    "The RGBA target did not expose an Evergine texture.");

            bool complete = commandContext.ConvertFrame(
                externalFrame, rgbaTarget, conversionPipeline, cameraUv);

            Log.Debug(
                Tag,
                "ARCore camera pipeline initialized. " +
                "First external YCbCr conversion submitted.");

            LogCameraUvIfChanged(
                cameraUv);

            LogPersistentHandles();

            return complete ? ImportedTexture : null;
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                "VK_ERROR_DEVICE_LOST",
                StringComparison.Ordinal))
        {
            vulkanDeviceLost =
                true;

            Log.Error(
                Tag,
                "VK_ERROR_DEVICE_LOST occurred while initializing the " +
                "ARCore Vulkan camera pipeline.");

            CleanupExternalResources();

            throw;
        }
        catch
        {
            CleanupExternalResources();
            throw;
        }
    }

    private Texture? UpdateExternalCameraFrame(
        HardwareBuffer hardwareBuffer,
        nint nativeHardwareBuffer,
        VulkanHardwareBufferQueryResult query,
        float[] cameraUv)
    {
        VulkanYcbcrResources currentYcbcr =
            ycbcrResources
            ?? throw new InvalidOperationException(
                "YCbCr resources are missing.");

        VulkanRgbaTarget currentTarget =
            rgbaTarget
            ?? throw new InvalidOperationException(
                "RGBA target is missing.");

        VulkanCameraConversionPipeline currentPipeline =
            conversionPipeline
            ?? throw new InvalidOperationException(
                "Camera conversion pipeline is missing.");

        VulkanCameraCommandContext currentCommands =
            commandContext
            ?? throw new InvalidOperationException(
                "Camera command context is missing.");

        Texture currentTexture =
            ImportedTexture
            ?? throw new InvalidOperationException(
                "The persistent Evergine camera texture is missing.");

        /*
         * The descriptor set and previous external image may still be in use
         * by the last submission. Wait before replacing the descriptor image
         * view or destroying that previous frame.
         */
        currentCommands.WaitForPreviousFrame();

        VulkanExternalFrame nextFrame = externalFrameImporter.Import(
            hardwareBuffer, nativeHardwareBuffer, query, currentYcbcr);
        try
        {
            currentPipeline.UpdateExternalFrame(
                nextFrame);
        }
        catch
        {
            nextFrame.Dispose();
            throw;
        }

        // The previous submission has completed. Transfer ownership BEFORE
        // submitting the next image, so a timeout cannot free an in-use image
        // or rewrite a descriptor still referenced by pending GPU commands.
        VulkanExternalFrame? previousFrame = externalFrame;
        externalFrame = nextFrame;
        previousFrame?.Dispose();
        bool complete = currentCommands.ConvertFrame(
            nextFrame, currentTarget, currentPipeline, cameraUv);
        LogCameraUvIfChanged(cameraUv);
        return complete ? currentTexture : null;
    }

    public Texture? TryCompletePendingImport()
    {
        ThrowIfNotDrawThread();
        ThrowIfDisposed();
        return commandContext?.TryCompleteFrame() == true ? ImportedTexture : null;
    }

    private bool HasCompleteExternalPipeline()
    {
        return ycbcrResources is not null &&
               externalFrame is not null &&
               rgbaTarget is not null &&
               conversionPipeline is not null &&
               commandContext is not null &&
               ImportedTexture is not null;
    }

    private bool RequiresExternalPipelineRebuild(
        uint sourceWidth,
        uint sourceHeight,
        uint outputWidth,
        uint outputHeight,
        VulkanHardwareBufferQueryResult query)
    {
        if (ycbcrResources is null ||
            externalFrame is null ||
            rgbaTarget is null)
        {
            return true;
        }

        VkAndroidHardwareBufferFormatPropertiesANDROID formatProperties =
            query.FormatProperties;

        if (!ycbcrResources.IsCompatible(
                ref formatProperties))
        {
            return true;
        }

        /*
         * The imported ARCore source frame and the persistent RGBA output are
         * intentionally allowed to have different dimensions. Rebuild only
         * when either the source geometry or requested viewport target changes.
         */
        if (externalFrame.Width != sourceWidth ||
            externalFrame.Height != sourceHeight)
        {
            return true;
        }

        if (rgbaTarget.Width != outputWidth ||
            rgbaTarget.Height != outputHeight)
        {
            return true;
        }

        return false;
    }

    private void LogCameraUvIfChanged(
        float[] cameraUv)
    {
        if (cameraUv.Length != 8)
        {
            return;
        }

        bool changed =
            !hasLoggedCameraUv;

        for (int index = 0;
             index < cameraUv.Length && !changed;
             index++)
        {
            changed =
                MathF.Abs(
                    lastLoggedCameraUv[index] - cameraUv[index]) >
                0.000001f;
        }

        if (!changed)
        {
            return;
        }

        Array.Copy(
            cameraUv,
            lastLoggedCameraUv,
            cameraUv.Length);

        hasLoggedCameraUv =
            true;

        Log.Info(
            Tag,
            "ARCORE_CAMERA_UV_TRANSFORM " +
            $"topLeft=({cameraUv[0]:F6},{cameraUv[1]:F6}); " +
            $"topRight=({cameraUv[2]:F6},{cameraUv[3]:F6}); " +
            $"bottomLeft=({cameraUv[4]:F6},{cameraUv[5]:F6}); " +
            $"bottomRight=({cameraUv[6]:F6},{cameraUv[7]:F6}).");
    }

    private void LogPersistentHandles()
    {
        if (externalFrame is null ||
            rgbaTarget is null ||
            conversionPipeline is null ||
            commandContext is null)
        {
            return;
        }

        Log.Debug(
            Tag,
            $"External Image = " +
            $"0x{externalFrame.Image.Handle:X}");

        Log.Debug(
            Tag,
            $"RGBA Image = " +
            $"0x{rgbaTarget.Image.Handle:X}");

        Log.Debug(
            Tag,
            $"DescriptorSet = " +
            $"0x{conversionPipeline.DescriptorSet.Handle:X}");

        Log.Debug(
            Tag,
            $"RenderPass = " +
            $"0x{conversionPipeline.RenderPass.Handle:X}");

        Log.Debug(
            Tag,
            $"Framebuffer = " +
            $"0x{conversionPipeline.Framebuffer.Handle:X}");

        Log.Debug(
            Tag,
            $"Pipeline = " +
            $"0x{conversionPipeline.GraphicsPipeline.Handle:X}");

        Log.Debug(
            Tag,
            $"CommandBuffer = " +
            $"0x{commandContext.CommandBuffer.Handle:X}");
    }

    private static ResourceFactory ResolveResourceFactory(
        VKGraphicsContext graphicsContext)
    {
        Type contextType =
            graphicsContext.GetType();

        PropertyInfo? property =
            contextType.GetProperty(
                "Factory",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

        property ??=
            contextType.GetProperty(
                "ResourceFactory",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

        if (property is null)
        {
            throw new InvalidOperationException(
                $"{contextType.FullName} does not expose a " +
                "Factory or ResourceFactory property.");
        }

        object? value =
            property.GetValue(
                graphicsContext);

        if (value is not ResourceFactory factory)
        {
            throw new InvalidOperationException(
                $"{property.Name} did not return a " +
                $"{typeof(ResourceFactory).FullName} instance.");
        }

        return factory;
    }

    private void CleanupExternalResources()
    {
        if (commandContext is not null && !commandContext.TryDrainForDisposal())
        {
            // Retain this generation's entire resource set until device teardown.
            // Destroying only the image/descriptor after a failed fence wait can
            // cause the subsequent VK_ERROR_DEVICE_LOST seen in field captures.
            return;
        }
        /*
         * Command work must finish before resources referenced by it are
         * destroyed. Keep the complete set if the fence has not completed.
         */
        commandContext?.Dispose();

        commandContext =
            null;

        /*
         * Pipeline owns the framebuffer and descriptor set, which reference
         * the RGBA target and external image view respectively.
         */
        conversionPipeline?.Dispose();

        conversionPipeline =
            null;

        externalFrame?.Dispose();

        externalFrame =
            null;

        /*
         * VulkanRgbaTarget owns ImportedTexture. Do not dispose the texture
         * separately or it would be double-disposed.
         */
        rgbaTarget?.Dispose();

        rgbaTarget =
            null;

        ycbcrResources?.Dispose();

        ycbcrResources =
            null;

        ImportedTexture =
            null;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(
                nameof(ARCoreVulkanImporter));
        }
    }

    internal void RebindDrawThread()
    {
        ThrowIfDisposed();
        drawThreadId = Environment.CurrentManagedThreadId;
    }

    private void ThrowIfNotDrawThread()
    {
        if (Environment.CurrentManagedThreadId != drawThreadId)
        {
            throw new InvalidOperationException(
                "ARCore Vulkan importer access must remain on its owning " +
                "Evergine draw thread.");
        }
    }

    public void QuiesceAndDisposeOnDrawThread()
    {
        ThrowIfNotDrawThread();

        if (disposed)
        {
            return;
        }

        WaitForGraphicsDeviceIdle(graphicsContext);

        Dispose();
    }

    public static void WaitForGraphicsDeviceIdle(
        VKGraphicsContext graphicsContext)
    {
        ArgumentNullException.ThrowIfNull(graphicsContext);

        VkDevice device =
            graphicsContext.VkDevice;

        if (device.Handle == 0)
        {
            return;
        }

        VkResult idleResult =
            VulkanNative.vkDeviceWaitIdle(device);

        if (idleResult != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"vkDeviceWaitIdle failed with {idleResult} during " +
                "ARCore graphics teardown.");
        }
    }

    public void Dispose()
    {
        ThrowIfNotDrawThread();

        if (disposed)
        {
            return;
        }

        CleanupExternalResources();

        disposed =
            true;

        GC.SuppressFinalize(
            this);
    }
}
