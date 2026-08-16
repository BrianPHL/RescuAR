using Evergine.Bindings.Vulkan;

public unsafe struct VkAndroidHardwareBufferFormatPropertiesANDROID
{
    public VkStructureType sType;

    public void* pNext;

    public VkFormat format;

    public ulong externalFormat;

    public VkFormatFeatureFlags formatFeatures;

    public VkComponentMapping samplerYcbcrConversionComponents;

    public VkSamplerYcbcrModelConversion suggestedYcbcrModel;

    public VkSamplerYcbcrRange suggestedYcbcrRange;

    public VkChromaLocation suggestedXChromaOffset;

    public VkChromaLocation suggestedYChromaOffset;
}
