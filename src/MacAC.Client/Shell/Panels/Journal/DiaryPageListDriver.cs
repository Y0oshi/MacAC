using System.Globalization;
using System.Numerics;
using MacAC.Mechanics.Diary;
using MacAC.Mechanics.Shell;

namespace MacAC.Client.Shell.Panels;

public sealed class DiaryPageListDriver
{
    /// <summary>The layout the row template lives in - authored <c>0x63</c>.</summary>
    public const uint RankBlueprintArrangementTag = 0x21000067u;

    /// <summary>The row template element - authored <c>0x62</c>.</summary>
    public const uint RankBlueprintElemTag = 0x10000589u;

    private const uint RosterIdent = 0x10000583u;
    private const uint EraseBtnIdent = 0x10000585u;
    private const uint HuntFieldIdent = 0x10000587u;
    private const uint RestartBtnIdent = 0x10000588u;

    private const uint RankNumberIdent = 0x1000058Au;
    private const uint RankBannerIdent = 0x1000058Bu;
    private const uint RankTickerIdent = 0x1000058Cu;
    private const uint RankCaptionIdent = 0x1000058Du;

    private static readonly Vector4 ChosenLabelTint = Vector4.One;

    public sealed record Bindings(
        ISimDiaryLens Journal,
        SimDiaryLedger Commands,
        Action<int> OpenPage,
        Func<uint, uint, WidgetElem?> TemplateResolver,
        Func<DateTime>? Now = null);

    private readonly Bindings _bindings;
    private readonly WidgetBlueprintRosterBbox? _roster;
    private readonly WidgetField? _hunt;
    private readonly List<int> _rankSheets = [];
    private readonly List<(int Page, WidgetPhrase? Title, Vector4 Unselected)> _ranks = [];

    private static readonly TimeSpan DoublePressPane = TimeSpan.FromSeconds(1d);

    private long _renderedRev = -1;
    private string _renderedHunt = string.Empty;
    private int _previousPressSheet;
    private DateTime _previousPressAt;

    public DiaryPageListDriver(WidgetElem sheet, Bindings bindings)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

        _roster = WidgetElem.SeekDescendant(sheet, RosterIdent) as WidgetBlueprintRosterBbox;
        _roster?.TemplateResolver = bindings.TemplateResolver;

        _hunt = WidgetElem.SeekDescendant(sheet, HuntFieldIdent) as WidgetField;

        if (WidgetElem.SeekDescendant(sheet, EraseBtnIdent) is WidgetBtn erase)
            erase.OnClick = EraseChosen;
        if (WidgetElem.SeekDescendant(sheet, RestartBtnIdent) is WidgetBtn restart)
        {
            restart.OnClick = () =>
            {
                _hunt?.AssignPhrase(string.Empty);
                Refresh();
            };
        }

        Refresh();
    }

    public int ChosenSheet { get; private set; }

    public IReadOnlyList<int> RankSheets => _rankSheets;

    public static bool SheetContainsString(DiaryPage sheet, string hunt)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return string.IsNullOrEmpty(hunt)
            ? true
            : sheet.Label.Contains(hunt, StringComparison.Ordinal)
            || sheet.Title.Contains(hunt, StringComparison.Ordinal)
            || sheet.Notes.Contains(hunt, StringComparison.Ordinal);
    }

    public void Tick()
    {
        string hunt = _hunt?.Text ?? string.Empty;
        if (_bindings.Journal.Snapshot.Revision != _renderedRev
            || !string.Equals(hunt, _renderedHunt, StringComparison.Ordinal))

            Refresh();
    }

    public void Refresh()
    {
        var capture = _bindings.Journal.Snapshot;
        _renderedRev = capture.Revision;
        _renderedHunt = _hunt?.Text ?? string.Empty;

        var sheets = _bindings.Journal.Pages;

        _rankSheets.Clear();
        _ranks.Clear();
        _roster?.DrainPreservingRoll();

        for (int idx = 0; idx < sheets.Count; ++idx)
        {
            DiaryPage sheet = sheets[idx];
            if (!SheetContainsString(sheet, _renderedHunt))
                continue;

            int sheetNumber = idx + 1;
            _rankSheets.Add(sheetNumber);
            if (_roster is null)
                continue;

            WidgetElem? rank = _roster.AppendGearFromBlueprintRoster(0);
            if (rank is null)
                continue;

            AssignPhrase(WidgetElem.SeekDescendant(rank, RankNumberIdent) as WidgetPhrase,
                sheetNumber.ToString(CultureInfo.InvariantCulture));
            WidgetPhrase? banner = WidgetElem.SeekDescendant(rank, RankBannerIdent) as WidgetPhrase;
            AssignPhrase(banner, sheet.Title);
            AssignPhrase(WidgetElem.SeekDescendant(rank, RankTickerIdent) as WidgetPhrase,
                sheet.IsTickerRunning
                    ? CanonDurationText.Format(sheet.RunningTimerSeconds)
                    : sheet.HasTicker
                        ? CanonDurationText.Format(sheet.TickerInterval.TotalSeconds)
                        : string.Empty);
            AssignPhrase(WidgetElem.SeekDescendant(rank, RankCaptionIdent) as WidgetPhrase, sheet.Label);

            _ranks.Add((sheetNumber, banner, banner?.DefaultTint ?? Vector4.One));

            int grabbed = sheetNumber;
            if (rank is WidgetDatElement clickable)
            {
                clickable.ClickThrough = false;
                clickable.OnClick = () => Click(grabbed);
            }
        }

        if (ChosenSheet is not 0 && !_rankSheets.Contains(ChosenSheet))
            ChosenSheet = 0;

        ImposePickHighlight();
    }

    public void Click(int sheetNumber)
    {
        DateTime instant = _bindings.Now?.Invoke() ?? DateTime.UtcNow;

        if (sheetNumber == _previousPressSheet && instant - _previousPressAt <= DoublePressPane)
        {
            _previousPressSheet = 0;
            _previousPressAt = default;
            Open(sheetNumber);
            return;
        }

        _previousPressSheet = sheetNumber;
        _previousPressAt = instant;
        Select(sheetNumber);
    }

    public void Select(int sheetNumber)
    {
        ChosenSheet = sheetNumber;
        ImposePickHighlight();
    }

    public void Open(int sheetNumber)
    {
        Select(sheetNumber);
        _bindings.OpenPage(sheetNumber);
    }

    private void EraseChosen()
    {
        if (ChosenSheet is 0)
            return;

        _bindings.Commands.EraseSheet(ChosenSheet);
        ChosenSheet = 0;
        Refresh();
    }

    private void ImposePickHighlight()
    {
        foreach ((int sheetNumber, WidgetPhrase? banner, Vector4 unselected) in _ranks)
        {
            banner?.DefaultTint = sheetNumber == ChosenSheet ? ChosenLabelTint : unselected;
        }
    }

    private static void AssignPhrase(WidgetPhrase? phrase, string val)
    {
        if (phrase is null) return;
        phrase.StrokesSupplier = () => [new WidgetPhrase.Line(val, phrase.DefaultTint)];
    }
}
