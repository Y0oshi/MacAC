using System.Numerics;

namespace MacAC.Client.Graphics.Gpu;

internal readonly record struct GpuTintAffix(
    IGpuRasterizeMark? Target,
    GpuPullOp Load,
    GpuVaultOp Store,
    Vector4 ClearColor);

internal readonly record struct GpuZDepthAffix(
    GpuPullOp Load,
    GpuVaultOp Store,
    float ClearDepth,
    uint ClearStencil,
    IGpuDirectedZDepthMark? DirectionalTarget = null,
    int Layer = 0);

internal sealed record GpuPassSpec
{
    public required string Name { get; init; }

    public required GpuTintAffix Color { get; init; }

    // False only for dedicated depth-only producers such as directional shadow maps
    public bool HasTintAffix { get; init; } = true;

    // Depth/stencil attachment, or null for 2-D passes that need no depth
    public GpuZDepthAffix? ZDepth { get; init; }

    public int SampleCount { get; init; } = 1;

    // Non-zero Vulkan multiview mask
    public uint LensBitmask { get; init; }

    public static GpuPassSpec BackbufferWipe(string label, Vector4 wipeTint, int specimenTally)
    {
        return new()
        {
            Name = label,
            Color = new GpuTintAffix(
            Target: null,
            Load: GpuPullOp.Clear,
            Store: specimenTally > 1 ? GpuVaultOp.Resolve : GpuVaultOp.Store,
            ClearColor: wipeTint),
            ZDepth = new GpuZDepthAffix(
            Load: GpuPullOp.Clear,
            Store: GpuVaultOp.DontCare,
            ClearDepth: 1f,
            ClearStencil: 0),
            SampleCount = specimenTally,
        };
    }

    public static GpuPassSpec DirectedZDepth(
        string label,
        IGpuDirectedZDepthMark mark,
        int stratum)
    {
        return new()
        {
            Name = label,
            Color = default,
            HasTintAffix = false,
            ZDepth = new GpuZDepthAffix(
            Load: GpuPullOp.Clear,
            Store: GpuVaultOp.Store,
            ClearDepth: 1f,
            ClearStencil: 0,
            DirectionalTarget: mark,
            Layer: stratum),
            SampleCount = 1,
        };
    }

    public static GpuPassSpec DirectedZDepthMultiview(
        string label,
        IGpuDirectedZDepthMark mark,
        uint lensBitmask)
    {
        return new()
        {
            Name = label,
            Color = default,
            HasTintAffix = false,
            ZDepth = new GpuZDepthAffix(
            GpuPullOp.Clear,
            GpuVaultOp.Store,
            1f,
            0,
            mark,
            Layer: 0),
            SampleCount = 1,
            LensBitmask = lensBitmask,
        };
    }
}
