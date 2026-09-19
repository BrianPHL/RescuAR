using Android.Hardware;
using Android.Util;
using Evergine.Bindings.Vulkan;
using Evergine.Common.Graphics;
using Evergine.Vulkan;
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
    private readonly int drawThreadId;
    private readonly float[] lastLoggedCameraUv = new float[8];

    private VulkanYcbcrResources? ycbcrResources;
    private VulkanExternalFrame? externalFrame;
    private VulkanRgbaTarget? rgbaTarget;
    private VulkanCameraConversionPipeline? conversionPipeline;
    private VulkanCameraCommandContext? commandContext;

    /*
     * These two fields are only used by the ordinary non-external fallback
     * path. ARCore on the tested Samsung device uses the external YCbCr path.
     */
    private VkImage directImage;
    private VkDeviceMemory directMemory;

    private bool imported;
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
    public Texture ImportHardwareBuffer(
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
    public Texture ImportHardwareBuffer(
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
    /// target whose dimensions match the actual Evergine display viewport.
    ///
    /// The source HardwareBuffer can remain 1920x1080 while the output target
    /// is portrait (for example 1080x1968). ARCore's transformed UVs select
    /// the correct crop/orientation, while the output target dimensions
    /// preserve the viewport aspect ratio without stretching.
    /// </summary>
    public Texture ImportHardwareBuffer(
        HardwareBuffer hardwareBuffer,
        float[] cameraUv,
        uint outputWidth,
        uint outputHeight)
    {
        ThrowIfNotDrawThread();
        ThrowIfDisposed();

        if (vulkanDeviceLost)
        {
            if (ImportedTexture is not null)
            {
                return ImportedTexture;
            }

            throw new InvalidOperationException(
                "The Vulkan camera path is unavailable because the Vulkan " +
                "device was previously lost.");
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

        if (query.FormatProperties.format ==
            VkFormat.VK_FORMAT_UNDEFINED)
        {
            throw new NotSupportedException(
                "The HardwareBuffer returned VK_FORMAT_UNDEFINED " +
                "without a valid external format.");
        }

        /*
         * The normal ARCore path on the tested device is external YCbCr.
         * Preserve the concrete-format fallback behavior as import-once.
         */
        if (imported)
        {
            return ImportedTexture
                ?? throw new InvalidOperationException(
                    "The importer is marked as imported, " +
                    "but ImportedTexture is null.");
        }

        return ImportOrdinaryBuffer(
            hardwareBuffer,
            nativeBuffer.Pointer,
            query);
    }

    private Texture ImportOrUpdateExternalCameraBuffer(
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
            /*
             * If a previous concrete-format fallback was initialized, remove
             * it before switching this importer to the external ARCore path.
             */
            if (directImage.Handle != 0 ||
                directMemory.Handle != 0)
            {
                CleanupDirectResources();
            }

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

            if (ImportedTexture is not null)
            {
                return ImportedTexture;
            }

            throw;
        }
    }

    private Texture InitializeExternalCameraPipeline(
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

            commandContext.ConvertFrame(
                externalFrame,
                rgbaTarget,
                conversionPipeline,
                cameraUv);

            ImportedTexture =
                rgbaTarget.Texture
                ?? throw new InvalidOperationException(
                    "The RGBA target did not expose an Evergine texture.");

            imported =
                true;

            Log.Debug(
                Tag,
                "ARCore camera pipeline initialized. " +
                "First external YCbCr frame converted to RGBA.");

            LogCameraUvIfChanged(
                cameraUv);

            LogPersistentHandles();

            return ImportedTexture;
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

    private Texture UpdateExternalCameraFrame(
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

        VulkanExternalFrame? nextFrame =
            null;

        try
        {
            nextFrame =
                externalFrameImporter.Import(
                    hardwareBuffer,
                    nativeHardwareBuffer,
                    query,
                    currentYcbcr);

            currentPipeline.UpdateExternalFrame(
                nextFrame);

            /*
             * ConvertFrame resets/re-records the reusable command buffer and
             * submits a fullscreen draw into the SAME persistent RGBA image.
             *
             * cameraUv is pushed into the fragment shader for this frame.
             *
             * ConvertFrame waits for completion before returning.
             */
            currentCommands.ConvertFrame(
                nextFrame,
                currentTarget,
                currentPipeline,
                cameraUv);

            VulkanExternalFrame? previousFrame =
                externalFrame;

            externalFrame =
                nextFrame;

            nextFrame =
                null;

            previousFrame?.Dispose();

            imported =
                true;

            LogCameraUvIfChanged(
                cameraUv);

            return currentTexture;
        }
        catch
        {
            nextFrame?.Dispose();

            /*
             * If the descriptor was already changed to the failed next frame,
             * restore the last valid image view when possible. No old frame is
             * destroyed until a new conversion has completed successfully.
             */
            if (externalFrame is not null &&
                !externalFrame.IsDisposed)
            {
                try
                {
                    currentPipeline.UpdateExternalFrame(
                        externalFrame);
                }
                catch
                {
                    // Preserve the original update failure.
                }
            }

            throw;
        }
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

    /// <summary>
    /// Retains the previous ordinary Vulkan-format fallback for HardwareBuffers
    /// that expose a concrete VkFormat rather than an Android external format.
    /// </summary>
    private Texture ImportOrdinaryBuffer(
        HardwareBuffer hardwareBuffer,
        nint nativeHardwareBuffer,
        VulkanHardwareBufferQueryResult query)
    {
        VkFormat format =
            query.FormatProperties.format;

        VkExternalMemoryImageCreateInfo externalMemoryInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO,

                pNext = null,

                handleTypes =
                    VkExternalMemoryHandleTypeFlags
                        .VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID
            };

        VkImageCreateInfo imageInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,

                pNext =
                    &externalMemoryInfo,

                flags = 0,

                imageType =
                    VkImageType.VK_IMAGE_TYPE_2D,

                format =
                    format,

                extent =
                    new VkExtent3D
                    {
                        width =
                            checked((uint)hardwareBuffer.Width),

                        height =
                            checked((uint)hardwareBuffer.Height),

                        depth = 1
                    },

                mipLevels = 1,
                arrayLayers = 1,

                samples =
                    VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT,

                tiling =
                    VkImageTiling.VK_IMAGE_TILING_OPTIMAL,

                usage =
                    VkImageUsageFlags.VK_IMAGE_USAGE_SAMPLED_BIT,

                sharingMode =
                    VkSharingMode.VK_SHARING_MODE_EXCLUSIVE,

                queueFamilyIndexCount = 0,
                pQueueFamilyIndices = null,

                initialLayout =
                    VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED
            };

        VkImage createdImage =
            default;

        VkResult result =
            VulkanNative.vkCreateImage(
                graphicsContext.VkDevice,
                &imageInfo,
                null,
                &createdImage);

        Log.Debug(
            Tag,
            $"vkCreateImage returned {result}");

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"vkCreateImage failed with {result}.");
        }

        directImage =
            createdImage;

        try
        {
            VkMemoryDedicatedAllocateInfo dedicatedInfo =
                new()
                {
                    sType =
                        VkStructureType
                            .VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO,

                    pNext = null,

                    image =
                        directImage,

                    buffer =
                        default
                };

            VkImportAndroidHardwareBufferInfoANDROID importInfo =
                new()
                {
                    sType =
                        VkStructureType
                            .VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID,

                    pNext =
                        &dedicatedInfo,

                    buffer =
                        nativeHardwareBuffer
                };

            VkPhysicalDeviceMemoryProperties memoryProperties =
                new();

            VulkanNative.vkGetPhysicalDeviceMemoryProperties(
                graphicsContext.VkPhysicalDevice,
                &memoryProperties);

            uint memoryTypeIndex =
                VulkanMemoryHelper.FindMemoryType(
                    ref memoryProperties,
                    query.MemoryTypeBits);

            VkMemoryAllocateInfo allocationInfo =
                new()
                {
                    sType =
                        VkStructureType
                            .VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,

                    pNext =
                        &importInfo,

                    allocationSize =
                        query.AllocationSize,

                    memoryTypeIndex =
                        memoryTypeIndex
                };

            VkDeviceMemory createdMemory =
                default;

            result =
                VulkanNative.vkAllocateMemory(
                    graphicsContext.VkDevice,
                    &allocationInfo,
                    null,
                    &createdMemory);

            Log.Debug(
                Tag,
                $"vkAllocateMemory returned {result}");

            if (result != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException(
                    $"vkAllocateMemory failed with {result}.");
            }

            directMemory =
                createdMemory;

            VkBindImageMemoryInfo bindInfo =
                new()
                {
                    sType =
                        VkStructureType
                            .VK_STRUCTURE_TYPE_BIND_IMAGE_MEMORY_INFO,

                    pNext = null,

                    image =
                        directImage,

                    memory =
                        directMemory,

                    memoryOffset = 0
                };

            result =
                VulkanNative.vkBindImageMemory2(
                    graphicsContext.VkDevice,
                    1,
                    &bindInfo);

            Log.Debug(
                Tag,
                $"vkBindImageMemory2 returned {result}");

            if (result != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException(
                    $"vkBindImageMemory2 failed with {result}.");
            }

            PixelFormat pixelFormat =
                ConvertVulkanFormatToEvergine(
                    format);

            TextureDescription textureDescription =
                TextureDescription.CreateTexture2DDescription(
                    checked((uint)hardwareBuffer.Width),
                    checked((uint)hardwareBuffer.Height),
                    pixelFormat);

            ImportedTexture =
                resourceFactory.GetTextureFromNativePointer(
                    (nint)directImage.Handle,
                    ref textureDescription);

            if (ImportedTexture is null)
            {
                throw new InvalidOperationException(
                    "Evergine returned a null texture wrapper.");
            }

            imported =
                true;

            Log.Debug(
                Tag,
                "Evergine texture wrapper created successfully.");

            return ImportedTexture;
        }
        catch
        {
            CleanupDirectResources();
            throw;
        }
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

    private static PixelFormat ConvertVulkanFormatToEvergine(
        VkFormat format)
    {
        if (format ==
            VkFormat.VK_FORMAT_R8G8B8A8_UNORM)
        {
            return PixelFormat.R8G8B8A8_UNorm;
        }

        throw new NotSupportedException(
            $"No Evergine PixelFormat mapping exists " +
            $"for Vulkan format {format}.");
    }

    private void CleanupExternalResources()
    {
        /*
         * Command work must finish before resources referenced by it are
         * destroyed. CommandContext.Dispose() performs a defensive fence wait.
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

        imported =
            false;
    }

    private void CleanupDirectResources()
    {
        /*
         * The direct fallback texture wrapper does not belong to a helper
         * object, so this path owns it directly.
         */
        ImportedTexture?.Dispose();

        ImportedTexture =
            null;

        VkDevice device =
            graphicsContext.VkDevice;

        if (device.Handle != 0)
        {
            if (directImage.Handle != 0)
            {
                VulkanNative.vkDestroyImage(
                    device,
                    directImage,
                    null);

                directImage =
                    default;
            }

            if (directMemory.Handle != 0)
            {
                VulkanNative.vkFreeMemory(
                    device,
                    directMemory,
                    null);

                directMemory =
                    default;
            }
        }

        imported =
            false;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(
                nameof(ARCoreVulkanImporter));
        }
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
        CleanupDirectResources();

        disposed =
            true;

        GC.SuppressFinalize(
            this);
    }
}
