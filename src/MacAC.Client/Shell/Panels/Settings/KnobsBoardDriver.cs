using MacAC.Mechanics.Comms;

namespace MacAC.Client.Shell.Panels;

public sealed class KnobsBoardDriver : IRetainedPaneDriver
{
    public const uint HubLayoutId = 0x2100006Eu;

    public const uint SocketElementId = 0x1000018Du;

    private const uint GameplaySheetIdent = 0x10000212u;
    private const uint ToonSheetIdent = 0x10000211u;
    private const uint CommsSheetIdent = 0x1000050Cu;
    private const uint SettingsSheetIdent = 0x10000213u;

    private const uint ShutBtnIdent = 0x10000210u;

    private const uint QuitToToonPickIdent = 0x10000203u;
    private const uint ConfigureKeyboardIdent = 0x10000204u;
    private const uint InPlayHelpFilesIdent = 0x10000205u;
    private const uint UrgentAssistanceIdent = 0x10000206u;
    private const uint DossierAbuseIdent = 0x10000207u;
    private const uint UsePointerTurningPrefsIdent = 0x100005CCu;
    private const uint QuitPlayIdent = 0x10000617u;

    private const uint EnactBtnIdent = 0x100001FCu;
    private const uint RestartBtnIdent = 0x100001FDu;
    private const uint DefaultsBtnIdent = 0x100001FEu;

    public sealed record CallbacksUnit(
        Action Toggle,
        Action RequestExitToCharacterSelection,
        Action ExitGame,
        Action UseMouseTurningSettings,
        Action<string> DisplaySystemMessage,
        Action? AfterApply = null,
        Action? OpenConfigureKeyboard = null)
    {
        public string UrgentAssistanceMsg { get; init; } =
            OptionsPaneText.UrgentAssistanceUnavailable;

        public string DossierAbuseMsg { get; init; } =
            OptionsPaneText.DossierAbuseUnavailable;
    }

    private readonly Dictionary<uint, KnobPage> _sheets = [];
    private bool _destroyed;

    public WidgetElem Root => _tabBoard;

    private readonly WidgetTabBoard _tabBoard;

    public WidgetTabBoard TabPanel => _tabBoard;
    public IReadOnlyDictionary<uint, KnobPage> Sheets => _sheets;

    public KnobPage GameplaySheet => _sheets[GameplaySheetIdent];

    public KnobPage ToonSheet => _sheets[ToonSheetIdent];

    public KnobPage CommsSheet => _sheets[CommsSheetIdent];

    public KnobPage SettingsSheet => _sheets[SettingsSheetIdent];

    public bool IsShowingGameplay =>
        _tabBoard.EngagedSheetElemIdent == GameplaySheetIdent;

    public bool IsShowingToon =>
        _tabBoard.EngagedSheetElemIdent == ToonSheetIdent;

    public bool IsShowingConfiguration =>
        _tabBoard.EngagedSheetElemIdent == SettingsSheetIdent;

    public void RevealGameplay() => _tabBoard.SwitchTo(GameplaySheetIdent);

    public void RevealToon() => _tabBoard.SwitchTo(ToonSheetIdent);

    public void RevealConfiguration() => _tabBoard.SwitchTo(SettingsSheetIdent);

    private KnobsBoardDriver(WidgetTabBoard tabBoard, Action? followingEnact)
    {
        _tabBoard = tabBoard;

        _sheets.Add(GameplaySheetIdent, new KnobPage { FollowingEnact = null });
        foreach (uint sheetIdent in new[] { ToonSheetIdent, CommsSheetIdent, SettingsSheetIdent })
        {
            KnobPage sheet = new KnobPage { FollowingEnact = followingEnact };
            _sheets.Add(sheetIdent, sheet);
        }

        _tabBoard.ActivePageChanged += OnEngagedSheetAltered;
    }

    public static KnobsBoardDriver? Bind(
        ImportedArrangement arrangement,
        CallbacksUnit hooks,
        Func<uint, (uint tex, int w, int h)>? locateSprite = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(hooks);

        if (arrangement.Root is not WidgetTabBoard tabBoard)
        {
            Console.WriteLine(
                "[UI] OptionsPanelController.Bind: root didn't build as WidgetTabBoard "
                + $"(actual type {arrangement.Root.GetType().Name}) - Options panel will not open");
            return null;
        }

        KnobsBoardDriver driver = new KnobsBoardDriver(tabBoard, hooks.AfterApply);

        if (arrangement.SeekElem(ShutBtnIdent) is WidgetBtn shut)
            shut.OnClick = hooks.Toggle;
        else
            Console.WriteLine(
                $"[UI] KnobsBoardDriver: close button 0x{ShutBtnIdent:X8} "
                + "not found in the built layout - its handler wasn't wired");

        AttachBtn(arrangement, QuitToToonPickIdent, hooks.RequestExitToCharacterSelection);
        AttachBtn(arrangement, ConfigureKeyboardIdent, hooks.OpenConfigureKeyboard);
        AttachBtn(arrangement, UsePointerTurningPrefsIdent, hooks.UseMouseTurningSettings);
        AttachBtn(arrangement, QuitPlayIdent, hooks.ExitGame);
        AttachBtn(arrangement, UrgentAssistanceIdent,
            () => hooks.DisplaySystemMessage(hooks.UrgentAssistanceMsg));
        AttachBtn(arrangement, DossierAbuseIdent,
            () => hooks.DisplaySystemMessage(hooks.DossierAbuseMsg));

        foreach (uint sheetIdent in new[] { ToonSheetIdent, CommsSheetIdent, SettingsSheetIdent })
        {
            KnobPage sheet = driver._sheets[sheetIdent];
            WidgetElem? sheetTrunk = WidgetElem.SeekDescendant(tabBoard, sheetIdent);
            if (sheetTrunk is null) continue;

            WidgetBtn? enact = AttachSheetBtn(sheetTrunk, EnactBtnIdent, sheet.Apply);
            WidgetBtn? restart = AttachSheetBtn(sheetTrunk, RestartBtnIdent, sheet.Reset);
            WidgetBtn? defaults = AttachSheetBtn(sheetTrunk, DefaultsBtnIdent, sheet.Defaults);

            AppendFooterBacking(sheetTrunk, enact, restart, defaults, locateSprite);

            if (enact is not null && restart is not null)
            {
                sheet.OnKnobAltered = () =>
                {
                    uint phase = sheet.Changed
                        ? WidgetButtonStateMachine.Normal
                        : WidgetButtonStateMachine.Ghosted;
                    enact.TrySetCanonPhase(phase);
                    restart.TrySetCanonPhase(phase);
                };
                sheet.OnKnobAltered();
            }
        }

        return driver;
    }

    public void ActivateTabs() => _tabBoard.ActivateTabBehavior();

    private const int FooterBackingZOrdering = int.MinValue / 2;

    public void OnConcealed()
    {
        if (_sheets.TryGetValue(_tabBoard.EngagedSheetElemIdent, out KnobPage? sheet))
            sheet.OnHidden();
    }

    public void OnShown()
    {
        if (_sheets.TryGetValue(_tabBoard.EngagedSheetElemIdent, out KnobPage? sheet))
            sheet.OnShown();
    }

    public void OnSrvOptionsSeeded()
    {
        if (_sheets.TryGetValue(_tabBoard.EngagedSheetElemIdent, out KnobPage? sheet))
            sheet.ReloadFromOnline();
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _tabBoard.ActivePageChanged -= OnEngagedSheetAltered;
    }

    private static WidgetBtn? AttachSheetBtn(WidgetElem sheetTrunk, uint elemIdent, Action onPress)
    {
        if (WidgetElem.SeekDescendant(sheetTrunk, elemIdent) is WidgetBtn btn)
        {
            btn.OnClick = onPress;
            return btn;
        }

        Console.WriteLine(
            $"[UI] KnobsBoardDriver: page 0x{sheetTrunk.DatElemIdent:X8}'s button "
            + $"0x{elemIdent:X8} not found - its handler wasn't wired");
        return null;
    }

    private static void AppendFooterBacking(
        WidgetElem sheetTrunk,
        WidgetBtn? enact,
        WidgetBtn? restart,
        WidgetBtn? defaults,
        Func<uint, (uint tex, int w, int h)>? locateSprite)
    {
        WidgetBtn? lead = enact ?? restart ?? defaults;
        if (lead is null)
        {
            Console.WriteLine(
                $"[UI] KnobsBoardDriver: page 0x{sheetTrunk.DatElemIdent:X8} has no "
                + "resolved Apply/Reset/Defaults buttons - footer backing field skipped");
            return;
        }

        WidgetSolidSpriteFill backing = new WidgetSolidSpriteFill
        {
            SpriteIdent = CanonChromeSprites.MiddlePopulate,
            SpriteResolve = locateSprite,
            Left = 0f,
            Top = lead.Top,
            Width = sheetTrunk.Width,
            Height = lead.Height,
            ZOrder = FooterBackingZOrdering,
        };
        sheetTrunk.AddChild(backing);
    }

    private static void AttachBtn(ImportedArrangement arrangement, uint elemIdent, Action? onPress)
    {
        if (onPress is null) return;
        if (arrangement.SeekElem(elemIdent) is WidgetBtn btn)
        {
            btn.OnClick = () =>
            {
                Console.WriteLine($"[options] gameplay button 0x{elemIdent:X8} clicked - handler invoked");
                onPress();
            };
        }
        else
            Console.WriteLine(
                $"[UI] KnobsBoardDriver: Gameplay-tab button 0x{elemIdent:X8} "
                + "not found in the built layout - its handler wasn't wired");
    }

    private void OnEngagedSheetAltered(uint earlierSheetElemIdent, uint newSheetElemIdent)
    {
        if (earlierSheetElemIdent is not 0 && _sheets.TryGetValue(earlierSheetElemIdent, out KnobPage? earlier))
            earlier.OnHidden();

        if (_sheets.TryGetValue(newSheetElemIdent, out KnobPage? upcoming))
            upcoming.OnShown();
    }
}
