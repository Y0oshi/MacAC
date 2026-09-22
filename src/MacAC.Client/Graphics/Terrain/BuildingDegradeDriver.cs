using MacAC.Cockpit.Panels.Settings;

namespace MacAC.Client.Graphics;

internal sealed class BuildingDegradeDriver : IStructureDowngradeCycleBeat
{
    internal const int FpsHistoryLen = 20;
    internal const int ContenderHistoryLen = 30;
    internal const float FloorFps = 8f;
    internal const float IdealFps = 10f;
    internal const float CeilingFps = 20f;

    private readonly Func<ReadoutPrefs> _prefs;
    private readonly float[] _cycleSecs = new float[FpsHistoryLen];
    private readonly float[] _contenderHistory = new float[ContenderHistoryLen];

    internal BuildingDegradeDriver(Func<ReadoutPrefs> settings)
        => _prefs = settings ?? throw new ArgumentNullException(nameof(settings));

    internal float Fps { get; private set; }
    internal float AutomaticMultiplier { get; private set; }
    internal float DowngradeGap => _prefs().DegradeDistance;
    internal float EngagedMultiplier
    {
        get
        {
            var prefs = _prefs();
            return prefs.AutomaticDegrades
                ? AutomaticMultiplier
                : prefs.GraphicsPerformance;
        }
    }

    internal void Tick(double passedSecs)
    {
        double sum = 0d;
        for (int idx = 0; idx < _cycleSecs.Length; ++idx)
            sum += _cycleSecs[idx];
        Fps = sum > 0.000199999995f
            ? (float)(FpsHistoryLen / sum)
            : 0f;

        ProgressCycleHistory(_cycleSecs, (float)passedSecs);
        AutomaticMultiplier = ProgressAutomaticMultiplier(
            _contenderHistory,
            _prefs().AutomaticDegrades,
            Fps,
            AutomaticMultiplier);
    }

    internal static void ProgressCycleHistory(Span<float> history, float cycleSecs)
    {
        if (history.Length != FpsHistoryLen)
            throw new ArgumentException(
                $"Retail FPS history must contain precisely {FpsHistoryLen} slots.",
                nameof(history));

        history[..^1].CopyTo(history[1..]);
        history[0] = cycleSecs;
    }

    internal static float ProgressAutomaticMultiplier(
        Span<float> history,
        bool automatic,
        float fps,
        float latest)
    {
        if (history.Length != ContenderHistoryLen)
            throw new ArgumentException(
                $"Retail degrade history must contain precisely {ContenderHistoryLen} slots.",
                nameof(history));

        history[1..].CopyTo(history);
        if (!automatic)
        {
            history[^1] = latest;
            return latest;
        }

        float contender = DeriveContender(fps, latest);
        bool stable = IsContenderStable(history, contender);

        if (stable)
            latest = contender;
        history[^1] = latest;
        return latest;
    }

    internal static bool IsContenderStable(ReadOnlySpan<float> history, float contender)
    {
        if (history.Length != ContenderHistoryLen)
            throw new ArgumentException(
                $"Retail degrade history must contain precisely {ContenderHistoryLen} slots.",
                nameof(history));

        for (int idx = 0; idx < ContenderHistoryLen; ++idx)
        {
            if (!(Math.Abs((double)history[idx] - contender) < 0.01))
                return false;
        }

        return true;
    }

    internal static float DeriveContender(float fps, float latest)
    {
        double fpsWide = fps;
        double floorFps = FloorFps;
        double idealFps = IdealFps;
        double ceilingFps = CeilingFps;
        double w0 = LoShoulder(fpsWide, floorFps * 0.75,
            (floorFps + idealFps) * 0.5);
        double w1 = Triangle(fpsWide, floorFps,
            floorFps * 0.25 + idealFps * 0.75);
        double w2 = Triangle(fpsWide,
            (floorFps + idealFps) * 0.5,
            (idealFps + ceilingFps) * 0.5);
        double w3 = Triangle(fpsWide,
            idealFps * 0.75 + ceilingFps * 0.25,
            ceilingFps);
        double w4 = HiShoulder(fpsWide,
            (idealFps + ceilingFps) * 0.5,
            ceilingFps * 1.25);
        double weight = w0 + w1 + w2 + w3 + w4;
        float numeratorFollowingW0 = (float)((double)-0.150000006f * w0);
        float numeratorFollowingW1 = (float)(
            (double)numeratorFollowingW0 - (double)0.02f * w1);
        float numeratorFollowingW2 = (float)(
            (double)numeratorFollowingW1 + (double)0f * w2);
        float numeratorFollowingW3 = (float)(
            (double)numeratorFollowingW2 + (double)0.01f * w3);
        double numerator = (double)numeratorFollowingW3 + (double)0.1f * w4;
        double adjustment = weight > 0d
            ? numerator / weight
            : 0d;
        double contender = (double)latest + adjustment;
        if (contender > 1d)
            contender = 1d;
        else if (contender < -1d)
            contender = -1d;
        return (float)contender;
    }

    void IStructureDowngradeCycleBeat.Tick(double passedSecs) => Tick(passedSecs);

    private static double Triangle(double val, double lo, double hi)
        => Math.Max(0d, 1d - Math.Abs(2d * val - (hi + lo)) / (hi - lo));

    private static double LoShoulder(double val, double peak, double zero)
    {
        return val < peak ? 1d : Math.Max(0d, 1d - Math.Abs(val - peak) / (zero - peak));
    }

    private static double HiShoulder(double val, double zero, double peak)
    {
        return val > peak ? 1d : Math.Max(0d, 1d - Math.Abs(val - peak) / (peak - zero));
    }
}
