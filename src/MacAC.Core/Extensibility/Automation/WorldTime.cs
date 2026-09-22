namespace MacAC.Extensibility.Automation;

public readonly record struct WorldTimeFrame(
    bool IsAvailable,
    double GameTicks,
    int Year,
    int Month,
    int Day,
    int Hour,
    string MonthName,
    string HourName,
    bool IsDay,
    double MinutesUntilDay,
    double MinutesUntilNight);

public interface IWorldTimeControls
{
    WorldTimeFrame Snapshot => default;
}
