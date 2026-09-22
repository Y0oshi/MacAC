using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;
using MacAC.Sim.Play;
using MacAC.Sim.Kinetics;
using MacAC.Sim.Realm;

namespace MacAC.Sim.Presence;

public interface ISimDirectRealmMirror
{
    void ProjectSummon(SimActorRecord capture, bool isOwnAvatar);

    void ProjectLocus(SimActorRecord capture, bool isOwnAvatar, PoseStampVerdict disposition);

    void MiddleOnApprovedForceLocus(SimActorRecord capture);

    void CommenceWarp();

    SimDestinationFitness ReadyDest(long unveilGen, SimWarpDestination dest, SimRealmHarborMirrorTicket gateway);
}

public sealed class SimOnlineActorSessionDriver
{
    private const int GatewayParkTraceInterval = 100;

    private readonly SimCore _sim;
    private readonly RealmSession _session;
    private readonly Action<string> _trace;
    private readonly ISimDirectRealmMirror? _mirror;
    private readonly SimGrantedPositionPilot? _grantedLoci;
    private readonly Action? _onSigninDoneSent;
    private readonly Func<uint, bool> _descendantRecognized;
    private readonly Func<uint, ushort?> _ancestorIncarnation;
    private readonly Func<AncestorSignal.Parsed, bool> _admitAncestor;
    private bool _signinDoneSent;

    private (long Generation, SimWarpDestination Destination, SimRealmHarborMirrorTicket Projection)? _shelvedGateway;
    private int _shelvedGatewayReattempts;

    public SimOnlineActorSessionDriver(
        SimCore runtime,
        RealmSession session,
        Action<string>? trace = null,
        ISimDirectRealmMirror? realmProj = null,
        SimGrantedPositionPilot? approvedLocusSteer = null,
        Action? onSigninDoneSent = null)
    {
        _sim = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _trace = trace ?? (static _ => { });
        _mirror = realmProj;
        _grantedLoci = approvedLocusSteer;
        _onSigninDoneSent = onSigninDoneSent;
        _descendantRecognized = oid => Entities.Entities.TryGetSnapshot(oid, out _);
        _ancestorIncarnation = oid => Entities.Entities.TryGetSnapshot(oid, out RealmSession.MoverSpawn summon) ? summon.InstanceSequence : null;
        _admitAncestor = contender => Entities.TryApplyParent(contender, acknowledgeProj: null, out _);
    }

    private SimActorObjectLifetime Entities => _sim.EntityObjects;

    private uint SelfOid => _sim.AvatarIdentity.ServerGuid;

    public OnlineActorSessionSink MakeDrain()
    {
        return new(
        OnSpawned,
        OnDeleted,
        OnPickedUp,
        OnLocomotionUpdated,
        OnLocusUpdated,
        OnVectorUpdated,
        OnPhaseUpdated,
        OnAncestorUpdated,
        OnWarpBegun,
        OnLooksUpdated,
        _ => { },
        _ => { },
        _ => { });
    }

    public void PumpGatewayWrapUp() => ProgressGateway();

    private void OnSpawned(RealmSession.MoverSpawn summon)
    {
        bool isSelf = summon.Guid == SelfOid;
        SimActorRegistrationResult enrollment = _mirror is null
            ? Entities.EnrollActor(summon)
            : Entities.EnrollActorWithStartingResidence(summon, isOwnAvatar: isSelf);
        if (enrollment.Canonical is not { } canon)
            return;

        bool imposed = Entities.ImposeApprovedSummon(
            canon, canon.BuildIntegrationVersion, canon.Snapshot,
            replaceGen: enrollment.Inbound.Disposition is SpawnStampVerdict.NewGeneration);
        if (!imposed)
            return;

        _mirror?.ProjectSummon(canon, canon.ServerGuid == SelfOid);
        ReattemptDescendantsWaitingFor(canon.ServerGuid);

        // Without a projection host, the local player's own spawn is the moment login completes.
        if (_mirror is null && canon.ServerGuid == SelfOid && !_signinDoneSent)
        {
            _signinDoneSent = true;
            _session.TransmitPlayAct(LoginCompleteAction.Build());
            _session.TransmitHouseAsk();
            _onSigninDoneSent?.Invoke();
        }
    }

    private void OnDeleted(ObjectDeletion.Parsed erase)
    {
        if (erase.Guid == SelfOid
            || !Entities.TryAdmitErase(erase, isOwnAvatar: false, dropKeptObject: true, out SimActorDeleteAcceptance acceptance))

            return;

        Entities.ConcludeApprovedErase(acceptance);
        if (acceptance.RetiredCanon is { } retired && Entities.RetireCanonSole(retired) is { } miss)
            throw miss;
    }

    private void OnPickedUp(PickupNotice.Parsed lift) => _ = Entities.TryApplyPickup(lift, acknowledgeProj: null, out _);

    private void OnLocomotionUpdated(RealmSession.MoverMotionUpdate refresh)
    {
        bool isSelf = refresh.Guid == SelfOid;
        _ = Entities.TryApplyMotion(refresh, retainCargo: !isSelf || !refresh.IsAutonomous, acknowledgeProj: null, out _, out _);
    }

    private void OnVectorUpdated(VelocityUpdate.Parsed refresh) => _ = Entities.TryApplyVector(refresh, acknowledgeProj: null, out _);

    private void OnPhaseUpdated(GroupPhase.Parsed refresh)
    {
        _ = Entities.TryEnactState(refresh, acknowledgeProj: null, out _, out _);
    }

    private void OnLooksUpdated(ObjDescNotice.Parsed refresh) => _ = Entities.TryApplyObjDsc(refresh, acknowledgeProj: null, out _);

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private void OnLocusUpdated(RealmSession.MoverPositionUpdate refresh)
    {
        if (!SimSovereignPositionRouteSorter.IsValidBuildWireLocus(refresh.Position) || (refresh.Velocity is { } v && !Finite(v)))
            return;

        bool isSelf = refresh.Guid == SelfOid;
        AvatarLocomotionDriver? avatar = isSelf ? _sim.MovementOwner.Controller : null;
        bool recognized = Entities.TryApplyLocus(
            refresh,
            isSelf,
            forceLocusSpin: avatar?.CorpusFacing,
            latestOwnVel: avatar?.CorpusVel,
            acknowledgeProj: null,
            out PoseStampVerdict disposition,
            out _,
            out GrantedKineticsTimestamps stamps);
        if (!recognized || disposition is PoseStampVerdict.Rejected)
            return;

        if (!isSelf)
        {
            SealWireChamber(refresh);
            return;
        }

        if (disposition is PoseStampVerdict.Apply)
        {
            var position = refresh.Position;
            SimWarpDestination dest = new SimWarpDestination(
                refresh.Guid,
                refresh.InstanceSequence,
                refresh.PositionSequence,
                refresh.TeleportSequence,
                refresh.ForcePositionSequence,
                new Locus(position.LandblockId, new Vector3(position.PositionX, position.PositionY, position.PositionZ), new Quaternion(position.RotationX, position.RotationY, position.RotationZ, position.RotationW)));
            _sim.PassageHolder.OfferWarpDest(dest, stamps.TeleportAdvanced);
        }

        if (Entities.Entities.TryFetchEngaged(refresh.Guid, out SimActorRecord capture))
        {
            bool handledByPilot = false;
            if (disposition is PoseStampVerdict.ForcePosition)
            {
                _mirror?.MiddleOnApprovedForceLocus(capture);
                SimGrantedPositionExecutionStatus condition =
                    _grantedLoci?.TryExecuteAcceptedLocalPosition(capture, refresh, disposition, stamps, stamps.PreviousTeleport)
                    ?? SimGrantedPositionExecutionStatus.NotApplicable;
                handledByPilot = condition is not SimGrantedPositionExecutionStatus.NotApplicable;
            }
            if (!handledByPilot)
            {
                SealWireChamber(refresh);
                _mirror?.ProjectLocus(capture, isOwnAvatar: true, disposition);
            }
        }
        TryDoneGateway();
    }

    // Rebuckets a placed, non-missile entity into the cell the wire says it is in
    private void SealWireChamber(RealmSession.MoverPositionUpdate refresh)
    {
        if (!Entities.Entities.TryFetchEngaged(refresh.Guid, out SimActorRecord canon)
            || Entities.TryFetchStartingBuildResidence(canon, out _)
            || IsOnlineMissile(canon, refresh.Guid))

            return;
        _ = Entities.SealWireChamberRebucket(canon, refresh.Position.LandblockId);
    }

    private bool IsOnlineMissile(SimActorRecord canon, uint oid)
    {
        return oid != SelfOid
        && (canon.FinalKineticsCondition & KineticStateFlags.Missile) != 0
        && canon.Projectile is { } missile
        && ReferenceEquals(canon.KineticBody, missile.Body);
    }

    private void OnAncestorUpdated(AncestorSignal.Parsed refresh)
    {
        Entities.Entities.AncestorAttachments.Enqueue(refresh);
        FastenDescendant(refresh.ChildGuid);
    }

    private bool FastenDescendant(uint descendantOid)
    {
        var relations = Entities.Entities.AncestorAttachments;
        relations.Resolve(descendantOid, _descendantRecognized, _ancestorIncarnation, _admitAncestor);
        if (!relations.TryFetchLinedProj(descendantOid, out AnchorAttachmentRelation lined)
            || !Entities.Entities.TryFetchEngaged(descendantOid, out SimActorRecord canon))

            return false;
        if (!relations.CanSealIncarnation(lined, _ancestorIncarnation))
        {
            relations.RejectProj(lined);
            return false;
        }

        ulong locusVer = canon.PositionAuthorityVersion;
        if (!Entities.TryCommitAncestor(lined, acknowledgeProj: null, out _) || !relations.SealProj(lined, _ancestorIncarnation))
            return false;

        bool committed = Entities.SealApprovedAncestorCellless(canon, locusVer, acknowledgeProj: null);
        if (committed && KineticTelemetry.ProbeChildCellEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[child-cell] parent=0x{lined.ParentGuid:X8} child=0x{canon.ServerGuid:X8} new=0x{canon.WholeChamberTag:X8} cause=headless-attach"));
        }
        return committed;
    }

    private void ReattemptDescendantsWaitingFor(uint ancestorOid)
    {
        var waiting = Entities.Entities.AncestorAttachments.DescendantsUnresolvedForAncestor(ancestorOid);
        for (int idx = 0; idx < waiting.Count; ++idx)
            FastenDescendant(waiting[idx]);
    }

    private void OnWarpBegun(uint rawSeries)
    {
        ushort series = unchecked((ushort)rawSeries);
        var passage = _sim.PassageHolder;
        if (!passage.TryFifoWarpBegin(series))
            return;
        _mirror?.CommenceWarp();
        if (!passage.EngageQueuedWarp())
            throw new InvalidOperationException("Runtime rejected its queued headless teleport activation");
        TryDoneGateway();
    }

    private void TryDoneGateway()
    {
        var passage = _sim.PassageHolder;
        if (!passage.TryFetchApprovedWarpDest(out SimWarpDestination dest)
            || !passage.TryCommenceGatewayUnveil(dest.TeleportSequence, dest.CellId, out long gen))

            return;
        if (!passage.TryEnrollHubProj(gen, dest.CellId, out SimRealmHarborMirrorTicket proj))
            throw new InvalidOperationException("Runtime rejected the headless portal projection");

        Ack(passage, proj, SimRealmHarborAckStage.ProjectionRegistered);
        _shelvedGateway = (gen, dest, proj);
        _shelvedGatewayReattempts = 0;
        ProgressGateway();
    }

    // Finishes the parked portal once the destination is ready, then tells the server we are in
    private void ProgressGateway()
    {
        if (_shelvedGateway is not { } shelved)
            return;
        (long gen, SimWarpDestination dest, SimRealmHarborMirrorTicket proj) = shelved;

        var passage = _sim.PassageHolder;
        bool inside = (dest.CellId & 0xFFFFu) >= 0x0100u;
        SimDestinationFitness readiness = _mirror?.ReadyDest(gen, dest, proj)
            ?? new SimDestinationFitness(
                gen, dest.CellId, inside, IsUnhydratable: false, RequiredRenderRadius: inside ? 0 : 1,
                IsRenderNeighborhoodReady: true, AreCompositeTexturesReady: true, IsCollisionReady: true);
        if (!readiness.IsCollisionReady)
        {
            ++_shelvedGatewayReattempts;
            if (_shelvedGatewayReattempts % GatewayParkTraceInterval is 0)
                _trace($"headless: portal completion still parked after {_shelvedGatewayReattempts} retries generation={gen} cell=0x{dest.CellId:X8}");
            return;
        }

        _shelvedGateway = null;
        _shelvedGatewayReattempts = 0;

        if (!passage.AcknowledgeDestReadiness(readiness))
            throw new InvalidOperationException("Runtime rejected headless destination readiness");
        if (!passage.AcknowledgeGatewayMaterialized(gen, dest.TeleportSequence, dest.CellId))
            throw new InvalidOperationException("Runtime rejected headless portal materialization");
        Ack(passage, proj, SimRealmHarborAckStage.SimulationReleaseProjected);

        if (!passage.DemandDestReservationFree(proj))
            throw new InvalidOperationException("Runtime rejected headless destination release");
        Ack(passage, proj, SimRealmHarborAckStage.DestinationReservationReleased);

        if (!passage.AcknowledgeRealmViewRectShown(gen) || !passage.Complete(gen))
            throw new InvalidOperationException("Runtime rejected headless portal completion");
        Ack(passage, proj, SimRealmHarborAckStage.TerminalProjected);

        _session.TransmitPlayAct(LoginCompleteAction.Build());
        passage.FinishWarp();
        _trace($"headless: portal complete generation={gen} cell=0x{dest.CellId:X8}");
        _onSigninDoneSent?.Invoke();
    }

    private static void Ack(SimRealmCrossingLedger passage, SimRealmHarborMirrorTicket proj, SimRealmHarborAckStage juncture)
    {
        if (!passage.AcknowledgeHubProj(new SimRealmHarborAck(proj, juncture)))
            throw new InvalidOperationException($"Runtime rejected headless host acknowledgement {juncture}.");
    }
}
