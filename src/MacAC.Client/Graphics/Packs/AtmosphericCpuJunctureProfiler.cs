using System.Diagnostics;
using MacAC.Client.Telemetry;

namespace MacAC.Client.Graphics.Packs;

internal readonly record struct AtmosphericCpuJunctureCycle(
    long FrameSerial,
    long ShadowCasterBuildTicks,
    long ShadowEnvironmentTicks,
    long ShadowPreparedDrawsAndTransformsTicks,
    long ShadowFitAndUniformTicks,
    long ShadowLayeredPassRecordingTicks,
    long ShadowBookkeepingTicks,
    long PostSetupAndOtherTicks,
    long PostSunRaysTicks,
    long PostFilmicTicks);

internal readonly record struct RenderPackCpuStageTelemetry(
    string Stage,
    int SampleCount,
    double CpuMillisecondsP50,
    double CpuMillisecondsP95,
    double CpuMillisecondsP99);

internal sealed class AtmosphericCpuJunctureProfiler
{
    internal const int SpecimenIntervalCycles = AtmosphericGpuTickerSampling.LoIntervalCycles;

    private static readonly string[] JunctureLabels =
    [
        "target-preparation",
        "shadow-caster-build",
        "shadow-environment",
        "shadow-prepared-draws-and-transforms",
        "shadow-fit-and-uniform",
        "shadow-layered-pass-recording",
        "shadow-bookkeeping",
        "post-setup-and-other",
        "post-sun-rays",
        "post-filmic",
        "performance-observe-bookkeeping",
        "measured-pack-total",
        "measured-pack-unattributed",
    ];

    private readonly CycleStatsBuffer[] _microseconds;

    internal AtmosphericCpuJunctureProfiler(int cap = RasterizeBundlePerformancePane.DefaultCap)
    {
        _microseconds = new CycleStatsBuffer[JunctureLabels.Length];
        for (int idx = 0; idx < _microseconds.Length; ++idx)
            _microseconds[idx] = new CycleStatsBuffer(cap);
    }

    internal static bool ShouldMeasure(long cycleSerialNo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cycleSerialNo);
        return cycleSerialNo % SpecimenIntervalCycles is 0;
    }

    internal void Observe(
        in AtmosphericCpuJunctureCycle frame,
        long markPrepBeats,
        long measuredBundleSumBeats,
        long watchBookkeepingBeats)
    {
        if (frame.FrameSerial <= 0)
            throw new ArgumentOutOfRangeException(nameof(frame));
        ArgumentOutOfRangeException.ThrowIfNegative(markPrepBeats);
        ArgumentOutOfRangeException.ThrowIfNegative(measuredBundleSumBeats);
        ArgumentOutOfRangeException.ThrowIfNegative(watchBookkeepingBeats);

        long attributedBeats = checked(
            markPrepBeats
            + frame.ShadowCasterBuildTicks
            + frame.ShadowEnvironmentTicks
            + frame.ShadowPreparedDrawsAndTransformsTicks
            + frame.ShadowFitAndUniformTicks
            + frame.ShadowLayeredPassRecordingTicks
            + frame.ShadowBookkeepingTicks
            + frame.PostSetupAndOtherTicks
            + frame.PostSunRaysTicks
            + frame.PostFilmicTicks);
        long unattributedBeats = Math.Max(0L, measuredBundleSumBeats - attributedBeats);

        Push(0, markPrepBeats);
        Push(1, frame.ShadowCasterBuildTicks);
        Push(2, frame.ShadowEnvironmentTicks);
        Push(3, frame.ShadowPreparedDrawsAndTransformsTicks);
        Push(4, frame.ShadowFitAndUniformTicks);
        Push(5, frame.ShadowLayeredPassRecordingTicks);
        Push(6, frame.ShadowBookkeepingTicks);
        Push(7, frame.PostSetupAndOtherTicks);
        Push(8, frame.PostSunRaysTicks);
        Push(9, frame.PostFilmicTicks);
        Push(10, watchBookkeepingBeats);
        Push(11, measuredBundleSumBeats);
        Push(12, unattributedBeats);
    }

    internal IReadOnlyList<RenderPackCpuStageTelemetry> Freeze()
    {
        var outcome = new RenderPackCpuStageTelemetry[JunctureLabels.Length];
        for (int idx = 0; idx < outcome.Length; ++idx)
        {
            var specimens = _microseconds[idx];
            outcome[idx] = new RenderPackCpuStageTelemetry(
                JunctureLabels[idx],
                specimens.Count,
                specimens.Percentile(0.50) / 1000d,
                specimens.Percentile(0.95) / 1000d,
                specimens.Percentile(0.99) / 1000d);
        }
        return outcome;
    }

    internal void Reset()
    {
        for (int idx = 0; idx < _microseconds.Length; ++idx)
            _microseconds[idx].Reset();
    }

    private void Push(int juncture, long beats)
    {
        long microseconds = checked((long)Math.Round(
            beats * 1_000_000d / Stopwatch.Frequency,
            MidpointRounding.AwayFromZero));
        _microseconds[juncture].Push(microseconds);
    }
}

internal interface IAtmosphericCpuStageProfileEngine
{
    bool ShouldProfileCpuCycle(long cycleSerialNo);

    void ConcludeCpuProfile(
        long cycleSerialNo,
        long markPrepBeats,
        long measuredBundleSumBeats,
        long watchBookkeepingBeats,
        bool stableCycleBoundary);
}
