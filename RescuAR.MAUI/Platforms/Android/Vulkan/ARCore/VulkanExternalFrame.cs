using Evergine.Bindings.Vulkan;
using Evergine.Vulkan;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

/// <summary>
/// Owns the Vulkan resources associated with one imported ARCore camera frame.
/// This object is intentionally limited to per-frame external-image resources.
/// </summary>
internal unsafe sealed class VulkanExternalFrame : IDisposable
{
    private readonly VKGraphicsContext graphicsContext;
    private bool disposed;

    internal VulkanExternalFrame(
        VKGraphicsContext graphicsContext,
        VkImage image,
        VkDeviceMemory memory,
        VkImageView imageView,
        ulong externalFormat,
        uint width,
        uint height)
    {
        this.graphicsContext =
            graphicsContext
            ?? throw new ArgumentNullException(nameof(graphicsContext));

        Image = image;
        Memory = memory;
        ImageView = imageView;
        ExternalFormat = externalFormat;
        Width = width;
        Height = height;
    }

    public VkImage Image { get; private set; }

    public VkDeviceMemory Memory { get; private set; }

    public VkImageView ImageView { get; private set; }

    public ulong ExternalFormat { get; }

    public uint Width { get; }

    public uint Height { get; }

    public bool IsDisposed => disposed;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

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
