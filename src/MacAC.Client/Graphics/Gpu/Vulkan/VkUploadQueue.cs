using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkUploadQueue : IDisposable
{
    // Plan §4.3: a 48 MiB persistently mapped staging ring
    internal const ulong DefaultLoadingCapOctets = 48UL * 1024 * 1024;

    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly VkDeviceMemoryAllotter _allocator;
    private readonly VkFrameFlightDriver _flights;
    private readonly VkStagingRingLedger _loopPhase;
    private readonly VkDebugNames _diagLabels;

    private readonly Buffer _loadingBuf;
    private readonly VkAllocation _loadingAlloc;

    private readonly List<ClientBufferCopy2> _bufferCopies = [];
    private readonly List<PictureCopy2> _imageCopies = [];
    private readonly List<TemporaryLoading> _temporaries = [];

    private bool _destroyed;

    internal enum BufferDuplicateFlavor
    {
        HostStaging,
        DeviceMigration,
    }

    private readonly record struct ClientBufferCopy2(
        Buffer Source,
        Buffer Destination,
        ulong SourceOffset,
        ulong DestinationOffset,
        ulong SizeBytes,
        BufferDuplicateFlavor Kind);

    private readonly record struct PictureCopy2(
        Buffer Source,
        ulong SourceOffset,
        Image Destination,
        uint MipLevel,
        uint Layer,
        uint Width,
        uint Height);

    private readonly record struct TemporaryLoading(Buffer Buffer, VkAllocation Allocation);

    internal VkUploadQueue(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        VkDeviceMemoryAllotter allocator,
        VkFrameFlightDriver flights,
        VkDebugNames debugNames,
        ulong loadingCapOctets = DefaultLoadingCapOctets)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _flights = flights ?? throw new ArgumentNullException(nameof(flights));
        _diagLabels = debugNames ?? throw new ArgumentNullException(nameof(debugNames));
        _loopPhase = new VkStagingRingLedger(loadingCapOctets);

        (_loadingBuf, _loadingAlloc) = BuildHubBuf(
            loadingCapOctets,
            BufferUsageFlags.TransferSrcBit,
            "vk-staging-ring");
    }

    private readonly Dictionary<Image, ImageLayout> _imageEntryLayouts = [];

    private readonly List<MipBlitAsk> _mipBlits = [];

    private readonly record struct MipBlitAsk(
        Image Image,
        int Width,
        int Height,
        int MipLevelCount,
        int LayerCount);

    internal int QueuedBufDuplicateTally => _bufferCopies.Count;

    internal int QueuedImageDuplicateTally => _imageCopies.Count;

    internal ulong LoadingOnlineOctets => _loopPhase.OnlineOctets;

    internal void StageBufferWrite(
        Buffer dest,
        ulong destShiftOctets,
        ReadOnlySpan<byte> blob,
        string holderLabel)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (blob.IsEmpty)
            return;

        (Buffer src, ulong srcShift) = Stage(blob, holderLabel);
        _bufferCopies.Add(new ClientBufferCopy2(
            src,
            dest,
            srcShift,
            destShiftOctets,
            (ulong)blob.Length,
            BufferDuplicateFlavor.HostStaging));
    }

    internal void StageImageWrite(
        Image dest,
        int mipTier,
        int stratum,
        int width,
        int height,
        ImageLayout listingArrangement,
        ReadOnlySpan<byte> blob,
        string holderLabel)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (blob.IsEmpty)
            return;

        (Buffer src, ulong srcShift) = Stage(blob, holderLabel, alignmentOctets: 16);
        _imageCopies.Add(new PictureCopy2(
            src,
            srcShift,
            dest,
            (uint)mipTier,
            (uint)stratum,
            (uint)width,
            (uint)height));
        CaptureListingArrangement(dest, listingArrangement);
    }

    internal void QueueMipBlit(
        Image image,
        int width,
        int height,
        int mipTierTally,
        int stratumTally,
        ImageLayout listingArrangement)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (mipTierTally <= 1)
            return;
        _mipBlits.Add(new MipBlitAsk(image, width, height, mipTierTally, stratumTally));
        CaptureListingArrangement(image, listingArrangement);
    }

    private void CaptureListingArrangement(Image image, ImageLayout listingArrangement) =>
        _imageEntryLayouts.TryAdd(image, listingArrangement);

    internal void EnqueueBufferCopy(
        Buffer src,
        Buffer dest,
        ulong srcShiftOctets,
        ulong destShiftOctets,
        ulong byteTally)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (byteTally == 0)
            return;
        _bufferCopies.Add(new ClientBufferCopy2(
            src,
            dest,
            srcShiftOctets,
            destShiftOctets,
            byteTally,
            BufferDuplicateFlavor.DeviceMigration));
    }

    internal bool Record(CommandBuffer directives)
    {
        if (_bufferCopies.Count == 0 && _imageCopies.Count == 0 && _mipBlits.Count == 0)
            return false;

        if (_imageEntryLayouts.Count > 0)
            ChangeoverImagesToTransfer(directives);

        foreach (ClientBufferCopy2 duplicate in _bufferCopies)
        {
            if (RequiresDeviceMigrationReadBarrier(duplicate.Kind))
            {
                BufferMemoryBarrier2 barrier = CreateDeviceMigrationReadBarrier(
                    duplicate.Source,
                    duplicate.SourceOffset,
                    duplicate.SizeBytes);
                var dep = new DependencyInfo
                {
                    SType = StructureType.DependencyInfo,
                    BufferMemoryBarrierCount = 1,
                    PBufferMemoryBarriers = &barrier,
                };
                _vk.CmdPipelineBarrier2(directives, &dep);
            }

            var zone = new BufferCopy
            {
                SrcOffset = duplicate.SourceOffset,
                DstOffset = duplicate.DestinationOffset,
                Size = duplicate.SizeBytes,
            };
            _vk.CmdCopyBuffer(directives, duplicate.Source, duplicate.Destination, 1, &zone);
        }

        foreach (PictureCopy2 duplicate in _imageCopies)
        {
            var zone = new BufferImageCopy
            {
                BufferOffset = duplicate.SourceOffset,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = duplicate.MipLevel,
                    BaseArrayLayer = duplicate.Layer,
                    LayerCount = 1,
                },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D(duplicate.Width, duplicate.Height, 1),
            };
            _vk.CmdCopyBufferToImage(
                directives,
                duplicate.Source,
                duplicate.Destination,
                ImageLayout.TransferDstOptimal,
                1,
                &zone);
        }

        foreach (MipBlitAsk blit in _mipBlits)
            CaptureMipBlit(directives, blit);

        if (_imageEntryLayouts.Count > 0)
            ChangeoverImagesToShaderScan(directives);

        if (_bufferCopies.Count > 0)
        {
            var barrier = new MemoryBarrier2
            {
                SType = StructureType.MemoryBarrier2,
                SrcStageMask = PipelineStageFlags2.AllTransferBit,
                SrcAccessMask = AccessFlags2.TransferWriteBit,
                DstStageMask = PipelineStageFlags2.VertexInputBit
                    | PipelineStageFlags2.VertexShaderBit
                    | PipelineStageFlags2.FragmentShaderBit
                    | PipelineStageFlags2.DrawIndirectBit,
                DstAccessMask = AccessFlags2.VertexAttributeReadBit
                    | AccessFlags2.IndexReadBit
                    | AccessFlags2.ShaderReadBit
                    | AccessFlags2.UniformReadBit
                    | AccessFlags2.IndirectCommandReadBit,
            };
            var dep = new DependencyInfo
            {
                SType = StructureType.DependencyInfo,
                MemoryBarrierCount = 1,
                PMemoryBarriers = &barrier,
            };
            _vk.CmdPipelineBarrier2(directives, &dep);
        }

        _bufferCopies.Clear();
        _imageCopies.Clear();
        _mipBlits.Clear();
        _imageEntryLayouts.Clear();
        return true;
    }

    internal static BufferMemoryBarrier2 CreateDeviceMigrationReadBarrier(
        Buffer src,
        ulong srcShiftOctets,
        ulong byteCount)
    {
        return byteCount == 0
            ? throw new ArgumentOutOfRangeException(nameof(byteCount))
            : new BufferMemoryBarrier2
            {
                SType = StructureType.BufferMemoryBarrier2,
                SrcStageMask = PipelineStageFlags2.AllTransferBit,
                SrcAccessMask = AccessFlags2.TransferWriteBit,
                DstStageMask = PipelineStageFlags2.AllTransferBit,
                DstAccessMask = AccessFlags2.TransferReadBit,
                SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                Buffer = src,
                Offset = srcShiftOctets,
                Size = byteCount,
            };
    }

    internal static bool RequiresDeviceMigrationReadBarrier(BufferDuplicateFlavor sort) =>
        sort == BufferDuplicateFlavor.DeviceMigration;

    private static ImageSubresourceRange WholeTintImage => new()
    {
        AspectMask = ImageAspectFlags.ColorBit,
        BaseMipLevel = 0,
        LevelCount = Silk.NET.Vulkan.Vk.RemainingMipLevels,
        BaseArrayLayer = 0,
        LayerCount = Silk.NET.Vulkan.Vk.RemainingArrayLayers,
    };

    private void ChangeoverImagesToTransfer(CommandBuffer directives)
    {
        var barriers = new ImageMemoryBarrier2[_imageEntryLayouts.Count];
        int ordinal = 0;
        foreach ((Image image, ImageLayout listingArrangement) in _imageEntryLayouts)
        {
            barriers[ordinal++] = new ImageMemoryBarrier2
            {
                SType = StructureType.ImageMemoryBarrier2,
                SrcStageMask = PipelineStageFlags2.AllCommandsBit,
                SrcAccessMask = AccessFlags2.None,
                DstStageMask = PipelineStageFlags2.AllTransferBit,
                DstAccessMask = AccessFlags2.TransferWriteBit | AccessFlags2.TransferReadBit,
                OldLayout = listingArrangement,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = WholeTintImage,
            };
        }

        SubmitBarriers(directives, barriers);
    }

    private void ChangeoverImagesToShaderScan(CommandBuffer directives)
    {
        var barriers = new ImageMemoryBarrier2[_imageEntryLayouts.Count];
        int ordinal = 0;
        foreach (Image image in _imageEntryLayouts.Keys)
        {
            barriers[ordinal++] = new ImageMemoryBarrier2
            {
                SType = StructureType.ImageMemoryBarrier2,
                SrcStageMask = PipelineStageFlags2.AllTransferBit,
                SrcAccessMask = AccessFlags2.TransferWriteBit,
                DstStageMask = PipelineStageFlags2.FragmentShaderBit | PipelineStageFlags2.VertexShaderBit,
                DstAccessMask = AccessFlags2.ShaderReadBit,
                OldLayout = ImageLayout.TransferDstOptimal,
                NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = WholeTintImage,
            };
        }

        SubmitBarriers(directives, barriers);
    }

    private void SubmitBarriers(CommandBuffer directives, ImageMemoryBarrier2[] barriers)
    {
        if (barriers.Length == 0)
            return;
        fixed (ImageMemoryBarrier2* lead = barriers)
        {
            var dep = new DependencyInfo
            {
                SType = StructureType.DependencyInfo,
                ImageMemoryBarrierCount = (uint)barriers.Length,
                PImageMemoryBarriers = lead,
            };
            _vk.CmdPipelineBarrier2(directives, &dep);
        }
    }

    private void CaptureMipBlit(CommandBuffer directives, in MipBlitAsk req)
    {
        int width = req.Width;
        int height = req.Height;

        for (uint tier = 1; tier < req.MipLevelCount; tier++)
        {
            int upcomingWidth = Math.Max(1, width / 2);
            int upcomingHeight = Math.Max(1, height / 2);

            ChangeoverMipTier(
                directives,
                req.Image,
                tier - 1,
                ImageLayout.TransferDstOptimal,
                ImageLayout.TransferSrcOptimal,
                AccessFlags2.TransferWriteBit,
                AccessFlags2.TransferReadBit);

            var blit = new ImageBlit2
            {
                SType = StructureType.ImageBlit2,
                SrcSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = tier - 1,
                    BaseArrayLayer = 0,
                    LayerCount = (uint)req.LayerCount,
                },
                DstSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = tier,
                    BaseArrayLayer = 0,
                    LayerCount = (uint)req.LayerCount,
                },
            };
            blit.SrcOffsets.Element0 = new Offset3D(0, 0, 0);
            blit.SrcOffsets.Element1 = new Offset3D(width, height, 1);
            blit.DstOffsets.Element0 = new Offset3D(0, 0, 0);
            blit.DstOffsets.Element1 = new Offset3D(upcomingWidth, upcomingHeight, 1);

            var details = new BlitImageInfo2
            {
                SType = StructureType.BlitImageInfo2,
                SrcImage = req.Image,
                SrcImageLayout = ImageLayout.TransferSrcOptimal,
                DstImage = req.Image,
                DstImageLayout = ImageLayout.TransferDstOptimal,
                RegionCount = 1,
                PRegions = &blit,
                Filter = Filter.Linear,
            };
            _vk.CmdBlitImage2(directives, &details);

            ChangeoverMipTier(
                directives,
                req.Image,
                tier - 1,
                ImageLayout.TransferSrcOptimal,
                ImageLayout.TransferDstOptimal,
                AccessFlags2.TransferReadBit,
                AccessFlags2.TransferWriteBit);

            width = upcomingWidth;
            height = upcomingHeight;
        }
    }

    private void ChangeoverMipTier(
        CommandBuffer directives,
        Image image,
        uint tier,
        ImageLayout formerArrangement,
        ImageLayout newArrangement,
        AccessFlags2 srcAccess,
        AccessFlags2 destAccess)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.AllTransferBit,
            SrcAccessMask = srcAccess,
            DstStageMask = PipelineStageFlags2.AllTransferBit,
            DstAccessMask = destAccess,
            OldLayout = formerArrangement,
            NewLayout = newArrangement,
            SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = tier,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = Silk.NET.Vulkan.Vk.RemainingArrayLayers,
            },
        };
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier,
        };
        _vk.CmdPipelineBarrier2(directives, &dep);
    }

    internal void FreeFinished(long finishedSerialNo) => _loopPhase.Release(finishedSerialNo);

    private (Buffer Buffer, ulong Offset) Stage(
        ReadOnlySpan<byte> blob,
        string holderLabel,
        ulong alignmentOctets = 4)
    {
        long serialNo = _flights.OpenSerialNo != 0 ? _flights.OpenSerialNo : _flights.SubmittedSerialNo + 1;
        if (_loopPhase.TryReserve(blob.Length, alignmentOctets, serialNo, out ulong shift))
        {
            blob.CopyTo(_loadingAlloc.AsSpan().Slice((int)shift, blob.Length));
            return (_loadingBuf, shift);
        }

        (Buffer temporary, VkAllocation alloc) = BuildHubBuf(
            (ulong)blob.Length,
            BufferUsageFlags.TransferSrcBit,
            $"vk-staging-temp-{holderLabel}");
        blob.CopyTo(alloc.AsSpan());
        _temporaries.Add(new TemporaryLoading(temporary, alloc));

        Buffer grabbed = temporary;
        VkAllocation grabbedAlloc = alloc;
        _flights.Retire(() =>
        {
            _vk.DestroyBuffer(_device, grabbed, null);
            _allocator.Release(grabbedAlloc);
            _temporaries.RemoveAll(listing => listing.Buffer.Handle == grabbed.Handle);
        });
        return (temporary, 0);
    }

    private (Buffer Buffer, VkAllocation Allocation) BuildHubBuf(
        ulong byteSize,
        BufferUsageFlags usage,
        string label)
    {
        var build = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = byteSize,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };
        VkInterop.Check(
            _vk.CreateBuffer(_device, &build, null, out Buffer buf),
            $"vkCreateBuffer ({label})");

        _vk.GetBufferMemoryRequirements(_device, buf, out MemoryRequirements requirements);
        VkAllocation alloc = _allocator.Reserve(
            requirements,
            GpuMemoryTenancy.HostWritable,
            label);
        VkInterop.Check(
            _vk.BindBufferMemory(_device, buf, alloc.Memory, alloc.ShiftOctets),
            $"vkBindBufferMemory ({label})");
        _diagLabels.LabelBuf(buf, label);
        return (buf, alloc);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        foreach (TemporaryLoading temporary in _temporaries)
        {
            _vk.DestroyBuffer(_device, temporary.Buffer, null);
            _allocator.Release(temporary.Allocation);
        }
        _temporaries.Clear();

        _vk.DestroyBuffer(_device, _loadingBuf, null);
        _allocator.Release(_loadingAlloc);
        _loopPhase.Reset();
        _bufferCopies.Clear();
        _imageCopies.Clear();

    }
}
