using MacAC.Dat;
using MacAC.Client.Controls;
using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Heavens;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Kinetics;
using MacAC.Client.Paging;
using MacAC.Client.Preferences;
using MacAC.Client.Realm;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.PluginHosting;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;
using MacAC.Sim;
using MacAC.Sim.Actors;
using MacAC.Sim.Realm;
using Silk.NET.Windowing;

namespace MacAC.Client.Rigging;

internal sealed record OnlineDisplayDependencies(
    EngineKnobs Options,
    PlayPaneVisuals Graphics,
    IWindow Window,
    object DatLock,
    EnginePreferencesDriver Settings,
    HostQuiescenceTurnstile HostQuiescence,
    KineticEngine KineticEngine,
    KineticAssetCache KineticAssetCache,
    RealmPlayPhase WorldGameState,
    RealmSignals WorldEvents,
    SimCore Runtime,
    OnlineActorEngineSlot RuntimeSlot,
    DeferredOnlineActorMotionEngineWiring MotionBindings,
    DeferredActorEffectAdvanceSource EffectAdvance,
    ActorEffectPoseRegistry EffectPoses,
    RemotePhysicsUpdater RemotePhysicsUpdater,
    AvatarShadeLedger LocalPlayerShadow,
    OnlineActorMotionEngineView<OnlineActorMotionLedger> AnimatedEntities,
    MotionDisplayTelemetry AnimationDiagnostics,
    ActorTaxonomyStash ClassificationCache,
    SeeThroughFadeKeeper TranslucencyFades,
    CanonAlphaFifo RetailAlphaQueue,
    ChamberVis CellVisibility,
    OnlineRealmOriginLedger WorldOrigin,
    AvatarIdentityLedger PlayerIdentity,
    FollowCameraInputLedger ChaseCameraInput,
    PointerPositionLedger PointerPosition,
    AvatarApproachCompletionLedger PlayerApproachCompletions,
    PlayRasterizeAssetLifespan GpuResourceLifetime,
    TransferableAssetSocket<PortalTunnelDisplay> PortalTunnelFallback,
    AnimHookRouter HookRouter,
    IRenderFrameTelemetryLog RenderDiagnosticLog,
    WorldClock WorldTime,
    DeferredCanonicalRealmActorCountSource? DevWorldEntities,
    DeferredRenderFrameTelemetrySource? DevFrameDiagnostics,
    DeferredRenderFrameTelemetrySource UiFrameDiagnostics,
    Action<string> Log,
    Action<string>? Toast,
    MacAC.Client.Graphics.Packs.IRenderPackTelemetryCaptureSource?
        RenderPackDiagnostics = null)
{
    public PickPhase Selection => Runtime.ActHolder.Selection;

    public SimActorObjectLifetime EntityObjects =>
        Runtime.EntityObjects;

    public SimRealmCrossingLedger RealmPassage => Runtime.PassageHolder;

    public SimAvatarLocomotionLedger PlayerDriver =>
        Runtime.MovementOwner;

    public SimToonLedger Character => Runtime.ToonHolder;
}

internal sealed record OnlineDisplayResult(
    DeferredOnlineActorEngineComponentLifespan ComponentLifecycle,
    OnlineActorMotionEngineDriver MotionRuntime,
    DeferredOnlineActorParentAcceptance ParentAcceptance,
    ActorSummonBridge EntitySpawnAdapter,
    ActorProgramActivator EntityScriptActivator,
    CanonStaticAnimatingObjectRota StaticAnimationScheduler,
    SimRealmCrossingLedger WorldTransit,
    RealmEpochAvailabilityLedger WorldAvailability,
    GpuRealmPhase WorldState,
    RenderStageShadeEngine? RenderSceneShadow,
    OnlineActorCore LiveEntities,
    EnginePlacementDisplaySink PlacementProjection,
    OwnAvatarShadeSyncer LocalPlayerShadowSynchronizer,
    MissileDriver ProjectileController,
    OnlineActorMirrorWithdrawalDriver ProjectionWithdrawal,
    OnlineActorLightDriver Lights,
    OnlineActorMotionRota AnimationScheduler,
    OnlineActorMotionExhibitor AnimationPresenter,
    EquippedChildRenderDriver EquippedChildren,
    ActorEffectDriver EntityEffects,
    OnlineActorDisplayDriver Presentation,
    RealmPaintRouter? DrawDispatcher,
    CanonPickingStage SelectionScene,
    RealmPickingProbe SelectionQuery,
    PickingDealingDriver SelectionInteractions,
    RetainedWidgetGameplayWiring? RetainedGameplay,
    EffigyViewportPainter? PaperdollRenderer,
    EffigyFrameExhibitor? PaperdollPresenter,
    CreatureAssayViewportPainter? CreatureAppraisalRenderer,
    CreatureAssayFrameExhibitor? CreatureAppraisalPresenter,
    ChargenPreviewPainter? ChargenPreviewRenderer,
    ChargenPreviewDriver? ChargenPreviewController,
    ChargenPreviewPainter? SummaryPreviewRenderer,
    ChargenPreviewDriver? SummaryPreviewController,
    BatchFrustum EnvCellFrustum,
    EnvironChamberPainter? EnvCellRenderer,
    LandblockDisplayPipeline LandblockPipeline,
    ClipCycle ClipFrame,
    GatewayZDepthBitmaskPainter? PortalDepthMask,
    HeavensPainter? SkyRenderer,
    MotePainter? ParticleRenderer,
    RenderFrameTelemetryDriver FrameDiagnostics,
    OnlineDisplayEngineWiring RuntimeBindings,
    DeferredOnlineActorLandblockLoadedSink LandblockLoaded);

internal interface IGameWindowOnlineDisplayPublication
{
    void PublishLivePresentation(OnlineDisplayResult outcome);
}

internal enum OnlineDisplayAssemblyPoint
{
    CanonicalRuntimeCreated,
    CanonicalRuntimeBound,
    MotionRuntimeBound,
    ProjectionVisibilityBound,
    CorePresentationCreated,
    EffectRoutingBound,
    SelectionAndRadarBound,
    RetainedGameplayBound,
    PrivateCreatureViewportsCreated,
    EnvironmentCellsCreated,
    LandblockPipelineCreated,
    PortalResourcesCreated,
    SkyAndParticlesCreated,
    DiagnosticsBound,
    ResultPublished,
}

internal sealed partial class OnlineDisplayAssemblyPhase(
    OnlineDisplayDependencies dependencies,
    IGameWindowOnlineDisplayPublication publication,
    Action<OnlineDisplayAssemblyPoint>? flawInjection = null)
        : IOnlineDisplayAssemblyPhase<
        PlayPanePlatformOutcome<PlayPaneVisuals, Silk.NET.Input.IInputContext>,
        HubFeedCameraOutcome,
        SubstanceFxListSoundOutcome,
        PreferencesDevToolsResult,
        RealmRenderResult,
        DealingRetainedWidgetResult,
        OnlineDisplayResult>
{
    private readonly OnlineDisplayDependencies _deps = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));

    private readonly IGameWindowOnlineDisplayPublication _bulletin = publication
            ?? throw new ArgumentNullException(nameof(publication));

    private readonly Action<OnlineDisplayAssemblyPoint>? _flawInjection = flawInjection;

    private sealed class NullAnimFetcher : IAnimReader
    {
        public static NullAnimFetcher Instance { get; } = new();
        public MotionClip? PullAnim(uint ident) => null;
    }

    private sealed class DeferredPickingDealingSource
    {
        public PickingDealingDriver? Current { get; private set; }

        public void Bind(PickingDealingDriver val)
        {
            ArgumentNullException.ThrowIfNull(val);
            if (Current is not null)
            {
                throw new InvalidOperationException(
                    "Live motion selection interactions are by now bound");
            }
            Current = val;
        }
    }
}
