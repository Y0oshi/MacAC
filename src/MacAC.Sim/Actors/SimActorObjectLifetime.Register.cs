using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Play;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class SimActorObjectLifetime
{
    public SimActorRegistrationResult EnrollActor(
        RealmSession.MoverSpawn incoming,
        Func<SimActorRecord, Exception?>? retirePrecedingProj = null)
    {
        return RegisterEntityCore(
            incoming,
            commenceStartingResidence: false,
            isOwnAvatar: false,
            retirePrecedingProj);
    }

    internal SimActorRegistrationResult EnrollActorWithStartingResidence(
        RealmSession.MoverSpawn incoming,
        bool isOwnAvatar,
        Func<SimActorRecord, Exception?>? retirePrecedingProj = null)
    {
        return RegisterEntityCore(
            incoming,
            commenceStartingResidence: true,
            isOwnAvatar,
            retirePrecedingProj);
    }

    private SimActorRegistrationResult RegisterEntityCore(
        RealmSession.MoverSpawn incoming,
        bool commenceStartingResidence,
        bool isOwnAvatar,
        Func<SimActorRecord, Exception?>? retirePrecedingProj)
    {
        Live();
        if (commenceStartingResidence && isOwnAvatar)
        {
            Physics.WatchOwnAvatarBuild(
                (incoming.Physics?.Position ?? incoming.Position)
                    ?.LandblockId ?? 0u);
        }
        if (_clearingSess)
        {
            throw new InvalidOperationException(
                "A Runtime entity can't register while its session lifetime is clearing");
        }
        if (commenceStartingResidence
            && !BuildPersonaConsistent(incoming))
        {
            throw new InvalidOperationException(
                $"ObjectCreation 0x{incoming.Guid:X8} has inconsistent instance or parent projections");
        }
        if (commenceStartingResidence)
            incoming = SimSpawnIntakeLocker.Freeze(incoming);
        uint ancestorOid = incoming.ParentGuid
            ?? incoming.Physics?.Parent?.Guid
            ?? 0u;
        if (commenceStartingResidence
            && ancestorOid is not 0u
            && !Entities.TryFetchEngaged(ancestorOid, out _))
        {
            Entities.AncestorAttachments.EnqueueDeferredCreate(
                incoming,
                isOwnAvatar);
            return new SimActorRegistrationResult(
                SupersededIncoming(),
                Canonical: null,
                LogicalRegistrationCreated: false,
                ReplacedExistingGeneration: false,
                DeferredForParent: true);
        }
        var preview =
            Entities.PreviewCreateDisposition(incoming);
        bool requiresFreshResidenceAdmission = preview is
            SpawnStampVerdict.InitialGeneration
            or SpawnStampVerdict.NewGeneration;
        if (commenceStartingResidence
            && requiresFreshResidenceAdmission
            && !StartingBuildResidences.CanAdmitBuild(incoming))
        {
            throw new InvalidOperationException(
                $"ObjectCreation 0x{incoming.Guid:X8} can't acquire a structurally valid initial residence lease");
        }

        SimActorRecord? queuedResidenceCapture = null;
        SimSpawnTenancyLease queuedResidence = default;
        bool admitIntoQueuedResidence = commenceStartingResidence
            && preview is SpawnStampVerdict.ExistingGeneration
            && Entities.TryFetchEngaged(
                incoming.Guid,
                out queuedResidenceCapture)
            && StartingBuildResidences.TryFetchTransaction(
                queuedResidenceCapture,
                out queuedResidence);
        if (admitIntoQueuedResidence
            && !StartingBuildResidences.CanQueue(
                queuedResidenceCapture!,
                queuedResidence))
        {
            throw new InvalidOperationException(
                $"ObjectCreation 0x{incoming.Guid:X8} can't append to its pending initial residence FIFO");
        }
        if (admitIntoQueuedResidence
            && !PinnedBuildWellFormed(incoming))
        {
            _ = Entities.TryFetchApprovedTimestamps(
                incoming.Guid,
                out GrantedKineticsTimestamps timestamps);
            return new SimActorRegistrationResult(
                new IncomingBuildOutcome(
                    SpawnStampVerdict.ExistingGeneration,
                    queuedResidenceCapture!.Snapshot,
                    SameGenerationEvents: null,
                    timestamps),
                queuedResidenceCapture,
                LogicalRegistrationCreated: false,
                ReplacedExistingGeneration: false);
        }

        IncomingBuildOutcome outcome = admitIntoQueuedResidence
            ? Entities.AdmitBuildPostponedSameGen(incoming)
            : Entities.AdmitBuild(incoming);
        if (outcome.Disposition
            is SpawnStampVerdict.StaleGeneration)
        {
            return new SimActorRegistrationResult(
                outcome,
                Canonical: null,
                LogicalRegistrationCreated: false,
                ReplacedExistingGeneration: false);
        }

        ulong sessVer = Entities.SessionLifetimeVersion;
        ulong opVer =
            Entities.ProgressLifespanAlteration(incoming.Guid);

        if (outcome.Disposition
            is SpawnStampVerdict.ExistingGeneration)
        {
            if (Entities.TryFetchEngaged(
                    incoming.Guid,
                    out SimActorRecord kept))
            {
                if (admitIntoQueuedResidence)
                {
                    if (!ReferenceEquals(kept, queuedResidenceCapture)
                        || !AdmitSameEpochBuild(
                            kept,
                            queuedResidence,
                            incoming,
                            outcome,
                            isOwnAvatar))
                    {
                        throw TenancyMiss(
                            kept,
                            broadcastDeleted: true);
                    }

                    var dormant = outcome with
                    {
                        Snapshot = kept.Snapshot,
                        SameGenerationEvents = null,
                    };
                    return new SimActorRegistrationResult(
                        dormant,
                        kept,
                        LogicalRegistrationCreated: false,
                        ReplacedExistingGeneration: false);
                }

                Entities.RenewCapture(
                    kept,
                    outcome.Snapshot,
                    renewLocus: !commenceStartingResidence);
                if (!commenceStartingResidence)
                    Entities.ProgressBuildArbiter(kept);
                PublishEntity(SimActorChange.Updated, kept);
                if (!OpHolds(
                        incoming.Guid,
                        kept,
                        sessVer,
                        opVer))
                {
                    return SupersededEnrollment(
                        incoming.Guid,
                        replacedExtantGen: false);
                }
                return new SimActorRegistrationResult(
                    outcome,
                    kept,
                    LogicalRegistrationCreated: false,
                    ReplacedExistingGeneration: false);
            }

            if (Entities.TryGetTeardown(
                    incoming.Guid,
                    outcome.Snapshot.InstanceSequence,
                    out _))
            {
                return new SimActorRegistrationResult(
                    outcome,
                    Canonical: null,
                    LogicalRegistrationCreated: false,
                    ReplacedExistingGeneration: false);
            }

            if (commenceStartingResidence
                && !StartingBuildResidences.CanAdmitBuild(outcome.Snapshot))
            {
                throw new InvalidOperationException(
                    $"Recovered ObjectCreation 0x{incoming.Guid:X8} can't acquire a structurally valid initial residence lease");
            }

            var recovered = Entities.AppendEngaged(outcome.Snapshot);
            if (!OpenTenancy(
                    recovered,
                    outcome,
                    commenceStartingResidence,
                    isOwnAvatar))
            {
                throw TenancyMiss(
                    recovered,
                    broadcastDeleted: false);
            }
            PublishEntity(SimActorChange.Registered, recovered);
            if (!OpHolds(
                    incoming.Guid,
                    recovered,
                    sessVer,
                    opVer))
            {
                return SupersededEnrollment(
                    incoming.Guid,
                    replacedExtantGen: false);
            }
            return new SimActorRegistrationResult(
                outcome,
                recovered,
                LogicalRegistrationCreated: true,
                ReplacedExistingGeneration: false);
        }

        bool replaced = Entities.RemoveActive(
            incoming.Guid,
            out SimActorRecord? preceding);
        if (outcome.Disposition
            is SpawnStampVerdict.NewGeneration)
        {
            if (preceding is not null)
            {
                UnseatSealedDescendants(
                    preceding.ServerGuid,
                    preceding.Incarnation);
            }
            Entities.AncestorAttachments.FinishGen(
                incoming.Guid,
                outcome.Snapshot.InstanceSequence);
        }

        Exception? tidyMiss = null;
        if (preceding is not null)
        {
            Entities.HoldTeardown(preceding);
            try
            {
                PublishEntity(SimActorChange.Deleted, preceding);
            }
            catch (Exception problem)
            {
                tidyMiss = problem;
            }

            Exception? projMiss = retirePrecedingProj is null
                ? RetireCanonSole(preceding)
                : retirePrecedingProj(preceding);
            tidyMiss = Merge(tidyMiss, projMiss);
        }

        if (Entities.SessionLifetimeVersion != sessVer
            || Entities.LatestLifespanAlteration(incoming.Guid)
                != opVer)
        {
            if (tidyMiss is not null)
            {
                throw new AggregateException(
                    $"Prior incarnation of live entity 0x{incoming.Guid:X8} failed teardown while its incoming replacement was superseded",
                    tidyMiss);
            }

            return new SimActorRegistrationResult(
                SupersededIncoming(),
                Entities.TryFetchEngaged(
                    incoming.Guid,
                    out SimActorRecord latest)
                        ? latest
                        : null,
                LogicalRegistrationCreated: false,
                ReplacedExistingGeneration: replaced);
        }

        var canon = Entities.AppendEngaged(outcome.Snapshot);
        if (!OpenTenancy(
                canon,
                outcome,
                commenceStartingResidence,
                isOwnAvatar))
        {
            throw TenancyMiss(
                canon,
                broadcastDeleted: false);
        }
        try
        {
            PublishEntity(SimActorChange.Registered, canon);
        }
        catch (Exception problem)
        {
            if (tidyMiss is not null)
            {
                throw new AggregateException(
                    $"Live entity 0x{incoming.Guid:X8} registered after prior cleanup and commit observers failed",
                    tidyMiss,
                    problem);
            }
            throw;
        }

        if (!OpHolds(
                incoming.Guid,
                canon,
                sessVer,
                opVer))
        {
            if (tidyMiss is not null)
            {
                throw new AggregateException(
                    $"Prior incarnation of live entity 0x{incoming.Guid:X8} failed teardown while its committed replacement was superseded",
                    tidyMiss);
            }

            return SupersededEnrollment(
                incoming.Guid,
                replaced);
        }

        return new SimActorRegistrationResult(
            outcome,
            canon,
            LogicalRegistrationCreated: true,
            ReplacedExistingGeneration: replaced,
            tidyMiss);
    }

    private bool AdmitSameEpochBuild(
        SimActorRecord canon,
        in SimSpawnTenancyLease preceding,
        in RealmSession.MoverSpawn incoming,
        in IncomingBuildOutcome admitted,
        bool isOwnAvatar)
    {
        if (!ReferenceEquals(
                canon,
                Entities.TryFetchEngaged(
                    incoming.Guid,
                    out SimActorRecord latest)
                        ? latest
                        : null)
            || canon.Incarnation != incoming.InstanceSequence
            || admitted.Disposition
                is not SpawnStampVerdict.ExistingGeneration
            || !StartingBuildResidences.CanQueue(canon, preceding))

            return false;

        var acts = ImmutableArray.CreateBuilder<
            SimSpawnTailAction>();
        var locusSrc =
            SimGrantedPositionSource.Unknown;

        if (admitted.SameGenerationEvents is { } signals)
        {
            acts.Add(new SimSpawnTailAction(
                SimSpawnTailActionKind
                    .PreTailDescriptionAdaptation,
                incoming.Guid,
                Description: signals.Description));

            if (Entities.TryAdmitPostponedObjRefDsc(
                    signals.Appearance,
                    out _))
            {
                acts.Add(new SimSpawnTailAction(
                    SimSpawnTailActionKind.ObjDesc,
                    incoming.Guid,
                    ObjDesc: signals.Appearance));
            }

            if (signals.Parent is { } ancestor)
            {
                if (Entities.TryAdmitPostponedBuildAncestor(
                        ancestor,
                        out _))
                {
                    acts.Add(new SimSpawnTailAction(
                        SimSpawnTailActionKind.CreateParent,
                        incoming.Guid,
                        CreateParent: ancestor));
                }
            }
            else if (signals.Position is { } locus)
            {
                if (!SimSovereignPositionRouteSorter
                        .IsValidBuildWireLocus(locus.Position)
                    || locus.Velocity is { } vel
                        && !Finite(vel))

                    return false;

                if (!Entities.TryAdmitPostponedLocus(
                        locus,
                        isOwnAvatar,
                        out PoseStampVerdict disposition,
                        out GrantedKineticsTimestamps timestamps,
                        out bool stampAlteration))

                    return false;
                if (disposition is not PoseStampVerdict.Rejected
                    || stampAlteration)
                {
                    locusSrc = SimGrantedPositionSource
                        .SameIncarnationCreate;
                    acts.Add(new SimSpawnTailAction(
                        SimSpawnTailActionKind.Position,
                        incoming.Guid,
                        Position: locus,
                        PositionSource: locusSrc,
                        PositionDisposition: disposition,
                        PreviousTeleportSequence:
                            timestamps.PreviousTeleport,
                        AcceptedTimestamps: timestamps,
                        HasTimestampMutation: stampAlteration));
                }
            }
            else if (signals.Pickup is { } lift
                && Entities.TryAdmitPostponedLift(lift, out _))
            {
                acts.Add(new SimSpawnTailAction(
                    SimSpawnTailActionKind.Pickup,
                    incoming.Guid,
                    Pickup: lift));
            }

            if (signals.Movement is { } travel)
            {
                bool cargoImposed = Entities.TryAdmitPostponedLocomotion(
                    travel,
                    out GrantedKineticsTimestamps timestamps,
                    out bool stampAlteration);
                if (cargoImposed || stampAlteration)
                {
                    acts.Add(new SimSpawnTailAction(
                        SimSpawnTailActionKind.Movement,
                        incoming.Guid,
                        Movement: travel,
                        AcceptedTimestamps: timestamps,
                        AppliesMovementPayload: cargoImposed,
                        RetainMovementPayload: true,
                        HasTimestampMutation: stampAlteration));
                }
            }

            if (Entities.TryAdmitPostponedPhase(signals.State, out _))
            {
                acts.Add(new SimSpawnTailAction(
                    SimSpawnTailActionKind.State,
                    incoming.Guid,
                    State: signals.State));
            }
            if (Entities.TryAdmitPostponedVector(signals.Vector, out _))
            {
                acts.Add(new SimSpawnTailAction(
                    SimSpawnTailActionKind.Vector,
                    incoming.Guid,
                    Vector: signals.Vector));
            }
        }

        acts.Add(new SimSpawnTailAction(
            SimSpawnTailActionKind.WeenieDescription,
            incoming.Guid,
            WeenieDescription: incoming));
        acts.Add(new SimSpawnTailAction(
            SimSpawnTailActionKind.ResidentCellCleanup,
            incoming.Guid));

        var kept = StartingBuildResidences
            .QueueApproved(
                canon,
                preceding,
                SimSpawnFollowupKind.SameIncarnationCreate,
                locusSrc,
                acts.ToImmutable());
        return kept.IsValid;
    }

    private static bool BuildPersonaConsistent(
        in RealmSession.MoverSpawn incoming)
    {
        if (incoming.Guid is 0u)
            return false;

        bool hasTopAncestorOid = incoming.ParentGuid is not null;
        bool hasTopAncestorLocale = incoming.ParentLocation is not null;
        if (hasTopAncestorOid != hasTopAncestorLocale)
            return false;

        if (incoming.Physics is not { } kinetics)
        {
            return incoming.Position is null
                && incoming.SetupTableId is null
                && incoming.MotionState is null
                && incoming.MotionTableId is null
                && incoming.PhysicsState is null
                && incoming.ObjScale is null
                && incoming.Friction is null
                && incoming.Elasticity is null
                && incoming.InstanceSequence is 0
                && incoming.MovementSequence is 0
                && incoming.ServerControlSequence is 0
                && incoming.PositionSequence is 0
                && !hasTopAncestorOid
                && incoming.PlacementId is null;
        }
        if (kinetics.Timestamps.Instance != incoming.InstanceSequence
            || kinetics.Timestamps.Position != incoming.PositionSequence
            || kinetics.Timestamps.Movement != incoming.MovementSequence
            || kinetics.Timestamps.ServerControlledMove
                != incoming.ServerControlSequence
            || kinetics.Position != incoming.Position)
            return false;

        KineticAttachment? flattenedAncestor = incoming.ParentGuid is { } ancestorOid
            && incoming.ParentLocation is { } ancestorLocale
                ? new KineticAttachment(ancestorOid, ancestorLocale)
                : null;
        if (flattenedAncestor != kinetics.Parent
            || incoming.PlacementId != kinetics.AnimationFrame)

            return false;
        return true;
    }

    private static bool PinnedBuildWellFormed(
        in RealmSession.MoverSpawn incoming)
    {
        if (incoming.Guid is 0u)
            return false;
        if (incoming.Physics is not { } kinetics)
            return true;
        if (kinetics.Parent is null
            && kinetics.Position is { LandblockId: not 0u } locus
            && !SimSovereignPositionRouteSorter
                .IsValidBuildWireLocus(locus))

            return false;
        if (kinetics.Velocity is { } vel && !Finite(vel)
            || kinetics.Acceleration is { } acceleration
                && !Finite(acceleration)
            || kinetics.AngularVelocity is { } angularVel
                && !Finite(angularVel)
            || kinetics.Scale is { } scaling && !float.IsFinite(scaling)
            || kinetics.Friction is { } friction && !float.IsFinite(friction)
            || kinetics.Elasticity is { } elasticity
                && !float.IsFinite(elasticity)
            || kinetics.Translucency is { } seeThrough
                && !float.IsFinite(seeThrough))

            return false;
        return true;
    }

    private bool TryFetchOpenTenancy(
        uint oid,
        out SimActorRecord canon,
        out SimSpawnTenancyLease tenancy)
    {
        if (Entities.TryFetchEngaged(oid, out canon)
            && StartingBuildResidences.TryFetchTransaction(
                canon,
                out tenancy))

            return true;

        canon = null!;
        tenancy = default;
        return false;
    }

    private void ParkDormant(
        SimActorRecord canon,
        in SimSpawnTenancyLease preceding,
        SimSpawnFollowupKind sort,
        SimGrantedPositionSource locusSrc,
        in SimSpawnTailAction act)
    {
        var kept = StartingBuildResidences
            .QueueApproved(
                canon,
                preceding,
                sort,
                locusSrc,
                ImmutableArray.Create(act));
        if (!kept.IsValid)
        {
            throw new InvalidOperationException(
                $"Accepted {sort} for 0x{canon.ServerGuid:X8}/{canon.Incarnation} could not be retained by its initial-placement FIFO");
        }
    }

    private bool OpenTenancy(
        SimActorRecord canon,
        in IncomingBuildOutcome approved,
        bool commenceStartingResidence,
        bool isOwnAvatar)
    {
        if (!commenceStartingResidence)
            return true;

        if (canon.PositionAuthorityVersion is 0UL)
            Entities.ProgressLocusArbiter(canon);
        if (canon.WholeChamberTag is not 0u)
            Entities.AssignWholeChamber(canon, 0u, 0u);
        var tenancy =
            StartingBuildResidences.Begin(
                canon,
                approved,
                isOwnAvatar);
        if (!tenancy.IsValid)
            return false;
        _tenancyBegan?.Invoke(canon);
        return true;
    }

    private Exception TenancyMiss(
        SimActorRecord canon,
        bool broadcastDeleted)
    {
        if (!Entities.RemoveActive(canon))
        {
            return new InvalidOperationException(
                $"Initial residence for 0x{canon.ServerGuid:X8} failed after its canonical incarnation was superseded");
        }

        Exception? miss = null;
        if (broadcastDeleted)
        {
            try
            {
                PublishEntity(SimActorChange.Deleted, canon);
            }
            catch (Exception problem)
            {
                miss = problem;
            }
        }
        miss = Merge(miss, RetireCanonSole(canon));
        var cause = new InvalidOperationException(
            $"Initial residence for 0x{canon.ServerGuid:X8} could not acquire its exact Runtime placement lease");
        return miss is null
            ? cause
            : new AggregateException(cause, miss);
    }

    private static Exception? Merge(
        Exception? lead,
        Exception? second)
    {
        return lead is null
            ? second
            : second is null
                ? lead
                : new AggregateException(lead, second);
    }

    private static IncomingBuildOutcome SupersededIncoming()
    {
        return new(
        SpawnStampVerdict.StaleGeneration,
        default,
        null,
        default);
    }

    private bool OpHolds(
        uint oid,
        SimActorRecord canon,
        ulong sessVer,
        ulong opVer)
    {
        return Entities.SessionLifetimeVersion == sessVer
        && Entities.LatestLifespanAlteration(oid) == opVer
        && Entities.IsCurrent(canon);
    }

    private SimActorRegistrationResult SupersededEnrollment(
        uint oid,
        bool replacedExtantGen)
    {
        return new(
            SupersededIncoming(),
            Entities.TryFetchEngaged(oid, out SimActorRecord latest)
                ? latest
                : null,
            LogicalRegistrationCreated: false,
            ReplacedExistingGeneration: replacedExtantGen);
    }
}
