using System.Buffers;
using System.Globalization;
using System.Text;

namespace MacAC.Mechanics.Diary;

public readonly record struct DiaryReadOutcome(
    IReadOnlyList<DiaryPage> Pages,
    string? Error);

public static class DiaryFile
{
    private static class Tag
    {
        public const string NewSheet = "<NEWP>";
        public const string SheetNumber = "<PNUM>";
        public const string Label = "<LABE>";
        public const string Title = "<TITL>";
        public const string Notes = "<NOTE>";
        public const string Days = "<DAYS>";
        public const string Hours = "<HOUR>";
        public const string Minutes = "<MINU>";
        public const string LocaleX = "<LOCX>";
        public const string LocaleY = "<LOCY>";
        public const string RunningMoment = "<TIME>";
    }

    private const int TagWidth = 6;

    public const string MalformedFileMsg =
        "Problem loading journal: Your journal file does not create a new page!";

    private static readonly SearchValues<char> ForbiddenInFileLabel =
        SearchValues.Create("\"<>|:*?\\/");

    public static string FileLabelFor(string srvLabel, string toonLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(srvLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(toonLabel);
        return $"Journal-{Scrub(srvLabel)}-{Scrub(toonLabel)}.txt";
    }

    public static DiaryReadOutcome Read(string phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);

        List<DiaryPage> sheets = new List<DiaryPage>();
        DiaryPage? open = null;
        bool anything = false;

        foreach (string raw in phrase.Split('\n'))
        {
            string stroke = raw.TrimEnd('\r');
            if (stroke.Length is 0)
                continue;

            if (stroke[0] != '<')
            {
                anything = true;
                continue;
            }

            string tag = stroke.Length >= TagWidth ? stroke[..TagWidth] : stroke;
            string val = stroke.Length > TagWidth + 1 ? stroke[(TagWidth + 1)..] : string.Empty;
            anything = true;

            if (tag == Tag.NewSheet)
            {
                if (open is not null)
                    sheets.Add(open);
                open = DiaryPage.Empty;
                continue;
            }

            if (open is null)
                return new DiaryReadOutcome([], MalformedFileMsg);

            open = Absorb(open, tag, val);
        }

        if (open is not null)
            sheets.Add(open);

        if (sheets.Count is 0 && anything)
            return new DiaryReadOutcome([], MalformedFileMsg);

        for (int idx = 0; idx < sheets.Count; ++idx)
            sheets[idx] = sheets[idx].Clipped();

        return new DiaryReadOutcome(sheets, null);
    }

    public static string Write(IReadOnlyList<DiaryPage> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);

        StringBuilder phrase = new StringBuilder();
        for (int idx = 0; idx < sheets.Count; ++idx)
        {
            DiaryPage sheet = sheets[idx];
            phrase.Append(Tag.NewSheet).Append('\n');
            Line(phrase, Tag.SheetNumber, Whole(idx + 1));
            Line(phrase, Tag.Label, sheet.Label);
            Line(phrase, Tag.Title, sheet.Title);
            Line(phrase, Tag.Notes, sheet.Notes);
            Line(phrase, Tag.Days, Whole(sheet.TimerDays));
            Line(phrase, Tag.Hours, Whole(sheet.TimerHours));
            Line(phrase, Tag.Minutes, Whole(sheet.TimerMinutes));
            if (sheet.HasLocation)
            {
                Line(phrase, Tag.LocaleX, Fractional(sheet.LocationX));
                Line(phrase, Tag.LocaleY, Fractional(sheet.LocationY));
            }
            Line(phrase, Tag.RunningMoment, Fractional(sheet.RunningTimerSeconds));
        }

        return phrase.ToString();
    }

    private static string Scrub(string raw)
    {
        Span<char> buf = raw.Length <= 256 ? stackalloc char[raw.Length] : new char[raw.Length];
        for (int idx = 0; idx < raw.Length; ++idx)
        {
            char c = raw[idx];
            buf[idx] = c < ' ' || ForbiddenInFileLabel.Contains(c) ? '_' : c;
        }
        return new string(buf);
    }

    private static DiaryPage Absorb(DiaryPage sheet, string tag, string val)
    {
        return tag switch
        {
            Tag.Label => sheet with { Label = val },
            Tag.Title => sheet with { Title = val },
            Tag.Notes => sheet with { Notes = val },
            Tag.Days => sheet with { TimerDays = WholeNumber(val) },
            Tag.Hours => sheet with { TimerHours = WholeNumber(val) },
            Tag.Minutes => sheet with { TimerMinutes = WholeNumber(val) },
            Tag.LocaleX => sheet with { LocationX = Real(val), HasLocation = true },
            Tag.LocaleY => sheet with { LocationY = Real(val), HasLocation = true },
            Tag.RunningMoment => sheet with { RunningTimerSeconds = Real(val) },
            _ => sheet,
        };
    }

    private static void Line(StringBuilder phrase, string tag, string val)
    {
        phrase.Append(tag)
            .Append(' ')
            .Append(val.Replace('\n', ' ').Replace('\r', ' '))
            .Append('\n');
    }

    private static string Whole(int val) => val.ToString(CultureInfo.InvariantCulture);

    private static string Fractional(double val) =>
        val.ToString("0.######", CultureInfo.InvariantCulture);

    private static int WholeNumber(string phrase)
    {
        return int.TryParse(phrase.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int num) ? num : 0;
    }

    private static float Real(string phrase)
    {
        return float.TryParse(phrase.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
    }
}
