using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Illumination;

namespace MacAC.Client.Graphics.Batching;

public sealed unsafe partial class RealmPaintRouter
{
    internal RealmTransformFrameSlice CommenceDirectedShadeXformCycle(
        IGpuCycle cycle,
        ReadOnlySpan<Matrix4x4> xforms)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        return _realmXformCycles.Begin(
            cycle,
            xforms,
            LocateDirectedShadeXformMappingDims(xforms.Length));
    }

    internal RealmTransformFrameSlice CommenceDirectedShadeXformCycle(
        IGpuCycle cycle,
        in RealmTransformFrameSlice keptShadeStem)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        return _realmXformCycles.CommenceKept(
            cycle,
            in keptShadeStem);
    }

    internal uint LocateDirectedShadeXformMappingDims(
        int neededStemInsts,
        int plainInstUpperTied = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(neededStemInsts);
        ArgumentOutOfRangeException.ThrowIfNegative(plainInstUpperTied);
        uint ceiling = _device?.Capabilities.UpperDepotBufSpanOctets
            ?? RealmTransformCapRule.VulkanGuaranteedUpperDepotBufSpanOctets;
        uint latestCycleDemand = checked(
            (uint)neededStemInsts + (uint)plainInstUpperTied);
        uint neededCombinedInsts = checked(
            (uint)neededStemInsts + _plainXformDemandHiWater);
        return RealmTransformCapRule.LocateMappingByteSize(
            Math.Max(latestCycleDemand, neededCombinedInsts),
            ceiling);
    }

    internal void AbortDirectedShadeXformCycle(IGpuCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        _realmXformCycles.Cancel(cycle);
    }

    internal bool HasDirectedShadeXformCycle(long cycleSerialNo) =>
        _realmXformCycles.IsEngagedFor(cycleSerialNo);

    private static IDisposable CommenceRhiTicker(
        IGpuSweepCoder coder,
        bool diag,
        string ambitLabel) =>
        diag ? coder.CommenceTickerAmbit(ambitLabel) : NullRhiTickerAmbit.Instance;

    internal uint DirectedShadeXformCycleConsumedInsts =>
        _realmXformCycles.ConsumedInsts;

    private TriMeshPipeGroup PipesFor(IGpuSweepCoder coder)
    {
        return coder.Pass.SampleCount > 1
            ? _backbufferPipes!
            : _offscreenPipes!;
    }

    private static IGpuPipe PipeForBlend(TriMeshPipeGroup pipes, SeeThroughKind blend)
    {
        return blend switch
        {
            SeeThroughKind.Additive => pipes.AlphaAdditive,
            SeeThroughKind.InvAlpha => pipes.AlphaInverse,
            _ => pipes.AlphaBlend,
        };
    }

    private static IGpuPipe PipeForMatl(
        TriMeshPipeGroup pipes,
        CanonSurfaceMaterialState material,
        IGpuPipe solidPipe)
    {
        return material.Blend switch
        {
            CanonSurfaceBlend.Opaque => pipes.Opaque,
            CanonSurfaceBlend.StraightAlpha => material.AlphaTestTurnedOn
                ? pipes.AlphaBlendDepthWrite
                : pipes.AlphaBlend,
            CanonSurfaceBlend.AlphaAdditive => material.AlphaTestTurnedOn
                ? pipes.AlphaAdditiveDepthWrite
                : pipes.AlphaAdditive,
            CanonSurfaceBlend.Additive => material.AlphaTestTurnedOn
                ? pipes.RawAdditiveDepthWrite
                : pipes.RawAdditive,
            CanonSurfaceBlend.InverseAlpha => material.AlphaTestTurnedOn
                ? pipes.AlphaInverseDepthWrite
                : pipes.AlphaInverse,
            CanonSurfaceBlend.InverseAdditive => material.AlphaTestTurnedOn
                ? pipes.InverseAdditiveDepthWrite
                : pipes.InverseAdditive,
            CanonSurfaceBlend.Clip => solidPipe,
            _ => throw new ArgumentOutOfRangeException(nameof(material), material, "Unrecognized SetSurface blend"),
        };
    }

    private static void AttachLoopSection<T>(
        IGpuSweepCoder coder,
        IGpuCycle cycle,
        uint mapping,
        ReadOnlySpan<T> blob)
        where T : unmanaged =>
        AttachSection(coder, mapping, EmitLoopSection(cycle, blob));

    private IGpuCycle DemandRhiCycle()
    {
        return !_dynamicCycleBegun
            ? throw new InvalidOperationException("BeginFrame has to be called prior to drawing world entities")
            : _cycles!.LatestCycle
            ?? throw new InvalidOperationException(
                "RealmPaintRouter needs an open IGpuCycle (see GpuDeviceCycleLifespan)");
    }

    private static bool DeactivateSpecificsTextures() => false;

    private static TriMeshPipeGroup BuildTriMeshPipeSet(
        IClientGpuDevice dev,
        int specimens,
        string baseShaderLabel = "props_lit",
        GpuShaderGroup? baseShaders = null,
        string labelStem = "wb-mesh",
        bool usesRasterizeBundleShaderAbi = false)
    {
        string suffix = specimens > 1 ? string.Empty : "-1x";
        List<IGpuPipe> built = new List<IGpuPipe>(12);
        try
        {
            return new TriMeshPipeGroup(
                specimens,
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-opaque{suffix}", GpuBlendManner.None, true, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-opaque-a2c{suffix}", GpuBlendManner.None, true, true, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-alpha{suffix}", GpuBlendManner.StraightAlpha, false, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-alpha-depth-write{suffix}", GpuBlendManner.StraightAlpha, true, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-additive{suffix}", GpuBlendManner.Additive, false, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-additive-depth-write{suffix}", GpuBlendManner.Additive, true, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-raw-additive{suffix}", GpuBlendManner.RawAdditive, false, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-raw-additive-depth-write{suffix}", GpuBlendManner.RawAdditive, true, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-inverse{suffix}", GpuBlendManner.InverseAlpha, false, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-inverse-depth-write{suffix}", GpuBlendManner.InverseAlpha, true, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-inverse-additive{suffix}", GpuBlendManner.InverseAdditive, false, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)),
                Follow(BuildTriMeshPipe(
                    dev, $"{labelStem}-inverse-additive-depth-write{suffix}", GpuBlendManner.InverseAdditive, true, false, specimens,
                    shaders: baseShaders,
                    shaderLabel: baseShaderLabel,
                    usesRasterizeBundleShaderAbi: usesRasterizeBundleShaderAbi)));
        }
        catch
        {
            for (int idx = built.Count - 1; idx >= 0; --idx)
                built[idx].Dispose();
            throw;
        }

        IGpuPipe Follow(IGpuPipe pipe)
        {
            built.Add(pipe);
            return pipe;
        }
    }

    private static IGpuPipe BuildTriMeshPipe(
        IClientGpuDevice dev,
        string label,
        GpuBlendManner blend,
        bool zDepthEmit,
        bool alphaToCoverage,
        int specimenTally,
        string shaderLabel = "props_lit",
        GpuShaderGroup? shaders = null,
        GpuContrastOp zDepthContrast = MacAC.Client.Graphics.RealmDepthContract.RealmContrast,
        bool usesRasterizeBundleShaderAbi = false)
    {
        return dev.BuildPipe(new GpuPipeSpec
        {
            Name = label,
            Shaders = shaders ?? new GpuShaderGroup(shaderLabel),
            VertArrangement = GpuVertexArrangement.RealmTriMesh,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = blend,
            Depth = new GpuDepthLedger(Test: true, Write: zDepthEmit, zDepthContrast),
            Cull = GpuPruneManner.Back,
            FrontFace = GpuFrontFacet.Clockwise,
            AlphaToCoverage = alphaToCoverage,
            TintEmit = true,
            UsesRasterizeBundleShaderAbi = usesRasterizeBundleShaderAbi,
            SampleCount = specimenTally,
        });
    }

    // Writes the prepared deferred-alpha payload into the frame ring once
    private void ReadyRhiAlphaSections(int tally)
    {
        _readiedAlphaInstTally = tally;
        IGpuCycle cycle = DemandRhiCycle();
        _alphaInsts = EmitRealmXformSection(
            cycle,
            _stage.Models(tally),
            out uint xformBaseInst);
        _alphaXformBaseInst = xformBaseInst;
        _alphaLots = EmitLoopSection<BatchData>(cycle, _lotBlob.AsSpan(0, tally));
        _alphaClipSockets = EmitLoopSection<uint>(cycle, _stage.ClipSockets(tally));
        int lampTally = SceneLightPacker.Pack(_ptCapture, ref _globalLampBlob);
        int pushTally = lampTally > 0 ? lampTally : 1;
        _alphaGlobalLamps = EmitLoopSection<float>(
            cycle,
            _globalLampBlob.AsSpan(0, pushTally * SceneLightPacker.FloatsPerLamp));
        _alphaLampSets = EmitLoopSection<int>(
            cycle,
            _stage.Lamps(tally));
        _alphaInside = EmitLoopSection<uint>(cycle, _stage.Inside(tally));
        _alphaDensity = EmitLoopSection<float>(cycle, _stage.Opacities(tally));
        _alphaPickIllumination = EmitLoopSection<Vector2>(
            cycle,
            _stage.PickIllumination(tally));
        _alphaSpecificsBucket = EmitLoopSection<uint>(
            cycle,
            _stage.SpecificsBuckets(tally));
        var directives = EmitIndirectDirectives(
            cycle,
            _indirectDirectives.AsSpan(0, tally),
            xformBaseInst);
        _alphaDirectives = new RhiSegment(
            directives.Buffer,
            directives.ShiftOctets,
            checked((uint)(tally * PaintDirectiveStride)));
    }

    private GpuShoveConstants AttachAlphaPaintPhase(
        IGpuSweepCoder coder,
        IGpuCycle cycle,
        GlobalTriMeshBuffer triMesh,
        Matrix4x4 lensProj,
        out TriMeshPipeGroup pipes)
    {
        var pushConstants = new GpuShoveConstants
        {
            LensMirror = lensProj,
            PaintIdentShift = 0,
            IlluminationManner = 0,
            RasterizePass = 1,
            LampDiag = DrawTelemetry.LampDiagManner,
            TextureIndexA = 0,
            TextureOrdinalB = _alphaXformBaseInst,
            ParamA = 0f,
            ParameterB = 0f,
        };

        pipes = PipesFor(
            coder,
            cycle,
            out DirectionalShadeFrameWiring shadeMapping);
        AttachPipeWithTriMesh(coder, pipes.AlphaBlend, triMesh);
        coder.AssignPushConstants(in pushConstants);
        AttachDirectedShadeRecipient(coder, in shadeMapping);
        AttachSection(coder, GpuBindingModel.DepotInsts, _alphaInsts);
        AttachSection(coder, GpuBindingModel.DepotLots, _alphaLots);
        AttachSection(coder, GpuBindingModel.DepotClipSockets, _alphaClipSockets);
        AttachSection(coder, GpuBindingModel.DepotGlobalLamps, _alphaGlobalLamps);
        AttachSection(coder, GpuBindingModel.DepotInstLampSets, _alphaLampSets);
        AttachSection(coder, GpuBindingModel.DepotInstInside, _alphaInside);
        AttachSection(coder, GpuBindingModel.StorageInstanceAlpha, _alphaDensity);
        AttachSection(
            coder,
            GpuBindingModel.DepotInstPickIllumination,
            _alphaPickIllumination);
        AttachSection(
            coder,
            GpuBindingModel.DepotInstSpecificsBucket,
            _alphaSpecificsBucket);
        MacAC.Client.Graphics.RealmFrameSectionWiring.AttachClipZones(
            coder, _ambit!.Sections, cycle);
        MacAC.Client.Graphics.RealmFrameSectionWiring.AttachTableauIllumination(
            coder, _ambit!.Sections, cycle);
        return pushConstants;
    }

    // Binds a pipeline and immediately re-establishes the mesh source
    private static void AttachPipeWithTriMesh(
        IGpuSweepCoder coder,
        IGpuPipe pipe,
        GlobalTriMeshBuffer triMesh)
    {
        coder.BindPipeline(pipe);
        coder.AttachVertBuf(
            0,
            triMesh.VertVault ?? throw new InvalidOperationException(
                "The shared mesh arena has no vertex store"),
            0);
        coder.AttachOrdinalBuf(
            triMesh.OrdinalVault ?? throw new InvalidOperationException(
                "The shared mesh arena has no index store"),
            0,
            GpuOrdinalKind.UInt16);
    }

    private void AttachGlobalLampsRhi(IGpuSweepCoder coder, IGpuCycle cycle)
    {
        int lampTally = SceneLightPacker.Pack(_ptCapture, ref _globalLampBlob);
        int pushTally = lampTally > 0 ? lampTally : 1;
        AttachLoopSection<float>(
            coder,
            cycle,
            GpuBindingModel.DepotGlobalLamps,
            _globalLampBlob.AsSpan(0, pushTally * SceneLightPacker.FloatsPerLamp));
    }

    private static void AttachSection(
        IGpuSweepCoder coder,
        uint mapping,
        in RhiSegment section)
    {
        if (section.Buffer is null)
            return;
        coder.AttachDepotBuf(
            mapping,
            section.Buffer,
            section.OffsetBytes,
            section.SizeBytes);
    }

    private void ArmStructureSpecifics(
        ref GpuShoveConstants pushConstants,
        CanonSurfaceMaterialState matlPhase)
    {
        pushConstants.TextureIndexA = _structureSpecifics.TextureSlot.Index;
        pushConstants.ParamA = _structureSpecifics.Tiling;
        pushConstants.ParameterB = matlPhase.AlphaTestReference;
        if (matlPhase.FogEnabled)
            pushConstants.RasterizePass &= ~CanonDetailTextureContract.NoFogRasterizePassBit;
        else
            pushConstants.RasterizePass |= CanonDetailTextureContract.NoFogRasterizePassBit;
    }

    private static void WipeSpecificsPushConstants(
        ref GpuShoveConstants pushConstants)
    {
        pushConstants.TextureIndexA = 0;
        pushConstants.ParamA = 0f;
        pushConstants.ParameterB = 0f;
        pushConstants.RasterizePass &= ~CanonDetailTextureContract.NoFogRasterizePassBit;
    }

    private static void ImposePruneMannerRhi(IGpuSweepCoder coder, FaceCulling manner)
    {
        coder.AssignFrontFace(GpuFrontFacet.Clockwise);
        switch (manner)
        {
            case FaceCulling.None:
                coder.AssignPruneManner(GpuPruneManner.None);
                break;
            case FaceCulling.Clockwise:
                coder.AssignPruneManner(GpuPruneManner.Front);
                break;
            case FaceCulling.CounterClockwise:
            case FaceCulling.Landblock:
                coder.AssignPruneManner(GpuPruneManner.Back);
                break;
        }
    }

    private RhiSegment EmitRealmXformSection(
        IGpuCycle cycle,
        ReadOnlySpan<float> matrixFloats,
        out uint leadInst)
    {
        RestartRealmXformCycleIfStale(cycle.SerialNo);
        if ((matrixFloats.Length & 15) is not 0)
        {
            throw new ArgumentException(
                "World transforms must contain complete 16-float matrices",
                nameof(matrixFloats));
        }
        WatchPlainXformDemand(
            cycle.SerialNo,
            checked((uint)(matrixFloats.Length / 16)));
        bool privatePass = UpcomingClassicPaintIsPrivatePass;
        UpcomingClassicPaintIsPrivatePass = false;
        if (privatePass || !_realmXformCycles.IsEngaged)
        {
            leadInst = 0;
            return EmitLoopSection(cycle, matrixFloats);
        }

        var appended = _realmXformCycles.Affix(
            cycle,
            MemoryMarshal.Cast<float, Matrix4x4>(matrixFloats));
        leadInst = appended.FirstInstance;
        return new RhiSegment(
            appended.Buffer,
            appended.BaseOffsetBytes,
            appended.BindingSizeBytes);
    }

    private static GpuLoopAlloc EmitIndirectDirectives(
        IGpuCycle cycle,
        Span<DrawElementsIndirectDirective> directives,
        uint baseInst)
    {
        int byteTally = checked(directives.Length * PaintDirectiveStride);
        var alloc = cycle.ReserveLoop(
            byteTally,
            GpuLoopPurpose.Indirect);
        if (baseInst is 0)
        {
            MemoryMarshal.AsBytes(directives).CopyTo(alloc.Data);
            return alloc;
        }

        int adjusted = 0;
        try
        {
            for (int idx = 0; idx < directives.Length; ++idx)
            {
                directives[idx].BaseInst = checked(
                    directives[idx].BaseInst + baseInst);
                ++adjusted;
            }
            MemoryMarshal.AsBytes(directives).CopyTo(alloc.Data);
        }
        finally
        {
            for (int idx = 0; idx < adjusted; ++idx)
                directives[idx].BaseInst -= baseInst;
        }
        return alloc;
    }

    private void RestartRealmXformCycleIfStale(long cycleSerialNo) => _realmXformCycles.RestartIfStale(cycleSerialNo);

    private void RestartRealmXformCycle() => _realmXformCycles.Reset();

    private void WatchPlainXformDemand(long cycleSerialNo, uint insts)
    {
        if (_plainXformDemandCycleSerialNo != cycleSerialNo)
        {
            _plainXformDemandCycleSerialNo = cycleSerialNo;
            _plainXformDemandThisCycle = 0;
        }
        _plainXformDemandThisCycle = checked(
            _plainXformDemandThisCycle + insts);
        _plainXformDemandHiWater = Math.Max(
            _plainXformDemandHiWater,
            _plainXformDemandThisCycle);
    }

    private void ProbeRhiTickers(bool diag)
    {
        if (!diag || _device is null)
            return;

        double sumMsec = 0;
        bool any = false;
        if (_device.Tickers.TryLocate(SolidTickerAmbit, out double solidMsec))
        {
            sumMsec += solidMsec;
            any = true;
        }
        if (_device.Tickers.TryLocate(SeeThruTickerAmbit, out double seeThruMsec))
        {
            sumMsec += seeThruMsec;
            any = true;
        }
        if (!any)
            return;

        _gpuSpecimens[_gpuSpecimenCur] = (long)(sumMsec * 1000.0);
        _gpuSpecimenCur = (_gpuSpecimenCur + 1) % _gpuSpecimens.Length;
    }

    private void TeardownRhiAssetList()
    {
        var backbuffer = _backbufferPipes;
        var offscreen = _offscreenPipes;
        _backbufferPipes = null;
        _offscreenPipes = null;
        TeardownTriMeshPipeSet(backbuffer);
        if (!ReferenceEquals(offscreen, backbuffer))
            TeardownTriMeshPipeSet(offscreen);
        TeardownDirectedShadeRecipientPipes();
    }

    private static void TeardownTriMeshPipeSet(TriMeshPipeGroup? pipes)
    {
        if (pipes is null)
            return;
        pipes.Opaque.Dispose();
        pipes.OpaqueAlphaToCoverage.Dispose();
        pipes.AlphaBlend.Dispose();
        pipes.AlphaBlendDepthWrite.Dispose();
        pipes.AlphaAdditive.Dispose();
        pipes.AlphaAdditiveDepthWrite.Dispose();
        pipes.RawAdditive.Dispose();
        pipes.RawAdditiveDepthWrite.Dispose();
        pipes.AlphaInverse.Dispose();
        pipes.AlphaInverseDepthWrite.Dispose();
        pipes.InverseAdditive.Dispose();
        pipes.InverseAdditiveDepthWrite.Dispose();
    }
}
