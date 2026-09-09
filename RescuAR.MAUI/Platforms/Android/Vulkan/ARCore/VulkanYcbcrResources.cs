using Android.Util;
using Evergine.Bindings.Vulkan;
using Evergine.Vulkan;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

/// <summary>
/// Owns the Vulkan YCbCr conversion and sampler used to sample ARCore's
/// implementation-defined Android camera format.
///
/// The Samsung A54 camera stream used by this project is rendered with
/// BT.601 narrow-range conversion. This was selected after direct visual
/// comparison against the ARCore CPU image and the device camera preview.
/// </summary>
internal unsafe sealed class VulkanYcbcrResources : IDisposable
{
    private const string Tag = "RescuAR-Vulkan";

    private readonly VKGraphicsContext graphicsContext;
    private bool disposed;

    private VulkanYcbcrResources(
        VKGraphicsContext graphicsContext,
        VkSamplerYcbcrConversion conversion,
        VkSampler sampler,
        ulong externalFormat)
    {
        this.graphicsContext = graphicsContext;
        Conversion = conversion;
        Sampler = sampler;
        ExternalFormat = externalFormat;
    }

    public VkSamplerYcbcrConversion Conversion { get; private set; }

    public VkSampler Sampler { get; private set; }

    public ulong ExternalFormat { get; }

    public static VulkanYcbcrResources Create(
        VKGraphicsContext graphicsContext,
        ref VkAndroidHardwareBufferFormatPropertiesANDROID formatProperties)
    {
        ArgumentNullException.ThrowIfNull(graphicsContext);

        if (formatProperties.externalFormat == 0)
        {
            throw new InvalidOperationException(
                "Cannot create external YCbCr resources because " +
                "externalFormat is zero.");
        }

        VkSamplerYcbcrConversion conversion =
            CreateConversion(
                graphicsContext,
                ref formatProperties);

        try
        {
            VkSampler sampler =
                CreateSampler(
                    graphicsContext,
                    conversion);

            return new VulkanYcbcrResources(
                graphicsContext,
                conversion,
                sampler,
                formatProperties.externalFormat);
        }
        catch
        {
            if (conversion.Handle != 0)
            {
                VulkanNative.vkDestroySamplerYcbcrConversion(
                    graphicsContext.VkDevice,
                    conversion,
                    null);
            }

            throw;
        }
    }

    private static VkSamplerYcbcrConversion CreateConversion(
        VKGraphicsContext graphicsContext,
        ref VkAndroidHardwareBufferFormatPropertiesANDROID formatProperties)
    {
        VkExternalFormatANDROID externalFormatInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID,

                pNext = null,
                externalFormat = formatProperties.externalFormat
            };

        /*
         * Production camera color configuration.
         *
         * The driver reports BT.601 + ITU_FULL for this external format,
         * but repeated visual comparison showed that BT.601 + ITU_NARROW
         * provides substantially better black levels, contrast, and overall
         * appearance in the RescuAR passthrough on the tested device.
         *
         * Component mapping and chroma locations remain driver-provided.
         */
        VkSamplerYcbcrModelConversion selectedModel =
            VkSamplerYcbcrModelConversion
                .VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601;

        VkSamplerYcbcrRange selectedRange =
            VkSamplerYcbcrRange
                .VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;

        Log.Debug(
            Tag,
            "Camera YCbCr conversion: BT.601 / ITU_NARROW.");

        VkSamplerYcbcrConversionCreateInfo conversionCreateInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO,

                pNext = &externalFormatInfo,
                format = VkFormat.VK_FORMAT_UNDEFINED,
                components = formatProperties.samplerYcbcrConversionComponents,
                ycbcrModel = selectedModel,
                ycbcrRange = selectedRange,
                xChromaOffset = formatProperties.suggestedXChromaOffset,
                yChromaOffset = formatProperties.suggestedYChromaOffset,
                chromaFilter = VkFilter.VK_FILTER_LINEAR,
                forceExplicitReconstruction = 0
            };

        VkSamplerYcbcrConversion conversion = default;

        VkResult result =
            VulkanNative.vkCreateSamplerYcbcrConversion(
                graphicsContext.VkDevice,
                &conversionCreateInfo,
                null,
                &conversion);

        Log.Debug(
            Tag,
            $"vkCreateSamplerYcbcrConversion returned {result}");

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                "vkCreateSamplerYcbcrConversion failed " +
                $"with {result}.");
        }

        Log.Debug(
            Tag,
            $"VkSamplerYcbcrConversion = 0x{conversion.Handle:X}");

        return conversion;
    }

    private static VkSampler CreateSampler(
        VKGraphicsContext graphicsContext,
        VkSamplerYcbcrConversion conversion)
    {
        if (conversion.Handle == 0)
        {
            throw new InvalidOperationException(
                "Cannot create an external sampler before the " +
                "YCbCr conversion exists.");
        }

        VkSamplerYcbcrConversionInfo conversionInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO,

                pNext = null,
                conversion = conversion
            };

        VkSamplerCreateInfo samplerCreateInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO,

                pNext = &conversionInfo,
                flags = 0,
                magFilter = VkFilter.VK_FILTER_LINEAR,
                minFilter = VkFilter.VK_FILTER_LINEAR,
                mipmapMode = VkSamplerMipmapMode.VK_SAMPLER_MIPMAP_MODE_NEAREST,
                addressModeU = VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
                addressModeV = VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
                addressModeW = VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
                mipLodBias = 0,
                anisotropyEnable = 0,
                maxAnisotropy = 1,
                compareEnable = 0,
                compareOp = VkCompareOp.VK_COMPARE_OP_ALWAYS,
                minLod = 0,
                maxLod = 0,
                borderColor = VkBorderColor.VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK,
                unnormalizedCoordinates = 0
            };

        VkSampler sampler = default;

        VkResult result =
            VulkanNative.vkCreateSampler(
                graphicsContext.VkDevice,
                &samplerCreateInfo,
                null,
                &sampler);

        Log.Debug(
            Tag,
            $"vkCreateSampler(external) returned {result}");

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"External vkCreateSampler failed with {result}.");
        }

        Log.Debug(
            Tag,
            $"External VkSampler = 0x{sampler.Handle:X}");

        return sampler;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        VkDevice device = graphicsContext.VkDevice;

        if (device.Handle != 0)
        {
            if (Sampler.Handle != 0)
            {
                VulkanNative.vkDestroySampler(
                    device,
                    Sampler,
                    null);

                Sampler = default;
            }

            if (Conversion.Handle != 0)
            {
                VulkanNative.vkDestroySamplerYcbcrConversion(
                    device,
                    Conversion,
                    null);

                Conversion = default;
            }
        }

        disposed = true;
    }
}
