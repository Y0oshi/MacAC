using System.Diagnostics;
using System.Globalization;
using System.Text;
using MacAC.Mechanics.Drawing;

namespace MacAC.Client.Telemetry;

public enum CycleJuncture
{
    Update = 0,
    /// <summary>RealmMeshAdapter.Tick - staged mesh/texture GPU upload drain.</summary>
    Upload = 1,
    ImGui = 2,
    Pacing = 3,
}

internal readonly record struct CycleHistoryCapture(
    int FrameIndex,
    double TimestampMs,
    long CpuUs,
    long GpuUs,
    long AllocBytes,
    long UpdateUs,
    long UploadUs,
    long ImGuiUs,
    long PacingUs);

public sealed class CycleProfiler : IDisposable
{
    private const int PaneCap = 2048;              // ~12 s at 165 fps
    private const int HistoryStartingCap = 131072;
    private const long DossierIntervalBeats = 5 * TimeSpan.TicksPerSecond;
    private static readonly int JunctureTally = Enum.GetValues<CycleJuncture>().Length;

    private readonly CycleStatsBuffer _cpuUs = new(PaneCap);
    private readonly CycleStatsBuffer _gpuUs = new(PaneCap);
    private readonly CycleStatsBuffer _allocOctets = new(PaneCap);
    private readonly CycleStatsBuffer[] _junctureUs;
    private readonly long[] _junctureAccumBeats;
    private readonly long[] _previousJunctureUs;
    private readonly List<CycleHistoryCapture>? _history;
    private readonly long _profilerBeginStamp;
    private readonly DateTime _profilerBeginUtc;
    private bool _externalGpuEngaged;
    private long _previousBoundaryStamp;
    private long _previousAllocOctets;
    private long _previousDossierBeats;
    private int _gc0Base, _gc1Base, _gc2Base;
    private int _cyclesInPane;
    private int _holderThreadIdent;
    private bool _threadWarned;
    private bool _wasTurnedOn;

    public string? LastReport { get; private set; }

    public int LatestCycleOrdinal { get; private set; } = -1;

    public CycleProfiler()
    {
        _junctureUs = new CycleStatsBuffer[JunctureTally];
        for (int idx = 0; idx < JunctureTally; ++idx) _junctureUs[idx] = new CycleStatsBuffer(PaneCap);
        _junctureAccumBeats = new long[JunctureTally];
        _previousJunctureUs = new long[JunctureTally];
        _profilerBeginStamp = Stopwatch.GetTimestamp();
        _profilerBeginUtc = DateTime.UtcNow;
        if (DrawTelemetry.CycleHistoryTrail is not null)
            _history = new List<CycleHistoryCapture>(HistoryStartingCap);
    }

    public void FrameBoundary()
    {
        bool turnedOn = DrawTelemetry.CycleProfTurnedOn;
        if (!turnedOn)
        {
            if (_wasTurnedOn)
            {
                _wasTurnedOn = false;
                _previousBoundaryStamp = 0;
                LatestCycleOrdinal = -1;
            }
            return;
        }

        if (_holderThreadIdent is 0) _holderThreadIdent = Environment.CurrentManagedThreadId;
        else if (!_threadWarned && _holderThreadIdent != Environment.CurrentManagedThreadId)
        {
            _threadWarned = true;
            Console.WriteLine("[frame-prof] WARNING: frame boundary crossed threads; alloc counter is per-thread and now unreliable");
        }

        long instant = Stopwatch.GetTimestamp();
        long allocInstant = GC.GetAllocatedBytesForCurrentThread();

        if (_wasTurnedOn)
        {
            long cpuUs = (instant - _previousBoundaryStamp) * 1_000_000L / Stopwatch.Frequency;
            _cpuUs.Push(cpuUs);
            long allocDiff = allocInstant - _previousAllocOctets;
            _allocOctets.Push(allocDiff);
            for (int idx = 0; idx < JunctureTally; ++idx)
            {
                long junctureUs = _junctureAccumBeats[idx] * 1_000_000L / Stopwatch.Frequency;
                _junctureUs[idx].Push(junctureUs);
                _previousJunctureUs[idx] = junctureUs;
                _junctureAccumBeats[idx] = 0;
            }
            ++_cyclesInPane;
            _history?.Add(new CycleHistoryCapture(
                    LatestCycleOrdinal,
                    (instant - _profilerBeginStamp) * 1000.0 / Stopwatch.Frequency,
                    cpuUs,
                    -1L,
                    allocDiff,
                    _previousJunctureUs[(int)CycleJuncture.Update],
                    _previousJunctureUs[(int)CycleJuncture.Upload],
                    _previousJunctureUs[(int)CycleJuncture.ImGui],
                    _previousJunctureUs[(int)CycleJuncture.Pacing]));
            ++LatestCycleOrdinal;
        }
        else
        {
            _wasTurnedOn = true;
            _previousDossierBeats = DateTime.UtcNow.Ticks;
            Array.Clear(_junctureAccumBeats);
            _gc0Base = GC.CollectionCount(0); _gc1Base = GC.CollectionCount(1); _gc2Base = GC.CollectionCount(2);
            LatestCycleOrdinal = 0;
        }

        _previousBoundaryStamp = instant;
        _previousAllocOctets = allocInstant;

        long instantBeats = DateTime.UtcNow.Ticks;
        if (instantBeats - _previousDossierBeats >= DossierIntervalBeats && _cyclesInPane > 0)
        {
            int gc0 = GC.CollectionCount(0) - _gc0Base;
            int gc1 = GC.CollectionCount(1) - _gc1Base;
            int gc2 = GC.CollectionCount(2) - _gc2Base;
            LastReport = ComposeDossier(_cyclesInPane, _cpuUs, _gpuUs,
                gpuEngaged: _externalGpuEngaged,
                _allocOctets, gc0, gc1, gc2, _junctureUs);
            Console.WriteLine(LastReport);
            _previousDossierBeats = instantBeats;
            _gc0Base += gc0; _gc1Base += gc1; _gc2Base += gc2;
            _cyclesInPane = 0;
            _cpuUs.Reset(); _gpuUs.Reset(); _allocOctets.Reset();
            for (int idx = 0; idx < JunctureTally; ++idx) _junctureUs[idx].Reset();
        }
    }

    public void CaptureGpuSpecimen(int cycleOrdinal, long passedUs)
    {
        if (!_wasTurnedOn)
            return;

        _externalGpuEngaged = true;
        _gpuUs.Push(passedUs);
        if (_history is not null && (uint)cycleOrdinal < (uint)_history.Count)
        {
            var rank = _history[cycleOrdinal];
            _history[cycleOrdinal] = rank with { GpuUs = passedUs };
        }
    }

    public JunctureAmbit BeginStage(CycleJuncture juncture)
    {
        return DrawTelemetry.CycleProfTurnedOn
                ? new JunctureAmbit(this, juncture, Stopwatch.GetTimestamp())
                : default;
    }

    public static string ComposeDossier(
        int cycleTally,
        CycleStatsBuffer cpu, CycleStatsBuffer gpu, bool gpuEngaged,
        CycleStatsBuffer alloc, int gc0, int gc1, int gc2,
        CycleStatsBuffer[] junctures)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        StringBuilder builder = new StringBuilder(256);
        builder.Append("[frame-prof] n=").Append(cycleTally);
        builder.AppendFormat(info, " | cpu_ms p50={0:0.0} p95={1:0.0} p99={2:0.0} max={3:0.0}",
            cpu.Percentile(0.50) / 1000.0, cpu.Percentile(0.95) / 1000.0,
            cpu.Percentile(0.99) / 1000.0, cpu.Max() / 1000.0);
        if (gpuEngaged)
            builder.AppendFormat(info, " | gpu_ms p50={0:0.0} p95={1:0.0}",
                gpu.Percentile(0.50) / 1000.0, gpu.Percentile(0.95) / 1000.0);
        else
            builder.Append(" | gpu=off(wbdiag)");
        builder.AppendFormat(info, " | alloc_kb p50={0:0.0} max={1:0.0} gc={2}/{3}/{4}",
            alloc.Percentile(0.50) / 1024.0, alloc.Max() / 1024.0, gc0, gc1, gc2);
        string[] labels = ["upd", "upl", "imgui", "pace"];
        for (int idx = 0; idx < junctures.Length && idx < labels.Length; ++idx)
            builder.AppendFormat(info, " | {0} p50={1:0.0} p95={2:0.0}",
                labels[idx], junctures[idx].Percentile(0.50) / 1000.0, junctures[idx].Percentile(0.95) / 1000.0);
        return builder.ToString();
    }

    public void Dispose()
    {
        if (_history is { Count: > 0 } && DrawTelemetry.CycleHistoryTrail is { } trail)
        {
            try
            {
                using StreamWriter writer = new StreamWriter(trail, append: false);
                EmitHistoryCsv(_history, writer, _profilerBeginUtc);
                Console.WriteLine($"[frame-prof] wrote {_history.Count} history record(s) to '{trail}'");
            }
            catch (Exception exc)
            {
                Console.WriteLine($"[frame-prof] WARNING: could not write frame history to '{trail}': {exc.Message}");
            }
        }
    }

    internal void FinishJuncture(CycleJuncture juncture, long beginStamp)
    {
        _junctureAccumBeats[(int)juncture] += Stopwatch.GetTimestamp() - beginStamp;
    }

    internal static void EmitHistoryCsv(
        IEnumerable<CycleHistoryCapture> records,
        TextWriter writer,
        DateTime profilerBeginUtc)
    {
        CultureInfo info = CultureInfo.InvariantCulture;
        DateTime beginUtc = profilerBeginUtc.ToUniversalTime();
        writer.WriteLine(
            "frame,timestamp_ms,timestamp_utc,cpu_us,gpu_us,alloc_bytes,"
            + "update_us,upload_us,imgui_us,pacing_us");
        foreach (CycleHistoryCapture r in records)
        {
            writer.Write(r.FrameIndex.ToString(info)); writer.Write(',');
            writer.Write(r.TimestampMs.ToString("0.000", info)); writer.Write(',');
            writer.Write(beginUtc.AddMilliseconds(r.TimestampMs).ToString("O", info));
            writer.Write(',');
            writer.Write(r.CpuUs.ToString(info)); writer.Write(',');
            writer.Write(r.GpuUs.ToString(info)); writer.Write(',');
            writer.Write(r.AllocBytes.ToString(info)); writer.Write(',');
            writer.Write(r.UpdateUs.ToString(info)); writer.Write(',');
            writer.Write(r.UploadUs.ToString(info)); writer.Write(',');
            writer.Write(r.ImGuiUs.ToString(info)); writer.Write(',');
            writer.WriteLine(r.PacingUs.ToString(info));
        }
    }
}

public readonly struct JunctureAmbit : IDisposable
{
    private readonly CycleProfiler? _holder;
    private readonly CycleJuncture _juncture;
    private readonly long _begin;

    internal JunctureAmbit(CycleProfiler holder, CycleJuncture juncture, long begin)
    {
        _holder = holder; _juncture = juncture; _begin = begin;
    }

    public void Dispose() => _holder?.FinishJuncture(_juncture, _begin);
}
