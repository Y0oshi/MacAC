namespace MacAC.Client.Shell.Panels;

public sealed class JournalBoardDriver : IRetainedPaneDriver
{
    public const uint HubArrangementIdent = 0x2100006Eu;

    public const uint SocketElemIdent = 0x10000559u;

    public const uint ContractsSheetIdent = 0x100005D4u;

    public const uint NotesSheetIdent = 0x10000563u;

    public const uint SheetRosterSheetIdent = 0x10000564u;

    private const uint ShutBtnIdent = 0x10000562u;
    private readonly Action<uint, uint> _onEngagedSheetAltered;
    private bool _destroyed;

    /// <summary>Root element of the imported panel - the tab host itself.</summary>
    public WidgetElem Root => _tabBoard;

    private readonly WidgetTabBoard _tabBoard;

    public WidgetTabBoard TabBoard => _tabBoard;
    private readonly DiaryContractsPageDriver? _contracts;

    public DiaryContractsPageDriver? Contracts => _contracts;
    private readonly DiaryNotesPageDriver? _notes;

    public DiaryNotesPageDriver? Notes => _notes;
    public DiaryPageListDriver? PageList { get; private set; }

    private readonly Action _persistJournal;

    private JournalBoardDriver(
        WidgetTabBoard tabBoard,
        DiaryContractsPageDriver? contracts,
        DiaryNotesPageDriver? notes,
        DiaryPageListDriver? sheetRoster,
        Action persistJournal)
    {
        _tabBoard = tabBoard;
        _contracts = contracts;
        _notes = notes;
        PageList = sheetRoster;
        _persistJournal = persistJournal;

        _onEngagedSheetAltered = (earlier, _) =>
        {
            if (earlier == NotesSheetIdent)
            {
                _notes?.OnHidden();
                _persistJournal();
            }
        };
        _tabBoard.ActivePageChanged += _onEngagedSheetAltered;
    }

    public sealed record ClientCallbacks(
        Action Toggle,
        DiaryContractsPageDriver.Bindings Contracts,
        DiaryNotesPageDriver.Bindings Notes,
        Action SaveJournal,
        Func<Action<int>, DiaryPageListDriver.Bindings> PageList);

    public static JournalBoardDriver? Bind(ImportedArrangement arrangement, ClientCallbacks hooks)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(hooks);

        if (arrangement.Root is not WidgetTabBoard tabBoard)
        {
            Console.WriteLine(
                "[UI] JournalPanelController.Bind: root didn't build as WidgetTabBoard "
                + $"(actual type {arrangement.Root.GetType().Name}) - journal panel will not open");
            return null;
        }

        if (arrangement.SeekElem(ShutBtnIdent) is WidgetBtn shut)
            shut.OnClick = hooks.Toggle;

        DiaryContractsPageDriver? contracts = null;
        if (arrangement.SeekElem(ContractsSheetIdent) is { } contractsSheet)
            contracts = new DiaryContractsPageDriver(contractsSheet, hooks.Contracts);
        else
            Console.WriteLine("[UI] JournalBoardDriver: contracts page not found");

        DiaryNotesPageDriver? notes = null;
        if (arrangement.SeekElem(NotesSheetIdent) is { } notesSheet)
            notes = new DiaryNotesPageDriver(notesSheet, hooks.Notes);
        else
            Console.WriteLine("[UI] JournalBoardDriver: notes page not found");

        DiaryPageListDriver? sheetRoster = null;
        JournalBoardDriver built = new JournalBoardDriver(
            tabBoard, contracts, notes, sheetRoster: null, hooks.SaveJournal);
        if (arrangement.SeekElem(SheetRosterSheetIdent) is { } rosterSheet)
        {
            sheetRoster = new DiaryPageListDriver(
                rosterSheet,
                hooks.PageList(sheetNumber =>
                {
                    notes?.SealPhrase();
                    hooks.Notes.Commands.GotoSheet(sheetNumber);
                    built.RevealNotes();
                }));
        }
        else
        {
            Console.WriteLine("[UI] JournalBoardDriver: page list not found");
        }

        built.FastenSheetRoster(sheetRoster);
        return built;
    }

    public void EngageTabs() => _tabBoard.ActivateTabBehavior();

    public void RevealContracts() => _tabBoard.SwitchTo(ContractsSheetIdent);

    /// <summary>Switches to the notes tab - what opening a page from the index does.</summary>
    public void RevealNotes() => _tabBoard.SwitchTo(NotesSheetIdent);

    public void RevealSheetRoster() => _tabBoard.SwitchTo(SheetRosterSheetIdent);

    public void Tick()
    {
        if (_destroyed) return;
        _contracts?.Tick();
        _notes?.Tick();
        PageList?.Tick();
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _tabBoard.ActivePageChanged -= _onEngagedSheetAltered;

        _notes?.OnHidden();
        _persistJournal();
    }

    private void FastenSheetRoster(DiaryPageListDriver? sheetRoster)
        => PageList = sheetRoster;
}
