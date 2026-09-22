using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Illumination;

namespace MacAC.Client.Graphics;

internal readonly record struct GpuBufferSegment(
    IClientGpuBuffer? Buffer,
    uint OffsetBytes,
    uint SizeBytes)
{
    public bool IsValid => Buffer is not null && SizeBytes > 0;
}

internal sealed class RealmFrameSections
{
    // Set 1 binding 1 - SceneLighting
    public GpuBufferSegment SceneLighting { get; set; }

    public GpuBufferSegment ClipZones { get; set; }

    public void Reset()
    {
        SceneLighting = default;
        ClipZones = default;
    }
}

internal interface IRealmPassScope
{
    int SampleCount { get; }

    // The open encoder, or null outside the world phase
    IGpuSweepCoder? LatestCoder { get; }

    IGpuSweepCoder DemandCoder();

    int AffixWidth { get; }

    int AffixHeight { get; }

    void WipeInteriorZDepth();

    // The frame-global sections every renderer in this pass rebinds
    RealmFrameSections Sections { get; }

    IDisposable Publish(IGpuSweepCoder coder);
}

internal static class RealmFrameSectionWiring
{
    internal static void AttachTableauIllumination(
        IGpuSweepCoder coder,
        RealmFrameSections sections,
        IGpuCycle cycle)
    {
        var section = sections.SceneLighting;
        if (!section.IsValid)
        {
            section = Zeroed(
                cycle,
                SceneLightBlock.SizeInBytes,
                GpuLoopPurpose.Uniform);
        }

        coder.AttachUniformBuf(
            (uint)SceneLightBlock.MappingPt,
            section.Buffer!,
            section.OffsetBytes,
            section.SizeBytes);
    }

    internal static void AttachClipZones(
        IGpuSweepCoder coder,
        RealmFrameSections sections,
        IGpuCycle cycle)
    {
        var section = sections.ClipZones;
        if (!section.IsValid)
        {
            section = Zeroed(
                cycle,
                ClipCycle.ChamberClipStrideOctets,
                GpuLoopPurpose.Storage);
        }

        coder.AttachDepotBuf(
            GpuBindingModel.DepotClipZones,
            section.Buffer!,
            section.OffsetBytes,
            section.SizeBytes);
    }

    private static GpuBufferSegment Zeroed(
        IGpuCycle cycle,
        int byteTally,
        GpuLoopPurpose usage)
    {
        var alloc = cycle.ReserveLoop(byteTally, usage);
        alloc.Data.Clear();
        return new GpuBufferSegment(
            alloc.Buffer,
            alloc.ShiftOctets,
            (uint)byteTally);
    }
}
