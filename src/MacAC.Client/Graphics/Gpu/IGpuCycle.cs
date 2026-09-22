using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics.Gpu;

internal readonly ref struct GpuLoopAlloc
{
    public GpuLoopAlloc(IClientGpuBuffer buf, uint shiftOctets, Span<byte> blob)
    {
        Buffer = buf;
        ShiftOctets = shiftOctets;
        Data = blob;
    }

    // The ring buffer to bind
    public IClientGpuBuffer Buffer { get; }

    public uint ShiftOctets { get; }

    // CPU-writable memory for this allocation
    public Span<byte> Data { get; }

    public bool IsEmpty => Data.IsEmpty;

    public Span<T> AsSpan<T>() where T : unmanaged => MemoryMarshal.Cast<byte, T>(Data);
}

internal interface IGpuCycle : IDisposable
{
    // Frames-in-flight slot index this frame occupies
    int SocketOrdinal { get; }

    // Monotonic frame serial
    long SerialNo { get; }

    GpuLoopAlloc ReserveLoop(int byteTally, GpuLoopPurpose usage);

    void BroadcastHubDepotWrites(IClientGpuBuffer buf);

    // Opens a rendering pass
    IGpuSweepCoder BeginPass(GpuPassSpec blurb);

    void End();
}
