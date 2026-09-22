using MacAC.Dat;
using System.Numerics;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics;

internal sealed class OnlineActorMotionExhibitor(
    OnlineActorCore liveEntities,
    IOnlineStaticPartFrameSource staticFrames,
    ActorEffectPoseRegistry effectPoses,
    IOnlineMotionDisplayScope context,
    MotionDisplayTelemetry diagnostics,
    int concealPieceOrdinal)
{
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IOnlineStaticPartFrameSource _staticCycles = staticFrames ?? throw new ArgumentNullException(nameof(staticFrames));
    private readonly ActorEffectPoseRegistry _fxPostures = effectPoses ?? throw new ArgumentNullException(nameof(effectPoses));
    private readonly IOnlineMotionDisplayScope _ctx = context ?? throw new ArgumentNullException(nameof(context));
    private readonly MotionDisplayTelemetry _telemetry = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly int _concealPieceOrdinal = concealPieceOrdinal;
    private readonly List<OnlineActorRecord> _capture = [];
    private bool _isPresenting;

    public void ReadyAnim(
        OnlineActorRecord capture,
        OnlineActorMotionLedger anim)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(anim);
        var core = _onlineActors;
        if (!core.IsLatestAnimHolder(capture, anim)
            || !ReferenceEquals(capture.WorldEntity, anim.Entity)
            || anim.Sequencer is not { LocomotionDoneMark: null } scheduler)

            return;

        RealmActor grabbedActor = anim.Entity;
        scheduler.LocomotionDoneMark = (locomotion, success) =>
        {
            var latest = _onlineActors;
            if (!latest.IsLatestAnimHolder(capture, anim)
                || !ReferenceEquals(capture.WorldEntity, grabbedActor)
                || !ReferenceEquals(anim.Entity, grabbedActor))

                return;

            var interpreter = _ctx.LocateLocomotionInterpreter(capture);
            interpreter?.MotionDone(locomotion, success);
            if (_telemetry.DumpMotionEnabled)
            {
                Console.WriteLine(
                    $"[MOTIONDONE] guid={capture.ServerOid:X8} motion=0x{locomotion:X8} "
                    + $"success={success} pending={(interpreter?.MotionsQueued() ?? false)}");
            }
        };
    }

    public void Present(
        IReadOnlyDictionary<SimActorKey, OnlineActorMotionSchedule> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var core = _onlineActors;
        if (_isPresenting)
            return;

        _isPresenting = true;
        try
        {
            core.DuplicateSpatialTrunkObjectRecordsTo(_capture);
            foreach (OnlineActorRecord capture in _capture)
            {
                if (!TryFetchLatest(core, capture, out RealmActor actor, out OnlineActorMotionLedger anim))
                    continue;

                ReadyAnim(capture, anim);
                if (!TryFetchLatest(core, capture, actor, anim))
                    continue;

                OnlineActorMotionSchedule plan = default;
                if (capture.ProjTag is { } tag)
                    schedules.TryGetValue(tag, out plan);
                bool hasPlainPlan = IsLatestPlan(core, plan, capture, actor, anim);
                IReadOnlyList<PieceTransform>? seriesCycles = hasPlainPlan
                    ? plan.SequenceFrames
                    : null;
                bool constructPieces = hasPlainPlan && plan.ComposeParts;
                float legacyProceedSecs = hasPlainPlan
                    ? plan.LegacyAdvanceSeconds
                    : 0f;
                ulong objectTimerEpoch = capture.ObjectTimerEpoch;
                ulong projVer = capture.ProjAlterationVer;
                ulong exhibitRev = anim.ExhibitRev;

                if (_staticCycles.TryGrabOnlinePieceCycles(
                        capture,
                        actor,
                        anim,
                        objectTimerEpoch,
                        projVer,
                        exhibitRev,
                        out IReadOnlyList<PieceTransform> staticPieceCycles) == true)
                {
                    if (!TryFetchLatest(
                            core,
                            capture,
                            actor,
                            anim,
                            objectTimerEpoch,
                            projVer,
                            exhibitRev))

                        continue;
                    seriesCycles = staticPieceCycles;
                    constructPieces = true;
                }

                if (anim.Sequencer is not null)
                {
                    WriteSeriesTelemetry(capture, anim, seriesCycles);
                }
                else
                {
                    int span = anim.HighFrame - anim.LoCycle;
                    bool concealedLegacy = (capture.FinalKineticsPhase & KineticStateFlags.Hidden) != 0;
                    if (span <= 0 && !concealedLegacy)
                        continue;
                    if (span > 0 && legacyProceedSecs > 0f)
                    {
                        anim.CurrCycle = CanonAnimCyclePlayback.Advance(
                            anim.CurrCycle,
                            anim.LoCycle,
                            anim.HighFrame,
                            anim.Framerate,
                            legacyProceedSecs);
                    }
                }

                if (!constructPieces
                    || !TryFetchLatest(
                        core,
                        capture,
                        actor,
                        anim,
                        objectTimerEpoch,
                        projVer,
                        exhibitRev))

                    continue;

                ConstructAndBroadcast(core, capture, actor, anim, seriesCycles,
                    objectTimerEpoch, projVer, exhibitRev);
            }
        }
        finally
        {
            _isPresenting = false;
        }
    }

    private void ConstructAndBroadcast(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        OnlineActorMotionLedger anim,
        IReadOnlyList<PieceTransform>? seriesCycles,
        ulong objectTimerEpoch,
        ulong projVer,
        ulong exhibitRev)
    {
        int pieceTally = anim.PieceBlueprint.Count;
        WritePieceTelemetry(capture, anim, seriesCycles, pieceTally);
        SecureKeptPostures(anim);
        var triMeshRefs = anim.TriMeshRefsTemp;
        triMeshRefs.Clear();
        var rigidPostures = anim.FxPiecePosturesTemp;
        var visualPostures = anim.VisualPiecePosturesTemp;
        Matrix4x4 scaling = anim.Scale == 1f
            ? Matrix4x4.Identity
            : Matrix4x4.CreateScale(anim.Scale);

        for (int idx = 0; idx < pieceTally; ++idx)
        {
            if (TryLocatePieceCycle(
                    anim,
                    seriesCycles,
                    idx,
                    out Vector3 origin,
                    out Quaternion facing))
            {
                Vector3 defaultScaling = idx < anim.Setup.DefaultScale.Count
                    ? anim.Setup.DefaultScale[idx]
                    : Vector3.One;
                Matrix4x4 visual = Matrix4x4.CreateScale(defaultScaling)
                    * Matrix4x4.CreateFromQuaternion(facing)
                    * Matrix4x4.CreateTranslation(origin);
                if (anim.Scale != 1f)
                    visual *= scaling;
                visualPostures[idx] = visual;
                rigidPostures[idx] = Matrix4x4.CreateFromQuaternion(facing)
                    * Matrix4x4.CreateTranslation(origin * anim.Scale);
            }

            var blueprint = anim.PieceBlueprint[idx];
            if (!blueprint.IsDrawable
                || (_concealPieceOrdinal >= 0 && idx == _concealPieceOrdinal && pieceTally >= 10))

                continue;
            triMeshRefs.Add(new TriMeshRef(blueprint.GfxObjId, visualPostures[idx])
            {
                CanvasOverrides = blueprint.SurfaceOverrides,
            });
        }

        if (!TryFetchLatest(core, capture, actor, anim, objectTimerEpoch, projVer, exhibitRev))
            return;
        actor.MeshRefs = triMeshRefs;
        actor.AssignIndexedPiecePostures(rigidPostures, anim.PieceReadiness);
        if (!TryFetchLatest(core, capture, actor, anim, objectTimerEpoch, projVer, exhibitRev))
            return;
        _fxPostures.Publish(actor, rigidPostures, anim.PieceReadiness);
    }

    private static bool TryLocatePieceCycle(
        OnlineActorMotionLedger anim,
        IReadOnlyList<PieceTransform>? seriesCycles,
        int pieceOrdinal,
        out Vector3 origin,
        out Quaternion facing)
    {
        if (seriesCycles is not null)
        {
            if (pieceOrdinal < seriesCycles.Count)
            {
                origin = seriesCycles[pieceOrdinal].Origin;
                facing = seriesCycles[pieceOrdinal].Facing;
                return true;
            }
            origin = default;
            facing = default;
            return false;
        }

        return CanonAnimCyclePlayback.TryLerpPiece(
            anim.Animation,
            anim.CurrCycle,
            anim.LoCycle,
            anim.HighFrame,
            pieceOrdinal,
            out origin,
            out facing);
    }

    private static void SecureKeptPostures(OnlineActorMotionLedger anim)
    {
        int pieceTally = anim.PieceBlueprint.Count;
        if (anim.ExhibitPosturesInitialized
            && anim.VisualPiecePosturesTemp.Count == pieceTally
            && anim.FxPiecePosturesTemp.Count == pieceTally)

            return;

        var visual = anim.VisualPiecePosturesTemp;
        SecureKeptPosturesRest(pieceTally, anim, visual);
    }

    private static void SecureKeptPosturesRest(int pieceTally, OnlineActorMotionLedger anim, List<Matrix4x4> visual)
    {
        var rigid = anim.FxPiecePosturesTemp;
        visual.Clear();
        SecureKeptPosturesTail(pieceTally, anim, visual, rigid);
    }

    private static void SecureKeptPosturesTail(int pieceTally, OnlineActorMotionLedger anim, List<Matrix4x4> visual, List<Matrix4x4> rigid)
    {
        rigid.Clear();
        bool[] consumed = new bool[anim.Entity.MeshRefs.Count];
        for (int idx = 0; idx < pieceTally; ++idx)
        {
            Matrix4x4 rigidRest = idx < anim.Entity.IndexedPieceXforms.Count
                ? anim.Entity.IndexedPieceXforms[idx]
                : Matrix4x4.Identity;
            rigid.Add(rigidRest);

            var blueprint = anim.PieceBlueprint[idx];
            Matrix4x4 visualRest = default;
            bool located = false;
            for (int triMeshOrdinal = 0; triMeshOrdinal < anim.Entity.MeshRefs.Count; ++triMeshOrdinal)
            {
                TriMeshRef triMesh = anim.Entity.MeshRefs[triMeshOrdinal];
                if (consumed[triMeshOrdinal] || triMesh.GfxObjId != blueprint.GfxObjId)
                    continue;
                consumed[triMeshOrdinal] = true;
                visualRest = triMesh.PartTransform;
                located = true;
                break;
            }
            if (!located)
            {
                Vector3 defaultScaling = idx < anim.Setup.DefaultScale.Count
                    ? anim.Setup.DefaultScale[idx]
                    : Vector3.One;
                visualRest = Matrix4x4.CreateScale(defaultScaling * anim.Scale)
                    * rigidRest;
            }
            visual.Add(visualRest);
        }
        anim.ExhibitPosturesInitialized = true;
    }

    private void WriteSeriesTelemetry(
        OnlineActorRecord capture,
        OnlineActorMotionLedger anim,
        IReadOnlyList<PieceTransform>? seriesCycles)
    {
        if (!_telemetry.RemoteVelocityEnabled
            || capture.ServerOid is 0
            || capture.ServerOid == _ctx.OwnAvatarOid
            || capture.RemoteMotionRuntime is null)

            return;
        double instant = _telemetry.Now();
        if (instant - anim.PreviousSeriesProbeMoment <= 1d)
            return;
        var scheduler = anim.Sequencer!;
        Console.WriteLine(
            $"[SEQSTATE] guid={capture.ServerOid:X8} CurrentMotion=0x{scheduler.CurrentMotion:X8} "
            + $"CurrentSpeedMod={scheduler.CurrentSpeedMod:F3}");
        WriteSeriesTelemetryTrace(capture, instant, anim, scheduler);
    }

    private void WriteSeriesTelemetryTrace(OnlineActorRecord capture, double instant, OnlineActorMotionLedger anim, AnimSequencer scheduler)
    {
        var joint = scheduler.LatestJointDiag;
        int leadDigest = scheduler.LeadCyclicAnimRefDigest;
        Console.WriteLine(
                    $"[CURRNODE] guid={capture.ServerOid:X8} animRef=0x{joint.AnimRefHash:X8} "
                    + $"firstCyclicAnimRef=0x{leadDigest:X8} "
                    + $"isOnCyclic={joint.AnimRefHash == leadDigest && leadDigest is not 0} "
                    + $"isLooping={joint.IsLooping} fr={joint.Framerate:F2} "
                    + $"frame=[{joint.StartFrame}..{joint.EndFrame}] "
                    + $"pos={joint.FramePosition:F2} qCount={joint.QueueCount}");
        anim.PreviousSeriesProbeMoment = instant;
    }

    private void WritePieceTelemetry(
        OnlineActorRecord capture,
        OnlineActorMotionLedger anim,
        IReadOnlyList<PieceTransform>? seriesCycles,
        int pieceTally)
    {
        if (!_telemetry.RemoteVelocityEnabled
            || capture.ServerOid is 0
            || capture.ServerOid == _ctx.OwnAvatarOid
            || capture.RemoteMotionRuntime is null)

            return;
        double instant = _telemetry.Now();
        if (instant - anim.PreviousPieceProbeMoment <= 1d)
            return;
        int animPieceTally = anim.Animation is not null
            && anim.Animation.Frames.Count > 0
                ? anim.Animation.Frames[0].Poses.Count
                : -1;
        WritePieceTelemetryTrace(capture, instant, anim, animPieceTally, seriesCycles, pieceTally);
    }

    private void WritePieceTelemetryTrace(OnlineActorRecord capture, double instant, OnlineActorMotionLedger anim, int animPieceTally, IReadOnlyList<PieceTransform>? seriesCycles, int pieceTally)
    {
        double digest = 0d;
        if (seriesCycles is not null)
        {
            foreach (PieceTransform cycle in seriesCycles)
            {
                digest += cycle.Origin.X + cycle.Origin.Y + cycle.Origin.Z
                    + cycle.Facing.X + cycle.Facing.Y
                    + cycle.Facing.Z + cycle.Facing.W;
            }
        }
        Console.WriteLine(
                    $"[PARTSDIAG] guid={capture.ServerOid:X8} pt.Count={pieceTally} "
                    + $"seqFrames.Count={seriesCycles?.Count ?? -1} "
                    + $"setup.Parts.Count={anim.Setup.PartIds.Count} "
                    + $"anim.PartFrames[0].Frames.Count={animPieceTally} seqHash={digest:F4}");
        anim.PreviousPieceProbeMoment = instant;
    }

    private static bool IsLatestPlan(
        OnlineActorCore core,
        OnlineActorMotionSchedule plan,
        OnlineActorRecord capture,
        RealmActor actor,
        OnlineActorMotionLedger anim)
    {
        return plan.ComposeParts
        && ReferenceEquals(plan.Record, capture)
        && ReferenceEquals(plan.Entity, actor)
        && ReferenceEquals(plan.Animation, anim)
        && TryFetchLatest(
            core,
            capture,
            actor,
            anim,
            plan.ObjectClockEpoch,
            plan.ProjectionMutationVersion,
            plan.PresentationRevision);
    }

    private static bool TryFetchLatest(
        OnlineActorCore core,
        OnlineActorRecord capture,
        out RealmActor actor,
        out OnlineActorMotionLedger anim)
    {
        var latestActor = capture.WorldEntity;
        var latestAnim =
            capture.AnimationRuntime as OnlineActorMotionLedger;
        actor = latestActor!;
        anim = latestAnim!;
        return latestActor is not null
            && latestAnim is not null
            && TryFetchLatest(core, capture, actor, anim);
    }

    private static bool TryFetchLatest(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        OnlineActorMotionLedger anim)
    {
        return core.IsLatestSpatialAnim(capture, anim)
        && ReferenceEquals(capture.WorldEntity, actor)
        && ReferenceEquals(anim.Entity, actor);
    }

    private static bool TryFetchLatest(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        OnlineActorMotionLedger anim,
        ulong objectTimerEpoch,
        ulong projVer,
        ulong exhibitRev)
    {
        return TryFetchLatest(core, capture, actor, anim)
        && capture.ObjectTimerEpoch == objectTimerEpoch
        && capture.ProjAlterationVer == projVer
        && anim.ExhibitRev == exhibitRev;
    }
}
