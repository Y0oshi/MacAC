namespace MacAC.Mechanics.Realm;

public static class DerethDateMoment
{
    public const int HoursInADay = 16;
    public const int DaysInAMonth = 30;
    public const int MonthsInAYear = 12;
    public const double DayBeats = 7620.0;
    public const double HourBeats = DayBeats / HoursInADay;       // 476.25
    public const double MonthBeats = DayBeats * DaysInAMonth;     // 228,600
    public const double YearBeats = MonthBeats * MonthsInAYear;   // 2,743,200
    public const double UpperBeats = 1_073_741_828.0;
    public const int ZeroYear = 10;

    /// <summary>Tick 0 is 7/16 of the way through a day.</summary>
    public const double DayRatioOriginShiftBeats = (7.0 / 16.0) * DayBeats;   // 3333.75

    public const double OriginShiftTicks = DayRatioOriginShiftBeats;

    private const int DaysInAYear = DaysInAMonth * MonthsInAYear;

    /// <summary>The 16 half-hour slots; day runs Dawnsong through WarmtideAndHalf.</summary>
    public enum HourLabel
    {
        Darktide = 0,
        DarktideAndHalf,
        Foredawn,
        ForedawnAndHalf,
        Dawnsong,
        DawnsongAndHalf,
        Morntide,
        MorntideAndHalf,
        Midsong,
        MidsongAndHalf,
        Warmtide,
        WarmtideAndHalf,
        Evensong,
        EvensongAndHalf,
        Gloaming,
        GloamingAndHalf,
    }

    public enum MonthLabel
    {
        Morningthaw = 0,
        Solclaim,
        Seedsow,
        Leafdawning,
        Verdantine,
        Thistledown,
        Harvestgain,
        Leafcull,
        Frostfell,
        Snowreap,
        Coldeve,
        Wintersebb,
    }

    public readonly record struct Almanac(int Year, MonthLabel Month, int Day, HourLabel Hour);

    public static double DayFraction(double beats)
    {
        double shifted = Shift(beats);
        return (shifted - Math.Floor(shifted / DayBeats) * DayBeats) / DayBeats;
    }

    public static HourLabel CurrentHour(double beats)
    {
        return (HourLabel)Math.Clamp((int)Math.Floor(DayFraction(beats) * HoursInADay), 0, HoursInADay - 1);
    }

    public static bool IsDaytime(double beats)
    {
        return CurrentHour(beats) is >= HourLabel.Dawnsong and <= HourLabel.WarmtideAndHalf;
    }

    public static Almanac ToCalendar(double beats)
    {
        double shifted = Shift(beats);
        int year = (int)(shifted / YearBeats);
        double intoYear = shifted - year * YearBeats;
        int month = Math.Min((int)(intoYear / MonthBeats), MonthsInAYear - 1);
        double intoMonth = intoYear - month * MonthBeats;
        int day = Math.Min((int)(intoMonth / DayBeats) + 1, DaysInAMonth);
        return new Almanac(year + ZeroYear, (MonthLabel)month, day, CurrentHour(beats));
    }

    public static int Year(double beats) => (int)(Shift(beats) / YearBeats);

    public static int AbsoluteYear(double beats) => Year(beats) + ZeroYear;

    public static int DayOfYear(double beats)
    {
        double shifted = Shift(beats);
        int year = (int)(shifted / YearBeats);
        double intoYear = shifted - year * YearBeats;
        return Math.Clamp((int)(intoYear / DayBeats), 0, DaysInAYear - 1);
    }

    private static double Shift(double beats) => Math.Max(0, beats) + OriginShiftTicks;
}
