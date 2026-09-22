using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

public sealed unsafe partial class MotePainter
{
    private void PaintSequencedRhi(IClientCamera cam)
    {
        MoteSubmissionOrdering.Sort(_submissionTemp);
        var global = _triMeshBridge?.TriMeshKeeper?.GlobalBuf;
        Matrix4x4 lensProj = cam.View * cam.Projection;
        var coder = _ambit!.DemandCoder();
        IGpuCycle cycle = DemandRhiCycle();

        for (int idx = 0; idx < _submissionTemp.Count;)
        {
            var submission = _submissionTemp[idx];
            if (submission.Kind == MoteSubmissionKind.Billboard)
            {
                LotTag tag = _paintRosterTemp[submission.DrawIndex].Key;
                _execTemp.Clear();
                do
                {
                    _execTemp.Add(_paintRosterTemp[submission.DrawIndex].Instance);
                    ++idx;
                    if (idx >= _submissionTemp.Count)
                        break;
                    submission = _submissionTemp[idx];
                }
                while (submission.Kind == MoteSubmissionKind.Billboard
                    && _paintRosterTemp[submission.DrawIndex].Key == tag);

                PaintInstsRhi(coder, cycle, _execTemp, lensProj, tag.Additive);
                continue;
            }

            if (!TriMeshMotesOnHand || global is null)
            {
                ++idx;
                continue;
            }

            var triMeshPaint = _meshDrawListScratch[submission.DrawIndex];
            var triMeshTag = triMeshPaint.Key;
            var lot = triMeshPaint.Batch;
            _triMeshExecTemp.Clear();
            do
            {
                _triMeshExecTemp.Add(_meshDrawListScratch[submission.DrawIndex].Instance);
                ++idx;
                if (idx >= _submissionTemp.Count)
                    break;
                submission = _submissionTemp[idx];
            }
            while (submission.Kind == MoteSubmissionKind.Mesh
                && _meshDrawListScratch[submission.DrawIndex].Key == triMeshTag);

            int neededInsts = _triMeshExecTemp.Count;
            if (_triMeshInstTemp.Length < neededInsts)
                _triMeshInstTemp = new MeshMoteGpuInstance[neededInsts + 256];
            for (int inst = 0; inst < _triMeshExecTemp.Count; ++inst)
            {
                EmitTriMeshGpuInst(
                    ref _triMeshInstTemp[inst],
                    _triMeshExecTemp[inst]);
            }

            var insts = EmitVertLoop<MeshMoteGpuInstance>(
                cycle,
                _triMeshInstTemp.AsSpan(0, neededInsts));
            PaintTriMeshLotRhi(
                coder,
                cycle,
                global,
                lot,
                lensProj,
                insts.Buffer,
                insts.ShiftOctets,
                (uint)_triMeshExecTemp.Count,
                leadInst: 0,
                solidZDepthPhase: false);
        }
    }

    private void PaintImmediateMoteSubmissionRhi(
        Matrix4x4 lensProj,
        MoteSubmissionKind sort,
        int paintOrdinal,
        bool solidZDepthPhase)
    {
        var coder = _ambit!.DemandCoder();
        IGpuCycle cycle = DemandRhiCycle();

        if (sort == MoteSubmissionKind.Billboard)
        {
            MoteDraw paint = _paintRosterTemp[paintOrdinal];
            _execTemp.Clear();
            _execTemp.Add(paint.Instance);
            PaintInstsRhi(coder, cycle, _execTemp, lensProj, paint.Key.Additive);
            return;
        }

        var global = _triMeshBridge?.TriMeshKeeper?.GlobalBuf;
        if (!TriMeshMotesOnHand || global is null)
            return;

        var triMeshPaint = _meshDrawListScratch[paintOrdinal];
        if (_triMeshInstTemp.Length < 1)
            _triMeshInstTemp = new MeshMoteGpuInstance[256];
        EmitTriMeshGpuInst(ref _triMeshInstTemp[0], triMeshPaint.Instance);
        var insts = EmitVertLoop<MeshMoteGpuInstance>(
            cycle,
            _triMeshInstTemp.AsSpan(0, 1));
        PaintTriMeshLotRhi(
            coder,
            cycle,
            global,
            triMeshPaint.Batch,
            lensProj,
            insts.Buffer,
            insts.ShiftOctets,
            instTally: 1,
            leadInst: 0,
            solidZDepthPhase: solidZDepthPhase);
    }

    private void PaintInstsRhi(
        IGpuSweepCoder coder,
        IGpuCycle cycle,
        List<MoteInstance> insts,
        Matrix4x4 lensProj,
        bool additive)
    {
        if (insts.Count is 0)
            return;

        if (_instTemp.Length < insts.Count)
            _instTemp = new BillboardGpuInst[insts.Count + 256];
        for (int idx = 0; idx < insts.Count; ++idx)
            EmitBillboardGpuInst(ref _instTemp[idx], insts[idx]);

        var loop = EmitVertLoop<BillboardGpuInst>(
            cycle,
            _instTemp.AsSpan(0, insts.Count));
        AttachBillboardPipe(
            coder,
            cycle,
            lensProj,
            additive,
            loop.Buffer,
            loop.ShiftOctets);
        coder.PaintIndexed(
            (uint)QuadOrdinals.Length,
            (uint)insts.Count,
            0,
            0,
            0);
    }

    private void PaintTriMeshLotRhi(
        IGpuSweepCoder coder,
        IGpuCycle cycle,
        GlobalTriMeshBuffer global,
        ThingRasterizeLot lot,
        Matrix4x4 lensProj,
        IClientGpuBuffer instBuf,
        uint instShiftOctets,
        uint instTally,
        uint leadInst,
        bool solidZDepthPhase = false)
    {
        if (instTally is 0)
            return;

        coder.BindPipeline(PipelineForMeshBlend(LocateTriMeshBlend(lot), solidZDepthPhase));
        coder.AssignPushConstants(new GpuShoveConstants
        {
            LensMirror = lensProj,
            PaintIdentShift = 0,
            IlluminationManner = 0,
            RasterizePass = 0,
            LampDiag = 0,
            TextureIndexA = lot.TextureSlot.Index,
            TextureOrdinalB = 0,
            ParamA = lot.TextureIndex,
            ParameterB = 0f,
        });
        ImposeTriMeshPruneMannerRhi(coder, lot.CullMode);
        coder.AttachVertBuf(
            0,
            global.VertVault ?? throw new InvalidOperationException(
                "The shared mesh arena has no vertex store"),
            0);
        coder.AttachVertBuf(1, instBuf, instShiftOctets);
        coder.AttachOrdinalBuf(
            global.OrdinalVault ?? throw new InvalidOperationException(
                "The shared mesh arena has no index store"),
            0,
            GpuOrdinalKind.UInt16);
        RealmFrameSectionWiring.AttachClipZones(
            coder,
            _ambit!.Sections,
            cycle);
        coder.PaintIndexed(
            (uint)lot.OrdinalTally,
            instTally,
            (uint)lot.LeadIdx,
            (int)lot.BaseVertex,
            leadInst);
    }

    private void PaintReadiedAlphaLotRhi(int leadReadiedPaint, int paintTally)
    {
        var global = _triMeshBridge?.TriMeshKeeper?.GlobalBuf;
        var coder = _ambit!.DemandCoder();

        int idx = leadReadiedPaint;
        int readiedFinish = leadReadiedPaint + paintTally;
        while (idx < readiedFinish)
        {
            var postponed = _readiedAlpha[idx];
            if (postponed.Kind == MoteSubmissionKind.Billboard)
            {
                LotTag tag = postponed.Billboard.Key;
                Matrix4x4 lensProj = postponed.LensMirror;
                uint baseInst = _readiedInstShifts[idx];
                int execBegin = idx;
                do
                {
                    ++idx;
                    if (idx >= readiedFinish)
                        break;
                    postponed = _readiedAlpha[idx];
                }
                while (postponed.Kind == MoteSubmissionKind.Billboard
                       && postponed.Billboard.Key == tag
                       && postponed.LensMirror == lensProj);

                if (_readiedBillboardInsts.Buffer is { } billboards)
                {
                    AttachBillboardPipe(
                        coder,
                        DemandRhiCycle(),
                        lensProj,
                        tag.Additive,
                        billboards,
                        _readiedBillboardInsts.OffsetBytes);
                    coder.PaintIndexed(
                        (uint)QuadOrdinals.Length,
                        (uint)(idx - execBegin),
                        0,
                        0,
                        baseInst);
                }

                continue;
            }

            if (!TriMeshMotesOnHand || global is null)
            {
                ++idx;
                continue;
            }

            var triMeshTag = postponed.Mesh.Key;
            var lot = postponed.Mesh.Batch;
            Matrix4x4 triMeshLensProj = postponed.LensMirror;
            uint triMeshBaseInst = _readiedInstShifts[idx];
            int triMeshExecBegin = idx;
            do
            {
                ++idx;
                if (idx >= readiedFinish)
                    break;
                postponed = _readiedAlpha[idx];
            }
            while (postponed.Kind == MoteSubmissionKind.Mesh
                   && postponed.Mesh.Key == triMeshTag
                   && postponed.LensMirror == triMeshLensProj);

            if (_readiedTriMeshInsts.Buffer is { } triMeshInsts)
            {
                PaintTriMeshLotRhi(
                    coder,
                    DemandRhiCycle(),
                    global,
                    lot,
                    triMeshLensProj,
                    triMeshInsts,
                    _readiedTriMeshInsts.OffsetBytes,
                    (uint)(idx - triMeshExecBegin),
                    triMeshBaseInst,
                    solidZDepthPhase: false);
            }
        }
    }
}
