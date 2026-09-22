namespace MacAC.Client.Shell.Panels;

public sealed class LookupHouseBoardDriver : IRetainedPaneDriver
{
    public const uint HubArrangementTag = 0x2100006Eu;
    public const uint SocketElemTag = 0x1000018Cu;

    private const uint LookupBtnIdent = 0x100001F3u;
    private const uint LookupSheetIdent = 0x100001F6u;
    private const uint HouseBtnIdent = 0x100001F4u;
    private const uint HouseSheetIdent = 0x100001F7u;
    private const uint ShutBtnIdent = 0x100001F5u;

    public sealed record CallbacksDef(
        Action Toggle,
        MapPageDriver.Bindings Map,
        DwellingPageDriver.Bindings House);

    private readonly MapPageDriver? _lookup;
    private readonly DwellingPageDriver? _house;
    private readonly Action<uint, uint> _onEngagedSheetAltered;
    private bool _shown;
    private bool _destroyed;

    public WidgetElem Root => _tabBoard;
    private readonly WidgetTabBoard _tabBoard;

    public WidgetTabBoard TabPanel => _tabBoard;
    private LookupHouseBoardDriver(
        WidgetTabBoard tabBoard, MapPageDriver? lookup, DwellingPageDriver? house)
    {
        _tabBoard = tabBoard;
        _lookup = lookup;
        _house = house;

        _onEngagedSheetAltered = (_, _) => TriggerHouseShownIfEngaged();
        _tabBoard.ActivePageChanged += _onEngagedSheetAltered;
    }

    public static LookupHouseBoardDriver? Bind(
        ElemDetails trunkDetails, ImportedArrangement arrangement, CallbacksDef hooks)
    {
        ArgumentNullException.ThrowIfNull(trunkDetails);
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(hooks);

        if (arrangement.Root is not WidgetTabBoard tabBoard)
        {
            Console.WriteLine(
                "[UI] MapHousePanelController.Bind: root didn't build as WidgetTabBoard "
                + $"(actual type {arrangement.Root.GetType().Name}) - Map/House panel will not open");
            return null;
        }

        if (arrangement.SeekElem(ShutBtnIdent) is WidgetBtn shut)
            shut.OnClick = hooks.Toggle;
        else
            Console.WriteLine(
                $"[UI] LookupHouseBoardDriver: close button 0x{ShutBtnIdent:X8} not found");

        WidgetElem? lookupSheet = WidgetElem.SeekDescendant(tabBoard, LookupSheetIdent);
        WidgetElem? houseSheet = WidgetElem.SeekDescendant(tabBoard, HouseSheetIdent);

        MapPageDriver? lookup = null;
        if (lookupSheet is not null)
        {
            var lookupSheetDetails = SeekDetails(trunkDetails, LookupSheetIdent);
            lookup = lookupSheetDetails is null
                ? null
                : MapPageDriver.Bind(lookupSheet, lookupSheetDetails, hooks.Map);
        }
        DwellingPageDriver? house = houseSheet is null
            ? null
            : DwellingPageDriver.Bind(houseSheet, hooks.House);

        if (lookupSheet is null)
            Console.WriteLine($"[UI] LookupHouseBoardDriver: Map page 0x{LookupSheetIdent:X8} not found");
        if (houseSheet is null)
            Console.WriteLine($"[UI] LookupHouseBoardDriver: House page 0x{HouseSheetIdent:X8} not found");

        return new LookupHouseBoardDriver(tabBoard, lookup, house);
    }

    public void ArmTabs() => _tabBoard.ActivateTabBehavior();

    public bool IsShowingHouse => _tabBoard.EngagedSheetElemIdent == HouseSheetIdent;

    public bool IsShowingLookup => _tabBoard.EngagedSheetElemIdent == LookupSheetIdent;

    public void RevealLookup() => _tabBoard.SwitchTo(LookupSheetIdent);

    public void RevealHouse() => _tabBoard.SwitchTo(HouseSheetIdent);

    public void OnShown()
    {
        _shown = true;
        TriggerHouseShownIfEngaged();
    }

    public void OnConcealed() => _shown = false;

    public void Tick(double diffSecs)
    {
        if (_destroyed) return;
        _lookup?.Tick(diffSecs);
        _house?.Tick();
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _tabBoard.ActivePageChanged -= _onEngagedSheetAltered;
    }

    private void TriggerHouseShownIfEngaged()
    {
        if (_shown && IsShowingHouse)
            _house?.OnShown();
    }

    private static ElemDetails? SeekDetails(ElemDetails details, uint ident)
    {
        if (details.Id == ident) return details;
        foreach (ElemDetails descendant in details.Children)
        {
            var located = SeekDetails(descendant, ident);
            if (located is not null) return located;
        }
        return null;
    }
}
