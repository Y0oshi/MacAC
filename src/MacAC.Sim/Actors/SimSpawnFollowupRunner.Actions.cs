using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal sealed partial class SimSpawnFollowupRunner
{
    private SimSpawnExecutionStatus ImposeLocusAct(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimActorKey tag,
        ulong series,
        int juncture,
        SimSpawnTailAction act,
        in SimSpawnExecutionInputs feeds,
        Progress headway,
        List<HeldPublish>? buf)
    {
        if (!_tenancies.TryFetchTransaction(canon, out SimSpawnTenancyLease tenancy)
            || canon.Key != tag)

            return Abandon(canon, tag);

        var refresh = act.Position!.Value;
        var actorSort = SortOf(tenancy.Route.OperationKind);
        bool isOwnAvatar = actorSort is SimPositionActorKind.LocalPlayer;

        SimSovereignPositionRoute course;
        if (headway.LocusCombineSealedForReattempt)
        {
            course = headway.QueuedContinuationCourse;
        }
        else
        {
            var req =
                SimGrantedPositionRouteRequests.Build(
                    EpochInstant(),
                    canon,
                    tag,
                    refresh,
                    actorSort,
                    act.PositionSource,
                    act.PositionDisposition,
                    act.PreviousTeleportSequence,
                    act.AcceptedTimestamps.Teleport,
                    feeds.PlayerDistance,
                    feeds.UsePositionFromServer);

            course = SimSovereignPositionRouteSorter.ClassifyApprovedLocus(req);

            if (!course.Accepted)
            {
                bool stampedOk = act.PositionDisposition
                        is PoseStampVerdict.Rejected
                    ? _actors.ImposeApprovedLocusCapture(
                        canon.ServerGuid,
                        refresh,
                        PoseStampVerdict.Rejected,
                        act.AcceptedTimestamps,
                        isOwnAvatar,
                        null,
                        null,
                        installStanceCycle: false,
                        wipeAncestor: false,
                        out RealmSession.MoverSpawn stampedSole)
                    : _actors.ImposeApprovedLocusExecutionRejectedCapture(
                        canon.ServerGuid,
                        refresh.PositionSequence,
                        act.AcceptedTimestamps,
                        out stampedSole);
                if (!stampedOk)
                    return Abandon(canon, tag);
                _actors.RenewCapture(canon, stampedSole);
                headway.Trace.Add(PositionTrace(series, juncture, course));
                return SimSpawnExecutionStatus.Completed;
            }

            var corpus = canon.KineticBody;
            bool mergedOk = _actors.ImposeApprovedLocusCapture(
                canon.ServerGuid,
                refresh,
                act.PositionDisposition,
                act.AcceptedTimestamps,
                isOwnAvatar,
                corpus?.Orientation,
                corpus?.Velocity,
                installStanceCycle: course.ApplyPlacementFrameBeforeRouting,
                wipeAncestor: course.UnparentBeforeRouting,
                out RealmSession.MoverSpawn merged);
            if (!mergedOk)
                return Abandon(canon, tag);
            _actors.RenewCapture(canon, merged, renewLocus: false);
            _actors.ProgressLocusArbiter(canon);
            _actors.AncestorAttachments.FinishDescendantProj(canon.ServerGuid);
            _tenancies.AdvanceExecutorBaseline(
                canon, ticket, SimRunnerBaselineFields.PositionAuthorityVersion);
            ulong locusVer = canon.PositionAuthorityVersion;
            ulong spatialVer = canon.SpatialAuthorityVersion;
            Publish(
                canon,
                SimActorChange.Updated,
                () => canon.PositionAuthorityVersion == locusVer
                    && canon.SpatialAuthorityVersion == spatialVer,
                default,
                buf);

            if (!course.PerformsSetLocus)
            {
                headway.Trace.Add(PositionTrace(series, juncture, course));
                return SimSpawnExecutionStatus.Completed;
            }

            headway.LocusCombineSealedForReattempt = true;
            headway.QueuedContinuationCourse = course;
            headway.LocusCombineSealedVer = canon.PositionAuthorityVersion;
        }

        var stance = _physics.SetPosition
            .TryCommenceExclusiveAuthoredStance(
                canon,
                canon.PositionAuthorityVersion,
                course.OperationKind);
        if (!stance.IsValid)
        {
            if (!_actors.IsCurrent(canon)
                || canon.PositionAuthorityVersion != headway.LocusCombineSealedVer)
            {
                headway.LocusCombineSealedForReattempt = false;
                return Abandon(canon, tag);
            }
            return SimSpawnExecutionStatus.AwaitingContinuationPlacement;
        }
        if (!_physics.SetPosition.MonitorStanceWrapUp(stance))
        {
            _ = _physics.SetPosition.DropPreciseStance(stance);
            headway.LocusCombineSealedForReattempt = false;
            return Abandon(canon, tag);
        }

        headway.LocusCombineSealedForReattempt = false;
        headway.QueuedContinuationStance = stance;
        headway.QueuedContinuationSeries = series;
        headway.QueuedContinuationCourse = course;
        return SimSpawnExecutionStatus.AwaitingContinuationPlacement;
    }

    private bool ImposeObjRefDscAct(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimSpawnTailAction act,
        List<HeldPublish>? buf)
    {
        if (!_actors.ImposeApprovedObjRefDscCapture(
                canon.ServerGuid,
                act.ObjDesc!.Value,
                out RealmSession.MoverSpawn merged))

            return false;
        _actors.RenewCapture(canon, merged);
        _actors.ProgressObjRefDscArbiter(canon);
        ulong ver = canon.ObjRefDscArbiterVer;
        Publish(
            canon,
            SimActorChange.Updated,
            () => canon.ObjRefDscArbiterVer == ver,
            default,
            buf);
        return true;
    }

    private void SealAncestorAffix(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        bool rebaseline,
        List<HeldPublish>? buf)
    {
        _actors.ProgressLocusArbiter(canon);
        _physics.ImpactDossiers.ExitRealm(canon);
        var abort =
            _physics.SetPosition.Drop(canon);
        if (rebaseline)
        {
            _tenancies.AdvanceExecutorBaseline(
                canon, ticket, SimRunnerBaselineFields.PositionAuthorityVersion);
        }
        ulong locusVer = canon.PositionAuthorityVersion;
        ulong spatialVer = canon.SpatialAuthorityVersion;
        Publish(
            canon,
            SimActorChange.Updated,
            () => canon.PositionAuthorityVersion == locusVer
                && canon.SpatialAuthorityVersion == spatialVer,
            abort,
            buf);
    }

    private bool ImposeLiftAct(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimSpawnTailAction act,
        List<HeldPublish>? buf)
    {
        if (!_actors.ImposeApprovedLiftCapture(
                canon.ServerGuid,
                act.Pickup!.Value,
                out RealmSession.MoverSpawn merged))

            return false;
        _actors.RenewCapture(canon, merged);
        _actors.ProgressLocusArbiter(canon);
        _actors.AncestorAttachments.FinishDescendantProj(canon.ServerGuid);
        _physics.ImpactDossiers.ExitRealm(canon);
        var abort =
            _physics.SetPosition.Drop(canon);
        _actors.SuspendObjectTimer(canon);
        _actors.AssignWholeChamber(canon, 0u, 0u);
        _tenancies.AdvanceExecutorBaseline(
            canon,
            ticket,
            SimRunnerBaselineFields.PositionAuthorityVersion
                | SimRunnerBaselineFields.FullCellId);
        ulong locusVer = canon.PositionAuthorityVersion;
        ulong spatialVer = canon.SpatialAuthorityVersion;
        Publish(
            canon,
            SimActorChange.Withdrawn,
            () => canon.PositionAuthorityVersion == locusVer
                && canon.SpatialAuthorityVersion == spatialVer,
            abort,
            buf);
        return true;
    }

    private bool ImposeTravelAct(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimSpawnTailAction act,
        List<HeldPublish>? buf)
    {
        var refresh = act.Movement!.Value;
        if (!_actors.ImposeApprovedLocomotionCapture(
                canon.ServerGuid,
                refresh.MovementSequence,
                act.AcceptedTimestamps.ServerControlledMove,
                refresh,
                retainCargo: false,
                out RealmSession.MoverSpawn stamped))

            return false;
        _actors.RenewCapture(canon, stamped);
        if (!act.AppliesMovementPayload)

            return true;

        if (act.RetainMovementPayload)
        {
            if (!_actors.ImposeApprovedLocomotionCapture(
                    canon.ServerGuid,
                    refresh.MovementSequence,
                    act.AcceptedTimestamps.ServerControlledMove,
                    refresh,
                    retainCargo: true,
                    out RealmSession.MoverSpawn merged))

                return false;
            _actors.RenewCapture(canon, merged);
            _actors.ProgressTravelArbiter(canon);
        }
        _actors.ProgressTravelSeal(canon);
        ulong travelSealVer = canon.TravelSealVer;
        Publish(
            canon,
            SimActorChange.Updated,
            () => canon.TravelSealVer == travelSealVer,
            default,
            buf);
        return true;
    }

    private bool ImposePhaseAct(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimSpawnTailAction act,
        List<HeldPublish>? buf)
    {
        var refresh = act.State!.Value;
        if (!_actors.ImposeApprovedPhaseCapture(
                canon.ServerGuid,
                refresh,
                out RealmSession.MoverSpawn merged))

            return false;
        _actors.RenewCapture(canon, merged);
        var preview = CanonKineticShifts.Apply(
            canon.FinalKineticsCondition,
            (KineticStateFlags)refresh.PhysicsState);
        ulong precedingKineticsAlteration = canon.KineticsPhaseAlterationVer;
        if (preview.HiddenTransition is CanonHiddenShift.BecameHidden)
        {
            _physics.ImpactDossiers.ExitRealm(canon);
            if (!_actors.IsCurrent(canon)
                || canon.KineticsPhaseAlterationVer != precedingKineticsAlteration)

                return false;
        }
        var changeover =
            _actors.ImposeRawKineticsPhase(canon, refresh.PhysicsState);
        if (canon.Key is { } tag)
        {
            _physics.Engine.ShadeObjects.RefreshKineticsPhase(
                tag.LocalEntityId,
                (uint)canon.FinalKineticsCondition);
        }
        ulong phaseVer = canon.PhaseArbiterVer;
        ulong kineticsAlterationVer = canon.KineticsPhaseAlterationVer;
        Publish(
            canon,
            changeover.HiddenTransition is CanonHiddenShift.BecameHidden
                ? SimActorChange.Hidden
                : SimActorChange.Updated,
            () => canon.PhaseArbiterVer == phaseVer
                && canon.KineticsPhaseAlterationVer == kineticsAlterationVer,
            default,
            buf);
        return true;
    }

    private bool ImposeVectorAct(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimSpawnTailAction act,
        List<HeldPublish>? buf)
    {
        if (!_actors.ImposeApprovedVectorCapture(
                canon.ServerGuid,
                act.Vector!.Value,
                out RealmSession.MoverSpawn merged))

            return false;
        _actors.RenewCapture(canon, merged);
        _actors.ProgressVectorArbiter(canon);
        ulong ver = canon.VectorArbiterVer;
        Publish(
            canon,
            SimActorChange.Updated,
            () => canon.VectorArbiterVer == ver,
            default,
            buf);
        return true;
    }

    private bool ApplyWeenieDescriptionAction(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimSpawnTailAction act,
        List<HeldPublish>? buf)
    {
        if (!_actors.ImposeApprovedWeenieBlurbCapture(
                canon.ServerGuid,
                act.WeenieDescription!.Value,
                out RealmSession.MoverSpawn merged))

            return false;
        _actors.RenewCapture(canon, merged, renewLocus: false);
        _actors.ProgressBuildArbiter(canon);
        ulong buildVer = canon.BuildIntegrationVersion;
        _tenancies.AdvanceExecutorBaseline(
            canon,
            ticket,
            SimRunnerBaselineFields.PositionAuthorityVersion
                | SimRunnerBaselineFields.CreateIntegrationVersion);
        if (!_foldApprovedSummon(canon, buildVer, merged, /* replaceGeneration: */ false))
            return false;
        Publish(
            canon,
            SimActorChange.Updated,
            () => canon.BuildIntegrationVersion == buildVer,
            default,
            buf);
        return true;
    }

    private SimResidentCellCleanupVerdict? ImposeHousedChamberTidy(
        SimActorRecord canon)
    {
        uint claimedChamber = canon.Snapshot.Physics?.Position?.LandblockId
            ?? canon.Snapshot.Position?.LandblockId
            ?? 0u;
        if (claimedChamber is 0u)
        {
            return SimResidentCellCleanupVerdict
                .CelllessNoWeenieMarkUnreachable;
        }
        if (canon.WholeChamberTag is not 0u)
            return SimResidentCellCleanupVerdict.ResidentUnmarked;
        if (!_physics.SetPosition.IsPostponed(canon))

            return null;
        return SimResidentCellCleanupVerdict.DeferredUnderLostCellOwnership;
    }

    private void Publish(
        SimActorRecord canon,
        SimActorChange edit,
        Func<bool> fits,
        SimPlacementAbortStub abort,
        List<HeldPublish>? buf)
    {
        if (buf is not null)
        {
            buf.Add(new HeldPublish(edit, static () => true, abort));
            return;
        }
        BroadcastInstant(canon, edit, fits, abort);
    }

    private void BroadcastInstant(
        SimActorRecord canon,
        SimActorChange edit,
        Func<bool> fits,
        SimPlacementAbortStub abort)
    {
        _physics.SetPosition.BroadcastAbort(abort);
        if (_actors.IsCurrent(canon) && fits())
            _signals.PublishEntity(edit, canon);
    }

    private static SimSpawnExecutedAction PositionTrace(
        ulong series,
        int juncture,
        in SimSovereignPositionRoute course)
    {
        return new(
        SimSpawnExecutedActionKind.Position,
        series,
        juncture,
        course.Disposition,
        course.TeleportHookPhase,
        null,
        null,
        course.ConstrainPhase,
        course.StopInterpolating,
        course.ZeroVelocity,
        course.PreserveHeading,
        course.SendPositionImmediately,
        course.UnparentBeforeRouting);
    }

    private static SimSpawnExecutedAction Trace(
        SimSpawnExecutedActionKind sort,
        ulong series,
        int juncture = -1,
        SimAnchorRelationUpshot? ancestorRelationVerdict = null)
    {
        return new(
        sort,
        series,
        juncture,
        null,
        SimWarpHookPhase.None,
        ParentRelationOutcome: ancestorRelationVerdict);
    }

    private static SimPositionActorKind SortOf(
        SimSetPositionOperationKind opSort)
    {
        return opSort switch
        {
            SimSetPositionOperationKind.InitialLogin
                or SimSetPositionOperationKind.LocalAuthoritative =>
                SimPositionActorKind.LocalPlayer,
            SimSetPositionOperationKind.ProjectileAuthoritative =>
                SimPositionActorKind.Projectile,
            _ => SimPositionActorKind.Remote,
        };
    }
}
