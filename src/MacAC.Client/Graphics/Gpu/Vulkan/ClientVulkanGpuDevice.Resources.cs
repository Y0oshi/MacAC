using System.Numerics;
using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe partial class ClientVulkanGpuDevice
{
    private VulkanPipeArrangements.CreatedDef? _arrangements;
    private VkTextureChart? _textureChart;
    private VkBackbufferAttachments? _backbufferAffixes;
    private VkGpuTexture? _defaultTexture;
    private VkPipelineShelf? _pipeStash;
    private VkGpuTimerPool? _tickerReservoir;
    private VkGpuBuffer? _mappingDummy;
    private VulkanCycleMappings[] _cycleMappings = [];

    private readonly Dictionary<GpuSamplerSpec, VkGpuSampler> _samplers = [];
    private readonly Dictionary<string, (ShaderModule Vertex, ShaderModule Fragment)> _shaderModules = [];
    private readonly HashSet<VkGpuPipeline> _pipes = [];
    private readonly Dictionary<GpuBitmapFmt, int> _pipeFmtTenancyCounts = [];
    private readonly object _assetCreationSynchronize = new();
    private string _shaderSpirvFolder = string.Empty;
    private float _upperSamplerAnisotropy = 1f;

    private VkGpuPassEncoder? _openPass;
    private bool _openPassIsBackbuffer;

    private const string CycleTickerAmbitLabel = "frame";

    private int[] _cycleTickerTags = [];
    private readonly Queue<(int Tag, long ElapsedUs)> _cycleGpuSpecimens = new();
    private IDisposable? _openCycleTickerAmbit;

    /// <summary>
    /// Stands up everything the device owns for the whole of its life, in the one order that works:
    /// the limits decide how big the texture table may be, the layouts are what the table and the
    /// per-frame bindings are built against, and the default texture has to exist before the table
    /// can be told what to scrub evicted slots with.
    /// </summary>
    private void InitialiseAssetList(string? shaderSpirvFolder, string? pipeStashFolder)
    {
        _shaderSpirvFolder = shaderSpirvFolder ?? string.Empty;

        _vk.GetPhysicalDeviceProperties(_physicalDev, out PhysicalDeviceProperties props);
        _upperSamplerAnisotropy = props.Limits.MaxSamplerAnisotropy;

        var arrangements = VulkanPipeArrangements.Create(_vk, Handle);
        _arrangements = arrangements;
        _pipeStash = new VkPipelineShelf(_vk, _physicalDev, Handle, pipeStashFolder);

        InitialiseTimers();
        InitialiseTextureChart(arrangements);
        InitialiseBindings(arrangements);
    }

    /// <summary>One timer pool per flight socket, with no frame tagged to begin with.</summary>
    private void InitialiseTimers()
    {
        _tickerReservoir = new VkGpuTimerPool(
            _vk,
            _physicalDev,
            Handle,
            _flights.SocketTally,
            Capabilities.SupportsStampAsks);
        _cycleTickerTags = new int[_flights.SocketTally];
        Array.Fill(_cycleTickerTags, -1);
    }

    /// <summary>
    /// The bindless texture table, its backbuffer attachments, and the one texture every slot falls
    /// back to. That texture is registered first so it lands in slot 0, and it is also what an
    /// evicted slot is scrubbed with, so nothing ever samples a slot that names no image.
    /// </summary>
    private void InitialiseTextureChart(VulkanPipeArrangements.CreatedDef arrangements)
    {
        var chart = new VkTextureChart(
            _vk,
            Handle,
            arrangements.TextureChart,
            Math.Min(GpuBindingModel.TextureTableCapacity, Capabilities.UpperTextureChartSockets));
        _textureChart = chart;
        _backbufferAffixes = new VkBackbufferAttachments(
            _vk,
            Handle,
            _allocator,
            _diagLabels,
            ZDepthStencilFmt);

        _defaultTexture = new VkGpuTexture(
            _vk,
            Handle,
            _allocator,
            _uploads,
            _flights,
            _diagLabels,
            new GpuBitmapSpec(
                "vk-default-white",
                GpuBitmapFlavor.Texture2DArray,
                GpuBitmapFmt.Rgba8Unorm,
                Width: 1,
                Height: 1,
                LayerCount: 1,
                MipLevelCount: 1));
        _defaultTexture.Upload(0, 0, [255, 255, 255, 255]);

        var defaultSampler = (VkGpuSampler)BuildSampler(GpuSamplerSpec.WidgetClosest);
        chart.AssignScrubMark(_defaultTexture.View, defaultSampler.Handle);
        DefaultTextureSocket = chart.Register(_defaultTexture.View, defaultSampler.Handle);
    }

    /// <summary>
    /// The per-frame binding sets, and the one buffer every unused binding points at. Without that
    /// buffer each renderer would need its own descriptor set layout, one per combination of
    /// bindings it happens to leave empty.
    /// </summary>
    private void InitialiseBindings(VulkanPipeArrangements.CreatedDef arrangements)
    {
        var dummy = new VkGpuBuffer(
            _vk,
            Handle,
            _allocator,
            _uploads,
            _flights,
            _diagLabels,
            new GpuBufferSpec(
                "vk-binding-dummy",
                65536,
                GpuBufferPurpose.Storage | GpuBufferPurpose.Uniform,
                GpuMemoryTenancy.HostWritable));
        _mappingDummy = dummy;

        _cycleMappings = new VulkanCycleMappings[_flights.SocketTally];
        for (int socket = 0; socket < _flights.SocketTally; socket++)
        {
            _cycleMappings[socket] = new VulkanCycleMappings(
                _vk,
                Handle,
                arrangements,
                _loopBufs[socket],
                dummy,
                Capabilities.UpperDepotBufSpanOctets);
        }
    }

    private void CommenceCycleAssetList(int socketOrdinal)
    {
        if (_tickerReservoir is null)
            return;

        // BeginSlot reads back whatever this slot recorded the last time it was
        // used. That measurement belongs to the frame whose tag the slot still
        // holds - read it BEFORE BeginFrameTimerScope overwrites the tag.
        int issuingTag = _cycleTickerTags[socketOrdinal];
        _tickerReservoir.CommenceSocket(socketOrdinal);
        if (issuingTag >= 0
            && _tickerReservoir.TryGrabSettled(CycleTickerAmbitLabel, out double millis))
        {
            _cycleGpuSpecimens.Enqueue((issuingTag, (long)(millis * 1000d)));
        }
    }

    internal void CommenceCycleTickerAmbit(int cycleOrdinal)
    {
        if (_openCycle is null || _tickerReservoir is null || _openCycleTickerAmbit is not null)
            return;

        int socket = _openCycle.SocketOrdinal;
        _cycleTickerTags[socket] = cycleOrdinal;
        _openCycleTickerAmbit = _tickerReservoir.CommenceAmbit(_directiveBufs[socket], CycleTickerAmbitLabel);
    }

    internal void FinishCycleTickerAmbit()
    {
        _openCycleTickerAmbit?.Dispose();
        _openCycleTickerAmbit = null;
    }

    internal bool TryGrabCycleGpuSpecimen(out int cycleOrdinal, out long passedUs)
    {
        if (_cycleGpuSpecimens.Count == 0)
        {
            cycleOrdinal = -1;
            passedUs = 0;
            return false;
        }

        (cycleOrdinal, passedUs) = _cycleGpuSpecimens.Dequeue();
        return true;
    }

    private VulkanCycleMappings CycleMappingsAt(int socketOrdinal) => _cycleMappings[socketOrdinal];

    private void EndFrameRest(int socketOrdinal, CommandBuffer directives)
    {
        _ = socketOrdinal;
        _ = directives;
        FinishCycleTickerAmbit();
        if (_openPass is not null)
        {
            throw new InvalidOperationException(
                "A pass is still open at frame end. Dispose the encoder before ending the frame — " +
                "a dynamic-rendering block left open makes the whole command buffer invalid.");
        }
    }

    private void TeardownAssetList()
    {
        _openCycleTickerAmbit = null;
        _cycleGpuSpecimens.Clear();
        _cycleTickerTags = [];
        foreach (VulkanCycleMappings mappings in _cycleMappings)
            mappings.Dispose();
        _cycleMappings = [];

        foreach ((ShaderModule vert, ShaderModule fragment) in _shaderModules.Values)
        {
            if (vert.Handle != 0)
                _vk.DestroyShaderModule(_device, vert, null);
            if (fragment.Handle != 0)
                _vk.DestroyShaderModule(_device, fragment, null);
        }

        _shaderModules.Clear();
        _pipes.Clear();
        _pipeFmtTenancyCounts.Clear();

        foreach (VkGpuSampler sampler in _samplers.Values)
            sampler.Dispose();
        _samplers.Clear();

        _mappingDummy?.Dispose();
        _mappingDummy = null;
        _captureBuffer?.Dispose();
        _captureBuffer = null;
        _defaultTexture?.Dispose();
        _defaultTexture = null;

        _flights.EmptyAll();

        _tickerReservoir?.Dispose();
        _tickerReservoir = null;
        _pipeStash?.Dispose();
        _pipeStash = null;
        _backbufferAffixes?.Dispose();
        _backbufferAffixes = null;
        _textureChart?.Dispose();
        _textureChart = null;
        _arrangements?.Destroy(_vk, Handle);
        _arrangements = null;
    }

    // The three shared descriptor set layouts and the one pipeline layout
    internal VulkanPipeArrangements.CreatedDef Arrangements =>
        _arrangements ?? throw new InvalidOperationException("The device's pipeline layouts have not been created.");

    // The global sampled-texture table (plan §4.4)
    internal VkTextureChart TextureChart =>
        _textureChart ?? throw new InvalidOperationException("The device's texture table has not been created.");

    internal VkBackbufferAttachments BackbufferAffixes =>
        _backbufferAffixes ?? throw new InvalidOperationException("The backbuffer attachments have not been created.");

    internal VkGpuTimerPool TickerReservoir =>
        _tickerReservoir ?? throw new InvalidOperationException("The device's timer pool has not been created.");

    internal bool PipeStashFetchedFromDisk => _pipeStash?.FetchedFromDisk ?? false;

    public GpuTextureSlot DefaultTextureSocket { get; private set; } = GpuTextureSlot.Unassigned;

    public IGpuTickerReservoir Tickers => TickerReservoir;

    internal void ConfigureBackbufferAffixes(uint width, uint height, Format tintFmt, int specimenTally)
    {
        BackbufferAffixes.Configure(width, height, tintFmt, specimenTally);
        ConfigureBackbufferCapture(width, height);
    }

    public IGpuBitmap BuildTexture(in GpuBitmapSpec blurb)
    {
        HurlIfDestroyed();
        if (blurb.Format == GpuBitmapFmt.Rgba16FloatRenderTarget
            && !Capabilities.SupportsRgba16FloatRasterizeMarks)
        {
            throw new NotSupportedException(
                "RGBA16F colour-attachment, sampling, and linear filtering are unavailable.");
        }
        return blurb.Format == GpuBitmapFmt.Depth24Stencil8
            && !Capabilities.SupportsSampledZDepth
            ? throw new NotSupportedException(
                "The selected combined depth/stencil format cannot expose a sampled depth aspect.")
            : (IGpuBitmap)new VkGpuTexture(
            _vk,
            Handle,
            _allocator,
            _uploads,
            _flights,
            _diagLabels,
            blurb,
            specimenTally: 1,
            rasterizeMark: VkTextureFormatMapping.IsRasterizeMark(blurb.Format));
    }

    public IClientGpuSampler BuildSampler(in GpuSamplerSpec blurb)
    {
        lock (_assetCreationSynchronize)
            return BuildSamplerBolted(in blurb);
    }

    private IClientGpuSampler BuildSamplerBolted(in GpuSamplerSpec blurb)
    {
        HurlIfDestroyed();
        if (_samplers.TryGetValue(blurb, out VkGpuSampler? extant)
            && !extant.IsDestroyed)
            return extant;

        var built = new VkGpuSampler(
            _vk,
            Handle,
            _flights,
            _diagLabels,
            blurb,
            _upperSamplerAnisotropy);
        _samplers[blurb] = built;
        return built;
    }

    public IGpuRasterizeMark BuildRasterizeMark(in GpuRenderTargetSpec blurb)
    {
        HurlIfDestroyed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blurb.SampleCount);
        if ((uint)blurb.SampleCount > Capabilities.UpperSpecimenTally)
        {
            throw new NotSupportedException(
                $"The device supports at most {Capabilities.UpperSpecimenTally} colour/depth samples; "
                + $"'{blurb.Name}' requested {blurb.SampleCount}.");
        }
        if (blurb.ColorFormat == GpuBitmapFmt.Rgba16FloatRenderTarget)
        {
            if (!Capabilities.SupportsRgba16FloatRasterizeMarks)
            {
                throw new NotSupportedException(
                    "RGBA16F colour-attachment, sampling, and linear filtering are required by this render target.");
            }
            if ((uint)blurb.SampleCount > Capabilities.UpperRgba16FloatSpecimenTally)
            {
                throw new NotSupportedException(
                    $"RGBA16F supports at most {Capabilities.UpperRgba16FloatSpecimenTally} samples on this device; "
                    + $"'{blurb.Name}' requested {blurb.SampleCount}.");
            }
        }
        return blurb.SampleableDepth && !Capabilities.SupportsSampledZDepth
            ? throw new NotSupportedException(
                "The selected combined depth/stencil format cannot expose a sampled depth aspect.")
            : (IGpuRasterizeMark)new VkGpuRenderTarget(
            _vk,
            Handle,
            _allocator,
            _uploads,
            _flights,
            _diagLabels,
            blurb,
            ZDepthStencilFmt);
    }

    public IGpuDirectedZDepthMark BuildDirectedZDepthMark(
        in GpuDirectionalDepthTargetSpec description)
    {
        HurlIfDestroyed();
        ArgumentException.ThrowIfNullOrWhiteSpace(description.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(description.Resolution);
        if (description.LayerCount is < 2 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(description),
                description.LayerCount,
                "Directional depth targets require 2-4 cascade layers.");
        }
        if (description.DepthFormat != GpuBitmapFmt.Depth24Stencil8)
        {
            throw new ArgumentException(
                "Directional depth targets currently require Depth24Stencil8.",
                nameof(description));
        }
        return !Capabilities.SupportsSampledZDepth
            ? throw new NotSupportedException("Sampled depth is unavailable on this device.")
            : (IGpuDirectedZDepthMark)new VkDirectionalDepthTarget(
            _vk,
            Handle,
            _allocator,
            _uploads,
            _flights,
            _diagLabels,
            description,
            ZDepthStencilFmt);
    }

    public GpuTextureSlot EnrollTexture(IGpuBitmap texture, IClientGpuSampler sampler)
    {
        HurlIfDestroyed();
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(sampler);
        if (texture is not VkGpuTexture vulkanTexture)
            throw new ArgumentException("The Vulkan backend can only register a Vulkan texture.", nameof(texture));
        if (sampler is not VkGpuSampler vulkanSampler)
            throw new ArgumentException("The Vulkan backend can only register a Vulkan sampler.", nameof(sampler));
        return !vulkanTexture.IsSampleable || vulkanTexture.SampledLens.Handle == 0
            ? throw new ArgumentException(
                $"Texture '{vulkanTexture.Name}' is an attachment-only image and has no sampled view.",
                nameof(texture))
            : TextureChart.Register(
            vulkanTexture.SampledLens,
            vulkanSampler.Handle,
            vulkanTexture.SampledArrangement);
    }

    public void FreeTextureSocket(GpuTextureSlot slot)
    {
        HurlIfDestroyed();
        if (!slot.IsAssigned)
            throw new ArgumentException("Cannot release an unassigned texture slot.", nameof(slot));

        VkTextureChart chart = TextureChart;
        _flights.Retire(() => chart.FreeInstant(slot));
    }

    public IGpuPipe BuildPipe(GpuPipeSpec blurb)
    {
        lock (_assetCreationSynchronize)
            return BuildPipeBolted(blurb);
    }

    private IGpuPipe BuildPipeBolted(GpuPipeSpec blurb)
    {
        HurlIfDestroyed();
        ArgumentNullException.ThrowIfNull(blurb);
        if (blurb.LensMask != 0 && !Capabilities.SupportsMultiview)
            throw new NotSupportedException("The selected Vulkan device does not support multiview pipelines.");

        (ShaderModule vert, ShaderModule fragment, bool ownsModules) =
            PullShaderModules(blurb.Shaders);
        Format tintFmt = VkTextureFormatMapping.ComposeOf(blurb.TintFmt);
        VkGpuPipeline pipe;
        VulkanPipeArrangements.CreatedDef.PackArrangementLease? bundleTenancy = null;
        try
        {
            bundleTenancy = blurb.UsesRasterizeBundleShaderAbi
                ? Arrangements.ObtainBundleArrangement()
                : null;
            pipe = new VkGpuPipeline(
                _vk,
                Handle,
                _flights,
                _diagLabels,
                Arrangements,
                bundleTenancy,
                bundleTenancy?.PipeArrangement ?? Arrangements.PipeArrangement,
                _pipeStash?.Handle ?? default,
                vert,
                fragment,
                ownsModules,
                blurb,
                tintFmt,
                ZDepthStencilFmt);
        }
        catch
        {
            bundleTenancy?.Dispose();
            if (ownsModules)
            {
                _vk.DestroyShaderModule(_device, fragment, null);
                _vk.DestroyShaderModule(_device, vert, null);
            }
            throw;
        }
        try
        {
            foreach (GpuBitmapFmt fmt in _pipeFmtTenancyCounts.Keys)
                pipe.AppendTintFmtVariant(fmt);
            _pipes.Add(pipe);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    public IDisposable ObtainPipeTintFmt(GpuBitmapFmt fmt)
    {
        lock (_assetCreationSynchronize)
            return ObtainPipeTintFmtBolted(fmt);
    }

    private IDisposable ObtainPipeTintFmtBolted(GpuBitmapFmt format)
    {
        HurlIfDestroyed();
        if (!VkTextureFormatMapping.IsRasterizeMark(format)
            || VkTextureFormatMapping.IsZDepthStencil(format))
        {
            throw new ArgumentException(
                $"{format} is not a colour render-target format.",
                nameof(format));
        }
        if (format == GpuBitmapFmt.Rgba16FloatRenderTarget
            && !Capabilities.SupportsRgba16FloatRasterizeMarks)
        {
            throw new NotSupportedException(
                "RGBA16F colour-attachment, sampling, and linear filtering are unavailable.");
        }

        _pipes.RemoveWhere(static pipe => pipe.IsDestroyed);
        if (!_pipeFmtTenancyCounts.TryGetValue(format, out int tally))
        {
            var added = new List<VkGpuPipeline>(_pipes.Count);
            try
            {
                foreach (VkGpuPipeline pipeline in _pipes)
                {
                    if (pipeline.AppendTintFmtVariant(format))
                        added.Add(pipeline);
                }
            }
            catch
            {
                foreach (VkGpuPipeline pipeline in added)
                    pipeline.DropTintFmtVariant(format);
                throw;
            }
            _pipeFmtTenancyCounts.Add(format, 1);
        }
        else
        {
            _pipeFmtTenancyCounts[format] = checked(tally + 1);
        }

        return new PipeTintFmtTenancy(this, format);
    }

    private void FreePipeTintFmt(GpuBitmapFmt fmt)
    {
        lock (_assetCreationSynchronize)
        {
            if (_destroyed || !_pipeFmtTenancyCounts.TryGetValue(fmt, out int tally))
                return;
            if (tally > 1)
            {
                _pipeFmtTenancyCounts[fmt] = tally - 1;
                return;
            }

            _pipeFmtTenancyCounts.Remove(fmt);
            _pipes.RemoveWhere(static pipe => pipe.IsDestroyed);
            foreach (VkGpuPipeline pipeline in _pipes)
                pipeline.DropTintFmtVariant(fmt);
        }
    }

    private sealed class PipeTintFmtTenancy(
        ClientVulkanGpuDevice dev,
        GpuBitmapFmt fmt) : IDisposable
    {
        private ClientVulkanGpuDevice? _device = dev;

        public void Dispose() =>
            Interlocked.Exchange(ref _device, null)?.FreePipeTintFmt(fmt);
    }

    private (ShaderModule Vertex, ShaderModule Fragment, bool OwnsModules) PullShaderModules(
        in GpuShaderGroup shaders)
    {
        if (shaders.HasEmbeddedSpirv)
        {
            ShaderModule embeddedVert = BuildShaderModule(
                shaders.Name,
                "vert",
                shaders.VertSpirv.Span);
            try
            {
                return (
                    embeddedVert,
                    BuildShaderModule(shaders.Name, "frag", shaders.FragmentSpirv.Span),
                    true);
            }
            catch
            {
                _vk.DestroyShaderModule(_device, embeddedVert, null);
                throw;
            }
        }

        string label = shaders.Name;
        if (_shaderModules.TryGetValue(label, out (ShaderModule Vertex, ShaderModule Fragment) extant))
            return (extant.Vertex, extant.Fragment, false);

        ShaderModule vert = BuildShaderModule(label, "vert");
        ShaderModule fragment = BuildShaderModule(label, "frag");
        _shaderModules[label] = (vert, fragment);
        return (vert, fragment, false);
    }

    private ShaderModule BuildShaderModule(string label, string juncture)
    {
        string trail = Path.Combine(_shaderSpirvFolder, $"{label}.{juncture}.spv");
        return !File.Exists(trail)
            ? throw new FileNotFoundException(
                $"No committed SPIR-V for '{label}.{juncture}'. Run tools/compile-shaders.ps1; if that " +
                "cannot compile the shader for Vulkan, no Vulkan pipeline can be built from it.",
                trail)
            : BuildShaderModule(label, juncture, File.ReadAllBytes(trail));
    }

    private ShaderModule BuildShaderModule(
        string label,
        string juncture,
        ReadOnlySpan<byte> code)
    {
        if (code.Length < 4 || code.Length % 4 != 0)
            throw new InvalidDataException(
                $"'{label}.{juncture}' is {code.Length} bytes, which is not valid word-aligned SPIR-V.");
        if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(code) != 0x07230203u)
            throw new InvalidDataException($"'{label}.{juncture}' has no SPIR-V header.");

        fixed (byte* lead = code)
        {
            var build = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)lead,
            };
            VkInterop.Check(
                _vk.CreateShaderModule(_device, &build, null, out ShaderModule module),
                $"vkCreateShaderModule ('{label}.{juncture}')");
            return module;
        }
    }

    internal void CmdAttachPipeDefaults(CommandBuffer directives, GpuPipeSpec blurb)
    {
        _vk.CmdSetCullMode(directives, VkViewportMapping.ToVulkan(blurb.Cull));
        _vk.CmdSetFrontFace(directives, VkViewportMapping.ToVulkan(blurb.FrontFace));
        _vk.CmdSetDepthWriteEnable(directives, blurb.Depth.Write);
        if (!blurb.StencilTest)
            return;

        GpuStencilLedger stencil = blurb.Stencil;
        const StencilFaceFlags BothFaces = StencilFaceFlags.FaceFrontAndBack;
        _vk.CmdSetStencilOp(
            directives,
            BothFaces,
            VkViewportMapping.ToVulkan(stencil.Fail),
            VkViewportMapping.ToVulkan(stencil.Pass),
            VkViewportMapping.ToVulkan(stencil.DepthFail),
            VkViewportMapping.ToVulkan(stencil.Compare));
        _vk.CmdSetStencilCompareMask(directives, BothFaces, stencil.CompareMask);
        _vk.CmdSetStencilWriteMask(directives, BothFaces, stencil.WriteMask);
        _vk.CmdSetStencilReference(directives, BothFaces, stencil.Reference);
    }

    internal IGpuSweepCoder BeginPass(VkGpuFrame cycle, GpuPassSpec description)
    {
        HurlIfDestroyed();
        ArgumentNullException.ThrowIfNull(description);
        if (_openPass is not null)
            throw new InvalidOperationException("A pass is already open; dispose its encoder first.");

        CommandBuffer directives = _directiveBufs[cycle.SocketOrdinal];

        _uploads.Record(directives);

        _diagLabels.CommenceCaption(directives, description.Name);

        uint width;
        uint height;
        ImageView tintLens = default;
        ImageView locateLens = default;
        ImageView zDepthLens = default;
        ImageView zDepthLocateLens = default;
        bool hasTintAffix = description.HasTintAffix;
        bool backbuffer = hasTintAffix && description.Color.Target is null;
        uint lensBitmask = description.LensBitmask;
        GpuBitmapFmt passTintFmt = GpuBitmapFmt.Rgba8UnormRenderTarget;

        if (!hasTintAffix)
        {
            if (description.SampleCount != 1)
                throw new InvalidOperationException("Directional depth passes are single-sampled.");
            if (description.ZDepth is not { DirectionalTarget: VkDirectionalDepthTarget mark } zDepth)
            {
                throw new ArgumentException(
                    "A colour-less pass requires a Vulkan directional-depth target.",
                    nameof(description));
            }
            if (zDepth.Store != GpuVaultOp.Store)
                throw new InvalidOperationException("Directional depth must be stored for later sampling.");
            if (zDepth.Layer < 0 || zDepth.Layer >= mark.Description.LayerCount)
                throw new ArgumentOutOfRangeException(nameof(description), "Directional depth layer is outside the target.");

            width = (uint)mark.Description.Resolution;
            height = (uint)mark.Description.Resolution;
            if (lensBitmask != 0)
            {
                if (!Capabilities.SupportsMultiview)
                    throw new NotSupportedException("The selected Vulkan device does not support multiview.");
                zDepthLens = mark.MultiviewLens(lensBitmask);
                ChangeoverDirectedZDepthForRendering(
                    directives,
                    mark,
                    baseStratum: 0,
                    stratumTally: mark.StratumTallyForLensBitmask(lensBitmask));
            }
            else
            {
                zDepthLens = mark.LensAt(zDepth.Layer);
                ChangeoverDirectedZDepthForRendering(directives, mark, zDepth.Layer, 1);
            }
        }
        else if (backbuffer)
        {
            if (_backbuffer is null || _acquiredImageOrdinal is not { } imageOrdinal)
            {
                throw new InvalidOperationException(
                    "A pass declared Target: null, which the Vulkan backend honours literally as the " +
                    "swapchain image, but this device has no backbuffer or none was acquired for this frame.");
            }

            VkBackbufferAttachments affixes = BackbufferAffixes;
            width = _backbuffer.Width;
            height = _backbuffer.Height;
            ChangeoverBackbufferForRendering(directives, _backbuffer.ImageAt(imageOrdinal));

            if (affixes.HasMultisampledTint && description.SampleCount > 1)
            {
                tintLens = affixes.TintLens;
                locateLens = _backbuffer.LensAt(imageOrdinal);
                ChangeoverBackbufferTempTint(directives, affixes);
            }
            else
            {
                tintLens = _backbuffer.LensAt(imageOrdinal);
            }

            if (description.ZDepth is not null && affixes.HasZDepth)
            {
                zDepthLens = affixes.ZDepthLens;
                ChangeoverBackbufferZDepth(directives, affixes);
            }
        }
        else
        {
            if (description.Color.Target is not VkGpuRenderTarget mark)
                throw new ArgumentException("The Vulkan backend can only render into a Vulkan render target.");
            if (mark.Description.SampleCount != description.SampleCount)
            {
                throw new InvalidOperationException(
                    $"Pass '{description.Name}' declares {description.SampleCount} samples but target "
                    + $"'{mark.Description.Name}' was created for {mark.Description.SampleCount}.");
            }
            width = (uint)mark.Description.Width;
            height = (uint)mark.Description.Height;
            passTintFmt = mark.Description.ColorFormat;
            tintLens = mark.TintAffix.View;
            if (mark.TintLocate is { } tintLocate)
            {
                if (description.Color.Load == GpuPullOp.Load)
                {
                    throw new InvalidOperationException(
                        $"Multisampled target '{mark.Description.Name}' cannot Load a prior resolved image; "
                        + "its transient multisample attachment has no preserved contents.");
                }
                if (description.Color.Store != GpuVaultOp.Resolve)
                {
                    throw new InvalidOperationException(
                        $"Multisampled target '{mark.Description.Name}' must use Store=Resolve so its "
                        + "single-sampled ColorTexture receives this pass.");
                }
                locateLens = tintLocate.View;
            }
            else if (description.Color.Store == GpuVaultOp.Resolve)
            {
                throw new InvalidOperationException(
                    $"Single-sampled target '{mark.Description.Name}' cannot use Store=Resolve.");
            }
            ChangeoverRasterizeMarkForRendering(directives, mark);
            if (description.ZDepth is not null && mark.ZDepthAffix is { } zDepth)
            {
                zDepthLens = zDepth.View;
                if (mark.Description.SampleCount > 1
                    && description.ZDepth.Value.Load == GpuPullOp.Load)
                {
                    throw new InvalidOperationException(
                        $"Multisampled depth target '{mark.Description.Name}' cannot Load transient depth.");
                }
                if (mark.Description.SampleableDepth
                    && description.ZDepth.Value.Store != GpuVaultOp.Store)
                {
                    throw new InvalidOperationException(
                        $"Sampleable depth on '{mark.Description.Name}' requires Store=Store.");
                }
                if (mark.DepthResolve is { } zDepthLocate)
                {
                    zDepthLocateLens = zDepthLocate.View;
                }
            }
        }

        RenderingAttachmentInfo tintAffix = default;
        if (hasTintAffix)
        {
            Vector4 wipe = description.Color.ClearColor;
            tintAffix = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = tintLens,
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = VkViewportMapping.ToVulkan(description.Color.Load),
                StoreOp = description.Color.Store == GpuVaultOp.Resolve
                    ? AttachmentStoreOp.DontCare
                    : VkViewportMapping.ToVulkan(description.Color.Store),
                ClearValue = new ClearValue
                {
                    Color = new ClearColorValue
                    {
                        Float32_0 = wipe.X,
                        Float32_1 = wipe.Y,
                        Float32_2 = wipe.Z,
                        Float32_3 = wipe.W,
                    },
                },
            };
            if (locateLens.Handle != 0)
            {
                tintAffix.ResolveMode = ResolveModeFlags.AverageBit;
                tintAffix.ResolveImageView = locateLens;
                tintAffix.ResolveImageLayout = ImageLayout.ColorAttachmentOptimal;
            }
        }

        RenderingAttachmentInfo zDepthAffix = default;
        RenderingAttachmentInfo stencilAffix = default;
        if (description.ZDepth is { } zDepthBlurb && zDepthLens.Handle != 0)
        {
            bool locateZDepth = zDepthLocateLens.Handle != 0;
            zDepthAffix = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = zDepthLens,
                ImageLayout = ImageLayout.DepthStencilAttachmentOptimal,
                LoadOp = VkViewportMapping.ToVulkan(zDepthBlurb.Load),
                StoreOp = locateZDepth
                    ? AttachmentStoreOp.DontCare
                    : VkViewportMapping.ToVulkan(zDepthBlurb.Store),
                ClearValue = new ClearValue
                {
                    DepthStencil = new ClearDepthStencilValue(
                        zDepthBlurb.ClearDepth,
                        zDepthBlurb.ClearStencil),
                },
            };
            stencilAffix = zDepthAffix;
            if (locateZDepth)
            {
                zDepthAffix.ResolveMode = ResolveModeFlags.SampleZeroBit;
                zDepthAffix.ResolveImageView = zDepthLocateLens;
                zDepthAffix.ResolveImageLayout = ImageLayout.DepthStencilAttachmentOptimal;
                stencilAffix.ResolveMode = ResolveModeFlags.SampleZeroBit;
                stencilAffix.ResolveImageView = zDepthLocateLens;
                stencilAffix.ResolveImageLayout = ImageLayout.DepthStencilAttachmentOptimal;
            }
        }

        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height)),
            LayerCount = 1,
            ViewMask = lensBitmask,
            ColorAttachmentCount = hasTintAffix ? 1u : 0u,
            PColorAttachments = hasTintAffix ? &tintAffix : null,
            PDepthAttachment = zDepthAffix.SType == StructureType.RenderingAttachmentInfo
                ? &zDepthAffix
                : null,
            PStencilAttachment = stencilAffix.SType == StructureType.RenderingAttachmentInfo
                ? &stencilAffix
                : null,
        };
        _vk.CmdBeginRendering(directives, &rendering);

        _openPassIsBackbuffer = backbuffer;
        var coder = new VkGpuPassEncoder(
            this,
            cycle,
            directives,
            _cycleMappings[cycle.SocketOrdinal],
            description,
            width,
            height,
            hasZDepthAffix: zDepthLens.Handle != 0,
            hasTintAffix,
            tintFmt: passTintFmt);
        _openPass = coder;
        return coder;
    }

    internal void FinishPass(VkGpuPassEncoder coder)
    {
        if (!ReferenceEquals(_openPass, coder))
            return;

        CommandBuffer directives = LatestDirectives;
        _vk.CmdEndRendering(directives);
        _diagLabels.FinishCaption(directives);

        if (!_openPassIsBackbuffer && coder.Pass.Color.Target is VkGpuRenderTarget mark)
        {
            ChangeoverRasterizeMarkForSampling(
                directives,
                mark,
                tintStored: coder.Pass.Color.Store != GpuVaultOp.DontCare,
                zDepthStored: coder.Pass.ZDepth?.Store == GpuVaultOp.Store);
        }
        else if (coder.Pass.ZDepth is
        { DirectionalTarget: VkDirectionalDepthTarget directedMark } zDepth)
        {
            if (coder.Pass.LensBitmask != 0)
            {
                ChangeoverDirectedZDepthForSampling(
                    directives,
                    directedMark,
                    0,
                    directedMark.StratumTallyForLensBitmask(coder.Pass.LensBitmask));
            }
            else
            {
                ChangeoverDirectedZDepthForSampling(directives, directedMark, zDepth.Layer, 1);
            }
        }

        _openPass = null;
    }

    private void ChangeoverBackbufferForRendering(CommandBuffer directives, Image image)
    {
        bool lead = !_backbufferRenderingPrimed;
        _backbufferRenderingPrimed = true;

        ImageMemoryBarrier2 barrier = CreateBackbufferRenderingBarrier(image, lead);
        SubmitImageBarrier(directives, barrier);
    }

    internal static ImageMemoryBarrier2 CreateBackbufferRenderingBarrier(
        Image image,
        bool lead) => new()
        {
            SType = StructureType.ImageMemoryBarrier2,
            // The first image use is ordered by the acquire semaphore, whose wait
            // is scoped to this same stage. TopOfPipe here would run before that
            // wait and race the presentation engine's ownership/layout use.
            SrcStageMask = lead
            ? AcquiredImagePauseJuncture
            : PipelineStageFlags2.ColorAttachmentOutputBit,
            SrcAccessMask = lead
            ? AccessFlags2.None
            : AccessFlags2.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            DstAccessMask = lead
            ? AccessFlags2.ColorAttachmentWriteBit
            : AccessFlags2.ColorAttachmentWriteBit | AccessFlags2.ColorAttachmentReadBit,
            OldLayout = lead ? ImageLayout.Undefined : ImageLayout.ColorAttachmentOptimal,
            NewLayout = ImageLayout.ColorAttachmentOptimal,
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

    private void ChangeoverBackbufferTempTint(
        CommandBuffer directives,
        VkBackbufferAttachments affixes)
    {
        bool lead = !affixes.TintArrangementInitialized;
        affixes.FlagTintArrangementInitialized();
        ChangeoverImage(
            directives,
            affixes.TintImage,
            ImageAspectFlags.ColorBit,
            lead ? ImageLayout.Undefined : ImageLayout.ColorAttachmentOptimal,
            ImageLayout.ColorAttachmentOptimal,
            lead ? PipelineStageFlags2.TopOfPipeBit : PipelineStageFlags2.ColorAttachmentOutputBit,
            lead ? AccessFlags2.None : AccessFlags2.ColorAttachmentWriteBit,
            PipelineStageFlags2.ColorAttachmentOutputBit,
            AccessFlags2.ColorAttachmentWriteBit | AccessFlags2.ColorAttachmentReadBit);
    }

    private void ChangeoverBackbufferZDepth(
        CommandBuffer directives,
        VkBackbufferAttachments affixes)
    {
        bool lead = !affixes.ZDepthArrangementInitialized;
        affixes.FlagZDepthArrangementInitialized();
        const PipelineStageFlags2 ZDepthJunctures =
            PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit;
        ChangeoverImage(
            directives,
            affixes.ZDepthImage,
            ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
            lead ? ImageLayout.Undefined : ImageLayout.DepthStencilAttachmentOptimal,
            ImageLayout.DepthStencilAttachmentOptimal,
            lead ? PipelineStageFlags2.TopOfPipeBit : ZDepthJunctures,
            lead ? AccessFlags2.None : AccessFlags2.DepthStencilAttachmentWriteBit,
            ZDepthJunctures,
            AccessFlags2.DepthStencilAttachmentWriteBit | AccessFlags2.DepthStencilAttachmentReadBit);
    }

    private void ChangeoverRasterizeMarkForRendering(CommandBuffer directives, VkGpuRenderTarget mark)
    {
        VkGpuTexture tintAffix = mark.TintAffix;
        ChangeoverImage(
            directives,
            tintAffix.Image,
            ImageAspectFlags.ColorBit,
            tintAffix.CurrentLayout,
            ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlags2.AllCommandsBit,
            AccessFlags2.None,
            PipelineStageFlags2.ColorAttachmentOutputBit,
            AccessFlags2.ColorAttachmentWriteBit);
        tintAffix.FlagArrangement(ImageLayout.ColorAttachmentOptimal);

        if (mark.TintLocate is { } tintLocate)
        {
            ChangeoverImage(
                directives,
                tintLocate.Image,
                ImageAspectFlags.ColorBit,
                tintLocate.CurrentLayout,
                ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlags2.AllCommandsBit,
                AccessFlags2.None,
                PipelineStageFlags2.ColorAttachmentOutputBit,
                AccessFlags2.ColorAttachmentWriteBit);
            tintLocate.FlagArrangement(ImageLayout.ColorAttachmentOptimal);
        }

        if (mark.ZDepthAffix is { } zDepth)
        {
            SubmitImageBarrier(directives, BuildRasterizeMarkZDepthListingBarrier(
                zDepth.Image,
                zDepth.CurrentLayout,
                fixedFunctionLocate: false));
            zDepth.FlagArrangement(ImageLayout.DepthStencilAttachmentOptimal);
        }

        if (mark.DepthResolve is { } zDepthLocate)
        {
            SubmitImageBarrier(directives, BuildRasterizeMarkZDepthListingBarrier(
                zDepthLocate.Image,
                zDepthLocate.CurrentLayout,
                fixedFunctionLocate: true));
            zDepthLocate.FlagArrangement(ImageLayout.DepthStencilAttachmentOptimal);
        }
    }

    internal void BroadcastHubDepotWrites(
        VkGpuFrame cycle,
        IClientGpuBuffer buffer)
    {
        HurlIfDestroyed();
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(buffer);
        if (!ReferenceEquals(_openCycle, cycle))
            throw new InvalidOperationException("Host writes require the open Vulkan frame.");
        if (_openPass is not null)
        {
            throw new InvalidOperationException(
                "Retained host writes must be published before opening a rendering pass.");
        }
        if (buffer is not VkGpuBuffer vkBuf
            || buffer.Residency != GpuMemoryTenancy.HostWritable
            || !buffer.Usage.HasFlag(GpuBufferPurpose.Storage))
        {
            throw new ArgumentException(
                "Published host writes require a Vulkan host-writable storage buffer.",
                nameof(buffer));
        }

        CommandBuffer directives = _directiveBufs[cycle.SocketOrdinal];
        BufferMemoryBarrier2 barrier = VkHostStorageVisibility.Create(
            vkBuf.Handle,
            checked((ulong)vkBuf.SizeBytes));
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            BufferMemoryBarrierCount = 1,
            PBufferMemoryBarriers = &barrier,
        };
        _vk.CmdPipelineBarrier2(directives, &dep);
    }

    private void ChangeoverRasterizeMarkForSampling(
        CommandBuffer directives,
        VkGpuRenderTarget mark,
        bool tintStored,
        bool zDepthStored)
    {
        if (tintStored)
        {
            VkGpuTexture tint = mark.TintOutcome;
            ChangeoverImage(
                directives,
                tint.Image,
                ImageAspectFlags.ColorBit,
                tint.CurrentLayout,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.ColorAttachmentOutputBit,
                AccessFlags2.ColorAttachmentWriteBit,
                PipelineStageFlags2.FragmentShaderBit,
                AccessFlags2.ShaderReadBit);
            tint.FlagArrangement(ImageLayout.ShaderReadOnlyOptimal);
        }

        if (zDepthStored && mark.Description.SampleableDepth && mark.ZDepthOutcome is { } zDepth)
        {
            SubmitImageBarrier(directives, BuildRasterizeMarkZDepthSamplingBarrier(
                zDepth.Image,
                zDepth.CurrentLayout,
                fixedFunctionLocate: mark.DepthResolve is not null));
            zDepth.FlagArrangement(ImageLayout.DepthStencilReadOnlyOptimal);
        }
    }

    internal static ImageMemoryBarrier2 BuildRasterizeMarkZDepthListingBarrier(
        Image image,
        ImageLayout formerArrangement,
        bool fixedFunctionLocate)
    {
        PipelineStageFlags2 writerJuncture = fixedFunctionLocate
            ? PipelineStageFlags2.ColorAttachmentOutputBit
            : PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit;
        AccessFlags2 writerAccess = fixedFunctionLocate
            ? AccessFlags2.ColorAttachmentWriteBit
            : AccessFlags2.DepthStencilAttachmentWriteBit;
        return BuildImageBarrier(
            image,
            ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
            formerArrangement,
            ImageLayout.DepthStencilAttachmentOptimal,
            PipelineStageFlags2.AllCommandsBit,
            AccessFlags2.None,
            writerJuncture,
            writerAccess);
    }

    internal static ImageMemoryBarrier2 BuildRasterizeMarkZDepthSamplingBarrier(
        Image image,
        ImageLayout formerArrangement,
        bool fixedFunctionLocate)
    {
        PipelineStageFlags2 writerJuncture = fixedFunctionLocate
            ? PipelineStageFlags2.ColorAttachmentOutputBit
            : PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit;
        AccessFlags2 writerAccess = fixedFunctionLocate
            ? AccessFlags2.ColorAttachmentWriteBit
            : AccessFlags2.DepthStencilAttachmentWriteBit;
        return BuildImageBarrier(
            image,
            ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
            formerArrangement,
            ImageLayout.DepthStencilReadOnlyOptimal,
            writerJuncture,
            writerAccess,
            PipelineStageFlags2.FragmentShaderBit,
            AccessFlags2.ShaderReadBit);
    }

    private void ChangeoverDirectedZDepthForRendering(
        CommandBuffer directives,
        VkDirectionalDepthTarget mark,
        int baseStratum,
        int stratumTally)
    {
        const PipelineStageFlags2 ZDepthJunctures =
            PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit;
        ImageLayout formerArrangement = mark.ArrangementAt(baseStratum);
        for (int idx = 1; idx < stratumTally; idx++)
        {
            if (mark.ArrangementAt(baseStratum + idx) != formerArrangement)
                throw new InvalidOperationException("Multiview directional layers must share one layout.");
        }
        ChangeoverImage(
            directives,
            mark.Texture.Image,
            ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
            formerArrangement,
            ImageLayout.DepthStencilAttachmentOptimal,
            formerArrangement == ImageLayout.Undefined ? PipelineStageFlags2.TopOfPipeBit : PipelineStageFlags2.FragmentShaderBit,
            formerArrangement == ImageLayout.Undefined ? AccessFlags2.None : AccessFlags2.ShaderReadBit,
            ZDepthJunctures,
            AccessFlags2.DepthStencilAttachmentWriteBit,
            baseArrStratum: (uint)baseStratum,
            stratumTally: (uint)stratumTally);
        for (int idx = 0; idx < stratumTally; idx++)
            mark.FlagArrangement(baseStratum + idx, ImageLayout.DepthStencilAttachmentOptimal);
    }

    private void ChangeoverDirectedZDepthForSampling(
        CommandBuffer directives,
        VkDirectionalDepthTarget mark,
        int baseStratum,
        int stratumTally)
    {
        ChangeoverImage(
            directives,
            mark.Texture.Image,
            ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
            mark.ArrangementAt(baseStratum),
            ImageLayout.DepthStencilReadOnlyOptimal,
            PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
            AccessFlags2.DepthStencilAttachmentWriteBit,
            PipelineStageFlags2.FragmentShaderBit,
            AccessFlags2.ShaderReadBit,
            baseArrStratum: (uint)baseStratum,
            stratumTally: (uint)stratumTally);
        for (int idx = 0; idx < stratumTally; idx++)
            mark.FlagArrangement(baseStratum + idx, ImageLayout.DepthStencilReadOnlyOptimal);
    }

    private void ChangeoverImage(
        CommandBuffer directives,
        Image image,
        ImageAspectFlags aspect,
        ImageLayout formerArrangement,
        ImageLayout newArrangement,
        PipelineStageFlags2 srcJuncture,
        AccessFlags2 srcAccess,
        PipelineStageFlags2 destJuncture,
        AccessFlags2 destAccess,
        uint baseArrStratum = 0,
        uint stratumTally = Silk.NET.Vulkan.Vk.RemainingArrayLayers)
    {
        SubmitImageBarrier(directives, BuildImageBarrier(
            image,
            aspect,
            formerArrangement,
            newArrangement,
            srcJuncture,
            srcAccess,
            destJuncture,
            destAccess,
            baseArrStratum,
            stratumTally));
    }

    private static ImageMemoryBarrier2 BuildImageBarrier(
        Image image,
        ImageAspectFlags aspect,
        ImageLayout formerArrangement,
        ImageLayout newArrangement,
        PipelineStageFlags2 srcJuncture,
        AccessFlags2 srcAccess,
        PipelineStageFlags2 destJuncture,
        AccessFlags2 destAccess,
        uint baseArrStratum = 0,
        uint stratumTally = Silk.NET.Vulkan.Vk.RemainingArrayLayers) => new()
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
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspect,
                BaseMipLevel = 0,
                LevelCount = Silk.NET.Vulkan.Vk.RemainingMipLevels,
                BaseArrayLayer = baseArrStratum,
                LayerCount = stratumTally,
            },
        };

    private void SubmitImageBarrier(CommandBuffer directives, ImageMemoryBarrier2 barrier)
    {
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier,
        };
        _vk.CmdPipelineBarrier2(directives, &dep);
    }

    public byte[] CaptureBackbuffer(int width, int height)
    {
        HurlIfDestroyed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (_backbuffer is null)
            throw new InvalidOperationException("This device has no backbuffer to capture.");
        if (_captureBuffer is null)
        {
            throw new InvalidOperationException(
                "Backbuffer capture was not retained by this device. Construct it with " +
                "retainBackbufferCapture: true — reading the presented swapchain image " +
                "instead is a Vulkan usage error (see this method's remarks).");
        }
        if (width != _captureWidth || height != _captureHeight)
        {
            throw new ArgumentException(
                $"The retained capture is {_captureWidth}x{_captureHeight}; {width}x{height} was requested. " +
                "The capture buffer is sized with the swapchain, so a mismatch means the caller " +
                "and the backbuffer disagree about the frame that was just presented.");
        }
        if (!_captureValidity.HasSubmittedCopy)
        {
            throw new InvalidOperationException(
                "No retained capture copy has completed submission for the current backbuffer size.");
        }
        if (!_captureBuffer.HostWritesAreCoherent)
        {
            throw new NotSupportedException(
                "Retained backbuffer capture requires host-coherent memory; " +
                "capture-local invalidation for non-coherent memory is not implemented.");
        }

        VkInterop.Check(_vk.DeviceWaitIdle(_device), "vkDeviceWaitIdle (capture)");
        var px = new byte[(long)_captureWidth * _captureHeight * 4];
        _captureBuffer.Read(0, px);
        return VkBackbufferSwizzle.ToRgba(px, width, height, width * 4);
    }

    internal ImageLayout RecordBackbufferCapture(CommandBuffer directives, Image image)
    {
        if (_captureBuffer is null || _backbuffer is null)
            return ImageLayout.ColorAttachmentOptimal;
        if (_captureWidth != _backbuffer.Width || _captureHeight != _backbuffer.Height)
            return ImageLayout.ColorAttachmentOptimal;

        ChangeoverImage(
            directives,
            image,
            ImageAspectFlags.ColorBit,
            ImageLayout.ColorAttachmentOptimal,
            ImageLayout.TransferSrcOptimal,
            PipelineStageFlags2.ColorAttachmentOutputBit,
            AccessFlags2.ColorAttachmentWriteBit,
            PipelineStageFlags2.CopyBit,
            AccessFlags2.TransferReadBit);

        var zone = new BufferImageCopy
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
            ImageExtent = new Extent3D(_captureWidth, _captureHeight, 1),
        };
        _vk.CmdCopyImageToBuffer(
            directives,
            image,
            ImageLayout.TransferSrcOptimal,
            _captureBuffer.Handle,
            1,
            &zone);
        ulong copiedByteTally = checked((ulong)_captureWidth * _captureHeight * 4);
        var hubScanBarrier = new BufferMemoryBarrier2
        {
            SType = StructureType.BufferMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.CopyBit,
            SrcAccessMask = AccessFlags2.TransferWriteBit,
            DstStageMask = PipelineStageFlags2.HostBit,
            DstAccessMask = AccessFlags2.HostReadBit,
            SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            Buffer = _captureBuffer.Handle,
            Offset = 0,
            Size = copiedByteTally,
        };
        var hubScanDep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            BufferMemoryBarrierCount = 1,
            PBufferMemoryBarriers = &hubScanBarrier,
        };
        _vk.CmdPipelineBarrier2(directives, &hubScanDep);
        _captureValidity.RecordCopy();
        return ImageLayout.TransferSrcOptimal;
    }

    private void ConfigureBackbufferCapture(uint width, uint height)
    {
        if (!_retainBackbufferCapture)
            return;
        if (_captureBuffer is not null && _captureWidth == width && _captureHeight == height)
            return;

        _captureValidity.Invalidate();
        _captureBuffer?.Dispose();
        _captureBuffer = null;
        _captureWidth = width;
        _captureHeight = height;
        if (width == 0 || height == 0)
            return;

        _captureBuffer = new VkGpuBuffer(
            _vk,
            Handle,
            _allocator,
            _uploads,
            ImmediateGpuAssetSunsetFifo.Instance,
            _diagLabels,
            new GpuBufferSpec(
                "vk-backbuffer-capture",
                width * height * 4,
                GpuBufferPurpose.TransferDestination,
                GpuMemoryTenancy.HostReadable));
    }

    private readonly bool _retainBackbufferCapture;
    private VkGpuBuffer? _captureBuffer;
    private uint _captureWidth;
    private uint _captureHeight;
    private VkBackbufferCaptureValidity _captureValidity;
    private bool _backbufferRenderingPrimed;
}

internal struct VkBackbufferCaptureValidity
{
    internal bool DuplicateRecorded { get; private set; }

    internal bool HasSubmittedCopy { get; private set; }

    internal void BeginFrame()
    {
        DuplicateRecorded = false;
        HasSubmittedCopy = false;
    }

    internal void RecordCopy() => DuplicateRecorded = true;

    internal void CompleteSubmission()
    {
        HasSubmittedCopy = DuplicateRecorded;
        DuplicateRecorded = false;
    }

    internal void Invalidate()
    {
        DuplicateRecorded = false;
        HasSubmittedCopy = false;
    }
}
