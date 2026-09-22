namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkGpuFrame : IGpuCycle
{
    private readonly ClientVulkanGpuDevice _device;
    private IGpuSweepCoder? _openPass;
    private bool _ended;

    internal VkGpuFrame(ClientVulkanGpuDevice device, int socketOrdinal, long serialNo)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        SocketOrdinal = socketOrdinal;
        SerialNo = serialNo;
    }

    public int SocketOrdinal { get; }

    public long SerialNo { get; }

    public GpuLoopAlloc ReserveLoop(int byteTally, GpuLoopPurpose usage) =>
        _device.ReserveLoop(SocketOrdinal, byteTally, usage);

    public void BroadcastHubDepotWrites(IClientGpuBuffer buf) =>
        _device.BroadcastHubDepotWrites(this, buf);

    public IGpuSweepCoder BeginPass(GpuPassSpec blurb)
    {
        ArgumentNullException.ThrowIfNull(blurb);
        if (_openPass is not null)
        {
            throw new InvalidOperationException(
                "A pass is by now open on this frame; dispose it prior to beginning another");
        }

        IGpuSweepCoder coder = _device.BeginPass(this, blurb);
        _openPass = coder;
        return coder;
    }

    internal void ShutPass(IGpuSweepCoder coder)
    {
        if (ReferenceEquals(_openPass, coder))
            _openPass = null;
    }

    public void End()
    {
        if (_ended)
            return;
        _ended = true;
        _device.EndFrame(this);
    }

    public void Dispose() => End();
}
