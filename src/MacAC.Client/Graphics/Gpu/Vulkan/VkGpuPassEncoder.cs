using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkGpuPassEncoder : IGpuSweepCoder
{
    private readonly ClientVulkanGpuDevice _device;
    private readonly VkGpuFrame _cycle;
    private readonly CommandBuffer _commands;
    private readonly VulkanCycleMappings _bindings;
    private readonly uint _affixWidth;
    private readonly uint _affixHeight;
    private readonly bool _hasTintAffix;
    private readonly GpuBitmapFmt _tintFmt;

    internal int AffixWidth => (int)_affixWidth;

    internal int AffixHeight => (int)_affixHeight;

    private readonly bool _hasZDepthAffix;

    internal bool HasZDepthAffix => _hasZDepthAffix;
    private VkGpuPipeline? _pipe;
    private VkDrawWiringLedger _paintMappingPhase;
    private bool _closed;

    internal VkGpuPassEncoder(
        ClientVulkanGpuDevice dev,
        VkGpuFrame cycle,
        CommandBuffer directives,
        VulkanCycleMappings mappings,
        GpuPassSpec pass,
        uint affixWidth,
        uint affixHeight,
        bool hasZDepthAffix,
        bool hasTintAffix,
        GpuBitmapFmt tintFmt)
    {
        _device = dev;
        _cycle = cycle;
        _commands = directives;
        _bindings = mappings;
        _affixWidth = affixWidth;
        _affixHeight = affixHeight;
        _hasZDepthAffix = hasZDepthAffix;
        _hasTintAffix = hasTintAffix;
        _tintFmt = tintFmt;
        Pass = pass;

        // A pass always starts with the whole attachment drawable.
        AssignViewRect(0, 0, (int)affixWidth, (int)affixHeight);
        AssignScissor(0, 0, (int)affixWidth, (int)affixHeight);

    }

    public GpuPassSpec Pass { get; }

    public void BindPipeline(IGpuPipe pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        HurlIfClosed();
        if (pipeline is not VkGpuPipeline vulkanPipe)
            throw new ArgumentException("The Vulkan backend can only bind a Vulkan pipeline", nameof(pipeline));
        if (vulkanPipe.Description.HasTintAttachment != _hasTintAffix)
        {
            throw new InvalidOperationException(
                $"Pipeline '{vulkanPipe.Description.Name}' colour-attachment intent doesn't match pass '{Pass.Name}'.");
        }
        if (vulkanPipe.Description.LensMask != Pass.LensBitmask)
        {
            throw new InvalidOperationException(
                $"Pipeline '{vulkanPipe.Description.Name}' view mask doesn't match pass '{Pass.Name}'.");
        }

        _pipe = vulkanPipe;
        _device.Api.CmdBindPipeline(
            _commands,
            PipelineBindPoint.Graphics,
            vulkanPipe.ProcessFor(_hasZDepthAffix, _tintFmt));

        _device.CmdAttachPipeDefaults(_commands, vulkanPipe.Description);
    }

    public void AttachDepotBuf(uint mapping, IClientGpuBuffer buf, uint shiftOctets, uint byteSize)
    {
        HurlIfClosed();
        _bindings.AssignDepot(mapping, DemandBuf(buf), shiftOctets, byteSize);
        _paintMappingPhase.FlagStale();
    }

    public void AttachUniformBuf(uint mapping, IClientGpuBuffer buf, uint shiftOctets, uint byteSize)
    {
        HurlIfClosed();
        _bindings.AssignUniform(mapping, DemandBuf(buf), shiftOctets, byteSize);
        _paintMappingPhase.FlagStale();
    }

    public void AttachVertBuf(uint mapping, IClientGpuBuffer buf, uint shiftOctets)
    {
        HurlIfClosed();
        var hnd = DemandBuf(buf).Handle;
        ulong shift = shiftOctets;
        _device.Api.CmdBindVertexBuffers(_commands, mapping, 1, &hnd, &shift);
    }

    public void AttachOrdinalBuf(IClientGpuBuffer buf, uint shiftOctets, GpuOrdinalKind ordinalKind)
    {
        HurlIfClosed();
        _device.Api.CmdBindIndexBuffer(
            _commands,
            DemandBuf(buf).Handle,
            shiftOctets,
            VkViewportMapping.ToVulkan(ordinalKind));
    }

    public void AssignPushConstants(in GpuShoveConstants constants)
    {
        HurlIfClosed();
        fixed (GpuShoveConstants* ptr = &constants)
        {
            _device.Api.CmdPushConstants(
                _commands,
                _pipe?.PipeArrangement ?? _device.Arrangements.PipeArrangement,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0,
                (uint)GpuBindingModel.PushConstantOctets,
                ptr);
        }
    }

    public void AssignViewRect(int x, int y, int width, int height)
    {
        HurlIfClosed();
        Viewport viewRect = VkViewportMapping.ToVulkan(x, y, width, height, _affixHeight);
        _device.Api.CmdSetViewport(_commands, 0, 1, &viewRect);
    }

    public void AssignScissor(int x, int y, int width, int height)
    {
        HurlIfClosed();
        Rect2D scissor = VkViewportMapping.ScissorToVulkan(x, y, width, height, _affixHeight);
        _device.Api.CmdSetScissor(_commands, 0, 1, &scissor);
    }

    public void AssignPruneManner(GpuPruneManner pruneManner)
    {
        HurlIfClosed();
        _device.Api.CmdSetCullMode(_commands, VkViewportMapping.ToVulkan(pruneManner));
    }

    public void AssignFrontFace(GpuFrontFacet frontFace)
    {
        HurlIfClosed();
        _device.Api.CmdSetFrontFace(_commands, VkViewportMapping.ToVulkan(frontFace));
    }

    public void AssignZDepthEmit(bool turnedOn)
    {
        HurlIfClosed();
        _device.Api.CmdSetDepthWriteEnable(_commands, turnedOn);
    }

    public void AssignStencil(in GpuStencilLedger stencil)
    {
        HurlIfClosed();
        const StencilFaceFlags BothFaces = StencilFaceFlags.FaceFrontAndBack;
        _device.Api.CmdSetStencilOp(
            _commands,
            BothFaces,
            VkViewportMapping.ToVulkan(stencil.Fail),
            VkViewportMapping.ToVulkan(stencil.Pass),
            VkViewportMapping.ToVulkan(stencil.DepthFail),
            VkViewportMapping.ToVulkan(stencil.Compare));
        _device.Api.CmdSetStencilCompareMask(_commands, BothFaces, stencil.CompareMask);
        _device.Api.CmdSetStencilWriteMask(_commands, BothFaces, stencil.WriteMask);
        _device.Api.CmdSetStencilReference(_commands, BothFaces, stencil.Reference);
    }

    public void PaintIndexed(
        uint ordinalTally,
        uint instTally,
        uint leadOrdinal,
        int vertShift,
        uint leadInst)
    {
        HurlIfClosed();
        DemandPipe();
        DrainMappings();
        _device.Api.CmdDrawIndexed(_commands, ordinalTally, instTally, leadOrdinal, vertShift, leadInst);
    }

    public void Draw(uint vertTally, uint instTally, uint leadVert, uint leadInst)
    {
        HurlIfClosed();
        DemandPipe();
        DrainMappings();
        _device.Api.CmdDraw(_commands, vertTally, instTally, leadVert, leadInst);
    }

    public void MultiPaintIndexedIndirect(IClientGpuBuffer directives, uint shiftOctets, uint paintTally, uint strideOctets)
    {
        HurlIfClosed();
        DemandPipe();
        if (paintTally is 0)
            return;
        DrainMappings();
        _device.Api.CmdDrawIndexedIndirect(
            _commands,
            DemandBuf(directives).Handle,
            shiftOctets,
            paintTally,
            strideOctets);
    }

    public IDisposable CommenceTickerAmbit(string ambitLabel) =>
        _device.TickerReservoir.CommenceAmbit(_commands, ambitLabel);

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _device.FinishPass(this);
        _cycle.ShutPass(this);
    }

    internal void WipeAffixes(
        uint affixTally,
        ClearAttachment* affixes,
        uint rectTally,
        ClearRect* rects)
    {
        HurlIfClosed();
        _device.Api.CmdClearAttachments(_commands, affixTally, affixes, rectTally, rects);
    }

    private void DrainMappings()
    {
        var pipe = DemandPipe();
        ulong pipeArrangement = pipe.PipeArrangement.Handle;
        int bundleGen = pipe.BundlePhase?.Generation ?? 0;
        if (!_paintMappingPhase.RequiresBind(pipeArrangement, bundleGen))
            return;
        _bindings.Bind(
            _commands,
            _device,
            pipe.PipeArrangement,
            pipe.BundlePhase);
        _paintMappingPhase.FlagTied(pipeArrangement, bundleGen);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VkGpuBuffer DemandBuf(IClientGpuBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer is not VkGpuBuffer vulkanBuf
            ? throw new ArgumentException("The Vulkan backend can only bind Vulkan buffers", nameof(buffer))
            : vulkanBuf;
    }

    private VkGpuPipeline DemandPipe()
    {
        return _pipe is null ? throw new InvalidOperationException("BindPipeline has to be called prior to drawing") : _pipe;
    }

    private void HurlIfClosed() => ObjectDisposedException.ThrowIf(_closed, this);
}
