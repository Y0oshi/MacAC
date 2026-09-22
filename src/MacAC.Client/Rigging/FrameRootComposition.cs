using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Preferences;
using MacAC.Client.Pulse;
using MacAC.Client.Realm;
using MacAC.Client.SimBridge;
using MacAC.Client.Telemetry;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;
using MacAC.Sim;
using MacAC.Sim.Presence;
using MacAC.Wire.Messages;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace MacAC.Client.Rigging;

internal sealed record CycleTrunkDeps(
    EngineKnobs Options,
    SimCore Runtime,
    PlayPaneVisuals Graphics,
    IWindow Window,
    IInputContext Input,
    WorldClock WorldTime,
    WeatherEngine Weather,
    LightKeeper Lighting,
    RealmEnvironmentDriver WorldEnvironment,
    KineticEngine KineticEngine,
    ChamberVis CellVisibility,
    AvatarModeLedger PlayerMode,
    AvatarIdentityLedger PlayerIdentity,
    FollowCameraInputLedger ChaseCameraInput,
    OnlineRealmOriginLedger WorldOrigin,
    MoteVisibilityDriver ParticleVisibility,
    ActorEffectPoseRegistry EffectPoses,
    RealmRenderRangeLedger RenderRange,
    EnginePreferencesDriver Settings,
    BuildingDegradeDriver BuildingDegrades,
    DisplayFramePacingDriver DisplayFramePacing,
    RealmTableauDiagPhase WorldSceneDebugState,
    CanonAlphaFifo RetailAlphaQueue,
    CycleProfiler FrameProfiler,
    bool FrameDiagnosticsEnabled,
    IRenderFrameTelemetryLog RenderDiagnosticLog,
    DebugVmRenderFactsHerald DebugVmRenderFacts,
    IFeedGrabOrigin InputCapture,
    RouterCameraInputSource CameraInput,
    OnlineActorMotionEngineView<OnlineActorMotionLedger> Animations,
    PulseFrameClock UpdateClock,
    PlayCycleGraphSocket FrameGraphs,
    Action<string> Log,
    MacAC.Client.Graphics.Packs.DeferredRenderPackTelemetrySource?
        RenderPackDiagnostics = null)
{
    public SimAvatarLocomotionLedger AvatarDriver =>
        Runtime.MovementOwner;

    public PickPhase Selection => Runtime.ActHolder.Selection;

    public FightingPhase Combat => Runtime.ActHolder.Combat;
}

internal sealed record CycleTrunkOutcome(
    PulseFrameConductor Update,
    RenderFrameConductor Render,
    FrameRootEngineWiring RuntimeBindings,
    IDisposable FrameGraphPublication,
    OnlineSessionHarbor SessionHost,
    CurrentGameEngineBridge SimCore);

internal interface IPlayPaneCycleTrunkBulletin
{
    void PublishFrameRoots(CycleTrunkOutcome outcome);
}

internal enum FrameRootAssemblyPoint
{
    RenderResourcesCreated,
    WorldRendererCreated,
    LifecycleAutomationBound,
    RenderRootCreated,
    UpdateRootCreated,
    FrameGraphPublished,
    ResultPublished,
}

internal sealed class FrameRootEngineWiring : IDisposable
{
    private readonly List<(string Name, IDisposable Binding)> _bindings = [];
    private bool _deactivationBegun;

    public void Adopt(string label, IDisposable mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(mapping);
        ObjectDisposedException.ThrowIf(_deactivationBegun, this);
        _bindings.Add((label, mapping));
    }

    public void Dispose()
    {
        if (_deactivationBegun && _bindings.Count == 0)
            return;
        _deactivationBegun = true;

        List<Exception>? misses = null;
        for (int idx = _bindings.Count - 1; idx >= 0; idx--)
        {
            (string label, IDisposable mapping) = _bindings[idx];
            try
            {
                mapping.Dispose();
                _bindings.RemoveAt(idx);
            }
            catch (Exception miss)
            {
                (misses ??= []).Add(new InvalidOperationException(
                    $"Frame-root binding '{label}' didn't detach",
                    miss));
            }
        }

        if (misses is not null)
        {
            throw new AggregateException(
                "Frame-root binding cleanup remains incomplete",
                misses);
        }
    }
}

internal sealed class FrameRootAssemblyPhase(
    CycleTrunkDeps dependencies,
    IPlayPaneCycleTrunkBulletin publication,
    Action<FrameRootAssemblyPoint>? flawInjection = null)
        : IFrameRootAssemblyPhase<
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext>,
        HubFeedCameraOutcome,
        SubstanceFxListSoundOutcome,
        PreferencesDevToolsResult,
        RealmRenderResult,
        DealingRetainedWidgetResult,
        OnlineDisplayResult,
        SessionAvatarResult,
        CycleTrunkOutcome>
{
    private readonly CycleTrunkDeps _deps = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));
    private readonly IPlayPaneCycleTrunkBulletin _bulletin = publication
            ?? throw new ArgumentNullException(nameof(publication));
    private readonly Action<FrameRootAssemblyPoint>? _flawInjection = flawInjection;

    public CycleTrunkOutcome Compose(
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform,
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        PreferencesDevToolsResult prefs,
        RealmRenderResult realm,
        DealingRetainedWidgetResult dealing,
        OnlineDisplayResult online,
        SessionAvatarResult sess)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(dealing);
        ArgumentNullException.ThrowIfNull(online);
        ArgumentNullException.ThrowIfNull(sess);
        if (!ReferenceEquals(_deps.Graphics, platform.Graphics)
            || !ReferenceEquals(_deps.Input, platform.Input))
        {
            throw new InvalidOperationException(
                "Frame-root dependencies do not match the ordered platform result");
        }

        var ambit = new AssemblyAcquisitionScope();
        FrameRootEngineWiring? mappings = null;
        bool mappingsPossessedByAmbit = false;
        try
        {
            CycleTrunkOutcome outcome = ComposeCore(
                hub,
                substance,
                prefs,
                realm,
                dealing,
                online,
                sess,
                ambit,
                ref mappings,
                ref mappingsPossessedByAmbit);
            ambit.Complete();
            return outcome;
        }
        catch (Exception miss)
        {
            if (mappings is not null && !mappingsPossessedByAmbit)
            {
                ambit.Own(
                    "frame-root runtime bindings",
                    mappings,
                    static val => val.Dispose());
            }

            ambit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }

    private CycleTrunkOutcome ComposeCore(
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        PreferencesDevToolsResult prefs,
        RealmRenderResult realm,
        DealingRetainedWidgetResult dealing,
        OnlineDisplayResult online,
        SessionAvatarResult sess,
        AssemblyAcquisitionScope ambit,
        ref FrameRootEngineWiring? mappings,
        ref bool mappingsPossessedByAmbit)
    {
        CycleTrunkDeps dependencies = _deps;
        mappings = new FrameRootEngineWiring();
        RealmRenderFoundation foundation = realm.Foundation;
        var rasterizeSigninPhase = new RenderSignInStateSource(
            dependencies.Options.LiveMode,
            dependencies.PlayerMode);
        var warpRasterizePhase =
            new AvatarWarpRenderStateSource(
                sess.LocalTeleport,
                rasterizeSigninPhase);
        var vulkanWipe = new MacAC.Client.Graphics.Gpu.Vulkan.VkBackbufferClearLedger();
        var rasterizeCycleOnlinePrep =
            new EngineRenderFrameOnlinePreparation(
                foundation.TextureCache,
                foundation.MeshAdapter,
                sess.WorldReveal,
                warpRasterizePhase,
                rasterizeSigninPhase,
                new OnlineSignInRevealCellSource(
                    online.LiveEntities,
                    dependencies.PlayerIdentity),
                online.ParticleRenderer,
                dependencies.FrameProfiler,
                dependencies.FrameDiagnosticsEnabled);
        IRasterizeCycleWipeStage wipeStage =
            new MacAC.Client.Graphics.Gpu.Vulkan.VkRenderFrameClearPhase(
                dependencies.WorldTime,
                dependencies.Weather,
                warpRasterizePhase,
                dependencies.ParticleVisibility,
                vulkanWipe);
        var rasterizeCycleAssetList = new RenderFrameResourceDriver(
            hub.FrameSlots,
            new EngineRenderFrameBeginResources(
                foundation.TextureCache,
                online.DrawDispatcher,
                online.EnvCellRenderer,
                online.PortalDepthMask,
                online.ClipFrame,
                foundation.Terrain,
                foundation.SceneLighting),
            wipeStage,
            rasterizeCycleOnlinePrep);
        Flaw(FrameRootAssemblyPoint.RenderResourcesCreated);

        MacAC.Client.Graphics.Packs.RenderPackDriver? rasterizeBundleDriver = null;
        MacAC.Client.Graphics.Packs.RenderPackPickingWiring? rasterizeBundlePick = null;
        MacAC.Client.Graphics.Packs.AtmosphericFrameInputLedger? atmosphericFeeds = null;
        if (prefs.RenderPacks is { } rasterizeBundleRegistry)
        {
            rasterizeBundleDriver = new MacAC.Client.Graphics.Packs.RenderPackDriver(
                rasterizeBundleRegistry.Freeze,
                new MacAC.Client.Graphics.Packs.AtmosphericRenderPackEngineMint(
                    hub.GpuDevice,
                    dependencies.Options.SkyAnimationPhaseSeconds),
                new MacAC.Client.Graphics.Packs.RenderPackReceiverPipelineMarshal(
                    foundation.Terrain!,
                    online.DrawDispatcher!),
                MacAC.Client.Graphics.Packs.ThreadPoolRenderPackPreparationRota.Instance,
                rasterizeBundleRegistry);
            mappings.Adopt("render-pack controller", rasterizeBundleDriver);
            rasterizeBundlePick = new MacAC.Client.Graphics.Packs.RenderPackPickingWiring(
                dependencies.Settings,
                rasterizeBundleDriver,
                dependencies.Log);
            mappings.Adopt("render-pack selection", rasterizeBundlePick);
            if (dependencies.RenderPackDiagnostics is { } rasterizeBundleTelemetry)
            {
                mappings.Adopt(
                    "render-pack diagnostics",
                    rasterizeBundleTelemetry.BindOwned(rasterizeBundleDriver));
            }
            atmosphericFeeds = new MacAC.Client.Graphics.Packs.AtmosphericFrameInputLedger();

            if (online.SkyRenderer is { } heavensForNightHeavens)
            {
                var nightHeavensDriver = rasterizeBundleDriver;
                heavensForNightHeavens.EnhancedNightSkyActive = () =>
                    nightHeavensDriver.ActiveRuntime?.Descriptor.Id
                        == MacAC.Client.Graphics.Packs.BuiltInAtmosphericRasterizeBundle.Id;
            }
        }

        var rasterizeWeatherCycle = new RenderWeatherFrameDriver(
            dependencies.WorldTime,
            dependencies.Weather);
        var heavensPesCycle = new HeavensPesFrameDriver(
            substance.ScriptRunner,
            substance.ParticleSink,
            dependencies.EffectPoses,
            online.EntityEffects,
            dependencies.Log,
            substance.Audio is { } sound
                ? sound.Engine.HaltAllForHolder
                : null);
        IRealmStageFramePhase? realmTableauPainter = null;
        CurrentRenderStageOracle? latestRasterizeTableauOracle = null;
        RenderStageShadeComparisonDriver? rasterizeTableauShadeComparison = null;
        {
            RealmRenderTelemetry realmRasterizeTelemetry =
                new(dependencies.RenderDiagnosticLog);
            IRenderFrameGlLedger realmCycleGlPhase = NullRenderFrameGlLedger.Instance;
            IRealmPassScope? realmPassAmbit = dependencies.Graphics.RealmPassAmbit;
            IRealmPassSurface realmPassCanvas = new RhiRealmPassSurface(
                realmPassAmbit
                    ?? throw new InvalidOperationException(
                        "The graphics backend must publish a world pass scope"),
                hub.GpuFrameLifetime,
                online.ClipFrame);
            var realmCycleSurroundings =
                new EngineRealmFrameEnvironmentPreparation(
                    dependencies.Options,
                    dependencies.WorldTime,
                    dependencies.Lighting,
                    online.DrawDispatcher!,
                    online.EnvCellRenderer!,
                    foundation.SceneLighting!,
                    dependencies.RenderRange,
                    heavensPesCycle,
                    persistentDaylight: () =>
                        dependencies.Runtime.ToonHolder.Options.GetOptionBit(
                            CharacterOptionId.PersistentAtDay));
            var realmCycleCam = new EngineRealmFrameCameraSource(
                hub.CameraController,
                sess.LocalTeleport.ApplyViewPlane);
            var realmCycleTrunks = new EngineRealmFrameRootSource(
                dependencies.KineticEngine,
                dependencies.CellVisibility,
                dependencies.PlayerMode,
                dependencies.ChaseCameraInput,
                dependencies.AvatarDriver,
                dependencies.WorldOrigin);
            var heavensPesActivationLatch = new EngineHeavensPesActivationTurnstile(
                heavensPesCycle,
                realmCycleCam,
                realmCycleTrunks);
            mappings.Adopt(
                "sky presentation effects activation",
                sess.LiveObjectFrame.AttachHeavensPesActivationLatchPossessed(
                    heavensPesActivationLatch));
            var realmRasterizeCycleBuilder = new RealmRenderFrameAssembler(
                realmCycleCam,
                new EngineRealmFrameVisibilityPreparation(
                    online.SelectionScene,
                    dependencies.ParticleVisibility,
                    foundation.Terrain!,
                    sess.WorldReveal,
                    online.EnvCellFrustum),
                new EngineRealmFramePreferencesPreview(
                    dependencies.Settings,
                    substance.Audio?.Engine,
                    hub.CameraController,
                    dependencies.DisplayFramePacing),
                realmCycleTrunks,
                realmCycleSurroundings,
                new EngineRealmFrameAnimatedActorSource(
                    dependencies.Animations,
                    online.StaticAnimationScheduler,
                    online.EquippedChildren),
                new EngineRealmFrameBuildingSource(
                    online.LandblockPipeline,
                    dependencies.CellVisibility),
                new EngineDirectionalShadeCellMembership(
                    dependencies.KineticEngine.ShadeObjects));
            var landPaintTelemetry = new LandscapeDrawTelemetryDriver(
                dependencies.FrameDiagnosticsEnabled,
                realmRasterizeTelemetry,
                new EngineFramePipelineTelemetryFactsSource(
                    foundation.Terrain!,
                    online.LandblockPipeline,
                    rasterizeCycleOnlinePrep,
                    online.DrawDispatcher!,
                    sess.Streaming,
                    online.LiveEntities,
                    online.WorldState),
                dependencies.RenderDiagnosticLog);
            var canonPLensChambers = new CanonPViewCellSource(dependencies.CellVisibility);
            var canonPLensPassExecutor = new CanonPLensSweepRunner(
                realmPassCanvas,
                realmCycleGlPhase,
                online.ClipFrame,
                foundation.Terrain,
                online.EnvCellRenderer!,
                online.DrawDispatcher!,
                online.SkyRenderer,
                substance.ParticleSystem,
                online.ParticleRenderer,
                online.PortalDepthMask,
                dependencies.RetailAlphaQueue,
                landPaintTelemetry);
            var realmTableauTelemetry = new RealmStageTelemetryDriver(
                new EngineRealmStagePViewTelemetrySource(
                    dependencies.AvatarDriver,
                    dependencies.KineticEngine,
                    dependencies.CellVisibility),
                dependencies.WorldSceneDebugState,
                null,
                dependencies.KineticEngine,
                dependencies.PlayerMode,
                dependencies.AvatarDriver,
                dependencies.DebugVmRenderFacts,
                diagVmConsumerEngaged: false);
            var realmTableauPasss = new RealmStagePassRunner(
                realmPassCanvas,
                realmCycleGlPhase,
                online.ClipFrame,
                online.DrawDispatcher!,
                online.EnvCellRenderer!,
                foundation.Terrain,
                landPaintTelemetry,
                online.SkyRenderer,
                substance.ParticleSystem,
                online.ParticleRenderer);
            online.DrawDispatcher!.AssignLatestRasterizeTableauWatcher(null);
            online.SelectionScene.AssignLatestRasterizeTableauWatcher(null);
            realmTableauPainter = new RealmStagePainter(
                rasterizeCycleAssetList,
                rasterizeSigninPhase,
                dependencies.WorldEnvironment,
                realmRasterizeCycleBuilder,
                new EngineRealmStageActorSource(online.WorldState),
                online.SelectionScene,
                dependencies.RetailAlphaQueue,
                dependencies.ParticleVisibility,
                new RealmStagePViewPainter(
                    new CanonPViewPainter(
                        online.RenderSceneShadow
                            ?? throw new InvalidOperationException(
                                "The retail frame walk needs the retained render scene"),
                        online.LandblockPipeline.RasterizePublisher?.StrollBuildings
                            ?? throw new InvalidOperationException(
                                "The retail frame walk needs the building registry"),
                        online.LandblockPipeline.RasterizePublisher?.StrideScenery
                            ?? throw new InvalidOperationException(
                                "The retail frame walk needs the landscape registry"),
                        dependencies.CellVisibility,
                        dependencies.KineticEngine.ShadeObjects,
                        dependencies.BuildingDegrades),
                    canonPLensPassExecutor),
                canonPLensChambers,
                realmTableauPasss,
                dependencies.RenderRange,
                realmTableauTelemetry,
                online.WorldAvailability,
                atmosphericFeeds);
            realmTableauPainter =
                new MacAC.Client.Graphics.Gpu.Vulkan.VulkanRealmTableauStage(
                    hub.GpuFrameLifetime,
                    vulkanWipe,
                    () => dependencies.Graphics.Vulkan?.SampleCount ?? 1,
                    (dependencies.Graphics as VkGameWindowGraphics)?.RealmPassAmbitCore
                        ?? throw new InvalidOperationException(
                            "The Vulkan world phase needs the Vulkan graphics handle"),
                    realmTableauPainter,
                    rasterizeBundleDriver,
                    atmosphericFeeds,
                    rasterizeBundlePick is null
                        ? null
                        : rasterizeBundlePick.ImposeAtCycleBoundary,
                    online.RenderSceneShadow,
                    online.DrawDispatcher,
                    foundation.Terrain);
        }
        Flaw(FrameRootAssemblyPoint.WorldRendererCreated);
        RealmLifespanAutopilotDriver? lifecycleAutomation = null;
        if (dealing.RetainedUi?.Screenshots is { } screenshots
            && dependencies.Options.AutomationArtifactDirectory is { } artifactFolder)
        {
            MacAC.Cockpit.Panels.Settings.RenderPackPick?
                automationPreviousEnhancedPick = null;

            (bool Succeeded, string Error) PersistAutomationRasterizeBundlePick(
                MacAC.Cockpit.Panels.Settings.RenderPackPick pick)
            {
                dependencies.Settings.StoreReadout(dependencies.Settings.Readout with
                {
                    RenderPack = pick,
                });
                return dependencies.Settings.Readout.RenderPack == pick
                    ? (true, string.Empty)
                    : (false, $"render-pack selection '{pick.PresetId}' was not persisted");
            }

            (bool Succeeded, string Error) PickAutomationRasterizeBundle(string preset)
            {
                var selection = string.Equals(
                    preset,
                    "retail",
                    StringComparison.Ordinal)
                    ? MacAC.Cockpit.Panels.Settings
                        .RenderPackPick.Retail
                    : new MacAC.Cockpit.Panels.Settings
                        .RenderPackPick(
                            MacAC.Client.Graphics.Packs
                                .BuiltInAtmosphericRasterizeBundle.Id,
                            "1.0.0",
                            preset);
                return PersistAutomationRasterizeBundlePick(selection);
            }

            (bool Succeeded, string Error) DeactivateAutomationRasterizeBundle()
            {
                var latest = dependencies.Settings.Readout.RenderPack;
                if (!latest.IsCanon)
                    automationPreviousEnhancedPick = latest;
                return PersistAutomationRasterizeBundlePick(
                    MacAC.Cockpit.Panels.Settings
                        .RenderPackPick.Retail);
            }

            (bool Succeeded, string Error) ReenableAutomationRasterizeBundle()
            {
                return automationPreviousEnhancedPick is { } selection
                    ? PersistAutomationRasterizeBundlePick(selection)
                    : (false, "render-pack re-enable requires a prior enhanced selection");
            }

            var assetCaptures =
                new RealmLifespanResourceCaptureSource(
                    online.WorldState,
                    dependencies.Animations,
                    online.FrameDiagnostics,
                    online.LiveEntities,
                    sess.Streaming,
                    substance.ParticleSystem,
                    substance.ParticleSink,
                    online.EntityEffects,
                    online.Lights,
                    substance.ScriptRunner,
                    foundation.MeshAdapter,
                    foundation.TextureCache,
                    online.DrawDispatcher,
                    dependencies.FrameProfiler,
                    substance.Dats,
                    foundation.Residency,
                    dependencies.KineticEngine.DataCache
                        ?? throw new InvalidOperationException(
                            "Lifecycle automation needs the canonical physics cache"),
                    latestRasterizeTableauOracle,
                    rasterizeTableauShadeComparison);
            lifecycleAutomation =
                new RealmLifespanAutopilotDriver(
                    () => sess.WorldReveal.Snapshot,
                    () => dependencies.WorldEnvironment.Runtime.Ownership,
                    () => online.WorldTransit.Ownership,
                    () => sess.WorldReveal.GatewayMaterializationTally,
                    assetCaptures.Capture,
                    screenshots,
                    artifactFolder,
                    msg => dependencies.Log("[UI-PROBE] " + msg),
                    () => rasterizeBundleDriver?.FloorPerformanceSpecimenTally ?? 0,
                    () =>
                    {
                        if (rasterizeBundleDriver is null)
                        {
                            return (
                                false,
                                "render-pack performance automation is unavailable");
                        }
                        bool restart = rasterizeBundleDriver.TryRestartPerformanceEvidence(
                            out string problem);
                        return (restart, problem);
                    },
                    () => rasterizeBundleDriver?.Capture.State ==
                        MacAC.Client.Graphics.Packs.RenderPackActivationPhase.FailedToRetail,
                    fetchRasterizeBundleCondition: () =>
                    {
                        MacAC.Client.Graphics.Packs.RenderPackActivationCapture capture =
                            rasterizeBundleDriver?.Capture
                            ?? new MacAC.Client.Graphics.Packs.RenderPackActivationCapture(
                                MacAC.Client.Graphics.Packs.RenderPackActivationPhase.Retail,
                                MacAC.Cockpit.Panels.Settings
                                    .RenderPackPick.Retail,
                                ActivePackDisplayName: null,
                                Reason: null,
                                ActivationGeneration: 0);
                        var phase = capture.State switch
                        {
                            MacAC.Client.Graphics.Packs.RenderPackActivationPhase.Retail =>
                                MacAC.Client.Shell.Testing
                                    .CanonWidgetAutopilotRenderPackPhase.Retail,
                            MacAC.Client.Graphics.Packs.RenderPackActivationPhase.CandidatePending =>
                                MacAC.Client.Shell.Testing
                                    .CanonWidgetAutopilotRenderPackPhase.CandidatePending,
                            MacAC.Client.Graphics.Packs.RenderPackActivationPhase.Active =>
                                MacAC.Client.Shell.Testing
                                    .CanonWidgetAutopilotRenderPackPhase.Active,
                            MacAC.Client.Graphics.Packs.RenderPackActivationPhase.FailedToRetail =>
                                MacAC.Client.Shell.Testing
                                    .CanonWidgetAutopilotRenderPackPhase.FailedToRetail,
                            _ => throw new ArgumentOutOfRangeException(),
                        };
                        return new MacAC.Client.Shell.Testing
                            .CanonWidgetAutopilotRenderPackStatus(
                                phase,
                                capture.Selection.PackId,
                                capture.Selection.PresetId,
                                capture.ActivationGeneration,
                                capture.Reason);
                    },
                    pickRasterizeBundle: PickAutomationRasterizeBundle,
                    deactivateRasterizeBundle: DeactivateAutomationRasterizeBundle,
                    reenableRasterizeBundle: ReenableAutomationRasterizeBundle,
                    fetchFramebufferDims: () =>
                    {
                        var dims = dependencies.Window.FramebufferSize;
                        return (dims.X, dims.Y);
                    },
                    rescaleFramebuffer: (width, height) =>
                    {
                        if (dependencies.Settings.Readout.Fullscreen)
                        {
                            return (
                                false,
                                "automation framebuffer resize requires windowed mode");
                        }
                        string resolution = $"{width}x{height}";
                        dependencies.Settings.StoreReadout(dependencies.Settings.Readout with
                        {
                            Resolution = resolution,
                        });
                        return string.Equals(
                            dependencies.Settings.Readout.Resolution,
                            resolution,
                            StringComparison.Ordinal)
                            ? (true, string.Empty)
                            : (false, $"framebuffer resize '{resolution}' was not persisted");
                    },
                    reqClientShut: dependencies.Window.Close);
            mappings.Adopt(
                "world lifecycle automation owner",
                lifecycleAutomation);
            mappings.Adopt(
                "world lifecycle automation binding",
                dealing.LateBindings.Automation.Bind(
                    lifecycleAutomation));
        }
        else if (dealing.RetainedUi is not null)
        {
            mappings.Adopt(
                "world reveal facts automation binding",
                dealing.LateBindings.Automation.Bind(
                    new RealmRevealFactsAutopilotEngine(
                        () => sess.WorldReveal.Snapshot,
                        () => sess.WorldReveal.GatewayMaterializationTally)));
        }
        Flaw(FrameRootAssemblyPoint.LifecycleAutomationBound);

        IRetainedGameplayWidgetFrame? keptGameplayWidget =
            dependencies.Options.RetailUi && dealing.RetainedUi is { } kept
                ? new RetainedGameplayWidgetFrame(kept.Runtime, dependencies.Input)
                : null;
        IPrivateCycleScreenshot? privateScreenshot =
            dealing.RetainedUi?.Screenshots is { } cycleScreenshots
                ? new PrivateCycleScreenshot(cycleScreenshots)
                : null;
        var privateExhibit = new PrivateDisplayPainter(
            new AvatarPortalViewport(
                sess.LocalTeleport,
                hub.CameraController),
            rasterizeCycleAssetList,
            new PrivateActorViewportFrameGroup(
                online.PaperdollPresenter,
                online.CreatureAppraisalPresenter,
                online.ChargenPreviewController,
                online.SummaryPreviewController),
            keptGameplayWidget,
            devTools: null);
        var cyclePrep = new RenderFramePreparationDriver(
            rasterizeCycleAssetList,
            devTools: null,
            rasterizeWeatherCycle,
            online.PaperdollPresenter);
        IRenderFramePostTelemetryPhase postTelemetry =
            rasterizeTableauShadeComparison is not null
            && lifecycleAutomation is not null
                ? new SerialRenderFramePostTelemetryPhase(
                    rasterizeTableauShadeComparison,
                    lifecycleAutomation)
                : (IRenderFramePostTelemetryPhase?)lifecycleAutomation
                    ?? NullRenderFramePostTelemetryPhase.Instance;
        if (RenderDisplayTelemetry.SensorSigninCycles)
        {
            var signinCycleSensor = new SignInDisplayFrameProbe(
                () => sess.LocalTeleport.IsPortalViewRectVisible,
                rasterizeSigninPhase,
                dependencies.Log);
            postTelemetry =
                postTelemetry is NullRenderFramePostTelemetryPhase
                    ? signinCycleSensor
                    : new SerialRenderFramePostTelemetryPhase(
                        postTelemetry,
                        signinCycleSensor);
        }
        var rasterizeCycle = new RenderFrameConductor(
            hub.GpuFrameLifetime,
            dependencies.Graphics.Vulkan is { } vulkanVisuals
                ? new MacAC.Client.Graphics.Gpu.Vulkan.VkFrameGpuMeasurement(
                    dependencies.FrameProfiler,
                    vulkanVisuals.Device)
                : MacAC.Client.Graphics.Gpu.Vulkan.NullRasterizeCycleGpuReading.Instance,
            cyclePrep,
            realmTableauPainter,
            privateExhibit,
            online.FrameDiagnostics,
            postTelemetry,
            NullRasterizeCycleMissRecovery.Instance,
            dependencies.BuildingDegrades,
            privateScreenshot);
        Flaw(FrameRootAssemblyPoint.RenderRootCreated);

        var onlineCycleCoordinator = new CanonOnlineFrameMarshal(
            sess.LiveObjectFrame,
            online.WorldState,
            sess.SessionHost,
            sess.LocalPlayerFrame,
            sess.LiveSpatialReconciler,
            online.WorldAvailability,
            online.RenderSceneShadow?.OnlineProjections,
            sess.PlacementProjectionRetry);
        var camCycle = new CameraFrameDriver(
            hub.CameraController,
            dependencies.InputCapture,
            dependencies.CameraInput,
            sess.LocalPlayerFrameRuntime,
            dependencies.ChaseCameraInput,
            sess.LocalPlayerFrame,
            sess.LiveSpatialReconciler,
            new MacAC.Client.Fighting.FightingCameraTargetSource(
                new MacAC.Client.Fighting.ToonKnobFightingPreferencesSource(
                    dependencies.Runtime.ToonHolder.Options),
                dependencies.Combat,
                dependencies.Selection,
                online.SelectionQuery));
        var refreshCycle = new PulseFrameConductor(
            new OnlineActorTeardownFramePhase(online.LiveEntities),
            new ConsolePulseFrameFailureSink(),
            dependencies.UpdateClock,
            new KineticsScriptClockHerald(substance.ScriptRunner),
            sess.StreamingFrame,
            sess.GameplayInput,
            onlineCycleCoordinator,
            new OnlineActorOnlinenessFramePhase(
                sess.Liveness,
                new StopwatchClientMonotonicMomentOrigin()),
            sess.LocalTeleport,
            new AvatarModeAutoEntryFramePhase(sess.PlayerModeAutoEntry),
            camCycle,
            new RenderStagePulseCommitPhase(online.RenderSceneShadow),
            online.WorldAvailability);
        Flaw(FrameRootAssemblyPoint.UpdateRootCreated);

        var mappingsTenancy = ambit.Own(
            "frame-root runtime bindings",
            mappings,
            static val => val.Dispose());
        mappingsPossessedByAmbit = true;
        IDisposable cycleGraphBulletin = dependencies.FrameGraphs.PublishOwned(
            refreshCycle,
            rasterizeCycle);
        var graphTenancy = ambit.Own(
            "game frame graph publication",
            cycleGraphBulletin,
            static val => val.Dispose());
        Flaw(FrameRootAssemblyPoint.FrameGraphPublished);

        var outcome = new CycleTrunkOutcome(
            refreshCycle,
            rasterizeCycle,
            mappings,
            cycleGraphBulletin,
            sess.SessionHost,
            sess.SimCore);
        _bulletin.PublishFrameRoots(outcome);
        graphTenancy.Transfer();
        mappingsTenancy.Transfer();
        Flaw(FrameRootAssemblyPoint.ResultPublished);
        return outcome;
    }

    private void Flaw(FrameRootAssemblyPoint pt) =>
        _flawInjection?.Invoke(pt);
}
