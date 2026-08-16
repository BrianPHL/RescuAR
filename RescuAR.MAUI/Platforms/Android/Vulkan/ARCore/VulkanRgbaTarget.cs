using Android.Util;
using Evergine.Bindings.Vulkan;
using Evergine.Common.Graphics;
using Evergine.Vulkan;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

/// <summary>
/// Owns the persistent RGBA image that receives the YCbCr-to-RGBA conversion
/// and the Evergine texture wrapper around that native Vulkan image.
///
/// The Vulkan render target remains UNORM while Evergine samples the same
/// stored camera values as sRGB. The native image is mutable-format so the
/// compatible sRGB interpretation is allowed.
/// </summary>
internal unsafe sealed class VulkanRgbaTarget : IDisposable
{
    private const string Tag = "RescuAR-Vulkan";

    private readonly VKGraphicsContext graphicsContext;
    private bool disposed;

    private VulkanRgbaTarget(
        VKGraphicsContext graphicsContext,
        VkImage image,
        VkDeviceMemory memory,
        VkImageView imageView,
        Texture texture,
        uint width,
        uint height)
    {
        this.graphicsContext = graphicsContext;
        Image = image;
        Memory = memory;
        ImageView = imageView;
        Texture = texture;
        Width = width;
        Height = height;
    }

    public VkImage Image { get; private set; }

    public VkDeviceMemory Memory { get; private set; }

    public VkImageView ImageView { get; private set; }

    public Texture? Texture { get; private set; }

    public uint Width { get; }

    public uint Height { get; }

    /*
     * The custom camera conversion render pass writes into an UNORM
     * attachment. Do not change this to SRGB: the Evergine wrapper below
     * provides the sRGB sampling interpretation.
     */
    public VkFormat Format =>
        VkFormat.VK_FORMAT_R8G8B8A8_UNORM;

    public static VulkanRgbaTarget Create(
        VKGraphicsContext graphicsContext,
        ResourceFactory resourceFactory,
        uint width,
        uint height)
    {
        ArgumentNullException.ThrowIfNull(graphicsContext);
        ArgumentNullException.ThrowIfNull(resourceFactory);

        if (width == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "RGBA output width must be nonzero.");
        }

        if (height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "RGBA output height must be nonzero.");
        }

        VkImage image = default;
        VkDeviceMemory memory = default;
        VkImageView imageView = default;
        Texture? texture = null;

        try
        {
            image =
                CreateImage(
                    graphicsContext,
                    width,
                    height);

            memory =
                AllocateAndBindMemory(
                    graphicsContext,
                    image);

            imageView =
                CreateImageView(
                    graphicsContext,
                    image);

            /*
             * Evergine samples the persistent camera texture as sRGB.
             * This preserves the improved tone/contrast obtained during
             * the camera color comparison while keeping the Vulkan
             * render attachment itself UNORM.
             */
            TextureDescription textureDescription =
                TextureDescription.CreateTexture2DDescription(
                    width,
                    height,
                    PixelFormat.R8G8B8A8_UNorm_SRgb);

            texture =
                resourceFactory.GetTextureFromNativePointer(
                    (nint)image.Handle,
                    ref textureDescription);

            if (texture is null)
            {
                throw new InvalidOperationException(
                    "Evergine returned a null texture wrapper for the " +
                    "converted RGBA camera image.");
            }

            Log.Debug(
                Tag,
                "RGBA camera target created: Vulkan UNORM render target, " +
                "Evergine sRGB sampling.");

            return new VulkanRgbaTarget(
                graphicsContext,
                image,
                memory,
                imageView,
                texture,
                width,
                height);
        }
        catch
        {
            texture?.Dispose();

            VkDevice device = graphicsContext.VkDevice;

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

            throw;
        }
    }

    private static VkImage CreateImage(
        VKGraphicsContext graphicsContext,
        uint width,
        uint height)
    {
        VkImageCreateInfo imageInfo =
            new()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
                pNext = null,

                /*
                 * Required so the UNORM storage can also be interpreted
                 * through the compatible sRGB texture format used by
                 * Evergine.
                 */
                flags =
                    VkImageCreateFlags
                        .VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT,

                imageType = VkImageType.VK_IMAGE_TYPE_2D,
                format = VkFormat.VK_FORMAT_R8G8B8A8_UNORM,

                extent =
                    new VkExtent3D
                    {
                        width = width,
                        height = height,
                        depth = 1
                    },

                mipLevels = 1,
                arrayLayers = 1,
                samples = VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT,
                tiling = VkImageTiling.VK_IMAGE_TILING_OPTIMAL,

                usage =
                    VkImageUsageFlags.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT |
                    VkImageUsageFlags.VK_IMAGE_USAGE_SAMPLED_BIT |
                    VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_SRC_BIT,

                sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE,
                queueFamilyIndexCount = 0,
                pQueueFamilyIndices = null,
                initialLayout = VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED
            };

        VkImage image = default;

        VkResult result =
            VulkanNative.vkCreateImage(
                graphicsContext.VkDevice,
                &imageInfo,
                null,
                &image);

        Log.Debug(
            Tag,
            $"vkCreateImage(RGBA target) returned {result}");

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"RGBA target vkCreateImage failed with {result}.");
        }

        return image;
    }

    private static VkDeviceMemory AllocateAndBindMemory(
        VKGraphicsContext graphicsContext,
        VkImage image)
    {
        VkMemoryRequirements requirements = new();

        VulkanNative.vkGetImageMemoryRequirements(
            graphicsContext.VkDevice,
            image,
            &requirements);

        VkPhysicalDeviceMemoryProperties memoryProperties = new();

        VulkanNative.vkGetPhysicalDeviceMemoryProperties(
            graphicsContext.VkPhysicalDevice,
            &memoryProperties);

        uint memoryTypeIndex =
            VulkanMemoryHelper.FindMemoryType(
                ref memoryProperties,
                requirements.memoryTypeBits);

        VkMemoryAllocateInfo allocationInfo =
            new()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                pNext = null,
                allocationSize = requirements.size,
                memoryTypeIndex = memoryTypeIndex
            };

        VkDeviceMemory memory = default;

        VkResult result =
            VulkanNative.vkAllocateMemory(
                graphicsContext.VkDevice,
                &allocationInfo,
                null,
                &memory);

        Log.Debug(
            Tag,
            $"vkAllocateMemory(RGBA target) returned {result}");

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"RGBA target vkAllocateMemory failed with {result}.");
        }

        result =
            VulkanNative.vkBindImageMemory(
                graphicsContext.VkDevice,
                image,
                memory,
                0);

        Log.Debug(
            Tag,
            $"vkBindImageMemory(RGBA target) returned {result}");

        if (result != VkResult.VK_SUCCESS)
        {
            VulkanNative.vkFreeMemory(
                graphicsContext.VkDevice,
                memory,
                null);

            throw new InvalidOperationException(
                $"RGBA target vkBindImageMemory failed with {result}.");
        }

        return memory;
    }

    private static VkImageView CreateImageView(
        VKGraphicsContext graphicsContext,
        VkImage image)
    {
        /*
         * The custom YCbCr conversion render pass uses this UNORM view.
         */
        VkImageViewCreateInfo viewInfo =
            new()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
                pNext = null,
                flags = 0,
                image = image,
                viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D,
                format = VkFormat.VK_FORMAT_R8G8B8A8_UNORM,

                components =
                    new VkComponentMapping
                    {
                        r = VkComponentSwizzle.VK_COMPONENT_SWIZZLE_IDENTITY,
                        g = VkComponentSwizzle.VK_COMPONENT_SWIZZLE_IDENTITY,
                        b = VkComponentSwizzle.VK_COMPONENT_SWIZZLE_IDENTITY,
                        a = VkComponentSwizzle.VK_COMPONENT_SWIZZLE_IDENTITY
                    },

                subresourceRange =
                    new VkImageSubresourceRange
                    {
                        aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT,
                        baseMipLevel = 0,
                        levelCount = 1,
                        baseArrayLayer = 0,
                        layerCount = 1
                    }
            };

        VkImageView imageView = default;

        VkResult result =
            VulkanNative.vkCreateImageView(
                graphicsContext.VkDevice,
                &viewInfo,
                null,
                &imageView);

        Log.Debug(
            Tag,
            $"vkCreateImageView(RGBA target) returned {result}");

        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"RGBA target vkCreateImageView failed with {result}.");
        }

        return imageView;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Texture?.Dispose();
        Texture = null;

        VkDevice device = graphicsContext.VkDevice;

        if (device.Handle != 0)
        {
            if (ImageView.Handle != 0)
            {
                VulkanNative.vkDestroyImageView(
                    device,
                    ImageView,
                    null);

                ImageView = default;
            }

            if (Image.Handle != 0)
            {
                VulkanNative.vkDestroyImage(
                    device,
                    Image,
                    null);

                Image = default;
            }

            if (Memory.Handle != 0)
            {
                VulkanNative.vkFreeMemory(
                    device,
                    Memory,
                    null);

                Memory = default;
            }
        }

        disposed = true;
    }
}
