using MacAC.Client.Telemetry;

namespace MacAC.Client.Graphics.Packs;

internal readonly record struct RenderPackPerformanceCapture(
    int CpuSampleCount,
    int AbsoluteReceiverCpuSampleCount,
    int GpuSampleCount,
    double IncrementalCpuMillisecondsP50,
    double IncrementalCpuMillisecondsP95,
    double IncrementalCpuMillisecondsP99,
    double AbsoluteReceiverCpuMillisecondsP50,
    double AbsoluteReceiverCpuMillisecondsP95,
    double AbsoluteReceiverCpuMillisecondsP99,
    double InclusiveGpuMillisecondsP50,
    double InclusiveGpuMillisecondsP95,
    double InclusiveGpuMillisecondsP99,
    long ResidentGpuBytes,
    long TransientGpuBytes)
{
    internal bool HasStableAutoPane(int floorSpecimens)
    {
        return floorSpecimens > 0
        && CpuSampleCount >= floorSpecimens
        && GpuSampleCount >= floorSpecimens;
    }
}

internal readonly record struct RenderPackEnginePerformanceMetrics(
    long ResourceGeneration,
    bool HasResolvedGpuMeasurement,
    double InclusiveResolvedGpuMilliseconds,
    long RetainedGpuBytes,
    long TransientGpuBytes);

internal interface IRenderPackEnginePerformanceSource
{
    RenderPackEnginePerformanceMetrics GrabPerformanceMetrics();
}

internal readonly record struct RasterizeBundleCyclePerformanceObservation(
    double PackAddedCpuMilliseconds,
    bool StableFrameBoundary,
    int ViewportWidth,
    int ViewportHeight,
    int SampleCount,
    double AbsoluteEnhancedWorldReceiverCpuMilliseconds = 0d);

internal static class RasterizeBundlePerformanceAmbitLabels
{
    internal const string EnhancedRealmRecipient = "atmospheric-world-receiver";
}

internal sealed class RasterizeBundlePerformancePane
{
    internal const int DefaultCap = 2048;

    private readonly CycleStatsBuffer _cpuMicroseconds;
    private readonly CycleStatsBuffer _absoluteRecipientCpuMicroseconds;
    private readonly CycleStatsBuffer _gpuMicroseconds;
    private long _housedGpuOctets;
    private long _transientGpuOctets;

    internal RasterizeBundlePerformancePane(int capacity = DefaultCap)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _cpuMicroseconds = new CycleStatsBuffer(capacity);
        _absoluteRecipientCpuMicroseconds = new CycleStatsBuffer(capacity);
        _gpuMicroseconds = new CycleStatsBuffer(capacity);
    }

    internal void Observe(
        double incrementalCpuMilliseconds,
        double absoluteReceiverCpuMilliseconds,
        bool hasSettledGpuMeasurement,
        double inclusiveResolvedGpuMilliseconds,
        long residentGpuBytes,
        long transientGpuBytes)
    {
        if (!double.IsFinite(incrementalCpuMilliseconds) || incrementalCpuMilliseconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(incrementalCpuMilliseconds));
        if (!double.IsFinite(absoluteReceiverCpuMilliseconds)
            || absoluteReceiverCpuMilliseconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(absoluteReceiverCpuMilliseconds));
        }
        if (hasSettledGpuMeasurement
            && (!double.IsFinite(inclusiveResolvedGpuMilliseconds)
                || inclusiveResolvedGpuMilliseconds < 0d))
        {
            throw new ArgumentOutOfRangeException(nameof(inclusiveResolvedGpuMilliseconds));
        }
        if (residentGpuBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(residentGpuBytes));
        if (transientGpuBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(transientGpuBytes));

        _cpuMicroseconds.Push(ToMicroseconds(incrementalCpuMilliseconds));
        _absoluteRecipientCpuMicroseconds.Push(
            ToMicroseconds(absoluteReceiverCpuMilliseconds));
        if (hasSettledGpuMeasurement)
            _gpuMicroseconds.Push(ToMicroseconds(inclusiveResolvedGpuMilliseconds));
        _housedGpuOctets = residentGpuBytes;
        _transientGpuOctets = transientGpuBytes;
    }

    internal RenderPackPerformanceCapture Freeze()
    {
        return new(
        _cpuMicroseconds.Count,
        _absoluteRecipientCpuMicroseconds.Count,
        _gpuMicroseconds.Count,
        ToMillis(_cpuMicroseconds.Percentile(0.50)),
        ToMillis(_cpuMicroseconds.Percentile(0.95)),
        ToMillis(_cpuMicroseconds.Percentile(0.99)),
        ToMillis(_absoluteRecipientCpuMicroseconds.Percentile(0.50)),
        ToMillis(_absoluteRecipientCpuMicroseconds.Percentile(0.95)),
        ToMillis(_absoluteRecipientCpuMicroseconds.Percentile(0.99)),
        ToMillis(_gpuMicroseconds.Percentile(0.50)),
        ToMillis(_gpuMicroseconds.Percentile(0.95)),
        ToMillis(_gpuMicroseconds.Percentile(0.99)),
        _housedGpuOctets,
        _transientGpuOctets);
    }

    internal int FloorSpecimenTally
    {
        get
        {
            return Math.Min(
        _cpuMicroseconds.Count,
        Math.Min(
            _absoluteRecipientCpuMicroseconds.Count,
            _gpuMicroseconds.Count));
        }
    }

    internal void Reset()
    {
        _cpuMicroseconds.Reset();
        _absoluteRecipientCpuMicroseconds.Reset();
        _gpuMicroseconds.Reset();
        _housedGpuOctets = 0;
        _transientGpuOctets = 0;
    }

    private static long ToMicroseconds(double millis)
    {
        return checked((long)Math.Round(
            millis * 1000d,
            MidpointRounding.AwayFromZero));
    }

    private static double ToMillis(long microseconds) =>
        microseconds / 1000d;
}
