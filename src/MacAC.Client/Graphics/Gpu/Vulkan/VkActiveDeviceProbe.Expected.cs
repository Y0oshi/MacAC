using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static unsafe partial class VkActiveDeviceProbe
{
    internal static ReadOnlySpan<byte> AnticipatedWipeRgba => [0x33, 0x77, 0xBB, 0xEE];

    internal static VkFunctionProbeResult Run(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev,
        Queue visualsFifo,
        uint visualsClan)
    {
        ArgumentNullException.ThrowIfNull(vk);
        List<string> misses = new List<string>();

        bool descriptorArrangement = false;
        bool pushConstantArrangement = false;
        bool dynamicRendering = false;
        bool timelinePause = false;
        bool hubAskRestart = false;
        bool readback = false;

        DescriptorSetLayout depotArrangement = default;
        DescriptorSetLayout uniformArrangement = default;
        DescriptorSetLayout chartArrangement = default;
        PipelineLayout pipeArrangement = default;

        try
        {
            descriptorArrangement = Attempt(
                "descriptor-indexing set layouts",
                () =>
                {
                    depotArrangement = VulkanPipeArrangements.BuildDepotSetArrangement(vk, dev);
                    uniformArrangement = VulkanPipeArrangements.BuildUniformSetArrangement(vk, dev);
                    chartArrangement = VulkanPipeArrangements.BuildTextureChartSetArrangement(vk, dev);
                },
                misses);

            if (descriptorArrangement)
            {
                pushConstantArrangement = Attempt(
                    "three-set pipeline layout with the 96-byte push-constant block",
                    () => pipeArrangement = VulkanPipeArrangements.BuildPipeArrangement(
                        vk,
                        dev,
                        depotArrangement,
                        uniformArrangement,
                        chartArrangement),
                    misses);
            }

            hubAskRestart = Attempt(
                "host timestamp query-pool reset",
                () => InspectHubAskRestart(vk, dev),
                misses);

            byte[]? px = null;
            dynamicRendering = Attempt(
                "dynamic-rendering clear, synchronization2 barriers and timeline submit",
                () => px = PaintAndScanBack(vk, physicalDev, dev, visualsFifo, visualsClan),
                misses);
            timelinePause = dynamicRendering;

            if (dynamicRendering && px is not null)
            {
                readback = Attempt(
                    "offscreen readback pixel comparison",
                    () => VerifyWipeColour(px),
                    misses);
            }
        }
        finally
        {
            if (pipeArrangement.Handle is not 0)
                vk.DestroyPipelineLayout(dev, pipeArrangement, null);
            if (chartArrangement.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, chartArrangement, null);
            if (uniformArrangement.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, uniformArrangement, null);
            if (depotArrangement.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, depotArrangement, null);
        }

        return new VkFunctionProbeResult(
            DeviceCreation: true,
            DescriptorIndexingLayout: descriptorArrangement,
            PushConstantLayout: pushConstantArrangement,
            DynamicRenderingClear: dynamicRendering,
            TimelineSemaphoreWait: timelinePause,
            HostQueryReset: hubAskRestart,
            OffscreenReadback: readback,
            Failures: misses);
    }

    internal static uint? SeekMemoryKind(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        uint kindBitset,
        MemoryPropertyFlags props)
    {
        vk.GetPhysicalDeviceMemoryProperties(physicalDev, out PhysicalDeviceMemoryProperties memory);
        for (uint idx = 0; idx < memory.MemoryTypeCount && idx < 32; ++idx)
        {
            bool allowed = (kindBitset & (1u << (int)idx)) is not 0;
            if (allowed && memory.MemoryTypes[(int)idx].PropertyFlags.HasFlag(props))
                return idx;
        }

        return null;
    }

    internal static void VerifyWipeColour(ReadOnlySpan<byte> px)
    {
        if (px.Length != SensorReach * SensorReach * 4)
        {
            throw new InvalidOperationException(
                $"the readback returned {px.Length} bytes; wanted " +
                $"{SensorReach * SensorReach * 4}.");
        }

        for (int idx = 0; idx < px.Length; idx += 4)
        {
            for (int lane = 0; lane < 4; ++lane)
            {
                byte actual = px[idx + lane];
                byte anticipated = AnticipatedWipeRgba[lane];
                if (Math.Abs(actual - anticipated) > 1)
                {
                    throw new InvalidOperationException(
                        $"pixel {idx / 4} channel {lane} read 0x{actual:X2}, wanted " +
                        $"0x{anticipated:X2}.");
                }
            }
        }
    }

    private static bool Attempt(string label, Action act, List<string> misses)
    {
        try
        {
            act();
            return true;
        }
        catch (Exception problem)
        {
            misses.Add($"{label}: {problem.GetType().Name}: {problem.Message}");
            return false;
        }
    }

    private static void InspectHubAskRestart(Silk.NET.Vulkan.Vk vk, Device dev)
    {
        QueryPoolCreateInfo build = new QueryPoolCreateInfo
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = 2,
        };
        VkInterop.Check(
            vk.CreateQueryPool(dev, &build, null, out QueryPool reservoir),
            "vkCreateQueryPool");
        try
        {
            vk.ResetQueryPool(dev, reservoir, 0, 2);
        }
        finally
        {
            vk.DestroyQueryPool(dev, reservoir, null);
        }
    }

    private static void CaptureWipeAndDuplicate(
        Silk.NET.Vulkan.Vk vk,
        CommandBuffer directives,
        Image image,
        ImageView lens,
        Silk.NET.Vulkan.Buffer readback)
    {
        CommandBufferBeginInfo commence = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VkInterop.Check(vk.BeginCommandBuffer(directives, &commence), "vkBeginCommandBuffer");

        ImageSubresourceRange subresource = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };

        Barrier(
            vk,
            directives,
            image,
            subresource,
            ImageLayout.Undefined,
            ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlags2.TopOfPipeBit,
            AccessFlags2.None,
            PipelineStageFlags2.ColorAttachmentOutputBit,
            AccessFlags2.ColorAttachmentWriteBit);

        ClearValue wipe = new ClearValue
        {
            Color = new ClearColorValue
            {
                Float32_0 = AnticipatedWipeRgba[0] / 255f,
                Float32_1 = AnticipatedWipeRgba[1] / 255f,
                Float32_2 = AnticipatedWipeRgba[2] / 255f,
                Float32_3 = AnticipatedWipeRgba[3] / 255f,
            },
        };
        RenderingAttachmentInfo affix = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = lens,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = wipe,
        };
        RenderingInfo rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(SensorReach, SensorReach)),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &affix,
        };
        vk.CmdBeginRendering(directives, &rendering);
        vk.CmdEndRendering(directives);

        Barrier(
            vk,
            directives,
            image,
            subresource,
            ImageLayout.ColorAttachmentOptimal,
            ImageLayout.TransferSrcOptimal,
            PipelineStageFlags2.ColorAttachmentOutputBit,
            AccessFlags2.ColorAttachmentWriteBit,
            PipelineStageFlags2.CopyBit,
            AccessFlags2.TransferReadBit);

        BufferImageCopy zone = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(SensorReach, SensorReach, 1),
        };
        vk.CmdCopyImageToBuffer(
            directives,
            image,
            ImageLayout.TransferSrcOptimal,
            readback,
            1,
            &zone);

        VkInterop.Check(vk.EndCommandBuffer(directives), "vkEndCommandBuffer");
    }

    private static void Barrier(
        Silk.NET.Vulkan.Vk vk,
        CommandBuffer directives,
        Image image,
        ImageSubresourceRange subresource,
        ImageLayout formerArrangement,
        ImageLayout newArrangement,
        PipelineStageFlags2 srcJuncture,
        AccessFlags2 srcAccess,
        PipelineStageFlags2 destJuncture,
        AccessFlags2 destAccess)
    {
        ImageMemoryBarrier2 barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = srcJuncture,
            SrcAccessMask = srcAccess,
            DstStageMask = destJuncture,
            DstAccessMask = destAccess,
            OldLayout = formerArrangement,
            NewLayout = newArrangement,
            SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = subresource,
        };
        DependencyInfo dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier,
        };
        vk.CmdPipelineBarrier2(directives, &dep);
    }

    private static (Image Image, DeviceMemory Memory) BuildTintMark(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev)
    {
        ImageCreateInfo build = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(SensorReach, SensorReach, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        VkInterop.Check(
            vk.CreateImage(dev, &build, null, out Image image),
            "vkCreateImage (probe colour target)");

        vk.GetImageMemoryRequirements(dev, image, out MemoryRequirements requirements);
        var memory = Reserve(
            vk,
            physicalDev,
            dev,
            requirements,
            MemoryPropertyFlags.DeviceLocalBit);
        VkInterop.Check(
            vk.BindImageMemory(dev, image, memory, 0),
            "vkBindImageMemory (probe colour target)");
        return (image, memory);
    }

    private static (Silk.NET.Vulkan.Buffer Buffer, DeviceMemory Memory) BuildReadbackBuf(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev,
        uint byteTally)
    {
        BufferCreateInfo build = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = byteTally,
            Usage = BufferUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
        };
        VkInterop.Check(
            vk.CreateBuffer(dev, &build, null, out Silk.NET.Vulkan.Buffer buf),
            "vkCreateBuffer (probe readback)");

        vk.GetBufferMemoryRequirements(dev, buf, out MemoryRequirements requirements);
        var memory = Reserve(
            vk,
            physicalDev,
            dev,
            requirements,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        VkInterop.Check(
            vk.BindBufferMemory(dev, buf, memory, 0),
            "vkBindBufferMemory (probe readback)");
        return (buf, memory);
    }

    private static ImageView BuildImageLens(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        Image image)
    {
        ImageViewCreateInfo build = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        VkInterop.Check(
            vk.CreateImageView(dev, &build, null, out ImageView lens),
            "vkCreateImageView (probe colour target)");
        return lens;
    }

    private static DeviceMemory Reserve(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev,
        MemoryRequirements requirements,
        MemoryPropertyFlags props)
    {
        uint? kindOrdinal = SeekMemoryKind(
            vk,
            physicalDev,
            requirements.MemoryTypeBits,
            props);
        if (kindOrdinal is not { } ordinal)
        {
            throw new NotSupportedException(
                $"No Vulkan memory type satisfies {props} for the capability probe");
        }

        MemoryAllocateInfo reserve = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = ordinal,
        };
        VkInterop.Check(
            vk.AllocateMemory(dev, &reserve, null, out DeviceMemory memory),
            "vkAllocateMemory (probe)");
        return memory;
    }
}
