using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Geometry;

namespace MacAC.Client.Graphics.Batching;

public sealed unsafe partial class RealmPaintRouter
{
    private void PaintReadiedAlphaLotRhi(
        GlobalTriMeshBuffer triMesh,
        int firstPreparedDraw,
        int paintTally)
    {
        if (_alphaDirectives.Buffer is null)
            return;

        var coder = _ambit!.DemandCoder();
        IGpuCycle cycle = DemandRhiCycle();
        var pushConstants = AttachAlphaPaintPhase(
            coder, cycle, triMesh, _postponedAlphaLensProj, out TriMeshPipeGroup pipes);

        if (firstPreparedDraw < 0
            || paintTally < 0
            || firstPreparedDraw > _readiedAlphaInstTally - paintTally)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstPreparedDraw),
                "The prepared-alpha draw range exceeds its uploaded instance/category payload");
        }

        int execBegin = firstPreparedDraw;
        int readiedFinish = firstPreparedDraw + paintTally;
        while (execBegin < readiedFinish)
        {
            var blend = _postponedAlphaSorts[execBegin];
            int execFinish = execBegin + 1;
            while (execFinish < readiedFinish && _postponedAlphaSorts[execFinish] == blend)
                ++execFinish;

            AttachPipeWithTriMesh(coder, PipeForBlend(pipes, blend), triMesh);
            coder.AssignPushConstants(in pushConstants);
            PaintIndirectSpanRhi(
                coder,
                ref pushConstants,
                _alphaDirectives.Buffer!,
                _alphaDirectives.OffsetBytes,
                execBegin,
                execFinish - execBegin);
            execBegin = execFinish;
        }
    }

    private void PaintImmediateAlphaInstRhi(
        GlobalTriMeshBuffer triMesh,
        SeeThroughKind blend,
        CanonSurfaceMaterialState matlPhase,
        Matrix4x4 lensProj)
    {
        if (_alphaDirectives.Buffer is null)
            return;

        var coder = _ambit!.DemandCoder();
        IGpuCycle cycle = DemandRhiCycle();
        var pushConstants = AttachAlphaPaintPhase(
            coder, cycle, triMesh, lensProj, out TriMeshPipeGroup pipes);

        AttachPipeWithTriMesh(
            coder,
            PipeForMatl(pipes, matlPhase, pipes.Opaque),
            triMesh);
        coder.AssignPushConstants(in pushConstants);
        ArmStructureSpecifics(ref pushConstants, matlPhase);
        PaintIndirectSpanRhi(
            coder,
            ref pushConstants,
            _alphaDirectives.Buffer!,
            _alphaDirectives.OffsetBytes,
            beginDirective: 0,
            directiveTally: 1);
        WipeSpecificsPushConstants(ref pushConstants);
        coder.AssignPushConstants(in pushConstants);
    }

    private void PaintImmediateSeeThruRhi(
        IGpuSweepCoder coder,
        GlobalTriMeshBuffer triMesh,
        TriMeshPipeGroup pipes,
        ref GpuShoveConstants pushConstants,
        IClientGpuBuffer directiveBuf,
        uint directiveBase,
        ReadOnlySpan<uint> consumedSpecificsBuckets,
        bool specificsTurnedOn)
    {
        int directive = _solidPaintTally;
        int finish = directive + _seeThruPaintTally;
        while (directive < finish)
        {
            var matlPhase =
                _clusterFeedTemp[directive].MaterialState;
            bool hasSpecifics = specificsTurnedOn
                && DirectiveContainsSpecificsBucket(
                    _indirectDirectives[directive],
                    consumedSpecificsBuckets);
            int execFinish = directive + 1;
            while (execFinish < finish
                && !hasSpecifics
                && (!specificsTurnedOn
                    || !DirectiveContainsSpecificsBucket(
                        _indirectDirectives[execFinish],
                        consumedSpecificsBuckets)))
            {
                ++execFinish;
            }

            AttachPipeWithTriMesh(
                coder,
                hasSpecifics
                    ? PipeForMatl(pipes, matlPhase, pipes.Opaque)
                    : pipes.AlphaBlend,
                triMesh);
            if (hasSpecifics)
                ArmStructureSpecifics(ref pushConstants, matlPhase);
            else
                WipeSpecificsPushConstants(ref pushConstants);
            PaintIndirectSpanRhi(
                coder,
                ref pushConstants,
                directiveBuf,
                directiveBase,
                directive,
                execFinish - directive);
            directive = execFinish;
        }
        WipeSpecificsPushConstants(ref pushConstants);
        coder.AssignPushConstants(in pushConstants);
    }

    private void PaintSpecificsAwareSpanRhi(
        IGpuSweepCoder coder,
        GlobalTriMeshBuffer triMesh,
        TriMeshPipeGroup pipes,
        IGpuPipe solidPipe,
        ref GpuShoveConstants pushConstants,
        IClientGpuBuffer directiveBuf,
        uint directiveBase,
        int leadDirective,
        int directiveTally,
        ReadOnlySpan<uint> specificsBuckets,
        bool specificsTurnedOn)
    {
        int directive = leadDirective;
        int finish = leadDirective + directiveTally;
        while (directive < finish)
        {
            bool hasSpecifics = specificsTurnedOn
                && DirectiveContainsSpecificsBucket(
                    _indirectDirectives[directive], specificsBuckets);
            int execFinish = directive + 1;
            while (execFinish < finish
                && !hasSpecifics
                && (!specificsTurnedOn
                    || !DirectiveContainsSpecificsBucket(
                        _indirectDirectives[execFinish], specificsBuckets)))
            {
                ++execFinish;
            }

            if (hasSpecifics)
            {
                var matlPhase =
                    _clusterFeedTemp[directive].MaterialState;
                AttachPipeWithTriMesh(
                    coder,
                    PipeForMatl(pipes, matlPhase, solidPipe),
                    triMesh);
                ArmStructureSpecifics(ref pushConstants, matlPhase);
            }
            else
            {
                AttachPipeWithTriMesh(coder, solidPipe, triMesh);
                WipeSpecificsPushConstants(ref pushConstants);
            }
            PaintIndirectSpanRhi(
                coder,
                ref pushConstants,
                directiveBuf,
                directiveBase,
                directive,
                execFinish - directive);
            directive = execFinish;
        }
        WipeSpecificsPushConstants(ref pushConstants);
        coder.AssignPushConstants(in pushConstants);
    }

    private void PaintIndirectSpanRhi(
        IGpuSweepCoder coder,
        ref GpuShoveConstants pushConstants,
        IClientGpuBuffer directiveBuf,
        uint directiveBaseShiftOctets,
        int beginDirective,
        int directiveTally,
        FaceCulling[]? pruneManners = null)
    {
        FaceCulling[] manners = pruneManners ?? _drawCullModes;
        int finish = beginDirective + directiveTally;
        int directive = beginDirective;
        while (directive < finish)
        {
            FaceCulling pruneManner = manners[directive];
            ImposePruneMannerRhi(coder, pruneManner);

            int execTally = 1;
            while (directive + execTally < finish && manners[directive + execTally] == pruneManner)
                ++execTally;

            pushConstants.PaintIdentShift = directive;
            coder.AssignPushConstants(in pushConstants);
            coder.MultiPaintIndexedIndirect(
                directiveBuf,
                directiveBaseShiftOctets + (uint)(directive * PaintDirectiveStride),
                (uint)execTally,
                (uint)PaintDirectiveStride);

            directive += execTally;
        }
    }
}
