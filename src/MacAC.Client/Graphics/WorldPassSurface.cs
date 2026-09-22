using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

internal interface IRenderFrameGlLedger
{
    void ReinstateCycleDefaults();
}
internal interface IRealmPassSurface
{
    void StageClipCycle();

    void ActivateClipGaps();

    void SwitchOffClipGaps();

    void ClearInteriorZDepth();
}

internal sealed class RhiRealmPassSurface(
    IRealmPassScope scope,
    ILatestGpuCycleOrigin frames,
    ClipCycle clipFrame) : IRealmPassSurface
{
    private readonly IRealmPassScope _ambit = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly ILatestGpuCycleOrigin _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
    private readonly ClipCycle _clipCycle = clipFrame ?? throw new ArgumentNullException(nameof(clipFrame));

    public void StageClipCycle()
    {
        _ambit.Sections.ClipZones = Publish(
            _clipCycle.ZoneOctets,
            GpuLoopPurpose.Storage);
    }

    public void ActivateClipGaps()
    {
    }

    public void SwitchOffClipGaps()
    {
    }

    public void ClearInteriorZDepth() => _ambit.WipeInteriorZDepth();

    private GpuBufferSegment Publish(ReadOnlySpan<byte> blob, GpuLoopPurpose usage)
    {
        IGpuCycle cycle = _cycles.LatestCycle
            ?? throw new InvalidOperationException(
                "The world clip frame needs an open IGpuCycle (see GpuDeviceCycleLifespan)");
        int byteTally = Math.Max(blob.Length, ClipCycle.ChamberClipStrideOctets);
        var alloc = cycle.ReserveLoop(byteTally, usage);
        alloc.Data.Clear();
        if (!blob.IsEmpty)
            blob.CopyTo(alloc.Data);
        return new GpuBufferSegment(
            alloc.Buffer,
            alloc.ShiftOctets,
            (uint)byteTally);
    }
}

internal sealed class NullRenderFrameGlLedger : IRenderFrameGlLedger
{
    public static NullRenderFrameGlLedger Instance { get; } = new();

    private NullRenderFrameGlLedger()
    {
    }

    public void ReinstateCycleDefaults()
    {
    }
}
