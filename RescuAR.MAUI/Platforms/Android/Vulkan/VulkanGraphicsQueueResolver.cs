using System.Reflection;
using Android.Util;
using Evergine.Bindings.Vulkan;
using Evergine.Vulkan;

namespace RescuAR.MAUI.Platforms.Android.Vulkan;

/// <summary>
/// Resolves the graphics queue that Evergine actually created and owns.
///
/// Do not independently select a physical-device queue family and call
/// vkGetDeviceQueue here. A physical device can expose queue families that
/// were not enabled when Evergine created the logical device. Reusing the
/// queue stored by VKGraphicsContext keeps the AR camera path attached to the
/// same graphics queue/family selected by Evergine.
/// </summary>
internal sealed class VulkanGraphicsQueueResolver
{
    private const string Tag =
        "RescuAR-VulkanQueue";

    private const BindingFlags InstanceMemberFlags =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic;

    public VkQueue Queue { get; }

    public uint QueueFamilyIndex { get; }

    public VulkanGraphicsQueueResolver(
        VKGraphicsContext graphicsContext)
    {
        ArgumentNullException.ThrowIfNull(
            graphicsContext);

        Queue =
            ResolveEvergineGraphicsQueue(
                graphicsContext);

        QueueFamilyIndex =
            ResolveEvergineGraphicsQueueFamilyIndex(
                graphicsContext);

        if (Queue.Handle == 0)
        {
            throw new InvalidOperationException(
                "Evergine's Vulkan graphics queue is null.");
        }

        Log.Debug(
            Tag,
            "Using Evergine-owned graphics queue: " +
            $"family={QueueFamilyIndex}, " +
            $"queue=0x{Queue.Handle:X}");
    }

    private static VkQueue ResolveEvergineGraphicsQueue(
        VKGraphicsContext graphicsContext)
    {
        Type contextType =
            graphicsContext.GetType();

        FieldInfo? queueField =
            contextType.GetField(
                "vkGraphicsQueue",
                InstanceMemberFlags);

        if (queueField?.GetValue(graphicsContext) is VkQueue queue)
        {
            return queue;
        }

        /*
         * Keep a small compatibility fallback for Evergine versions that
         * expose the same value as a property instead of the currently
         * observed private field.
         */
        PropertyInfo? queueProperty =
            contextType.GetProperty(
                "VkGraphicsQueue",
                InstanceMemberFlags)
            ?? contextType.GetProperty(
                "GraphicsQueue",
                InstanceMemberFlags);

        if (queueProperty?.GetValue(graphicsContext) is VkQueue propertyQueue)
        {
            return propertyQueue;
        }

        throw new InvalidOperationException(
            "Unable to resolve Evergine's graphics VkQueue from " +
            "VKGraphicsContext. The AR camera path will not guess a queue.");
    }

    private static uint ResolveEvergineGraphicsQueueFamilyIndex(
        VKGraphicsContext graphicsContext)
    {
        Type contextType =
            graphicsContext.GetType();

        object? queueIndices =
            contextType
                .GetField(
                    "QueueIndices",
                    InstanceMemberFlags)
                ?.GetValue(graphicsContext)
            ?? contextType
                .GetProperty(
                    "QueueIndices",
                    InstanceMemberFlags)
                ?.GetValue(graphicsContext);

        if (queueIndices is null)
        {
            throw new InvalidOperationException(
                "Unable to resolve Evergine's Vulkan QueueIndices.");
        }

        if (TryReadGraphicsIndex(
            queueIndices,
            out uint graphicsIndex))
        {
            return graphicsIndex;
        }

        throw new InvalidOperationException(
            "Unable to resolve Evergine's graphics queue-family index " +
            "from VKQueueFamilyIndices. The AR camera path will not guess " +
            "a queue family.");
    }

    private static bool TryReadGraphicsIndex(
        object queueIndices,
        out uint index)
    {
        Type indicesType =
            queueIndices.GetType();

        /*
         * First try common Evergine-style names explicitly. These are kept
         * reflection-based because VKQueueFamilyIndices is not part of the
         * public GraphicsContext API in the version currently used by the
         * project.
         */
        string[] preferredNames =
        [
            "GraphicsFamily",
            "GraphicsFamilyIndex",
            "GraphicsQueueFamily",
            "GraphicsQueueFamilyIndex",
            "Graphics"
        ];

        foreach (string name in preferredNames)
        {
            FieldInfo? field =
                indicesType.GetField(
                    name,
                    InstanceMemberFlags);

            if (field is not null &&
                TryConvertIndex(
                    field.GetValue(queueIndices),
                    out index))
            {
                return true;
            }

            PropertyInfo? property =
                indicesType.GetProperty(
                    name,
                    InstanceMemberFlags);

            if (property is not null &&
                TryConvertIndex(
                    property.GetValue(queueIndices),
                    out index))
            {
                return true;
            }
        }

        /*
         * Compatibility fallback: accept a numeric member whose name clearly
         * identifies it as the graphics queue index/family.
         */
        foreach (FieldInfo field in
                 indicesType.GetFields(
                     InstanceMemberFlags))
        {
            if (!field.Name.Contains(
                    "graphics",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryConvertIndex(
                field.GetValue(queueIndices),
                out index))
            {
                return true;
            }
        }

        foreach (PropertyInfo property in
                 indicesType.GetProperties(
                     InstanceMemberFlags))
        {
            if (!property.Name.Contains(
                    "graphics",
                    StringComparison.OrdinalIgnoreCase) ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (TryConvertIndex(
                property.GetValue(queueIndices),
                out index))
            {
                return true;
            }
        }

        index = 0;
        return false;
    }

    private static bool TryConvertIndex(
        object? value,
        out uint index)
    {
        switch (value)
        {
            case uint unsignedIndex:
                index = unsignedIndex;
                return true;

            case int signedIndex when signedIndex >= 0:
                index = checked((uint)signedIndex);
                return true;

            case ulong unsignedLongIndex
                when unsignedLongIndex <= uint.MaxValue:
                index = checked((uint)unsignedLongIndex);
                return true;

            case long signedLongIndex
                when signedLongIndex >= 0 &&
                     signedLongIndex <= uint.MaxValue:
                index = checked((uint)signedLongIndex);
                return true;
        }

        Type? valueType =
            value?.GetType();

        Type? underlyingType =
            valueType is null
                ? null
                : Nullable.GetUnderlyingType(valueType);

        if (underlyingType is not null)
        {
            object? nullableValue =
                valueType!
                    .GetProperty("Value")
                    ?.GetValue(value);

            return TryConvertIndex(
                nullableValue,
                out index);
        }

        index = 0;
        return false;
    }
}
