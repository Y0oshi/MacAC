using MacAC.Assets;
using MacAC.Client.Arcana;
using MacAC.Client.Controls;
using MacAC.Client.Fighting;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Heavens;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Client.Rigging;
using MacAC.Client.Shell;
using MacAC.Client.Sound;
using MacAC.Client.Telemetry;
using MacAC.Cockpit.Input;
using MacAC.Sim;
using Silk.NET.Input;

namespace MacAC.Client.Graphics;

internal enum PlayPaneLifespanCondition
{
    Active,
    RetryableIncomplete,
    Complete,
    CompleteWithCleanupFailures,
    CompleteWithDeferredNativeRelease,
    AbandonedIncomplete,
}

internal sealed record GameWindowLifetimeDigest(
    PlayPaneLifespanCondition Status,
    string? BlockedStage,
    IReadOnlyList<AssetShutdownTidyMiss> CleanupFailures,
    Exception? Error)
{
    public bool IsTerminal
    {
        get
        {
            return Status is
        PlayPaneLifespanCondition.Complete
        or PlayPaneLifespanCondition.CompleteWithCleanupFailures
        or PlayPaneLifespanCondition.CompleteWithDeferredNativeRelease
        or PlayPaneLifespanCondition.AbandonedIncomplete;
        }
    }
}

internal sealed record IngressShutdownTrunks(
    HostQuiescenceTurnstile HostQuiescence,
    OnlineFightingModeDirectiveSlot CombatCommands,
    EngineTelemetryDirectiveSlot DiagnosticCommands,
    RetainedWidgetGameplayWiring? RetainedGameplay,
    GameplayFeedActRouter? GameplayActions,
    CameraPointerInputDriver? CameraPointer,
    InputRouter? Dispatcher,
    SilkPointerOrigin? MouseSource,
    SilkKeyboardOrigin? KeyboardSource,
    CanonWidgetEngineLease RetailUi,
    WidgetHub? RetainedUiHost,
    IDisposable? Plugins,
    SimCore Runtime,
    RouterLocomotionInputSource LocomotionInput,
    RouterCameraInputSource CameraInput,
    SilkWindowCallbackWiring? WindowCallbacks);

internal sealed record CycleShutdownTrunks(
    IDisposable? FrameGraphPublication,
    FrameRootEngineWiring? FrameBindings,
    SessionAvatarEngineWiring? SessionBindings,
    DealingWidgetLateWiring? InteractionBindings);

internal sealed record OnlineShutdownRoots(
    CameraPointerInputDriver? CameraPointer,
    CanonWidgetEngineLease RetailUi,
    ArcanaEngine? Magic,
    GearDealingDriver? ItemInteraction,
    ExternalContainerLifespanDriver? ExternalContainers,
    LandblockStreamer? Streamer,
    EquippedChildRenderDriver? EquippedChildren,
    OnlineActorCore? LiveEntities,
    SimCore Runtime,
    IDisposable RuntimeHostLease,
    RenderStageShadeEngine? RenderSceneShadow,
    OnlineDisplayEngineWiring? PresentationBindings,
    DeferredActorEffectAdvanceSource EffectAdvance,
    ActorEffectDriver? EntityEffects,
    MotionHookRegistrationSet? HookRegistrations,
    OnlineActorLightDriver? LiveLights,
    OnlineActorDisplayDriver? LivePresentation,
    MotionHookFrameQueue? AnimationHookFrames,
    ActorEffectPoseRegistry EffectPoses,
    OpenAlSoundEngine? Audio);

internal sealed record RasterizeShutdownTrunks(
    GpuCycleFlightDriver? FrameFlights,
    IClientGpuDevice? GpuDevice,
    AvatarWarpDriver? LocalTeleport,
    TransferableAssetSocket<PortalTunnelDisplay> PortalTunnelFallback,
    EffigyViewportPainter? Paperdoll,
    CreatureAssayViewportPainter? CreatureAppraisal,
    ChargenPreviewPainter? ChargenPreview,
    ChargenPreviewDriver? ChargenPreviewController,
    ChargenPreviewPainter? SummaryPreview,
    ChargenPreviewDriver? SummaryPreviewController,
    RealmPaintRouter? DrawDispatcher,
    EnvironChamberPainter? EnvironmentCells,
    GatewayZDepthBitmaskPainter? PortalDepthMask,
    ClipCycle? ClipFrame,
    HeavensPainter? Sky,
    MotePainter? Particles,
    BitmapStash? Textures,
    RealmTriMeshBridge? MeshAdapter,
    LandModernPainter? Terrain,
    StageLightingUboWiring? SceneLighting,
    DiagStrokePainter? DebugLines,
    PhrasePainter? TextRenderer,
    BitmapFont? DebugFont,
    DisplayFramePacingDriver FramePacing,
    CycleProfiler FrameProfiler,
    PlayRasterizeAssetLifespan DedicatedResources,
    AssetConstructionTidyRegister ConstructionCleanup);

internal sealed record PlatformShutdownTrunks(
    IDatAccess? Dats,
    IBakedAssetSource? PreparedAssets,
    IInputContext? Input,
    PlayPaneVisuals? Graphics);

internal sealed record PlayPaneShutdownTrunks(
    IngressShutdownTrunks Ingress,
    CycleShutdownTrunks Frame,
    OnlineShutdownRoots Live,
    RasterizeShutdownTrunks Render,
    PlatformShutdownTrunks Platform);

internal sealed class PlayPaneLifespan
{
    private readonly Func<AssetShutdownTransaction>? _injectedTransactionMaker;
    private PlayPaneShutdownTrunks? _trunks;
    private AssetShutdownTransaction? _transaction;
    private IDisposable? _nativePane;
    private Func<bool>? _isNativeRasterizeLoopLoaded;
    private Action? _reqNativeShut;
    private bool _nativeFreeAttempted;
    private bool _completing;

    public PlayPaneLifespan()
    {
    }

    internal PlayPaneLifespan(Func<AssetShutdownTransaction> transactionFactory)
    {
        _injectedTransactionMaker = transactionFactory
            ?? throw new ArgumentNullException(nameof(transactionFactory));
    }

    public GameWindowLifetimeDigest Report { get; private set; } = new(
        PlayPaneLifespanCondition.Active,
        null,
        [],
        null);
    public bool HasShutdownRoots
    {
        get =>
        field || _injectedTransactionMaker is not null; private set;
    }
    internal bool RetainsShutdownGraph => _trunks is not null || _transaction is not null;

    public void BroadcastNativePane(
        IDisposable nativePane,
        Func<bool>? isRasterizeLoopLoaded = null,
        Action? reqShut = null)
    {
        ArgumentNullException.ThrowIfNull(nativePane);
        if (_nativeFreeAttempted || _nativePane is not null)
            throw new InvalidOperationException("A native window is by now lifetime-owned");
        _nativePane = nativePane;
        _isNativeRasterizeLoopLoaded = isRasterizeLoopLoaded;
        _reqNativeShut = reqShut;
    }

    public void BroadcastShutdownTrunks(PlayPaneShutdownTrunks trunks)
    {
        ArgumentNullException.ThrowIfNull(trunks);
        if (_transaction is not null || Report.Status != PlayPaneLifespanCondition.Active)
            throw new InvalidOperationException("Shutdown roots can't change after completion starts");
        if (_trunks is not null && !ReferenceEquals(_trunks, trunks))
            throw new InvalidOperationException("Shutdown roots are by now published");
        _trunks = trunks;
        HasShutdownRoots = true;
    }

    public GameWindowLifetimeDigest TryDone()
    {
        if (Report.IsTerminal || _completing)
            return Report;
        SecureTransaction();

        _completing = true;
        try
        {
            try
            {
                _transaction!.CompleteOrThrow();
            }
            catch (Exception problem)
            {
                Report = new GameWindowLifetimeDigest(
                    PlayPaneLifespanCondition.RetryableIncomplete,
                    _transaction!.LatestJunctureLabel,
                    _transaction.TidyMisses,
                    problem);
                return Report;
            }

            PlayPaneLifespanCondition condition = _transaction!.TidyMisses.Count is 0
                ? PlayPaneLifespanCondition.Complete
                : PlayPaneLifespanCondition.CompleteWithCleanupFailures;
            Report = new GameWindowLifetimeDigest(
                condition,
                null,
                _transaction.TidyMisses,
                null);
            return Report;
        }
        finally
        {
            _completing = false;
        }
    }

    public GameWindowLifetimeDigest ConcludeAndFreeNativePane()
    {
        if (_completing)
            return Report;
        if (!Report.IsTerminal)
            TryDone();

        if (Report.Status == PlayPaneLifespanCondition.RetryableIncomplete)
        {
            AbandonKeptTrunksFollowingTerminalMiss();
            (_, Exception? nativeMiss) = ReleaseNativeWindow();
            Exception terminalProblem = nativeMiss is null
                ? Report.Error ?? new InvalidOperationException(
                    "Shutdown didn't converge prior to native fallback")
                : new AggregateException(
                    "Shutdown and native fallback both failed",
                    Report.Error ?? new InvalidOperationException(
                        "Shutdown didn't converge prior to native fallback"),
                    nativeMiss);
            Report = new GameWindowLifetimeDigest(
                PlayPaneLifespanCondition.AbandonedIncomplete,
                Report.BlockedStage,
                Report.CleanupFailures,
                terminalProblem);
            return Report;
        }

        if (Report.IsTerminal && !_nativeFreeAttempted)
        {
            (NativeReleaseVerdict verdict, Exception? nativeMiss) = ReleaseNativeWindow();
            if (verdict == NativeReleaseVerdict.Deferred)
            {
                Report = new GameWindowLifetimeDigest(
                    PlayPaneLifespanCondition.CompleteWithDeferredNativeRelease,
                    "native window",
                    Report.CleanupFailures,
                    null);
            }
            else if (nativeMiss is not null)
            {
                Report = new GameWindowLifetimeDigest(
                    PlayPaneLifespanCondition.AbandonedIncomplete,
                    "native window",
                    Report.CleanupFailures,
                    nativeMiss);
            }
            else
            {
                FreeFinishedTrunks();
            }
        }

        return Report;
    }

    private void SecureTransaction()
    {
        if (_transaction is not null)
            return;
        _transaction = _injectedTransactionMaker?.Invoke()
            ?? PlayPaneShutdownManifest.Create(
                _trunks ?? throw new InvalidOperationException(
                    "Shutdown roots must publish prior to completion starts"));
    }

    private enum NativeReleaseVerdict
    {
        Released,
        Deferred,
        Failed,
    }

    private (NativeReleaseVerdict Outcome, Exception? Error) ReleaseNativeWindow()
    {
        if (_nativeFreeAttempted)
            return (NativeReleaseVerdict.Released, null);

        if (_isNativeRasterizeLoopLoaded?.Invoke() == true)
        {
            try
            {
                _reqNativeShut?.Invoke();
            }
            catch
            {
            }
            _nativeFreeAttempted = true;
            return (NativeReleaseVerdict.Deferred, null);
        }

        _nativeFreeAttempted = true;
        try
        {
            _nativePane?.Dispose();
            _nativePane = null;
            return (NativeReleaseVerdict.Released, null);
        }
        catch (Exception problem)
        {
            return (NativeReleaseVerdict.Failed, problem);
        }
    }

    private void FreeFinishedTrunks()
    {
        _transaction = null;
        _trunks = null;
    }

    private void AbandonKeptTrunksFollowingTerminalMiss()
    {
        var keptWidget = _trunks?.Live.RetailUi;
        if (keptWidget?.HasDisposalMiss == true)
            keptWidget.AbandonFollowingTerminalMiss();
    }
}

internal static partial class PlayPaneShutdownManifest
{
    private static AssetShutdownOp Hard(string label, Action act) =>
        new(label, act);

    private static AssetShutdownOp Soft(string label, Action act) =>
        new(label, act, ResourceShutdownOperationRule.ReportAndContinue);

    private static void TeardownPlayCore(SimCore core)
    {
        core.Dispose();
        var ownership = core.CaptureOwnership();
        if (!ownership.IsConverged)
        {
            throw new InvalidOperationException(
                "The canonical game runtime didn't converge after disposal");
        }
    }

    private static void TeardownKeptGameplay(RetainedWidgetGameplayWiring? mapping)
    {
        if (mapping is null)
            return;
        mapping.Dispose();
        if (!mapping.IsDisposalComplete)
            throw new InvalidOperationException("Retained gameplay callback removal remains pending");
    }

    private static void TeardownGameplayActs(GameplayFeedActRouter? acts)
    {
        if (acts is null)
            return;
        acts.Dispose();
        if (!acts.IsDisposalComplete)
            throw new InvalidOperationException("Gameplay action callback removal remains pending");
    }

    private static void TeardownCamPtr(CameraPointerInputDriver? ptr)
    {
        if (ptr is null)
            return;
        ptr.Dispose();
        if (!ptr.IsDisposalComplete)
            throw new InvalidOperationException("Camera pointer callback removal remains pending");
    }

    private static void TeardownRouter(IngressShutdownTrunks trunks)
    {
        var router = trunks.Dispatcher;
        if (router is null)
            return;
        trunks.LocomotionInput.Unwire(router);
        TeardownRouterRest(router, trunks);
    }

    private static void TeardownRouterRest(InputRouter router, IngressShutdownTrunks trunks)
    {
        trunks.CameraInput.Loosen(router);
        router.Dispose();
        if (!router.IsDisposalComplete)
            throw new InvalidOperationException("Input dispatcher source removal remains pending");
    }

    private static void TeardownPointerSrc(SilkPointerOrigin? src)
    {
        if (src is null)
            return;
        src.Dispose();
        if (!src.IsDisposalComplete)
            throw new InvalidOperationException("Mouse source callback removal remains pending");
    }

    private static void TeardownKeyboardSrc(SilkKeyboardOrigin? src)
    {
        if (src is null)
            return;
        src.Dispose();
        if (!src.IsDisposalComplete)
            throw new InvalidOperationException("Keyboard source callback removal remains pending");
    }

    private static void TeardownPaneHooks(SilkWindowCallbackWiring? mapping)
    {
        if (mapping is null)
            return;
        mapping.Dispose();
        if (!mapping.IsDisposalComplete)
            throw new NativePaneHookTidyPostponedFault();
    }

    private static void TeardownCanonWidget(CanonWidgetEngineLease tenancy)
    {
        tenancy.Dispose();
        if (!tenancy.IsDisposalComplete)
            throw new InvalidOperationException("The retained UI ownership lease didn't complete disposal");
    }

    private static void TeardownTapRegistrations(MotionHookRegistrationSet? registrations)
    {
        if (registrations is null)
            return;
        registrations.Dispose();
        if (!registrations.IsTidyDone)
            throw new InvalidOperationException("Animation-hook registration cleanup remains pending");
    }

    private static void TeardownSound(OpenAlSoundEngine? engine)
    {
        if (engine is null)
            return;
        engine.Dispose();
        if (!engine.IsDisposalComplete)
            throw new InvalidOperationException("OpenAL native-resource cleanup remains pending");
    }
}
