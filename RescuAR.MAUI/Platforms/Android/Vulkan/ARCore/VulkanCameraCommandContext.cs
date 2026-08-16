using Android.Util;
using Evergine.Bindings.Vulkan;
using Evergine.Vulkan;
using RescuAR.MAUI.Platforms.Android.Vulkan;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

/// <summary>
/// Uses Evergine's Vulkan graphics queue and owns the command pool, command
/// buffer, and fence used to execute the ARCore external-YCbCr to RGBA
/// conversion pass.
///
/// The implementation intentionally uses a single command buffer and fence.
/// This favors correctness and simple lifetime management for the initial
/// real-time camera passthrough implementation. A later optimization can
/// replace this with multiple in-flight frames.
/// </summary>
internal unsafe sealed class VulkanCameraCommandContext : IDisposable
{
    private const string Tag = "RescuAR-Vulkan";

    private readonly VKGraphicsContext graphicsContext;

    private VkQueue graphicsQueue;
    private uint graphicsQueueFamilyIndex;

    private VkCommandPool commandPool;
    private VkCommandBuffer commandBuffer;
    private VkFence conversionFence;

    private bool hasSubmittedWork;
    private bool disposed;

    private VulkanCameraCommandContext(
        VKGraphicsContext graphicsContext)
    {
        this.graphicsContext =
            graphicsContext
            ?? throw new ArgumentNullException(
                nameof(graphicsContext));
    }

    public VkQueue GraphicsQueue
    {
        get
        {
            ThrowIfDisposed();
            return graphicsQueue;
        }
    }

    public uint GraphicsQueueFamilyIndex
    {
        get
        {
            ThrowIfDisposed();
            return graphicsQueueFamilyIndex;
        }
    }

    public VkCommandBuffer CommandBuffer
    {
        get
        {
            ThrowIfDisposed();
            return commandBuffer;
        }
    }

    /// <summary>
    /// Resolves a graphics-capable queue and creates the reusable command and
    /// synchronization resources.
    /// </summary>
    public static VulkanCameraCommandContext Create(
        VKGraphicsContext graphicsContext)
    {
        ArgumentNullException.ThrowIfNull(
            graphicsContext);

        VulkanCameraCommandContext context =
            new(
                graphicsContext);

        try
        {
            context.ResolveGraphicsQueue();
            context.CreateCommandResources();

            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Records and submits one conversion draw.
    ///
    /// The source external image is newly imported and begins in
    /// VK_IMAGE_LAYOUT_UNDEFINED. This method transitions it to
    /// VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL before the fragment shader
    /// samples it. The render pass transitions the persistent RGBA target to
    /// its color-attachment layout and finishes it in shader-read-only layout.
    ///
    /// The method waits for completion before returning. This is intentionally
    /// conservative for the first real-time implementation.
    /// </summary>
    public void ConvertFrame(
        VulkanExternalFrame sourceFrame,
        VulkanRgbaTarget rgbaTarget,
        VulkanCameraConversionPipeline pipeline,
        float[] cameraUv)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(
            sourceFrame);

        ArgumentNullException.ThrowIfNull(
            rgbaTarget);

        ArgumentNullException.ThrowIfNull(
            pipeline);

        ArgumentNullException.ThrowIfNull(
            cameraUv);

        if (cameraUv.Length != 8)
        {
            throw new ArgumentException(
                "Camera UV data must contain exactly eight floats: " +
                "top-left, top-right, bottom-left, and bottom-right.",
                nameof(cameraUv));
        }

        if (sourceFrame.IsDisposed)
        {
            throw new ObjectDisposedException(
                nameof(sourceFrame));
        }

        PrepareForSubmission();

        RecordConversionCommands(
            sourceFrame,
            rgbaTarget,
            pipeline,
            cameraUv);

        SubmitAndWait();
    }

    /// <summary>
    /// Waits for the most recently submitted conversion, if one exists.
    /// Useful before destroying or replacing per-frame external resources.
    /// </summary>
    public void WaitForPreviousFrame()
    {
        ThrowIfDisposed();

        if (!hasSubmittedWork ||
            conversionFence.Handle == 0)
        {
            return;
        }

        WaitForFence();
    }

    private void ResolveGraphicsQueue()
    {
        /*
         * Reuse the exact graphics queue/family that Evergine created.
         * Do not independently enumerate physical-device queue families: a
         * family exposed by the physical device is not necessarily enabled
         * on Evergine's logical device.
         */
        VulkanGraphicsQueueResolver resolver =
            new(
                graphicsContext);

        graphicsQueue =
            resolver.Queue;

        graphicsQueueFamilyIndex =
            resolver.QueueFamilyIndex;
    }

    private void CreateCommandResources()
    {
        VkCommandPoolCreateInfo poolInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,

                pNext = null,

                flags =
                    VkCommandPoolCreateFlags
                        .VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,

                queueFamilyIndex =
                    graphicsQueueFamilyIndex
            };

        VkCommandPool createdPool =
            default;

        VkResult result =
            VulkanNative.vkCreateCommandPool(
                graphicsContext.VkDevice,
                &poolInfo,
                null,
                &createdPool);

        ThrowIfFailed(
            result,
            "vkCreateCommandPool");

        commandPool =
            createdPool;

        VkCommandBufferAllocateInfo allocateInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,

                pNext = null,

                commandPool =
                    commandPool,

                level =
                    VkCommandBufferLevel
                        .VK_COMMAND_BUFFER_LEVEL_PRIMARY,

                commandBufferCount = 1
            };

        VkCommandBuffer allocatedBuffer =
            default;

        result =
            VulkanNative.vkAllocateCommandBuffers(
                graphicsContext.VkDevice,
                &allocateInfo,
                &allocatedBuffer);

        ThrowIfFailed(
            result,
            "vkAllocateCommandBuffers");

        commandBuffer =
            allocatedBuffer;

        VkFenceCreateInfo fenceInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_FENCE_CREATE_INFO,

                pNext = null,

                flags = 0
            };

        VkFence createdFence =
            default;

        result =
            VulkanNative.vkCreateFence(
                graphicsContext.VkDevice,
                &fenceInfo,
                null,
                &createdFence);

        ThrowIfFailed(
            result,
            "vkCreateFence");

        conversionFence =
            createdFence;
    }

    private void PrepareForSubmission()
    {
        if (!hasSubmittedWork)
        {
            return;
        }

        /*
         * ConvertFrame waits after every submission, so this should normally
         * return immediately. Keeping the explicit wait makes the class safe
         * if that policy changes later.
         */
        WaitForFence();

        VkFence fence =
            conversionFence;

        VkResult result =
            VulkanNative.vkResetFences(
                graphicsContext.VkDevice,
                1,
                &fence);

        ThrowIfFailed(
            result,
            "vkResetFences");

        result =
            VulkanNative.vkResetCommandBuffer(
                commandBuffer,
                0);

        ThrowIfFailed(
            result,
            "vkResetCommandBuffer");
    }

    private void RecordConversionCommands(
        VulkanExternalFrame sourceFrame,
        VulkanRgbaTarget rgbaTarget,
        VulkanCameraConversionPipeline pipeline,
        float[] cameraUv)
    {
        VkCommandBufferBeginInfo beginInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,

                pNext = null,

                flags =
                    VkCommandBufferUsageFlags
                        .VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT,

                pInheritanceInfo = null
            };

        VkResult result =
            VulkanNative.vkBeginCommandBuffer(
                commandBuffer,
                &beginInfo);

        ThrowIfFailed(
            result,
            "vkBeginCommandBuffer");

        /*
         * Each VulkanExternalFrame is newly imported from the current ARCore
         * HardwareBuffer. Its Vulkan image is therefore treated as starting
         * in VK_IMAGE_LAYOUT_UNDEFINED for this conversion.
         */
        VkImageMemoryBarrier externalBarrier =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,

                pNext = null,

                srcAccessMask = 0,

                dstAccessMask =
                    VkAccessFlags
                        .VK_ACCESS_SHADER_READ_BIT,

                oldLayout =
                    VkImageLayout
                        .VK_IMAGE_LAYOUT_UNDEFINED,

                newLayout =
                    VkImageLayout
                        .VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,

                srcQueueFamilyIndex =
                    uint.MaxValue,

                dstQueueFamilyIndex =
                    uint.MaxValue,

                image =
                    sourceFrame.Image,

                subresourceRange =
                    new VkImageSubresourceRange
                    {
                        aspectMask =
                            VkImageAspectFlags
                                .VK_IMAGE_ASPECT_COLOR_BIT,

                        baseMipLevel = 0,
                        levelCount = 1,

                        baseArrayLayer = 0,
                        layerCount = 1
                    }
            };

        VulkanNative.vkCmdPipelineBarrier(
            commandBuffer,
            VkPipelineStageFlags
                .VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
            VkPipelineStageFlags
                .VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
            0,
            0,
            null,
            0,
            null,
            1,
            &externalBarrier);

        /*
         * The fullscreen triangle overwrites the color attachment, so a
         * zero-initialized clear value is sufficient and avoids depending on
         * binding-specific VkClearColorValue convenience fields.
         */
        VkClearValue clearValue =
            default;

        VkRenderPassBeginInfo renderPassBegin =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO,

                pNext = null,

                renderPass =
                    pipeline.RenderPass,

                framebuffer =
                    pipeline.Framebuffer,

                renderArea =
                    new VkRect2D
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
                                width =
                                    rgbaTarget.Width,

                                height =
                                    rgbaTarget.Height
                            }
                    },

                clearValueCount = 1,

                pClearValues =
                    &clearValue
            };

        VulkanNative.vkCmdBeginRenderPass(
            commandBuffer,
            &renderPassBegin,
            VkSubpassContents
                .VK_SUBPASS_CONTENTS_INLINE);

        VulkanNative.vkCmdBindPipeline(
            commandBuffer,
            VkPipelineBindPoint
                .VK_PIPELINE_BIND_POINT_GRAPHICS,
            pipeline.GraphicsPipeline);

        VkDescriptorSet descriptorSet =
            pipeline.DescriptorSet;

        VulkanNative.vkCmdBindDescriptorSets(
            commandBuffer,
            VkPipelineBindPoint
                .VK_PIPELINE_BIND_POINT_GRAPHICS,
            pipeline.PipelineLayout,
            0,
            1,
            &descriptorSet,
            0,
            null);

        /*
         * Upload the four ARCore texture-coordinate corners directly to the
         * fragment shader.
         *
         * Layout:
         *
         * cameraUv[0], cameraUv[1] = top-left
         * cameraUv[2], cameraUv[3] = top-right
         * cameraUv[4], cameraUv[5] = bottom-left
         * cameraUv[6], cameraUv[7] = bottom-right
         */
        fixed (float* cameraUvPointer =
            cameraUv)
        {
            VulkanNative.vkCmdPushConstants(
                commandBuffer,
                pipeline.PipelineLayout,
                VkShaderStageFlags
                    .VK_SHADER_STAGE_FRAGMENT_BIT,
                0,
                8 * sizeof(float),
                cameraUvPointer);
        }

        VulkanNative.vkCmdDraw(
            commandBuffer,
            3,
            1,
            0,
            0);

        VulkanNative.vkCmdEndRenderPass(
            commandBuffer);

        result =
            VulkanNative.vkEndCommandBuffer(
                commandBuffer);

        ThrowIfFailed(
            result,
            "vkEndCommandBuffer");
    }

    private void SubmitAndWait()
    {
        VkCommandBuffer submittedBuffer =
            commandBuffer;

        VkSubmitInfo submitInfo =
            new()
            {
                sType =
                    VkStructureType
                        .VK_STRUCTURE_TYPE_SUBMIT_INFO,

                pNext = null,

                waitSemaphoreCount = 0,
                pWaitSemaphores = null,
                pWaitDstStageMask = null,

                commandBufferCount = 1,
                pCommandBuffers =
                    &submittedBuffer,

                signalSemaphoreCount = 0,
                pSignalSemaphores = null
            };

        VkResult result =
            VulkanNative.vkQueueSubmit(
                graphicsQueue,
                1,
                &submitInfo,
                conversionFence);

        ThrowIfFailed(
            result,
            "vkQueueSubmit");

        hasSubmittedWork =
            true;

        WaitForFence();

    }

    private void WaitForFence()
    {
        VkFence fence =
            conversionFence;

        VkResult result =
            VulkanNative.vkWaitForFences(
                graphicsContext.VkDevice,
                1,
                &fence,
                1,
                ulong.MaxValue);

        ThrowIfFailed(
            result,
            "vkWaitForFences");
    }

    private static void ThrowIfFailed(
        VkResult result,
        string operation)
    {
        if (result == VkResult.VK_SUCCESS)
        {
            return;
        }

        Log.Error(
            Tag,
            $"{operation} failed with {result}.");

        throw new InvalidOperationException(
            $"{operation} failed with {result}.");
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(
                nameof(VulkanCameraCommandContext));
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
            /*
             * Do not destroy command resources while submitted GPU work may
             * still reference them.
             */
            if (hasSubmittedWork &&
                conversionFence.Handle != 0)
            {
                try
                {
                    WaitForFence();
                }
                catch
                {
                    /*
                     * Dispose must still attempt to release local Vulkan
                     * objects. The original error path remains responsible
                     * for reporting the submission failure.
                     */
                }
            }

            if (conversionFence.Handle != 0)
            {
                VulkanNative.vkDestroyFence(
                    device,
                    conversionFence,
                    null);

                conversionFence =
                    default;
            }

            if (commandPool.Handle != 0)
            {
                /*
                 * Destroying the command pool implicitly frees command buffers
                 * allocated from it.
                 */
                VulkanNative.vkDestroyCommandPool(
                    device,
                    commandPool,
                    null);

                commandPool =
                    default;

                commandBuffer =
                    default;
            }
        }

        graphicsQueue =
            default;

        hasSubmittedWork =
            false;

        disposed =
            true;
    }
}
