using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Illumination;

namespace MacAC.Client.Graphics;

public sealed unsafe class StageLightingUboWiring : IDisposable
{
    private readonly ILatestGpuCycleOrigin _cycles;
    private readonly RealmFrameSections _sections;
    private bool _cycleBegun;

    internal int DynamicBufTally => 0;

    internal StageLightingUboWiring(
        ILatestGpuCycleOrigin frames,
        RealmFrameSections sections)
    {
        _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
        _sections = sections ?? throw new ArgumentNullException(nameof(sections));
    }

    public void BeginFrame(int cycleSocket)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cycleSocket);
        _cycleBegun = true;
    }

    public void Upload(SceneLightBlock blob)
    {
        if (!_cycleBegun)
            throw new InvalidOperationException("BeginFrame has to be called prior to uploading scene lighting");

        IGpuCycle cycle = _cycles.LatestCycle
            ?? throw new InvalidOperationException(
                "Scene lighting needs an open IGpuCycle (see GpuDeviceCycleLifespan)");
        var alloc = cycle.ReserveLoop(
            SceneLightBlock.SizeInBytes,
            GpuLoopPurpose.Uniform);
        new ReadOnlySpan<byte>(&blob, SceneLightBlock.SizeInBytes)
            .CopyTo(alloc.Data);
        _sections.SceneLighting = new GpuBufferSegment(
            alloc.Buffer,
            alloc.ShiftOctets,
            (uint)SceneLightBlock.SizeInBytes);
    }

    public void Dispose()
    {
    }
}
