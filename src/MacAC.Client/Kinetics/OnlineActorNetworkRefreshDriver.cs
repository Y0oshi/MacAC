using MacAC.Assets;
using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Link;
using MacAC.Client.Pulse;
using MacAC.Client.Realm;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;

namespace MacAC.Client.Kinetics;

internal sealed partial class OnlineActorNetworkRefreshDriver
    : IOnlineActorWirePulseSink,
      IOnlineActorSameEpochPulseSink,
      IAvatarLandblockSource
{
    private readonly OnlineActorCore _onlineActors;

    private readonly ClientThingChart _objects;

    private readonly OnlineActorFillingDriver _onlineActorHydration;

    private readonly ActorEffectDriver _actorFxList;

    private readonly OnlineActorDisplayDriver _onlineActorExhibit;

    private readonly OnlineActorLightDriver _onlineActorLamps;

    private readonly EquippedChildRenderDriver _equippedDescendantPainter;

    private readonly MissileDriver _missileDriver;

    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger> _animatedEntities;

    private readonly RemoteLocomotionObservationLedger _distantTravelObservations;

    private readonly RemotePhysicsUpdater _distantKineticsUpdater;

    private readonly RemoteInboundMotionRouter _distantIncomingLocomotion;

    private readonly OnlineActorMotionEngineDriver _locomotionCore;

    private readonly KineticEngine _kineticsEngine;

    private readonly IDatAccess _datFiles;

    private readonly IAnimReader _animFetcher;

    private readonly SimFightingTargetLedger? _fightingMarkDriver;

    private readonly OnlineRealmOriginLedger _origin;

    private readonly MacAC.Client.Paging.IAvatarWarpWireSink
        _ownAvatarWarp;

    private readonly ISimAvatarDriverSource _avatarDriverSrc;

    private readonly AvatarOutboundDriver _ownAvatarOutgoing;

    private readonly IAvatarKineticsHostSource _avatarHubSrc;

    private readonly IAvatarIdentitySource _avatarPersona;

    private readonly IKineticsScriptTimeSource _gameTime;

    private readonly IOnlineRealmSessionSource _session;

    private readonly OnlineActorInboundAuthorityTurnstile _arbiterLatch;

    private readonly ILocomotionTruthDiagnosticSink _travelTruthTelemetry;

    private readonly StashRealmDropMirrorDriver?
        _realmDiscardProj;

    private readonly SimGrantedPositionPilot _approvedLocusSteer;

    private readonly SimPeerPlacementPilot _distantStanceSteer;

    private SimActorRecord? _distantArmCanon;

    private PeerMotion? _distantArmLocomotion;

    private OnlineActorRecord? _distantArmLocusCapture;

    private ulong _distantArmLocusArbiterVer;

    private MacAC.Mechanics.Realm.RealmActor? _distantArmAnticipatedActor;

    private OnlineActorRecord? _missileArmLocusCapture;

    private ulong _missileArmLocusArbiterVer;

    private sealed class DistantArmHooks
    {
        internal readonly Func<bool> IsLatestLocusHolder;
        internal readonly Func<bool> ExecWarpTap;

        internal readonly Func<bool> IsLatestMissileLocusHolder;

        internal DistantArmHooks(OnlineActorNetworkRefreshDriver holder)
        {
            IsLatestLocusHolder = holder.IsLatestDistantArmLocusHolder;
            ExecWarpTap = holder.ExecuteStashedDistantWarpTap;
            IsLatestMissileLocusHolder =
                holder.IsLatestMissileArmLocusHolder;
        }
    }

    private readonly DistantArmHooks _distantArmHooks;

    public OnlineActorNetworkRefreshDriver(
        OnlineActorCore liveEntities,
        ClientThingChart objects,
        OnlineActorFillingDriver liveEntityHydration,
        ActorEffectDriver entityEffects,
        OnlineActorDisplayDriver liveEntityPresentation,
        OnlineActorLightDriver liveEntityLights,
        EquippedChildRenderDriver equippedChildRenderer,
        MissileDriver projectileController,
        OnlineActorMotionEngineView<OnlineActorMotionLedger> animatedEntities,
        RemoteLocomotionObservationLedger remoteMovementObservations,
        RemotePhysicsUpdater remotePhysicsUpdater,
        RemoteInboundMotionRouter remoteInboundMotion,
        OnlineActorMotionEngineDriver motionRuntime,
        KineticEngine physicsEngine,
        IDatAccess dats,
        IAnimReader animLoader,
        SimFightingTargetLedger? fightingMarkDriver,
        OnlineRealmOriginLedger origin,
        MacAC.Client.Paging.IAvatarWarpWireSink localPlayerTeleport,
        ISimAvatarDriverSource playerControllerSource,
        AvatarOutboundDriver localPlayerOutbound,
        IAvatarKineticsHostSource playerHostSource,
        IAvatarIdentitySource playerIdentity,
        IKineticsScriptTimeSource gameTime,
        IOnlineRealmSessionSource session,
        Action<uint, GrantedKineticsTimestamps> broadcastTimestamps,
        ILocomotionTruthDiagnosticSink movementTruthDiagnostics,
        SimGrantedPositionPilot acceptedPositionDrive,
        SimPeerPlacementPilot remotePlacementDrive,
        StashRealmDropMirrorDriver? realmDiscardProj = null)
    {
        _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _onlineActorHydration = liveEntityHydration ?? throw new ArgumentNullException(nameof(liveEntityHydration));
        _actorFxList = entityEffects ?? throw new ArgumentNullException(nameof(entityEffects));
        _onlineActorExhibit = liveEntityPresentation ?? throw new ArgumentNullException(nameof(liveEntityPresentation));
        _onlineActorLamps = liveEntityLights ?? throw new ArgumentNullException(nameof(liveEntityLights));
        _equippedDescendantPainter = equippedChildRenderer ?? throw new ArgumentNullException(nameof(equippedChildRenderer));
        _missileDriver = projectileController ?? throw new ArgumentNullException(nameof(projectileController));
        _animatedEntities = animatedEntities ?? throw new ArgumentNullException(nameof(animatedEntities));
        _distantTravelObservations = remoteMovementObservations ?? throw new ArgumentNullException(nameof(remoteMovementObservations));
        _distantKineticsUpdater = remotePhysicsUpdater ?? throw new ArgumentNullException(nameof(remotePhysicsUpdater));
        _distantIncomingLocomotion = remoteInboundMotion ?? throw new ArgumentNullException(nameof(remoteInboundMotion));
        _locomotionCore = motionRuntime ?? throw new ArgumentNullException(nameof(motionRuntime));
        _kineticsEngine = physicsEngine ?? throw new ArgumentNullException(nameof(physicsEngine));
        _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
        _animFetcher = animLoader ?? throw new ArgumentNullException(nameof(animLoader));
        _fightingMarkDriver = fightingMarkDriver;
        _origin = origin ?? throw new ArgumentNullException(nameof(origin));
        _ownAvatarWarp = localPlayerTeleport
            ?? throw new ArgumentNullException(nameof(localPlayerTeleport));
        _avatarDriverSrc = playerControllerSource ?? throw new ArgumentNullException(nameof(playerControllerSource));
        _ownAvatarOutgoing = localPlayerOutbound
            ?? throw new ArgumentNullException(nameof(localPlayerOutbound));
        _avatarHubSrc = playerHostSource ?? throw new ArgumentNullException(nameof(playerHostSource));
        _avatarPersona = playerIdentity ?? throw new ArgumentNullException(nameof(playerIdentity));
        _gameTime = gameTime ?? throw new ArgumentNullException(nameof(gameTime));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _arbiterLatch = new OnlineActorInboundAuthorityTurnstile(
            liveEntities,
            broadcastTimestamps);
        _travelTruthTelemetry = movementTruthDiagnostics
            ?? throw new ArgumentNullException(nameof(movementTruthDiagnostics));
        _approvedLocusSteer = acceptedPositionDrive
            ?? throw new ArgumentNullException(nameof(acceptedPositionDrive));
        _distantStanceSteer = remotePlacementDrive
            ?? throw new ArgumentNullException(nameof(remotePlacementDrive));
        _realmDiscardProj = realmDiscardProj;
        _distantArmHooks = new DistantArmHooks(this);
    }

    internal enum RemoteContactArm : byte
    {
        AirborneSnap,

        // Route 4a's near InterpolateTo branch
        SteadyStateInterpolate,

        FarSnapPlacement,

        TeleportPlacement,

        UnroutedCatchUp,
    }

    internal readonly record struct RemoteContactRouting(
        RemoteContactArm Arm,
        SimPeerPlacementExecutionStatus? Placement,
        SimPeerSettledStatePosition.Act? Interpolation = null);
}
