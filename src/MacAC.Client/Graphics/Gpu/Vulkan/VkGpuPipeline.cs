using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkGpuPipeline : IGpuPipe
{
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly IGpuAssetSunsetFifo _sunset;
    private readonly VkDebugNames _diagLabels;
    private readonly VulkanPipeArrangements.CreatedDef _arrangements;
    private readonly VulkanPipeArrangements.CreatedDef.PackArrangementLease? _bundleArrangementTenancy;
    private readonly PipelineCache _stash;
    private readonly ShaderModule _vertModule;
    private readonly ShaderModule _fragmentModule;
    private readonly bool _ownsShaderModules;
    private readonly Format _zDepthStencilFmt;
    private readonly Pipeline _withZDepthAffix;
    private readonly Pipeline _withoutZDepthAffix;
    private readonly Dictionary<GpuBitmapFmt, VkGpuPipeline> _tintVariants = [];

    internal VkGpuPipeline(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        IGpuAssetSunsetFifo retirement,
        VkDebugNames debugNames,
        VulkanPipeArrangements.CreatedDef layouts,
        VulkanPipeArrangements.CreatedDef.PackArrangementLease? bundleArrangementTenancy,
        PipelineLayout arrangement,
        PipelineCache stash,
        ShaderModule vertModule,
        ShaderModule fragmentModule,
        bool ownsShaderModules,
        GpuPipeSpec description,
        Format tintFmt,
        Format zDepthStencilFmt)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _sunset = retirement ?? throw new ArgumentNullException(nameof(retirement));
        _diagLabels = debugNames ?? throw new ArgumentNullException(nameof(debugNames));
        _arrangements = layouts ?? throw new ArgumentNullException(nameof(layouts));
        _bundleArrangementTenancy = bundleArrangementTenancy;
        _arrangement = arrangement;
        _stash = stash;
        _vertModule = vertModule;
        _fragmentModule = fragmentModule;
        _ownsShaderModules = ownsShaderModules;
        _zDepthStencilFmt = zDepthStencilFmt;
        Description = description ?? throw new ArgumentNullException(nameof(description));

        nint listingPt = SilkMarshal.StringToPtr("main");
        try
        {
            PipelineShaderStageCreateInfo* junctures = stackalloc PipelineShaderStageCreateInfo[2];
            junctures[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertModule,
                PName = (byte*)listingPt,
            };
            junctures[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragmentModule,
                PName = (byte*)listingPt,
            };

            var vertArrangement = description.VertArrangement;
            int mappingTally = vertArrangement.Bindings.Length;
            VertexInputBindingDescription* mappings =
                stackalloc VertexInputBindingDescription[Math.Max(1, mappingTally)];
            for (int idx = 0; idx < mappingTally; ++idx)
            {
                var declared = vertArrangement.Bindings[idx];
                mappings[idx] = new VertexInputBindingDescription
                {
                    Binding = declared.Binding,
                    Stride = declared.StrideBytes,
                    InputRate = declared.InputRate == GpuVertFeedRate.Instance
                        ? VertexInputRate.Instance
                        : VertexInputRate.Vertex,
                };
            }

            int attrTally = vertArrangement.Attributes.Length;
            VertexInputAttributeDescription* attrs =
                stackalloc VertexInputAttributeDescription[Math.Max(1, attrTally)];
            for (int idx = 0; idx < attrTally; ++idx)
            {
                var attr = vertArrangement.Attributes[idx];
                attrs[idx] = new VertexInputAttributeDescription
                {
                    Location = attr.Location,
                    Binding = attr.Binding,
                    Format = VkViewportMapping.ToVulkan(attr.Format),
                    Offset = attr.OffsetBytes,
                };
            }

            var vertFeed = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = (uint)mappingTally,
                PVertexBindingDescriptions = mappingTally is 0 ? null : mappings,
                VertexAttributeDescriptionCount = (uint)attrTally,
                PVertexAttributeDescriptions = attrTally is 0 ? null : attrs,
            };
            var assembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = VkViewportMapping.ToVulkan(description.Wiring),
                PrimitiveRestartEnable = false,
            };
            var viewRect = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
            };
            var rasterization = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1f,
                CullMode = VkViewportMapping.ToVulkan(description.Cull),
                FrontFace = VkViewportMapping.ToVulkan(description.FrontFace),
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                DepthBiasEnable = false,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = VkTextureFormatMapping.ProbeTallyOf(description.SampleCount),
                SampleShadingEnable = false,
                AlphaToCoverageEnable = description.AlphaToCoverage && description.SampleCount > 1,
            };
            var stencil = description.Stencil;
            StencilOpState stencilOps = new StencilOpState
            {
                FailOp = VkViewportMapping.ToVulkan(stencil.Fail),
                PassOp = VkViewportMapping.ToVulkan(stencil.Pass),
                DepthFailOp = VkViewportMapping.ToVulkan(stencil.DepthFail),
                CompareOp = VkViewportMapping.ToVulkan(stencil.Compare),
                CompareMask = stencil.CompareMask,
                WriteMask = stencil.WriteMask,
                Reference = stencil.Reference,
            };
            var zDepthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = description.Depth.Test,
                DepthWriteEnable = description.Depth.Write,
                DepthCompareOp = VkViewportMapping.ToVulkan(description.Depth.Compare),
                DepthBoundsTestEnable = false,
                StencilTestEnable = description.StencilTest,
                Front = stencilOps,
                Back = stencilOps,
            };

            (BlendFactor src, BlendFactor dest) =
                VkViewportMapping.BlendFactorsOf(description.Blend);
            var affix = new PipelineColorBlendAttachmentState
            {
                BlendEnable = description.Blend != GpuBlendManner.None,
                SrcColorBlendFactor = src,
                DstColorBlendFactor = dest,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = src,
                DstAlphaBlendFactor = dest,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = description.TintEmit
                    ? ColorComponentFlags.RBit | ColorComponentFlags.GBit
                        | ColorComponentFlags.BBit | ColorComponentFlags.ABit
                    : 0,
            };
            var blend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = description.HasTintAttachment ? 1u : 0u,
                PAttachments = description.HasTintAttachment ? &affix : null,
            };

            DynamicState* dynamicPhases = stackalloc DynamicState[9];
            dynamicPhases[0] = DynamicState.Viewport;
            dynamicPhases[1] = DynamicState.Scissor;
            dynamicPhases[2] = DynamicState.CullMode;
            dynamicPhases[3] = DynamicState.FrontFace;
            dynamicPhases[4] = DynamicState.DepthWriteEnable;
            uint dynamicPhaseTally = 5;
            if (description.StencilTest)
            {
                dynamicPhases[5] = DynamicState.StencilOp;
                dynamicPhases[6] = DynamicState.StencilCompareMask;
                dynamicPhases[7] = DynamicState.StencilWriteMask;
                dynamicPhases[8] = DynamicState.StencilReference;
                dynamicPhaseTally = 9;
            }
            var dynamic = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = dynamicPhaseTally,
                PDynamicStates = dynamicPhases,
            };

            Format tint = tintFmt;
            var rendering = new PipelineRenderingCreateInfo
            {
                SType = StructureType.PipelineRenderingCreateInfo,
                ViewMask = description.LensMask,
                ColorAttachmentCount = description.HasTintAttachment ? 1u : 0u,
                PColorAttachmentFormats = description.HasTintAttachment ? &tint : null,
                DepthAttachmentFormat = zDepthStencilFmt,
                StencilAttachmentFormat = zDepthStencilFmt,
            };

            var build = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                PNext = &rendering,
                StageCount = 2,
                PStages = junctures,
                PVertexInputState = &vertFeed,
                PInputAssemblyState = &assembly,
                PViewportState = &viewRect,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisample,
                PDepthStencilState = &zDepthStencil,
                PColorBlendState = &blend,
                PDynamicState = &dynamic,
                Layout = arrangement,
                RenderPass = default,
                Subpass = 0,
            };

            VkInterop.Check(
                _vk.CreateGraphicsPipelines(_device, stash, 1, &build, null, out Pipeline withZDepth),
                $"vkCreateGraphicsPipelines ('{description.Name}', depth attachment)");
            _withZDepthAffix = withZDepth;
            debugNames.LabelPipe(withZDepth, description.Name);

            rendering.DepthAttachmentFormat = Format.Undefined;
            rendering.StencilAttachmentFormat = Format.Undefined;
            try
            {
                VkInterop.Check(
                    _vk.CreateGraphicsPipelines(_device, stash, 1, &build, null, out Pipeline withoutZDepth),
                    $"vkCreateGraphicsPipelines ('{description.Name}', no depth attachment)");
                _withoutZDepthAffix = withoutZDepth;
                debugNames.LabelPipe(withoutZDepth, $"{description.Name}-nodepth");
            }
            catch
            {
                _vk.DestroyPipeline(_device, withZDepth, null);
                throw;
            }
        }
        finally
        {
            SilkMarshal.Free(listingPt);
        }
    }

    public GpuPipeSpec Description { get; }

    public void Dispose()
    {
        if (IsDestroyed)
            return;
        IsDestroyed = true;
        foreach (VkGpuPipeline variant in _tintVariants.Values)
            variant.Dispose();
        _tintVariants.Clear();
        Pipeline withZDepth = _withZDepthAffix;
        Pipeline withoutZDepth = _withoutZDepthAffix;
        var vert = _vertModule;
        var fragment = _fragmentModule;
        bool demolishModules = _ownsShaderModules;
        var bundleTenancy = _bundleArrangementTenancy;
        _sunset.Retire(() =>
        {
            _vk.DestroyPipeline(_device, withZDepth, null);
            _vk.DestroyPipeline(_device, withoutZDepth, null);
            if (demolishModules)
            {
                _vk.DestroyShaderModule(_device, fragment, null);
                _vk.DestroyShaderModule(_device, vert, null);
            }
            bundleTenancy?.Dispose();
        });
    }

    internal Pipeline ProcessFor(bool passHasZDepthAffix) =>
        passHasZDepthAffix ? _withZDepthAffix : _withoutZDepthAffix;

    internal bool IsDestroyed { get; private set; }

    private readonly PipelineLayout _arrangement;

    internal PipelineLayout PipeArrangement => _arrangement;
    // Non-null only for a pipeline flagged for render-pack ABI v1
    internal VulkanPipeArrangements.CreatedDef.BundlePhase? BundlePhase => _bundleArrangementTenancy?.State;

    // Selects the prebuilt attachment-format variant required by the live pass
    internal Pipeline ProcessFor(
        bool passHasZDepthAffix,
        GpuBitmapFmt tintFmt)
    {
        if (tintFmt == Description.TintFmt)
            return ProcessFor(passHasZDepthAffix);
        return _tintVariants.TryGetValue(tintFmt, out VkGpuPipeline? variant)
            ? variant.ProcessFor(passHasZDepthAffix)
            : throw new InvalidOperationException(
            $"Pipeline '{Description.Name}' has no prebuilt {tintFmt} attachment variant");
    }

    internal bool AppendTintFmtVariant(GpuBitmapFmt fmt)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        if (!Description.HasTintAttachment
            || !Description.AllowTintFmtVariants
            || fmt == Description.TintFmt
            || _tintVariants.ContainsKey(fmt))
            return false;

        GpuPipeSpec variantBlurb = Description with
        {
            Name = $"{Description.Name}-{fmt.ToString().ToLowerInvariant()}",
            TintFmt = fmt,
            AllowTintFmtVariants = false,
        };
        VulkanPipeArrangements.CreatedDef.PackArrangementLease? bundleTenancy =
            Description.UsesRasterizeBundleShaderAbi ? _arrangements.ObtainBundleArrangement() : null;
        VkGpuPipeline variant;
        try
        {
            variant = new VkGpuPipeline(
                _vk,
                _device,
                _sunset,
                _diagLabels,
                _arrangements,
                bundleTenancy,
                bundleTenancy?.PipeArrangement ?? _arrangements.PipeArrangement,
                _stash,
                _vertModule,
                _fragmentModule,
                ownsShaderModules: false,
                variantBlurb,
                VkTextureFormatMapping.ComposeOf(fmt),
                _zDepthStencilFmt);
        }
        catch
        {
            bundleTenancy?.Dispose();
            throw;
        }
        _tintVariants.Add(fmt, variant);
        return true;
    }

    internal void DropTintFmtVariant(GpuBitmapFmt fmt)
    {
        if (_tintVariants.Remove(fmt, out VkGpuPipeline? variant))
            variant.Dispose();
    }
}

internal sealed unsafe class VkPipelineShelf : IDisposable
{
    private const uint PreambleLenOctets = 32;
    private const uint PreambleVerOne = 1;

    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly string? _trail;
    private bool _destroyed;

    internal VkPipelineShelf(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev,
        string? stashFolder)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;

        vk.GetPhysicalDeviceProperties(physicalDev, out PhysicalDeviceProperties props);
        byte[] pipeStashUuid = new byte[16];
        for (int idx = 0; idx < 16; ++idx)
            pipeStashUuid[idx] = props.PipelineCacheUuid[idx];

        byte[]? starting = null;
        if (!string.IsNullOrWhiteSpace(stashFolder))
        {
            _trail = Path.Combine(stashFolder, "vulkan-pipeline-cache.bin");
            starting = TryScanCompatible(_trail, props.VendorID, props.DeviceID, pipeStashUuid);
        }

        FetchedFromDisk = starting is not null;
        fixed (byte* blob = starting)
        {
            PipelineCacheCreateInfo build = new PipelineCacheCreateInfo
            {
                SType = StructureType.PipelineCacheCreateInfo,
                InitialDataSize = (nuint)(starting?.Length ?? 0),
                PInitialData = starting is null ? null : blob,
            };
            VkInterop.Check(
                _vk.CreatePipelineCache(_device, &build, null, out PipelineCache stash),
                "vkCreatePipelineCache");
            Handle = stash;
        }
    }

    internal PipelineCache Handle { get; }

    internal bool FetchedFromDisk { get; }

    public void Dispose()
    {
        if (_destroyed)
            return;
        Save();
        _destroyed = true;
        if (Handle.Handle is not 0)
            _vk.DestroyPipelineCache(_device, Handle, null);
    }

    internal static byte[]? VetPreamble(
        byte[]? blob,
        uint merchantIdent,
        uint devIdent,
        ReadOnlySpan<byte> pipeStashUuid)
    {
        if (blob is null || blob.Length < PreambleLenOctets)
            return null;

        uint len = BitConverter.ToUInt32(blob, 0);
        uint ver = BitConverter.ToUInt32(blob, 4);
        uint blobMerchant = BitConverter.ToUInt32(blob, 8);
        uint blobDev = BitConverter.ToUInt32(blob, 12);
        if (len != PreambleLenOctets || ver != PreambleVerOne)
            return null;
        if (blobMerchant != merchantIdent || blobDev != devIdent)
            return null;
        return !blob.AsSpan(16, 16).SequenceEqual(pipeStashUuid) ? null : blob;
    }

    internal void Save()
    {
        if (_destroyed || _trail is null)
            return;

        try
        {
            nuint dims = 0;
            if (_vk.GetPipelineCacheData(_device, Handle, ref dims, null) != Result.Success || dims == 0)
                return;

            byte[] blob = new byte[(int)dims];
            fixed (byte* lead = blob)
            {
                if (_vk.GetPipelineCacheData(_device, Handle, ref dims, lead) != Result.Success)
                    return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_trail)!);
            string temporary = _trail + ".tmp";
            File.WriteAllBytes(temporary, blob);
            File.Move(temporary, _trail, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static byte[]? TryScanCompatible(
        string trail,
        uint merchantIdent,
        uint devIdent,
        ReadOnlySpan<byte> pipeStashUuid)
    {
        try
        {
            return !File.Exists(trail) ? null : VetPreamble(File.ReadAllBytes(trail), merchantIdent, devIdent, pipeStashUuid);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
