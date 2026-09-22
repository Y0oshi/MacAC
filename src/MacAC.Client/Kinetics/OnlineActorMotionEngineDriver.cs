using System.Collections.Immutable;
using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Kinetics;

internal sealed class OnlineActorMotionEngineDriver(
    OnlineActorCore liveEntities,
    KineticAssetCache physicsDataCache,
    Func<PickingDealingDriver?> selectionInteractions,
    PickPhase selection,
    OnlineRealmOriginLedger origin)
        : IOnlineActorMotionEngineWiring
{
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly KineticAssetCache _kineticsBlobStash = physicsDataCache ?? throw new ArgumentNullException(nameof(physicsDataCache));
    private readonly Func<PickingDealingDriver?> _pickInteractions = selectionInteractions ?? throw new ArgumentNullException(nameof(selectionInteractions));
    private readonly PickPhase _pick = selection ?? throw new ArgumentNullException(nameof(selection));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));

    public MacAC.Mechanics.Kinetics.Gait.IKineticObjHost? ResolvePhysicsHost(uint ident)
    {
        if (_onlineActors is not { } onlineActors)
            return null;

        bool isEngaged = onlineActors.TryFetchRecord(ident, out OnlineActorRecord engagedCapture);
        if (!isEngaged)
            return null;

        if (onlineActors.IsHidden(ident))

            return null;
        if (onlineActors.TryGetPhysicsHost(ident, out var extant))
            return extant;

        double InstantSecs() => (System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
        ActorKineticsHarbor minimal = ActorKineticsHostAssembly.BuildMinimal(
            engagedCapture,
            ResolvePhysicsHost,
            InstantSecs);
        onlineActors.SetupKineticsHub(engagedCapture, minimal);
        return minimal;
    }

    public (float Radius, float Height) GetSetupCylinder(
        uint srvOid, MacAC.Mechanics.Realm.RealmActor actor)
    {
        var rig =
            _kineticsBlobStash.FetchPlanarRig(actor.SrcGfxObjRefOrRigIdent);
        if (rig is null)
            return (0f, 0f);
        float scaling =
            _onlineActors.Snapshots.TryGetValue(srvOid, out var spawn)
                && spawn.ObjScale is { } objRefScaling && objRefScaling > 0f
            ? objRefScaling
            : (actor.Scale > 0f ? actor.Scale : 1f);
        return (rig.Radius * scaling, rig.Height * scaling);
    }

    public (ImmutableArray<PackedContactSphere> Spheres, float Scale, float StepUpHeight, float StepDownHeight)
        FetchRigCarrierForm(uint srvOid, MacAC.Mechanics.Realm.RealmActor actor)
    {
        var rig =
            _kineticsBlobStash.FetchPlanarRig(actor.SrcGfxObjRefOrRigIdent);
        if (rig is null)
            return (ImmutableArray<PackedContactSphere>.Empty, 1f, 0.4f, 0.4f);

        float scaling =
            _onlineActors.Snapshots.TryGetValue(srvOid, out var spawn)
                && spawn.ObjScale is { } objRefScaling && objRefScaling > 0f
            ? objRefScaling
            : (actor.Scale > 0f ? actor.Scale : 1f);

        float hopUp = rig.StepUpHeight > 0f ? rig.StepUpHeight * scaling : 0.4f;
        float hopDown = rig.StepDownHeight > 0f ? rig.StepDownHeight * scaling : 0.4f;
        return (rig.Spheres, scaling, hopUp, hopDown);
    }

    public void StickToObjectFromWire(
        MacAC.Mechanics.Kinetics.Gait.IKineticObjHost? hub,
        uint markOid)
    {
        if (hub is not ActorKineticsHarbor actorHub)
            return;
        if (_onlineActors is not { } onlineActors
            || !onlineActors.TryFetchDealingEligibleActor(markOid, out var tgtEnt))
            return;
        var (radius, height) = GetSetupCylinder(markOid, tgtEnt);
        actorHub.LocusKeeper.StickTo(markOid, radius, height);
    }

    public void ClearTargetForHiddenEntity(uint srvOid)
    {
        if (_pickInteractions() is { } interactions)
            interactions.OnActorConcealed(srvOid);
        else if (_pick.ChosenObjectTag == srvOid)
            _pick.Clear(
                MacAC.Mechanics.Targeting.PickChangeSource.System,
                MacAC.Mechanics.Targeting.PickChangeReason.Cleared);

        if (_onlineActors?.TryGetPhysicsHost(srvOid, out var concealedHub) == true
            && concealedHub is ActorKineticsHarbor concealedActorHub)
            concealedActorHub.AlertConcealed();
    }

    public bool RouteServerMoveTo(
        MacAC.Mechanics.Kinetics.Gait.LocomotionKeeper travel,
        uint chamberIdent,
        MacAC.Wire.RealmSession.MoverMotionUpdate refresh)
    {
        if (refresh.MotionState.IsSrvControlledRelocateTo
            && refresh.MotionState.MoveToPath is { } trail)
        {
            // my_run_rate write (unpack_movement @300603)
            if (refresh.MotionState.MoveToRunRate is { } mtExecRate)
                travel.Minterp.MyExecRate = mtExecRate;

            System.Numerics.Vector3 destRealm = MacAC.Mechanics.Kinetics.Gait.ApproachMath
                .OriginToRealm(
                    trail.OriginCellId, trail.OriginX, trail.OriginY, trail.OriginZ,
                    _origin.CenterX, _origin.CenterY);
            var mp = MacAC.Mechanics.Kinetics.Gait.LocomotionParams.FromWire(
                trail.Bitfield,
                trail.DistanceToObject,
                trail.MinDistance,
                trail.FailDistance,
                refresh.MotionState.MoveToSpeed ?? 1f,
                trail.WalkRunThreshold,
                trail.DesiredHeading);

            LocomotionPacket msec = new MacAC.Mechanics.Kinetics.LocomotionPacket
            {
                Params = mp,
            };
            // mt 6 with a resolvable target → MoveToObject (the P4 tracker
            // feeds position updates per tick); else degrade to
            // MoveToPosition at the wire origin (§2f).
            if (refresh.MotionState.MovementType is 6
                && trail.TargetGuid is { } tgtOid
                && _onlineActors is { } onlineRelocateActors
                && onlineRelocateActors.TryFetchDealingEligibleActor(tgtOid, out var tgtEnt)
                && ResolvePhysicsHost(tgtOid) is not null)
            {
                msec.Type = MacAC.Mechanics.Kinetics.TravelKind.MoveToObject;
                msec.ObjectId = tgtOid;
                msec.TopTierIdent = tgtOid;
                (msec.Radius, msec.Height) = GetSetupCylinder(tgtOid, tgtEnt);
                msec.Spot = new MacAC.Mechanics.Kinetics.Locus(
                    chamberIdent, tgtEnt.Position,
                    System.Numerics.Quaternion.Identity);
            }
            else
            {
                msec.Type = MacAC.Mechanics.Kinetics.TravelKind.MoveToPosition;
                msec.Spot = new MacAC.Mechanics.Kinetics.Locus(
                    chamberIdent, destRealm,
                    System.Numerics.Quaternion.Identity);
            }
            travel.PerformMovement(msec);
            return true;
        }

        if (refresh.MotionState.IsSrvControlledPivotTo
            && refresh.MotionState.TurnToPath is { } pivotTrail)
        {
            var mp = MacAC.Mechanics.Kinetics.Gait.LocomotionParams.FromWirePivotTo(
                pivotTrail.Bitfield,
                pivotTrail.Speed,
                pivotTrail.DesiredHeading);

            LocomotionPacket msec = new MacAC.Mechanics.Kinetics.LocomotionPacket { Params = mp };
            if (refresh.MotionState.MovementType is 8
                && pivotTrail.TargetGuid is { } pivotTgt
                && _onlineActors is { } onlinePivotActors
                && onlinePivotActors.TryFetchDealingEligibleActor(pivotTgt, out var pivotEnt))
            {
                msec.Type = MacAC.Mechanics.Kinetics.TravelKind.TurnToObject;
                msec.ObjectId = pivotTgt;
                msec.TopTierIdent = pivotTgt;
                msec.Spot = new MacAC.Mechanics.Kinetics.Locus(
                    chamberIdent, pivotEnt.Position,
                    System.Numerics.Quaternion.Identity);
            }
            else
            {
                msec.Type = MacAC.Mechanics.Kinetics.TravelKind.TurnToHeading;
                if (refresh.MotionState.MovementType is 8
                    && pivotTrail.WireHeading is { } wireBearing)

                    mp.WantedBearing = wireBearing;
            }
            travel.PerformMovement(msec);
            return true;
        }

        return false;
    }

    internal MacAC.Mechanics.Kinetics.Gait.MotionTableDispatchTap? EnsureRemoteMotionBindings(
        PeerMotion motion, OnlineActorMotionLedger? ledger, uint srvOid)
    {
        var scheduler = ledger?.Sequencer;
        if (scheduler is not null && motion.Sink is null)
        {
            motion.Sink = new MacAC.Mechanics.Kinetics.Gait.MotionTableDispatchTap(scheduler);
            motion.Motion.DefaultSink = motion.Sink;
        }
        if (scheduler is not null)
        {
            motion.Motion.RemoveLinkAnimations = () => scheduler.Manager.HandleEnterWorld();
            motion.Motion.BootstrapLocomotionCharts = () => scheduler.Manager.BootstrapCondition();
            motion.Motion.VerifyForCompletedMotions = scheduler.Manager.VerifyForFinishedMotions;
        }

        if (motion.Host is not null)
            return motion.Sink;
        if (_onlineActors is not { } onlineActors
            || !onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord hubCapture)
            || !ReferenceEquals(hubCapture.RemoteMotionRuntime, motion))

            return motion.Sink;
        PeerMotion rmT = motion;
        KineticBody mtCorpus = motion.Body;
        if (!_onlineActors.TryFetchRealmActor(srvOid, out var selfActor))
            return motion.Sink;
        ActorKineticsHarbor hub = null!;
        double InstantSecs() => (System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
        motion.Movement.RelocateToMaker = () =>
        {
            var mtm = new MacAC.Mechanics.Kinetics.Gait.MoveToKeeper(
                motion.Motion,
                stopCompletely: () => rmT.Movement.PerformMovement(
                    new MacAC.Mechanics.Kinetics.LocomotionPacket
                    {
                        Type = MacAC.Mechanics.Kinetics.TravelKind.StopCompletely,
                    }),
                getPosition: () => new MacAC.Mechanics.Kinetics.Locus(
                    rmT.CellId, mtCorpus.Position, mtCorpus.Orientation),
                getHeading: () => MacAC.Mechanics.Kinetics.Gait.ApproachMath.FetchBearing(
                    mtCorpus.Orientation),
                setHeading: (h, _) => mtCorpus.Orientation =
                    MacAC.Mechanics.Kinetics.Gait.ApproachMath.ApplyBearing(mtCorpus.Orientation, h),
                getOwnRadius: () => GetSetupCylinder(srvOid, selfActor).Radius,
                getOwnHeight: () => GetSetupCylinder(srvOid, selfActor).Height,
                contact: () => mtCorpus.OnWalkable,
                isInterpolating: () => rmT.Lerp.IsActive,
                getVelocity: () => mtCorpus.Velocity,
                getSelfId: () => srvOid,
                setTarget: (cx, tlid, radius, q) => hub.AssignMark(cx, tlid, radius, q),
                clearTarget: () => hub.WipeMark(),
                getTargetQuantum: () => hub.MarkKeeper.FetchMarkQuantum(),
                setTargetQuantum: q => hub.MarkKeeper.AssignMarkQuantum(q),
                curMoment: InstantSecs)
            {
                StickTo = (tlid, radius, height) =>
                    hub.LocusKeeper.StickTo(tlid, radius, height),
                Unstick = hub.LocusKeeper.UnStick
            };
            return mtm;
        };
        motion.Motion.InterruptCurrentMovement =
            () => rmT.Movement.CancelMoveTo(
                MacAC.Mechanics.Kinetics.WeenieProblem.ActionCancelled);

        ActorKineticsHarbor configuredHub = new ActorKineticsHarbor(
            srvOid,
            fetchLocus: () => new MacAC.Mechanics.Kinetics.Locus(
                hubCapture.WholeChamberIdent,
                hubCapture.WorldEntity?.Position ?? mtCorpus.Position,
                mtCorpus.Orientation),
            fetchVel: () => mtCorpus.Velocity,
            fetchRadius: () => GetSetupCylinder(srvOid, selfActor).Radius,
            inLink: () => mtCorpus.OnWalkable,
            minterpUpperPace: () => rmT.Motion.FetchAdjustedUpperPace(),
            curMoment: InstantSecs,
            kineticsTickerMoment: InstantSecs,
            fetchObjectA: ResolvePhysicsHost,
            hndRefreshMark: rmT.Movement.ProcessRefreshMark,
            interruptLatestTravel: () => rmT.Movement.CancelMoveTo(
                MacAC.Mechanics.Kinetics.WeenieProblem.ActionCancelled));
        hub = ActorKineticsHostAssembly.SetupOrRebind(
            onlineActors,
            hubCapture,
            configuredHub);
        motion.FlagWholeKineticsHubTied();

        motion.Movement.CraftRelocateToKeeper();
        motion.Motion.UnstickFromObject = hub.LocusKeeper.UnStick;
        return motion.Sink;
    }

}
