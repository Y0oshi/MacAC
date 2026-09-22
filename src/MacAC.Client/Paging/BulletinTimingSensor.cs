using System.Diagnostics;
using System.Text;

namespace MacAC.Client.Paging;

internal sealed class BulletinJunctureTimings
{
    private readonly Dictionary<string, (long Ticks, int Count)> _junctures = [];

    public long SumBeats { get; private set; }

    public void Add(string juncture, long beats)
    {
        if (beats < 0)
            beats = 0;
        SumBeats += beats;
        _junctures[juncture] = _junctures.TryGetValue(juncture, out (long Ticks, int Count) preceding)
            ? (preceding.Ticks + beats, preceding.Count + 1)
            : (beats, 1);
    }

    public IReadOnlyDictionary<string, (long Ticks, int Count)> Stages => _junctures;
}

internal static class BulletinTimingSensor
{
    private const int CumulativeEmitInterval = 64;

    private static readonly Dictionary<string, (long Ticks, int Count)>
        s_cumulativeJunctures = [];
    private static readonly Dictionary<string, int> s_beatPaneYieldCauses =
        [];
    private static long s_cumulativeBeats;
    private static int s_publications;
    private static double s_beatPaneTotalMsec;
    private static double s_beatPaneUpperMsec;
    private static int s_beatPaneTally;
    private static int s_beatPaneYields;
    private static int s_beatPaneOps;
    private static int s_beatPaneAdmissions;
    private static int s_previousWorkerBacklog;
    private static int s_previousQueuedCompletions;

    public static bool Enabled => PagingTelemetry.SensorUnveilTiming;

    // Per-transaction accumulator, or null when the probe is off
    public static BulletinJunctureTimings? BuildTimings() =>
        Enabled ? new BulletinJunctureTimings() : null;

    public static void WriteBulletin(
        uint lbIdent,
        string sort,
        BulletinJunctureTimings timings)
    {
        ++s_publications;
        s_cumulativeBeats += timings.SumBeats;
        StringBuilder stroke = new StringBuilder(256);
        stroke.Append("[publish-timing] lb=0x")
            .Append(lbIdent.ToString("X8"))
            .Append(" kind=").Append(sort)
            .Append(" totalMs=")
            .Append(ToMsec(timings.SumBeats).ToString("F2"))
            .Append(" stages=");
        WriteBulletinRest(timings, stroke);
    }

    private static void WriteBulletinRest(BulletinJunctureTimings timings, StringBuilder stroke)
    {
        AffixJuncturesByPrice(stroke, timings.Stages);
        Console.WriteLine(stroke.ToString());
        foreach ((string juncture, (long beats, int tally)) in timings.Stages)
        {
            s_cumulativeJunctures[juncture] = s_cumulativeJunctures.TryGetValue(
                juncture,
                out (long Ticks, int Count) preceding)
                ? (preceding.Ticks + beats, preceding.Count + tally)
                : (beats, tally);
        }
        if (s_publications % CumulativeEmitInterval is 0)
            WriteCumulative();
    }

    public static void WatchPagingBeat(
        in PagingWorkMeterCapture capture,
        int workerBacklog,
        int queuedCompletions)
    {
        ++s_beatPaneTally;
        s_beatPaneTotalMsec += capture.ElapsedMilliseconds;
        if (capture.ElapsedMilliseconds > s_beatPaneUpperMsec)
            s_beatPaneUpperMsec = capture.ElapsedMilliseconds;
        s_beatPaneYields += capture.YieldCount;
        s_beatPaneOps += capture.Operations;
        s_beatPaneAdmissions += capture.Used.CompletionAdmissions;
        if (capture.YieldCount > 0
            && capture.LastLimit != PagingWorkLimit.None
            && capture.LastStage is { } juncture)
        {
            string cause = $"{juncture}/{capture.LastLimit}";
            s_beatPaneYieldCauses[cause] =
                s_beatPaneYieldCauses.TryGetValue(cause, out int preceding)
                    ? preceding + 1
                    : 1;
        }
        s_previousWorkerBacklog = workerBacklog;
        s_previousQueuedCompletions = queuedCompletions;
    }

    public static void WritePagingBeatPane()
    {
        StringBuilder stroke = new StringBuilder(192);
        stroke.Append("[stream-tick] ticks=").Append(s_beatPaneTally)
            .Append(" sumMs=").Append(s_beatPaneTotalMsec.ToString("F1"))
            .Append(" maxMs=").Append(s_beatPaneUpperMsec.ToString("F1"))
            .Append(" ops=").Append(s_beatPaneOps)
            .Append(" yields=").Append(s_beatPaneYields)
            .Append(" admissions=").Append(s_beatPaneAdmissions)
            .Append(" workerBacklog=").Append(s_previousWorkerBacklog)
            .Append(" queuedCompletions=").Append(s_previousQueuedCompletions)
            .Append(" yieldReasons=");
        bool lead = true;
        foreach ((string cause, int tally) in
            s_beatPaneYieldCauses.OrderByDescending(
                static duo => duo.Value))
        {
            if (!lead)
                stroke.Append(',');
            lead = false;
            stroke.Append(cause).Append('x').Append(tally);
        }
        WritePagingBeatPaneRest(stroke, lead);
    }

    private static void WritePagingBeatPaneRest(StringBuilder stroke, bool lead)
    {
        if (lead)
            stroke.Append("none");
        Console.WriteLine(stroke.ToString());
        s_beatPaneTally = 0;
        WritePagingBeatPaneTail();
    }

    private static void WritePagingBeatPaneTail()
    {
        s_beatPaneTotalMsec = 0;
        s_beatPaneUpperMsec = 0;
        s_beatPaneYields = 0;
        WritePagingBeatPaneCoda();
    }

    private static void WritePagingBeatPaneCoda()
    {
        s_beatPaneOps = 0;
        s_beatPaneAdmissions = 0;
        s_beatPaneYieldCauses.Clear();
    }

    private static void WriteCumulative()
    {
        StringBuilder stroke = new StringBuilder(256);
        stroke.Append("[publish-timing] CUMULATIVE landblocks=")
            .Append(s_publications)
            .Append(" totalMs=")
            .Append(ToMsec(s_cumulativeBeats).ToString("F0"))
            .Append(" stages=");
        AffixJuncturesByPrice(stroke, s_cumulativeJunctures);
        Console.WriteLine(stroke.ToString());
    }

    private static void AffixJuncturesByPrice(
        StringBuilder stroke,
        IReadOnlyDictionary<string, (long Ticks, int Count)> junctures)
    {
        bool lead = true;
        foreach ((string juncture, (long beats, int tally)) in
            junctures.OrderByDescending(static duo => duo.Value.Ticks))
        {
            if (!lead)
                stroke.Append(',');
            lead = false;
            stroke.Append(TrimJunctureStem(juncture))
                .Append(':')
                .Append(ToMsec(beats).ToString("F2"))
                .Append('/')
                .Append(tally);
        }
    }

    // Every pipeline stage name starts with publication-; dropping the shared prefix keeps the
    // per-landblock line readable
    private static string TrimJunctureStem(string juncture)
    {
        return juncture.StartsWith("publication-", StringComparison.Ordinal)
            ? juncture["publication-".Length..]
            : juncture;
    }

    private static double ToMsec(long stopwatchBeats) =>
        stopwatchBeats * 1000.0 / Stopwatch.Frequency;
}
