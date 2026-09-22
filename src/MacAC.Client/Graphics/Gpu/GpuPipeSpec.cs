using System.Collections.Immutable;

namespace MacAC.Client.Graphics.Gpu;

internal enum GpuVertFmt
{
    Float1,
    Float2,
    Float3,
    Float4,

    // Four unsigned bytes scaled to [0,1] floats - a shader vec4 input
    UByte4Normalized,

    UByte4UInt,

    UInt1,
}

internal enum GpuVertFeedRate
{
    // The binding advances once per vertex - the default for every layout written before V6l
    Vertex,

    // The binding advances once per instance (GL divisor 1)
    Instance,
}

internal readonly record struct GpuVertexWiring(
    uint Binding,
    uint StrideBytes,
    GpuVertFeedRate InputRate = GpuVertFeedRate.Vertex);

internal readonly record struct GpuVertexAttribute(
    uint Location,
    GpuVertFmt Format,
    uint OffsetBytes,
    uint Binding = 0);

internal sealed record GpuVertexArrangement(
    ImmutableArray<GpuVertexWiring> Bindings,
    ImmutableArray<GpuVertexAttribute> Attributes)
{
    public static GpuVertexArrangement Interleaved(
        uint strideOctets,
        ImmutableArray<GpuVertexAttribute> attrs)
    {
        return new(
            [new GpuVertexWiring(0, strideOctets, GpuVertFeedRate.Vertex)],
            attrs);
    }

    public uint StrideOctets =>
        Bindings.IsDefaultOrEmpty ? 0u : Bindings[0].StrideBytes;

    public uint StrideOf(uint binding)
    {
        foreach (GpuVertexWiring contender in Bindings)
        {
            if (contender.Binding == binding)
                return contender.StrideBytes;
        }

        throw new ArgumentOutOfRangeException(
            nameof(binding),
            binding,
            "The vertex layout declares no such binding");
    }

    // How often binding advances
    public GpuVertFeedRate FeedRateOf(uint binding)
    {
        foreach (GpuVertexWiring contender in Bindings)
        {
            if (contender.Binding == binding)
                return contender.InputRate;
        }

        throw new ArgumentOutOfRangeException(
            nameof(binding),
            binding,
            "The vertex layout declares no such binding");
    }

    public static GpuVertexArrangement RealmTriMesh { get; } = Interleaved(
        strideOctets: 32,
        [
            new GpuVertexAttribute(0, GpuVertFmt.Float3, 0),
            new GpuVertexAttribute(1, GpuVertFmt.Float3, 12),
            new GpuVertexAttribute(2, GpuVertFmt.Float2, 24),
        ]);

    public static GpuVertexArrangement None { get; } = new([], []);
}

internal readonly record struct GpuShaderGroup
{
    internal GpuShaderGroup(string label)
        : this(label, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty)
    {
    }

    internal GpuShaderGroup(
        string label,
        ReadOnlyMemory<byte> vertSpirv,
        ReadOnlyMemory<byte> fragmentSpirv)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (vertSpirv.IsEmpty != fragmentSpirv.IsEmpty)
            throw new ArgumentException("Both SPIR-V stages has to be supplied together");
        Name = label;
        VertSpirv = vertSpirv;
        FragmentSpirv = fragmentSpirv;
    }

    internal string Name { get; }

    internal ReadOnlyMemory<byte> VertSpirv { get; }

    internal ReadOnlyMemory<byte> FragmentSpirv { get; }

    internal bool HasEmbeddedSpirv => !VertSpirv.IsEmpty;
}

internal readonly record struct GpuDepthLedger(bool Test, bool Write, GpuContrastOp Compare)
{
    // Standard opaque geometry: test and write, nearer wins
    public static GpuDepthLedger SolidDefault { get; } = new(Test: true, Write: true, GpuContrastOp.LessOrEqual);

    // Translucent geometry: test against existing depth but do not occlude later draws
    public static GpuDepthLedger TranslucentDefault { get; } = new(Test: true, Write: false, GpuContrastOp.LessOrEqual);

    // Sky and 2-D overlays: depth is irrelevant
    public static GpuDepthLedger Disabled { get; } = new(Test: false, Write: false, GpuContrastOp.Always);
}

internal readonly record struct GpuStencilLedger(
    GpuContrastOp Compare,
    ClientGpuStencilOp Fail,
    ClientGpuStencilOp DepthFail,
    ClientGpuStencilOp Pass,
    uint Reference,
    uint CompareMask,
    uint WriteMask)
{
    // GL's and Vulkan's own defaults: always pass, never write
    public static GpuStencilLedger Default { get; } = new(
        GpuContrastOp.Always,
        ClientGpuStencilOp.Keep,
        ClientGpuStencilOp.Keep,
        ClientGpuStencilOp.Keep,
        Reference: 0,
        CompareMask: 0xFF,
        WriteMask: 0xFF);
}

internal sealed record GpuPipeSpec
{
    public uint LensMask { get; init; }
    // Stable identifier, e.g. "mesh-opaque".
    public required string Name { get; init; }

    // The GLSL pair this pipeline draws with
    public required GpuShaderGroup Shaders { get; init; }

    public required GpuVertexArrangement VertArrangement { get; init; }

    public GpuPrimitiveWiring Wiring { get; init; } = GpuPrimitiveWiring.TriangleList;

    public GpuBlendManner Blend { get; init; } = GpuBlendManner.None;

    public GpuDepthLedger Depth { get; init; } = GpuDepthLedger.SolidDefault;

    public GpuPruneManner Cull { get; init; } = GpuPruneManner.Back;

    public GpuFrontFacet FrontFace { get; init; } = GpuFrontFacet.CounterClockwise;

    public bool AlphaToCoverage { get; init; }

    public bool TintEmit { get; init; } = true;

    public bool HasTintAttachment { get; init; } = true;

    public bool StencilTest { get; init; }

    public GpuStencilLedger Stencil { get; init; } = GpuStencilLedger.Default;

    public GpuBitmapFmt TintFmt { get; init; } = GpuBitmapFmt.Rgba8UnormRenderTarget;

    public bool AllowTintFmtVariants { get; init; } = true;

    public bool UsesRasterizeBundleShaderAbi { get; init; }

    public int SampleCount { get; init; } = 1;
}
