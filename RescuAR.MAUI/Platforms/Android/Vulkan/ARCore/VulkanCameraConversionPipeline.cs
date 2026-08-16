using Android.Util;
using Evergine.Bindings.Vulkan;
using Evergine.Vulkan;
using System.Reflection;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

/// <summary>
/// Owns the persistent Vulkan graphics resources used to convert an imported
/// ARCore external YCbCr image into the persistent RGBA render target.
///
/// This object intentionally does not own:
/// - the per-frame VulkanExternalFrame;
/// - the VulkanYcbcrResources;
/// - the VulkanRgbaTarget;
/// - command buffers, queues, or fences.
///
/// The descriptor set is updated whenever a new VulkanExternalFrame is used.
/// </summary>
internal unsafe sealed class VulkanCameraConversionPipeline : IDisposable
{
    private const string Tag = "RescuAR-Vulkan";

    private readonly VKGraphicsContext graphicsContext;
    private bool disposed;

    private VkShaderModule fullscreenVertexShader;
    private VkShaderModule externalFragmentShader;

    private VkDescriptorSetLayout descriptorSetLayout;
    private VkDescriptorPool descriptorPool;
    private VkDescriptorSet descriptorSet;

    private VkRenderPass renderPass;
    private VkFramebuffer framebuffer;

    private VkPipelineLayout pipelineLayout;
    private VkPipeline graphicsPipeline;

    private VulkanCameraConversionPipeline(
        VKGraphicsContext graphicsContext)
    {
        this.graphicsContext =
            graphicsContext
            ?? throw new ArgumentNullException(
                nameof(graphicsContext));
    }

    public VkDescriptorSet DescriptorSet
    {
        get
        {
            ThrowIfDisposed();
            return descriptorSet;
        }
    }

    public VkDescriptorSetLayout DescriptorSetLayout
    {
        get
        {
            ThrowIfDisposed();
            return descriptorSetLayout;
        }
    }

    public VkRenderPass RenderPass
    {
        get
        {
            ThrowIfDisposed();
            return renderPass;
        }
    }

    public VkFramebuffer Framebuffer
    {
        get
        {
            ThrowIfDisposed();
            return framebuffer;
        }
    }

    public VkPipelineLayout PipelineLayout
    {
        get
        {
            ThrowIfDisposed();
            return pipelineLayout;
        }
    }

    public VkPipeline GraphicsPipeline
    {
        get
        {
            ThrowIfDisposed();
            return graphicsPipeline;
        }
    }

    /// <summary>
    /// Creates all persistent conversion-pipeline resources.
    ///
    /// The immutable sampler is supplied by VulkanYcbcrResources. The first
    /// external frame provides the initial image view written to the descriptor.
    /// Future frames can replace only that image view through UpdateExternalFrame.
    /// </summary>
    public static VulkanCameraConversionPipeline Create(
        VKGraphicsContext graphicsContext,
        VulkanYcbcrResources ycbcrResources,
        VulkanRgbaTarget rgbaTarget,
        VulkanExternalFrame initialFrame,
        Assembly shaderAssembly)
    {
        ArgumentNullException.ThrowIfNull(
            graphicsContext);

        ArgumentNullException.ThrowIfNull(
            ycbcrResources);

        ArgumentNullException.ThrowIfNull(
            rgbaTarget);

        ArgumentNullException.ThrowIfNull(
            initialFrame);

        ArgumentNullException.ThrowIfNull(
            shaderAssembly);

        if (initialFrame.IsDisposed)
        {
            throw new ObjectDisposedException(
                nameof(initialFrame));
        }

        if (initialFrame.ExternalFormat !=
            ycbcrResources.ExternalFormat)
        {
            throw new InvalidOperationException(
                "The initial external frame does not match the " +
                "YCbCr resource external format.");
        }

        VulkanCameraConversionPipeline pipeline =
            new(
                graphicsContext);

        try
        {
            pipeline.CreateShaderModules(
                shaderAssembly);

            pipeline.CreateDescriptorResources(
                ycbcrResources.Sampler,
                initialFrame.ImageView);

            pipeline.CreateRenderPassAndFramebuffer(
                rgbaTarget);

            pipeline.CreateGraphicsPipeline(
                rgbaTarget.Width,
                rgbaTarget.Height);

            return pipeline;
        }
        catch
        {
            pipeline.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Rewrites binding 0 so the existing descriptor set samples a new ARCore
    /// external image view. The sampler remains immutable and therefore is not
    /// recreated or rewritten per frame.
    /// </summary>
    public void UpdateExternalFrame(
        VulkanExternalFrame frame)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(
            frame);

        if (frame.IsDisposed)
        {
            throw new ObjectDisposedException(
                nameof(frame));
        }

        UpdateDescriptorImage(
            frame.ImageView);
    }

    private void CreateShaderModules(
        Assembly shaderAssembly)
    {
        fullscreenVertexShader =
            VulkanShaderLoader.LoadEmbeddedShaderModule(
                graphicsContext.VkDevice,
                shaderAssembly,
                "CameraFullscreen.vert.spv",
                "fullscreen vertex");

        try
        {
            externalFragmentShader =
                VulkanShaderLoader.LoadEmbeddedShaderModule(
                    graphicsContext.VkDevice,
                    shaderAssembly,
                    "CameraExternal.frag.spv",
                    "external camera fragment");
        }
        catch
        {
            if (fullscreenVertexShader.Handle != 0)
            {
                VulkanNative.vkDestroyShaderModule(
                    graphicsContext.VkDevice,
                    fullscreenVertexShader,
                    null);

                fullscreenVertexShader =
                    default;
            }

            throw;
        }
    }

    private void CreateDescriptorResources(
        VkSampler immutableYcbcrSampler,
        VkImageView initialImageView)
    {
        if (immutableYcbcrSampler.Handle == 0)
        {
            throw new InvalidOperationException(
                "Cannot create the camera descriptor layout with " +
                "a null YCbCr sampler.");
        }

        if (initialImageView.Handle == 0)
        {
            throw new InvalidOperationException(
                "Cannot initialize the camera descriptor with " +
                "a null external image view.");
        }

        /*
         * When sampler YCbCr conversion is used, the combined-image sampler
         * is declared as an immutable sampler in the descriptor-set layout.
         */
        VkSampler immutableSampler =
            immutableYcbcrSampler;

        VkDescriptorSetLayoutBinding binding =
            new()
            {
                binding = 0,

                descriptorType =
                    VkDescriptorType
                        .VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,

                descriptorCount = 1,

                stageFlags =
                    VkShaderStageFlags
                        .VK_SHADER_STAGE_FRAGMENT_BIT,

                pImmutableSamplers =
                    &immutableSampler
            };

        VkDescriptorSetLayoutCreateInfo layoutInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO,

                pNext = null,
                flags = 0,
                bindingCount = 1,
                pBindings = &binding
            };

        VkDescriptorSetLayout createdLayout =
            default;

        VkResult result =
            VulkanNative.vkCreateDescriptorSetLayout(
                graphicsContext.VkDevice,
                &layoutInfo,
                null,
                &createdLayout);

        Log.Debug(
            Tag,
            $"vkCreateDescriptorSetLayout returned {result}");

        ThrowIfFailed(
            result,
            "vkCreateDescriptorSetLayout");

        descriptorSetLayout =
            createdLayout;

        VkDescriptorPoolSize poolSize =
            new()
            {
                type =
                    VkDescriptorType
                        .VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,

                descriptorCount = 1
            };

        VkDescriptorPoolCreateInfo poolInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO,

                pNext = null,
                flags = 0,
                maxSets = 1,
                poolSizeCount = 1,
                pPoolSizes = &poolSize
            };

        VkDescriptorPool createdPool =
            default;

        result =
            VulkanNative.vkCreateDescriptorPool(
                graphicsContext.VkDevice,
                &poolInfo,
                null,
                &createdPool);

        Log.Debug(
            Tag,
            $"vkCreateDescriptorPool returned {result}");

        ThrowIfFailed(
            result,
            "vkCreateDescriptorPool");

        descriptorPool =
            createdPool;

        VkDescriptorSetLayout setLayout =
            descriptorSetLayout;

        VkDescriptorSetAllocateInfo allocateInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO,

                pNext = null,
                descriptorPool = descriptorPool,
                descriptorSetCount = 1,
                pSetLayouts = &setLayout
            };

        VkDescriptorSet allocatedSet =
            default;

        result =
            VulkanNative.vkAllocateDescriptorSets(
                graphicsContext.VkDevice,
                &allocateInfo,
                &allocatedSet);

        Log.Debug(
            Tag,
            $"vkAllocateDescriptorSets returned {result}");

        ThrowIfFailed(
            result,
            "vkAllocateDescriptorSets");

        descriptorSet =
            allocatedSet;

        UpdateDescriptorImage(
            initialImageView);
    }

    private void UpdateDescriptorImage(
        VkImageView imageView)
    {
        if (descriptorSet.Handle == 0)
        {
            throw new InvalidOperationException(
                "Cannot update the camera descriptor before the " +
                "descriptor set exists.");
        }

        if (imageView.Handle == 0)
        {
            throw new ArgumentException(
                "The external image view is null.",
                nameof(imageView));
        }

        VkDescriptorImageInfo imageInfo =
            new()
            {
                /*
                 * The sampler is immutable in the descriptor-set layout.
                 * The sampler field is therefore intentionally left null.
                 */
                sampler = default,

                imageView =
                    imageView,

                imageLayout =
                    VkImageLayout
                        .VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
            };

        VkWriteDescriptorSet write =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,

                pNext = null,

                dstSet =
                    descriptorSet,

                dstBinding = 0,
                dstArrayElement = 0,
                descriptorCount = 1,

                descriptorType =
                    VkDescriptorType
                        .VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,

                pImageInfo =
                    &imageInfo,

                pBufferInfo = null,
                pTexelBufferView = null
            };

        VulkanNative.vkUpdateDescriptorSets(
            graphicsContext.VkDevice,
            1,
            &write,
            0,
            null);
    }

    private void CreateRenderPassAndFramebuffer(
        VulkanRgbaTarget rgbaTarget)
    {
        VkAttachmentDescription colorAttachment =
            new()
            {
                flags = 0,

                format =
                    rgbaTarget.Format,

                samples =
                    VkSampleCountFlags
                        .VK_SAMPLE_COUNT_1_BIT,

                loadOp =
                    VkAttachmentLoadOp
                        .VK_ATTACHMENT_LOAD_OP_CLEAR,

                storeOp =
                    VkAttachmentStoreOp
                        .VK_ATTACHMENT_STORE_OP_STORE,

                stencilLoadOp =
                    VkAttachmentLoadOp
                        .VK_ATTACHMENT_LOAD_OP_DONT_CARE,

                stencilStoreOp =
                    VkAttachmentStoreOp
                        .VK_ATTACHMENT_STORE_OP_DONT_CARE,

                initialLayout =
                    VkImageLayout
                        .VK_IMAGE_LAYOUT_UNDEFINED,

                finalLayout =
                    VkImageLayout
                        .VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
            };

        VkAttachmentReference colorReference =
            new()
            {
                attachment = 0,

                layout =
                    VkImageLayout
                        .VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL
            };

        VkSubpassDescription subpass =
            new()
            {
                flags = 0,

                pipelineBindPoint =
                    VkPipelineBindPoint
                        .VK_PIPELINE_BIND_POINT_GRAPHICS,

                inputAttachmentCount = 0,
                pInputAttachments = null,

                colorAttachmentCount = 1,
                pColorAttachments = &colorReference,

                pResolveAttachments = null,
                pDepthStencilAttachment = null,

                preserveAttachmentCount = 0,
                pPreserveAttachments = null
            };

        VkSubpassDependency dependency =
            new()
            {
                srcSubpass =
                    uint.MaxValue,

                dstSubpass = 0,

                srcStageMask =
                    VkPipelineStageFlags
                        .VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,

                dstStageMask =
                    VkPipelineStageFlags
                        .VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,

                srcAccessMask = 0,

                dstAccessMask =
                    VkAccessFlags
                        .VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT,

                dependencyFlags = 0
            };

        VkRenderPassCreateInfo renderPassInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO,

                pNext = null,
                flags = 0,

                attachmentCount = 1,
                pAttachments = &colorAttachment,

                subpassCount = 1,
                pSubpasses = &subpass,

                dependencyCount = 1,
                pDependencies = &dependency
            };

        VkRenderPass createdRenderPass =
            default;

        VkResult result =
            VulkanNative.vkCreateRenderPass(
                graphicsContext.VkDevice,
                &renderPassInfo,
                null,
                &createdRenderPass);

        Log.Debug(
            Tag,
            $"vkCreateRenderPass returned {result}");

        ThrowIfFailed(
            result,
            "vkCreateRenderPass");

        renderPass =
            createdRenderPass;

        VkImageView attachment =
            rgbaTarget.ImageView;

        VkFramebufferCreateInfo framebufferInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO,

                pNext = null,
                flags = 0,

                renderPass =
                    renderPass,

                attachmentCount = 1,
                pAttachments = &attachment,

                width =
                    rgbaTarget.Width,

                height =
                    rgbaTarget.Height,

                layers = 1
            };

        VkFramebuffer createdFramebuffer =
            default;

        result =
            VulkanNative.vkCreateFramebuffer(
                graphicsContext.VkDevice,
                &framebufferInfo,
                null,
                &createdFramebuffer);

        Log.Debug(
            Tag,
            $"vkCreateFramebuffer returned {result}");

        ThrowIfFailed(
            result,
            "vkCreateFramebuffer");

        framebuffer =
            createdFramebuffer;
    }

    private void CreateGraphicsPipeline(
        uint outputWidth,
        uint outputHeight)
    {
        if (outputWidth == 0 ||
            outputHeight == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputWidth),
                "Graphics pipeline dimensions must be nonzero.");
        }

        VkDescriptorSetLayout layout =
            descriptorSetLayout;

        /*
         * Four vec2 values:
         *
         * topLeft     = 2 floats
         * topRight    = 2 floats
         * bottomLeft  = 2 floats
         * bottomRight = 2 floats
         *
         * 8 floats × 4 bytes = 32 bytes.
         *
         * These coordinates are consumed by CameraExternal.frag.
         */
        VkPushConstantRange cameraUvPushConstants =
            new()
            {
                stageFlags =
                    VkShaderStageFlags
                        .VK_SHADER_STAGE_FRAGMENT_BIT,

                offset = 0,

                size =
                    8 * sizeof(float)
            };

        VkPipelineLayoutCreateInfo pipelineLayoutInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,

                pNext = null,
                flags = 0,

                setLayoutCount = 1,
                pSetLayouts = &layout,

                pushConstantRangeCount = 1,
                pPushConstantRanges =
                    &cameraUvPushConstants
            };

        VkPipelineLayout createdPipelineLayout =
            default;

        VkResult result =
            VulkanNative.vkCreatePipelineLayout(
                graphicsContext.VkDevice,
                &pipelineLayoutInfo,
                null,
                &createdPipelineLayout);

        Log.Debug(
            Tag,
            $"vkCreatePipelineLayout returned {result}");

        ThrowIfFailed(
            result,
            "vkCreatePipelineLayout");

        pipelineLayout =
            createdPipelineLayout;

        byte* entryPoint =
            stackalloc byte[5]
            {
                (byte)'m',
                (byte)'a',
                (byte)'i',
                (byte)'n',
                0
            };

        VkPipelineShaderStageCreateInfo* stages =
            stackalloc VkPipelineShaderStageCreateInfo[2];

        stages[0] =
            new VkPipelineShaderStageCreateInfo
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,

                pNext = null,
                flags = 0,

                stage =
                    VkShaderStageFlags
                        .VK_SHADER_STAGE_VERTEX_BIT,

                module =
                    fullscreenVertexShader,

                pName =
                    entryPoint,

                pSpecializationInfo = null
            };

        stages[1] =
            new VkPipelineShaderStageCreateInfo
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,

                pNext = null,
                flags = 0,

                stage =
                    VkShaderStageFlags
                        .VK_SHADER_STAGE_FRAGMENT_BIT,

                module =
                    externalFragmentShader,

                pName =
                    entryPoint,

                pSpecializationInfo = null
            };

        VkPipelineVertexInputStateCreateInfo vertexInput =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO,

                pNext = null,
                flags = 0,

                vertexBindingDescriptionCount = 0,
                pVertexBindingDescriptions = null,

                vertexAttributeDescriptionCount = 0,
                pVertexAttributeDescriptions = null
            };

        VkPipelineInputAssemblyStateCreateInfo inputAssembly =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO,

                pNext = null,
                flags = 0,

                topology =
                    VkPrimitiveTopology
                        .VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST,

                primitiveRestartEnable = 0
            };

        VkViewport viewport =
            new()
            {
                x = 0,
                y = 0,

                width =
                    outputWidth,

                height =
                    outputHeight,

                minDepth = 0,
                maxDepth = 1
            };

        VkRect2D scissor =
            new()
            {
                offset =
                    new VkOffset2D
                    {
                        x = 0,
                        y = 0
                    },

                extent =
                    new VkExtent2D
                    {
                        width = outputWidth,
                        height = outputHeight
                    }
            };

        VkPipelineViewportStateCreateInfo viewportState =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO,

                pNext = null,
                flags = 0,

                viewportCount = 1,
                pViewports = &viewport,

                scissorCount = 1,
                pScissors = &scissor
            };

        VkPipelineRasterizationStateCreateInfo rasterization =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO,

                pNext = null,
                flags = 0,

                depthClampEnable = 0,
                rasterizerDiscardEnable = 0,

                polygonMode =
                    VkPolygonMode.VK_POLYGON_MODE_FILL,

                cullMode =
                    VkCullModeFlags.VK_CULL_MODE_NONE,

                frontFace =
                    VkFrontFace
                        .VK_FRONT_FACE_COUNTER_CLOCKWISE,

                depthBiasEnable = 0,
                depthBiasConstantFactor = 0,
                depthBiasClamp = 0,
                depthBiasSlopeFactor = 0,

                lineWidth = 1
            };

        VkPipelineMultisampleStateCreateInfo multisample =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO,

                pNext = null,
                flags = 0,

                rasterizationSamples =
                    VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT,

                sampleShadingEnable = 0,
                minSampleShading = 0,
                pSampleMask = null,
                alphaToCoverageEnable = 0,
                alphaToOneEnable = 0
            };

        VkPipelineColorBlendAttachmentState blendAttachment =
            new()
            {
                blendEnable = 0,

                srcColorBlendFactor =
                    VkBlendFactor.VK_BLEND_FACTOR_ONE,

                dstColorBlendFactor =
                    VkBlendFactor.VK_BLEND_FACTOR_ZERO,

                colorBlendOp =
                    VkBlendOp.VK_BLEND_OP_ADD,

                srcAlphaBlendFactor =
                    VkBlendFactor.VK_BLEND_FACTOR_ONE,

                dstAlphaBlendFactor =
                    VkBlendFactor.VK_BLEND_FACTOR_ZERO,

                alphaBlendOp =
                    VkBlendOp.VK_BLEND_OP_ADD,

                colorWriteMask =
                    VkColorComponentFlags.VK_COLOR_COMPONENT_R_BIT |
                    VkColorComponentFlags.VK_COLOR_COMPONENT_G_BIT |
                    VkColorComponentFlags.VK_COLOR_COMPONENT_B_BIT |
                    VkColorComponentFlags.VK_COLOR_COMPONENT_A_BIT
            };

        VkPipelineColorBlendStateCreateInfo colorBlend =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO,

                pNext = null,
                flags = 0,

                logicOpEnable = 0,

                logicOp =
                    VkLogicOp.VK_LOGIC_OP_COPY,

                attachmentCount = 1,
                pAttachments = &blendAttachment
            };

        VkGraphicsPipelineCreateInfo pipelineInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,

                pNext = null,
                flags = 0,

                stageCount = 2,
                pStages = stages,

                pVertexInputState = &vertexInput,
                pInputAssemblyState = &inputAssembly,
                pTessellationState = null,
                pViewportState = &viewportState,
                pRasterizationState = &rasterization,
                pMultisampleState = &multisample,
                pDepthStencilState = null,
                pColorBlendState = &colorBlend,
                pDynamicState = null,

                layout =
                    pipelineLayout,

                renderPass =
                    renderPass,

                subpass = 0,

                basePipelineHandle = default,
                basePipelineIndex = -1
            };

        VkPipeline createdPipeline =
            default;

        result =
            VulkanNative.vkCreateGraphicsPipelines(
                graphicsContext.VkDevice,
                default,
                1,
                &pipelineInfo,
                null,
                &createdPipeline);

        Log.Debug(
            Tag,
            $"vkCreateGraphicsPipelines returned {result}");

        ThrowIfFailed(
            result,
            "vkCreateGraphicsPipelines");

        graphicsPipeline =
            createdPipeline;
    }

    private static void ThrowIfFailed(
        VkResult result,
        string operation)
    {
        if (result != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException(
                $"{operation} failed with {result}.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(
                nameof(VulkanCameraConversionPipeline));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        VkDevice device =
            graphicsContext.VkDevice;

        if (device.Handle != 0)
        {
            if (graphicsPipeline.Handle != 0)
            {
                VulkanNative.vkDestroyPipeline(
                    device,
                    graphicsPipeline,
                    null);

                graphicsPipeline =
                    default;
            }

            if (pipelineLayout.Handle != 0)
            {
                VulkanNative.vkDestroyPipelineLayout(
                    device,
                    pipelineLayout,
                    null);

                pipelineLayout =
                    default;
            }

            if (framebuffer.Handle != 0)
            {
                VulkanNative.vkDestroyFramebuffer(
                    device,
                    framebuffer,
                    null);

                framebuffer =
                    default;
            }

            if (renderPass.Handle != 0)
            {
                VulkanNative.vkDestroyRenderPass(
                    device,
                    renderPass,
                    null);

                renderPass =
                    default;
            }

            if (descriptorPool.Handle != 0)
            {
                /*
                 * Descriptor sets allocated from this pool are implicitly
                 * released when the pool is destroyed.
                 */
                VulkanNative.vkDestroyDescriptorPool(
                    device,
                    descriptorPool,
                    null);

                descriptorPool =
                    default;

                descriptorSet =
                    default;
            }

            if (descriptorSetLayout.Handle != 0)
            {
                VulkanNative.vkDestroyDescriptorSetLayout(
                    device,
                    descriptorSetLayout,
                    null);

                descriptorSetLayout =
                    default;
            }

            if (externalFragmentShader.Handle != 0)
            {
                VulkanNative.vkDestroyShaderModule(
                    device,
                    externalFragmentShader,
                    null);

                externalFragmentShader =
                    default;
            }

            if (fullscreenVertexShader.Handle != 0)
            {
                VulkanNative.vkDestroyShaderModule(
                    device,
                    fullscreenVertexShader,
                    null);

                fullscreenVertexShader =
                    default;
            }
        }

        disposed =
            true;
    }
}
