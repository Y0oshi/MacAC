using MacAC.Client.Telemetry;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkFrameGpuMeasurement : IRasterizeCycleGpuReading
{
    private readonly CycleProfiler _profiler;
    private readonly ClientVulkanGpuDevice _device;

    internal VkFrameGpuMeasurement(CycleProfiler profiler, ClientVulkanGpuDevice device)
    {
        _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public void BeginFrame()
    {
        _profiler.FrameBoundary();

        while (_device.TryGrabCycleGpuSpecimen(out int cycleOrdinal, out long passedUs))
            _profiler.CaptureGpuSpecimen(cycleOrdinal, passedUs);

        _device.CommenceCycleTickerAmbit(_profiler.LatestCycleOrdinal);
    }

    public void EndFrame() => _device.FinishCycleTickerAmbit();
}
