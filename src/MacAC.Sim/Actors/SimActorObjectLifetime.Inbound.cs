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

    public bool ImposeApprovedSummon(
        SimActorRecord canon,
        ulong anticipatedBuildIntegrationVer,
        RealmSession.MoverSpawn summon,
        bool replaceGen)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        if (canon.ServerGuid != summon.Guid
            || canon.Incarnation != summon.InstanceSequence)

            return false;

        bool IsLatest() =>
            Entities.IsCurrent(canon)
            && canon.BuildIntegrationVersion
                == anticipatedBuildIntegrationVer;

        return IsLatest()
            && ObjectTableBindings.ImposeActorSummon(
                Objects,
                summon,
                replaceGen,
                IsLatest)
            && IsLatest();
    }

    public bool TryApplyObjDsc(
        ObjDescNotice.Parsed refresh,
        Action<SimActorRecord>? acknowledgeProj,
        out RealmSession.MoverSpawn approved)
    {
        Live();
        if (TryFetchOpenTenancy(
                refresh.Guid,
                out SimActorRecord queued,
                out SimSpawnTenancyLease tenancy))
        {
            if (!StartingBuildResidences.CanQueue(queued, tenancy))
            {
                approved = queued.Snapshot;
                return false;
            }
            bool approvedByLatch = Entities.TryAdmitPostponedObjRefDsc(
                refresh,
                out _);
            approved = queued.Snapshot;
            if (!approvedByLatch)
                return false;
            ParkDormant(
                queued,
                tenancy,
                SimSpawnFollowupKind.ObjDesc,
                SimGrantedPositionSource.Unknown,
                new SimSpawnTailAction(
                    SimSpawnTailActionKind.ObjDesc,
                    refresh.Guid,
                    ObjDesc: refresh));
            return true;
        }
        bool imposed = Entities.TryApplyObjRefDesc(refresh, out approved);
        if (!imposed
            || !Entities.TryFetchEngaged(
                refresh.Guid,
                out SimActorRecord canon))

            return imposed;

        Entities.RenewCapture(canon, approved);
        Entities.ProgressObjRefDscArbiter(canon);
        ulong arbiterVer = canon.ObjRefDscArbiterVer;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Updated,
            () => canon.ObjRefDscArbiterVer == arbiterVer);
    }

    public bool TryApplyPickup(
        PickupNotice.Parsed refresh,
        Action<SimActorRecord>? acknowledgeProj,
        out RealmSession.MoverSpawn approved)
    {
        Live();
        if (TryFetchOpenTenancy(
                refresh.Guid,
                out SimActorRecord queued,
                out SimSpawnTenancyLease tenancy))
        {
            if (!StartingBuildResidences.CanQueue(queued, tenancy))
            {
                approved = queued.Snapshot;
                return false;
            }
            bool approvedByLatch = Entities.TryAdmitPostponedLift(
                refresh,
                out _);
            approved = queued.Snapshot;
            if (!approvedByLatch)
                return false;
            ParkDormant(
                queued,
                tenancy,
                SimSpawnFollowupKind.Pickup,
                SimGrantedPositionSource.Unknown,
                new SimSpawnTailAction(
                    SimSpawnTailActionKind.Pickup,
                    refresh.Guid,
                    Pickup: refresh));
            return true;
        }
        bool imposed = Entities.TryApplyLift(refresh, out approved);
        if (!imposed
            || !Entities.TryFetchEngaged(
                refresh.Guid,
                out SimActorRecord canon))

            return imposed;

        Entities.RenewCapture(canon, approved);
        var startingAbort =
            DiscardTenancy(canon);
        Entities.ProgressLocusArbiter(canon);
        Entities.AncestorAttachments.FinishDescendantProj(refresh.Guid);
        Physics.ImpactDossiers.ExitRealm(canon);
        var plainAbort =
            Physics.SetPosition.Drop(canon);
        var abort =
            StrongerCancel(startingAbort, plainAbort);
        Entities.SuspendObjectTimer(canon);
        Entities.AssignWholeChamber(canon, 0u, 0u);
        ulong locusVer = canon.PositionAuthorityVersion;
        ulong spatialVer = canon.SpatialAuthorityVersion;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Withdrawn,
            () => canon.PositionAuthorityVersion == locusVer
                && canon.SpatialAuthorityVersion == spatialVer,
            abort);
    }

    public bool TryApplyCreateAncestor(
        CreateAnchorUpdate refresh,
        Action<SimActorRecord>? acknowledgeProj,
        out RealmSession.MoverSpawn approved)
    {
        Live();
        if (TryFetchOpenTenancy(
                refresh.ChildGuid,
                out _,
                out _))
        {
            throw new InvalidOperationException(
                "A ObjectCreation parent relation has to be admitted inside its atomic same-generation Create envelope");
        }
        bool imposed = Entities.TryApplyBuildParent(refresh, out approved);
        return SealLocusLane(
            imposed,
            refresh.ChildGuid,
            approved,
            acknowledgeProj);
    }

    public bool TryApplyParent(
        AncestorSignal.Parsed refresh,
        Action<SimActorRecord>? acknowledgeProj,
        out RealmSession.MoverSpawn approved)
    {
        Live();
        if (TryFetchOpenTenancy(
                refresh.ChildGuid,
                out SimActorRecord queued,
                out SimSpawnTenancyLease tenancy))
        {
            if (!StartingBuildResidences.CanQueue(queued, tenancy))
            {
                approved = queued.Snapshot;
                return false;
            }
            if (!Entities.TryFetchEngaged(
                    refresh.ParentGuid,
                    out SimActorRecord ancestor)
                || ancestor.Incarnation != refresh.ParentInstanceSequence)
            {
                Entities.AncestorAttachments.Enqueue(refresh);
                approved = queued.Snapshot;
                return false;
            }

            bool approvedByLatch = Entities.TryAdmitPostponedAncestor(
                refresh,
                out GrantedKineticsTimestamps timestamps);
            approved = queued.Snapshot;
            if (!approvedByLatch)
                return false;
            ParkDormant(
                queued,
                tenancy,
                SimSpawnFollowupKind.Parent,
                SimGrantedPositionSource.Unknown,
                new SimSpawnTailAction(
                    SimSpawnTailActionKind.Parent,
                    refresh.ChildGuid,
                    Parent: refresh,
                    AcceptedTimestamps: timestamps));
            return true;
        }
        bool imposed = Entities.TryApplyAncestor(refresh, out approved);
        return SealLocusLane(
            imposed,
            refresh.ChildGuid,
            approved,
            acknowledgeProj);
    }

    public bool TryCommitAncestor(
        AnchorAttachmentRelation relation,
        Action<SimActorRecord>? acknowledgeProj,
        out RealmSession.MoverSpawn approved)
    {
        Live();
        bool committed = Entities.TrySealParent(
            relation.ChildGuid,
            relation.ParentGuid,
            relation.ParentLocation,
            relation.PlacementId,
            relation.ChildPositionSequence,
            out approved);
        if (!committed
            || !Entities.TryFetchEngaged(
                relation.ChildGuid,
                out SimActorRecord canon))

            return committed;

        Entities.RenewCapture(canon, approved);
        var startingAbort =
            DiscardTenancy(canon);
        var plainAbort =
            Physics.SetPosition.Drop(canon);
        var abort =
            StrongerCancel(startingAbort, plainAbort);
        Entities.ProgressAncestorSeal(canon);
        ulong ancestorSealVer = canon.AncestorSealVer;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Updated,
            () => canon.AncestorSealVer
                == ancestorSealVer,
            abort);
    }

    public bool SealApprovedAncestorCellless(
        SimActorRecord canon,
        ulong locusArbiterVer,
        Action<SimActorRecord>? acknowledgeProj)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        if (!Entities.IsCurrent(canon)
            || canon.PositionAuthorityVersion != locusArbiterVer)

            return false;

        var startingAbort =
            DiscardTenancy(canon);
        var plainAbort =
            Physics.SetPosition.Drop(canon);
        var abort =
            StrongerCancel(startingAbort, plainAbort);
        Physics.ImpactDossiers.ExitRealm(canon);
        Entities.SuspendObjectTimer(canon);
        Entities.AssignWholeChamber(canon, 0u, 0u);
        if (Entities.AncestorAttachments.TryFetchSealedAncestor(
                canon.ServerGuid,
                out uint ancestorOid,
                out ushort ancestorInstSeries)
            && Entities.TryFetchEngaged(ancestorOid, out SimActorRecord ancestor)
            && ancestor.Incarnation == ancestorInstSeries
            && ancestor.WholeChamberTag is not 0u)
        {
            if (KineticTelemetry.ProbeChildCellEnabled)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[child-cell] parent=0x{ancestorOid:X8} child=0x{canon.ServerGuid:X8} old=0x00000000 new=0x{ancestor.WholeChamberTag:X8} cause=attach"));
            }
            Entities.AssignWholeChamber(
                canon,
                ancestor.WholeChamberTag,
                ancestor.CanonLbTag);
        }
        ulong spatialVer = canon.SpatialAuthorityVersion;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Withdrawn,
            () => canon.PositionAuthorityVersion
                    == locusArbiterVer
                && canon.SpatialAuthorityVersion == spatialVer,
            abort);
    }

    public bool TryApplyMotion(
        RealmSession.MoverMotionUpdate refresh,
        bool retainCargo,
        Action<SimActorRecord>? acknowledgeProj,
        out RealmSession.MoverSpawn approved,
        out GrantedKineticsTimestamps timestamps)
    {
        Live();
        if (TryFetchOpenTenancy(
                refresh.Guid,
                out SimActorRecord queued,
                out SimSpawnTenancyLease tenancy))
        {
            if (!StartingBuildResidences.CanQueue(queued, tenancy))
            {
                approved = queued.Snapshot;
                timestamps = default;
                return false;
            }
            bool cargoImposed = Entities.TryAdmitPostponedLocomotion(
                refresh,
                out timestamps,
                out bool stampAlteration);
            approved = queued.Snapshot;
            if (!cargoImposed && !stampAlteration)
                return false;
            ParkDormant(
                queued,
                tenancy,
                SimSpawnFollowupKind.Movement,
                SimGrantedPositionSource.Unknown,
                new SimSpawnTailAction(
                    SimSpawnTailActionKind.Movement,
                    refresh.Guid,
                    Movement: refresh,
                    AcceptedTimestamps: timestamps,
                    AppliesMovementPayload: cargoImposed,
                    RetainMovementPayload: retainCargo,
                    HasTimestampMutation: stampAlteration));
            return cargoImposed;
        }
        bool imposed = Entities.TryApplyLocomotion(
            refresh,
            retainCargo,
            out approved,
            out timestamps);
        if (Entities.TryGetSnapshot(
                refresh.Guid,
                out RealmSession.MoverSpawn capture)
            && Entities.TryFetchEngaged(
                refresh.Guid,
                out SimActorRecord canon))
        {
            Entities.RenewCapture(canon, capture);
            if (imposed && retainCargo)
                Entities.ProgressTravelArbiter(canon);
            if (imposed)
            {
                Entities.ProgressTravelSeal(canon);
                ulong travelSealVer =
                    canon.TravelSealVer;
                return AckProjThenBroadcast(
                    canon,
                    () => acknowledgeProj?.Invoke(canon),
                    SimActorChange.Updated,
                    () => canon.TravelSealVer
                        == travelSealVer);
            }

            acknowledgeProj?.Invoke(canon);
        }

        return imposed;
    }

    public bool TryApplyVector(
        VelocityUpdate.Parsed refresh,
        Action<SimActorRecord>? acknowledgeProj,
        out RealmSession.MoverSpawn approved)
    {
        Live();
        if (TryFetchOpenTenancy(
                refresh.Guid,
                out SimActorRecord queued,
                out SimSpawnTenancyLease tenancy))
        {
            if (!Finite(refresh.Velocity)
                || !Finite(refresh.Omega)
                || !StartingBuildResidences.CanQueue(queued, tenancy))
            {
                approved = queued.Snapshot;
                return false;
            }
            bool approvedByLatch = Entities.TryAdmitPostponedVector(
                refresh,
                out _);
            approved = queued.Snapshot;
            if (!approvedByLatch)
                return false;
            ParkDormant(
                queued,
                tenancy,
                SimSpawnFollowupKind.Vector,
                SimGrantedPositionSource.Unknown,
                new SimSpawnTailAction(
                    SimSpawnTailActionKind.Vector,
                    refresh.Guid,
                    Vector: refresh));
            return true;
        }
        bool imposed = Entities.TryApplyVector(refresh, out approved);
        if (!imposed
            || !Entities.TryFetchEngaged(
                refresh.Guid,
                out SimActorRecord canon))

            return imposed;

        Entities.RenewCapture(canon, approved);
        Entities.ProgressVectorArbiter(canon);
        ulong arbiterVer = canon.VectorArbiterVer;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Updated,
            () => canon.VectorArbiterVer == arbiterVer);
    }

    public bool TryEnactState(
        GroupPhase.Parsed refresh,
        Action<SimActorRecord, CanonKineticShift>?
            acknowledgeProj,
        out RealmSession.MoverSpawn approved,
        out CanonKineticShift changeover)
    {
        Live();
        if (TryFetchOpenTenancy(
                refresh.Guid,
                out SimActorRecord queued,
                out SimSpawnTenancyLease tenancy))
        {
            if (!StartingBuildResidences.CanQueue(queued, tenancy))
            {
                approved = queued.Snapshot;
                changeover = default;
                return false;
            }
            bool approvedByLatch = Entities.TryAdmitPostponedPhase(
                refresh,
                out _);
            approved = queued.Snapshot;
            changeover = default;
            if (!approvedByLatch)
                return false;
            ParkDormant(
                queued,
                tenancy,
                SimSpawnFollowupKind.State,
                SimGrantedPositionSource.Unknown,
                new SimSpawnTailAction(
                    SimSpawnTailActionKind.State,
                    refresh.Guid,
                    State: refresh));
            return true;
        }
        bool imposed = Entities.TryEnactCondition(refresh, out approved);
        changeover = default;
        if (!imposed
            || !Entities.TryFetchEngaged(
                refresh.Guid,
                out SimActorRecord canon))

            return imposed;

        Entities.RenewCapture(canon, approved);
        var preview =
            CanonKineticShifts.Apply(
                canon.FinalKineticsCondition,
                (KineticStateFlags)refresh.PhysicsState);
        ulong precedingKineticsAlteration = canon.KineticsPhaseAlterationVer;
        if (preview.HiddenTransition is CanonHiddenShift.BecameHidden)
        {
            Physics.ImpactDossiers.ExitRealm(canon);
            if (!Entities.IsCurrent(canon)
                || canon.KineticsPhaseAlterationVer
                    != precedingKineticsAlteration)
            {
                changeover = default;
                return false;
            }
        }
        changeover = Entities.ImposeRawKineticsPhase(
            canon,
            refresh.PhysicsState);
        if (canon.Key is { } tag)
        {
            Physics.Engine.ShadeObjects.RefreshKineticsPhase(
                tag.LocalEntityId,
                (uint)canon.FinalKineticsCondition);
        }
        ulong phaseVer = canon.PhaseArbiterVer;
        ulong kineticsAlterationVer =
            canon.KineticsPhaseAlterationVer;
        var sealedChangeover = changeover;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(
                canon,
                sealedChangeover),
            sealedChangeover.HiddenTransition
                is CanonHiddenShift.BecameHidden
                    ? SimActorChange.Hidden
                    : SimActorChange.Updated,
            () => canon.PhaseArbiterVer == phaseVer
                && canon.KineticsPhaseAlterationVer
                    == kineticsAlterationVer);
    }

    public bool TryApplyLocus(
        RealmSession.MoverPositionUpdate refresh,
        bool isOwnAvatar,
        System.Numerics.Quaternion? forceLocusSpin,
        System.Numerics.Vector3? latestOwnVel,
        Action<SimActorRecord>? acknowledgeProj,
        out PoseStampVerdict disposition,
        out RealmSession.MoverSpawn approved,
        out GrantedKineticsTimestamps timestamps)
    {
        Live();
        if (TryFetchOpenTenancy(
                refresh.Guid,
                out SimActorRecord queuedCanon,
                out SimSpawnTenancyLease queuedTenancy))
        {
            if (!SimSovereignPositionRouteSorter
                    .IsValidBuildWireLocus(refresh.Position)
                || refresh.Velocity is { } vel
                    && !Finite(vel)
                || !StartingBuildResidences.CanQueue(
                    queuedCanon,
                    queuedTenancy))
            {
                disposition = PoseStampVerdict.Rejected;
                approved = default;
                timestamps = default;
                return false;
            }

            bool postponedRecognized = Entities.TryAdmitPostponedLocus(
                refresh,
                isOwnAvatar,
                out disposition,
                out timestamps,
                out bool stampAlteration);
            approved = queuedCanon.Snapshot;
            if (!postponedRecognized)
                return false;

            if (isOwnAvatar
                && disposition is not PoseStampVerdict.Rejected)
            {
                Physics.WatchOwnRealmCycle(
                    refresh.Position.LandblockId,
                    timestamps.TeleportAdvanced);
            }

            if (disposition is PoseStampVerdict.Rejected
                && !stampAlteration)

                return true;

            ParkDormant(
                queuedCanon,
                queuedTenancy,
                SimSpawnFollowupKind.Position,
                SimGrantedPositionSource.PositionEvent,
                new SimSpawnTailAction(
                    SimSpawnTailActionKind.Position,
                    refresh.Guid,
                    Position: refresh,
                    PositionSource:
                        SimGrantedPositionSource.PositionEvent,
                    PositionDisposition: disposition,
                    PreviousTeleportSequence:
                        timestamps.PreviousTeleport,
                    AcceptedTimestamps: timestamps,
                    HasTimestampMutation: stampAlteration));
            return true;
        }
        bool hadCanon = Entities.TryFetchEngaged(
            refresh.Guid,
            out SimActorRecord priorCanon);
        uint priorChamber = priorCanon?.WholeChamberTag ?? 0u;
        bool recognized = Entities.TryEnactPosition(
            refresh,
            isOwnAvatar,
            forceLocusSpin,
            latestOwnVel,
            out disposition,
            out approved,
            out timestamps);
        if (!recognized
            || !Entities.TryGetSnapshot(
                refresh.Guid,
                out RealmSession.MoverSpawn capture)
            || !Entities.TryFetchEngaged(
                refresh.Guid,
                out SimActorRecord canon))

            return recognized;

        if (isOwnAvatar
            && disposition is not PoseStampVerdict.Rejected)
        {
            Physics.WatchOwnRealmCycle(
                refresh.Position.LandblockId,
                timestamps.TeleportAdvanced);
        }

        bool approvedLocus =
            disposition is not PoseStampVerdict.Rejected;
        timestamps = timestamps with
        {
            PreMergeCommittedCellId = hadCanon ? priorChamber : null,
        };
        SimPlacementAbortStub abort = default;
        if (approvedLocus)
        {
            abort = Physics.SetPosition.Drop(
                canon,
                revertCancelledPark: true);
        }
        Entities.RenewCapture(
            canon,
            capture,
            renewLocus: false);
        if (approvedLocus
            && ReferenceEquals(canon, priorCanon))
        {
            Entities.ProgressLocusArbiter(canon);
            Entities.AncestorAttachments.FinishDescendantProj(refresh.Guid);
        }

        ulong locusVer = canon.PositionAuthorityVersion;
        ulong spatialVer = canon.SpatialAuthorityVersion;
        if (!approvedLocus)
        {
            acknowledgeProj?.Invoke(canon);
            return SameCanon(
                canon,
                () => canon.PositionAuthorityVersion == locusVer
                    && canon.SpatialAuthorityVersion == spatialVer);
        }

        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            priorChamber != canon.WholeChamberTag
                ? SimActorChange.Rebucketed
                : SimActorChange.Updated,
            () => canon.PositionAuthorityVersion == locusVer
                && canon.SpatialAuthorityVersion == spatialVer,
            abort);
    }

    public bool TryAdmitErase(
        ObjectDeletion.Parsed erase,
        bool isOwnAvatar,
        bool dropKeptObject,
        out SimActorDeleteAcceptance acceptance)
    {
        Live();
        if (!isOwnAvatar)
        {
            Entities.AncestorAttachments.AbortPostponedDescendantGen(
                erase.Guid,
                erase.InstanceSequence);
        }
        if (!Entities.TryDelete(erase, isOwnAvatar))
        {
            acceptance = null!;
            return false;
        }

        Entities.ProgressLifespanAlteration(erase.Guid);
        UnseatSealedDescendants(erase.Guid, erase.InstanceSequence);
        Entities.AncestorAttachments.EraseGen(
            erase.Guid,
            erase.InstanceSequence);

        SimActorRecord? retiredCanon = null;
        if (Entities.TryFetchEngaged(
                erase.Guid,
                out SimActorRecord engaged)
            && engaged.Incarnation == erase.InstanceSequence
            && Entities.RemoveActive(engaged))
        {
            var startingAbort =
                DiscardTenancy(engaged);
            Physics.ImpactDossiers.Drop(engaged);
            var plainAbort =
                Physics.SetPosition.Drop(
                    engaged,
                    freeReadiedCarrier: true);
            var abort =
                StrongerCancel(
                    startingAbort,
                    plainAbort);
            retiredCanon = engaged;
            Entities.HoldTeardown(engaged);
            Physics.SetPosition.BroadcastAbort(abort);
            PublishEntity(
                dropKeptObject
                    ? SimActorChange.Deleted
                    : SimActorChange.Withdrawn,
                engaged);
        }

        acceptance = new SimActorDeleteAcceptance(
            this,
            erase,
            retiredCanon,
            dropKeptObject);
        return true;
    }

    public void ConcludeApprovedErase(
        SimActorDeleteAcceptance acceptance)
    {
        Live();
        ArgumentNullException.ThrowIfNull(acceptance);
        if (!ReferenceEquals(acceptance.Owner, this))
        {
            throw new InvalidOperationException(
                "A delete acceptance belongs to a different Runtime lifetime");
        }
        if (acceptance.Completed)
        {
            throw new InvalidOperationException(
                "A delete acceptance has by now been completed");
        }

        acceptance.Completed = true;
        if (acceptance.DropKeptObject)
            ObjectTableBindings.ImposeActorErase(Objects, acceptance.Delete);
    }
    internal SimSovereignPositionRoute? ClassifyDistantApprovedLocus(
        SimActorRecord canon,
        in RealmSession.MoverPositionUpdate refresh,
        PoseStampVerdict disposition,
        in GrantedKineticsTimestamps timestamps,
        float? avatarGap)
    {
        ArgumentNullException.ThrowIfNull(canon);
        if (_gen is not { } gen
            || timestamps.PreMergeCommittedCellId is not { } preCombineSealedChamberIdent)

            return null;
        SimPositionActorKind sort =
            (canon.FinalKineticsCondition & KineticStateFlags.Missile) != 0
                && canon.Projectile is SimMissile tiedMissile
                && ReferenceEquals(canon.KineticBody, tiedMissile.Body)
                ? SimPositionActorKind.Projectile
                : SimPositionActorKind.Remote;
        if (!SimGrantedPositionRouteRequests.TryAssemble(
                gen(),
                canon,
                refresh,
                sort,
                SimGrantedPositionSource.PositionEvent,
                disposition,
                timestamps.PreviousTeleport,
                timestamps.Teleport,
                avatarGap,
                useLocusFromSrv: false,
                preCombineSealedChamberIdent,
                out SimGrantedPositionRouteRequest req))

            return null;

        return SimSovereignPositionRouteSorter
            .ClassifyApprovedLocus(req);
    }

    private void UnseatSealedDescendants(
        uint ancestorOid,
        ushort ancestorInstSeries)
    {
        var descendants = Entities.AncestorAttachments.DescendantsAffixedToAncestor(
            ancestorOid,
            ancestorInstSeries);
        for (int idx = 0; idx < descendants.Count; ++idx)
        {
            if (!Entities.TryFetchEngaged(descendants[idx], out SimActorRecord descendant))
                continue;
            if (KineticTelemetry.ProbeChildCellEnabled)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[child-cell] parent=0x{ancestorOid:X8} child=0x{descendant.ServerGuid:X8} old=0x{descendant.WholeChamberTag:X8} new=0x00000000 cause=delete"));
            }
            Entities.AssignWholeChamber(descendant, 0u, 0u);
        }
    }
}
