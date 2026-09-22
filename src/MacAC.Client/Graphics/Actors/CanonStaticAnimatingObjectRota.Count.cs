using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed partial class CanonStaticAnimatingObjectRota
{
    internal int Count => _holders.Count;

    public bool Register(RealmActor actor, ProgramActivationDetails details)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(details);
        if (!details.UsesStaticAnimationWorkset
            || details.Setup is not { } rig
            || details.DefaultAnimationId is 0)

            return false;

        if (_holders.TryGetValue(actor.Id, out ClientOwner? kept))
        {
            return ReferenceEquals(kept.Entity, actor)
                ? true
                : throw new InvalidOperationException(
                $"DAT-static animation owner 0x{actor.Id:X8} is by now registered");
        }

        AnimSequencer? scheduler = null;
        if (actor.ServerGuid is 0)
        {
            scheduler = new AnimSequencer(
                rig,
                new MotionBook(),
                _animFetcher);
            if (!scheduler.HasLatestJoint)
                return false;
        }

        int pieceTally = rig.PartIds.Count;
        uint[] gfxIdents = AssemblePieceGfxIdents(rig, actor);
        bool[] onHand = new bool[pieceTally];
        if (details.PartAvailability is { } supplied)
        {
            for (int idx = 0; idx < pieceTally && idx < supplied.Count; ++idx)
                onHand[idx] = supplied[idx];
        }
        var canvases = FitCanvasSubstitutions(actor, gfxIdents, onHand);
        _holders.Add(actor.Id, new ClientOwner
        {
            Entity = actor,
            Setup = rig,
            Sequencer = scheduler,
            PieceGfxIdents = gfxIdents,
            CanvasSubstitutions = canvases,
            PieceOnHand = onHand,
        });
        return true;
    }

    public void Rebind(RealmActor actor, ProgramActivationDetails details)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(details);
        if (!_holders.TryGetValue(actor.Id, out ClientOwner? holder))
        {
            throw new InvalidOperationException(
                $"DAT-static animation owner 0x{actor.Id:X8} isn't registered");
        }
        if (ReferenceEquals(holder.Entity, actor))
            return;
        if (actor.ServerGuid is not 0
            || !details.UsesStaticAnimationWorkset
            || details.Setup is null
            || details.DefaultAnimationId is 0)
        {
            throw new InvalidOperationException(
                $"DAT-static animation owner 0x{actor.Id:X8} can't be rebound to incompatible Setup data");
        }

        int pieceTally = holder.Setup.PartIds.Count;
        uint[] gfxIdents = AssemblePieceGfxIdents(holder.Setup, actor);
        bool[] onHand = new bool[pieceTally];
        if (details.PartAvailability is { } supplied)
        {
            for (int idx = 0; idx < pieceTally && idx < supplied.Count; ++idx)
                onHand[idx] = supplied[idx];
        }

        RebindRest(actor, holder, gfxIdents, onHand);
    }

    private void RebindRest(RealmActor actor, ClientOwner holder, uint[] gfxIdents, bool[] onHand)
    {
        RealmActor displaced = holder.Entity;
        if (ReferenceEquals(displaced.MeshRefs, holder.MeshRefs))
            displaced.MeshRefs = holder.MeshRefs.ToArray();
        if (ReferenceEquals(displaced.IndexedPieceXforms, holder.PiecePostures)
                    || ReferenceEquals(displaced.IndexedPieceOnHand, holder.PieceOnHand))
        {
            displaced.AssignIndexedPiecePostures(
                holder.PiecePostures.ToArray(),
                holder.PieceOnHand.ToArray());
        }
        holder.Entity = actor;
        holder.PieceGfxIdents = gfxIdents;
        RebindTail(holder, actor, gfxIdents, onHand);
    }

    private void RebindTail(ClientOwner holder, RealmActor actor, uint[] gfxIdents, bool[] onHand)
    {
        holder.CanvasSubstitutions = FitCanvasSubstitutions(actor, gfxIdents, onHand);
        holder.PieceOnHand = onHand;
        holder.HasReadiedOnlinePieceCycles = false;
        holder.ReadiedOnlinePieceCycles.Clear();
    }

    public bool AttachOnlineHolder(
        RealmActor actor,
        OnlineActorMotionLedger anim,
        KineticBody corpus)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(anim);
        ArgumentNullException.ThrowIfNull(corpus);
        if (!_holders.TryGetValue(actor.Id, out ClientOwner? holder))
            return false;
        if (!ReferenceEquals(holder.Entity, actor) || actor.ServerGuid is 0)
        {
            throw new InvalidOperationException(
                $"Static animation owner 0x{actor.Id:X8} doesn't match this live incarnation");
        }
        if (anim.Sequencer is not { HasLatestJoint: true } scheduler)
            return false;

        DirtyQueued(holder);
        holder.Sequencer = scheduler;
        holder.OnlineAnim = anim;
        holder.Body = corpus;
        return true;
    }

    public bool TryGrabOnlinePieceCycles(
        OnlineActorRecord capture,
        RealmActor actor,
        OnlineActorMotionLedger anim,
        ulong objectTimerEpoch,
        ulong projAlterationVer,
        ulong exhibitRev,
        out IReadOnlyList<PieceTransform> cycles)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(anim);
        uint holderIdent = actor.Id;
        if (!_holders.TryGetValue(holderIdent, out ClientOwner? holder))
        {
            cycles = Array.Empty<PieceTransform>();
            return false;
        }

        bool preciseHolder = ReferenceEquals(capture.WorldEntity, actor)
            && ReferenceEquals(capture.AnimationRuntime, anim)
            && capture.ObjectTimerEpoch == objectTimerEpoch
            && capture.ProjAlterationVer == projAlterationVer
            && anim.ExhibitRev == exhibitRev
            && ReferenceEquals(holder.Entity, actor)
            && ReferenceEquals(holder.OnlineAnim, anim)
            && ReferenceEquals(holder.Sequencer, anim.Sequencer)
            && holder.Entity.ServerGuid is not 0;
        if (!preciseHolder)
        {
            DirtyQueued(holder);
            cycles = Array.Empty<PieceTransform>();
            return false;
        }

        if (!holder.HasReadiedOnlinePieceCycles)
        {
            cycles = Array.Empty<PieceTransform>();
            return false;
        }

        if (!ReferenceEquals(holder.ReadiedCycleScheduler, anim.Sequencer)
            || !IsHousedAtVer(holder, holder.QueuedResidencyVer))
        {
            DirtyQueued(holder);
            cycles = Array.Empty<PieceTransform>();
            return false;
        }

        if (holder.ReadiedExhibitRev != exhibitRev)
        {
            DirtyReadiedCycle(holder);
            cycles = Array.Empty<PieceTransform>();
            return false;
        }

        holder.HasReadiedOnlinePieceCycles = false;
        cycles = holder.ReadiedOnlinePieceCycles;
        return true;
    }

    public void Unregister(uint holderIdent) => _holders.Remove(holderIdent);

    public void DuplicateMovingActorIdentsTo(HashSet<uint> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        foreach ((uint holderIdent, ClientOwner holder) in _holders)
        {
            if (holder.Sequencer is not null)
                dest.Add(holderIdent);
        }
    }

    public void Tick(float passedSecs)
    {
        if (!float.IsFinite(passedSecs)
            || passedSecs <= 0f
            || _holders.Count is 0)

            return;

        _capture.Clear();
        _capture.AddRange(_holders.Values);
        foreach (ClientOwner holder in _capture)
        {
            if (!_holders.TryGetValue(holder.Entity.Id, out ClientOwner? latest)
                || !ReferenceEquals(latest, holder))

                continue;

            if (holder.Sequencer is null
                && holder.Entity.ServerGuid is not 0
                && _locateOnlineHolder?.Invoke(holder.Entity) is { } mapping)
            {
                _ = AttachOnlineHolder(
                    holder.Entity,
                    mapping.Animation,
                    mapping.Body);
            }
            if (holder.Sequencer is not { } scheduler)
                continue;

            holder.PassedSinceRefresh += passedSecs;
            if (!_isHoused(holder.Entity))
            {
                DirtyQueued(holder);
                continue;
            }
            ulong residencyVer = _residencyVer(holder.Entity);

            double holderPassed = holder.PassedSinceRefresh;
            holder.PassedSinceRefresh = 0d;
            if (holderPassed is <= CycleEpsilon
                or > CeilingPassed)

                continue;

            var cycles = scheduler.Advance((float)holderPassed);
            holder.ReadiedOnlinePieceCycles.Clear();
            for (int idx = 0; idx < cycles.Count; ++idx)
                holder.ReadiedOnlinePieceCycles.Add(cycles[idx]);

            if (holder.Body is { } corpus)
            {
                Quaternion earlierFacing = corpus.Orientation;
                holder.TrunkCycleTemp.Origin = corpus.Position;
                holder.TrunkCycleTemp.Orientation = earlierFacing;
                PoseOps.GSpin(holder.TrunkCycleTemp, corpus.Omega);
                corpus.AssignCycleInLatestChamber(
                    corpus.Position,
                    holder.TrunkCycleTemp.Orientation);
                holder.Entity.Rotation = corpus.Orientation;
                if (corpus.Orientation != earlierFacing)
                {
                    _sealOnlineTrunk(holder.Entity, corpus);
                    if (!_holders.TryGetValue(holder.Entity.Id, out latest)
                        || !ReferenceEquals(latest, holder)
                        || !IsHousedAtVer(holder, residencyVer))
                    {
                        DirtyQueued(holder);
                        continue;
                    }
                }
            }

            else if (holder.Omega != Vector3.Zero)
            {
                holder.TrunkCycleTemp.Origin = holder.Entity.Position;
                holder.TrunkCycleTemp.Orientation = holder.Entity.Rotation;
                float omegaScaling = (float)(
                    holderPassed * DatStaticOmegaReferenceHz);
                PoseOps.GSpin(
                    holder.TrunkCycleTemp,
                    holder.Omega * omegaScaling);
                holder.Entity.Rotation = holder.TrunkCycleTemp.Orientation;
            }

            if (!IsHousedAtVer(holder, residencyVer))
            {
                DirtyQueued(holder);
                continue;
            }

            if (holder.Entity.ServerGuid is not 0)
            {
                holder.HasReadiedOnlinePieceCycles = true;
                holder.ReadiedCycleScheduler = scheduler;
                holder.ReadiedExhibitRev =
                    holder.OnlineAnim?.ExhibitRev ?? 0UL;
            }
            else
            {
                Compose(holder, holder.ReadiedOnlinePieceCycles);
                _broadcastPiecePostures(holder.Entity, holder.PiecePostures, holder.PieceOnHand);
                if (!_holders.TryGetValue(holder.Entity.Id, out latest)
                    || !ReferenceEquals(latest, holder)
                    || !IsHousedAtVer(holder, residencyVer))
                {
                    DirtyQueued(holder);
                    continue;
                }
            }

            holder.QueuedProcTaps = scheduler;
            holder.QueuedResidencyVer = residencyVer;
        }
    }

    public void ProcessHooks()
    {
        _tapCapture.Clear();
        foreach (ClientOwner holder in _holders.Values)
        {
            if (holder.QueuedProcTaps is not null)
                _tapCapture.Add(holder);
        }

        foreach (ClientOwner holder in _tapCapture)
        {
            if (!_holders.TryGetValue(holder.Entity.Id, out ClientOwner? latest)
                || !ReferenceEquals(latest, holder)
                || holder.QueuedProcTaps is not { } scheduler
                || !ReferenceEquals(holder.Sequencer, scheduler)
                || !IsHousedAtVer(holder, holder.QueuedResidencyVer))
            {
                DirtyQueued(holder);
                continue;
            }

            ImposeOmegaTaps(holder, scheduler.QueuedTaps);

            holder.QueuedProcTaps = null;
            _grabTaps(holder.Entity.Id, scheduler);
        }
    }

    internal bool TryGrabReadiedCyclesForTest(
        uint holderIdent,
        out IReadOnlyList<PieceTransform> cycles)
    {
        if (!_holders.TryGetValue(holderIdent, out ClientOwner? holder))
        {
            cycles = Array.Empty<PieceTransform>();
            return false;
        }
        if (!holder.HasReadiedOnlinePieceCycles)
        {
            cycles = Array.Empty<PieceTransform>();
            return false;
        }
        if (!IsHousedAtVer(holder, holder.QueuedResidencyVer))
        {
            DirtyQueued(holder);
            cycles = Array.Empty<PieceTransform>();
            return false;
        }
        holder.HasReadiedOnlinePieceCycles = false;
        cycles = holder.ReadiedOnlinePieceCycles;
        return true;
    }

    internal void DuplicateEngagedDatStaticActorsTo(List<RealmActor> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach (ClientOwner holder in _holders.Values)
        {
            if (holder.Entity.ServerGuid is 0
                && holder.Sequencer is not null
                && _isHoused(holder.Entity))

                dest.Add(holder.Entity);
        }
    }

    internal static bool TryLocateOmega(
        IReadOnlyList<Cue> taps, out Vector3 omega)
    {
        omega = default;
        bool located = false;
        for (int idx = 0; idx < taps.Count; ++idx)
        {
            if (taps[idx] is not SetOmegaCue set)
                continue;
            omega = new Vector3(set.Axis.X, set.Axis.Y, set.Axis.Z);
            located = true;
        }
        return located;
    }

    private static void ImposeOmegaTaps(ClientOwner holder, IReadOnlyList<Cue> taps)
    {
        if (!TryLocateOmega(taps, out Vector3 omega))
            return;
        holder.Omega = omega;
        if (holder.Body is { } corpus)
            corpus.Omega = omega;
    }

    private bool IsHousedAtVer(ClientOwner holder, ulong ver)
    {
        return _isHoused(holder.Entity)
        && _residencyVer(holder.Entity) == ver;
    }

    private static void DirtyQueued(ClientOwner holder)
    {
        DirtyReadiedCycle(holder);
        holder.QueuedProcTaps = null;
        holder.QueuedResidencyVer = 0UL;
    }

    private static void DirtyReadiedCycle(ClientOwner holder)
    {
        holder.ReadiedOnlinePieceCycles.Clear();
        holder.HasReadiedOnlinePieceCycles = false;
        holder.ReadiedCycleScheduler = null;
        holder.ReadiedExhibitRev = 0UL;
    }

    private static void Compose(ClientOwner holder, IReadOnlyList<PieceTransform> cycles)
    {
        SecureKeptPostures(holder);
        holder.MeshRefs.Clear();
        Matrix4x4 objectScaling = holder.Entity.Scale == 1f
            ? Matrix4x4.Identity
            : Matrix4x4.CreateScale(holder.Entity.Scale);

        int pieceTally = holder.Setup.PartIds.Count;
        ComposeRest(holder, objectScaling, pieceTally, cycles);
    }

    private static void ComposeRest(ClientOwner holder, Matrix4x4 objectScaling, int pieceTally, IReadOnlyList<PieceTransform> cycles)
    {
        for (int idx = 0; idx < pieceTally; ++idx)
        {
            if (idx < cycles.Count)
            {
                Vector3 origin = cycles[idx].Origin;
                Quaternion facing = cycles[idx].Facing;
                Vector3 defaultScaling = idx < holder.Setup.DefaultScale.Count
                    ? holder.Setup.DefaultScale[idx]
                    : Vector3.One;

                Matrix4x4 visual = Matrix4x4.CreateScale(defaultScaling)
                    * Matrix4x4.CreateFromQuaternion(facing)
                    * Matrix4x4.CreateTranslation(origin);
                if (holder.Entity.Scale != 1f)
                    visual *= objectScaling;
                holder.VisualPiecePostures[idx] = visual;
                holder.PiecePostures[idx] = Matrix4x4.CreateFromQuaternion(facing)
                    * Matrix4x4.CreateTranslation(origin * holder.Entity.Scale);
            }
            if (!holder.PieceOnHand[idx])
                continue;

            holder.MeshRefs.Add(new TriMeshRef(holder.PieceGfxIdents[idx], holder.VisualPiecePostures[idx])
            {
                CanvasOverrides = holder.CanvasSubstitutions[idx],
            });
        }
        holder.Entity.MeshRefs = holder.MeshRefs;
        holder.Entity.AssignIndexedPiecePostures(holder.PiecePostures, holder.PieceOnHand);
    }

    private static void SecureKeptPostures(ClientOwner holder)
    {
        int pieceTally = holder.Setup.PartIds.Count;
        if (holder.VisualPiecePostures.Count == pieceTally
            && holder.PiecePostures.Count == pieceTally)

            return;

        holder.VisualPiecePostures.Clear();
        SecureKeptPosturesRest(pieceTally, holder);
    }

    private static void SecureKeptPosturesRest(int pieceTally, ClientOwner holder)
    {
        holder.PiecePostures.Clear();
        bool[] consumed = new bool[holder.Entity.MeshRefs.Count];
        for (int idx = 0; idx < pieceTally; ++idx)
        {
            Matrix4x4 rigid = idx < holder.Entity.IndexedPieceXforms.Count
                ? holder.Entity.IndexedPieceXforms[idx]
                : Matrix4x4.Identity;
            holder.PiecePostures.Add(rigid);

            Matrix4x4 visual = default;
            bool located = false;
            for (int triMeshOrdinal = 0; triMeshOrdinal < holder.Entity.MeshRefs.Count; ++triMeshOrdinal)
            {
                TriMeshRef triMesh = holder.Entity.MeshRefs[triMeshOrdinal];
                if (consumed[triMeshOrdinal] || triMesh.GfxObjId != holder.PieceGfxIdents[idx])
                    continue;
                consumed[triMeshOrdinal] = true;
                visual = triMesh.PartTransform;
                located = true;
                break;
            }
            if (!located)
            {
                Vector3 defaultScaling = idx < holder.Setup.DefaultScale.Count
                    ? holder.Setup.DefaultScale[idx]
                    : Vector3.One;
                visual = Matrix4x4.CreateScale(defaultScaling * holder.Entity.Scale)
                    * rigid;
            }
            holder.VisualPiecePostures.Add(visual);
        }
    }

    private static uint[] AssemblePieceGfxIdents(RigSpec rig, RealmActor actor)
    {
        uint[] outcome = new uint[rig.PartIds.Count];
        for (int idx = 0; idx < outcome.Length; ++idx)
            outcome[idx] = (uint)rig.PartIds[idx];
        foreach (PartSwap substitute in actor.PieceSubstitutions)
        {
            if (substitute.PartIndex < outcome.Length)
                outcome[substitute.PartIndex] = substitute.GfxObjId;
        }
        return outcome;
    }

    private static IReadOnlyDictionary<uint, uint>?[] FitCanvasSubstitutions(
        RealmActor actor,
        uint[] gfxIdents,
        bool[] onHand)
    {
        var outcome = new IReadOnlyDictionary<uint, uint>?[gfxIdents.Length];
        bool[] consumed = new bool[actor.MeshRefs.Count];
        for (int pieceOrdinal = 0; pieceOrdinal < gfxIdents.Length; ++pieceOrdinal)
        {
            for (int triMeshOrdinal = 0; triMeshOrdinal < actor.MeshRefs.Count; ++triMeshOrdinal)
            {
                if (consumed[triMeshOrdinal]
                    || actor.MeshRefs[triMeshOrdinal].GfxObjId != gfxIdents[pieceOrdinal])

                    continue;

                consumed[triMeshOrdinal] = true;
                onHand[pieceOrdinal] = true;
                outcome[pieceOrdinal] = actor.MeshRefs[triMeshOrdinal].CanvasOverrides;
                break;
            }
        }
        return outcome;
    }
}
