namespace MacAC.Mechanics.Realm;

/// <summary>The Derethian calendar with a configurable time origin (the region DAT supplies it).</summary>
public sealed class DerethAlmanac
{
    private const int DaysInAYear = DerethDateMoment.DaysInAMonth * DerethDateMoment.MonthsInAYear;

    public DerethAlmanac(double originShiftBeats = DerethDateMoment.DayRatioOriginShiftBeats) =>
        AssignOriginShift(originShiftBeats);

    public double OriginShiftBeats { get; private set; }

    public void AssignOriginShift(double originOffsetTicks)
    {
        if (!double.IsFinite(originOffsetTicks))
            throw new ArgumentOutOfRangeException(nameof(originOffsetTicks));
        OriginShiftBeats = originOffsetTicks;
    }

    public double DayFraction(double beats)
    {
        double shifted = Shift(beats);
        return (shifted - Math.Floor(shifted / DerethDateMoment.DayBeats) * DerethDateMoment.DayBeats) / DerethDateMoment.DayBeats;
    }

    public DerethDateMoment.HourLabel LatestHour(double beats)
    {
        return (DerethDateMoment.HourLabel)Math.Clamp(
            (int)Math.Floor(DayFraction(beats) * DerethDateMoment.HoursInADay), 0, DerethDateMoment.HoursInADay - 1);
    }

    public bool IsDaytime(double beats)
    {
        return LatestHour(beats) is >= DerethDateMoment.HourLabel.Dawnsong and <= DerethDateMoment.HourLabel.WarmtideAndHalf;
    }

    public DerethDateMoment.Almanac ToCalendar(double beats)
    {
        double shifted = Shift(beats);
        int year = (int)(shifted / DerethDateMoment.YearBeats);
        double intoYear = shifted - year * DerethDateMoment.YearBeats;
        int month = Math.Min(DerethDateMoment.MonthsInAYear - 1, (int)(intoYear / DerethDateMoment.MonthBeats));
        double intoMonth = intoYear - month * DerethDateMoment.MonthBeats;
        int day = Math.Min(DerethDateMoment.DaysInAMonth, (int)(intoMonth / DerethDateMoment.DayBeats) + 1);
        return new DerethDateMoment.Almanac(year + DerethDateMoment.ZeroYear, (DerethDateMoment.MonthLabel)month, day, LatestHour(beats));
    }

    public int Year(double beats) => (int)(Shift(beats) / DerethDateMoment.YearBeats);

    public int AbsoluteYear(double beats) => Year(beats) + DerethDateMoment.ZeroYear;

    public int DayOfYear(double beats)
    {
        double shifted = Shift(beats);
        int year = (int)(shifted / DerethDateMoment.YearBeats);
        double intoYear = shifted - year * DerethDateMoment.YearBeats;
        return Math.Clamp((int)(intoYear / DerethDateMoment.DayBeats), 0, DaysInAYear - 1);
    }

    private double Shift(double ticks)
    {
        if (!double.IsFinite(ticks))
            throw new ArgumentOutOfRangeException(nameof(ticks));
        return Math.Max(0d, ticks) + OriginShiftBeats;
    }
}
