using System.Collections.Immutable;
using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Kinetics;

internal sealed class RemotePhysicsUpdater
{
    private readonly SimPeerKineticsStepper _runtime;
    private readonly Func<uint, RealmActor, (float Radius, float Height)>
        _fetchRigCylinder;
    private readonly Func<uint, RealmActor,
            (ImmutableArray<PackedContactSphere> Spheres, float Scale, float StepUpHeight, float StepDownHeight)>
        _fetchRigCarrierForm;
    private readonly Func<uint, MoverState> _fetchCarrierPvpPhase;
    private readonly Action<uint, OnlineActorMotionLedger, PeerMotion, Vector3>
        _enactSrvControlledVelCycle;
    private readonly List<OnlineActorRecord> _spatialDistantCapture = [];

    internal RemotePhysicsUpdater(
        SimKineticsLedger physics,
        Func<uint, RealmActor, (float Radius, float Height)> getSetupCylinder,
        Func<uint, RealmActor,
                (ImmutableArray<PackedContactSphere> Spheres, float Scale, float StepUpHeight, float StepDownHeight)>
            getSetupMoverShape,
        Action<uint, OnlineActorMotionLedger, PeerMotion, Vector3>
            applyServerControlledVelocityCycle,
        Func<uint, MoverState>? fetchCarrierPvpPhase = null)
    {
        _runtime = new SimPeerKineticsStepper(
            physics ?? throw new ArgumentNullException(nameof(physics)));
        _fetchRigCylinder = getSetupCylinder
            ?? throw new ArgumentNullException(nameof(getSetupCylinder));
        _fetchRigCarrierForm = getSetupMoverShape
            ?? throw new ArgumentNullException(nameof(getSetupMoverShape));
        _fetchCarrierPvpPhase = fetchCarrierPvpPhase ?? (static _ => MoverState.None);
        _enactSrvControlledVelCycle =
            applyServerControlledVelocityCycle
            ?? throw new ArgumentNullException(
                nameof(applyServerControlledVelocityCycle));
    }

    public void PulseConcealedActors(
        OnlineActorCore onlineActors,
        uint ownAvatarSrvOid,
        float dt,
        Action<RealmActor> broadcastTrunkPosture,
        Action<uint, AnimSequencer>? procAnimTaps = null,
        Action<uint>? flagPiecePostureStale = null)
    {
        ArgumentNullException.ThrowIfNull(onlineActors);
        ArgumentNullException.ThrowIfNull(broadcastTrunkPosture);

        Vector3? avatarLocus = null;
        if (ownAvatarSrvOid != 0
            && onlineActors.TryFetchRealmActor(
                ownAvatarSrvOid,
                out RealmActor avatarActor))
        {
            avatarLocus = avatarActor.Position;
        }

        onlineActors.DuplicateSpatialDistantLocomotionRecordsTo(
            _spatialDistantCapture);
        foreach (OnlineActorRecord capture in _spatialDistantCapture)
        {
            if (capture.ServerOid == ownAvatarSrvOid
                || (capture.FinalKineticsPhase & KineticStateFlags.Hidden) == 0
                || capture.RemoteMotionRuntime is not PeerMotion distant
                || !onlineActors.IsLatestSpatialDistantLocomotion(capture, distant)
                || capture.WorldEntity is not { } actor)
            {
                continue;
            }

            var anim =
                capture.AnimationRuntime as OnlineActorMotionLedger;
            CanonActivityOutcome activity =
                CanonActivityGate.Evaluate(
                    capture.ObjectTimer,
                    distant.Body,
                    onlineActors.FetchTrunkObjectTimerDisposition(
                        capture.ServerOid)
                        is CanonClockVerdict.Advance,
                    capture.HasPieceArr,
                    (capture.FinalKineticsPhase
                        & KineticStateFlags.Static) != 0,
                    actor.Position,
                    avatarLocus,
                    dt);
            if (activity is not CanonActivityOutcome.Active)
                continue;

            AnimSequencer? scheduler = anim?.Sequencer;
            ulong objectTimerEpoch = capture.ObjectTimerEpoch;
            CanonQuantumBatch lot = capture.ObjectTimer.Advance(dt);
            for (int qi = 0; qi < lot.Count; qi++)
            {
                if (!IsLatest(
                        onlineActors,
                        capture,
                        actor,
                        distant,
                        objectTimerEpoch)
                    || !PulseConcealed(
                        distant,
                        actor,
                        lot.FetchQuantum(qi),
                        scheduler?.Manager,
                        procAnimTaps,
                        scheduler,
                        onlineActors,
                        capture,
                        objectTimerEpoch))
                {
                    break;
                }
            }

            if (lot.Count > 0
                && IsLatest(
                    onlineActors,
                    capture,
                    actor,
                    distant,
                    objectTimerEpoch))
            {
                flagPiecePostureStale?.Invoke(capture.ServerOid);
                broadcastTrunkPosture(actor);
            }
        }
    }

    public bool Tick(
        PeerMotion distant,
        OnlineActorMotionLedger anim,
        float dt,
        MotionDeltaPose trunkLocomotionOwnCycle,
        int onlineMiddleX,
        int onlineMiddleY,
        Action<uint, AnimSequencer>? procAnimTaps = null,
        OnlineActorCore? holderCore = null,
        OnlineActorRecord? holderCapture = null,
        ulong holderTimerEpoch = 0)
    {
        ArgumentNullException.ThrowIfNull(anim);
        return Tick(
            distant,
            anim.Entity,
            anim.Scale,
            anim.Sequencer,
            anim,
            dt,
            trunkLocomotionOwnCycle,
            onlineMiddleX,
            onlineMiddleY,
            procAnimTaps,
            holderCore,
            holderCapture,
            holderTimerEpoch);
    }

    public bool Tick(
        PeerMotion distant,
        RealmActor actor,
        float objectScaling,
        AnimSequencer? scheduler,
        OnlineActorMotionLedger? animForVelCycle,
        float dt,
        MotionDeltaPose trunkLocomotionOwnCycle,
        int onlineMiddleX,
        int onlineMiddleY,
        Action<uint, AnimSequencer>? procAnimTaps = null,
        OnlineActorCore? holderCore = null,
        OnlineActorRecord? holderCapture = null,
        ulong holderTimerEpoch = 0)
    {
        ArgumentNullException.ThrowIfNull(distant);
        ArgumentNullException.ThrowIfNull(actor);
        if (holderCore is null
            || holderCapture is null)
        {
            return false;
        }

        bool HolderValid() => IsLatest(
            holderCore,
            holderCapture,
            actor,
            distant,
            holderTimerEpoch);
        var (radius, height) =
            _fetchRigCylinder(holderCapture.ServerOid, actor);
        var form = _fetchRigCarrierForm(holderCapture.ServerOid, actor);
        Action<Vector3>? staleCycle =
            animForVelCycle is null
                ? null
                : vel => _enactSrvControlledVelCycle(
                    holderCapture.ServerOid,
                    animForVelCycle,
                    distant,
                    vel);

        return _runtime.Tick(
            holderCapture.Canonical,
            distant,
            objectScaling,
            scheduler,
            dt,
            holderTimerEpoch,
            trunkLocomotionOwnCycle,
            radius,
            height,
            onlineMiddleX,
            onlineMiddleY,
            procAnimTaps,
            staleCycle,
            capture =>
            {
                if (!HolderValid())
                    return false;
                actor.SetPosition(capture.Position);
                actor.ParentCellId = capture.FullCellId;
                actor.Rotation = capture.Orientation;
                return HolderValid();
            },
            HolderValid,
            orbRoster: form.Spheres,
            orbScaling: form.Scale,
            hopUpHeight: form.StepUpHeight,
            hopDownHeight: form.StepDownHeight,
            carrierPvpPhase: _fetchCarrierPvpPhase(holderCapture.ServerOid));
    }

    public bool PulseConcealed(
        PeerMotion distant,
        RealmActor actor,
        float dt,
        MotionTableKeeper? pieceArrHndTravel = null,
        Action<uint, AnimSequencer>? procAnimTaps = null,
        AnimSequencer? scheduler = null,
        OnlineActorCore? holderCore = null,
        OnlineActorRecord? holderCapture = null,
        ulong holderTimerEpoch = 0)
    {
        ArgumentNullException.ThrowIfNull(distant);
        ArgumentNullException.ThrowIfNull(actor);
        if (holderCore is null
            || holderCapture is null)
        {
            return false;
        }

        bool HolderValid() => IsLatest(
            holderCore,
            holderCapture,
            actor,
            distant,
            holderTimerEpoch);
        var (radius, height) =
            _fetchRigCylinder(holderCapture.ServerOid, actor);
        var form = _fetchRigCarrierForm(holderCapture.ServerOid, actor);
        return _runtime.PulseConcealed(
            holderCapture.Canonical,
            distant,
            dt,
            holderTimerEpoch,
            radius,
            height,
            pieceArrHndTravel,
            procAnimTaps,
            scheduler,
            capture =>
            {
                if (!HolderValid())
                    return false;
                actor.SetPosition(capture.Position);
                actor.ParentCellId = capture.FullCellId;
                actor.Rotation = capture.Orientation;
                return HolderValid();
            },
            HolderValid,
            orbRoster: form.Spheres,
            orbScaling: form.Scale,
            hopUpHeight: form.StepUpHeight,
            hopDownHeight: form.StepDownHeight,
            carrierPvpPhase: _fetchCarrierPvpPhase(holderCapture.ServerOid));
    }

    public void SyncRemoteShadowToBody(
        uint actorIdent,
        ISimPeerPlacement distant,
        int onlineMiddleX,
        int onlineMiddleY,
        uint? authoritativeChamberIdent = null) =>
        _runtime.SynchronizeDistantShadeToCorpus(
            actorIdent,
            distant,
            onlineMiddleX,
            onlineMiddleY,
            authoritativeChamberIdent);

    public void SyncRemoteShadowToBody(
        uint actorIdent,
        KineticBody corpus,
        int onlineMiddleX,
        int onlineMiddleY,
        uint authoritativeChamberIdent) =>
        _runtime.SynchronizeDistantShadeToCorpus(
            actorIdent,
            corpus,
            onlineMiddleX,
            onlineMiddleY,
            authoritativeChamberIdent);

    internal static bool ShouldSynchronizeShadePosture(
        Vector3 latestLocus,
        Quaternion latestFacing,
        Vector3 previousLocus,
        Quaternion previousFacing) =>
        SimPeerKineticsStepper.ShouldSynchronizeShadePosture(
            latestLocus,
            latestFacing,
            previousLocus,
            previousFacing);

    internal static bool ShouldSynchronizeShade(
        bool chamberAltered,
        Vector3 latestLocus,
        Quaternion latestFacing,
        Vector3 previousLocus,
        Quaternion previousFacing) =>
        SimPeerKineticsStepper.ShouldSynchronizeShade(
            chamberAltered,
            latestLocus,
            latestFacing,
            previousLocus,
            previousFacing);

    private static bool IsLatest(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        PeerMotion distant,
        ulong objectTimerEpoch) =>
        capture.ObjectTimerEpoch == objectTimerEpoch
        && core.IsLatestSpatialDistantLocomotion(capture, distant)
        && ReferenceEquals(capture.WorldEntity, actor);
}
