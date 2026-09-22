using MacAC.Wire;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

/// <summary>A local entity id plus the incarnation that distinguishes reuse of it.</summary>
public readonly record struct SimActorKey(uint LocalEntityId, ushort Incarnation);

public sealed class SimActorRecord
{
    private readonly Queue<CanonKineticShift> _queuedShifts = new();

    internal SimActorRecord(RealmSession.MoverSpawn capture)
    {
        ServerGuid = capture.Guid;
        Snapshot = capture;
        RenewDerivedPhase();
        PositionAuthorityVersion = capture.Position is null ? 0UL : 1UL;
        VectorArbiterVer = capture.Physics is null ? 0UL : 1UL;
        VelArbiterVer = capture.Position is null && capture.Physics is null ? 0UL : 1UL;
        TravelArbiterVer = 1UL;
        ObjRefDscArbiterVer = 1UL;
        FinalKineticsCondition = CanonKineticShifts.ConstructorPhase;
        ImposeRawKineticsPhase(RawKineticsCondition);
    }

    public uint ServerGuid { get; }
    public ushort Incarnation => Snapshot.InstanceSequence;
    public ushort Generation => Incarnation;
    public RealmSession.MoverSpawn Snapshot { get; internal set; }
    public uint? OwnActorTag { get; internal set; }
    public SimActorKey? Key
    {
        get
        {
            return OwnActorTag is { } ownIdent ? new SimActorKey(ownIdent, Incarnation) : null;
        }
    }

    public uint WholeChamberTag { get; internal set; }
    public uint CanonLbTag { get; internal set; }
    public uint RawKineticsCondition { get; internal set; }
    public KineticStateFlags FinalKineticsCondition { get; internal set; }
    public bool HasPieceArray { get; internal set; }
    public KineticBody? KineticBody { get; private set; }
    public bool KineticsCorpusAcquisitionInHeadway { get; internal set; }
    public IKineticObjHost? PhysicsHost { get; internal set; }
    public CanonQuantumClock ObjectClock { get; } = new();

    public ISimPeerMotion? PeerMotion { get; internal set; }
    public bool DistantLocomotionMappingInHeadway { get; internal set; }
    public ISimMissile? Projectile { get; internal set; }
    public bool MissileMappingInHeadway { get; internal set; }
    public bool RequiresDistantStanceCore { get; internal set; }
    public bool EraseApprovedForTeardown { get; internal set; }

    public ulong KineticsOwnershipEpoch { get; private set; }
    public ulong ObjectTimerEpoch { get; private set; }
    public ulong SpatialAuthorityVersion { get; private set; }
    public ulong PlacementCommitVersion { get; private set; }
    public ulong KineticsPhaseAlterationVer { get; private set; }
    public ulong PositionAuthorityVersion { get; private set; }
    public ulong PhaseArbiterVer { get; private set; }
    public ulong VectorArbiterVer { get; private set; }
    public ulong VelArbiterVer { get; private set; }
    public ulong TravelArbiterVer { get; private set; }
    public ulong TravelSealVer { get; private set; } = 1UL;
    public ulong AncestorSealVer { get; private set; }
    public ulong ObjRefDscArbiterVer { get; private set; }
    public ulong BuildIntegrationVersion { get; private set; } = 1UL;

    internal void ProgressLocusArbiter()
    {
        ++PositionAuthorityVersion;
        ++VelArbiterVer;
    }

    internal void ProgressVectorArbiter()
    {
        ++VectorArbiterVer;
        ++VelArbiterVer;
    }

    internal void ProgressTravelArbiter()
    {
        ++TravelArbiterVer;
        ++VelArbiterVer;
    }

    internal void ProgressTravelSeal() => TravelSealVer++;

    internal void ProgressStanceSeal() => PlacementCommitVersion++;

    internal void ProgressAncestorSeal() => AncestorSealVer++;

    internal void ProgressObjRefDscArbiter() => ObjRefDscArbiterVer++;

    // A (re)create supersedes every authority at once
    internal void ProgressBuildArbiter()
    {
        ++PositionAuthorityVersion;
        ++PhaseArbiterVer;
        ++VectorArbiterVer;
        ++VelArbiterVer;
        ++TravelArbiterVer;
        ++ObjRefDscArbiterVer;
        ++BuildIntegrationVersion;
    }

    internal void SuspendObjectTimer()
    {
        ObjectClock.Deactivate();
        ++ObjectTimerEpoch;
    }

    internal void ReactivateObjectTimer()
    {
        if (ObjectClock.Engage())
            ++ObjectTimerEpoch;
    }

    internal void RestartObjectTimerForJoinRealm(bool isStatic)
    {
        ObjectClock.RestartForJoinRealm(isStatic);
        ++ObjectTimerEpoch;
    }

    internal bool TryDequeuePhaseChangeover(out CanonKineticShift changeover) => _queuedShifts.TryDequeue(out changeover);

    // Applies a raw wire state through retail's transition table and queues the shift for consumers
    internal CanonKineticShift ImposeRawKineticsPhase(uint rawPhase)
    {
        ++PhaseArbiterVer;
        ++KineticsPhaseAlterationVer;
        RawKineticsCondition = rawPhase;
        var shift = CanonKineticShifts.Apply(FinalKineticsCondition, (KineticStateFlags)rawPhase);
        FinalKineticsCondition = shift.FinalState;
        MirrorPhaseToCorpus();
        _queuedShifts.Enqueue(shift);
        return shift;
    }

    internal void AssignDescendantNoPaint(bool noPaint)
    {
        ++KineticsPhaseAlterationVer;
        FinalKineticsCondition = noPaint ? FinalKineticsCondition | KineticStateFlags.NoDraw : FinalKineticsCondition & ~KineticStateFlags.NoDraw;
        MirrorPhaseToCorpus();
    }

    internal void AssignFinalKineticsPhase(KineticStateFlags phase)
    {
        if (phase == FinalKineticsCondition)
            return;
        ++KineticsPhaseAlterationVer;
        FinalKineticsCondition = phase;
    }

    internal void AssignKineticsCorpus(KineticBody? corpus)
    {
        if (ReferenceEquals(KineticBody, corpus))
            return;
        KineticBody = corpus;
        ++KineticsOwnershipEpoch;
    }

    // Clears the missile flags after impact; false when nothing changed (or it was not a missile when
    // required)
    internal bool HaltMissileFollowingImpact(bool demandLatestMissile)
    {
        const KineticStateFlags MissileBitset = KineticStateFlags.Missile | KineticStateFlags.AlignPath | KineticStateFlags.PathClipped;
        if (demandLatestMissile && (FinalKineticsCondition & KineticStateFlags.Missile) == 0)
            return false;
        var stopped = FinalKineticsCondition & ~MissileBitset;
        if (stopped == FinalKineticsCondition)
            return false;
        ++KineticsPhaseAlterationVer;
        FinalKineticsCondition = stopped;
        MirrorPhaseToCorpus();
        return true;
    }

    internal void RenewDerivedPhase(bool renewLocus = true)
    {
        if (renewLocus && Snapshot.Position is { } locus)
            AssignWholeChamber(locus.LandblockId, (locus.LandblockId & 0xFFFF0000u) | 0xFFFFu);
        RawKineticsCondition = Snapshot.Physics?.RawState ?? Snapshot.PhysicsState ?? 0u;
    }

    internal void AssignWholeChamber(uint wholeChamberIdent, uint canonLbIdent)
    {
        ++SpatialAuthorityVersion;
        WholeChamberTag = wholeChamberIdent;
        CanonLbTag = canonLbIdent;
    }

    // Pushes the final state into the body, when there is one
    private void MirrorPhaseToCorpus()
    {
        if (KineticBody is not null)
            KineticBody.State = FinalKineticsCondition;
    }
}
