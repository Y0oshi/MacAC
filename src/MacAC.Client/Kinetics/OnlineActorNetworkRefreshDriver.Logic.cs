using MacAC.Client.Graphics;
using MacAC.Client.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Kinetics;

internal sealed partial class OnlineActorNetworkRefreshDriver
{
    private AvatarLocomotionDriver? _playerController => _avatarDriverSrc.Controller;

    private ActorKineticsHarbor? _avatarHub => _avatarHubSrc.Host;

    private uint _avatarSrvOid => _avatarPersona.SrvOid;

    private double _physicsScriptGameTime => _gameTime.LatestProgramMoment;

    internal void RestartSessPhase() => _arbiterLatch.RestartSessPhase();

    internal uint? PreviousOnlineAvatarLbIdent =>
        _arbiterLatch.PreviousOnlineAvatarLbIdent;

    uint? IAvatarLandblockSource.PreviousRecognizedLbIdent =>
        PreviousOnlineAvatarLbIdent;

    internal static bool TryApplyGenericRemoteRenderPose(
        MacAC.Mechanics.Realm.RealmActor actor,
        SimSovereignPositionRoute? course,
        System.Numerics.Vector3 realmSpot,
        uint lbIdent,
        System.Numerics.Quaternion spin)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (SimPeerSettledStatePosition.OwnsSteadyPhase(course))
            return false;

        actor.SetPosition(realmSpot);
        actor.ParentCellId = lbIdent;
        actor.Rotation = spin;
        return true;
    }

    internal static bool TryAdoptWireChamberFollowingRouting(
        PeerMotion distant,
        RemoteContactArm arm,
        uint wireChamberIdent)
    {
        return TryAdoptWireChamberFollowingRouting(
                distant,
                new RemoteContactRouting(arm, Placement: null),
                wireChamberIdent);
    }

    // Adopts the wire cell as the body's cell only when the routing moved the body to the wire pose
    internal static bool TryAdoptWireChamberFollowingRouting(
        PeerMotion distant,
        RemoteContactRouting routing,
        uint wireChamberIdent)
    {
        ArgumentNullException.ThrowIfNull(distant);
        if (routing.Arm is RemoteContactArm.FarSnapPlacement
            or RemoteContactArm.TeleportPlacement)
            return false;
        if (routing.Interpolation is SimPeerSettledStatePosition.Act.Enqueued)
            return false;
        distant.CellId = wireChamberIdent;
        return true;
    }

    internal static bool RequiresSpatialProjRecovery(
        OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return capture.WorldEntity is null || !capture.IsSpatiallyProjected;
    }

    private static SimPeerGrantedPositionArm ToConstraintArm(
        RemoteContactArm arm)
    {
        return arm switch
        {
            RemoteContactArm.TeleportPlacement =>
                SimPeerGrantedPositionArm.TeleportPlacement,
            RemoteContactArm.FarSnapPlacement =>
                SimPeerGrantedPositionArm.FarSnapPlacement,
            RemoteContactArm.SteadyStateInterpolate =>
                SimPeerGrantedPositionArm.NearInterpolate,
            RemoteContactArm.AirborneSnap =>
                SimPeerGrantedPositionArm.NearInterpolate,
            RemoteContactArm.UnroutedCatchUp =>
                SimPeerGrantedPositionArm.UnroutedCatchUp,
            _ => throw new ArgumentOutOfRangeException(
                nameof(arm), arm, "Unhandled RemoteContactArm in ToConstraintArm"),
        };
    }

    private static bool IsAvatarOid(uint oid) =>
        (oid & 0xFF000000u) == 0x50000000u;

    private static bool IsDoorLabel(string? label) => label == "Door";

    private bool IsLatestDistantArmLocusHolder()
    {
        return _distantArmLocusCapture is { } capture
        && _onlineActors.IsLatestLocusArbiter(
            capture, _distantArmLocusArbiterVer)
        && (_distantArmAnticipatedActor is null
            || ReferenceEquals(capture.WorldEntity, _distantArmAnticipatedActor));
    }

    private bool IsLatestMissileArmLocusHolder()
    {
        return _missileArmLocusCapture is { } capture
        && _onlineActors.IsLatestLocusArbiter(
            capture, _missileArmLocusArbiterVer);
    }

    private void SeedRemoteSpawnPlacement(
        PeerMotion distant,
        uint srvOid,
        MacAC.Mechanics.Realm.RealmActor actor,
        System.Numerics.Vector3 realmSpot,
        uint chamberIdent)
    {
        var (radius, height) = _locomotionCore.GetSetupCylinder(srvOid, actor);
        if (radius < 0.05f)
        {
            radius = 0.48f;
            height = 1.835f;
        }

        var carrierFlagSet = IsAvatarOid(srvOid)
            ? MacAC.Mechanics.Kinetics.MoverState.IsPlayer
              | MacAC.Mechanics.Kinetics.MoverState.EdgeSlide
            : MacAC.Mechanics.Kinetics.MoverState.EdgeSlide;

        if (!MacAC.Mechanics.Kinetics.SpawnSettler.TrySettle(
            _kineticsEngine,
            distant.Body,
            realmSpot,
            chamberIdent,
            radius,
            height,
            carrierFlagSet,
            actor.Id,
            distant.Movement.HitGround,
            distant.Motion.LeaveGround))

            return;
        distant.Airborne = !distant.Body.OnWalkable;
    }

    private bool RunRemoteTeleportHook(
        SimActorRecord canon,
        PeerMotion distant,
        Func<bool> isLatest)
    {
        var hub = distant.Host;
        return RemoteWarpHook.Run(
            new RemoteWarpHookActions(
                CancelMoveTo: distant.Movement.CancelMoveTo,
                UnStick: () => hub?.LocusKeeper.UnStick(),
                StopInterpolating: distant.Lerp.Clear,
                UnConstrain: () => hub?.LocusKeeper.UnConstrain(),
                NotifyTeleported: () => hub?.AlertTeleported(),
                ReportCollisionEnd: () =>
                    _onlineActors.ForceFinishImpactReporting(canon)),
            isLatest);
    }

    private RemoteContactRouting? ExecuteDistantArmRear(
        SimActorRecord canon,
        OnlineActorRecord locusCapture,
        PeerMotion distant,
        SimSovereignPositionRoute? course,
        uint oid,
        System.Numerics.Vector3 realmSpot,
        System.Numerics.Quaternion spin,
        ulong locusArbiterVer,
        MacAC.Mechanics.Realm.RealmActor? anticipatedActor)
    {
        _distantArmCanon = canon;
        _distantArmLocomotion = distant;
        _distantArmLocusCapture = locusCapture;
        _distantArmLocusArbiterVer = locusArbiterVer;
        _distantArmAnticipatedActor = anticipatedActor;

        var routing = ImposeDistantLinkRouting(
            _distantStanceSteer,
            canon,
            distant,
            course,
            realmSpot,
            spin,
            willBeDrTicked: WillAdvanceRemoteMotion(oid, distant),
            execWarpTap: _distantArmHooks.ExecWarpTap);

        return (routing.Arm is RemoteContactArm.FarSnapPlacement
                or RemoteContactArm.TeleportPlacement)
            && (!_distantArmHooks.IsLatestLocusHolder()
                || !ReferenceEquals(locusCapture.RemoteMotionRuntime, distant))
            ? null
            : routing;
    }

    private bool ExecuteStashedDistantWarpTap()
    {
        return _distantArmCanon is { } canon
        && _distantArmLocomotion is { } locomotion
        && RunRemoteTeleportHook(
            canon, locomotion, _distantArmHooks.IsLatestLocusHolder);
    }

    private MacAC.Client.Kinetics.DistantIncomingLocomotionRelayOutcome
        DispatchRemoteInboundMotion(
        MacAC.Wire.RealmSession.MoverMotionUpdate refresh,
        MacAC.Mechanics.Realm.RealmActor actor,
        OnlineActorMotionLedger? ledger,
        OnlineActorRecord approvedCapture,
        ulong approvedTravelArbiterVer,
        ulong approvedVelArbiterVer)
    {
        if (refresh.Guid == _avatarSrvOid)
            return default;

        bool IsLatestHolder(PeerMotion? anticipatedDistant = null) =>
            _onlineActors is { } online
            && online.IsLatestTravelArbiter(
                approvedCapture,
                approvedTravelArbiterVer)
            && online.IsLatestVelArbiter(
                approvedCapture,
                approvedVelArbiterVer)
            && ReferenceEquals(approvedCapture.WorldEntity, actor)
            && (ledger is null
                ? approvedCapture.AnimationRuntime is null
                : ReferenceEquals(approvedCapture.AnimationRuntime, ledger))
            && (anticipatedDistant is null
                || ReferenceEquals(
                    approvedCapture.RemoteMotionRuntime,
                    anticipatedDistant));
        if (!IsLatestHolder())
            return default;

        if (!_onlineActors.TryFetchDistantLocomotionCore(
                refresh.Guid,
                out ISimPeerMotion? distantCore)
            || distantCore is not PeerMotion distant)
        {
            distant = _onlineActors.FetchOrBuildDistantLocomotionCore(
                refresh.Guid);
            distant.Body.Orientation = actor.Rotation;
            distant.Body.Position = actor.Position;
        }
        if (!distant.Body.InContact)
        {
            SeedRemoteSpawnPlacement(
                distant,
                refresh.Guid,
                actor,
                distant.Body.Position,
                actor.VisChamberIdent ?? 0u);
        }
        if (!IsLatestHolder(distant))
            return default;

        var drain = _locomotionCore.EnsureRemoteMotionBindings(distant, ledger, refresh.Guid);
        uint directiveClass = ledger?.Sequencer?.CurrentMotion & 0xFF000000u
            ?? distant.Motion.InterpretedPhase.ForwardCommand & 0xFF000000u;
        if (directiveClass is 0u)
            directiveClass = 0x41000000u;

        var outcome =
            _distantIncomingLocomotion.Apply(
                refresh,
                distant.Movement,
                drain,
                distant.Host,
                distant.CellId,
                directiveClass,
                () => IsLatestHolder(distant));

        if (outcome.Superseded || !IsLatestHolder(distant))
            return outcome with { Superseded = true };

        if (outcome.AheadDirectiveAltered)
        {
            if (System.Environment.GetEnvironmentVariable(
                    "MACAC_REMOTE_VEL_DIAG") == "1")
            {
                System.Console.WriteLine(
                    $"[FWD_WIRE] guid={refresh.Guid:X8} "
                    + $"oldCmd=0x{outcome.PreviousForwardCommand:X8} "
                    + $"newCmd=0x{outcome.CurrentForwardCommand:X8} "
                    + $"newLow=0x{outcome.CurrentForwardCommand & 0xFFu:X2} "
                    + $"speed={refresh.MotionState.ForwardSpeed ?? 1f:F3}");
            }
            distant.EarlierSrvSpotMoment = 0.0;
        }

        if (outcome.AppliedInterpretedState && ledger is null)
        {
            _fightingMarkDriver?.OnLocomotionImposed(
                refresh.Guid,
                outcome.CurrentForwardCommand);
            if (!IsLatestHolder(distant))
                return outcome with { Superseded = true };
        }
        return outcome;
    }

    private bool WillAdvanceRemoteMotion(uint srvOid, PeerMotion distant)
    {
        return _onlineActors is { } core
            && core.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            && ReferenceEquals(capture.RemoteMotionRuntime, distant)
            && (capture.FinalKineticsPhase
                & MacAC.Mechanics.Kinetics.KineticStateFlags.Static) == 0
            && core.FetchTrunkObjectTimerDisposition(srvOid)
                is MacAC.Mechanics.Kinetics.CanonClockVerdict.Advance
            && core.IsLatestSpatialDistantLocomotion(capture, distant);
    }

    private bool WillSoleFifoInterpolation(
        uint oid,
        SimSovereignPositionRoute? course,
        System.Numerics.Vector3 realmSpot)
    {
        if (SimPeerWarpPosition.OwnsTeleportPlacement(course))
            return false;
        if (!_onlineActors!.TryFetchDistantLocomotionCore(oid, out ISimPeerMotion? core)
            || core is not PeerMotion distant)
            return false;
        if (!distant.Body.InContact)
            return false;
        var arm = SimPeerFarSnapPosition.ResolveArm(course);
        return arm is SimPeerGrantedPositionArm.FarSnapPlacement
            or SimPeerGrantedPositionArm.AirborneNoOperation
            ? false
            : !SimPeerSettledStatePosition.WouldSnap(
            distant, realmSpot, WillAdvanceRemoteMotion(oid, distant));
    }

    private SimSovereignPositionRoute? ClassifyDistantApprovedLocus(
        MacAC.Wire.RealmSession.MoverPositionUpdate refresh,
        SimActorRecord canon,
        MacAC.Mechanics.Kinetics.PoseStampVerdict stampDisposition,
        GrantedKineticsTimestamps timestamps,
        System.Numerics.Vector3 realmSpot)
    {
        return _onlineActors.ClassifyDistantApprovedLocus(
            canon,
            refresh,
            stampDisposition,
            timestamps,
            _playerController is { } driver
                ? System.Numerics.Vector3.Distance(realmSpot, driver.Position)
                : null);
    }
}
