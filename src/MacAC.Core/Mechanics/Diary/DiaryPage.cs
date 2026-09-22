namespace MacAC.Mechanics.Diary;

/// <summary>One page of the in-game journal, as edited by the player.</summary>
public sealed record DiaryPage(
    string Label = "",
    string Title = "",
    string Notes = "",
    int TimerDays = 0,
    int TimerHours = 0,
    int TimerMinutes = 0,
    float LocationX = 0f,
    float LocationY = 0f,
    bool HasLocation = false,
    double RunningTimerSeconds = 0d)
{
    public const int UpperCaptionLen = 16;

    public const int UpperBannerLen = 32;

    public const int UpperNotesLen = 2048;

    /// <summary>What "New" produces: a page with nothing on it.</summary>
    public static readonly DiaryPage Empty = new();

    public bool HasTicker => (TimerDays | TimerHours | TimerMinutes) is not 0;

    public TimeSpan TickerInterval => new(TimerDays, TimerHours, TimerMinutes, 0);

    public bool IsTickerRunning => RunningTimerSeconds > 0d;

    /// <summary>The page with every text field cut to its edit-box limit.</summary>
    public DiaryPage Clipped()
    {
        return this with
        {
            Label = Truncate(Label, UpperCaptionLen),
            Title = Truncate(Title, UpperBannerLen),
            Notes = Truncate(Notes, UpperNotesLen),
        };
    }

    private static string Truncate(string phrase, int threshold) =>
        phrase.Length > threshold ? phrase[..threshold] : phrase;
}
