using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Controls;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Machine;
using MacAC.Client.Preferences;
using MacAC.Client.Realm;
using MacAC.Client.Rigging;
using MacAC.Cockpit.Input;
using MacAC.Host;
using MacAC.Mechanics.PluginHosting;
using MacAC.Sim;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace MacAC.Client.Graphics;

public sealed class PlayPane :
    IDisposable,
    IPlayPanePlatformBulletin<PlayPaneVisuals, IInputContext>,
    IPlayPaneHubFeedCameraBulletin,
    IPlayPaneSubstanceFxListSoundBulletin,
    IGameWindowRealmRenderPublication,
    IGameWindowDealingRetainedWidgetPublication,
    IGameWindowOnlineDisplayPublication,
    IGameWindowSessionAvatarPublication,
    IPlayPaneCycleTrunkBulletin
{
    private static double ClientTickerInstant() =>
        System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;

    internal static WindowOptions BuildStartupPaneKnobs(
        bool preciseAutomationFramebuffer,
        string persistedResolution,
        bool useVSynchronize,
        bool straightToonLaunch = false)
    {
        WindowOptions defaults = WindowOptions.DefaultVulkan;
        Vector2D<int> dims = new(800, 600);
        WindowBorder border = defaults.WindowBorder;
        if (preciseAutomationFramebuffer || straightToonLaunch)
        {
            if (!SilkEngineDisplayWindowTarget.TryDecodeResolution(
                    persistedResolution,
                    out int width,
                    out int height))
            {
                throw new InvalidOperationException(
                    preciseAutomationFramebuffer
                        ? "Exact automation framebuffer needs a valid persisted resolution"
                        : "Direct character launch needs a valid saved resolution");
            }
            dims = new Vector2D<int>(width, height);
            if (preciseAutomationFramebuffer)
                border = WindowBorder.Hidden;
        }

        return defaults with
        {
            Size = dims,
            Title = "macac — Vulkan",
            VSync = useVSynchronize,
            WindowBorder = border,
            IsVisible = !preciseAutomationFramebuffer,
        };
    }

    private readonly MacAC.Client.EngineKnobs _knobs;
    private readonly MotionDisplayTelemetry _animTelemetry;
    private readonly string _datDirection;
    private readonly RealmPlayPhase _realmPlayPhase;
    private readonly RealmSignals _realmSignals;
    private readonly HostQuiescenceTurnstile _hubStillness = new();
    private IWindow? _window;
    private bool _rasterizeLoopLoaded;
    private bool _nativeShutAsked;
    private bool _nativeExecReturned;
    private SilkWindowCallbackWiring? _paneHooks;
    private PlayPaneVisuals? _graphics;
    private MacAC.Client.Graphics.Gpu.Vulkan.VkGraphicsScope? _vulkanVisuals;
    private FramePacingRule _startupPacing;
    private MacAC.Cockpit.Settings.QualityKnobs _startupFidelity =
        MacAC.Cockpit.Settings.QualityKnobs.From(
            MacAC.Cockpit.Settings.QualityTier.High);
    private IInputContext? _input;
    private LandModernPainter? _land;
    private CameraDriver? _camDriver;
    private IDatAccess? _datFiles;
    private IBakedAssetSource? _preparedAssets;
    private readonly MacAC.Client.Controls.PointerPositionLedger _ptrLocus = new();
    private MacAC.Client.Controls.CameraPointerInputDriver? _camPtrFeed;
    private BitmapStash? _textureStash;
    private MacAC.Client.Graphics.Batching.RealmTriMeshBridge? _wbTriMeshBridge;
    private MacAC.Client.Graphics.Batching.ActorSummonBridge? _wbActorSummonBridge;
    private MacAC.Client.Graphics.Effects.ActorProgramActivator? _actorProgramActivator;
    private CanonStaticAnimatingObjectRota? _staticAnimScheduler;
    private RenderStageShadeEngine? _rasterizeTableauShade;
    private MacAC.Client.Graphics.Batching.RealmPaintRouter? _wbPaintRouter;
    private MacAC.Client.Graphics.Picking.CanonPickingStage? _canonPickTableau;
    private MacAC.Client.Dealing.RealmPickingProbe? _realmPickAsk;
    private MacAC.Client.Dealing.PickingDealingDriver? _pickInteractions;
    private DiagStrokePainter? _diagStrokes;
    private readonly MacAC.Client.Graphics.RealmTableauDiagPhase
        _realmTableauDiagPhase = new();

    private PhrasePainter? _phrasePainter;
    private BitmapFont? _diagTypeface;
    private readonly bool _cycleDiag = string.Equals(
        System.Environment.GetEnvironmentVariable("MACAC_WB_DIAG"),
        "1",
        System.StringComparison.Ordinal);

    private readonly MacAC.Client.Telemetry.CycleProfiler _cycleProfiler = new();
    private readonly MacAC.Client.Graphics.IRenderFrameTelemetryLog
        _rasterizeProbeTrace =
            new MacAC.Client.Graphics.ConsoleRenderFrameTelemetryLog();
    private readonly MacAC.Client.Graphics.DebugVmRenderFactsHerald
        _diagVmRasterizeFacts = new();
    private MacAC.Client.Graphics.RenderFrameTelemetryDriver?
        _rasterizeCycleTelemetry;
    private OnlineDisplayEngineWiring? _onlineExhibitMappings;
    private SessionAvatarEngineWiring? _sessAvatarMappings;
    private MacAC.Client.Telemetry.FrameScreenshotDriver? _frameScreenshots;
    private FrameRootEngineWiring? _cycleTrunkMappings;
    private IDisposable? _cycleGraphBulletin;
    private MacAC.Client.Graphics.GpuCycleFlightDriver? _gpuFrameFlights;
    private IClientGpuDevice? _gpuDev;
    private GpuDeviceCycleLifespan? _gpuCycleLifespan;
    private readonly MacAC.Client.Graphics.PlayCycleGraphSocket _cycleGraphs = new();
    private readonly MacAC.Client.Graphics.PlayRasterizeAssetLifespan
        _rasterizeAssetLifespan = new();
    private readonly MacAC.Client.Graphics.AssetConstructionTidyRegister
        _constructionCleanup = new();
    private readonly MacAC.Client.Realm.RealmEnvironmentDriver _worldEnvironment;
    private readonly PlayPaneLifespan _lifespan = new();
    private Exception? _runFailure;
    private readonly DisplayFramePacingDriver _readoutCyclePacing;
    private readonly EnginePreferencesDriver _runtimeSettings;
    private readonly BuildingDegradeDriver _structureDegrades;

    private MacAC.Client.Paging.LandblockStreamer? _streamer;
    private MacAC.Client.Paging.GpuRealmPhase _realmPhase = new();
    private MacAC.Client.Paging.LandblockDisplayPipeline?
        _lbExhibitPipe;
    private MacAC.Client.Graphics.EquippedChildRenderDriver? _equippedDescendantPainter;
    private MacAC.Client.Paging.PagingDriver? _pagingDriver;
    private MacAC.Client.Paging.PagingOriginRecenterMarshal?
        _pagingOriginRecenter;
    private MacAC.Client.Paging.RealmRevealMarshal? _worldReveal;
    private MacAC.Client.Paging.DatSpawnClaimFillingClassifier?
        _summonClaimHydration;
    private readonly MacAC.Client.Paging.DeferredAvatarWarpWireSink
        _ownAvatarWarpDrain = new();
    private MacAC.Client.Paging.AvatarWarpDriver?
        _ownAvatarWarp;
    private readonly MacAC.Client.Graphics.RealmRenderRangeLedger _rasterizeSpan =
        new(nearbyRadius: 4, farawayRadius: 12);
    private MacAC.Mechanics.Kinetics.KineticEngine _kineticsEngine =>
        _runtimeEntityObjects.Physics.Engine;

    private MacAC.Mechanics.Kinetics.KineticAssetCache _kineticsBlobStash =>
        _runtimeEntityObjects.Physics.DataCache;

    private MacAC.Mechanics.Kinetics.MoverState FetchCarrierPvpPhase(uint srvOid) =>
        MacAC.Mechanics.Kinetics.EntityContactFlagsExt.LocateCarrierPvpPhase(
            _runtimeEntityObjects.Objects,
            srvOid);

    private readonly MacAC.Client.Kinetics.RemotePhysicsUpdater _distantKineticsUpdater;
    private readonly MacAC.Client.Kinetics.RemoteInboundMotionRouter
        _distantIncomingLocomotion;
    private readonly MacAC.Client.Realm.CanonInboundEventRouter
        _incomingActorSignals = new();
    private OnlineActorMotionRota _onlineAnimScheduler = null!;
    private OnlineActorMotionExhibitor _animPresenter = null!;
    private readonly MacAC.Client.Controls.LocomotionTruthTelemetryDriver
        _travelTruthTelemetry;
    private readonly AvatarOutboundDriver _ownAvatarOutgoing;
    private readonly MacAC.Client.Pulse.PulseFrameClock _refreshCycleTimer;
    private MacAC.Client.Kinetics.MissileDriver? _missileDriver;
    private MacAC.Client.Realm.OnlineActorMirrorWithdrawalDriver?
        _onlineActorProjWithdrawal;
    private MacAC.Client.Realm.OnlineActorFillingDriver? _onlineActorHydration;
    private MacAC.Client.Kinetics.OnlineActorNetworkRefreshDriver? _onlineActorNetworkUpdates;
    private MacAC.Client.Link.OnlineActorSessionDriver? _onlineActorSessSignals;
    private readonly MacAC.Client.Kinetics.DeferredOnlineActorMotionEngineWiring
        _onlineActorLocomotionMappings = new();

    private readonly ChamberVis _chamberVis = new();

    private readonly object _datMutex = new();

    private float[]? _heightTable;
    private MacAC.Mechanics.Landscape.TerrainBlendContext? _blendCx;
    private System.Collections.Concurrent.ConcurrentDictionary<uint, MacAC.Mechanics.Landscape.SurfaceFacts>? _canvasStash;

    private MacAC.Client.Graphics.Batching.EnvironChamberPainter? _environChamberPainter;
    private MacAC.Client.Graphics.Batching.BatchFrustum? _environChamberFrustum;

    private MacAC.Client.Graphics.GatewayZDepthBitmaskPainter? _portalDepthMask;
    private readonly MacAC.Client.Graphics.TransferableAssetSocket<
        MacAC.Client.Graphics.PortalTunnelDisplay> _gatewayTunnelBackup = new();

    private ClipCycle? _clipCycle;

    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger> _animatedEntities;
    private readonly MacAC.Client.Realm.OnlineActorEngineSlot _onlineActorCoreSocket = new();

    private readonly MacAC.Client.Graphics.Batching.ActorTaxonomyStash _taxonomyStash = new();

    private MacAC.Mechanics.Kinetics.IAnimReader? _animFetcher;
    private MacAC.Sim.Kinetics.OnlineActorContactAssembler? _onlineActorImpactBuilder;

    private readonly MacAC.Mechanics.Kinetics.AnimHookRouter _tapRouter = new();
    private MacAC.Client.Rigging.MotionHookRegistrationSet?
        _tapRegistrations;
    private readonly MacAC.Client.Graphics.Effects.DeferredActorEffectAdvanceSource
        _actorFxProceed = new();

    private MacAC.Client.Sound.OpenAlSoundEngine? _soundEngine;
    private MacAC.Mechanics.Sound.DatWaveCache? _sfxStash;
    private MacAC.Client.Sound.DictionaryActorSoundChart? _actorSfxCharts;
    private MacAC.Client.Sound.SoundTapDrain? _soundDrain;

    private MacAC.Mechanics.Effects.EmitterSpecRegistry? _spoutRegistry;
    private MacAC.Mechanics.Effects.MoteSys? _moteSys;
    private MacAC.Mechanics.Effects.ParticleHookTap? _moteDrain;
    private readonly MacAC.Client.Graphics.Effects.MoteVisibilityDriver _moteVis = new();
    private readonly MacAC.Client.Graphics.Effects.ActorEffectPoseRegistry _fxPostures = new();
    private MacAC.Client.Graphics.Effects.MotionHookFrameQueue? _animTapCycles;
    private MacAC.Mechanics.Effects.KineticScriptRunner? _programRunner;
    private MacAC.Assets.Vfx.CanonKineticScriptLoader? _kineticsProgramFetcher;
    private MacAC.Client.Graphics.Effects.ActorEffectDriver? _actorFxList;
    private MacAC.Client.Graphics.MotePainter? _motePainter;
    private readonly MacAC.Client.Graphics.CanonAlphaFifo _canonAlphaFifo;
    private readonly MacAC.Client.Kinetics.RemoteLocomotionObservationLedger
        _distantTravelObservations = new();

    private MacAC.Client.Realm.OnlineActorCore? _onlineActors;
    private MacAC.Client.Realm.OnlineActorOnlinenessDriver? _onlineActorLiveness;

    private readonly MacAC.Client.Extensions.AppAutopilotSurface? _automation;
    private readonly SimCore _runtime;
    private readonly IDisposable _coreHubTenancy;
    private SimCommsLedger _coreCommunication =>
        _runtime.CommunicationHolder;
    private SimActionLedger _runtimeActions => _runtime.ActHolder;
    public MacAC.Mechanics.Targeting.PickPhase Selection =>
        _runtimeActions.Selection;
    private readonly SessionStatusScribe _conditionWriter;

    internal SessionStatusScribe ConditionWriter => _conditionWriter;
    public MacAC.Mechanics.Comms.ChatTranscript Chat => _coreCommunication.Chat;
    public MacAC.Mechanics.Comms.TurbineChatPhase TurbineChat =>
        _coreCommunication.TurbineChat;
    public MacAC.Mechanics.Fellows.FriendsLedger Friends =>
        _coreCommunication.Friends;
    public MacAC.Mechanics.Fellows.SquelchLedger Squelch =>
        _coreCommunication.Squelch;
    public MacAC.Mechanics.Fighting.FightingPhase Combat =>
        _runtimeActions.Combat;
    private SimActorObjectLifetime _runtimeEntityObjects =>
        _runtime.EntityObjects;
    private SimStashLedger _coreSatchel =>
        _runtime.SatchelHolder;
    private SimToonLedger _runtimeCharacter =>
        _runtime.ToonHolder;
    public MacAC.Mechanics.Gear.ItemManaGauge GearMana =>
        _coreSatchel.ItemMana;
    public MacAC.Mechanics.Arcana.ArcanumChart SpellTable => SpellBook.Metadata;
    public MacAC.Mechanics.Arcana.Grimoire SpellBook =>
        _runtimeCharacter.Spellbook;
    public IReadOnlyList<MacAC.Mechanics.Gear.HotbarSlot> Shortcuts =>
        _coreSatchel.Shortcuts.Items;
    public MacAC.Mechanics.Avatar.SelfState LocalPlayer =>
        _runtimeCharacter.LocalPlayer;

    private MacAC.Cockpit.Panels.Chat.ChatModel? _canonCommsVm;
    private MacAC.Client.Shell.WidgetHub? _uiHost;
    private MacAC.Client.Shell.CanonWidgetEngine? _retailUiRuntime;
    private readonly MacAC.Client.Shell.CanonWidgetEngineLease _canonWidgetTenancy = new();
    private DealingWidgetLateWiring? _dealingWidgetLateMappings;
    private readonly DeferredRenderFrameTelemetrySource _widgetCycleTelemetry = new();
    private readonly MacAC.Client.Graphics.Packs.DeferredRenderPackTelemetrySource
        _rasterizeBundleTelemetry = new();
    private readonly MacAC.Client.Fighting.FightingAttackOperationsSlot
        _fightingAssaultOps = new();
    private readonly MacAC.Client.Fighting.EngineFightingTargetOperationsSlot
        _fightingMarkOps = new();
    private readonly MacAC.Client.Fighting.EngineFightingModeOperationsSlot
        _fightingMannerOps = new();
    private readonly MacAC.Client.Arcana.EngineArcanaCastOperationsSlot
        _arcanumCastingOps = new();
    private readonly MacAC.Client.Fighting.FightingFeedbackSlot
        _fightingFeedback = new();
    private SimFightingAttackLedger? _fightingAssaultDriver;
    private MacAC.Client.Shell.GearDealingDriver? _gearDealingDriver;
    private MacAC.Client.Realm.ExternalContainerLifespanDriver? _externalVesselLifecycle;
    private MacAC.Client.Arcana.ArcanaEngine? _magicCore;
    private ArcanaCatalog? _magicRegistry;
    private readonly MacAC.Mechanics.Gear.StackSplitGauge _pileDivideQty = new();
    private MacAC.Client.Graphics.EffigyViewportPainter? _paperdollViewRectPainter;
    private MacAC.Client.Graphics.EffigyFrameExhibitor? _paperdollCyclePresenter;
    private MacAC.Client.Graphics.CreatureAssayViewportPainter?
        _beastAppraisalViewRectPainter;
    private MacAC.Client.Graphics.CreatureAssayFrameExhibitor?
        _beastAppraisalCyclePresenter;
    private MacAC.Client.Graphics.ChargenPreviewPainter? _chargenPreviewPainter;
    private MacAC.Client.Graphics.ChargenPreviewDriver? _chargenPreviewDriver;
    private MacAC.Client.Graphics.ChargenPreviewPainter? _summaryPreviewPainter;
    private MacAC.Client.Graphics.ChargenPreviewDriver? _summaryPreviewDriver;
    private readonly MacAC.Client.Extensions.BufferedWidgetRegistry? _widgetRegistry;
    private readonly MacAC.Client.Extensions.BufferedRasterizeBundleRegistry? _rasterizeBundleRegistry;
    private MacAC.Client.Extensions.GraphicalExtensionSession? _pluginSession;
    private const bool DevToolsTurnedOn = false;

    public readonly MacAC.Mechanics.Realm.WorldClock RealmTime;
    public readonly MacAC.Mechanics.Illumination.LightKeeper Lighting = new();

    public readonly MacAC.Mechanics.Realm.WeatherEngine Weather;
    private MacAC.Mechanics.Illumination.LightingHookTap? _illuminationDrain;
    private MacAC.Client.Graphics.Effects.OnlineActorLightDriver? _onlineActorLamps;
    private MacAC.Client.Realm.OnlineActorDisplayDriver? _onlineActorExhibit;

    private readonly MacAC.Mechanics.Drawing.SeeThroughFadeKeeper _seeThroughFades = new();
    private MacAC.Mechanics.Drawing.SeeThroughHookTap? _seeThroughDrain;

    private MacAC.Client.Graphics.StageLightingUboWiring? _tableauIlluminationUbo;
    private MacAC.Client.Graphics.Heavens.HeavensPainter? _heavensPainter;
    private SimAvatarLocomotionLedger _playerControllerSlot =>
        _runtime.MovementOwner;
    private AvatarLocomotionDriver? _playerController
        => _playerControllerSlot.Controller;
    private readonly MacAC.Client.Controls.FollowCameraInputLedger _pursueCamFeed = new();
    private MacAC.Client.Graphics.FollowCamera? _pursueCam
        => _pursueCamFeed.Legacy;
    private MacAC.Client.Graphics.CanonFollowCamera? _canonPursueCam
        => _pursueCamFeed.Retail;
    private readonly MacAC.Client.Controls.AvatarModeLedger _ownAvatarManner = new();
    private bool _avatarManner
        => _ownAvatarManner.IsPlayerMode;
    private readonly MacAC.Client.Controls.AvatarIdentityLedger
        _ownAvatarPersona;
    private uint _avatarSrvOid
    {
        get => _ownAvatarPersona.SrvOid;
        set => _ownAvatarPersona.SrvOid = value;
    }
    private readonly MacAC.Client.Kinetics.AvatarShadeLedger _ownAvatarShade = new();

    private readonly MacAC.Client.Controls.ViewportAspectLedger _viewRectAspect = new();
    private readonly FramebufferResizeDriver _framebufferRescale;
    private MacAC.Client.Controls.AvatarModeDriver? _avatarMannerDriver;
    private readonly MacAC.Client.Dealing.AvatarApproachCompletionLedger
        _avatarApproachCompletions = new();
    private MacAC.Client.Controls.AvatarMotionDriver?
        _ownAvatarAnim;
    private MacAC.Client.Kinetics.OwnAvatarShadeSyncer?
        _ownAvatarShadeSynchronizer;
    private MacAC.Client.Shell.Panels.ToonSheetSupplier? _toonSheetSupplier;
    private MacAC.Client.Controls.AvatarMannerAutoListing? _avatarMannerAutoListing;

    private MacAC.Client.Controls.SilkKeyboardOrigin? _kbSrc;
    private MacAC.Client.Controls.SilkPointerOrigin? _pointerSrc;
    private MacAC.Cockpit.Input.InputRouter? _feedRouter;
    private readonly MacAC.Client.Controls.RetainedWidgetInputCaptureSlot _keptFeedGrab;
    private readonly MacAC.Client.Controls.CompoundFeedGrabOrigin _feedGrab;
    private readonly MacAC.Client.Controls.RouterLocomotionInputSource _travelFeed;
    private readonly MacAC.Client.Controls.RouterCameraInputSource _camFeed = new();
    private MacAC.Client.Controls.IPointerGazeCursor? _pointerGazeCur;
    private MacAC.Client.Controls.GameplayInputFrameDriver? _gameplayFeedCycle;
    private MacAC.Client.Controls.GameplayFeedActRouter? _gameplayFeedActs;
    private MacAC.Client.Controls.RetainedWidgetGameplayWiring? _keptWidgetGameplayMapping;
    private readonly MacAC.Client.Telemetry.EngineTelemetryDirectiveSlot
        _coreProbeDirectives = new();
    private readonly MacAC.Client.Fighting.OnlineFightingModeDirectiveSlot
        _onlineFightingMannerDirectives = new();
    private readonly MacAC.Cockpit.Input.KeyBindingBook _tagMappings;
    private bool _tagMappingsPersisted;
    private readonly GraphicalHubPlatformServices _platformServices;
    private readonly UserStateLayout _applicationTrails;

    private static MacAC.Cockpit.Input.KeyBindingBook PullStartupTagMappings(
        string trail)
    {
        var mappings = MacAC.Client.Controls.CanonKeymapProfileStore.PullEngagedOrJson(
            trail, out string profileLabel);
        Console.WriteLine(
            $"keybinds: loaded {mappings.All.Count} bindings; active retail profile "
            + $"'{profileLabel}', JSON mirror {trail}");
        return mappings;
    }

    private OnlineSessionHarbor? _liveSessionHost;
    private MacAC.Wire.RealmSession? OnlineSess =>
        _liveSessionHost?.CurrentSess;
    private readonly MacAC.Client.Realm.OnlineRealmOriginLedger _onlineRealmOrigin = new();
    private int _onlineMiddleX => _onlineRealmOrigin.CenterX;
    private int _onlineMiddleY => _onlineRealmOrigin.CenterY;
    private static readonly IReadOnlyDictionary<uint, MacAC.Wire.RealmSession.MoverSpawn> VacantOnlineSummonLookup =
        new Dictionary<uint, MacAC.Wire.RealmSession.MoverSpawn>();
    private IReadOnlyDictionary<uint, MacAC.Wire.RealmSession.MoverSpawn> PreviousSpawns =>
        _onlineActors?.Snapshots ?? VacantOnlineSummonLookup;
    private readonly MacAC.Client.Controls.AvatarKineticsHostSlot
        _avatarHubSocket = new();
    private ActorKineticsHarbor? _avatarHub
        => _avatarHubSocket.Host;

    public PlayPane(
        MacAC.Client.EngineKnobs knobs,
        RealmPlayPhase realmPlayPhase,
        RealmSignals realmSignals,
        MacAC.Client.Extensions.BufferedWidgetRegistry? widgetRegistry = null)
        : this(
            knobs,
            realmPlayPhase,
            realmSignals,
            widgetRegistry,
            GraphicalHubPlatformServices.Resolve())
    {
    }

    internal PlayPane(
        MacAC.Client.EngineKnobs options,
        RealmPlayPhase realmPlayPhase,
        RealmSignals realmSignals,
        MacAC.Client.Extensions.BufferedWidgetRegistry? widgetRegistry,
        GraphicalHubPlatformServices platformServices,
        MacAC.Client.Extensions.AppAutopilotSurface? automation = null,
        MacAC.Client.Extensions.BufferedRasterizeBundleRegistry? rasterizeBundleRegistry = null)
    {
        _knobs = options ?? throw new System.ArgumentNullException(nameof(options));
        MacAC.Mechanics.Drawing.DrawTelemetry.DumpWalkTranscriptEnabled =
            options.DumpWalkTranscript;
        _automation = automation;
        _conditionWriter = new SessionStatusScribe(options.StatusFilePath);
        _platformServices = platformServices
            ?? throw new ArgumentNullException(nameof(platformServices));
        _applicationTrails = _platformServices.Paths;
        _tagMappings = PullStartupTagMappings(
            _applicationTrails.KeybindsTrail);
        _runtime = new SimCore(new SimCoreDependencies(
            _fightingAssaultOps,
            _fightingMarkOps,
            _fightingMannerOps,
            _arcanumCastingOps,
            Log: Console.WriteLine,
            TimeSyncDiagnostic:
                options.DumpSky ? Console.WriteLine : null));
        _coreHubTenancy = _runtime.ObtainHubTenancy(
            "graphical PlayPane");
        _automation?.Bind(_runtime, _runtime.ToonHolder, _runtime.ActHolder.SpellCast);
        _automation?.AttachMissileImpact(_kineticsEngine);
        _ownAvatarPersona = new MacAC.Client.Controls.AvatarIdentityLedger(
            _runtime.AvatarIdentity);
        _refreshCycleTimer = new MacAC.Client.Pulse.PulseFrameClock(
            _runtime.Clock);
        _worldEnvironment = new MacAC.Client.Realm.RealmEnvironmentDriver(
            _runtime.SurroundingsHolder,
            options.ForcedDayGroupIndex,
            Console.WriteLine,
            options.PinnedWorldDayFraction);
        var alphaTempBudgets =
            MacAC.Client.Graphics.Tenancy.AlphaScratchAllowanceProfile.Create(
                _knobs.ResidencyBudgets.AlphaScratchBytes);
        _canonAlphaFifo = new MacAC.Client.Graphics.CanonAlphaFifo(
            alphaTempBudgets.QueueBytes);
        RealmTime = _worldEnvironment.WorldMoment;
        Weather = _worldEnvironment.Weather;
        Weather.DeactivateGapFogSrc = () =>
            _runtime.ToonHolder.Options.GetOptionBit(
                MacAC.Wire.Messages.CharacterOptionId.DisableDistanceFog);
        _runtime.CommunicationHolder.ReadoutTimestampsSource = () =>
            _runtime.ToonHolder.Options.GetOptionBit(
                MacAC.Wire.Messages.CharacterOptionId.DisplayTimeStamps);
        _keptFeedGrab = new MacAC.Client.Controls.RetainedWidgetInputCaptureSlot();
        _feedGrab = new MacAC.Client.Controls.CompoundFeedGrabOrigin(
            new MacAC.Client.Controls.DeviceToolsFeedGrabOrigin(options.DevTools),
            _keptFeedGrab);
        _travelFeed = new MacAC.Client.Controls.RouterLocomotionInputSource(
            _playerControllerSlot,
            _feedGrab);
        _playerControllerSlot.ExecAsDefaultTravelSrc = () =>
            _runtime.ToonHolder.Options.GetOptionBit(
                MacAC.Wire.Messages.CharacterOptionId.ToggleRun);
        _framebufferRescale = new FramebufferResizeDriver(_viewRectAspect);
        _datDirection = options.DatDir;
        _realmPlayPhase = realmPlayPhase;
        _realmSignals = realmSignals;
        _readoutCyclePacing = new DisplayFramePacingDriver(
            options.UncappedRendering,
            _cycleProfiler,
            _platformServices.FramePacingWaiters);
        _runtimeSettings = new EnginePreferencesDriver(
            new JsonEnginePreferencesStorage(
                _applicationTrails.PrefsTrail),
            trace: Console.WriteLine,
            toonKnobVal: _runtime.ToonHolder.Options.GetOptionBit);
        _structureDegrades = new BuildingDegradeDriver(
            () => _runtimeSettings.ReadoutPreview);
        _animTelemetry = MotionDisplayTelemetry.FromEnvironment();
        _widgetRegistry = widgetRegistry;
        _rasterizeBundleRegistry = rasterizeBundleRegistry;
        _animatedEntities = new OnlineActorMotionEngineView<OnlineActorMotionLedger>(
            _onlineActorCoreSocket);
        _distantKineticsUpdater = new MacAC.Client.Kinetics.RemotePhysicsUpdater(
            _runtimeEntityObjects.Physics,
            _onlineActorLocomotionMappings.GetSetupCylinder,
            _onlineActorLocomotionMappings.FetchRigCarrierForm,
            MacAC.Client.Kinetics.DistantSrvControlledVelCycle.Apply,
            FetchCarrierPvpPhase);
        _distantIncomingLocomotion = new MacAC.Client.Kinetics.RemoteInboundMotionRouter(
            _onlineActorLocomotionMappings.RouteServerMoveTo,
            _onlineActorLocomotionMappings.StickToObjectFromWire);
        _travelTruthTelemetry =
            new MacAC.Client.Controls.LocomotionTruthTelemetryDriver(
                options.DumpMoveTruth,
                _playerControllerSlot,
                _ownAvatarPersona);
        _ownAvatarOutgoing = new AvatarOutboundDriver(
            _travelTruthTelemetry);
    }

    internal void BeginExtensionHosting(
        MacAC.Client.Extensions.GraphicalExtensionSession extensionSess)
    {
        ArgumentNullException.ThrowIfNull(extensionSess);
        if (_pluginSession is not null)
        {
            throw new InvalidOperationException(
                "The graphical plugin session is by now attached");
        }

        _pluginSession = extensionSess;
        extensionSess.Start();
    }

    public void Run()
    {
        _platformServices.ConfigurePaneBackend();
        EnginePreferencesCapture startup = _runtimeSettings.Startup;
        if (_knobs.VulkanCapabilityProbe)
        {
            using var sensor = new MacAC.Client.Graphics.Gpu.Vulkan.VkBringUpHost(
                _knobs,
                _platformServices,
                startup.Display.VSync);
            sensor.Run();
            return;
        }

        FramePacingRule startupPacing =
            _readoutCyclePacing.BootstrapStartup(startup.Display.VSync);
        WindowOptions knobs = BuildStartupPaneKnobs(
            _knobs.ExactAutomationFramebuffer,
            startup.Display.Resolution,
            startupPacing.UseVSync,
            straightToonLaunch: _knobs.LiveCharacterSelector is not null);
        _startupPacing = startupPacing;
        _startupFidelity = startup.Quality;

        _window = Window.Create(knobs);
        IWindow pane = _window;
        _runtimeSettings.AttachReadoutPane(
            new SilkEngineDisplayWindowTarget(pane),
            _knobs.ExactAutomationFramebuffer,
            straightToonLaunch: _knobs.LiveCharacterSelector is not null);
        if (!_knobs.LiveMode)
            _runtimeSettings.AssignGameplayReadout(true);
        _lifespan.BroadcastNativePane(
            pane,
            isRasterizeLoopLoaded: () => _rasterizeLoopLoaded,
            reqShut: pane.Close);
        _readoutCyclePacing.AttachCanvas(
            new SilkReadoutCyclePacingCanvas(_window));
        _paneHooks = SilkWindowCallbackWiring.Create(
            _window,
            new PaneHookMarks(
                OnLoad,
                OnUpdate,
                OnRender,
                OnClosing,
                OnFocusChanged,
                OnFramebufferResize),
            _readoutCyclePacing,
            _hubStillness);
        _paneHooks.Attach();
        try
        {
            _window.Run();
            _nativeExecReturned = true;
            CompleteShutdown(freeNativePane: false);
        }
        catch (Exception miss)
        {
            _constructionCleanup.KeepFrom(miss);
            _runFailure = miss;
            try
            {
                Telemetry.LocalCrashDigestWriter.TryEmit(
                    miss, _applicationTrails.Diagnostics,
                    CaptureLocalCrashReportContext, Console.Error.WriteLine);
            }
            catch { /* Diagnostics must never mask the frame-loop failure. */ }
            throw;
        }
    }

    private Telemetry.LocalCrashDigestScope CaptureLocalCrashReportContext()
    {
        var visuals = _vulkanVisuals;
        var capabilities = visuals?.Capabilities;
        uint? width = visuals?.Width;
        uint? height = visuals?.Height;
        var gpu = visuals is null ? null : new Telemetry.OwnCrashGpu(
            capabilities?.DeviceName, capabilities?.DriverInfo,
            capabilities?.InstanceApiVersion, capabilities?.DeviceApiVersion,
            capabilities?.DeviceApiVersionPacked,
            width is > 0 ? width : null, height is > 0 ? height : null,
            visuals.SampleCount);

        var driver = _playerController;
        Telemetry.LocalCrashRealm? realm = null;
        if (driver is not null)
        {
            var locus = driver.CellPosition;
            realm = new Telemetry.LocalCrashRealm(
                locus.ObjCellId, locus.Frame.Origin.X,
                locus.Frame.Origin.Y, locus.Frame.Origin.Z, driver.State.ToString());
        }
        return new Telemetry.LocalCrashDigestScope(gpu, realm);
    }

    void IPlayPanePlatformBulletin<PlayPaneVisuals, IInputContext>.PublishGraphics(
        PlayPaneVisuals visuals) =>
        BroadcastCompositionHolder(ref _graphics, visuals, "graphics API");

    void IPlayPanePlatformBulletin<PlayPaneVisuals, IInputContext>.PublishInput(
        IInputContext feed) =>
        BroadcastCompositionHolder(ref _input, feed, "input context");

    void IPlayPaneHubFeedCameraBulletin.PublishGpuFrameFlights(
        GpuCycleFlightDriver? val)
    {
        if (val is not null)
            BroadcastCompositionHolder(ref _gpuFrameFlights, val, "GPU frame flights");
    }

    void IPlayPaneHubFeedCameraBulletin.PublishGpuDevice(
        IClientGpuDevice val) =>
        BroadcastCompositionHolder(ref _gpuDev, val, "GPU device (RHI)");

    void IPlayPaneHubFeedCameraBulletin.PublishGpuFrameLifetime(
        GpuDeviceCycleLifespan val) =>
        BroadcastCompositionHolder(ref _gpuCycleLifespan, val, "GPU frame lifetime");

    void IPlayPaneHubFeedCameraBulletin.PublishKeyboardSource(
        MacAC.Client.Controls.SilkKeyboardOrigin val) =>
        BroadcastCompositionHolder(ref _kbSrc, val, "keyboard source");

    void IPlayPaneHubFeedCameraBulletin.PublishMouseSource(
        MacAC.Client.Controls.SilkPointerOrigin val) =>
        BroadcastCompositionHolder(ref _pointerSrc, val, "mouse source");

    void IPlayPaneHubFeedCameraBulletin.PublishMouseLookCursor(
        MacAC.Client.Controls.IPointerGazeCursor val) =>
        BroadcastCompositionHolder(ref _pointerGazeCur, val, "mouse-look cursor");

    void IPlayPaneHubFeedCameraBulletin.PublishInputDispatcher(
        MacAC.Cockpit.Input.InputRouter val) =>
        BroadcastCompositionHolder(ref _feedRouter, val, "input dispatcher");

    void IPlayPaneHubFeedCameraBulletin.PublishCameraController(
        CameraDriver val) =>
        BroadcastCompositionHolder(ref _camDriver, val, "camera controller");

    void IPlayPaneHubFeedCameraBulletin.PublishCameraPointerInput(
        MacAC.Client.Controls.CameraPointerInputDriver val) =>
        BroadcastCompositionHolder(ref _camPtrFeed, val, "camera pointer input");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishDatCollection(
        IDatAccess val)
    {
        BroadcastCompositionHolder(ref _datFiles, val, "DAT collection");

        if (_automation is null)
            return;
        _automation.AttachSpeciesLabelLocator(
            MacAC.Client.Shell.Panels.CreatureDisplayNamePicker.Load(val).Resolve);
        _automation.AttachSwatchTintLocator(
            new MacAC.Assets.CharGen.GenesisLookCatalog(val));
        if (!val.TryGet<SkillBook>(0x0E000004u, out var aptitudeChart)
            || aptitudeChart is null)
        {
            Console.Error.WriteLine(
                "plugin automation: retail SkillTable 0x0E000004 absent; "
                + "plugins will see unnamed skills");
            return;
        }

        var labels = new Dictionary<uint, string>(aptitudeChart.Skills.Count);
        var glyphs = new Dictionary<uint, uint>(aptitudeChart.Skills.Count);
        foreach (var listing in aptitudeChart.Skills)
        {
            labels[(uint)listing.Key] = listing.Value.Name;
            glyphs[(uint)listing.Key] = listing.Value.IconId;
        }
        _automation.AttachAptitudeLabels(labels);
        _automation.AttachAptitudeGlyphs(glyphs);
    }

    void IPlayPaneSubstanceFxListSoundBulletin.PublishPreparedAssetSource(
        IBakedAssetSource val) =>
        BroadcastCompositionHolder(
            ref _preparedAssets,
            val,
            "prepared asset source");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishMagicCatalog(
        ArcanaCatalog val)
    {
        BroadcastCompositionHolder(ref _magicRegistry, val, "magic catalog");
        _automation?.AttachMagicRegistry(val);
    }

    void IPlayPaneSubstanceFxListSoundBulletin.PublishAnimationLoader(
        MacAC.Mechanics.Kinetics.IAnimReader val) =>
        BroadcastCompositionHolder(ref _animFetcher, val, "animation loader");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishLiveEntityCollisionBuilder(
        MacAC.Sim.Kinetics.OnlineActorContactAssembler val) =>
        BroadcastCompositionHolder(
            ref _onlineActorImpactBuilder,
            val,
            "live-entity collision builder");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishEmitterRegistry(
        MacAC.Mechanics.Effects.EmitterSpecRegistry val) =>
        BroadcastCompositionHolder(ref _spoutRegistry, val, "emitter registry");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishParticleSystem(
        MacAC.Mechanics.Effects.MoteSys val) =>
        BroadcastCompositionHolder(ref _moteSys, val, "particle system");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishParticleSink(
        MacAC.Mechanics.Effects.ParticleHookTap val) =>
        BroadcastCompositionHolder(ref _moteDrain, val, "particle hook sink");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishAnimationHookFrames(
        MacAC.Client.Graphics.Effects.MotionHookFrameQueue val) =>
        BroadcastCompositionHolder(
            ref _animTapCycles,
            val,
            "animation-hook frame queue");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishPhysicsScriptLoader(
        MacAC.Assets.Vfx.CanonKineticScriptLoader val) =>
        BroadcastCompositionHolder(
            ref _kineticsProgramFetcher,
            val,
            "physics-script loader");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishPhysicsScriptRunner(
        MacAC.Mechanics.Effects.KineticScriptRunner val) =>
        BroadcastCompositionHolder(ref _programRunner, val, "physics-script runner");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishLightingSink(
        MacAC.Mechanics.Illumination.LightingHookTap val) =>
        BroadcastCompositionHolder(ref _illuminationDrain, val, "lighting hook sink");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishTranslucencySink(
        MacAC.Mechanics.Drawing.SeeThroughHookTap val) =>
        BroadcastCompositionHolder(
            ref _seeThroughDrain,
            val,
            "translucency hook sink");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishHookRegistrations(
        MacAC.Client.Rigging.MotionHookRegistrationSet val) =>
        BroadcastCompositionHolder(
            ref _tapRegistrations,
            val,
            "animation-hook registrations");

    void IPlayPaneSubstanceFxListSoundBulletin.PublishAudio(
        SubstanceSoundGraph val)
    {
        ArgumentNullException.ThrowIfNull(val);
        if (_sfxStash is not null
            || _soundEngine is not null
            || _actorSfxCharts is not null
            || _soundDrain is not null)
        {
            throw new InvalidOperationException(
                "The PlayPane composition shell by now owns audio state");
        }

        _sfxStash = val.SoundCache;
        _soundEngine = val.Engine;
        _actorSfxCharts = val.EntitySoundTables;
        _soundDrain = val.HookSink;
    }

    void IGameWindowRealmRenderPublication.PublishSceneLighting(
        StageLightingUboWiring val) =>
        BroadcastCompositionHolder(
            ref _tableauIlluminationUbo,
            val,
            "scene lighting");

    void IGameWindowRealmRenderPublication.PublishDebugLines(
        DiagStrokePainter val) =>
        BroadcastCompositionHolder(ref _diagStrokes, val, "debug lines");

    void IGameWindowRealmRenderPublication.PublishHudResources(
        BitmapFont typeface,
        PhrasePainter phrase)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ArgumentNullException.ThrowIfNull(phrase);
        if (_diagTypeface is not null || _phrasePainter is not null)
        {
            throw new InvalidOperationException(
                "The PlayPane composition shell by now owns world HUD resources");
        }
        _diagTypeface = typeface;
        _phrasePainter = phrase;
    }

    void IGameWindowRealmRenderPublication.PublishTerrain(
        LandModernPainter val) =>
        BroadcastCompositionHolder(ref _land, val, "terrain renderer");

    void IGameWindowRealmRenderPublication.PublishTerrainBuildState(
        float[] heightChart,
        MacAC.Mechanics.Landscape.TerrainBlendContext blending,
        System.Collections.Concurrent.ConcurrentDictionary<
            uint,
            MacAC.Mechanics.Landscape.SurfaceFacts> canvasStash)
    {
        ArgumentNullException.ThrowIfNull(heightChart);
        ArgumentNullException.ThrowIfNull(blending);
        ArgumentNullException.ThrowIfNull(canvasStash);
        if (_heightTable is not null || _blendCx is not null || _canvasStash is not null)
        {
            throw new InvalidOperationException(
                "The PlayPane composition shell by now owns terrain build state");
        }
        _heightTable = heightChart;
        _blendCx = blending;
        _canvasStash = canvasStash;
    }

    void IGameWindowRealmRenderPublication.PublishWbMeshAdapter(
        RealmTriMeshBridge val) =>
        BroadcastCompositionHolder(ref _wbTriMeshBridge, val, "WB mesh adapter");

    void IGameWindowRealmRenderPublication.PublishTextureCache(
        BitmapStash val) =>
        BroadcastCompositionHolder(ref _textureStash, val, "texture cache");

    void IGameWindowDealingRetainedWidgetPublication.PublishInteractionRetainedUi(
        DealingRetainedWidgetResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (_fightingAssaultDriver is not null
            || _externalVesselLifecycle is not null
            || _gearDealingDriver is not null
            || _dealingWidgetLateMappings is not null
            || _uiHost is not null
            || _retailUiRuntime is not null
            || _canonCommsVm is not null
            || _toonSheetSupplier is not null
            || _magicCore is not null
            || _frameScreenshots is not null)
        {
            throw new InvalidOperationException(
                "The PlayPane composition shell by now owns interaction/UI state");
        }

        _fightingAssaultDriver = outcome.CombatAttack;
        _externalVesselLifecycle = outcome.ExternalContainerLifecycle;
        _gearDealingDriver = outcome.ItemInteraction;
        _automation?.AttachEquipment(
            (gearIdent, askedLocale) =>
                outcome.ItemInteraction.TryWieldGear(
                    gearIdent,
                    (MacAC.Mechanics.Gear.WieldBitmask)askedLocale),
            () => outcome.ItemInteraction.IsAutoWieldOccupied);
        _automation?.AttachGearList(
            outcome.ItemInteraction.TryUseGearForAutomation,
            outcome.ItemInteraction.TryEnactGear,
            outcome.ItemInteraction.TryRelocateGearForAutomation,
            outcome.ItemInteraction.TryCombineGearListForAutomation,
            outcome.ItemInteraction.TryDiscardGearForAutomation,
            outcome.ItemInteraction.TryHandGearForAutomation,
            outcome.ItemInteraction.PutRealmGearInBackpack,
            outcome.ItemInteraction.TryEvaluateForAutomation,
            outcome.ItemInteraction.TrySalvageGearListForAutomation,
            (merchantIdent, gearIdent, quantity) => outcome.ItemInteraction.TryVend(
                merchantIdent,
                [(quantity, gearIdent)]));
        _dealingWidgetLateMappings = outcome.LateBindings;
        _magicCore = outcome.Magic;
        if (outcome.RetainedUi is { } kept)
        {
            _uiHost = kept.Host;
            _retailUiRuntime = kept.Runtime;
            _canonCommsVm = kept.Chat;
            _toonSheetSupplier = kept.CharacterSheet;
            _frameScreenshots = kept.Screenshots;
            kept.Runtime.FastenNativeCurPane(_window?.Native?.Glfw ?? 0);
        }
    }

    void IGameWindowOnlineDisplayPublication.PublishLivePresentation(
        OnlineDisplayResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (_onlineActors is not null
            || _wbActorSummonBridge is not null
            || _actorProgramActivator is not null
            || _staticAnimScheduler is not null
            || _rasterizeTableauShade is not null
            || _wbPaintRouter is not null
            || _canonPickTableau is not null
            || _realmPickAsk is not null
            || _pickInteractions is not null
            || _keptWidgetGameplayMapping is not null
            || _equippedDescendantPainter is not null
            || _actorFxList is not null
            || _onlineActorExhibit is not null
            || _missileDriver is not null
            || _onlineActorProjWithdrawal is not null
            || _onlineActorLamps is not null
            || _onlineAnimScheduler is not null
            || _animPresenter is not null
            || _paperdollViewRectPainter is not null
            || _paperdollCyclePresenter is not null
            || _beastAppraisalViewRectPainter is not null
            || _beastAppraisalCyclePresenter is not null
            || _chargenPreviewPainter is not null
            || _chargenPreviewDriver is not null
            || _environChamberPainter is not null
            || _environChamberFrustum is not null
            || _lbExhibitPipe is not null
            || _portalDepthMask is not null
            || _clipCycle is not null
            || _heavensPainter is not null
            || _motePainter is not null
            || _rasterizeCycleTelemetry is not null
            || _onlineExhibitMappings is not null)
        {
            throw new InvalidOperationException(
                "The PlayPane composition shell by now owns live presentation state");
        }

        _wbActorSummonBridge = outcome.EntitySpawnAdapter;
        _actorProgramActivator = outcome.EntityScriptActivator;
        _staticAnimScheduler = outcome.StaticAnimationScheduler;
        _realmPhase = outcome.WorldState;
        _rasterizeTableauShade = outcome.RenderSceneShadow;
        _onlineActors = outcome.LiveEntities;
        _missileDriver = outcome.ProjectileController;
        _onlineActorProjWithdrawal = outcome.ProjectionWithdrawal;
        _onlineActorLamps = outcome.Lights;
        _onlineAnimScheduler = outcome.AnimationScheduler;
        _animPresenter = outcome.AnimationPresenter;
        _equippedDescendantPainter = outcome.EquippedChildren;
        _actorFxList = outcome.EntityEffects;
        _onlineActorExhibit = outcome.Presentation;
        _wbPaintRouter = outcome.DrawDispatcher;
        _canonPickTableau = outcome.SelectionScene;
        _realmPickAsk = outcome.SelectionQuery;
        _pickInteractions = outcome.SelectionInteractions;
        _automation?.AttachPickActs(act =>
            outcome.SelectionInteractions.ServiceFeedAct(act switch
            {
                MacAC.Extensibility.Automation.SelectionVerb.PreviousSelection =>
                    FeedAct.SelectionPreviousSelection,
                MacAC.Extensibility.Automation.SelectionVerb.PreviousPlayer =>
                    FeedAct.SelectionPreviousPlayer,
                MacAC.Extensibility.Automation.SelectionVerb.NextPlayer =>
                    FeedAct.SelectionNextPlayer,
                _ => FeedAct.None,
            }));
        _keptWidgetGameplayMapping = outcome.RetainedGameplay;
        _paperdollViewRectPainter = outcome.PaperdollRenderer;
        _paperdollCyclePresenter = outcome.PaperdollPresenter;
        _beastAppraisalViewRectPainter = outcome.CreatureAppraisalRenderer;
        _beastAppraisalCyclePresenter = outcome.CreatureAppraisalPresenter;
        _chargenPreviewPainter = outcome.ChargenPreviewRenderer;
        _chargenPreviewDriver = outcome.ChargenPreviewController;
        _summaryPreviewPainter = outcome.SummaryPreviewRenderer;
        _summaryPreviewDriver = outcome.SummaryPreviewController;
        _environChamberFrustum = outcome.EnvCellFrustum;
        _environChamberPainter = outcome.EnvCellRenderer;
        _lbExhibitPipe = outcome.LandblockPipeline;
        _clipCycle = outcome.ClipFrame;
        _portalDepthMask = outcome.PortalDepthMask;
        _heavensPainter = outcome.SkyRenderer;
        _motePainter = outcome.ParticleRenderer;
        _rasterizeCycleTelemetry = outcome.FrameDiagnostics;
        _onlineExhibitMappings = outcome.RuntimeBindings;
    }

    void IGameWindowSessionAvatarPublication.PublishSessionPlayer(
        SessionAvatarResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (_streamer is not null
            || _pagingDriver is not null
            || _pagingOriginRecenter is not null
            || _worldReveal is not null
            || _summonClaimHydration is not null
            || _onlineActorHydration is not null
            || _onlineActorNetworkUpdates is not null
            || _onlineActorLiveness is not null
            || _onlineActorSessSignals is not null
            || _gameplayFeedCycle is not null
            || _ownAvatarAnim is not null
            || _ownAvatarShadeSynchronizer is not null
            || _avatarMannerDriver is not null
            || _avatarMannerAutoListing is not null
            || _ownAvatarWarp is not null
            || _liveSessionHost is not null
            || _gameplayFeedActs is not null
            || _sessAvatarMappings is not null)
        {
            throw new InvalidOperationException(
                "The PlayPane composition shell by now owns session/player state");
        }

        _streamer = outcome.Streamer;
        _pagingDriver = outcome.Streaming;
        _pagingOriginRecenter = outcome.StreamingOriginRecenter;
        _worldReveal = outcome.WorldReveal;
        _summonClaimHydration = outcome.SpawnClaimHydration;
        _onlineActorHydration = outcome.Hydration;
        _automation?.AttachGhostDeletion(outcome.Deletion.EraseClientGhost);
        _onlineActorNetworkUpdates = outcome.NetworkUpdates;
        _onlineActorLiveness = outcome.Liveness;
        _onlineActorSessSignals = outcome.SessionEvents;
        _gameplayFeedCycle = outcome.GameplayInput;
        _ownAvatarAnim = outcome.LocalPlayerAnimation;
        _ownAvatarShadeSynchronizer = outcome.LocalPlayerShadow;
        _avatarMannerDriver = outcome.PlayerMode;
        _avatarMannerAutoListing = outcome.PlayerModeAutoEntry;
        _ownAvatarWarp = outcome.LocalTeleport;
        _liveSessionHost = outcome.SessionHost;
        _automation?.AttachSessDirectives(outcome.SimCore);
        _gameplayFeedActs = outcome.GameplayActions;
        _sessAvatarMappings = outcome.RuntimeBindings;
    }

    void IPlayPaneCycleTrunkBulletin.PublishFrameRoots(
        CycleTrunkOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (_cycleTrunkMappings is not null
            || _cycleGraphBulletin is not null)
        {
            throw new InvalidOperationException(
                "The PlayPane composition shell by now owns frame roots");
        }

        _cycleTrunkMappings = outcome.RuntimeBindings;
        _cycleGraphBulletin = outcome.FrameGraphPublication;
    }

    private static void BroadcastCompositionHolder<T>(
        ref T? dest,
        T val,
        string label)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(val);
        if (dest is not null)
            throw new InvalidOperationException(
                $"The PlayPane composition shell by now owns {label}.");
        dest = val;
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_graphics), nameof(_input))]
    private PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> AcquirePlatform()
    {
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform =
            PlayPanePlatformAcquisition.Acquire(
                BuildVisuals,
                static visuals => visuals.Dispose(),
                () => _window!.CreateInput(),
                static feed => feed.Dispose(),
                this);
        return _graphics is null || _input is null
            ? throw new InvalidOperationException(
                "Platform acquisition returned without publishing both owners")
            : platform;
    }

    private static Func<int, int, byte[]> BuildBackbufferReader(
        PlayPaneVisuals visuals,
        IClientGpuDevice dev) =>
        (width, height) =>
            MacAC.Client.Telemetry.FrameScreenshotDriver.FlipRanks(
                dev.CaptureBackbuffer(width, height),
                width,
                height);

    private PlayPaneVisuals BuildVisuals()
    {
        var vulkan =
            MacAC.Client.Graphics.Gpu.Vulkan.VkGraphicsScope.Obtain(
                _window!,
                _knobs,
                _platformServices,
                _startupPacing,
                _startupFidelity.MsaaSamples,
                Console.WriteLine);
        _vulkanVisuals = vulkan;
        return new VkGameWindowGraphics(vulkan);
    }

    private void OnLoad()
    {

        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform = AcquirePlatform();

        DisplayModeRegistry.SetupFromPane(_window!);

        PaneGlyphFetcher.Apply(_window!);

        GameWindowAssemblyPipeline.Run<
            PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext>,
            HubFeedCameraOutcome,
            SubstanceFxListSoundOutcome,
            PreferencesDevToolsResult,
            RealmRenderResult,
            DealingRetainedWidgetResult,
            OnlineDisplayResult,
            SessionAvatarResult,
            CycleTrunkOutcome>(
            platform,
            platformOutcome => new HostInputCameraAssemblyPhase(
                new HubFeedCameraDeps(
                    _framebufferRescale,
                    _window!.FramebufferSize,
                    _hubStillness,
                    _feedGrab,
                    _tagMappings,
                    _travelFeed,
                    _camFeed,
                    _ownAvatarManner,
                    _pursueCamFeed,
                    _ptrLocus,
                    _rasterizeProbeTrace,
                    _knobs.InitialOrbitDistanceMeters,
                    _knobs.InitialOrbitYawDegrees,
                    _knobs.InitialOrbitPitchDegrees),
                this).Compose(platformOutcome),
            (platformOutcome, hubFeedCam) =>
                new ContentEffectsAudioAssemblyPhase(
                new SubstanceFxListSoundDeps(
                    _datDirection,
                    _knobs.PreparedAssetPath,
                    _knobs.ReadiedAssetTopLayerTrail,
                    _knobs.ReadiedAssetBaseRecipeVer,
                    _knobs.ReadiedAssetNetRecipeVer,
                    _knobs.ResidencyBudgets,
                    _kineticsBlobStash,
                    _animTelemetry.DumpMotionEnabled,
                    _runtime,
                    _tapRouter,
                    _fxPostures,
                    _actorFxProceed,
                    Lighting,
                    _seeThroughFades,
                    _knobs.NoAudio,
                    Console.WriteLine,
                    Console.Error.WriteLine),
                this).Compose(platformOutcome, hubFeedCam),
            (platformOutcome, hubFeedCam, substanceFxListSound) =>
                new PreferencesDevToolsAssemblyPhase(
                    new PreferencesDevToolsDependencies(
                        _runtimeSettings,
                        new EnginePreferencesStartupTargets(
                            _runtimeSettings.ReadoutPaneMark!,
                            _readoutCyclePacing,
                            hubFeedCam.CameraController,
                            substanceFxListSound.Audio?.Engine))
                    {
                        RenderPacks = _rasterizeBundleRegistry,
                        GpuDev = hubFeedCam.GpuDevice,
                    })
                    .Compose(platformOutcome, hubFeedCam, substanceFxListSound),
            (platformOutcome, substanceFxListSound, prefsDevTools) =>
            {
                const uint startingMiddleLbIdent = 0xA9B4FFFFu;
                RealmRenderResult realmRasterize = new RealmRenderAssemblyPhase(
                    new RealmRenderDependencies(
                        _worldEnvironment,
                        _rasterizeAssetLifespan,
                        _gpuDev!.Retirement,
                        _knobs.ResidencyBudgets,
                        startingMiddleLbIdent,
                        _applicationTrails.Diagnostics,
                        Console.WriteLine,
                        _gpuDev!,
                        _gpuCycleLifespan!),
                    this).Compose(platformOutcome, substanceFxListSound, prefsDevTools);
                Console.WriteLine(
                    $"loading world view centered on " +
                    $"0x{realmRasterize.TerrainBuild.InitialCenterLandblockId:X8}");
                return realmRasterize;
            },
            (platformOutcome, hubFeedCam, substanceFxListSound, prefsDevTools, realmRender) =>
            {
                Action<string>? compositionToast = null;
                return new DealingRetainedWidgetAssemblyPhase(
                    new DealingRetainedWidgetDependencies(
                    _knobs,
                    platformOutcome.Graphics,
                    BuildBackbufferReader(
                        platformOutcome.Graphics,
                        hubFeedCam.GpuDevice),
                    _window!,
                    platformOutcome.Input,
                    realmRender.Foundation.ShadersDirectory,
                    substanceFxListSound.Dats,
                    _datMutex,
                    realmRender.Foundation.TextureCache,
                    realmRender.Foundation.DebugFont,
                    _hubStillness,
                    _keptFeedGrab,
                    hubFeedCam.InputRouter,
                    _ownAvatarWarpDrain,
                    _applicationTrails.KeybindsTrail,
                    _runtimeSettings,
                    _structureDegrades,
                    _runtime,
                    _fightingAssaultOps,
                    _fightingMarkOps,
                    _arcanumCastingOps,
                    substanceFxListSound.ArcanaCatalog,
                    _pileDivideQty,
                    _widgetRegistry,
                    _onlineFightingMannerDirectives,
                    _ownAvatarPersona,
                    _ownAvatarManner,
                    lensPlane => new PickingCameraSource(
                        hubFeedCam.CameraController,
                        _window!,
                        lensPlane),
                    _widgetCycleTelemetry,
                    ExistingVitals: null,
                    compositionToast,
                    ClientTickerInstant,
                    Console.WriteLine,
                    hubFeedCam.GpuDevice,
                    hubFeedCam.GpuFrameLifetime,
                    () => RealmTime.LatestCalendar,
                    prefsDevTools.RenderPacks,
                    _rasterizeBundleTelemetry.SnapTelemetry,
                    _applicationTrails.Screenshots,
                    _automation,
                    GameplayInputFrame: () => _gameplayFeedCycle),
                _canonWidgetTenancy,
                this).Compose(
                    platformOutcome,
                    hubFeedCam,
                    substanceFxListSound,
                    prefsDevTools,
                    realmRender);
            },
            (platformOutcome,
                hubFeedCam,
                substanceFxListSound,
                prefsDevTools,
                realmRender,
                dealingWidget) =>
            {
                Action<string>? compositionToast = null;
                return new OnlineDisplayAssemblyPhase(
                    new OnlineDisplayDependencies(
                    _knobs,
                    platformOutcome.Graphics,
                    _window!,
                    _datMutex,
                    _runtimeSettings,
                    _hubStillness,
                    _kineticsEngine,
                    _kineticsBlobStash,
                    _realmPlayPhase,
                    _realmSignals,
                    _runtime,
                    _onlineActorCoreSocket,
                    _onlineActorLocomotionMappings,
                    _actorFxProceed,
                    _fxPostures,
                    _distantKineticsUpdater,
                    _ownAvatarShade,
                    _animatedEntities,
                    _animTelemetry,
                    _taxonomyStash,
                    _seeThroughFades,
                    _canonAlphaFifo,
                    _chamberVis,
                    _onlineRealmOrigin,
                    _ownAvatarPersona,
                    _pursueCamFeed,
                    _ptrLocus,
                    _avatarApproachCompletions,
                    _rasterizeAssetLifespan,
                    _gatewayTunnelBackup,
                    _tapRouter,
                    _rasterizeProbeTrace,
                    RealmTime,
                    DevWorldEntities: null,
                    DevFrameDiagnostics: null,
                    _widgetCycleTelemetry,
                    Console.WriteLine,
                    compositionToast,
                    _rasterizeBundleTelemetry),
                this).Compose(
                    platformOutcome,
                    hubFeedCam,
                    substanceFxListSound,
                    prefsDevTools,
                    realmRender,
                    dealingWidget);
            },
            (hubFeedCam,
                substanceFxListSound,
                prefsDevTools,
                realmRender,
                dealingWidget,
                onlineExhibit) =>
                new SessionAvatarAssemblyPhase(
                new SessionAvatarDependencies(
                    _knobs,
                    _runtime,
                    _window!,
                    _datMutex,
                    _runtimeSettings,
                    prefsDevTools,
                    _worldEnvironment,
                    _realmTableauDiagPhase,
                    _coreProbeDirectives,
                    _onlineFightingMannerDirectives,
                    _fightingMannerOps,
                    _hubStillness,
                    _kineticsEngine,
                    _kineticsBlobStash,
                    _realmPlayPhase,
                    _realmSignals,
                    _taxonomyStash,
                    _onlineActorCoreSocket,
                    _animatedEntities,
                    _distantTravelObservations,
                    _distantKineticsUpdater,
                    _distantIncomingLocomotion,
                    _incomingActorSignals,
                    _onlineActorLocomotionMappings,
                    _ownAvatarPersona,
                    _avatarHubSocket,
                    _ownAvatarManner,
                    _pursueCamFeed,
                    _ownAvatarOutgoing,
                    _ownAvatarWarpDrain,
                    _onlineRealmOrigin,
                    _rasterizeSpan,
                    _ownAvatarShade,
                    _viewRectAspect,
                    _avatarApproachCompletions,
                    _ptrLocus,
                    _travelFeed,
                    _feedGrab,
                    _moteVis,
                    _seeThroughFades,
                    _fxPostures,
                    _refreshCycleTimer,
                    _travelTruthTelemetry,
                    _fightingAssaultOps,
                    _fightingFeedback,
                    _gatewayTunnelBackup,
                    Console.WriteLine,
                    _automation is null
                        ? null
                        : _automation.TryHndExtensionDirective,
                    _conditionWriter),
                this).Compose(
                    hubFeedCam,
                    substanceFxListSound,
                    prefsDevTools,
                    realmRender,
                    dealingWidget,
                    onlineExhibit),
            (platformOutcome,
                hubFeedCam,
                substanceFxListSound,
                prefsDevTools,
                realmRender,
                dealingWidget,
                onlineExhibit,
                sessAvatar) => new FrameRootAssemblyPhase(
                    new CycleTrunkDeps(
                        _knobs,
                        _runtime,
                        platformOutcome.Graphics,
                        _window!,
                        platformOutcome.Input,
                        RealmTime,
                        Weather,
                        Lighting,
                        _worldEnvironment,
                        _kineticsEngine,
                        _chamberVis,
                        _ownAvatarManner,
                        _ownAvatarPersona,
                        _pursueCamFeed,
                        _onlineRealmOrigin,
                        _moteVis,
                        _fxPostures,
                        _rasterizeSpan,
                        _runtimeSettings,
                        _structureDegrades,
                        _readoutCyclePacing,
                        _realmTableauDiagPhase,
                        _canonAlphaFifo,
                        _cycleProfiler,
                        _cycleDiag,
                        _rasterizeProbeTrace,
                        _diagVmRasterizeFacts,
                        _feedGrab,
                        _camFeed,
                        _animatedEntities,
                        _refreshCycleTimer,
                        _cycleGraphs,
                        Console.WriteLine,
                        _rasterizeBundleTelemetry),
                    this).Compose(
                        platformOutcome,
                        hubFeedCam,
                        substanceFxListSound,
                        prefsDevTools,
                        realmRender,
                        dealingWidget,
                        onlineExhibit,
                        sessAvatar),
            cycleTrunks => new SessionStartAssemblyPhase(
                new SessionBeginDeps(
                    Console.WriteLine))
                .Start(cycleTrunks));
    }

    private void OnUpdate(double dt)
    {
        _rasterizeLoopLoaded = true;
        using var _updJuncture = _cycleProfiler.BeginStage(
            MacAC.Client.Telemetry.CycleJuncture.Update);
        _cycleGraphs.Tick(new MacAC.Client.Pulse.PulseFrameInput(dt));
        if (_knobs.LiveMode)
        {
            _runtimeSettings.AssignGameplayReadout(
                _runtime.CharacterSelection.Snapshot.Lifecycle
                    == SimToonPickLifespan.InWorld);
        }
        _realmSignals.TriggerBeat(dt);
        _rasterizeLoopLoaded = false;
    }

    private void OnRender(double diffSecs)
    {
        _rasterizeLoopLoaded = true;
        if (_nativeShutAsked)
        {
            _rasterizeLoopLoaded = false;
            return;
        }
        Vector2D<int> dims = _window!.Size;
        if (_vulkanVisuals is { } vulkan && !vulkan.ReadyCycle())
        {
            _rasterizeLoopLoaded = false;
            return;
        }
        MacAC.Client.Graphics.RenderFrameVerdict verdict;
        try
        {
            _cycleGraphs.Render(
                new MacAC.Client.Graphics.RasterizeCycleFeed(
                    diffSecs,
                    dims.X,
                    dims.Y)
                {
                    BackbufferWidth = (int)(_vulkanVisuals?.Width ?? 0u),
                    BackbufferHeight = (int)(_vulkanVisuals?.Height ?? 0u),
                },
                out verdict);
        }
        catch (MacAC.Client.Graphics.Gpu.Vulkan.VkSwapchainOutOfDateException)
            when (_vulkanVisuals is not null)
        {
            _vulkanVisuals.ReqRecreate();
            _rasterizeLoopLoaded = false;
            return;
        }

        if (!verdict.SkippedZeroArea)
            _vulkanVisuals?.NoteCycleClosed();
        _rasterizeLoopLoaded = false;
    }

    private void OnFramebufferResize(Silk.NET.Maths.Vector2D<int> newDims)
        => _framebufferRescale.Resize(newDims);

    private void OnClosing() => CompleteShutdown(freeNativePane: false);

    private void CompleteShutdown(bool freeNativePane)
    {
        if (!freeNativePane && !_nativeExecReturned)
        {
            _nativeShutAsked = true;
            return;
        }

        if (!_lifespan.HasShutdownRoots)
        {
            PersistTagMappingsAtShutdown();
            if (_runtime.Session.IsInWorld)
                _conditionWriter.Disconnected(_knobs.SessionId ?? "app", "stopped");
            _lifespan.BroadcastShutdownTrunks(CaptureShutdownRoots());
        }

        GameWindowLifetimeDigest dossier = freeNativePane
            ? _lifespan.ConcludeAndFreeNativePane()
            : _lifespan.TryDone();
        if (dossier.Status == PlayPaneLifespanCondition.Complete)
        {
            if (freeNativePane)
                ReportExited(dossier);
            return;
        }

        Console.Error.WriteLine(
            $"[shutdown] status={dossier.Status}, blocked={dossier.BlockedStage ?? "none"}");
        foreach (AssetShutdownTidyMiss tidy in dossier.CleanupFailures)
        {
            Console.Error.WriteLine(
                $"[shutdown] cleanup '{tidy.Operation}' in '{tidy.Stage}': " +
                tidy.Error);
        }

        if (dossier.Error is not null)
            Console.Error.WriteLine($"[shutdown] {dossier.Error}");

        if (freeNativePane)
            ReportExited(dossier);
    }

    private void PersistTagMappingsAtShutdown()
    {
        if (_tagMappingsPersisted || _feedRouter is null) return;
        _tagMappingsPersisted = true;
        try
        {
            KeyBindingBook latest = _feedRouter.Bindings;
            var profiles = new MacAC.Client.Controls.CanonKeymapProfileStore(
                _applicationTrails.KeybindsTrail);
            CanonKeymapSaveResult stored = profiles.PersistEngaged(latest);
            if (stored.Status != CanonKeymapSaveStatus.Saved)
            {
                Console.WriteLine(
                    $"keymap: shutdown save failed ({stored.Status}): {stored.Error}");
                return;
            }
            latest.StoreToFile(_applicationTrails.KeybindsTrail);
        }
        catch (Exception miss)
        {
            Console.WriteLine($"keymap: shutdown persistence failed: {miss.Message}");
        }
    }

    private void ReportExited(GameWindowLifetimeDigest dossier)
    {
        string sessIdent = _knobs.SessionId ?? "app";
        if (_runFailure is not null)
        {
            _conditionWriter.Exited(sessIdent, 1, "crashed");
            return;
        }

        if (dossier.Status == PlayPaneLifespanCondition.Complete)
            _conditionWriter.Exited(sessIdent, 0, "graceful");
        else
            _conditionWriter.Exited(sessIdent, 1, "shutdown-incomplete");
    }

    private PlayPaneShutdownTrunks CaptureShutdownRoots() => new(
        new IngressShutdownTrunks(
            _hubStillness,
            _onlineFightingMannerDirectives,
            _coreProbeDirectives,
            _keptWidgetGameplayMapping,
            _gameplayFeedActs,
            _camPtrFeed,
            _feedRouter,
            _pointerSrc,
            _kbSrc,
            _canonWidgetTenancy,
            _uiHost,
            _pluginSession,
            _runtime,
            _travelFeed,
            _camFeed,
            _paneHooks),
        new CycleShutdownTrunks(
            _cycleGraphBulletin,
            _cycleTrunkMappings,
            _sessAvatarMappings,
            _dealingWidgetLateMappings),
        new OnlineShutdownRoots(
            _camPtrFeed,
            _canonWidgetTenancy,
            _magicCore,
            _gearDealingDriver,
            _externalVesselLifecycle,
            _streamer,
            _equippedDescendantPainter,
            _onlineActors,
            _runtime,
            _coreHubTenancy,
            _rasterizeTableauShade,
            _onlineExhibitMappings,
            _actorFxProceed,
            _actorFxList,
            _tapRegistrations,
            _onlineActorLamps,
            _onlineActorExhibit,
            _animTapCycles,
            _fxPostures,
            _soundEngine),
        new RasterizeShutdownTrunks(
            _gpuFrameFlights,
            _gpuDev,
            _ownAvatarWarp,
            _gatewayTunnelBackup,
            _paperdollViewRectPainter,
            _beastAppraisalViewRectPainter,
            _chargenPreviewPainter,
            _chargenPreviewDriver,
            _summaryPreviewPainter,
            _summaryPreviewDriver,
            _wbPaintRouter,
            _environChamberPainter,
            _portalDepthMask,
            _clipCycle,
            _heavensPainter,
            _motePainter,
            _textureStash,
            _wbTriMeshBridge,
            _land,
            _tableauIlluminationUbo,
            _diagStrokes,
            _phrasePainter,
            _diagTypeface,
            _readoutCyclePacing,
            _cycleProfiler,
            _rasterizeAssetLifespan,
            _constructionCleanup),
        new PlatformShutdownTrunks(
            _datFiles,
            _preparedAssets,
            _input,
            _graphics));
    private void OnFocusChanged(bool focused)
        => _camPtrFeed?.ProcessFocusAltered(focused);

    public void Dispose()
    {
        CompleteShutdown(freeNativePane: true);
        _window = null;
    }

}
