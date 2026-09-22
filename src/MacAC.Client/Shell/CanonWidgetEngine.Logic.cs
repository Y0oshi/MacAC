using MacAC.Dat;
using MacAC.Client.Graphics;
using MacAC.Client.Link;
using MacAC.Client.Shell.Panels;
using MacAC.Client.Shell.Testing;
using MacAC.Cockpit.Input;
using MacAC.Cockpit.Panels.Vitals;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;
using Silk.NET.Input;

namespace MacAC.Client.Shell;

public sealed partial class CanonWidgetEngine
{
    private CanonAssayNamePicker GearLabels
    {
        get
        {
            if (field is not null)
                return field;
            lock (_bindings.Assets.DatLock)
            {
                _beastLabels ??= CreatureDisplayNamePicker.Load(_bindings.Assets.Dats);
                return field ??= CanonAssayNamePicker.Load(
                    _bindings.Assets.Dats, _beastLabels);
            }
        }
    }

    private StackSplitGauge PileDivideQty => _bindings.StackSplitQuantity;

    public void PersistJournal() => DiaryFile.Save(DateTime.UtcNow);

    public void FocusCommsEntry()
    {
        if (Host.Root.DefaultPhraseFeed is { } feed)
            Host.Root.AssignKeyboardFocus(feed);
    }

    public WidgetHub Host => _bindings.Host;

    public CanonWindowOpacityDriver PaneDensity { get; }

    public CanonWindowLockDisplayDriver PaneLockExhibit { get; }

    public CanonWidgetAssets Assets => _bindings.Assets;

    public GearDealingDriver GearDealing => _bindings.Inventory.ItemInteraction;

    public ToonSheetSupplier ToonSheetSupplier => _bindings.Character.Provider;

    public ToolbarDriver? ToolbarDriver { get; private set; }

    public ToolbarInputDriver? ToolbarFeedDriver { get; private set; }

    public FightingWidgetDriver? FightingWidgetDriver { get; private set; }

    public ArcanacastingWidgetDriver? ArcanacastingWidgetDriver { get; private set; }

    public ArcanabookWindowDriver? ArcanabookPaneDriver { get; private set; }

    public AssayWidgetDriver? AppraisalDriver { get; private set; }

    public WidgetViewport? BeastAppraisalViewRectWidget { get; private set; }

    public WidgetElem? ExaminationCycle { get; private set; }

    public EffectsWidgetDriver? PositiveFxListDriver { get; private set; }

    public EffectsWidgetDriver? NegativeFxListDriver { get; private set; }

    public LinkStatusWidgetDriver? ConnectConditionWidgetDriver { get; private set; }

    public VitaeWidgetDriver? VitaeWidgetDriver { get; private set; }

    public MiniGameWidgetDriver? MiniPlayWidgetDriver { get; private set; }

    public IndicatorBarDriver? IndicatorBarDriver { get; private set; }

    public JumpPowerbarDriver? LeapPowerbarDriver { get; private set; }

    public CanonFpsDriver? FpsDriver { get; private set; }

    public SelectedObjectDriver? ChosenObjectDriver { get; private set; }

    public WidgetViewport? PaperdollViewportWidget { get; private set; }

    public WidgetNineSlicePane? InventoryFrame { get; private set; }

    public StashDriver? SatchelBoardDriver { get; private set; }

    public CanonPromptMint? DialogFactory { get; private set; }

    public CanonTooltipExhibitor? HintPresenter { get; private set; }

    public ExternalContainerDriver? ExternalVesselDriver { get; private set; }

    public MerchantWidgetDriver? MerchantDriver { get; private set; }

    public KnobsBoardDriver? OptionsPanelController { get; private set; }

    public SocialBoardDriver? SocialPanelController { get; private set; }

    public Panels.JournalBoardDriver? JournalPanelController { get; private set; }

    private DiaryPersistence DiaryFile
    {
        get
        {
            return field ??= new DiaryPersistence(
            _bindings.Quests.JournalCommands,
            _bindings.Quests.JournalDirectory,
            _bindings.Quests.Report);
        }
    }

    public void RedeclareSocialBoardFollowingRealmListing() =>
        SocialPanelController?.RedeclareFollowingWorldEntry();

    internal ToonManagementWidgetDriver? ToonManagementDriver =>
        _toonManagementMount?.Controller;

    internal CreditsWidgetDriver? CreditsDriver { get; private set; }

    internal ToonCreationWidgetDriver? ToonCreationDriver =>
        _toonCreationMount?.Controller;

    internal WidgetViewport? ChargenPreviewViewRectWidget =>
        ToonCreationDriver?.LooksViewRect;

    internal MacAC.Client.Graphics.IClientChargenPreviewControl? ChargenPreviewControl
    {
        get => ToonCreationDriver?.LooksPreviewControl;
        set
        {
            if (ToonCreationDriver is { } driver)
                driver.LooksPreviewControl = value;
        }
    }

    internal MacAC.Mechanics.Genesis.IGenesisPalSetSource? ChargenPalSetSrc
    {
        get => ToonCreationDriver?.LooksPalSetSrc;
        set
        {
            if (ToonCreationDriver is { } driver)
                driver.LooksPalSetSrc = value;
        }
    }

    internal MacAC.Mechanics.Genesis.IGenesisGarbTableSource? ChargenClothingChartSrc
    {
        get => ToonCreationDriver?.LooksClothingChartSrc;
        set
        {
            if (ToonCreationDriver is { } driver)
                driver.LooksClothingChartSrc = value;
        }
    }

    internal MacAC.Mechanics.Genesis.IGenesisPaletteColorSource? ChargenSwatchTintSrc
    {
        get => ToonCreationDriver?.LooksSwatchTintSrc;
        set
        {
            if (ToonCreationDriver is { } driver)
                driver.LooksSwatchTintSrc = value;
        }
    }

    internal MacAC.Client.Shell.Panels.IChargenSwatchBitmapOrigin? ChargenSwatchTextureSrc
    {
        get => ToonCreationDriver?.LooksSwatchTextureSrc;
        set
        {
            if (ToonCreationDriver is { } driver)
                driver.LooksSwatchTextureSrc = value;
        }
    }

    internal WidgetViewport? SummaryPreviewViewRectWidget =>
        ToonCreationDriver?.SummaryViewRect;

    internal MacAC.Client.Graphics.IClientChargenPreviewControl? SummaryPreviewControl
    {
        get => ToonCreationDriver?.SummaryPreviewControl;
        set
        {
            if (ToonCreationDriver is { } driver)
                driver.SummaryPreviewControl = value;
        }
    }

    public void ReinstateArrangement()
    {
        _persistence?.AssignGameplayEngaged(_bindings.IsGameplayDisplay?.Invoke() ?? true);
        if (_bindings.Persistence is { } persistence)
        {
            var monitor = persistence.ScreenSize();
            Host.Root.Width = monitor.Width;
            Host.Root.Height = monitor.Height;
            _previousMonitorDims = new System.Numerics.Vector2(monitor.Width, monitor.Height);
        }
        _persistence?.ReinstateAll();
    }

    public void PersistArrangement()
    {
        _persistence?.PersistAll();
        PersistCommsPaneFilters();
    }

    public void PersistNamedArrangement(string profileLabel) => _persistence?.PersistNamed(profileLabel);

    public void ReinstateNamedArrangement(string profileLabel) => _persistence?.ReinstateNamed(profileLabel);

    public void Tick(double diffSecs)
    {
        _bindings.SynchronizeDisplayPhase?.Invoke();
        _persistence?.AssignGameplayEngaged(_bindings.IsGameplayDisplay?.Invoke() ?? true);
        Panels.WidgetMediaClock.Advance(diffSecs);
        FpsDriver?.Tick();
        _vividMarkIndicator?.Tick();
        _missileDiagTopLayer?.Tick();
        _vitalsFlankByFlank?.Tick();
        ArcanabookPaneDriver?.Tick();
        AppraisalDriver?.Tick(diffSecs);
        ArcanacastingWidgetDriver?.Tick();
        PositiveFxListDriver?.Tick();
        NegativeFxListDriver?.Tick();
        ConnectConditionWidgetDriver?.Tick();
        IndicatorBarDriver?.Tick();
        _commsPaneDriver?.RefreshUnreadIndicator();
        LeapPowerbarDriver?.Tick();
        SecureBarterDriver?.Tick();
        ChosenObjectDriver?.Tick(diffSecs);
        ExternalVesselDriver?.Tick();
        SocialPanelController?.Tick();
        JournalPanelController?.Tick();
        MapHousePanelController?.Tick(diffSecs);
        _gearCooldownDriver?.Tick();
        _connectionMount?.Tick();
        _toonManagementMount?.Tick();
        ToonManagementDriver?.Tick(_connectionMount?.IsShown == true);
        CreditsDriver?.Tick();
        _toonCreationMount?.Tick();
        ToonCreationDriver?.Tick();
        DialogFactory?.Tick();
        Host.Tick(diffSecs);
        HintPresenter?.Tick();
        _automation?.Tick(diffSecs);
    }

    public void Draw(System.Numerics.Vector2 monitorDims)
    {
        if (monitorDims != _previousMonitorDims)
        {
            Host.Root.Width = monitorDims.X;
            Host.Root.Height = monitorDims.Y;
            bool gameplay = _bindings.IsGameplayDisplay?.Invoke() ?? true;
            _persistence?.AssignGameplayEngaged(gameplay);
            if (gameplay) _persistence?.ReflowToMonitor();
            _previousMonitorDims = monitorDims;
        }

        Host.Draw(monitorDims);
    }

    public IReadOnlyList<FloatingCommsPaneDriver?> FloatingCommsPanes => _floatingCommsDrivers;

    public void SwitchGameplayKnobsSheet()
        => OpenKnobsSheet(KnobsPanePage.Gameplay);

    public bool FlipPane(string label)
    {
        return CanonPaneRegistry.TryFetchBoardIdent(label, out uint boardIdent)
                ? _boardWidget.FlipBoard(boardIdent)
                : Host.SwitchPane(label);
    }

    public bool SwitchFloatingCommsPane(int paneIdent)
        => Host.SwitchPane(FloatingCommsPaneLabel(paneIdent));

    public void RecordOutToon() => FinishToonSessWithCanonLatches();

    public uint RevealAck(string msg, Action<bool> finished)
    {
        ArgumentNullException.ThrowIfNull(msg);
        ArgumentNullException.ThrowIfNull(finished);
        return DialogFactory?.CraftAck(
            msg,
            blob => finished(blob.FetchBoolean(CanonPromptProperty.AckOutcome))) ?? 0u;
    }

    public void RestartSessPopups()
    {
        try
        {
            CreditsDriver?.RestartSess();
            ToonManagementDriver?.RestartSess();
            DialogFactory?.Reset();
            HintPresenter?.ConcealLatest();
            Host.Root.RestartHintTracking();
        }
        finally
        {
            _gameplayAckDriver?.ResetSession();
        }
    }

    public void RestartSessTransientWidget()
    {
        RestartSessPopups();
        AppraisalDriver?.ResetSession();
        Host.ConcealPane(PaneLabels.Examination);
        SocialPanelController?.RestartSessDeclaration();
    }

    public void RefreshCur(IEnumerable<IMouse> mice)
    {
        var feedback = _bindings.Cursor.Feedback.Update(Host.Root);
        _bindings.Cursor.Manager.Apply(mice, feedback);
    }

    public void FastenNativeCurPane(nint glfwPaneHnd) => _bindings.Cursor.Manager.FastenNativePane(glfwPaneHnd);

    public void ShutPane(string label)
    {
        if (CanonPaneRegistry.TryFetchBoardIdent(label, out uint boardIdent))
            _boardWidget.AssignBoardVis(boardIdent, shown: false);
        else
            Host.ConcealPane(label);
    }

    public void SynchronizeToolbarPaneBtns()
    {
        if (ToolbarDriver is null) return;
        foreach (var (boardIdent, paneLabel) in CanonPaneRegistry.ToolbarBoards)
            ToolbarDriver.AssignBoardOpen(boardIdent, Host.IsPaneShown(paneLabel));
    }

    public Panels.KeyboardSettingsDriver? KeyboardConfigController { get; private set; }

    public Panels.SecureBarterWidgetDriver? SecureBarterDriver { get; private set; }

    public SalvageWidgetDriver? SalvageDriver { get; private set; }

    public void Dispose()
    {
        if (IsDisposalComplete)
            return;

        _shutdown ??= BuildShutdownTransaction(
            () => _automation?.Dispose(),
            () => _persistence?.Dispose(),
            () =>
            {
                _toonSheetSubscription?.Dispose();
                _toonBannersDriver?.Dispose();
                _extensionFlankBoard?.Dispose();
                Host.PaneKeeper.WindowVisibilityChanged -= OnPaneVisAltered;
                PaneLockExhibit.Dispose();
                PaneDensity.Dispose();
                if (SecureBarterDriver is { } barter)
                {
                    _bindings.Inventory.ItemInteraction.SecureTradeRequested -=
                        barter.ReqSecureBarter;
                }
                if (SalvageDriver is { } salvage)
                    _bindings.Inventory.ItemInteraction.PolicyActionRequested -= salvage.ProcessRuleAct;
            },
            () => _gearAckDriver?.Dispose(),
            () =>
            {
                CreditsDriver?.Dispose();
                _connectionMount?.Dispose();
                _toonManagementMount?.Dispose();
                _toonCreationMount?.Dispose();
                _gameplayAckDriver?.Dispose();
            },
            () =>
            {
                DialogFactory?.Dispose();
                HintPresenter?.Dispose();
            },
            _boardWidget.Dispose,
            Host.Dispose);
        _shutdown.CompleteOrThrow();
        IsDisposalComplete = _shutdown.IsComplete;
    }

    internal void BootstrapForTenancy() => Bootstrap();

    internal static bool AuthorsWholeBoardMiddle(ElemDetails trunkDetails)
    {
        const float CanonBannerHeight = 25f;
        return Tour(trunkDetails);

        bool Tour(ElemDetails details)
        {
            bool coversCorpus = details.Width >= trunkDetails.Width
                && details.Height >= trunkDetails.Height - CanonBannerHeight;
            return coversCorpus && details.StateMedia.Values.Any(
                    media => media.File == CanonChromeSprites.MiddlePopulate)
                ? true
                : details.Children.Any(Tour);
        }
    }

    internal static CanonWidgetEngine BuildUninitialized(
        CanonWidgetEngineWiring mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        return new CanonWidgetEngine(mappings);
    }

    internal bool IsDisposalComplete { get; private set; }

    internal bool IsChargenPreviewSheetShown =>
        ToonCreationDriver?.IsLooksSheetShown ?? false;

    internal bool IsSummaryPreviewSheetShown =>
        ToonCreationDriver?.IsSummarySheetShown ?? false;

    internal static AssetShutdownTransaction BuildShutdownTransaction(
        Action teardownAutomation,
        Action teardownPersistence,
        Action delistPaneVis,
        Action teardownGearAck,
        Action teardownGameplayAck,
        Action teardownPopupMaker,
        Action teardownBoardDriver,
        Action teardownHub)
    {
        ArgumentNullException.ThrowIfNull(teardownAutomation);
        ArgumentNullException.ThrowIfNull(teardownPersistence);
        ArgumentNullException.ThrowIfNull(delistPaneVis);
        ArgumentNullException.ThrowIfNull(teardownGearAck);
        ArgumentNullException.ThrowIfNull(teardownGameplayAck);
        ArgumentNullException.ThrowIfNull(teardownPopupMaker);
        ArgumentNullException.ThrowIfNull(teardownBoardDriver);
        ArgumentNullException.ThrowIfNull(teardownHub);

        return new AssetShutdownTransaction(
            new AssetShutdownJuncture("retail UI observers",
            [
                new("UI automation", teardownAutomation),
                new("window persistence", teardownPersistence),
                new("window visibility", delistPaneVis),
            ]),
            new AssetShutdownJuncture("retail UI semantic controllers",
            [
                new("item confirmation", teardownGearAck),
                new("gameplay confirmation", teardownGameplayAck),
            ]),
            new AssetShutdownJuncture("retail UI panel composition",
            [
                new("dialog factory", teardownPopupMaker),
                new("panel controller", teardownBoardDriver),
            ]),
            new AssetShutdownJuncture("retained UI host",
            [
                new("host", teardownHub),
            ]));
    }

    private void Bootstrap()
    {
        var mappings = _bindings;
        lock (_bindings.Assets.DatLock)
        {
            var primaryBoardCycle = ArrangementLoader.ImportInfos(
                _bindings.Assets.Dats, 0x2100006Eu, 0x100005FEu);
            if (primaryBoardCycle is not null)
                _boardWidget.ConfigurePrimaryBoardCycle(primaryBoardCycle);
        }
        MountPanels();
        if (mappings.Connection is { } connection)
        {
            _connectionMount = new ConnectionWidgetMountMarshal(Host.Root, mappings.Assets, connection);
            _connectionMount.Tick();
        }
        ConfigureToonManagement();
        _toonManagementMount?.Tick();
        ConfigureToonCreation();
        _toonCreationMount?.Tick();
        Host.PaneKeeper.WindowVisibilityChanged += OnPaneVisAltered;
        AttachToolbarBoardBtns();
        SynchronizeToolbarPaneBtns();

        {
            var persistence = mappings.Persistence;
            _persistence = new CanonWindowArrangementPersistence(
                Host.PaneKeeper,
                persistence?.Store,
                persistence?.CharacterKey ?? (() => "default"),
                persistence?.ScreenSize ?? (() => ((int)Host.Root.Width, (int)Host.Root.Height)),
                phaseManagedVisPanes:
                [
                    PaneLabels.Combat,
                    PaneLabels.LeapPowerbar,
                    PaneLabels.ExternalVessel,
                    PaneLabels.Vendor,
                    PaneLabels.Vitals,
                    PaneLabels.FlankVitals,
                    PaneLabels.SecureBarter,
                    PaneLabels.Salvage,
                    PaneLabels.ExtensionShelf,
                ]);
            _persistence.RestartToDefaults();
            _persistence.AssignGameplayEngaged(_bindings.IsGameplayDisplay?.Invoke() ?? true);
        }

        if (mappings.Probe.Enabled)
        {
            var sensor = new CanonWidgetAutopilotProbe(
                Host.Root,
                mappings.Inventory.Objects,
                mappings.Probe.Log);
            _automation = new CanonWidgetAutopilotScriptRunner(
                sensor,
                mappings.Probe.ScriptPath,
                mappings.Probe.DumpOnStart,
                mappings.Probe.Log,
                phrase => CommsDirectiveRouter.Submit(
                    phrase,
                    mappings.Chat.ViewModel,
                    mappings.Chat.CommandBus(),
                    CommsChannelKind.Say),
                mappings.Probe.PressInput,
                mappings.Probe.SetInputHeld,
                mappings.Probe.Runtime,
                mappings.Probe.QueueMouseLookDelta);
        }
    }

    public LookupHouseBoardDriver? MapHousePanelController { get; private set; }

    private static string FloatingCommsPaneLabel(int windowId)
    {
        return windowId switch
        {
            1 => PaneLabels.CommsWindow1,
            2 => PaneLabels.CommsWindow2,
            3 => PaneLabels.CommsWindow3,
            4 => PaneLabels.CommsWindow4,
            _ => throw new ArgumentOutOfRangeException(nameof(windowId), windowId, "floating chat window id has to be 1-4"),
        };
    }

    private void PersistCommsPaneFilters()
    {
        if (_bindings.Chat.Store is not { } vault) return;
        var panes = _bindings.Chat.Windows;
        var latest = vault.LoadChat();
        vault.PersistChat(latest with
        {
            ChatWindowMainFilter = panes.FetchSift(ChatPaneState.PrimaryPaneIdent),
            ChatWindow1Filter = panes.FetchSift(1),
            ChatWindow2Filter = panes.FetchSift(2),
            ChatWindow3Filter = panes.FetchSift(3),
            ChatWindow4Filter = panes.FetchSift(4),
        });
    }

    private void PersistCommsDensity()
    {
        if (_bindings.Chat.Store is not { } vault) return;
        var latest = vault.LoadChat();
        vault.PersistChat(latest with
        {
            DefaultOpacity = PaneDensity.DefaultDensity,
            ActiveOpacity = PaneDensity.EngagedDensity,
        });
    }

    private ImportedArrangement? Import(uint arrangementIdent)
    {
        lock (_bindings.Assets.DatLock)
            return ArrangementLoader.Import(
                _bindings.Assets.Dats,
                arrangementIdent,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
    }

    private ImportedArrangement? Import(uint arrangementIdent, uint trunkElemIdent)
    {
        lock (_bindings.Assets.DatLock)
            return ArrangementLoader.Import(
                _bindings.Assets.Dats,
                arrangementIdent,
                trunkElemIdent,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont);
    }

    private void ReqFinishToonSess()
    {
        RevealAck(
            LocateFinishToonSessConfirmMsg(),
            approved =>
            {
                if (approved)
                    FinishToonSessWithCanonLatches();
            });
    }

    private void ReqQuitToToonPick()
    {
        RevealAck(
            LocateFinishToonSessConfirmMsg(),
            approved =>
            {
                if (approved)
                    FinishToonSessWithCanonLatches();
            });
    }

    private string ExtensionShelfConcealedMsg()
    {
        var router = _bindings.Keyboard?.Dispatcher;
        CockpitBinding? tied = router?.Bindings
            .ForAct(MacAC.Cockpit.Input.FeedAct.TogglePluginManager)
            .Cast<CockpitBinding?>()
            .FirstOrDefault();
        if (tied is not { } mapping)
        {
            return "Plugin shelf hidden. Bind Toggle Plugin Manager in "
                + "Configure Keyboard to show it again.";
        }

        DatStringPicker texts = new DatStringPicker(_bindings.Assets.Dats);
        string chordPhrase = new Panels.CanonKeyNames((chartIdent, stringIdent) =>
            texts.Resolve(chartIdent, stringIdent)).Portray(mapping.Chord);
        return $"Plugin shelf hidden. Press {chordPhrase} to show it again.";
    }

    private static string UnmappedTagMappingsTrail(string tagMappingsFileTrail)
    {
        string? direction = System.IO.Path.GetDirectoryName(tagMappingsFileTrail);
        string label = System.IO.Path.GetFileNameWithoutExtension(tagMappingsFileTrail);
        string ext = System.IO.Path.GetExtension(tagMappingsFileTrail);
        string sibling = $"{label}-unmapped{ext}";
        return string.IsNullOrEmpty(direction) ? sibling : System.IO.Path.Combine(direction, sibling);
    }

    private static KeyBindingBook ReplicateWithout(KeyBindingBook src, FeedAct act)
    {
        KeyBindingBook outcome = new KeyBindingBook();
        foreach (CockpitBinding binding in src.All)
            if (binding.Action != act) outcome.Add(binding);
        return outcome;
    }

    private void YieldFromCredits()
        => ToonManagementDriver?.AssignExhibitSuppressed(false);

    private string? LocateChosenObjectLabel(uint oid)
    {
        return _bindings.Toolbar.Objects.Get(oid) is { } objRef
            ? GearLabels.LocateAppropriateLabel(objRef)
            : _bindings.Toolbar.ResolveName(oid);
    }

    private string LocateFinishToonSessConfirmMsg()
    {
        const string backup = "Are you sure you want to end this character session?";
        lock (_bindings.Assets.DatLock)
        {
            return new DatStringPicker(_bindings.Assets.Dats).Resolve(
                    0x23000001u,
                    DatStringPicker.CalculateDigest("ID_Client_EndCharacterSessionConfirm"))
                ?? backup;
        }
    }

    private static string? LocateToonManagementString(
        DatStringPicker texts,
        uint chartIdent,
        string tag) =>
        texts.Resolve(chartIdent, DatStringPicker.CalculateDigest(tag));

    private void AttachToolbarBoardBtns()
    {
        ToolbarDriver?.AttachBoardBtns(
            boardIdent => CanonPaneRegistry.TryFetchPaneLabel(boardIdent, out string label)
                && Host.PaneKeeper.TryGet(label, out _),
            boardIdent =>
            {
                if (CanonPaneRegistry.TryFetchPaneLabel(boardIdent, out string label))
                    FlipPane(label);
            });
    }

    private void AttachVitalsArrangement(ImportedArrangement arrangement)
    {
        VitalsModel model = _bindings.Vitals.ViewModel;
        VitalsDriver.Bind(arrangement,
            () => model.HealthPercent,
            () => model.StaminaPct ?? 0f,
            () => model.ManaPercent ?? 0f,
            () => (model.HealthCurrent, model.HealthMax) is (uint c, uint m) ? $"{c}/{m}" : "",
            () => (model.StaminaCurrent, model.StaminaMax) is (uint c, uint m) ? $"{c}/{m}" : "",
            () => (model.ManaCurrent, model.ManaMax) is (uint c, uint m) ? $"{c}/{m}" : "");
    }

    private void OnPaneVisAltered(string paneLabel, bool shown)
    {
        _boardWidget.WatchPaneVis(paneLabel, shown);
        if (CanonPaneRegistry.TryFetchBoardIdent(paneLabel, out uint boardIdent))
            ToolbarDriver?.AssignBoardOpen(boardIdent, shown);

        if (TryFetchFloatingCommsPaneIdent(paneLabel, out int commsPaneIdent))
        {
            _bindings.Chat.Windows.SetOpen(commsPaneIdent, shown);
            _commsPaneDriver?.AssignIndicatorOpen(commsPaneIdent, shown);
        }
    }

    private static bool TryFetchFloatingCommsPaneIdent(string paneLabel, out int paneIdent)
    {
        paneIdent = paneLabel switch
        {
            PaneLabels.CommsWindow1 => 1,
            PaneLabels.CommsWindow2 => 2,
            PaneLabels.CommsWindow3 => 3,
            PaneLabels.CommsWindow4 => 4,
            _ => 0,
        };
        return paneIdent is not 0;
    }

    private static bool TryFetchCreditsBlobIdent(
        ElemDetails details,
        uint propIdent,
        out uint val)
    {
        if (details.TryFetchNetProp(propIdent, out WidgetPropertyValue prop)
            && prop.Kind is WidgetPropertyKind.DataId or WidgetPropertyKind.Enum
            && prop.UnsignedValue <= uint.MaxValue)
        {
            val = (uint)prop.UnsignedValue;
            return true;
        }

        val = 0u;
        return false;
    }

    private WidgetElem? AssembleSwallowedDescendant(ElemDetails details)
    {
        lock (_bindings.Assets.DatLock)
            return ArrangementLoader.Build(
                details,
                _bindings.Assets.ResolveSprite,
                _bindings.Assets.DefaultFont,
                _bindings.Assets.ResolveFont).Root;
    }

    private void EnrollIndicatorSpecificsBoard(
        uint boardIdent,
        string paneLabel,
        ElemDetails trunkDetails,
        WidgetElem trunk,
        IRetainedPaneDriver? driver)
    {
        var hnd = CanonPaneCycle.Mount(
            Host.Root,
            trunk,
            _bindings.Assets.ResolveSprite,
            new CanonPaneCycle.Options
            {
                PaneMoniker = paneLabel,
                Chrome = CanonWindowChrome.NineSlice,
                Left = 18f,
                Top = 18f,
                Visible = false,
                RescaleX = false,
                RescaleY = true,
                ResizableRims = RescaleRims.Bottom,
                ConstrainPullToAncestor = true,
                ConstrainRescaleToAncestor = true,
                SubstanceMoorings = MooringRims.Left | MooringRims.Top
                    | MooringRims.Right | MooringRims.Bottom,
                SubstancePressThrough = false,
                PaintChromeMiddle = !AuthorsWholeBoardMiddle(trunkDetails),
                Controller = driver,
            });
        _boardWidget.EnrollPrimaryBoard(
            boardIdent,
            paneLabel,
            hnd,
            trunkDetails.TryFetchNetBool(
                CanonPaneWidgetDriver.RevertEarlierPropIdent,
                out bool revertEarlier)
                && revertEarlier);
    }

    private void FinishToonSessWithCanonLatches()
    {
        switch (_bindings.Options.IsGrounded())
        {
            case true:
                _bindings.Indicators.EndCharacterSession();
                break;
            case false:
                _bindings.Options.DisplaySystemMessage(
                    TextRefusals.CantLogOffMidAir);
                break;
            case null:
                break;
        }
    }

    private void ImposePointerTurningPrefsMacro()
    {
        var latest = _bindings.Options.LoadCameraTurning();
        bool usePointerTurningLatest = _bindings.Options.IsUseMouseTurningEnabled();
        var outcome =
            MouseTurningPreferencesMacro.Compute(latest, usePointerTurningLatest);

        _bindings.Options.SaveCameraTurning(outcome.Updated);

        if (outcome.UseMouseTurningChanged)
        {
            _bindings.Options.CommandBus().Publish(
                new SetSingleToonKnobEngineCmd(
                    (uint)CharacterOptionId.UseMouseTurning, true));
        }

        foreach (string stroke in outcome.ChatLines)
            _bindings.Options.DisplayMouseTurningMacroLine(stroke);

        OptionsPanelController?.SettingsSheet.Apply();
    }

    private static uint[]? ScanBlobIdents(
        IReadOnlyDictionary<uint, PropertyValue> props,
        uint tag)
    {
        if (!props.TryGetValue(tag, out var raw)
            || raw is not ArrayProperty arr)
            return null;
        uint[] outcome = new uint[arr.Items.Count];
        for (int idx = 0; idx < arr.Items.Count; ++idx)
            if (arr.Items[idx] is DataIdProperty blobIdent)
                outcome[idx] = blobIdent.Value;
        return outcome;
    }

    private static void ConfigureToolbarFold(
        ImportedArrangement arrangement,
        WidgetCollapsibleFrame cycle,
        float substanceHeight)
    {
        uint[] idents =
        [
            0x100006B6u, 0x100006B7u, 0x100006B8u, 0x100006B9u,
            0x100006BAu, 0x100006BBu, 0x100006BCu, 0x100006BDu,
            0x100006BEu, 0x100006BFu, 0x100006C0u,
        ];
        List<WidgetElem> rank = new List<WidgetElem>();
        float top = float.MaxValue;
        foreach (uint ident in idents)
            if (arrangement.SeekElem(ident) is { } elem)
            {
                rank.Add(elem);
                top = Math.Min(top, elem.Top);
            }
        if (rank.Count is 0) return;

        const int border = CanonChromeSprites.Border;
        cycle.CollapsedHeight = top + 2 * border;
        cycle.ExpandedHeight = substanceHeight + 2 * border;
        cycle.SecondRank = rank;
        cycle.Resizable = true;
        cycle.ResizableEdges = RescaleRims.Bottom;
        cycle.MinHeight = cycle.CollapsedHeight;
        cycle.MaxHeight = cycle.ExpandedHeight;
    }

    private void ConfigureToonManagement()
    {
        var mappings =
            _bindings.CharacterSelection;
        if (mappings is null || _toonManagementMount is not null)
            return;

        _toonManagementMount = new ToonManagementWidgetMountMarshal(
            Host.Root,
            mappings with { RequestCreate = () => ToonCreationDriver?.Open() },
            SecurePopupMaker,
            PullToonManagementAssetList,
            OpenCredits);
    }

    private void ConfigureToonCreation()
    {
        var mappings = _bindings.CharacterCreation;
        if (mappings is null || _toonCreationMount is not null)
            return;

        _toonCreationMount = new ToonCreationWidgetMountMarshal(
            Host.Root,
            mappings,
            SecurePopupMaker,
            PullToonCreationAssetList);
    }

    private CanonPromptMint? SecurePopupMaker()
    {
        AttachPopupMaker();
        return DialogFactory;
    }
}
