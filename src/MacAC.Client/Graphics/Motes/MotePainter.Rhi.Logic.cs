using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Geometry;

namespace MacAC.Client.Graphics;

public sealed unsafe partial class MotePainter
{
    private bool TriMeshMotesOnHand => _triMeshAlphaPipe is not null;

    internal static MeshMotePipelineLedger LocateTriMeshMotePipePhase(
        SeeThroughKind blend,
        bool solidZDepthPhase)
    {
        return solidZDepthPhase
            ? new MeshMotePipelineLedger(
                GpuBlendManner.None,
                new GpuDepthLedger(Test: true, Write: true, RealmDepthContract.RealmContrast))
            : new MeshMotePipelineLedger(
                blend switch
                {
                    SeeThroughKind.Additive => GpuBlendManner.Additive,
                    SeeThroughKind.InvAlpha => GpuBlendManner.InverseAlpha,
                    _ => GpuBlendManner.StraightAlpha,
                },
                new GpuDepthLedger(Test: true, Write: false, RealmDepthContract.RealmContrast));
    }

    private IGpuPipe PipelineForMeshBlend(
        SeeThroughKind blend,
        bool solidZDepthPhase)
    {
        var phase = LocateTriMeshMotePipePhase(
            blend,
            solidZDepthPhase);
        return phase.Blend switch
        {
            GpuBlendManner.None => _triMeshSolidPipe!,
            GpuBlendManner.Additive => _triMeshAdditivePipe!,
            GpuBlendManner.InverseAlpha => _triMeshInvPipe!,
            _ => _triMeshAlphaPipe!,
        };
    }

    private static RhiVertSegment SectionOf(GpuLoopAlloc alloc) =>
        new(alloc.Buffer, alloc.ShiftOctets);

    private static GpuLoopAlloc EmitVertLoop<T>(IGpuCycle cycle, ReadOnlySpan<T> blob)
        where T : unmanaged
    {
        int elemOctets = sizeof(T);
        int byteTally = Math.Max(blob.Length * elemOctets, elemOctets);
        var alloc = cycle.ReserveLoop(byteTally, GpuLoopPurpose.Vertex);
        if (!blob.IsEmpty)
            blob.CopyTo(alloc.AsSpan<T>());
        return alloc;
    }

    private IGpuCycle DemandRhiCycle()
    {
        return !_dynamicCycleBegun
            ? throw new InvalidOperationException("BeginFrame has to be called prior to drawing particles")
            : _cycles!.LatestCycle
            ?? throw new InvalidOperationException(
                "MotePainter needs an open IGpuCycle (see GpuDeviceCycleLifespan)");
    }

    private void BuildRhiAssetList(IClientGpuDevice dev, int specimenTally)
    {
        _billboardAlphaPipe = BuildBillboardPipe(
            dev, "particle-billboard-alpha", GpuBlendManner.StraightAlpha, specimenTally);
        _billboardAdditivePipe = BuildBillboardPipe(
            dev, "particle-billboard-additive", GpuBlendManner.Additive, specimenTally);

        var quadVertOctets = MemoryMarshal.AsBytes<float>(QuadVerts);
        _quadVertBuf = dev.BuildBuf(new GpuBufferSpec(
            "particle-quad-vertices",
            quadVertOctets.Length,
            GpuBufferPurpose.Vertex | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        _quadVertBuf.Upload(0, quadVertOctets);

        var quadOrdinalOctets = MemoryMarshal.AsBytes<uint>(QuadOrdinals);
        _quadOrdinalBuf = dev.BuildBuf(new GpuBufferSpec(
            "particle-quad-indices",
            quadOrdinalOctets.Length,
            GpuBufferPurpose.Index | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        _quadOrdinalBuf.Upload(0, quadOrdinalOctets);

        if (_triMeshBridge?.TriMeshKeeper?.GlobalBuf is null)
            return;

        _triMeshSolidPipe = BuildTriMeshMotePipe(
            dev, "particle-mesh-opaque", GpuBlendManner.None, zDepthEmit: true, specimenTally);
        _triMeshAlphaPipe = BuildTriMeshMotePipe(
            dev, "particle-mesh-alpha", GpuBlendManner.StraightAlpha, zDepthEmit: false, specimenTally);
        _triMeshAdditivePipe = BuildTriMeshMotePipe(
            dev, "particle-mesh-additive", GpuBlendManner.Additive, zDepthEmit: false, specimenTally);
        _triMeshInvPipe = BuildTriMeshMotePipe(
            dev, "particle-mesh-inverse", GpuBlendManner.InverseAlpha, zDepthEmit: false, specimenTally);
    }

    private static IGpuPipe BuildBillboardPipe(
        IClientGpuDevice dev,
        string label,
        GpuBlendManner blend,
        int specimenTally)
    {
        return dev.BuildPipe(new GpuPipeSpec
        {
            Name = label,
            Shaders = new GpuShaderGroup("motes"),
            VertArrangement = BillboardVertArrangement,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = blend,
            Depth = new GpuDepthLedger(Test: true, Write: false, RealmDepthContract.RealmContrast),
            Cull = GpuPruneManner.None,
            FrontFace = GpuFrontFacet.CounterClockwise,
            AlphaToCoverage = false,
            TintEmit = true,
            SampleCount = specimenTally,
        });
    }

    private static IGpuPipe BuildTriMeshMotePipe(
        IClientGpuDevice dev,
        string label,
        GpuBlendManner blend,
        bool zDepthEmit,
        int specimenTally)
    {
        return dev.BuildPipe(new GpuPipeSpec
        {
            Name = label,
            Shaders = new GpuShaderGroup("motes_mesh"),
            VertArrangement = TriMeshVertArrangement,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = blend,
            Depth = new GpuDepthLedger(Test: true, Write: zDepthEmit, RealmDepthContract.RealmContrast),
            Cull = GpuPruneManner.None,
            FrontFace = GpuFrontFacet.Clockwise,
            AlphaToCoverage = false,
            TintEmit = true,
            SampleCount = specimenTally,
        });
    }

    // Binds a billboard pipeline and immediately re-establishes both vertex sources and the index
    // source
    private void AttachBillboardPipe(
        IGpuSweepCoder coder,
        IGpuCycle cycle,
        Matrix4x4 lensProj,
        bool additive,
        IClientGpuBuffer instBuf,
        uint instShiftOctets)
    {
        coder.BindPipeline(additive
            ? _billboardAdditivePipe!
            : _billboardAlphaPipe!);
        coder.AssignPushConstants(new GpuShoveConstants
        {
            LensMirror = lensProj,
            PaintIdentShift = 0,
            IlluminationManner = 0,
            RasterizePass = 0,
            LampDiag = 0,
            TextureIndexA = 0,
            TextureOrdinalB = 0,
            ParamA = 0f,
            ParameterB = 0f,
        });
        coder.AttachVertBuf(0, _quadVertBuf!, 0);
        coder.AttachVertBuf(1, instBuf, instShiftOctets);
        coder.AttachOrdinalBuf(_quadOrdinalBuf!, 0, GpuOrdinalKind.UInt32);
        RealmFrameSectionWiring.AttachClipZones(
            coder,
            _ambit!.Sections,
            cycle);
    }

    private static void ImposeTriMeshPruneMannerRhi(IGpuSweepCoder coder, FaceCulling manner)
    {
        coder.AssignFrontFace(GpuFrontFacet.Clockwise);
        coder.AssignPruneManner(manner switch
        {
            FaceCulling.None => GpuPruneManner.None,
            FaceCulling.Clockwise => GpuPruneManner.Front,
            _ => GpuPruneManner.Back,
        });
    }

    private void ReadyPostponedAlphaDrawsRhi(ReadOnlySpan<int> tickets)
    {
        IGpuCycle cycle = DemandRhiCycle();
        int tally = tickets.Length;
        if (_readiedAlpha.Length < tally)
            Array.Resize(ref _readiedAlpha, tally + 256);
        if (_readiedInstShifts.Length < tally)
            Array.Resize(ref _readiedInstShifts, tally + 256);
        if (_instTemp.Length < tally)
            Array.Resize(ref _instTemp, tally + 256);
        if (_triMeshInstTemp.Length < tally)
            _triMeshInstTemp = new MeshMoteGpuInstance[tally + 256];

        int billboardTally = 0;
        int triMeshTally = 0;
        for (int idx = 0; idx < tally; ++idx)
        {
            var postponed = _deferredAlpha[tickets[idx]];
            _readiedAlpha[idx] = postponed;
            if (postponed.Kind == MoteSubmissionKind.Billboard)
            {
                _readiedInstShifts[idx] = (uint)billboardTally;
                EmitBillboardGpuInst(
                    ref _instTemp[billboardTally++],
                    postponed.Billboard.Instance);
            }
            else
            {
                _readiedInstShifts[idx] = (uint)triMeshTally;
                EmitTriMeshGpuInst(
                    ref _triMeshInstTemp[triMeshTally++],
                    postponed.Mesh.Instance);
            }
        }

        _readiedBillboardInsts = billboardTally > 0
            ? SectionOf(EmitVertLoop<BillboardGpuInst>(
                cycle,
                _instTemp.AsSpan(0, billboardTally)))
            : default;
        _readiedTriMeshInsts = triMeshTally > 0
            ? SectionOf(EmitVertLoop<MeshMoteGpuInstance>(
                cycle,
                _triMeshInstTemp.AsSpan(0, triMeshTally)))
            : default;
        _readiedAlphaTally = tally;
    }

    private void TeardownRhiAssetList()
    {
        List<Exception>? misses = null;
        void Attempt(Action act)
        {
            try { act(); }
            catch (Exception problem) { (misses ??= []).Add(problem); }
        }

        Attempt(() => _billboardAlphaPipe?.Dispose());
        _billboardAlphaPipe = null;
        Attempt(() => _billboardAdditivePipe?.Dispose());
        _billboardAdditivePipe = null;
        Attempt(() => _triMeshSolidPipe?.Dispose());
        _triMeshSolidPipe = null;
        Attempt(() => _triMeshAlphaPipe?.Dispose());
        _triMeshAlphaPipe = null;
        Attempt(() => _triMeshAdditivePipe?.Dispose());
        _triMeshAdditivePipe = null;
        Attempt(() => _triMeshInvPipe?.Dispose());
        _triMeshInvPipe = null;
        Attempt(() => _quadVertBuf?.Dispose());
        _quadVertBuf = null;
        Attempt(() => _quadOrdinalBuf?.Dispose());
        _quadOrdinalBuf = null;
        _readiedBillboardInsts = default;
        _readiedTriMeshInsts = default;

        if (misses is not null)
            throw new AggregateException("The particle renderer's RHI resources didn't fully release", misses);
    }
}
