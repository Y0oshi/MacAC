using System.Diagnostics;

namespace MacAC.Client.Graphics;

internal readonly record struct LandscapeRenderTelemetryFacts(
    int VisibleSlots,
    int Draws,
    int LoadedSlots,
    int CapacitySlots);

// Owns permanent terrain timing and diagnostic publication
internal sealed class RealmRenderTelemetry(IRenderFrameTelemetryLog log)
{
    private readonly IRenderFrameTelemetryLog _trace = log ?? throw new ArgumentNullException(nameof(log));
    private readonly Stopwatch _landStopwatch = new();
    private readonly RollingTimingSpecimenPane _landSpecimens = new(256);

    public void CommenceLandPaint() => _landStopwatch.Restart();

    public void FinishLandPaint()
    {
        _landStopwatch.Stop();
        _landSpecimens.PushHundredthsMicroseconds(
            (long)(_landStopwatch.Elapsed.TotalMicroseconds * 100.0));
    }

    public void PushLandSpecimen(long passedHundredthsMicroseconds)
    {
        _landSpecimens.PushHundredthsMicroseconds(passedHundredthsMicroseconds);
    }

    public void BroadcastLandTelemetry(LandscapeRenderTelemetryFacts facts)
    {
        var timing = _landSpecimens.Freeze();
        double medianMicroseconds = timing.MedianHundredthsMicroseconds / 100.0;
        double p95Microseconds = timing.Percentile95HundredthsMicroseconds / 100.0;
        string allowance = medianMicroseconds > 1000.0 ? " BUDGET_OVER" : string.Empty;
        _trace.WriteLine(
            $"[TERRAIN-DIAG]{allowance} cpu_us={medianMicroseconds:F2}m/"
            + $"{p95Microseconds:F2}p95  draws={facts.Draws}/frame  "
            + $"visible={facts.VisibleSlots}  loaded={facts.LoadedSlots}  "
            + $"capacity={facts.CapacitySlots}");
    }
}
