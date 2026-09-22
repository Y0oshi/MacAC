using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkSwapchainOutOfDateException(string msg) : InvalidOperationException(msg);

internal interface IVkBackbuffer
{
    Format ImageFmt { get; }

    uint Width { get; }

    uint Height { get; }

    bool TryAcquire(Semaphore acquired, out uint imageOrdinal);

    Image ImageAt(uint imageOrdinal);

    ImageView LensAt(uint imageOrdinal);

    // The semaphore a submit rendering into imageOrdinal must signal
    Semaphore PaintDoneAt(uint imageOrdinal);

    bool Present(uint imageOrdinal);
}

internal sealed unsafe partial class ClientVulkanGpuDevice : IClientGpuDevice, IGpuPipeFmtVariantHub
{
    internal const int DefaultLoopCapOctetsPerSocket = 16 * 1024 * 1024;

    internal const PipelineStageFlags2 AcquiredImagePauseJuncture =
        PipelineStageFlags2.ColorAttachmentOutputBit;
    private readonly PhysicalDevice _physicalDev;
    private readonly Queue _graphicsQueue;
    private readonly Queue _presentFifo;
    private readonly uint _visualsClan;
    private readonly IVkBackbuffer? _backbuffer;
    private readonly Semaphore _timeline;

    private readonly CommandPool[] _directiveReservoirs;
    private readonly CommandBuffer[] _directiveBufs;
    private readonly Semaphore[] _imageAcquired;

    private readonly VkRingBufferLedger[] _loopPhases;
    private readonly VkGpuBuffer[] _loopBufs;

    private readonly List<Action> _queuedActs = [];

    private VkGpuFrame? _openCycle;
    private uint? _acquiredImageOrdinal;
    private bool _destroyed;

    internal ClientVulkanGpuDevice(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev,
        Queue visualsFifo,
        Queue presentFifo,
        uint visualsClan,
        VkDeviceFeatureSupport features,
        VkDeviceLimitSupport thresholds,
        VkFormatSupport formats,
        string devLabel,
        string driverDetails,
        string apiVer,
        VkDebugNames debugNames,
        IVkBackbuffer? backbuffer = null,
        string? shaderSpirvFolder = null,
        string? pipeStashFolder = null,
        int loopCapOctetsPerSocket = DefaultLoopCapOctetsPerSocket,
        int cyclesInFlight = VkFrameFlightDriver.DefaultCyclesInFlight,
        bool retainBackbufferGrab = false)
    {
        _retainBackbufferCapture = retainBackbufferGrab;
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(formats);
        _physicalDev = physicalDev;
        _device = dev;
        _graphicsQueue = visualsFifo;
        _presentFifo = presentFifo;
        _visualsClan = visualsClan;
        _backbuffer = backbuffer;
        _diagLabels = debugNames ?? throw new ArgumentNullException(nameof(debugNames));
        ZDepthStencilFmt = formats.DepthStencilFormat;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(loopCapOctetsPerSocket);

        Capabilities = new GpuCapabilityCapture
        {
            Backend = GpuBackendFlavor.Vulkan,
            DeviceName = devLabel,
            DriverInfo = driverDetails,
            ApiVer = apiVer,
            UpperTextureChartSockets = Math.Min(
                thresholds.MaxDescriptorSetUpdateAfterBindSampledImages,
                thresholds.MaxPerStageDescriptorUpdateAfterBindSampledImages),
            UpperDepotBufMappings = GpuBindingModel.DepotMappingTally,
            UpperPushConstantBytes = thresholds.MaxPushConstantsSize,
            LowerDepotBufShiftAlignment = Math.Max(thresholds.MinStorageBufferOffsetAlignment, 1),
            UpperDepotBufSpanOctets = thresholds.MaxStorageBufferRange,
            LowerUniformBufShiftAlignment = Math.Max(thresholds.MinUniformBufferOffsetAlignment, 1),
            UpperClipGaps = thresholds.MaxClipDistances,
            UpperSpecimenTally = thresholds.MaxColorSampleCount,
            UpperImageDimension2D = thresholds.MaxImageDimension2D,
            UpperImageArrStrata = thresholds.MaxImageArrayLayers,
            DevOwnMemoryOctets = thresholds.DeviceLocalHeapBytes,
            SupportsMultiPaintIndirect = features.MultiDrawIndirect,
            SupportsPaintParams = features.ShaderDrawParameters,
            SupportsTextureCompressionBc =
                features.TextureCompressionBc && formats.Bc1Sampled && formats.Bc2Sampled && formats.Bc3Sampled,
            SupportsStampAsks = thresholds.TimestampComputeAndGraphics,
            SupportsMultiview = features.Multiview,
            SupportsPersistentlyMappedRings = true,
            SupportsRgba16FloatRasterizeMarks =
                formats.Rgba16FloatColorAttachment
                && formats.Rgba16FloatSampled
                && formats.Rgba16FloatLinearFilter
                && formats.MaxRgba16FloatSampleCount > 0,
            UpperRgba16FloatSpecimenTally =
                formats.Rgba16FloatColorAttachment
                && formats.Rgba16FloatSampled
                && formats.Rgba16FloatLinearFilter
                    ? Math.Min(formats.MaxRgba16FloatSampleCount, thresholds.MaxColorSampleCount)
                    : 0u,
            SupportsSampledZDepth = formats.DepthStencilSampled,
        };

        var timelineKind = new SemaphoreTypeCreateInfo
        {
            SType = StructureType.SemaphoreTypeCreateInfo,
            SemaphoreType = SemaphoreType.Timeline,
            InitialValue = 0,
        };
        var timelineBuild = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
            PNext = &timelineKind,
        };
        VkInterop.Check(
            _vk.CreateSemaphore(_device, &timelineBuild, null, out _timeline),
            "vkCreateSemaphore (RHI frame timeline)");

        _flights = new VkFrameFlightDriver(
            new VkTimelineApi(_vk, _device, _timeline),
            cyclesInFlight);
        _allocator = new VkDeviceMemoryAllotter(_vk, physicalDev, _device);
        _uploads = new VkUploadQueue(_vk, _device, _allocator, _flights, _diagLabels);

        int sockets = _flights.SocketTally;
        _directiveReservoirs = new CommandPool[sockets];
        _directiveBufs = new CommandBuffer[sockets];
        _imageAcquired = new Semaphore[sockets];
        _loopPhases = new VkRingBufferLedger[sockets];
        _loopBufs = new VkGpuBuffer[sockets];
        for (int socket = 0; socket < sockets; socket++)
        {
            var reservoirBuild = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = visualsClan,
            };
            VkInterop.Check(
                _vk.CreateCommandPool(_device, &reservoirBuild, null, out CommandPool reservoir),
                "vkCreateCommandPool (RHI flight slot)");
            _directiveReservoirs[socket] = reservoir;

            var reserve = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = reservoir,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VkInterop.Check(
                _vk.AllocateCommandBuffers(_device, &reserve, out CommandBuffer directives),
                "vkAllocateCommandBuffers (RHI flight slot)");
            _directiveBufs[socket] = directives;

            var semaphoreBuild = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            VkInterop.Check(
                _vk.CreateSemaphore(_device, &semaphoreBuild, null, out Semaphore acquired),
                "vkCreateSemaphore (RHI image acquired)");
            _imageAcquired[socket] = acquired;

            _loopPhases[socket] = new VkRingBufferLedger((ulong)loopCapOctetsPerSocket);
            _loopBufs[socket] = new VkGpuBuffer(
                _vk,
                _device,
                _allocator,
                _uploads,
                _flights,
                _diagLabels,
                new GpuBufferSpec(
                    $"vk-ring-slot-{socket}",
                    loopCapOctetsPerSocket,
                    GpuBufferPurpose.Storage
                        | GpuBufferPurpose.Uniform
                        | GpuBufferPurpose.Indirect
                        | GpuBufferPurpose.Vertex
                        | GpuBufferPurpose.Index,
                    GpuMemoryTenancy.HostWritable));

            if (!_loopBufs[socket].IsMapped)
            {
                throw new NotSupportedException(
                    "The Vulkan per-frame ring must live in host-visible memory; this device " +
                    "offered no host-visible type for a HostWritable buffer");
            }
        }

        InitialiseAssetList(shaderSpirvFolder, pipeStashFolder);
    }

    public GpuBackendFlavor Backend => GpuBackendFlavor.Vulkan;

    public GpuCapabilityCapture Capabilities { get; }

    public IGpuAssetSunsetFifo Retirement => _flights;

    internal Format ZDepthStencilFmt { get; }

    private readonly Silk.NET.Vulkan.Vk _vk;

    internal Silk.NET.Vulkan.Vk Api => _vk;
    private readonly Device _device;

    internal Device Handle => _device;
    private readonly VkDeviceMemoryAllotter _allocator;

    internal VkDeviceMemoryAllotter Allocator => _allocator;
    private readonly VkUploadQueue _uploads;

    internal VkUploadQueue Uploads => _uploads;
    private readonly VkDebugNames _diagLabels;

    internal VkDebugNames DiagLabels => _diagLabels;
    private readonly VkFrameFlightDriver _flights;

    internal VkFrameFlightDriver Flights => _flights;
    internal CommandBuffer LatestDirectives => _directiveBufs[_flights.LatestSocket];

    public IClientGpuBuffer BuildBuf(in GpuBufferSpec blurb)
    {
        HurlIfDestroyed();
        return new VkGpuBuffer(_vk, _device, _allocator, _uploads, _flights, _diagLabels, blurb);
    }

    public void EnqueueDevAct(Action act)
    {
        ArgumentNullException.ThrowIfNull(act);
        _queuedActs.Add(act);
    }

    public void HandleDevActs()
    {
        Action[] queued = [.. _queuedActs];
        _queuedActs.Clear();
        foreach (Action act in queued)
            act();
    }

    public IGpuCycle BeginFrame() =>
        TryBeginFrame(out IGpuCycle? cycle) && cycle is not null
            ? cycle
            : throw new VkSwapchainOutOfDateException(
                "The swapchain is out of date and has to be recreated prior to another frame is recorded");

    internal bool TryBeginFrame(out IGpuCycle? cycle)
    {
        HurlIfDestroyed();
        cycle = null;
        if (_openCycle is not null)
            throw new InvalidOperationException("A frame is by now open; end it prior to beginning another");

        long serialNo = _flights.BeginFrame();
        _captureValidity.BeginFrame();
        int socket = _flights.LatestSocket;

        _uploads.FreeFinished(CompletedSerial());
        _loopPhases[socket].Reset();
        CycleMappingsAt(socket).BeginFrame();

        _acquiredImageOrdinal = null;
        if (_backbuffer is not null)
        {
            if (!_backbuffer.TryAcquire(_imageAcquired[socket], out uint imageOrdinal))
            {
                SignalTimelineWithoutJob(serialNo);
                _flights.EndFrame();
                return false;
            }

            _acquiredImageOrdinal = imageOrdinal;
        }

        VkInterop.Check(
            _vk.ResetCommandPool(_device, _directiveReservoirs[socket], 0),
            "vkResetCommandPool");
        var commence = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VkInterop.Check(
            _vk.BeginCommandBuffer(_directiveBufs[socket], &commence),
            "vkBeginCommandBuffer (frame)");

        _backbufferRenderingPrimed = false;

        CommenceCycleAssetList(socket);

        var opened = new VkGpuFrame(this, socket, serialNo);
        _openCycle = opened;
        cycle = opened;
        return true;
    }

    private long CompletedSerial()
    {
        VkInterop.Check(
            _vk.GetSemaphoreCounterValue(_device, _timeline, out ulong val),
            "vkGetSemaphoreCounterValue (upload release)");
        return (long)val;
    }

    internal GpuLoopAlloc ReserveLoop(int socketOrdinal, int byteTally, GpuLoopPurpose usage)
    {
        HurlIfDestroyed();
        ulong alignment = usage switch
        {
            GpuLoopPurpose.Storage => Capabilities.LowerDepotBufShiftAlignment,
            GpuLoopPurpose.Uniform => Capabilities.LowerUniformBufShiftAlignment,
            GpuLoopPurpose.Indirect => 4,
            _ => 16,
        };

        ulong shift = _loopPhases[socketOrdinal].Reserve(byteTally, alignment);
        VkGpuBuffer buf = _loopBufs[socketOrdinal];
        Span<byte> blob = byteTally == 0
            ? []
            : buf.MappedSpan.Slice((int)shift, byteTally);
        return new GpuLoopAlloc(buf, (uint)shift, blob);
    }

    internal void EndFrame(VkGpuFrame cycle)
    {
        if (!ReferenceEquals(_openCycle, cycle))
            return;

        int socket = cycle.SocketOrdinal;
        CommandBuffer directives = _directiveBufs[socket];

        EndFrameRest(socket, directives);

        _uploads.Record(directives);

        if (_acquiredImageOrdinal is { } imageOrdinal && _backbuffer is not null)
        {
            Image presentable = _backbuffer.ImageAt(imageOrdinal);
            ImageLayout latest = RecordBackbufferCapture(directives, presentable);
            ChangeoverBackbufferForPresent(directives, presentable, latest);
        }

        VkInterop.Check(_vk.EndCommandBuffer(directives), "vkEndCommandBuffer (frame)");

        var directiveSubmit = new CommandBufferSubmitInfo
        {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = directives,
        };
        SemaphoreSubmitInfo pauseSemaphore = CreateAcquiredImageWait(_imageAcquired[socket]);
        SemaphoreSubmitInfo* signals = stackalloc SemaphoreSubmitInfo[2];
        int signalTally = 0;
        if (_acquiredImageOrdinal is { } presented && _backbuffer is not null)
        {
            signals[signalTally++] = new SemaphoreSubmitInfo
            {
                SType = StructureType.SemaphoreSubmitInfo,
                Semaphore = _backbuffer.PaintDoneAt(presented),
                StageMask = PipelineStageFlags2.AllCommandsBit,
            };
        }

        signals[signalTally++] = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = _timeline,
            Value = (ulong)cycle.SerialNo,
            StageMask = PipelineStageFlags2.AllCommandsBit,
        };

        var submit = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            WaitSemaphoreInfoCount = _acquiredImageOrdinal is null ? 0u : 1u,
            PWaitSemaphoreInfos = _acquiredImageOrdinal is null ? null : &pauseSemaphore,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &directiveSubmit,
            SignalSemaphoreInfoCount = (uint)signalTally,
            PSignalSemaphoreInfos = signals,
        };
        VkInterop.Check(
            _vk.QueueSubmit2(_graphicsQueue, 1, &submit, default),
            "vkQueueSubmit2 (frame)");
        _captureValidity.CompleteSubmission();

        _flights.EndFrame();
        _openCycle = null;

        if (_acquiredImageOrdinal is { } toPresent && _backbuffer is not null)
        {
            PresentSucceeded = _backbuffer.Present(toPresent);
            _acquiredImageOrdinal = null;
        }
    }

    internal static SemaphoreSubmitInfo CreateAcquiredImageWait(Semaphore semaphore) => new()
    {
        SType = StructureType.SemaphoreSubmitInfo,
        Semaphore = semaphore,
        StageMask = AcquiredImagePauseJuncture,
    };

    // False after a present that reported the swapchain should be rebuilt
    internal bool PresentSucceeded { get; private set; } = true;

    private void ChangeoverBackbufferForPresent(CommandBuffer directives, Image image, ImageLayout latestArrangement)
    {
        bool grabbed = latestArrangement == ImageLayout.TransferSrcOptimal;
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = grabbed
                ? PipelineStageFlags2.CopyBit
                : PipelineStageFlags2.ColorAttachmentOutputBit,
            SrcAccessMask = grabbed
                ? AccessFlags2.TransferReadBit
                : AccessFlags2.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags2.BottomOfPipeBit,
            DstAccessMask = AccessFlags2.None,
            OldLayout = latestArrangement,
            NewLayout = ImageLayout.PresentSrcKhr,
            SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
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

    private void SignalTimelineWithoutJob(long serialNo)
    {
        var signal = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = _timeline,
            Value = (ulong)serialNo,
            StageMask = PipelineStageFlags2.AllCommandsBit,
        };
        var submit = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            SignalSemaphoreInfoCount = 1,
            PSignalSemaphoreInfos = &signal,
        };
        VkInterop.Check(
            _vk.QueueSubmit2(_graphicsQueue, 1, &submit, default),
            "vkQueueSubmit2 (abandoned frame timeline signal)");
    }

    public void PauseIdle()
    {
        if (_destroyed)
            return;
        VkInterop.Check(_vk.DeviceWaitIdle(_device), "vkDeviceWaitIdle");
        _flights.PauseForSubmittedJob();
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        _vk.DeviceWaitIdle(_device);
        TeardownAssetList();
        _flights.EmptyAll();

        foreach (VkGpuBuffer loop in _loopBufs)
            loop.Dispose();
        _flights.EmptyAll();

        foreach (Semaphore semaphore in _imageAcquired)
        {
            if (semaphore.Handle != 0)
                _vk.DestroySemaphore(_device, semaphore, null);
        }

        foreach (CommandPool reservoir in _directiveReservoirs)
        {
            if (reservoir.Handle != 0)
                _vk.DestroyCommandPool(_device, reservoir, null);
        }

        _uploads.Dispose();
        _flights.EmptyAll();

        if (_timeline.Handle != 0)
            _vk.DestroySemaphore(_device, _timeline, null);

        _flights.Dispose();
        _allocator.Dispose();
    }

    private void HurlIfDestroyed() => ObjectDisposedException.ThrowIf(_destroyed, this);
}
