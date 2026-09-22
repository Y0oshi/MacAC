using System.Numerics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Kinetics;

internal readonly record struct AcceptedMotionWirePulse(
    RealmSession.MoverMotionUpdate Update,
    OnlineActorRecord Record,
    GrantedKineticsTimestamps Timestamps,
    ulong MovementAuthorityVersion,
    ulong VelocityAuthorityVersion);

internal readonly record struct AcceptedVectorWirePulse(
    VelocityUpdate.Parsed Update,
    OnlineActorRecord Record,
    ulong VectorAuthorityVersion,
    ulong VelocityAuthorityVersion);

internal readonly record struct AcceptedStateWirePulse(
    GroupPhase.Parsed Update,
    OnlineActorRecord Record,
    ulong StateAuthorityVersion);

internal readonly record struct AcceptedPositionWirePulse(
    RealmSession.MoverPositionUpdate Update,
    SimActorRecord Canonical,
    RealmSession.MoverSpawn Spawn,
    GrantedKineticsTimestamps Timestamps,
    PoseStampVerdict TimestampDisposition,
    ulong PositionAuthorityVersion,
    ulong VelocityAuthorityVersion);

internal sealed class OnlineActorInboundAuthorityTurnstile(
    OnlineActorCore liveEntities,
    Action<uint, GrantedKineticsTimestamps> publishTimestamps)
{
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly Action<uint, GrantedKineticsTimestamps> _broadcastTimestamps = publishTimestamps
            ?? throw new ArgumentNullException(nameof(publishTimestamps));

    internal uint? PreviousOnlineAvatarLbIdent { get; private set; }

    internal void RestartSessPhase() => PreviousOnlineAvatarLbIdent = null;

    internal bool TryAdmitLocomotion(
        RealmSession.MoverMotionUpdate refresh,
        bool retainCargo,
        out AcceptedMotionWirePulse approved,
        out bool stampApproved)
    {
        approved = default;
        stampApproved = _onlineActors.TryEnactLocomotion(
            refresh,
            retainCargo,
            out _,
            out GrantedKineticsTimestamps timestamps);
        if (!stampApproved)
            return false;

        OnlineActorRecord? capture = null;
        ulong travelArbiterVer = 0;
        ulong velArbiterVer = 0;
        if (retainCargo)
        {
            if (!_onlineActors.TryFetchRecord(refresh.Guid, out capture))
                return false;
            travelArbiterVer = capture.TravelArbiterVer;
            velArbiterVer = capture.VelArbiterVer;
        }

        _broadcastTimestamps(refresh.Guid, timestamps);
        if (!retainCargo)
            return false;
        if (!_onlineActors.IsLatestTravelArbiter(
                capture!,
                travelArbiterVer)
            || !_onlineActors.IsLatestVelArbiter(
                capture!,
                velArbiterVer))

            return false;

        approved = new AcceptedMotionWirePulse(
            refresh,
            capture!,
            timestamps,
            travelArbiterVer,
            velArbiterVer);
        return true;
    }

    internal bool TryAdmitVector(
        VelocityUpdate.Parsed refresh,
        bool cargoIsValid,
        out AcceptedVectorWirePulse approved)
    {
        approved = default;
        if (!cargoIsValid
            || !_onlineActors.TryEnactVector(refresh, out _)
            || !_onlineActors.TryFetchRecord(refresh.Guid, out OnlineActorRecord capture))

            return false;

        approved = new AcceptedVectorWirePulse(
            refresh,
            capture,
            capture.VectorArbiterVer,
            capture.VelArbiterVer);
        return true;
    }

    internal bool TryAdmitPhase(
        GroupPhase.Parsed refresh,
        out AcceptedStateWirePulse approved)
    {
        approved = default;
        if (!_onlineActors.TryApplyState(refresh, out _, out _)
            || !_onlineActors.TryFetchRecord(refresh.Guid, out OnlineActorRecord capture))

            return false;

        approved = new AcceptedStateWirePulse(
            refresh,
            capture,
            capture.PhaseArbiterVer);
        return true;
    }

    internal bool TryAdmitLocus(
        RealmSession.MoverPositionUpdate refresh,
        uint ownAvatarOid,
        Quaternion? forceLocusSpin,
        Vector3? latestOwnVel,
        bool cargoIsValid,
        out AcceptedPositionWirePulse approved)
    {
        approved = default;
        if (!cargoIsValid)
            return false;

        bool isOwnAvatar = refresh.Guid == ownAvatarOid;
        bool recognized = _onlineActors.TryEnactLocus(
            refresh,
            isOwnAvatar,
            isOwnAvatar ? forceLocusSpin : null,
            isOwnAvatar ? latestOwnVel : null,
            out PoseStampVerdict disposition,
            out RealmSession.MoverSpawn summon,
            out GrantedKineticsTimestamps timestamps);
        if (!recognized)
        {
            if (isOwnAvatar)
                PreviousOnlineAvatarLbIdent = refresh.Position.LandblockId;
            return false;
        }

        SimActorRecord? canon = null;
        ulong locusArbiterVer = 0;
        ulong velArbiterVer = 0;
        if (disposition is not PoseStampVerdict.Rejected)
        {
            if (!_onlineActors.TryFetchCanon(refresh.Guid, out canon))
                return false;
            locusArbiterVer = canon.PositionAuthorityVersion;
            velArbiterVer = canon.VelArbiterVer;
        }

        _broadcastTimestamps(refresh.Guid, timestamps);
        if (disposition is PoseStampVerdict.Rejected)
            return false;
        if (!_onlineActors.IsLatestLocusArbiter(
                canon!,
                locusArbiterVer))

            return false;

        approved = new AcceptedPositionWirePulse(
            refresh,
            canon!,
            summon,
            timestamps,
            disposition,
            locusArbiterVer,
            velArbiterVer);
        return true;
    }

    internal void ObserveAcceptedLocalPosition(uint lbIdent) =>
        PreviousOnlineAvatarLbIdent = lbIdent;
}
