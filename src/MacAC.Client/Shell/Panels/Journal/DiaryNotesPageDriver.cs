using System.Globalization;
using MacAC.Mechanics.Diary;
using MacAC.Mechanics.Shell;

namespace MacAC.Client.Shell.Panels;

public sealed class DiaryNotesPageDriver
{
    private const uint EarlierBtnIdent = 0x10000565u;
    private const uint UpcomingBtnIdent = 0x10000566u;
    private const uint NewBtnIdent = 0x10000567u;

    private const uint CaptionFieldIdent = 0x10000569u;
    private const uint BannerFieldIdent = 0x1000056Bu;
    private const uint NotesFieldIdent = 0x1000056Du;

    private const uint LeadBtnIdent = 0x1000056Fu;
    private const uint SheetNumberIdent = 0x10000570u;
    private const uint PreviousBtnIdent = 0x10000571u;

    private const uint LocalePhraseIdent = 0x10000573u;
    private const uint CaptureBtnIdent = 0x10000574u;

    private const uint TickerDaysFieldIdent = 0x10000576u;
    private const uint TickerHoursFieldIdent = 0x10000578u;
    private const uint TickerMinutesFieldIdent = 0x1000057Au;

    private const uint TickerDaysCaptionIdent = 0x10000577u;
    private const uint TickerHoursCaptionIdent = 0x10000579u;
    private const uint TickerMinutesCaptionIdent = 0x1000057Bu;
    private const uint RunningTickerPhraseIdent = 0x1000057Cu;
    private const uint BeginBtnIdent = 0x1000057Du;

    public sealed record Bindings(
        ISimDiaryLens Journal,
        SimDiaryLedger Commands,
        Func<uint> PlayerCell,
        Func<DateTime> Now);

    private readonly Bindings _bindings;
    private readonly WidgetField? _caption;
    private readonly WidgetField? _banner;
    private readonly WidgetField? _notes;
    private readonly WidgetField? _tickerDays;
    private readonly WidgetField? _tickerHours;
    private readonly WidgetField? _tickerMinutes;
    private readonly WidgetPhrase? _sheetNumber;

    private readonly WidgetField? _locale;
    private readonly WidgetPhrase? _runningTicker;
    private readonly WidgetElem? _tickerDaysCaption;
    private readonly WidgetElem? _tickerHoursCaption;
    private readonly WidgetElem? _tickerMinutesCaption;
    private readonly WidgetBtn? _begin;

    private long _renderedRev = -1;

    public DiaryNotesPageDriver(WidgetElem sheet, Bindings bindings)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

        _caption = WidgetElem.SeekDescendant(sheet, CaptionFieldIdent) as WidgetField;
        _banner = WidgetElem.SeekDescendant(sheet, BannerFieldIdent) as WidgetField;
        _notes = WidgetElem.SeekDescendant(sheet, NotesFieldIdent) as WidgetField;
        _tickerDays = WidgetElem.SeekDescendant(sheet, TickerDaysFieldIdent) as WidgetField;
        _tickerHours = WidgetElem.SeekDescendant(sheet, TickerHoursFieldIdent) as WidgetField;
        _tickerMinutes = WidgetElem.SeekDescendant(sheet, TickerMinutesFieldIdent) as WidgetField;
        _sheetNumber = WidgetElem.SeekDescendant(sheet, SheetNumberIdent) as WidgetPhrase;
        _locale = WidgetElem.SeekDescendant(sheet, LocalePhraseIdent) as WidgetField;
        _runningTicker = WidgetElem.SeekDescendant(sheet, RunningTickerPhraseIdent) as WidgetPhrase;
        _tickerDaysCaption = WidgetElem.SeekDescendant(sheet, TickerDaysCaptionIdent);
        _tickerHoursCaption = WidgetElem.SeekDescendant(sheet, TickerHoursCaptionIdent);
        _tickerMinutesCaption = WidgetElem.SeekDescendant(sheet, TickerMinutesCaptionIdent);
        _begin = WidgetElem.SeekDescendant(sheet, BeginBtnIdent) as WidgetBtn;

        foreach (WidgetField? field in new[] { _tickerDays, _tickerHours, _tickerMinutes })
        {
            field?.ToonSift = static c => char.IsAsciiDigit(c);
        }

        foreach (WidgetField? field in new[] { _caption, _banner, _notes })
        {
            field?.OnFocusLost = _ => SealPhrase();
        }

        Bind(sheet, NewBtnIdent, () =>
        {
            SealPhrase();
            _bindings.Commands.NewPage();
            Refresh();
        });
        Bind(sheet, LeadBtnIdent, () => Steer(1));
        Bind(sheet, PreviousBtnIdent, () => Steer(_bindings.Journal.Snapshot.PageCount));
        Bind(sheet, EarlierBtnIdent,
            () => Steer(_bindings.Journal.Snapshot.CurrentPage - 1));
        Bind(sheet, UpcomingBtnIdent,
            () => Steer(_bindings.Journal.Snapshot.CurrentPage + 1));
        Bind(sheet, CaptureBtnIdent, CaptureLocale);
        Bind(sheet, BeginBtnIdent, FlipTicker);

        Refresh();
    }

    public void SealPhrase()
    {
        if (_bindings.Journal.Snapshot.CurrentPage is 0)
            return;

        _bindings.Commands.RefreshLatest(
            _caption?.Text ?? string.Empty,
            _banner?.Text ?? string.Empty,
            _notes?.Text ?? string.Empty);

        _bindings.Commands.AssignTicker(
            DecodeField(_tickerDays),
            DecodeField(_tickerHours),
            DecodeField(_tickerMinutes));
    }

    public void Tick()
    {
        if (_bindings.Journal.Snapshot.Revision != _renderedRev)
            Refresh();
        else
            RenewTicker();
    }

    public void OnHidden() => SealPhrase();

    public void Refresh()
    {
        var capture = _bindings.Journal.Snapshot;
        _renderedRev = capture.Revision;

        DiaryPage sheet = _bindings.Journal.Current;

        _caption?.AssignPhrase(sheet.Label);
        _banner?.AssignPhrase(sheet.Title);
        _notes?.AssignPhrase(sheet.Notes);
        _tickerDays?.AssignPhrase(Field(sheet.TimerDays));
        _tickerHours?.AssignPhrase(Field(sheet.TimerHours));
        _tickerMinutes?.AssignPhrase(Field(sheet.TimerMinutes));

        AssignPhrase(_sheetNumber, capture.CurrentPage is 0
            ? string.Empty
            : $"~ {capture.CurrentPage.ToString(CultureInfo.InvariantCulture)} ~");

        _locale?.AssignPhrase(sheet.HasLocation
            ? ComposeLocale(sheet.LocationX, sheet.LocationY)
            : string.Empty);

        RenewTicker();
    }

    private void RenewTicker()
    {
        double leftover = _bindings.Journal.LeftoverTickerSecs(_bindings.Now());
        bool running = leftover > 0d;

        _tickerDays?.Visible = !running;
        _tickerHours?.Visible = !running;
        _tickerMinutes?.Visible = !running;
        _tickerDaysCaption?.Visible = !running;
        _tickerHoursCaption?.Visible = !running;
        _tickerMinutesCaption?.Visible = !running;
        _runningTicker?.Visible = running;

        AssignPhrase(_runningTicker, running
            ? CanonDurationText.Format(leftover)
            : string.Empty);

        _begin?.Label = running ? "Stop" : "Start";
    }

    private void Steer(int sheetNumber)
    {
        SealPhrase();
        _bindings.Commands.GotoSheet(sheetNumber);
        Refresh();
    }

    private void CaptureLocale()
    {
        uint chamber = _bindings.PlayerCell();
        if (chamber is 0u)
            return;

        if (!MacAC.Mechanics.Shell.RadarCoords.TryFromChamber(chamber, out var coordinates))
            return;

        _bindings.Commands.CaptureLocale((float)coordinates.X, (float)coordinates.Y);
        Refresh();
    }

    private void FlipTicker()
    {
        if (_bindings.Journal.LeftoverTickerSecs(_bindings.Now()) > 0d)
        {
            _bindings.Commands.RestartTicker();
            Refresh();
            return;
        }

        SealPhrase();
        _bindings.Commands.BeginTicker(_bindings.Now());
        Refresh();
    }

    private static string ComposeLocale(float x, float y)
    {
        RadarCoords coordinates = new MacAC.Mechanics.Shell.RadarCoords(x, y);
        return $"{coordinates.YPhrase}, {coordinates.XPhrase}";
    }

    private void Bind(WidgetElem sheet, uint elemIdent, Action act)
    {
        if (WidgetElem.SeekDescendant(sheet, elemIdent) is WidgetBtn btn)
            btn.OnClick = act;
    }

    private static int DecodeField(WidgetField? field)
    {
        return int.TryParse(
            field?.Text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int val) && val >= 0
            ? val
            : 0;
    }

    private static string Field(int val) =>
        val is 0 ? string.Empty : val.ToString(CultureInfo.InvariantCulture);

    private static void AssignPhrase(WidgetPhrase? phrase, string val)
    {
        if (phrase is null) return;
        phrase.StrokesSupplier = () => [new WidgetPhrase.Line(val, phrase.DefaultTint)];
    }
}
