using Android.Hardware;
using Android.Util;
using Evergine.Bindings.Vulkan;
using Evergine.Vulkan;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

/// <summary>
/// Scalar Vulkan properties reported for one Android HardwareBuffer query.
/// The pNext chain used during the native query remains internal to the query
/// method; only values needed by later import stages are retained here.
/// </summary>
internal readonly struct VulkanHardwareBufferQueryResult
{
    public VulkanHardwareBufferQueryResult(
        ulong allocationSize,
        uint memoryTypeBits,
        VkAndroidHardwareBufferFormatPropertiesANDROID formatProperties)
    {
        AllocationSize = allocationSize;
        MemoryTypeBits = memoryTypeBits;
        FormatProperties = formatProperties;
    }

    public ulong AllocationSize { get; }

    public uint MemoryTypeBits { get; }

    public VkAndroidHardwareBufferFormatPropertiesANDROID FormatProperties { get; }

    public bool UsesExternalFormat =>
        FormatProperties.format == VkFormat.VK_FORMAT_UNDEFINED &&
        FormatProperties.externalFormat != 0;
}

/// <summary>
/// Imports one ARCore Android HardwareBuffer as a Vulkan external image.
///
/// This class owns no long-lived Vulkan resources. On success, ownership of
/// the imported VkImage, VkDeviceMemory, and VkImageView is transferred to
/// the returned VulkanExternalFrame instance.
/// </summary>
internal unsafe sealed class VulkanExternalFrameImporter
{
    private const string Tag = "RescuAR-Vulkan";

    private readonly VKGraphicsContext graphicsContext;

    private bool hasLoggedFormatProperties;

    public VulkanExternalFrameImporter(
        VKGraphicsContext graphicsContext)
    {
        this.graphicsContext =
            graphicsContext
            ?? throw new ArgumentNullException(
                nameof(graphicsContext));
    }

    /// <summary>
    /// Queries Vulkan's Android-HardwareBuffer properties and logs the same
    /// diagnostics used by the previously validated monolithic importer.
    /// </summary>
    public VulkanHardwareBufferQueryResult QueryProperties(
        HardwareBuffer hardwareBuffer,
        nint nativeHardwareBuffer)
    {
        ArgumentNullException.ThrowIfNull(
            hardwareBuffer);

        if (nativeHardwareBuffer == nint.Zero)
        {
            throw new ArgumentException(
                "Native AHardwareBuffer pointer is zero.",
                nameof(nativeHardwareBuffer));
        }

        VkAndroidHardwareBufferFormatPropertiesANDROID formatProperties =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID,

                pNext = null
            };

        VkAndroidHardwareBufferPropertiesANDROID properties =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID,

                pNext = &formatProperties
            };

        VkResult result =
            VulkanNative
                .vkGetAndroidHardwareBufferPropertiesANDROID(
                    graphicsContext.VkDevice,
                    nativeHardwareBuffer,
                    &properties);

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                "vkGetAndroidHardwareBufferPropertiesANDROID " +
                $"failed with {result}.");
        }

        if (!hasLoggedFormatProperties)
        { 
            Log.Debug(
                Tag,
                $"Vulkan format = {formatProperties.format}");

            Log.Debug(
                Tag,
                $"External format = 0x{formatProperties.externalFormat:X}");

            Log.Debug(
                Tag,
                $"Format features = {formatProperties.formatFeatures}");

            Log.Debug(
                Tag,
                $"YCbCr components = " +
                $"R:{formatProperties.samplerYcbcrConversionComponents.r}, " +
                $"G:{formatProperties.samplerYcbcrConversionComponents.g}, " +
                $"B:{formatProperties.samplerYcbcrConversionComponents.b}, " +
                $"A:{formatProperties.samplerYcbcrConversionComponents.a}");

            Log.Debug(
                Tag,
                $"YCbCr model = {formatProperties.suggestedYcbcrModel}");

            Log.Debug(
                Tag,
                $"YCbCr range = {formatProperties.suggestedYcbcrRange}");

            Log.Debug(
                Tag,
                $"X chroma offset = {formatProperties.suggestedXChromaOffset}");

            Log.Debug(
                Tag,
                $"Y chroma offset = {formatProperties.suggestedYChromaOffset}");

            hasLoggedFormatProperties = true;
        }

            /*
             * formatProperties.pNext is null, so copying this value out of the
             * stack-local query chain is safe. properties.pNext is intentionally
             * not exposed because it points at this stack-local structure.
             */
            return new VulkanHardwareBufferQueryResult(
                properties.allocationSize,
                properties.memoryTypeBits,
                formatProperties);
        }

    /// <summary>
    /// Imports an external-format ARCore camera buffer.
    ///
    /// The supplied VulkanYcbcrResources must have been created from format
    /// properties compatible with this buffer.
    /// </summary>
    public VulkanExternalFrame Import(
        HardwareBuffer hardwareBuffer,
        nint nativeHardwareBuffer,
        in VulkanHardwareBufferQueryResult query,
        VulkanYcbcrResources ycbcrResources)
    {
        ArgumentNullException.ThrowIfNull(
            hardwareBuffer);

        ArgumentNullException.ThrowIfNull(
            ycbcrResources);

        if (nativeHardwareBuffer == nint.Zero)
        {
            throw new ArgumentException(
                "Native AHardwareBuffer pointer is zero.",
                nameof(nativeHardwareBuffer));
        }

        if (!query.UsesExternalFormat)
        {
            throw new NotSupportedException(
                "VulkanExternalFrameImporter only handles " +
                "VK_FORMAT_UNDEFINED Android external formats.");
        }

        ulong externalFormat =
            query.FormatProperties.externalFormat;

        if (ycbcrResources.ExternalFormat != externalFormat)
        {
            throw new InvalidOperationException(
                "The YCbCr resources are not compatible with this " +
                "HardwareBuffer external format. " +
                $"Expected 0x{ycbcrResources.ExternalFormat:X}, " +
                $"received 0x{externalFormat:X}.");
        }

        uint width =
            checked((uint)hardwareBuffer.Width);

        uint height =
            checked((uint)hardwareBuffer.Height);

        VkImage image =
            default;

        VkDeviceMemory memory =
            default;

        VkImageView imageView =
            default;

        try
        {
            image =
                CreateExternalImage(
                    width,
                    height,
                    externalFormat);

            memory =
                ImportExternalMemory(
                    nativeHardwareBuffer,
                    image,
                    query.AllocationSize,
                    query.MemoryTypeBits);

            imageView =
                CreateExternalImageView(
                    image,
                    ycbcrResources.Conversion);

            return new VulkanExternalFrame(
                graphicsContext,
                image,
                memory,
                imageView,
                externalFormat,
                width,
                height);
        }
        catch
        {
            CleanupPartialImport(
                imageView,
                image,
                memory);

            throw;
        }
    }

    private VkImage CreateExternalImage(
        uint width,
        uint height,
        ulong externalFormat)
    {
        if (externalFormat == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(externalFormat),
                "External format cannot be zero.");
        }

        VkExternalFormatANDROID externalFormatInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID,

                pNext = null,

                externalFormat =
                    externalFormat
            };

        VkExternalMemoryImageCreateInfo externalMemoryInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO,

                pNext =
                    &externalFormatInfo,

                handleTypes =
                    VkExternalMemoryHandleTypeFlags
                        .VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID
            };

        VkImageCreateInfo imageCreateInfo =
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

                /*
                 * Android implementation-defined camera formats are
                 * represented through VkExternalFormatANDROID.
                 */
                format =
                    VkFormat.VK_FORMAT_UNDEFINED,

                extent =
                    new VkExtent3D
                    {
                        width = width,
                        height = height,
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

        VkImage image =
            default;

        VkResult result =
            VulkanNative.vkCreateImage(
                graphicsContext.VkDevice,
                &imageCreateInfo,
                null,
                &image);

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"External vkCreateImage failed with {result}.");
        }

        return image;
    }

    private VkDeviceMemory ImportExternalMemory(
        nint nativeHardwareBuffer,
        VkImage image,
        ulong allocationSize,
        uint memoryTypeBits)
    {
        if (image.Handle == 0)
        {
            throw new ArgumentException(
                "Cannot import HardwareBuffer memory for a null VkImage.",
                nameof(image));
        }

        VkMemoryDedicatedAllocateInfo dedicatedInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO,

                pNext = null,

                image =
                    image,

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
                memoryTypeBits);

        VkMemoryAllocateInfo allocationInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,

                pNext =
                    &importInfo,

                allocationSize =
                    allocationSize,

                memoryTypeIndex =
                    memoryTypeIndex
            };

        VkDeviceMemory memory =
            default;

        VkResult result =
            VulkanNative.vkAllocateMemory(
                graphicsContext.VkDevice,
                &allocationInfo,
                null,
                &memory);

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"External vkAllocateMemory failed with {result}.");
        }

        try
        {
            VkBindImageMemoryInfo bindInfo =
                new()
                {
                    sType =
                        VkStructureType
                            .VK_STRUCTURE_TYPE_BIND_IMAGE_MEMORY_INFO,

                    pNext = null,

                    image =
                        image,

                    memory =
                        memory,

                    memoryOffset = 0
                };

            result =
                VulkanNative.vkBindImageMemory2(
                    graphicsContext.VkDevice,
                    1,
                    &bindInfo);

            if (result != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException(
                    $"External vkBindImageMemory2 failed with {result}.");
            }

            return memory;
        }
        catch
        {
            if (memory.Handle != 0)
            {
                VulkanNative.vkFreeMemory(
                    graphicsContext.VkDevice,
                    memory,
                    null);
            }

            throw;
        }
    }

    private VkImageView CreateExternalImageView(
        VkImage image,
        VkSamplerYcbcrConversion conversion)
    {
        if (image.Handle == 0)
        {
            throw new InvalidOperationException(
                "Cannot create an external image view before " +
                "the image exists.");
        }

        if (conversion.Handle == 0)
        {
            throw new InvalidOperationException(
                "Cannot create an external image view before " +
                "the YCbCr conversion exists.");
        }

        VkSamplerYcbcrConversionInfo conversionInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO,

                pNext = null,

                conversion =
                    conversion
            };

        VkImageViewCreateInfo imageViewCreateInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,

                pNext =
                    &conversionInfo,

                flags = 0,

                image =
                    image,

                viewType =
                    VkImageViewType.VK_IMAGE_VIEW_TYPE_2D,

                format =
                    VkFormat.VK_FORMAT_UNDEFINED,

                components =
                    new VkComponentMapping
                    {
                        r =
                            VkComponentSwizzle
                                .VK_COMPONENT_SWIZZLE_IDENTITY,

                        g =
                            VkComponentSwizzle
                                .VK_COMPONENT_SWIZZLE_IDENTITY,

                        b =
                            VkComponentSwizzle
                                .VK_COMPONENT_SWIZZLE_IDENTITY,

                        a =
                            VkComponentSwizzle
                                .VK_COMPONENT_SWIZZLE_IDENTITY
                    },

                subresourceRange =
                    new VkImageSubresourceRange
                    {
                        aspectMask =
                            VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT,

                        baseMipLevel = 0,

                        levelCount = 1,

                        baseArrayLayer = 0,

                        layerCount = 1
                    }
            };

        VkImageView imageView =
            default;

        VkResult result =
            VulkanNative.vkCreateImageView(
                graphicsContext.VkDevice,
                &imageViewCreateInfo,
                null,
                &imageView);

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"External vkCreateImageView failed with {result}.");
        }

        return imageView;
    }

    private void CleanupPartialImport(
        VkImageView imageView,
        VkImage image,
        VkDeviceMemory memory)
    {
        VkDevice device =
            graphicsContext.VkDevice;

        if (device.Handle == 0)
        {
            return;
        }

        if (imageView.Handle != 0)
        {
            VulkanNative.vkDestroyImageView(
                device,
                imageView,
                null);
        }

        if (image.Handle != 0)
        {
            VulkanNative.vkDestroyImage(
                device,
                image,
                null);
        }

        if (memory.Handle != 0)
        {
            VulkanNative.vkFreeMemory(
                device,
                memory,
                null);
        }
    }
}
