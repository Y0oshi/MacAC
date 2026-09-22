using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe partial class VkSwapchain
{
    internal VkSwapchainSetup? Configuration { get; private set; }

    internal bool IsBuilt => _swapchain.Handle is not 0;

    internal int ImageTally => _images.Length;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        DemolishImageAssetList();
        if (_swapchain.Handle is not 0)
        {
            _swapchainApi.DestroySwapchain(_device, _swapchain, null);
            _swapchain = default;
        }

        Configuration = null;
    }

    internal Image ImageAt(uint ordinal) => _images[ordinal];

    internal ImageView LensAt(uint ordinal) => _views[ordinal];

    internal Semaphore PaintDoneAt(uint ordinal) => _renderComplete[ordinal];

    internal (SurfaceCapabilitiesKHR Capabilities,
        IReadOnlyList<SurfaceFormatKHR> Formats,
        IReadOnlyList<PresentModeKHR> PresentModes) AskCanvas()
    {
        VkInterop.Check(
            _canvasApi.GetPhysicalDeviceSurfaceCapabilities(
                _physicalDev,
                _canvas,
                out SurfaceCapabilitiesKHR capabilities),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        uint fmtTally = 0;
        VkInterop.Check(
            _canvasApi.GetPhysicalDeviceSurfaceFormats(
                _physicalDev,
                _canvas,
                ref fmtTally,
                null),
            "vkGetPhysicalDeviceSurfaceFormatsKHR (count)");
        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[fmtTally];
        if (fmtTally is not 0)
        {
            fixed (SurfaceFormatKHR* lead = formats)
            {
                VkInterop.Check(
                    _canvasApi.GetPhysicalDeviceSurfaceFormats(
                        _physicalDev,
                        _canvas,
                        ref fmtTally,
                        lead),
                    "vkGetPhysicalDeviceSurfaceFormatsKHR");
            }
        }

        uint mannerTally = 0;
        VkInterop.Check(
            _canvasApi.GetPhysicalDeviceSurfacePresentModes(
                _physicalDev,
                _canvas,
                ref mannerTally,
                null),
            "vkGetPhysicalDeviceSurfacePresentModesKHR (count)");
        PresentModeKHR[] manners = new PresentModeKHR[mannerTally];
        if (mannerTally is not 0)
        {
            fixed (PresentModeKHR* lead = manners)
            {
                VkInterop.Check(
                    _canvasApi.GetPhysicalDeviceSurfacePresentModes(
                        _physicalDev,
                        _canvas,
                        ref mannerTally,
                        lead),
                    "vkGetPhysicalDeviceSurfacePresentModesKHR");
            }
        }

        return (capabilities, formats, manners);
    }

    internal bool Recreate(FramePacingRule pacing, uint framebufferWidth, uint framebufferHeight)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);

        (SurfaceCapabilitiesKHR capabilities,
            IReadOnlyList<SurfaceFormatKHR> formats,
            IReadOnlyList<PresentModeKHR> manners) = AskCanvas();

        var configuration =
            VkSwapchainSetupMint.Create(
                capabilities,
                formats,
                manners,
                pacing,
                framebufferWidth,
                framebufferHeight);
        if (!configuration.IsPresentable)
            return false;

        var former = _swapchain;
        uint* clans = stackalloc uint[2]
        {
            _clans.GraphicsFamily,
            _clans.PresentFamily,
        };
        SwapchainCreateInfoKHR build = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _canvas,
            MinImageCount = configuration.ImageCount,
            ImageFormat = configuration.ImageFormat,
            ImageColorSpace = configuration.ColorSpace,
            ImageExtent = new Extent2D(configuration.Width, configuration.Height),
            ImageArrayLayers = 1,
            ImageUsage = configuration.Usage,
            ImageSharingMode = _clans.IsUnified ? SharingMode.Exclusive : SharingMode.Concurrent,
            QueueFamilyIndexCount = _clans.IsUnified ? 0u : 2u,
            PQueueFamilyIndices = _clans.IsUnified ? null : clans,
            PreTransform = configuration.PreTransform,
            CompositeAlpha = configuration.CompositeAlpha,
            PresentMode = configuration.PresentMode,
            Clipped = true,
            OldSwapchain = former,
        };

        VkInterop.Check(
            _swapchainApi.CreateSwapchain(_device, &build, null, out SwapchainKHR built),
            "vkCreateSwapchainKHR");

        DemolishImageAssetList();
        if (former.Handle is not 0)
            _swapchainApi.DestroySwapchain(_device, former, null);

        _swapchain = built;
        Configuration = configuration;
        ObtainImages(configuration);
        return true;
    }

    internal VkSwapchainAction TryAcquire(
        Semaphore acquired,
        ulong timeoutNanoseconds,
        out uint imageOrdinal)
    {
        imageOrdinal = 0;
        if (!IsBuilt)
            return VkSwapchainAction.RecreateNow;

        Result outcome = _swapchainApi.AcquireNextImage(
            _device,
            _swapchain,
            timeoutNanoseconds,
            acquired,
            default,
            ref imageOrdinal);
        var act = VkSwapchainRecreationRule.OnObtain(outcome);
        return act == VkSwapchainAction.Fail ? throw new VkCallException("vkAcquireNextImageKHR", outcome) : act;
    }

    internal VkSwapchainAction Present(Queue presentFifo, uint imageOrdinal)
    {
        var swapchain = _swapchain;
        Semaphore pause = _renderComplete[imageOrdinal];
        uint ordinal = imageOrdinal;
        PresentInfoKHR present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &pause,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &ordinal,
        };
        Result outcome = _swapchainApi.QueuePresent(presentFifo, &present);
        var act = VkSwapchainRecreationRule.OnPresent(outcome);
        return act == VkSwapchainAction.Fail ? throw new VkCallException("vkQueuePresentKHR", outcome) : act;
    }

    // One batched vkCmdPipelineBarrier2 image transition
    internal void ChangeoverImage(
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
        _vk.CmdPipelineBarrier2(directives, &dep);
    }

    private void ObtainImages(VkSwapchainSetup configuration)
    {
        uint tally = 0;
        VkInterop.Check(
            _swapchainApi.GetSwapchainImages(_device, _swapchain, ref tally, null),
            "vkGetSwapchainImagesKHR (count)");
        _images = new Image[tally];
        fixed (Image* lead = _images)
        {
            VkInterop.Check(
                _swapchainApi.GetSwapchainImages(_device, _swapchain, ref tally, lead),
                "vkGetSwapchainImagesKHR");
        }

        _views = new ImageView[tally];
        _renderComplete = new Semaphore[tally];
        for (uint idx = 0; idx < tally; ++idx)
        {
            ImageViewCreateInfo lensBuild = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _images[idx],
                ViewType = ImageViewType.Type2D,
                Format = configuration.ImageFormat,
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
                _vk.CreateImageView(_device, &lensBuild, null, out ImageView lens),
                "vkCreateImageView (swapchain image)");
            _views[idx] = lens;

            SemaphoreCreateInfo semaphoreBuild = new SemaphoreCreateInfo
            {
                SType = StructureType.SemaphoreCreateInfo,
            };
            VkInterop.Check(
                _vk.CreateSemaphore(_device, &semaphoreBuild, null, out Semaphore semaphore),
                "vkCreateSemaphore (render complete)");
            _renderComplete[idx] = semaphore;
        }
    }

    private void CaptureGrab(
        CommandBuffer directives,
        Image image,
        Silk.NET.Vulkan.Buffer dest,
        uint width,
        uint height)
    {
        CommandBufferBeginInfo commence = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VkInterop.Check(
            _vk.BeginCommandBuffer(directives, &commence),
            "vkBeginCommandBuffer (screenshot)");

        ImageSubresourceRange subresource = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        ChangeoverImage(
            directives,
            image,
            subresource,
            ImageLayout.PresentSrcKhr,
            ImageLayout.TransferSrcOptimal,
            PipelineStageFlags2.AllCommandsBit,
            AccessFlags2.None,
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
            ImageExtent = new Extent3D(width, height, 1),
        };
        _vk.CmdCopyImageToBuffer(
            directives,
            image,
            ImageLayout.TransferSrcOptimal,
            dest,
            1,
            &zone);

        ChangeoverImage(
            directives,
            image,
            subresource,
            ImageLayout.TransferSrcOptimal,
            ImageLayout.PresentSrcKhr,
            PipelineStageFlags2.CopyBit,
            AccessFlags2.TransferReadBit,
            PipelineStageFlags2.AllCommandsBit,
            AccessFlags2.None);

        VkInterop.Check(
            _vk.EndCommandBuffer(directives),
            "vkEndCommandBuffer (screenshot)");
    }

    private void DemolishImageAssetList()
    {
        foreach (Semaphore semaphore in _renderComplete)
        {
            if (semaphore.Handle is not 0)
                _vk.DestroySemaphore(_device, semaphore, null);
        }

        foreach (ImageView lens in _views)
        {
            if (lens.Handle is not 0)
                _vk.DestroyImageView(_device, lens, null);
        }

        _renderComplete = [];
        _views = [];
        _images = [];
    }
}
