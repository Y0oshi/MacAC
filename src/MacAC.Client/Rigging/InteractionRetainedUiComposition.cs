using MacAC.Assets;
using MacAC.Client.Arcana;
using MacAC.Client.Controls;
using MacAC.Client.Extensions;
using MacAC.Client.Fighting;
using MacAC.Client.Graphics;
using MacAC.Client.Preferences;
using MacAC.Client.Realm;
using MacAC.Client.Shell;
using MacAC.Client.Shell.Panels;
using MacAC.Client.Telemetry;
using MacAC.Cockpit.Input;
using MacAC.Cockpit.Panels.Chat;
using MacAC.Cockpit.Panels.Vitals;
using MacAC.Mechanics.Gear;
using MacAC.Sim;
using MacAC.Wire.Messages;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace MacAC.Client.Rigging;

internal sealed record DealingRetainedWidgetDependencies(
    EngineKnobs Options,
    PlayPaneVisuals Graphics,
    Func<int, int, byte[]> BackbufferReader,
    IView Window,
    IInputContext Input,
    string ShadersDirectory,
    IDatAccess Dats,
    object DatLock,
    BitmapStash TextureCache,
    BitmapFont? DebugFont,
    HostQuiescenceTurnstile HostQuiescence,
    RetainedWidgetInputCaptureSlot RetainedInputCapture,
    InputRouter? InputRouter,
    MacAC.Client.Paging.DeferredAvatarWarpWireSink TeleportSink,
    string KeyBindingsFilePath,
    EnginePreferencesDriver Settings,
    BuildingDegradeDriver BuildingDegrades,
    SimCore Runtime,
    ISimFightingAttackOps CombatAttackOperations,
    EngineFightingTargetOperationsSlot CombatTargetOperations,
    EngineArcanaCastOperationsSlot SpellCastOperations,
    ArcanaCatalog ArcanaCatalog,
    StackSplitGauge StackSplitQuantity,
    BufferedWidgetRegistry? UiRegistry,
    OnlineFightingModeDirectiveSlot CombatModeCommands,
    IAvatarIdentitySource PlayerIdentity,
    IAvatarModeSource PlayerMode,
    Func<DeferredPickingViewPlaneSource, PickingCameraSource>
        SelectionCameraFactory,
    DeferredRenderFrameTelemetrySource FrameDiagnostics,
    VitalsModel? ExistingVitals,
    Action<string>? Toast,
    Func<double> ClientTime,
    Action<string> Log,
    MacAC.Client.Graphics.Gpu.IClientGpuDevice GpuDevice,
    ILatestGpuCycleOrigin GpuFrameSource,
    Func<MacAC.Mechanics.Realm.DerethDateMoment.Almanac> CurrentCalendar,
    MacAC.Client.Graphics.Packs.RenderPackRegistrySource? RenderPackCatalog = null,
    Func<MacAC.Client.Graphics.Packs.RenderPackTelemetryCapture>?
        RenderPackDiagnostics = null,
    string? ScreenshotsDirectory = null,
    AppAutopilotSurface? Automation = null,
    Func<GameplayInputFrameDriver?>? GameplayInputFrame = null)
{
    public SimActionLedger Actions => Runtime.ActHolder;

    public SimStashLedger Inventory => Runtime.SatchelHolder;

    public SimToonLedger Character => Runtime.ToonHolder;

    public SimCommsLedger Communication =>
        Runtime.CommunicationHolder;

    public ISimAvatarDriverSource AvatarController =>
        Runtime.MovementOwner;
}

internal sealed record RetainedWidgetAssembly(
    WidgetHub Host,
    CanonWidgetEngine Runtime,
    VitalsModel Vitals,
    ChatModel Chat,
    ToonSheetSupplier CharacterSheet,
    FrameScreenshotDriver? Screenshots);

internal sealed record DealingRetainedWidgetResult(
    SimFightingAttackLedger CombatAttack,
    ExternalContainerLifespanDriver ExternalContainerLifecycle,
    GearDealingDriver ItemInteraction,
    ArcanaEngine Magic,
    RetainedWidgetAssembly? RetainedUi,
    DealingWidgetLateWiring LateBindings);

internal interface IGameWindowDealingRetainedWidgetPublication
{
    void PublishInteractionRetainedUi(DealingRetainedWidgetResult outcome);
}

internal enum DealingRetainedWidgetAssemblyPoint
{
    LateBindingsCreated,
    CombatTargetCreated,
    ExternalContainerLifecycleCreated,
    ItemInteractionCreated,
    MagicRuntimeCreated,
    RetainedUiDisabled,
    UiHostAcquired,
    InputCaptureBound,
    CursorAssetsCreated,
    CharacterSheetCreated,
    MouseInputWired,
    KeyboardInputWired,
    UiAssetsCreated,
    UiProbeCreated,
    UiRuntimeMounted,
    InventoryContainerBound,
    ResultPublished,
}

internal sealed class DealingWidgetLateWiring : IDisposable
{
    private IDisposable? _feedGrab;
    private IDisposable? _satchelVessel;
    private readonly List<(string Name, IDisposable Binding)> _lateHolderMappings = [];
    private PickingCameraSource? _pickCam;
    private bool _deactivationBegun;

    public DeferredOnlineSessionWidgetAuthority Session { get; } = new();
    public DeferredGameEngineStateDirectives SimCore { get; } = new();
    public DeferredPickingWidgetAuthority Selection { get; } = new();
    public DeferredPickingViewPlaneSource PickLensPlane { get; } = new();
    public DeferredRadarCaptureSource Radar { get; } = new();
    public DeferredStashContainerSource SatchelVessel { get; } = new();
    public DeferredRealmLifespanAutopilotEngine Automation { get; } = new();
    public PickingCameraSource PickCam
    {
        get
        {
            return _pickCam ?? throw new InvalidOperationException(
            "The retained-UI selection camera isn't initialized");
        }
    }

    public void BootstrapPickCam(PickingCameraSource src)
    {
        ArgumentNullException.ThrowIfNull(src);
        ObjectDisposedException.ThrowIf(_deactivationBegun, this);
        if (_pickCam is not null)
            throw new InvalidOperationException("The retained selection camera is by now initialized");
        _pickCam = src;
    }

    public void AdoptFeedGrab(IDisposable mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ObjectDisposedException.ThrowIf(_deactivationBegun, this);
        if (_feedGrab is not null)
            throw new InvalidOperationException("Retained input capture is by now owned");
        _feedGrab = mapping;
    }

    public void AdoptSatchelVessel(IDisposable mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ObjectDisposedException.ThrowIf(_deactivationBegun, this);
        if (_satchelVessel is not null)
            throw new InvalidOperationException("Retained inventory binding is by now owned");
        _satchelVessel = mapping;
    }

    public void AdoptLateHolderMapping(string label, IDisposable mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(mapping);
        ObjectDisposedException.ThrowIf(_deactivationBegun, this);
        _lateHolderMappings.Add((label, mapping));
    }

    public void Dispose()
    {
        if (_deactivationBegun
            && _lateHolderMappings.Count is 0
            && _satchelVessel is null
            && _feedGrab is null)
            return;
        _deactivationBegun = true;

        List<Exception>? misses = null;
        Automation.Deactivate();
        Radar.Deactivate();
        PickLensPlane.Deactivate();
        Selection.Deactivate();
        SimCore.Deactivate();
        Session.Deactivate();
        SatchelVessel.Deactivate();
        for (int idx = _lateHolderMappings.Count - 1; idx >= 0; --idx)
        {
            (string label, IDisposable mapping) = _lateHolderMappings[idx];
            try
            {
                mapping.Dispose();
                _lateHolderMappings.RemoveAt(idx);
            }
            catch (Exception miss)
            {
                (misses ??= []).Add(new InvalidOperationException(
                    $"Retained UI late owner binding '{label}' didn't detach",
                    miss));
            }
        }
        Release(ref _satchelVessel, "inventory container binding", ref misses);
        Release(ref _feedGrab, "retained input capture", ref misses);

        if (misses is not null)
            throw new AggregateException("Retained UI late binding cleanup failed", misses);
    }

    private static void Release(
        ref IDisposable? mapping,
        string label,
        ref List<Exception>? misses)
    {
        var latest = mapping;
        if (latest is null)
            return;
        try
        {
            latest.Dispose();
            mapping = null;
        }
        catch (Exception miss)
        {
            (misses ??= []).Add(new InvalidOperationException(
                $"Retained UI {label} didn't detach",
                miss));
        }
    }
}

internal interface IDealingRetainedWidgetAssemblyMint
{
    IDisposable AttachFightingMark(
        DealingRetainedWidgetDependencies deps,
        DeferredPickingWidgetAuthority pick);

    ExternalContainerLifespanDriver BuildExternalVesselLifecycle(
        DealingRetainedWidgetDependencies deps,
        DeferredOnlineSessionWidgetAuthority sess);

    GearDealingDriver BuildGearDealing(
        DealingRetainedWidgetDependencies deps,
        DealingWidgetLateWiring lateMappings);

    ArcanaEngine BuildMagicCore(
        DealingRetainedWidgetDependencies deps,
        DealingWidgetLateWiring lateMappings,
        GearDealingDriver gearDealing);

    RetainedWidgetAssembly BuildKeptWidget(
        DealingRetainedWidgetDependencies deps,
        DealingWidgetLateWiring lateMappings,
        CanonWidgetEngineLease tenancy,
        SimFightingAttackLedger fightingAssault,
        GearDealingDriver gearDealing,
        ArcanaEngine magic,
        Action<DealingRetainedWidgetAssemblyPoint> checkpoint);

    void Release(IDisposable asset);
}

internal sealed partial class CanonDealingRetainedWidgetAssemblyMint
    : IDealingRetainedWidgetAssemblyMint
{
    public IDisposable AttachFightingMark(
        DealingRetainedWidgetDependencies dependencies,
        DeferredPickingWidgetAuthority pick)
    {
        return dependencies.CombatTargetOperations.BindOwned(new OnlineFightingTargetOperations(
            autoTarget: () => dependencies.Character.Options.GetOptionBit(
                CharacterOptionId.AutoTarget),
            selectClosestTarget: () =>
                pick.PickClosestFightingObjective(unhideToast: false)));
    }

    public void Release(IDisposable asset) => asset.Dispose();

    private static void TryFree(
        ref IDisposable? asset,
        string label,
        ref List<Exception>? misses)
    {
        if (asset is null)
            return;
        try
        {
            asset.Dispose();
            asset = null;
        }
        catch (Exception miss)
        {
            (misses ??= []).Add(new InvalidOperationException(
                $"Retained UI {label} rollback failed",
                miss));
        }
    }
}

internal sealed class DealingRetainedWidgetAssemblyPhase(
    DealingRetainedWidgetDependencies dependencies,
    CanonWidgetEngineLease retainedUiLease,
    IGameWindowDealingRetainedWidgetPublication publication,
    IDealingRetainedWidgetAssemblyMint? maker = null,
    Action<DealingRetainedWidgetAssemblyPoint>? flawInjection = null)
        : IDealingWidgetAssemblyPhase<
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext>,
        HubFeedCameraOutcome,
        SubstanceFxListSoundOutcome,
        PreferencesDevToolsResult,
        RealmRenderResult,
        DealingRetainedWidgetResult>
{
    private readonly DealingRetainedWidgetDependencies _deps = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));
    private readonly CanonWidgetEngineLease _keptWidgetTenancy = retainedUiLease
            ?? throw new ArgumentNullException(nameof(retainedUiLease));
    private readonly IGameWindowDealingRetainedWidgetPublication _bulletin = publication
            ?? throw new ArgumentNullException(nameof(publication));
    private readonly IDealingRetainedWidgetAssemblyMint _maker = maker ?? new CanonDealingRetainedWidgetAssemblyMint();
    private readonly Action<DealingRetainedWidgetAssemblyPoint>? _flawInjection = flawInjection;

    public DealingRetainedWidgetResult Compose()
    {
        AssemblyAcquisitionScope ambit = new AssemblyAcquisitionScope();
        try
        {
            var lateTenancy = ambit.Acquire(
                "retained UI late bindings",
                static () => new DealingWidgetLateWiring(),
                _maker.Release);
            var late = lateTenancy.Resource;
            late.BootstrapPickCam(
                _deps.SelectionCameraFactory(late.PickLensPlane));
            Flaw(DealingRetainedWidgetAssemblyPoint.LateBindingsCreated);

            IDisposable fightingMarkMapping = _maker.AttachFightingMark(
                _deps,
                late.Selection);
            late.AdoptLateHolderMapping(
                "combat-target operations",
                fightingMarkMapping);
            Flaw(DealingRetainedWidgetAssemblyPoint.CombatTargetCreated);
            var externalTenancy = ambit.Acquire(
                "external container lifecycle",
                () => _maker.BuildExternalVesselLifecycle(
                    _deps,
                    late.Session),
                _maker.Release);
            Flaw(DealingRetainedWidgetAssemblyPoint.ExternalContainerLifecycleCreated);
            var gearTenancy = ambit.Acquire(
                "item interaction controller",
                () => _maker.BuildGearDealing(_deps, late),
                _maker.Release);
            Flaw(DealingRetainedWidgetAssemblyPoint.ItemInteractionCreated);
            var magicTenancy = ambit.Acquire(
                "magic runtime",
                () => _maker.BuildMagicCore(
                    _deps,
                    late,
                    gearTenancy.Resource),
                _maker.Release);
            Flaw(DealingRetainedWidgetAssemblyPoint.MagicRuntimeCreated);

            var widgetTenancy = ambit.Own(
                "retained UI runtime lease",
                _keptWidgetTenancy,
                _maker.Release);
            RetainedWidgetAssembly? keptWidget = null;
            if (_deps.Options.RetailUi)
            {
                keptWidget = _maker.BuildKeptWidget(
                    _deps,
                    late,
                    _keptWidgetTenancy,
                    _deps.Actions.CombatAttack,
                    gearTenancy.Resource,
                    magicTenancy.Resource,
                    Flaw);
            }
            else
            {
                Flaw(DealingRetainedWidgetAssemblyPoint.RetainedUiDisabled);
            }

            var outcome = new DealingRetainedWidgetResult(
                _deps.Actions.CombatAttack,
                externalTenancy.Resource,
                gearTenancy.Resource,
                magicTenancy.Resource,
                keptWidget,
                late);
            _bulletin.PublishInteractionRetainedUi(outcome);
            lateTenancy.Transfer();
            externalTenancy.Transfer();
            gearTenancy.Transfer();
            magicTenancy.Transfer();
            widgetTenancy.Transfer();
            Flaw(DealingRetainedWidgetAssemblyPoint.ResultPublished);
            ambit.Complete();
            return outcome;
        }
        catch (Exception miss)
        {
            ambit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }

    public DealingRetainedWidgetResult Compose(
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform,
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance,
        PreferencesDevToolsResult prefs,
        RealmRenderResult realm)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(realm);
        return !ReferenceEquals(_deps.Graphics, platform.Graphics)
            || !ReferenceEquals(_deps.Input, platform.Input)
            || !ReferenceEquals(_deps.InputRouter, hub.InputRouter)
            || !ReferenceEquals(_deps.Dats, substance.Dats)
            || !ReferenceEquals(_deps.ArcanaCatalog, substance.ArcanaCatalog)
            || !ReferenceEquals(_deps.TextureCache, realm.Foundation.TextureCache)
            || !ReferenceEquals(_deps.DebugFont, realm.Foundation.DebugFont)
            ? throw new InvalidOperationException(
                "Interaction/UI dependencies do not match the ordered phase results")
            : Compose();
    }

    private void Flaw(DealingRetainedWidgetAssemblyPoint pt) =>
        _flawInjection?.Invoke(pt);
}
