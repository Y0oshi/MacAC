using MacAC.Client.Controls;
using MacAC.Client.Dealing;
using MacAC.Client.Fighting;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Kinetics;
using MacAC.Client.Link;
using MacAC.Client.Paging;
using MacAC.Client.Preferences;
using MacAC.Client.Pulse;
using MacAC.Client.Realm;
using MacAC.Client.SimBridge;
using MacAC.Client.Telemetry;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.PluginHosting;
using MacAC.Sim;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;
using Silk.NET.Windowing;

namespace MacAC.Client.Rigging;

internal sealed record SessionAvatarDependencies(
    EngineKnobs Options,
    SimCore Runtime,
    IWindow Window,
    object DatLock,
    EnginePreferencesDriver Settings,
    PreferencesDevToolsResult SettingsDevTools,
    RealmEnvironmentDriver WorldEnvironment,
    RealmTableauDiagPhase WorldSceneDebugState,
    EngineTelemetryDirectiveSlot RuntimeDiagnosticCommands,
    OnlineFightingModeDirectiveSlot CombatModeCommands,
    EngineFightingModeOperationsSlot CombatModeOperations,
    HostQuiescenceTurnstile HostQuiescence,
    KineticEngine KineticEngine,
    KineticAssetCache KineticAssetCache,
    RealmPlayPhase WorldGameState,
    RealmSignals WorldEvents,
    ActorTaxonomyStash ClassificationCache,
    OnlineActorEngineSlot RuntimeSlot,
    OnlineActorMotionEngineView<OnlineActorMotionLedger> AnimatedEntities,
    RemoteLocomotionObservationLedger RemoteMovementObservations,
    RemotePhysicsUpdater RemotePhysicsUpdater,
    RemoteInboundMotionRouter RemoteInboundMotion,
    CanonInboundEventRouter InboundEntityEvents,
    DeferredOnlineActorMotionEngineWiring MotionBindings,
    AvatarIdentityLedger PlayerIdentity,
    AvatarKineticsHostSlot PlayerHost,
    AvatarModeLedger PlayerMode,
    FollowCameraInputLedger ChaseCameraInput,
    AvatarOutboundDriver PlayerOutbound,
    DeferredAvatarWarpWireSink TeleportSink,
    OnlineRealmOriginLedger WorldOrigin,
    RealmRenderRangeLedger RenderRange,
    AvatarShadeLedger PlayerShadow,
    ViewportAspectLedger ViewportAspect,
    AvatarApproachCompletionLedger PlayerApproachCompletions,
    PointerPositionLedger PointerPosition,
    RouterLocomotionInputSource LocomotionInput,
    IFeedGrabOrigin InputCapture,
    MacAC.Client.Graphics.Effects.MoteVisibilityDriver ParticleVisibility,
    SeeThroughFadeKeeper TranslucencyFades,
    ActorEffectPoseRegistry EffectPoses,
    PulseFrameClock UpdateClock,
    LocomotionTruthTelemetryDriver MovementDiagnostics,
    FightingAttackOperationsSlot CombatAttackOperations,
    FightingFeedbackSlot CombatFeedback,
    TransferableAssetSocket<PortalTunnelDisplay> PortalTunnelFallback,
    Action<string> Log,
    Func<string, bool>? TryHandlePluginCommand,
    SessionStatusScribe StatusWriter)
{
    public SimActionLedger Actions => Runtime.ActHolder;

    public SimActorObjectLifetime EntityObjects =>
        Runtime.EntityObjects;

    public SimAvatarLocomotionLedger PlayerController =>
        Runtime.MovementOwner;

    public SimStashLedger Inventory => Runtime.SatchelHolder;

    public SimCommsLedger Communication =>
        Runtime.CommunicationHolder;

    public SimToonLedger Character => Runtime.ToonHolder;
}

internal sealed record SessionAvatarResult(
    LandblockStreamer Streamer,
    PagingDriver Streaming,
    PagingOriginRecenterMarshal StreamingOriginRecenter,
    RealmRevealMarshal WorldReveal,
    DatSpawnClaimFillingClassifier SpawnClaimHydration,
    OnlineSessionDriver LiveSession,
    OnlineActorFillingDriver Hydration,
    OnlineActorDeletionDriver Deletion,
    OnlineActorNetworkRefreshDriver NetworkUpdates,
    OnlineActorOnlinenessDriver Liveness,
    OnlineActorSessionDriver SessionEvents,
    GameplayInputFrameDriver GameplayInput,
    PagingFrameDriver StreamingFrame,
    AvatarMotionDriver LocalPlayerAnimation,
    OwnAvatarShadeSyncer LocalPlayerShadow,
    OnlineAvatarFrameEngine LocalPlayerFrameRuntime,
    CanonAvatarFrameDriver LocalPlayerFrame,
    OnlineSpatialDisplayReconciler LiveSpatialReconciler,
    OnlineObjectFrameDriver LiveObjectFrame,
    AvatarModeDriver PlayerMode,
    AvatarMannerAutoListing PlayerModeAutoEntry,
    AvatarWarpDriver LocalTeleport,
    OnlineSessionHarbor SessionHost,
    EnginePlacementMirrorRetrySlot PlacementProjectionRetry,
    CurrentGameEngineBridge SimCore,
    GameplayFeedActRouter? GameplayActions,
    SessionAvatarEngineWiring RuntimeBindings);

internal interface IGameWindowSessionAvatarPublication
{
    void PublishSessionPlayer(SessionAvatarResult outcome);
}

internal enum SessionAvatarAssemblyPoint
{
    StreamingRadiiResolved,
    StreamerCreated,
    StreamerStarted,
    StreamingCreated,
    RuntimeSettingsBound,
    WorldRevealCreated,
    LiveSessionCreated,
    HydrationCreated,
    LiveEntityGraphBound,
    CombatOperationsBound,
    GameplayInputBound,
    UpdateLeavesCreated,
    PlayerModeBound,
    PortalTransferred,
    TeleportBound,
    SessionHostCreated,
    CommandsBound,
    GameplayActionsAttached,
    ResultPublished,
}

internal sealed partial class SessionAvatarAssemblyPhase(
    SessionAvatarDependencies dependencies,
    IGameWindowSessionAvatarPublication publication,
    Action<SessionAvatarAssemblyPoint>? flawInjection = null)
        : ISessionAvatarAssemblyPhase<
        HubFeedCameraOutcome,
        SubstanceFxListSoundOutcome,
        PreferencesDevToolsResult,
        RealmRenderResult,
        DealingRetainedWidgetResult,
        OnlineDisplayResult,
        SessionAvatarResult>
{
    private readonly SessionAvatarDependencies _deps = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));

    private readonly IGameWindowSessionAvatarPublication _bulletin = publication
            ?? throw new ArgumentNullException(nameof(publication));

    private readonly Action<SessionAvatarAssemblyPoint>? _flawInjection = flawInjection;
}
