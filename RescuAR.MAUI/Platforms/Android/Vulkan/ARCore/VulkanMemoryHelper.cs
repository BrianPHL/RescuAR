using Evergine.Bindings.Vulkan;
using System;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

internal static class VulkanMemoryHelper
{
    public static uint FindMemoryType(
        ref VkPhysicalDeviceMemoryProperties properties,
        uint typeBits)
    {
        for (uint index = 0;
             index < properties.memoryTypeCount;
             index++)
        {
            uint mask =
                1u << checked((int)index);

            if ((typeBits & mask) != 0)
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            "Unable to locate a compatible Vulkan memory type.");
    }
}
