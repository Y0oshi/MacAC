using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MacAC.Dat;
using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Geometry;
using RuntimeParticleEmitter = MacAC.Mechanics.Effects.MoteSpout;

namespace MacAC.Client.Graphics;

public sealed unsafe partial class MotePainter
{
    internal (int SetCount, long CapacityBytes) DynamicBufTelemetry => (0, 0);

    internal long AlphaTempAllowanceOctets => _alphaTempRule.AllowanceBytes;

    internal long KeptAlphaTempOctets
    {
        get
        {
            return checked(
        (long)_deferredAlpha.Capacity * Unsafe.SizeOf<DeferredMoteDraw>()
        + (long)_readiedAlpha.Length * Unsafe.SizeOf<DeferredMoteDraw>()
        + (long)_readiedInstShifts.Length * sizeof(uint)
        + (long)_readiedChamberAlphaTemp.Capacity
            * Unsafe.SizeOf<PreparedMoteAlphaSubmission>());
        }
    }

    internal (int Count, int Capacity, long RetainedBytes)
        ReadiedChamberAlphaTempTelemetry
    {
        get
        {
            return (
            _readiedChamberAlphaTemp.Count,
            _readiedChamberAlphaTemp.Capacity,
            checked((long)_readiedChamberAlphaTemp.Capacity
                * Unsafe.SizeOf<PreparedMoteAlphaSubmission>())
        );
        }
    }

    public void BeginFrame(int cycleSocket)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cycleSocket);

        _dynamicCycleBegun = true;
        _triMeshPullAskedThisCycle.Clear();
        _spoutRetirements.ReattemptQueued();
        _textures?.PulseMoteTextureStash();
    }

    public void Draw(
        IClientCamera cam,
        Vector3 camRealmSpot,
        ParticleDrawPass rasterizePass = ParticleDrawPass.Scene,
        Func<MacAC.Mechanics.Effects.MoteSpout, bool>? spoutSift = null,
        uint clipSocket = 0)
    {
        if (cam is null)
            return;

        Matrix4x4.Invert(cam.View, out var invLens);
        Vector3 camRight = Vector3.Normalize(new Vector3(invLens.M11, invLens.M12, invLens.M13));
        Vector3 camUp = Vector3.Normalize(new Vector3(invLens.M21, invLens.M22, invLens.M23));
        AssemblePaintRosters(
            camRealmSpot,
            rasterizePass,
            camRight,
            camUp,
            spoutSift,
            scopedSpouts: null,
            clipSocket);
        CompletePaint(cam, rasterizePass);
    }

    public void PaintForHolders(
        IClientCamera cam,
        Vector3 camRealmSpot,
        ParticleDrawPass rasterizePass,
        IReadOnlySet<uint> affixedHolderIdents,
        bool includeUnattached = false,
        IReadOnlySet<uint>? excludedAffixedHolderIdents = null,
        uint clipSocket = 0,
        LooseEmitterCellScope unattachedChamberAmbit = LooseEmitterCellScope.Any)
    {
        if (cam is null)
            return;

        _motes.DuplicateRenderableSpoutsForHolders(
            rasterizePass,
            affixedHolderIdents,
            includeUnattached,
            _scopedSpoutTemp,
            excludedAffixedHolderIdents,
            unattachedChamberAmbit);
        Matrix4x4.Invert(cam.View, out Matrix4x4 invLens);
        Vector3 camRight = Vector3.Normalize(new Vector3(invLens.M11, invLens.M12, invLens.M13));
        Vector3 camUp = Vector3.Normalize(new Vector3(invLens.M21, invLens.M22, invLens.M23));
        AssemblePaintRosters(
            camRealmSpot,
            rasterizePass,
            camRight,
            camUp,
            spoutSift: null,
            _scopedSpoutTemp,
            clipSocket);
        CompletePaint(cam, rasterizePass);
    }

    public void PaintForChamber(
        IClientCamera cam,
        Vector3 camRealmSpot,
        ParticleDrawPass rasterizePass,
        uint chamberIdent,
        uint clipSocket = 0)
    {
        if (cam is null)
            return;

        _motes.DuplicateRenderableSpoutsInChamber(rasterizePass, chamberIdent, _scopedSpoutTemp);
        if (_scopedSpoutTemp.Count is 0)
            return;
        Matrix4x4.Invert(cam.View, out Matrix4x4 invLens);
        Vector3 camRight = Vector3.Normalize(new Vector3(invLens.M11, invLens.M12, invLens.M13));
        Vector3 camUp = Vector3.Normalize(new Vector3(invLens.M21, invLens.M22, invLens.M23));
        AssemblePaintRosters(
            camRealmSpot,
            rasterizePass,
            camRight,
            camUp,
            spoutSift: null,
            _scopedSpoutTemp,
            clipSocket);
        CompletePaint(cam, rasterizePass);
    }

    public void Dispose()
    {
        if (_destroyed || _disposing) return;
        _disposing = true;
        try
        {
            if (_teardownAssetList is null)
            {
                var releases = new List<(string Name, Action Release)>();
                AssembleTeardownReleases(releases);
                _teardownAssetList = new RetryableAssetFreeRegister(releases);
            }

            var attempt = _teardownAssetList.Advance();
            if (!_teardownAssetList.IsComplete)
            {
                throw attempt.ToException(
                    "One or more particle renderer resources could not be released.");
            }

            ConcludeTeardown();
            _teardownAssetList = null;
            _destroyed = true;

            if (attempt.HasMisses)
            {
                throw attempt.ToException(
                    "Particle renderer resources released with exceptional committed outcomes.");
            }
        }
        finally
        {
            _disposing = false;
        }
    }

    internal static CanonAlphaMeshDecision CourseMoteSubmission(
        MoteSubmissionKind sort, SeeThroughKind triMeshSeeThrough, uint triMeshTintArgb)
    {
        bool isTriMesh = sort == MoteSubmissionKind.Mesh;
        byte bitmask = sort == MoteSubmissionKind.Billboard
            ? CanonAlphaMeshRouter.BitmaskAlphaClan
            : CanonAlphaMeshRouter.ConcealFromSeeThroughSort(triMeshSeeThrough);
        bool matlHasAlpha = isTriMesh && ((triMeshTintArgb >> 24) & 0xFFu) != 0xFFu;

        return CanonAlphaMeshRouter.Course(
            currentlyDrawingHeavens: false,
            delayBitmask: CanonAlphaMeshRouter.DefaultDelayBitmask,
            specificsCanvasEngaged: false,
            multiPassAlpha: false,
            subsetBitmask: bitmask,
            matlHasAlpha: matlHasAlpha);
    }

    internal static CanonAlphaMeshDecision DeferToCanonAlphaFifo(
        MoteSubmissionKind sort,
        SeeThroughKind triMeshSeeThrough,
        uint triMeshTintArgb,
        CanonAlphaFifo fifo,
        ICanonAlphaDrawSource src,
        ReserveDeferredParticleDraw allocatePostponed,
        DrawImmediateParticle paintImmediate,
        Matrix4x4 lensProj,
        int paintOrdinal)
    {
        var decision = CourseMoteSubmission(
            sort,
            triMeshSeeThrough,
            triMeshTintArgb);
        switch (decision.Action)
        {
            case CanonAlphaMeshAction.Append:
                {
                    int ticket = allocatePostponed();
                    fifo.TryAffix(decision.List, src, ticket, decision.OverrideClipmap);
                    break;
                }
            case CanonAlphaMeshAction.Immediate:
                paintImmediate(lensProj, sort, paintOrdinal, opaqueDepthState: true);
                break;
            case CanonAlphaMeshAction.AppendClipAndImmediate:
                {
                    int ticket = allocatePostponed();
                    fifo.TryAffix(decision.List, src, ticket, decision.OverrideClipmap);
                    paintImmediate(lensProj, sort, paintOrdinal, opaqueDepthState: false);
                    break;
                }
        }
        return decision;
    }

    internal int EarmarkReadiedRelayPostponedMote(
        MoteSubmissionKind sort,
        int paintOrdinal,
        Matrix4x4 lensProj)
    {
        DeferredMoteDraw postponed = sort == MoteSubmissionKind.Billboard
            ? new DeferredMoteDraw(
                sort,
                _paintRosterTemp[paintOrdinal],
                default,
                lensProj)
            : new DeferredMoteDraw(
                sort,
                default,
                _meshDrawListScratch[paintOrdinal],
                lensProj);
        int ticket = _deferredAlpha.Count;
        _deferredAlpha.Add(postponed);
        return ticket;
    }

    internal void RevertReadiedRelayPostponedMote(int ticket)
    {
        int rear = _deferredAlpha.Count - 1;
        if (ticket != rear)
        {
            throw new InvalidOperationException(
                $"Prepared particle rollback must target tail token {rear}, not {ticket}.");
        }

        _deferredAlpha.RemoveAt(rear);
    }

    internal ReadOnlySpan<PreparedMoteAlphaSubmission> ReadyForChamberAlpha(
        IClientCamera cam,
        Vector3 camRealmSpot,
        ParticleDrawPass rasterizePass,
        uint chamberIdent,
        uint clipSocket = 0)
    {
        _readiedChamberAlphaTemp.Clear();
        if (cam is null)
            return CollectionsMarshal.AsSpan(_readiedChamberAlphaTemp);

        _motes.DuplicateRenderableSpoutsInChamber(rasterizePass, chamberIdent, _scopedSpoutTemp);
        if (_scopedSpoutTemp.Count is 0)
            return CollectionsMarshal.AsSpan(_readiedChamberAlphaTemp);

        Matrix4x4.Invert(cam.View, out Matrix4x4 invLens);
        Vector3 camRight = Vector3.Normalize(new Vector3(invLens.M11, invLens.M12, invLens.M13));
        Vector3 camUp = Vector3.Normalize(new Vector3(invLens.M21, invLens.M22, invLens.M23));
        AssemblePaintRosters(
            camRealmSpot,
            rasterizePass,
            camRight,
            camUp,
            spoutSift: null,
            _scopedSpoutTemp,
            clipSocket);

        if (_submissionTemp.Count is 0)
            return CollectionsMarshal.AsSpan(_readiedChamberAlphaTemp);

        bool defers = rasterizePass == ParticleDrawPass.Scene && _alphaFifo?.IsCollecting == true;
        if (!defers)
        {
            PaintSequenced(cam);
            return CollectionsMarshal.AsSpan(_readiedChamberAlphaTemp);
        }

        MoteSubmissionOrdering.Sort(_submissionTemp);
        Matrix4x4 lensProj = cam.View * cam.Projection;
        var fifo = _alphaFifo!;
        int keptClipTally = 0;
        int keptAlphaTally = 0;
        for (int idx = 0; idx < _submissionTemp.Count; ++idx)
        {
            var submission = _submissionTemp[idx];
            SeeThroughKind seeThrough = submission.Kind == MoteSubmissionKind.Mesh
                ? _meshDrawListScratch[submission.DrawIndex].Batch.Translucency
                : default;
            uint tintArgb = submission.Kind == MoteSubmissionKind.Mesh
                ? _meshDrawListScratch[submission.DrawIndex].Instance.ColorArgb
                : default;
            var decision = CourseMoteSubmission(
                submission.Kind, seeThrough, tintArgb);
            var acts = LocateReadiedChamberAlphaActs(
                decision,
                ref keptClipTally,
                ref keptAlphaTally);

            if (acts.Retain)
            {
                _readiedChamberAlphaTemp.Add(new PreparedMoteAlphaSubmission(
                    fifo,
                    decision.List,
                    _alphaSrc,
                    this,
                    submission.Kind,
                    submission.DrawIndex,
                    lensProj,
                    decision.OverrideClipmap,
                    submission.DistanceSq,
                    submission.Sequence));
            }

            if (acts.DrawImmediate)
            {
                _drawImmediateParticle(
                    lensProj,
                    submission.Kind,
                    submission.DrawIndex,
                    opaqueDepthState: decision.Action == CanonAlphaMeshAction.Immediate);
            }
        }

        return CollectionsMarshal.AsSpan(_readiedChamberAlphaTemp);
    }

    private void DeferToCanonAlphaFifo(IClientCamera cam)
    {
        var fifo = _alphaFifo!;
        Matrix4x4 lensProj = cam.View * cam.Projection;
        for (int idx = 0; idx < _submissionTemp.Count; ++idx)
        {
            var submission = _submissionTemp[idx];

            DeferredMoteDraw postponed = submission.Kind == MoteSubmissionKind.Billboard
                ? new DeferredMoteDraw(
                    submission.Kind,
                    _paintRosterTemp[submission.DrawIndex],
                    default,
                    lensProj)
                : new DeferredMoteDraw(
                    submission.Kind,
                    default,
                    _meshDrawListScratch[submission.DrawIndex],
                    lensProj);

            _relayPostponedMote = postponed;
            SeeThroughKind seeThrough = submission.Kind == MoteSubmissionKind.Mesh
                ? _meshDrawListScratch[submission.DrawIndex].Batch.Translucency
                : default;
            uint tintArgb = submission.Kind == MoteSubmissionKind.Mesh
                ? _meshDrawListScratch[submission.DrawIndex].Instance.ColorArgb
                : default;
            DeferToCanonAlphaFifo(
                submission.Kind,
                seeThrough,
                tintArgb,
                fifo,
                _alphaSrc,
                _allocatePostponedMotePaint,
                _drawImmediateParticle,
                lensProj,
                submission.DrawIndex);
        }
    }

    private int EarmarkRelayPostponedMote()
    {
        int ticket = _deferredAlpha.Count;
        _deferredAlpha.Add(_relayPostponedMote);
        return ticket;
    }

    private void AffixSpoutDraws(
        RuntimeParticleEmitter emitter,
        Vector3 camRealmSpot,
        Vector3 camRight,
        Vector3 camUp,
        uint clipSocket,
        ref int series)
    {
        var draws = _paintRosterTemp;
        MoteGfxInfo gfxDetails = default;
        bool gfxDetailsSettled = false;

        for (int index = 0; index < emitter.Particles.Length; ++index)
        {
            ref Mote particle = ref emitter.Particles[index];
            if (!particle.Alive)
                continue;
            Vector3 spot = particle.Position;
            uint gfxObjRefIdent = emitter.Desc.HwGfxObjId is not 0 ? emitter.Desc.HwGfxObjId : emitter.Desc.GfxObjId;
            if (gfxObjRefIdent is not 0
                && LocateGeoSort(gfxObjRefIdent) == CanonMoteGeometryKind.FullMesh
                && TryAffixTriMeshDraws(
                    emitter,
                    particle,
                    gfxObjRefIdent,
                    camRealmSpot,
                    clipSocket,
                    ref series))

                continue;

            if (!gfxDetailsSettled)
            {
                gfxDetails = LocateMoteGfxDetails(emitter);
                gfxDetailsSettled = true;
            }
            Quaternion facing = MoteFacing(emitter, particle);
            Vector3 authoredOrderPt = particle.Position
                + Vector3.Transform(gfxDetails.SortCenter * particle.Size, facing);
            float distanceSq = Vector3.DistanceSquared(
                authoredOrderPt,
                camRealmSpot);
            bool additive = gfxDetails.HasMaterial
                ? gfxDetails.Additive
                : (emitter.Desc.Flags & EmitterBits.Additive) != 0;
            LotTag tag = new LotTag(additive);
            Vector3 axisX;
            Vector3 axisY;
            Vector3 toBeholder = camRealmSpot - spot;
            float toBeholderLen = toBeholder.Length();
            if (gfxDetails.IsBillboard)
            {
                Vector3 xd;
                Vector3 yd;
                if (toBeholderLen > 1e-3f)
                {
                    (xd, yd) = CanonParticleFacing.OrientQuad(
                        2u,
                        Quaternion.Identity,
                        Vector3.UnitX,
                        Vector3.UnitY,
                        toBeholder / toBeholderLen,
                        camRight,
                        camUp);
                }
                else
                {
                    (xd, yd) = (camRight, camUp);
                }

                spot += (xd * gfxDetails.CenterOffset.X
                      + yd * gfxDetails.CenterOffset.Z) * particle.Size;
                axisX = xd * (gfxDetails.Size.X * particle.Size);
                axisY = yd * (gfxDetails.Size.Y * particle.Size);
            }
            else
            {
                if (CanonParticleFacing.Faces(gfxDetails.DegradeMode)
                    && toBeholderLen > 1e-3f)
                {
                    (Vector3 xd, Vector3 yd) = CanonParticleFacing.OrientQuad(
                        gfxDetails.DegradeMode,
                        facing,
                        gfxDetails.AxisX,
                        gfxDetails.AxisY,
                        toBeholder / toBeholderLen,
                        camRight,
                        camUp);
                    Vector3 ownNorm = Vector3.Cross(gfxDetails.AxisX, gfxDetails.AxisY);
                    var spunNorm = Vector3.Cross(xd, yd);
                    if (spunNorm.LengthSquared() > 1e-10f)
                        spunNorm = Vector3.Normalize(spunNorm);
                    Vector3 c = gfxDetails.CenterOffset;
                    spot += (xd * Vector3.Dot(c, gfxDetails.AxisX)
                          + yd * Vector3.Dot(c, gfxDetails.AxisY)
                          + spunNorm * Vector3.Dot(c, ownNorm)) * particle.Size;
                    axisX = xd * (gfxDetails.Size.X * particle.Size);
                    axisY = yd * (gfxDetails.Size.Y * particle.Size);
                }
                else
                {
                    spot += Vector3.Transform(gfxDetails.CenterOffset * particle.Size, facing);
                    axisX = Vector3.Transform(gfxDetails.AxisX, facing) * (gfxDetails.Size.X * particle.Size);
                    axisY = Vector3.Transform(gfxDetails.AxisY, facing) * (gfxDetails.Size.Y * particle.Size);
                }
            }

            int paintOrdinal = draws.Count;
            draws.Add(new MoteDraw(
                tag,
                new MoteInstance(
                    spot,
                    axisX,
                    axisY,
                    particle.ColorArgb,
                    gfxDetails.TextureSlot,
                    distanceSq,
                    clipSocket)));
            _submissionTemp.Add(new MoteSubmission(
                MoteSubmissionKind.Billboard,
                paintOrdinal,
                distanceSq,
                series++));
        }
    }

    private MoteGfxInfo AuthoredMoteGfxDetails(
        PartMesh gfx,
        MacAC.Client.Graphics.Gpu.GpuTextureSlot texture,
        bool additive,
        bool hasMatl,
        uint canvasIdent,
        uint downgradeManner)
    {
        if (gfx.Vertices.ByIndex.Count is 0)
            return MoteGfxInfo.Billboard(
                texture,
                Vector2.One,
                Vector3.Zero,
                gfx.SortCenter,
                additive,
                hasMatl,
                canvasIdent);

        var lower = new Vector3(float.PositiveInfinity);
        var upper = new Vector3(float.NegativeInfinity);
        foreach (var (_, vertex) in gfx.Vertices.ByIndex)
        {
            lower = Vector3.Min(lower, vertex.Position);
            upper = Vector3.Max(upper, vertex.Position);
        }

        Vector3 dims = upper - lower;
        Vector3 middle = (lower + upper) * 0.5f;
        if (IsPtSprite(gfx))
        {
            float sx = BackupMoteReach(dims.X) * 0.9f;
            float sy = BackupMoteReach(dims.Z) * 0.9f;
            return MoteGfxInfo.Billboard(
                texture,
                new Vector2(sx, sy),
                middle,
                gfx.SortCenter,
                additive,
                hasMatl,
                canvasIdent);
        }

        Vector3 axisX;
        Vector3 axisY;
        Vector2 planeDims;
        if (dims.Y > dims.X && dims.Y > dims.Z)
        {
            if (dims.X > dims.Z)
            {
                axisX = Vector3.UnitX;
                axisY = Vector3.UnitY;
                planeDims = new Vector2(dims.X, dims.Y);
            }
            else
            {
                axisX = Vector3.UnitY;
                axisY = Vector3.UnitZ;
                planeDims = new Vector2(dims.Y, dims.Z);
            }
        }
        else if (dims.X > dims.Y && dims.X > dims.Z)
        {
            if (dims.Z > dims.Y)
            {
                axisX = Vector3.UnitX;
                axisY = Vector3.UnitZ;
                planeDims = new Vector2(dims.X, dims.Z);
            }
            else
            {
                axisX = Vector3.UnitX;
                axisY = Vector3.UnitY;
                planeDims = new Vector2(dims.X, dims.Y);
            }
        }
        else
        {
            if (dims.X > dims.Y)
            {
                axisX = Vector3.UnitX;
                axisY = Vector3.UnitZ;
                planeDims = new Vector2(dims.X, dims.Z);
            }
            else
            {
                axisX = Vector3.UnitY;
                axisY = Vector3.UnitZ;
                planeDims = new Vector2(dims.Y, dims.Z);
            }
        }

        planeDims.X = BackupMoteReach(planeDims.X);
        planeDims.Y = BackupMoteReach(planeDims.Y);
        return new MoteGfxInfo(
            texture,
            planeDims,
            axisX,
            axisY,
            middle,
            gfx.SortCenter,
            false,
            additive,
            hasMatl,
            canvasIdent,
            downgradeManner);
    }

    private static float BackupMoteReach(float val)
        => val > 1e-4f ? Math.Clamp(val, 1e-4f, 10_000f) : 1f;

    private static Quaternion MoteFacing(MacAC.Mechanics.Effects.MoteSpout emitter, Mote particle)
    {
        Quaternion facing = (emitter.Desc.Flags & EmitterBits.AttachLocal) != 0
            ? emitter.MooringRot
            : particle.SummonSpin;

        if (emitter.Desc.Type is MacAC.Mechanics.Effects.MoteKind.ParabolicLVGAGR
            or MacAC.Mechanics.Effects.MoteKind.ParabolicLVLALR
            or MacAC.Mechanics.Effects.MoteKind.ParabolicGVGAGR)
        {
            Vector3 angular = particle.C * particle.Age;
            float radians = angular.Length();
            if (radians > 1e-6f)
                facing = Quaternion.Normalize(facing * Quaternion.CreateFromAxisAngle(angular / radians, radians));
        }

        return facing;
    }

    private void PaintSequenced(IClientCamera cam) => PaintSequencedRhi(cam);

    private void PaintReadiedAlphaLot(int firstPreparedDraw, int paintTally)
    {
        if (paintTally <= 0)
            return;
        if (firstPreparedDraw < 0
            || firstPreparedDraw > _readiedAlphaTally - paintTally)
            throw new ArgumentOutOfRangeException(nameof(firstPreparedDraw));
        PaintReadiedAlphaLotRhi(firstPreparedDraw, paintTally);
    }

    private void ReadyPostponedAlphaDraws(ReadOnlySpan<int> tickets)
    {
        if (tickets.Length is 0)
            return;
        ReadyPostponedAlphaDrawsRhi(tickets);
    }

    private void CompletePaint(IClientCamera cam, ParticleDrawPass rasterizePass)
    {
        if (_submissionTemp.Count is 0)
            return;

        bool defers = rasterizePass == ParticleDrawPass.Scene && _alphaFifo?.IsCollecting == true;
        if (defers)
            DeferToCanonAlphaFifo(cam);
        else
            PaintSequenced(cam);
    }

    private void RestartPostponedAlpha()
    {
        int observedTally = Math.Max(
            _deferredAlpha.Count,
            _readiedAlphaTally);
        _deferredAlpha.Clear();
        _readiedChamberAlphaTemp.Clear();
        _readiedAlphaTally = 0;
        int latestCap = Math.Max(
            _deferredAlpha.Capacity,
            Math.Max(
                _readiedAlpha.Length,
                _readiedInstShifts.Length));
        int octetsPerPaint = checked(
            2 * Unsafe.SizeOf<DeferredMoteDraw>() + sizeof(uint));
        int markCap = _alphaTempRule.WatchAndPickCap(
            latestCap,
            observedTally,
            octetsPerPaint,
            floorCap: 256,
            growthQuantum: 256);
        if (markCap >= latestCap)
            return;

        _deferredAlpha.Capacity = markCap;
        Array.Resize(ref _readiedAlpha, markCap);
        Array.Resize(ref _readiedInstShifts, markCap);
    }

    private void AssemblePaintRosters(
        Vector3 camRealmSpot,
        ParticleDrawPass rasterizePass,
        Vector3 camRight,
        Vector3 camUp,
        Func<MacAC.Mechanics.Effects.MoteSpout, bool>? spoutSift,
        IReadOnlyList<RuntimeParticleEmitter>? scopedSpouts,
        uint clipSocket)
    {
        List<MoteDraw> draws = _paintRosterTemp;
        draws.Clear();
        _meshDrawListScratch.Clear();
        _submissionTemp.Clear();
        int series = 0;
        if (scopedSpouts is not null)
        {
            for (int idx = 0; idx < scopedSpouts.Count; ++idx)
            {
                AffixSpoutDraws(
                    scopedSpouts[idx],
                    camRealmSpot,
                    camRight,
                    camUp,
                    clipSocket,
                    ref series);
            }
            return;
        }

        foreach (RuntimeParticleEmitter spout in _motes.IterateRenderableSpouts(rasterizePass))
        {
            if (spoutSift is null || spoutSift(spout))
                AffixSpoutDraws(
                    spout,
                    camRealmSpot,
                    camRight,
                    camUp,
                    clipSocket,
                    ref series);
        }
    }

    private void AssembleTeardownReleases(List<(string Name, Action Release)> releases)
    {
        releases.Add(("emitter-death-subscription", () =>
            _motes.EmitterDied -= OnSpoutDied));
        releases.Add(("emitter-resources", RetireEverySettledSpout));
        if (_triMeshReferences is not null)
            releases.Add(("mesh-references", _triMeshReferences.Dispose));

        releases.Add(("rhi-resources", TeardownRhiAssetList));
    }

    private bool TryAffixTriMeshDraws(
        MacAC.Mechanics.Effects.MoteSpout spout,
        Mote mote,
        uint gfxObjRefIdent,
        Vector3 camRealmLocus,
        uint clipSocket,
        ref int series)
    {
        if (_triMeshBridge is null || !TriMeshMotesOnHand)
            return true;

        _triMeshReferences!.Register(spout.Handle, gfxObjRefIdent);
        var rasterizeBlob = _triMeshBridge.TryFetchRenderData(gfxObjRefIdent);
        if (rasterizeBlob is null)
        {
            if (_triMeshPullAskedThisCycle.Add(gfxObjRefIdent))
                _triMeshBridge.SecureFetched(gfxObjRefIdent);
            return true;
        }

        Quaternion facing = MoteFacing(spout, mote);
        Matrix4x4 model = Matrix4x4.CreateScale(mote.Size)
            * Matrix4x4.CreateFromQuaternion(facing)
            * Matrix4x4.CreateTranslation(mote.Position);
        Vector3 realmOrderMiddle = Vector3.Transform(rasterizeBlob.SortCenter, model);
        float gapSq = Vector3.DistanceSquared(realmOrderMiddle, camRealmLocus);
        MeshMoteInstance inst = new MeshMoteInstance(
            model,
            mote.ColorArgb,
            gapSq,
            clipSocket);

        for (int lotOrdinal = 0; lotOrdinal < rasterizeBlob.Batches.Count; ++lotOrdinal)
        {
            var lot = rasterizeBlob.Batches[lotOrdinal];
            if (lot.OrdinalTally <= 0 || !lot.TextureSlot.IsAssigned)
                continue;

            int paintOrdinal = _meshDrawListScratch.Count;
            _meshDrawListScratch.Add(new MeshMoteDraw(
                new TriMeshLotTag(gfxObjRefIdent, lotOrdinal),
                lot,
                inst));
            _submissionTemp.Add(new MoteSubmission(
                MoteSubmissionKind.Mesh,
                paintOrdinal,
                gapSq,
                series++));
        }

        return true;
    }

    private static void EmitBillboardGpuInst(
        ref BillboardGpuInst dest,
        MoteInstance mote)
    {
        dest = new BillboardGpuInst
        {
            Middle = new Vector4(mote.Position, 0f),
            AxisX = new Vector4(mote.AxisX, 0f),
            AxisY = new Vector4(mote.AxisY, 0f),
            Color = new Vector4(
                ((mote.TintArgb >> 16) & 0xFF) / 255f,
                ((mote.TintArgb >> 8) & 0xFF) / 255f,
                (mote.TintArgb & 0xFF) / 255f,
                ((mote.TintArgb >> 24) & 0xFF) / 255f),
            TextureIndex = mote.TextureSlot.IsAssigned
                ? mote.TextureSlot.Index
                : NoTextureSlot,
            ClipSlot = mote.ClipSocket,
        };
    }

    private static void EmitTriMeshGpuInst(
        ref MeshMoteGpuInstance dest,
        MeshMoteInstance inst)
    {
        dest = new MeshMoteGpuInstance
        {
            Model = inst.Model,
            Color = new Vector4(
                ((inst.ColorArgb >> 16) & 0xFF) / 255f,
                ((inst.ColorArgb >> 8) & 0xFF) / 255f,
                (inst.ColorArgb & 0xFF) / 255f,
                ((inst.ColorArgb >> 24) & 0xFF) / 255f),
            ClipSlot = inst.ClipSlot,
        };
    }

    private void OnSpoutDied(int hnd) => _spoutRetirements.CommenceSunset(hnd);

    private MoteGfxInfo ScanMoteGfxDetails(uint gfxObjRefIdent)
    {
        try
        {
            PartMesh? gfx = _datFiles?.Get<PartMesh>(gfxObjRefIdent);
            if (gfx is null)
                return MoteGfxInfo.Default;

            uint canvasIdent = gfx.SkinIds.Count > 0 ? gfx.SkinIds[0] : 0u;
            bool additive = false;
            if (canvasIdent is not 0)
            {
                Skin? canvas = _datFiles?.Get<Skin>(canvasIdent);
                additive = canvas is not null && canvas.Bits.HasFlag(SkinBits.Additive);
            }
            return AuthoredMoteGfxDetails(
                gfx,
                texture: MacAC.Client.Graphics.Gpu.GpuTextureSlot.Unassigned,
                additive,
                hasMatl: canvasIdent is not 0,
                canvasIdent: canvasIdent,
                downgradeManner: LocateLeadDowngradeManner(gfxObjRefIdent) ?? 0u);
        }
        catch
        {
            return MoteGfxInfo.Default;
        }
    }

    private bool IsPtSprite(PartMesh gfx)
        => LocateLeadDowngradeManner(gfx.Id) == 2u;

    private void RetireEverySettledSpout()
    {
        int[] hnds = [.. _moteGfxDetailsBySpout.Keys];
        for (int idx = 0; idx < hnds.Length; ++idx)
            _spoutRetirements.CommenceSunset(hnds[idx]);
        _spoutRetirements.CompleteOrThrow();
    }

    private void ConcludeTeardown()
    {
        _dynamicCycleBegun = false;
        _moteGfxDetailsBySpout.Clear();
        _moteGfxDetailsByGfxObjRef.Clear();
        _geoSortByGfxObjRef.Clear();
        _leadDowngradeMannerByGfxObjRef.Clear();
        _triMeshBlendByCanvas.Clear();
        _deferredAlpha.Clear();
        _readiedChamberAlphaTemp.Clear();
    }
}
