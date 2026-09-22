using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Stride;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Illumination;

namespace MacAC.Client.Graphics.Batching;

public sealed unsafe partial class RealmPaintRouter
{
    internal readonly record struct SequencedCombineExec(int FirstCommand, int CommandCount);

    private enum PipeBin
    {
        Opaque,
        AlphaBlend,
        AlphaAdditive,
        AlphaInverse,
    }

    private static PipeBin BinFor(SeeThroughKind sort)
    {
        return IsSolid(sort)
            ? PipeBin.Opaque
            : sort switch
            {
                SeeThroughKind.Additive => PipeBin.AlphaAdditive,
                SeeThroughKind.InvAlpha => PipeBin.AlphaInverse,
                _ => PipeBin.AlphaBlend,
            };
    }

    private IGpuPipe PipeForBin(TriMeshPipeGroup pipes, PipeBin bin) =>
        bin switch
        {
            PipeBin.Opaque => AlphaToCoverage ? pipes.OpaqueAlphaToCoverage : pipes.Opaque,
            PipeBin.AlphaAdditive => pipes.AlphaAdditive,
            PipeBin.AlphaInverse => pipes.AlphaInverse,
            _ => pipes.AlphaBlend,
        };

    internal static List<SequencedCombineExec> AssembleSequencedCombineExecutions(
        SequencedPaintFlow flow, IReadOnlyList<int>? forcedBreaksAscending = null,
        List<SequencedCombineExec>? dest = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        int tally = flow.Count;

        for (int idx = 0; idx < tally; idx++)
        {
            if (flow.Junctures[idx] == StrollPaintJuncture.PortalPunch)
            {
                throw new NotSupportedException(
                    $"SequencedPaintFlow command {idx} carries WalkDrawStage.PortalPunch, "
                    + "which can't be submitted through the ordered mesh path. "
                    + "Portal-punch geometry needs its dedicated emission path");
            }
        }

        IReadOnlyList<int> breaks = forcedBreaksAscending ?? Array.Empty<int>();
        int breakCur = 0;

        var executions = dest ?? [];
        executions.Clear();
        int cur = 0;
        while (cur < tally)
        {
            while (breakCur < breaks.Count && breaks[breakCur] <= cur)
                breakCur++;

            StrollPaintJuncture juncture = flow.Junctures[cur];
            PipeBin bin = BinFor(flow.Keys[cur].Translucency);
            FaceCulling prune = flow.Keys[cur].CullMode;
            bool specifics = flow.SpecificsCategories[cur] != 0;

            int finish = cur + 1;
            if (!specifics)
            {
                while (finish < tally
                    && !(breakCur < breaks.Count && breaks[breakCur] == finish)
                    && flow.Junctures[finish] == juncture
                    && flow.SpecificsCategories[finish] == 0
                    && BinFor(flow.Keys[finish].Translucency) == bin
                    && flow.Keys[finish].CullMode == prune)
                {
                    finish++;
                }
            }

            executions.Add(new SequencedCombineExec(cur, finish - cur));
            cur = finish;
        }

        return executions;
    }

    internal static void AssembleSequencedInstExecutions(
        SequencedPaintFlow flow,
        IReadOnlyList<SequencedCombineExec> combineExecutions,
        List<SequencedCombineExec> dest)
    {
        dest.Clear();
        foreach (SequencedCombineExec exec in combineExecutions)
        {
            int cur = exec.FirstCommand;
            int execFinish = cur + exec.CommandCount;
            while (cur < execFinish)
            {
                int finish = cur + 1;
                if (flow.AllowInstMerges[cur])
                {
                    while (finish < execFinish && flow.AllowInstMerges[finish]
                        && flow.Keys[finish] == flow.Keys[cur])
                        finish++;
                }
                dest.Add(new SequencedCombineExec(cur, finish - cur));
                cur = finish;
            }
        }
    }

    internal static DrawElementsIndirectDirective SequencedIndirectDirective(
        ClusterTag tag, SequencedCombineExec insts) =>
        DrawElementsIndirectDirective.For(tag, insts.FirstCommand, insts.CommandCount);

    private static void VetCombineExec(SequencedPaintFlow flow, SequencedCombineExec exec)
    {
        int leadDirective = exec.FirstCommand;
        StrollPaintJuncture juncture = flow.Junctures[leadDirective];
        PipeBin bin = BinFor(flow.Keys[leadDirective].Translucency);
        FaceCulling prune = flow.Keys[leadDirective].CullMode;
        bool specifics = flow.SpecificsCategories[leadDirective] != 0;
        int finish = leadDirective + exec.CommandCount;

        if (specifics && exec.CommandCount != 1)
        {
            throw new InvalidOperationException(
                $"Merge run [{leadDirective}, {finish}) carries a nonzero DetailCategory but "
                + $"contains {exec.CommandCount} commands - a detail-category command must "
                + "emit alone.");
        }

        for (int idx = leadDirective + 1; idx < finish; idx++)
        {
            if (flow.Junctures[idx] != juncture)
            {
                throw new InvalidOperationException(
                    $"Merge run [{leadDirective}, {finish}) crosses a StrollPaintJuncture boundary "
                    + $"at command {idx} ({flow.Junctures[idx]} != {juncture}) - a merge across a "
                    + "stage boundary is forbidden by construction");
            }
            if (BinFor(flow.Keys[idx].Translucency) != bin)
            {
                throw new InvalidOperationException(
                    $"Merge run [{leadDirective}, {finish}) crosses a pipeline boundary at "
                    + $"command {idx} - a merge across a material-state boundary is "
                    + "forbidden by construction");
            }
            if (flow.Keys[idx].CullMode != prune)
            {
                throw new InvalidOperationException(
                    $"Merge run [{leadDirective}, {finish}) crosses a cull-mode boundary at "
                    + $"command {idx} - a merge across a material-state boundary is "
                    + "forbidden by construction");
            }
            if (flow.SpecificsCategories[idx] != 0)
            {
                throw new InvalidOperationException(
                    $"Merge run [{leadDirective}, {finish}) contains a detail-category "
                    + $"command at {idx} beyond a solo run - a detail-category command "
                    + "must emit alone");
            }
        }
    }

    internal (IGpuCycle Frame, IGpuSweepCoder Encoder) DemandStrollSubmission() =>
        (DemandRhiCycle(), _ambit!.DemandCoder());

    internal (int Width, int Height)? StrollAffixReach =>
        _ambit is null ? null : (_ambit.AffixWidth, _ambit.AffixHeight);

    private SequencedPaintFlow? _sequencedFlow;
    private List<SequencedCombineExec> _sequencedExecutions = [];
    private readonly List<SequencedCombineExec> _sequencedInstExecutions = [];
    private int[] _sequencedIndirectShifts = [];
    private int _sequencedReadiedTally;
    private IGpuCycle? _sequencedCycle;
    private Matrix4x4 _sequencedLensProj;
    private uint _sequencedXformBaseInst;
    private RhiSegment _sequencedInsts;
    private RhiSegment _sequencedLots;
    private RhiSegment _sequencedClipSockets;
    private RhiSegment _sequencedGlobalLamps;
    private RhiSegment _sequencedLampSets;
    private RhiSegment _sequencedInside;
    private RhiSegment _sequencedAlpha;
    private RhiSegment _sequencedPickIllumination;
    private RhiSegment _sequencedSpecificsBucket;
    private RhiSegment _sequencedDirectives;

    internal void PrepareOrderedStream(
        IGpuCycle cycle,
        SequencedPaintFlow flow,
        in Matrix4x4 lensProj,
        IReadOnlyList<int>? forcedBreaksAscending = null)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(flow);

        _sequencedFlow = flow;
        _sequencedCycle = cycle;
        _sequencedReadiedTally = 0;

        AssembleSequencedCombineExecutions(flow, forcedBreaksAscending, _sequencedExecutions);
        AssembleSequencedInstExecutions(flow, _sequencedExecutions, _sequencedInstExecutions);

        int tally = flow.Count;
        if (tally == 0)
            return;

        GlobalTriMeshBuffer? global = _triMeshBridge.TriMeshKeeper?.GlobalBuf;
        if (global is null || !TriMeshSrcPrimed())
            return;

        SecurePostponedAlphaCap(tally);
        SecureSequencedPruneMannerCap(tally);
        if (_sequencedIndirectShifts.Length <= tally)
            _sequencedIndirectShifts = new int[tally + 65];
        for (int idx = 0; idx < tally; idx++)
        {
            _stage.Write(idx, new InstFacts(
                flow.Transforms[idx],
                SubmissionOrder: 0,
                flow.ClipSockets[idx],
                flow.Lights[idx],
                flow.InsideFlags[idx],
                flow.SpecificsCategories[idx],
                flow.Alphas[idx],
                flow.PickLighting[idx]));
        }

        int paintTally = _sequencedInstExecutions.Count;
        for (int idx = 0; idx < paintTally; idx++)
        {
            SequencedCombineExec insts = _sequencedInstExecutions[idx];
            ClusterTag tag = flow.Keys[insts.FirstCommand];
            int finish = insts.FirstCommand + insts.CommandCount;
            for (int directive = insts.FirstCommand; directive < finish; directive++)
                _sequencedIndirectShifts[directive] = idx;
            _lotBlob[idx] = BatchData.For(tag);
            _indirectDirectives[idx] = SequencedIndirectDirective(tag, insts);
            _sequencedPaintPruneManners[idx] = tag.CullMode;
        }
        _sequencedIndirectShifts[tally] = paintTally;

        _sequencedLensProj = lensProj;
        _sequencedInsts = EmitRealmXformSection(
            cycle, _stage.Models(tally), out uint xformBaseInst);
        _sequencedXformBaseInst = xformBaseInst;
        _sequencedLots = EmitLoopSection<BatchData>(cycle, _lotBlob.AsSpan(0, paintTally));
        _sequencedClipSockets = EmitLoopSection<uint>(cycle, _stage.ClipSockets(tally));
        int lampTally = SceneLightPacker.Pack(_ptCapture, ref _globalLampBlob);
        int pushTally = lampTally > 0 ? lampTally : 1;
        _sequencedGlobalLamps = EmitLoopSection<float>(
            cycle,
            _globalLampBlob.AsSpan(0, pushTally * SceneLightPacker.FloatsPerLamp));
        _sequencedLampSets = EmitLoopSection<int>(
            cycle, _stage.Lamps(tally));
        _sequencedInside = EmitLoopSection<uint>(cycle, _stage.Inside(tally));
        _sequencedAlpha = EmitLoopSection<float>(cycle, _stage.Opacities(tally));
        _sequencedPickIllumination = EmitLoopSection<Vector2>(
            cycle, _stage.PickIllumination(tally));
        _sequencedSpecificsBucket = EmitLoopSection<uint>(cycle, _stage.SpecificsBuckets(tally));
        GpuLoopAlloc directivesAlloc = EmitIndirectDirectives(
            cycle, _indirectDirectives.AsSpan(0, paintTally), xformBaseInst);
        _sequencedDirectives = new RhiSegment(
            directivesAlloc.Buffer,
            directivesAlloc.ShiftOctets,
            checked((uint)(paintTally * PaintDirectiveStride)));

        _sequencedReadiedTally = tally;
    }

    internal void DrawOrderedRange(IGpuSweepCoder coder, int firstCommand, int directiveTally)
    {
        ArgumentNullException.ThrowIfNull(coder);
        if (firstCommand < 0
            || directiveTally < 0
            || firstCommand > _sequencedReadiedTally - directiveTally)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstCommand),
                "The ordered draw range exceeds the payload the most recent "
                + "PrepareOrderedStream call uploaded");
        }
        if (directiveTally == 0)
            return;
        if (_sequencedDirectives.Buffer is null || _sequencedFlow is null)
            return;

        GlobalTriMeshBuffer? global = _triMeshBridge.TriMeshKeeper?.GlobalBuf;
        if (global is null)
            return;

        IGpuCycle cycle = _sequencedCycle
            ?? throw new InvalidOperationException(
                "DrawOrderedRange has no frame to bind clip-region/"
                + "scene-lighting sections against - PrepareOrderedStream must run first");
        TriMeshPipeGroup pipes = PipesFor(
            coder,
            cycle,
            out DirectionalShadeFrameWiring shadeMapping);
        var pushConstants = new GpuShoveConstants
        {
            LensMirror = _sequencedLensProj,
            PaintIdentShift = 0,
            IlluminationManner = 0,
            RasterizePass = 0,
            LampDiag = DrawTelemetry.LampDiagManner,
            TextureIndexA = 0,
            TextureOrdinalB = _sequencedXformBaseInst,
            ParamA = 0f,
            ParameterB = 0f,
        };

        {
            AttachPipeWithTriMesh(coder, pipes.Opaque, global);
            coder.AssignPushConstants(in pushConstants);
            AttachDirectedShadeRecipient(coder, in shadeMapping);
            AttachSection(coder, GpuBindingModel.DepotInsts, _sequencedInsts);
            AttachSection(coder, GpuBindingModel.DepotLots, _sequencedLots);
            AttachSection(coder, GpuBindingModel.DepotClipSockets, _sequencedClipSockets);
            AttachSection(coder, GpuBindingModel.DepotGlobalLamps, _sequencedGlobalLamps);
            AttachSection(coder, GpuBindingModel.DepotInstLampSets, _sequencedLampSets);
            AttachSection(coder, GpuBindingModel.DepotInstInside, _sequencedInside);
            AttachSection(coder, GpuBindingModel.StorageInstanceAlpha, _sequencedAlpha);
            AttachSection(
                coder, GpuBindingModel.DepotInstPickIllumination, _sequencedPickIllumination);
            AttachSection(
                coder, GpuBindingModel.DepotInstSpecificsBucket, _sequencedSpecificsBucket);
            MacAC.Client.Graphics.RealmFrameSectionWiring.AttachClipZones(
                coder, _ambit!.Sections, cycle);
            MacAC.Client.Graphics.RealmFrameSectionWiring.AttachTableauIllumination(
                coder, _ambit!.Sections, cycle);
        }

        IClientGpuBuffer directiveBuf = _sequencedDirectives.Buffer!;
        uint directiveBase = _sequencedDirectives.OffsetBytes;
        int spanFinish = firstCommand + directiveTally;
        bool specificsTurnedOn = CanonDetailTextureContract.ShouldRasterize(
                _structureSpecificsTurnedOn(),
                _structureSpecifics)
            && _structureSpecifics.Tiling != 0f;

        foreach (SequencedCombineExec exec in _sequencedExecutions)
        {
            int execFinish = exec.FirstCommand + exec.CommandCount;
            if (execFinish <= firstCommand)
                continue;
            if (exec.FirstCommand >= spanFinish)
                break;

            if (exec.FirstCommand < firstCommand || execFinish > spanFinish)
            {
                throw new InvalidOperationException(
                    $"DrawOrderedRange [{firstCommand}, {spanFinish}) straddles merge run "
                    + $"[{exec.FirstCommand}, {execFinish}) - a range boundary must coincide with "
                    + "a run boundary by construction (PrepareOrderedStream's "
                    + "forcedBreaksAscending should have forced a break here)");
            }

            VetCombineExec(_sequencedFlow, exec);

            PipeBin bin = BinFor(_sequencedFlow.Keys[exec.FirstCommand].Translucency);
            ClusterTag tag = _sequencedFlow.Keys[exec.FirstCommand];
            bool hasSpecifics = specificsTurnedOn
                && _sequencedFlow.SpecificsCategories[exec.FirstCommand] != 0u;
            IGpuPipe binPipe = PipeForBin(pipes, bin);
            IGpuPipe pipe = hasSpecifics
                ? PipeForMatl(pipes, tag.MaterialState, binPipe)
                : binPipe;
            pushConstants.RasterizePass = bin == PipeBin.Opaque ? 0 : 1;
            if (hasSpecifics)
                ArmStructureSpecifics(ref pushConstants, tag.MaterialState);
            else
                WipeSpecificsPushConstants(ref pushConstants);

            AttachPipeWithTriMesh(coder, pipe, global);
            PaintIndirectSpanRhi(
                coder, ref pushConstants, directiveBuf, directiveBase,
                _sequencedIndirectShifts[exec.FirstCommand],
                _sequencedIndirectShifts[execFinish] - _sequencedIndirectShifts[exec.FirstCommand],
                _sequencedPaintPruneManners);
        }

        WipeSpecificsPushConstants(ref pushConstants);
        coder.AssignPushConstants(in pushConstants);
    }

    private void SecureSequencedPruneMannerCap(int tally)
    {
        if (_sequencedPaintPruneManners.Length < tally)
            _sequencedPaintPruneManners = new FaceCulling[tally + 64];
    }
}
