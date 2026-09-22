using MacAC.Mechanics.Fellows;

namespace MacAC.Client.Shell.Panels;

public sealed class SocialBoardDriver : IRetainedPaneDriver
{
    public const uint HostArrangementId = 0x2100006Eu;

    public const uint SlotElemId = 0x1000018Fu;

    private const uint FriendsSheetIdent = 0x10000513u;
    private const uint AllegianceSheetIdent = 0x10000291u;
    private const uint FellowshipSheetIdent = 0x10000292u;
    private const uint SquelchSheetIdent = 0x1000054Au;

    private const uint ShutBtnIdent = 0x10000290u;

    public sealed record Callbacks(
        Action Toggle,
        SocialFellowshipSheetDriver.Bindings Fellowship,
        SocialAllegianceSheetDriver.Bindings Allegiance,
        FriendsLedger Friends,
        SquelchLedger Squelch,
        Func<uint, uint, WidgetElem?> TemplateResolver,
        SocialFriendsSheetDriver.Acts? FriendsActions = null,
        SocialSquelchSheetDriver.ClientActions? SquelchActions = null);

    private readonly SocialFellowshipSheetDriver? _fellowship;
    private readonly SocialAllegianceSheetDriver? _allegiance;
    private readonly SocialFriendsSheetDriver? _friends;
    private readonly SocialSquelchSheetDriver? _squelch;

    private readonly Action<uint, uint> _onEngagedSheetAltered;

    private bool _destroyed;

    public WidgetElem Root => _tabBoard;

    private readonly WidgetTabBoard _tabBoard;

    public WidgetTabBoard TabPanel => _tabBoard;
    private SocialBoardDriver(
        WidgetTabBoard tabBoard,
        SocialFellowshipSheetDriver? fellowship,
        SocialAllegianceSheetDriver? allegiance,
        SocialFriendsSheetDriver? friends,
        SocialSquelchSheetDriver? squelch)
    {
        _tabBoard = tabBoard;
        _fellowship = fellowship;
        _allegiance = allegiance;
        _friends = friends;
        _squelch = squelch;

        _onEngagedSheetAltered = (_, _) =>
        {
            RefreshFellowshipSheetVis();
            RefreshAllegianceSheetVis();
        };
        _tabBoard.ActivePageChanged += _onEngagedSheetAltered;
    }

    public static SocialBoardDriver? Bind(ImportedArrangement arrangement, Callbacks hooks)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(hooks);

        if (arrangement.Root is not WidgetTabBoard tabBoard)
        {
            Console.WriteLine(
                "[UI] SocialPanelController.Bind: root didn't build as WidgetTabBoard "
                + $"(actual type {arrangement.Root.GetType().Name}) - social panel will not open");
            return null;
        }

        if (arrangement.SeekElem(ShutBtnIdent) is WidgetBtn shut)
            shut.OnClick = hooks.Toggle;
        else
            Console.WriteLine(
                $"[UI] SocialBoardDriver: close button 0x{ShutBtnIdent:X8} "
                + "not found in the built layout - its handler wasn't wired");

        WidgetElem? fellowshipSheet = WidgetElem.SeekDescendant(tabBoard, FellowshipSheetIdent);
        WidgetElem? allegianceSheet = WidgetElem.SeekDescendant(tabBoard, AllegianceSheetIdent);
        WidgetElem? friendsSheet = WidgetElem.SeekDescendant(tabBoard, FriendsSheetIdent);
        WidgetElem? squelchSheet = WidgetElem.SeekDescendant(tabBoard, SquelchSheetIdent);

        SocialFellowshipSheetDriver? fellowship = fellowshipSheet is null
            ? null
            : SocialFellowshipSheetDriver.Bind(fellowshipSheet, hooks.Fellowship);
        SocialAllegianceSheetDriver? allegiance = allegianceSheet is null
            ? null
            : SocialAllegianceSheetDriver.Bind(allegianceSheet, hooks.Allegiance);
        SocialFriendsSheetDriver? friends = friendsSheet is null
            ? null
            : SocialFriendsSheetDriver.Bind(
                friendsSheet, hooks.Friends, hooks.TemplateResolver, hooks.FriendsActions);
        SocialSquelchSheetDriver? squelch = squelchSheet is null
            ? null
            : SocialSquelchSheetDriver.Bind(
                squelchSheet, hooks.Squelch, hooks.TemplateResolver, hooks.SquelchActions);

        if (fellowshipSheet is null)
            Console.WriteLine($"[UI] SocialBoardDriver: Fellowship page 0x{FellowshipSheetIdent:X8} not found");
        if (allegianceSheet is null)
            Console.WriteLine($"[UI] SocialBoardDriver: Allegiance page 0x{AllegianceSheetIdent:X8} not found");
        if (friendsSheet is null)
            Console.WriteLine($"[UI] SocialBoardDriver: Friends page 0x{FriendsSheetIdent:X8} not found");
        if (squelchSheet is null)
            Console.WriteLine($"[UI] SocialBoardDriver: Squelch page 0x{SquelchSheetIdent:X8} not found");

        return new SocialBoardDriver(tabBoard, fellowship, allegiance, friends, squelch);
    }

    public void ActivateTabs() => _tabBoard.ActivateTabBehavior();

    public void RevealAllegiance() => _tabBoard.SwitchTo(AllegianceSheetIdent);

    public void RevealFellowship() => _tabBoard.SwitchTo(FellowshipSheetIdent);

    public void RevealFriends() => _tabBoard.SwitchTo(FriendsSheetIdent);

    public bool IsShowingAllegiance => _tabBoard.EngagedSheetElemIdent == AllegianceSheetIdent;

    public bool IsShowingFellowship => _tabBoard.EngagedSheetElemIdent == FellowshipSheetIdent;

    public bool IsShowingFriends => _tabBoard.EngagedSheetElemIdent == FriendsSheetIdent;

    private bool _shown;

    public void OnShown()
    {
        _shown = true;
        RefreshFellowshipSheetVis();
        RefreshAllegianceSheetVis();
    }

    public void OnConcealed()
    {
        _shown = false;
        RefreshFellowshipSheetVis();
        RefreshAllegianceSheetVis();
    }

    public void RestartSessDeclaration()
    {
        _fellowship?.RewindSheetShownLatch();
        _allegiance?.RestartSheetShownLatch();
    }

    public void RedeclareFollowingWorldEntry()
    {
        RefreshFellowshipSheetVis();
        _allegiance?.RedeclareFollowingRealmListing();
    }

    public void Tick()
    {
        if (_destroyed) return;
        _fellowship?.Tick();
        _allegiance?.Tick();
        if (_shown)
        {
            _friends?.Tick();
            _squelch?.Tick();
        }
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _tabBoard.ActivePageChanged -= _onEngagedSheetAltered;
    }

    private void RefreshFellowshipSheetVis() =>
        _fellowship?.ApplySheetShown(_shown && IsShowingFellowship);

    private void RefreshAllegianceSheetVis() =>
        _allegiance?.AssignSheetShown(_shown && IsShowingAllegiance);
}
