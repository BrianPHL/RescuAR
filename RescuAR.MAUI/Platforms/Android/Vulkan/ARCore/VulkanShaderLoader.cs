using Android.Util;
using Evergine.Bindings.Vulkan;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

internal static unsafe class VulkanShaderLoader
{
    private const string Tag = "RescuAR-Vulkan";

    public static VkShaderModule LoadEmbeddedShaderModule(
        VkDevice device,
        Assembly assembly,
        string resourceSuffix,
        string label)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        Log.Debug(
            Tag,
            $"Loading shader resource '{resourceSuffix}' for {label}...");

        byte[] spirvBytes =
            LoadEmbeddedResourceBySuffix(
                assembly,
                resourceSuffix);

        Log.Debug(
            Tag,
            $"Loaded {label} shader: {spirvBytes.Length} bytes.");

        return CreateShaderModule(
            device,
            spirvBytes,
            label);
    }

    private static VkShaderModule CreateShaderModule(
        VkDevice device,
        byte[] spirvBytes,
        string label)
    {
        if (spirvBytes.Length == 0 ||
            spirvBytes.Length % sizeof(uint) != 0)
        {
            throw new InvalidDataException(
                $"The {label} SPIR-V resource has an invalid byte length: " +
                $"{spirvBytes.Length}.");
        }

        uint[] spirvWords =
            new uint[spirvBytes.Length / sizeof(uint)];

        Buffer.BlockCopy(
            spirvBytes,
            0,
            spirvWords,
            0,
            spirvBytes.Length);

        if (spirvWords[0] != 0x07230203)
        {
            throw new InvalidDataException(
                $"The {label} resource is not valid SPIR-V. " +
                $"Magic = 0x{spirvWords[0]:X8}.");
        }

        Log.Debug(
            Tag,
            $"Calling vkCreateShaderModule({label}) with " +
            $"{spirvBytes.Length} bytes / {spirvWords.Length} words.");

        fixed (uint* codePointer = spirvWords)
        {
            VkShaderModuleCreateInfo moduleInfo =
                new()
                {
                    sType =
                        VkStructureType
                            .VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO,

                    pNext = null,
                    flags = 0,

                    codeSize =
                        (nuint)spirvBytes.Length,

                    pCode =
                        codePointer
                };

            VkShaderModule module =
                default;

            VkResult result =
                VulkanNative.vkCreateShaderModule(
                    device,
                    &moduleInfo,
                    null,
                    &module);

            Log.Debug(
                Tag,
                $"vkCreateShaderModule({label}) returned {result}");

            if (result != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException(
                    $"Creating the {label} shader module failed with {result}.");
            }

            return module;
        }
    }

    private static byte[] LoadEmbeddedResourceBySuffix(
        Assembly assembly,
        string suffix)
    {
        string? resourceName =
            assembly
                .GetManifestResourceNames()
                .FirstOrDefault(
                    name =>
                        name.EndsWith(
                            suffix,
                            StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new FileNotFoundException(
                $"Embedded shader resource ending in '{suffix}' " +
                "was not found. Confirm that the .spv file is " +
                "declared as EmbeddedResource in the MAUI .csproj.");
        }

        using Stream stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Unable to open embedded shader resource " +
                $"'{resourceName}'.");

        using MemoryStream memory = new();
        stream.CopyTo(memory);

        return memory.ToArray();
    }
}
