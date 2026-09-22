using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

internal interface ILatestGpuCycleOrigin
{
    IGpuCycle? LatestCycle { get; }
}

internal sealed class GpuDeviceCycleLifespan(IClientGpuDevice device) : IRasterizeCycleLifespan, ILatestGpuCycleOrigin
{
    private readonly IClientGpuDevice _device = device ?? throw new ArgumentNullException(nameof(device));

    public IGpuCycle? LatestCycle { get; private set; }

    public void BeginFrame() => LatestCycle = _device.BeginFrame();

    public void EndFrame()
    {
        IGpuCycle? cycle = LatestCycle;
        LatestCycle = null;
        cycle?.End();
    }
}
