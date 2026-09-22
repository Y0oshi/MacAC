using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Illumination;

namespace MacAC.Client.Graphics.Batching;

public sealed unsafe partial class EnvironChamberPainter
{
    private readonly IClientGpuDevice? _device;
    private readonly ILatestGpuCycleOrigin? _cycles;
    private readonly IRealmPassScope? _ambit;
    private IGpuPipe? _solidPipe;
    private IGpuPipe? _alphaPipeline;
    private IGpuPipe? _alphaZDepthEmitPipe;
    private IGpuPipe? _clipPipe;
    private IGpuPipe? _additivePipeline;
    private IGpuPipe? _additiveZDepthEmitPipe;
    private IGpuPipe? _rawAdditivePipe;
    private IGpuPipe? _rawAdditiveZDepthEmitPipe;
    private IGpuPipe? _invPipe;
    private IGpuPipe? _invZDepthEmitPipe;
    private IGpuPipe? _invAdditivePipe;
    private IGpuPipe? _invAdditiveZDepthEmitPipe;
    private readonly LandTileset.CanonDetailTextureWiring _surroundingsSpecifics;
    private readonly Func<bool> _structureSpecificsTurnedOn;

    internal bool TransparentDetailEnabled
    {
        get
        {
            return CanonDetailTextureContract.ShouldRasterize(_structureSpecificsTurnedOn(), _surroundingsSpecifics)
        && _surroundingsSpecifics.Tiling != 0f;
        }
    }

    internal EnvironChamberPainter(
        IClientGpuDevice device,
        ILatestGpuCycleOrigin frames,
        IRealmPassScope scope,
        ThingTriMeshKeeper meshManager,
        BatchFrustum frustum,
        LandTileset.CanonDetailTextureWiring surroundingsSpecifics = default,
        Func<bool>? structureSpecificsTurnedOn = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
        _ambit = scope ?? throw new ArgumentNullException(nameof(scope));
        _meshManager = meshManager ?? throw new ArgumentNullException(nameof(meshManager));
        _frustum = frustum ?? throw new ArgumentNullException(nameof(frustum));
        _surroundingsSpecifics = surroundingsSpecifics;
        _structureSpecificsTurnedOn = structureSpecificsTurnedOn ?? DeactivateSpecificsTextures;

        try
        {
            _solidPipe = BuildShellPipe(
                device, "envcell-opaque", GpuBlendManner.None, zDepthEmit: true, scope.SampleCount);
            _alphaPipeline = BuildShellPipe(
                device, "envcell-alpha", GpuBlendManner.StraightAlpha, zDepthEmit: false, scope.SampleCount);
            _alphaZDepthEmitPipe = BuildShellPipe(
                device, "envcell-alpha-depth-write", GpuBlendManner.StraightAlpha, zDepthEmit: true, scope.SampleCount);
            _clipPipe = BuildShellPipe(
                device,
                "envcell-clip",
                GpuBlendManner.PremultipliedAlpha,
                zDepthEmit: true,
                scope.SampleCount);
            _additivePipeline = BuildShellPipe(
                device, "envcell-additive", GpuBlendManner.Additive, zDepthEmit: false, scope.SampleCount);
            _additiveZDepthEmitPipe = BuildShellPipe(
                device, "envcell-additive-depth-write", GpuBlendManner.Additive, zDepthEmit: true, scope.SampleCount);
            _rawAdditivePipe = BuildShellPipe(
                device, "envcell-raw-additive", GpuBlendManner.RawAdditive, zDepthEmit: false, scope.SampleCount);
            _rawAdditiveZDepthEmitPipe = BuildShellPipe(
                device, "envcell-raw-additive-depth-write", GpuBlendManner.RawAdditive, zDepthEmit: true, scope.SampleCount);
            _invPipe = BuildShellPipe(
                device, "envcell-inverse", GpuBlendManner.InverseAlpha, zDepthEmit: false, scope.SampleCount);
            _invZDepthEmitPipe = BuildShellPipe(
                device, "envcell-inverse-depth-write", GpuBlendManner.InverseAlpha, zDepthEmit: true, scope.SampleCount);
            _invAdditivePipe = BuildShellPipe(
                device, "envcell-inverse-additive", GpuBlendManner.InverseAdditive, zDepthEmit: false, scope.SampleCount);
            _invAdditiveZDepthEmitPipe = BuildShellPipe(
                device, "envcell-inverse-additive-depth-write", GpuBlendManner.InverseAdditive, zDepthEmit: true, scope.SampleCount);
            _initialized = true;
        }
        catch
        {
            TeardownRhiAssetList();
            throw;
        }
    }

    internal static FaceCulling LocateCanonChamberShellPruneManner(FaceCulling flanksKind)
    {
        _ = flanksKind;
        return FaceCulling.Clockwise;
    }

    internal void AttachSurroundingsSpecificsBucket(
        IGpuSweepCoder coder,
        IGpuCycle cycle,
        int instTally)
    {
        if (_specificsBucketBlob.Length < instTally)
            Array.Resize(ref _specificsBucketBlob, Math.Max(instTally, 16));
        Array.Fill(_specificsBucketBlob, 1u, 0, instTally);
        AttachLoopSection<uint>(
            coder,
            cycle,
            GpuBindingModel.DepotInstSpecificsBucket,
            _specificsBucketBlob.AsSpan(0, instTally));
    }

    private static bool DeactivateSpecificsTextures() => false;

    private static IGpuPipe BuildShellPipe(
        IClientGpuDevice dev,
        string label,
        GpuBlendManner blend,
        bool zDepthEmit,
        int specimenTally,
        string shaderLabel = "props_lit",
        GpuContrastOp zDepthContrast = MacAC.Client.Graphics.RealmDepthContract.RealmContrast)
    {
        return dev.BuildPipe(new GpuPipeSpec
        {
            Name = label,
            Shaders = new GpuShaderGroup(shaderLabel),
            VertArrangement = GpuVertexArrangement.RealmTriMesh,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = blend,
            Depth = new GpuDepthLedger(Test: true, Write: zDepthEmit, zDepthContrast),
            Cull = GpuPruneManner.Back,
            FrontFace = GpuFrontFacet.Clockwise,
            AlphaToCoverage = false,
            TintEmit = true,
            SampleCount = specimenTally,
        });
    }

    private void SubmitRhi(
        List<InstanceData> allInsts,
        BatchRenderPass rasterizePass,
        int sumDraws,
        int uniqueInstTally)
    {
        var ambit = _ambit!;
        var coder = ambit.DemandCoder();
        IGpuCycle cycle = _cycles!.LatestCycle
            ?? throw new InvalidOperationException(
                "EnvironChamberPainter needs an open IGpuCycle (see GpuDeviceCycleLifespan)");
        GlobalTriMeshBuffer triMesh = _meshManager.GlobalBuf
            ?? throw new InvalidOperationException("The shared mesh arena isn't published");

        if (_gpuInstanceTransforms.Length < uniqueInstTally)
        {
            Array.Resize(
                ref _gpuInstanceTransforms,
                Math.Max(_gpuInstanceTransforms.Length * 2, uniqueInstTally));
        }
        for (int idx = 0; idx < uniqueInstTally; ++idx)
            _gpuInstanceTransforms[idx] = allInsts[idx].Transform;

        if (_clipSocketBlob.Length < uniqueInstTally)
            _clipSocketBlob = new uint[Math.Max(_clipSocketBlob.Length * 2, uniqueInstTally)];
        Array.Clear(_clipSocketBlob, 0, uniqueInstTally);

        if (_instAlphaBlob.Length < uniqueInstTally)
        {
            _instAlphaBlob = new float[
                Math.Max(_instAlphaBlob.Length * 2, uniqueInstTally)];
        }
        Array.Fill(_instAlphaBlob, 1f, 0, uniqueInstTally);

        int lampStride = LightKeeper.UpperLightsPerEnvCell;
        if (_lampSetBlob.Length < uniqueInstTally * lampStride)
        {
            _lampSetBlob = new int[Math.Max(
                _lampSetBlob.Length * 2,
                uniqueInstTally * lampStride)];
        }
        for (int idx = 0; idx < uniqueInstTally; ++idx)
        {
            int[] chamberSet = FetchChamberLampSet(allInsts[idx].CellId);
            Array.Copy(chamberSet, 0, _lampSetBlob, idx * lampStride, lampStride);
        }

        int lampTally = SceneLightPacker.Pack(_ptCapture, ref _globalLampBlob);
        int globalLampPushTally = lampTally > 0 ? lampTally : 1;

        var pushConstants = new GpuShoveConstants
        {
            LensMirror = _previousLensProj,
            PaintIdentShift = 0,
            // A7 Fix D D-3/D-4: EnvCell bake - wrap points, no sun.
            IlluminationManner = 1,
            RasterizePass = (int)rasterizePass,
            LampDiag = MacAC.Mechanics.Drawing.DrawTelemetry.LampDiagManner,
            TextureIndexA = 0,
            TextureOrdinalB = 0,
            ParamA = 0f,
            ParameterB = 0f,
        };

        IGpuPipe basePipe = rasterizePass == BatchRenderPass.Transparent
            ? _alphaPipeline!
            : _solidPipe!;
        AttachPipeWithTriMesh(coder, basePipe, triMesh);
        coder.AssignPushConstants(in pushConstants);

        AttachLoopSection<Matrix4x4>(
            coder, cycle, GpuBindingModel.DepotInsts,
            _gpuInstanceTransforms.AsSpan(0, uniqueInstTally));
        AttachLoopSection<ModernLotBlob>(
            coder, cycle, GpuBindingModel.DepotLots,
            _modernBatches.AsSpan(0, sumDraws));
        AttachLoopSection<uint>(
            coder, cycle, GpuBindingModel.DepotClipSockets,
            _clipSocketBlob.AsSpan(0, uniqueInstTally));
        AttachLoopSection<float>(
            coder, cycle, GpuBindingModel.StorageInstanceAlpha,
            _instAlphaBlob.AsSpan(0, uniqueInstTally));
        AttachLoopSection<float>(
            coder, cycle, GpuBindingModel.DepotGlobalLamps,
            _globalLampBlob.AsSpan(
                0,
                globalLampPushTally * SceneLightPacker.FloatsPerLamp));
        AttachLoopSection<int>(
            coder, cycle, GpuBindingModel.DepotInstLampSets,
            _lampSetBlob.AsSpan(0, uniqueInstTally * lampStride));
        AttachSurroundingsSpecificsBucket(coder, cycle, uniqueInstTally);

        MacAC.Client.Graphics.RealmFrameSectionWiring.AttachClipZones(
            coder, ambit.Sections, cycle);
        MacAC.Client.Graphics.RealmFrameSectionWiring.AttachTableauIllumination(
            coder, ambit.Sections, cycle);

        var directives = cycle.ReserveLoop(
            sumDraws * sizeof(DrawElementsIndirectDirective),
            GpuLoopPurpose.Indirect);
        MemoryMarshal.AsBytes(_commands.AsSpan(0, sumDraws)).CopyTo(directives.Data);
        IClientGpuBuffer directiveBuf = directives.Buffer;
        uint directiveBase = directives.ShiftOctets;
        bool specificsTurnedOn = TransparentDetailEnabled;

        for (int paintSpanOrdinal = 0; paintSpanOrdinal < _mdiDrawRanges.Count; ++paintSpanOrdinal)
        {
            var paintSpan = _mdiDrawRanges[paintSpanOrdinal];
            int clusterOrdinal = paintSpan.GroupIndex;
            FaceCulling pruneManner = LocateCanonChamberShellPruneManner(
                (FaceCulling)(clusterOrdinal % PruneClusterTally));

            bool isAdditive = rasterizePass == BatchRenderPass.Transparent
                && clusterOrdinal >= AdditiveClusterBase
                && clusterOrdinal < ClipDdsClusterBase;
            bool isClip = rasterizePass == BatchRenderPass.Transparent
                && clusterOrdinal >= ClipDdsClusterBase;
            IGpuPipe spanBasePipe = isClip
                ? _clipPipe!
                : isAdditive
                    ? _additivePipeline!
                    : _alphaPipeline!;
            if (specificsTurnedOn)
                spanBasePipe = PipeForMatl(paintSpan.MaterialState);
            if (rasterizePass == BatchRenderPass.Transparent || specificsTurnedOn)
            {
                AttachPipeWithTriMesh(
                    coder,
                    spanBasePipe,
                    triMesh);
            }

            AssignPruneManner(coder, pruneManner);

            pushConstants.RasterizePass = isAdditive
                ? (int)rasterizePass | 0x100
                : (int)rasterizePass;
            if (specificsTurnedOn && !paintSpan.MaterialState.FogEnabled)
                pushConstants.RasterizePass |= CanonDetailTextureContract.NoFogRasterizePassBit;
            pushConstants.PaintIdentShift = paintSpan.FirstCommand;
            pushConstants.ParameterB = specificsTurnedOn
                ? paintSpan.MaterialState.AlphaTestReference
                : isClip
                    ? clusterOrdinal >= ClipPalettedClusterBase ? 100f / 255f : 200f / 255f
                    : 0f;
            pushConstants.TextureIndexA = specificsTurnedOn
                ? _surroundingsSpecifics.TextureSlot.Index
                : 0u;
            pushConstants.ParamA = specificsTurnedOn
                ? _surroundingsSpecifics.Tiling
                : 0f;
            coder.AssignPushConstants(in pushConstants);

            coder.MultiPaintIndexedIndirect(
                directiveBuf,
                directiveBase + (uint)(paintSpan.FirstCommand * sizeof(DrawElementsIndirectDirective)),
                (uint)paintSpan.CommandCount,
                (uint)sizeof(DrawElementsIndirectDirective));
        }

        pushConstants.TextureIndexA = 0;
        pushConstants.ParamA = 0f;
        pushConstants.ParameterB = 0f;
        pushConstants.RasterizePass &= ~CanonDetailTextureContract.NoFogRasterizePassBit;
        coder.AssignPushConstants(in pushConstants);
    }

    private IGpuPipe PipeForMatl(CanonSurfaceMaterialState material)
    {
        return material.Blend switch
        {
            CanonSurfaceBlend.Opaque => _solidPipe!,
            CanonSurfaceBlend.StraightAlpha => material.AlphaTestTurnedOn
                ? _alphaZDepthEmitPipe!
                : _alphaPipeline!,
            CanonSurfaceBlend.AlphaAdditive => material.AlphaTestTurnedOn
                ? _additiveZDepthEmitPipe!
                : _additivePipeline!,
            CanonSurfaceBlend.Additive => material.AlphaTestTurnedOn
                ? _rawAdditiveZDepthEmitPipe!
                : _rawAdditivePipe!,
            CanonSurfaceBlend.InverseAlpha => material.AlphaTestTurnedOn
                ? _invZDepthEmitPipe!
                : _invPipe!,
            CanonSurfaceBlend.InverseAdditive => material.AlphaTestTurnedOn
                ? _invAdditiveZDepthEmitPipe!
                : _invAdditivePipe!,
            CanonSurfaceBlend.Clip => _clipPipe!,
            _ => throw new ArgumentOutOfRangeException(nameof(material), material, "Unrecognized SetSurface blend"),
        };
    }

    private void AttachPipeWithTriMesh(
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

    private static void AssignPruneManner(IGpuSweepCoder coder, FaceCulling manner)
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

    private static void AttachLoopSection<T>(
        IGpuSweepCoder coder,
        IGpuCycle cycle,
        uint mapping,
        ReadOnlySpan<T> blob)
        where T : unmanaged
    {
        int elemOctets = sizeof(T);
        int byteTally = Math.Max(blob.Length * elemOctets, elemOctets);
        var alloc = cycle.ReserveLoop(byteTally, GpuLoopPurpose.Storage);
        if (!blob.IsEmpty)
            blob.CopyTo(alloc.AsSpan<T>());
        coder.AttachDepotBuf(
            mapping,
            alloc.Buffer,
            alloc.ShiftOctets,
            (uint)byteTally);
    }

    private void TeardownRhiAssetList()
    {
        _solidPipe?.Dispose();
        _solidPipe = null;
        _alphaPipeline?.Dispose();
        _alphaPipeline = null;
        _alphaZDepthEmitPipe?.Dispose();
        _alphaZDepthEmitPipe = null;
        _clipPipe?.Dispose();
        _clipPipe = null;
        _additivePipeline?.Dispose();
        _additivePipeline = null;
        _additiveZDepthEmitPipe?.Dispose();
        _additiveZDepthEmitPipe = null;
        _rawAdditivePipe?.Dispose();
        _rawAdditivePipe = null;
        _rawAdditiveZDepthEmitPipe?.Dispose();
        _rawAdditiveZDepthEmitPipe = null;
        _invPipe?.Dispose();
        _invPipe = null;
        _invZDepthEmitPipe?.Dispose();
        _invZDepthEmitPipe = null;
        _invAdditivePipe?.Dispose();
        _invAdditivePipe = null;
        _invAdditiveZDepthEmitPipe?.Dispose();
        _invAdditiveZDepthEmitPipe = null;
    }
}
