using System.Numerics;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Illumination;

namespace MacAC.Client.Graphics.Batching;

public sealed unsafe partial class RealmPaintRouter
{
    private void SubmitRhi(
        Matrix4x4 lensProj,
        int immediateInsts,
        int sumDraws,
        bool diag)
    {
        var ambit = _ambit!;
        var coder = ambit.DemandCoder();
        IGpuCycle cycle = DemandRhiCycle();
        GlobalTriMeshBuffer triMesh = _triMeshBridge.TriMeshKeeper?.GlobalBuf
            ?? throw new InvalidOperationException("The shared mesh arena isn't published");

        var pushConstants = new GpuShoveConstants
        {
            LensMirror = lensProj,
            PaintIdentShift = 0,
            IlluminationManner = 0,
            RasterizePass = 0,
            LampDiag = DrawTelemetry.LampDiagManner,
            TextureIndexA = 0,
            TextureOrdinalB = 0,
            ParamA = 0f,
            ParameterB = 0f,
        };

        RhiSegment instXforms = EmitRealmXformSection(
            cycle,
            _stage.Models(immediateInsts),
            out uint xformBaseInst);
        pushConstants.TextureOrdinalB = xformBaseInst;

        var pipes = PipesFor(
            coder,
            cycle,
            out DirectionalShadeFrameWiring shadeMapping);
        IGpuPipe solidPipe = AlphaToCoverage
            ? pipes.OpaqueAlphaToCoverage
            : pipes.Opaque;
        AttachPipeWithTriMesh(
            coder,
            solidPipe,
            triMesh);
        coder.AssignPushConstants(in pushConstants);
        AttachDirectedShadeRecipient(coder, in shadeMapping);

        AttachSection(
            coder,
            GpuBindingModel.DepotInsts,
            instXforms);
        AttachLoopSection<BatchData>(
            coder, cycle, GpuBindingModel.DepotLots,
            _lotBlob.AsSpan(0, sumDraws));
        AttachLoopSection<uint>(
            coder, cycle, GpuBindingModel.DepotClipSockets,
            _stage.ClipSockets(immediateInsts));
        AttachGlobalLampsRhi(coder, cycle);
        AttachLoopSection<int>(
            coder, cycle, GpuBindingModel.DepotInstLampSets,
            _stage.Lamps(immediateInsts));
        AttachLoopSection<uint>(
            coder, cycle, GpuBindingModel.DepotInstInside,
            _stage.Inside(immediateInsts));
        AttachLoopSection<float>(
            coder, cycle, GpuBindingModel.StorageInstanceAlpha,
            _stage.Opacities(immediateInsts));
        AttachLoopSection<Vector2>(
            coder, cycle, GpuBindingModel.DepotInstPickIllumination,
            _stage.PickIllumination(immediateInsts));
        AttachLoopSection<uint>(
            coder, cycle, GpuBindingModel.DepotInstSpecificsBucket,
            _stage.SpecificsBuckets(immediateInsts));

        MacAC.Client.Graphics.RealmFrameSectionWiring.AttachClipZones(
            coder, ambit.Sections, cycle);
        MacAC.Client.Graphics.RealmFrameSectionWiring.AttachTableauIllumination(
            coder, ambit.Sections, cycle);

        var directives = EmitIndirectDirectives(
            cycle,
            _indirectDirectives.AsSpan(0, sumDraws),
            xformBaseInst);
        IClientGpuBuffer directiveBuf = directives.Buffer;
        uint directiveBase = directives.ShiftOctets;
        ReadOnlySpan<uint> consumedSpecificsBuckets =
            _stage.SpecificsBuckets(immediateInsts);
        bool specificsTurnedOn = CanonDetailTextureContract.ShouldRasterize(
                _structureSpecificsTurnedOn(),
                _structureSpecifics)
            && _structureSpecifics.Tiling != 0f;

        if (_solidPaintTally > 0)
        {
            pushConstants.RasterizePass = 0;
            pushConstants.PaintIdentShift = 0;
            coder.AssignPushConstants(in pushConstants);
            using (CommenceRhiTicker(coder, diag, SolidTickerAmbit))
            {
                PaintSpecificsAwareSpanRhi(
                    coder, triMesh, pipes, solidPipe,
                    ref pushConstants, directiveBuf, directiveBase,
                    0, _solidPaintTally, consumedSpecificsBuckets, specificsTurnedOn);
            }
        }

        if (_seeThruPaintTally > 0)
        {
            pushConstants.RasterizePass = 1;
            pushConstants.PaintIdentShift = _solidPaintTally;
            coder.AssignPushConstants(in pushConstants);
            using (CommenceRhiTicker(coder, diag, SeeThruTickerAmbit))
            {
                PaintImmediateSeeThruRhi(
                    coder,
                    triMesh,
                    pipes,
                    ref pushConstants,
                    directiveBuf,
                    directiveBase,
                    consumedSpecificsBuckets,
                    specificsTurnedOn);
            }
        }

        ProbeRhiTickers(diag);
    }
}
