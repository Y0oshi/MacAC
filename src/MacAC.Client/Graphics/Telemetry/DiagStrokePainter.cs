using System.Numerics;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

public sealed class DiagStrokePainter : IDisposable
{
    internal const int FloatsPerVert = 6;
    private const int VertStrideOctets = FloatsPerVert * sizeof(float);

    internal static readonly GpuVertexArrangement VertArrangement = GpuVertexArrangement.Interleaved(
        strideOctets: VertStrideOctets,
        [
            new GpuVertexAttribute(0, GpuVertFmt.Float3, 0),
            new GpuVertexAttribute(1, GpuVertFmt.Float3, 12),
        ]);

    private readonly ILatestGpuCycleOrigin _cycleSrc;
    private readonly IGpuPipe _pipe;

    private readonly List<float> _buf = new(4096);
    private int _vertTally;

    internal DiagStrokePainter(IClientGpuDevice dev, ILatestGpuCycleOrigin frameSource, string shaderDirection)
    {
        ArgumentNullException.ThrowIfNull(dev);
        _cycleSrc = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderDirection);

        _pipe = dev.BuildPipe(new GpuPipeSpec
        {
            Name = "debug-line",
            Shaders = new GpuShaderGroup("wire_lines"),
            VertArrangement = VertArrangement,
            Wiring = GpuPrimitiveWiring.LineList,
            Blend = GpuBlendManner.None,
            Depth = GpuDepthLedger.Disabled,
            Cull = GpuPruneManner.None,
            AlphaToCoverage = false,
            TintEmit = true,
            SampleCount = 1,
        });
    }

    public void Begin()
    {
        _buf.Clear();
        _vertTally = 0;
    }

    public void AppendStroke(Vector3 a, Vector3 b, Vector3 tint)
    {
        _buf.Add(a.X); _buf.Add(a.Y); _buf.Add(a.Z);
        _buf.Add(tint.X); _buf.Add(tint.Y); _buf.Add(tint.Z);
        _buf.Add(b.X); _buf.Add(b.Y); _buf.Add(b.Z);
        _buf.Add(tint.X); _buf.Add(tint.Y); _buf.Add(tint.Z);
        _vertTally += 2;
    }

    public void AppendCylinder(Vector3 baseSpot, float radius, float height, Vector3 tint)
    {
        const int segments = 16;
        Vector3 top = baseSpot + new Vector3(0, 0, height);

        Vector3[] baseLoop = new Vector3[segments];
        Vector3[] topLoop = new Vector3[segments];
        for (int i = 0; i < segments; ++i)
        {
            float theta = i * (MathF.PI * 2f / segments);
            float cx = MathF.Cos(theta) * radius;
            float cy = MathF.Sin(theta) * radius;
            baseLoop[i] = new Vector3(baseSpot.X + cx, baseSpot.Y + cy, baseSpot.Z);
            topLoop[i] = new Vector3(top.X + cx, top.Y + cy, top.Z);
        }

        // Base ring
        for (int i = 0; i < segments; ++i)
            AppendStroke(baseLoop[i], baseLoop[(i + 1) % segments], tint);
        // Top ring
        for (int i = 0; i < segments; ++i)
            AppendStroke(topLoop[i], topLoop[(i + 1) % segments], tint);
        for (int i = 0; i < 4; ++i)
        {
            int index = i * (segments / 4);
            AppendStroke(baseLoop[index], topLoop[index], tint);
        }
    }

    /// <summary>Draw an axis-aligned box as 12 edges.</summary>
    public void AppendBbox(Vector3 lower, Vector3 upper, Vector3 tint)
    {
        Vector3[] c =
        [
            new(lower.X, lower.Y, lower.Z),
            new(upper.X, lower.Y, lower.Z),
            new(upper.X, upper.Y, lower.Z),
            new(lower.X, upper.Y, lower.Z),
            new(lower.X, lower.Y, upper.Z),
            new(upper.X, lower.Y, upper.Z),
            new(upper.X, upper.Y, upper.Z),
            new(lower.X, upper.Y, upper.Z),
        ];
        // Bottom
        AppendStroke(c[0], c[1], tint); AppendStroke(c[1], c[2], tint);
        AppendStroke(c[2], c[3], tint); AppendStroke(c[3], c[0], tint);
        // Top
        AppendStroke(c[4], c[5], tint); AppendStroke(c[5], c[6], tint);
        AppendStroke(c[6], c[7], tint); AppendStroke(c[7], c[4], tint);
        // Verticals
        AppendStroke(c[0], c[4], tint); AppendStroke(c[1], c[5], tint);
        AppendStroke(c[2], c[6], tint); AppendStroke(c[3], c[7], tint);
    }

    /// <summary>Upload + draw all accumulated lines.</summary>
    public void Flush(Matrix4x4 lens, Matrix4x4 proj)
    {
        if (_vertTally is 0) return;

        IGpuCycle cycle = _cycleSrc.LatestCycle
            ?? throw new InvalidOperationException(
                "DebugLineRenderer.Flush needs an open IGpuCycle (see GpuDeviceCycleLifespan) - " +
                "the host must drive IGpuDevice.BeginFrame() prior to rendering debug lines");

        using var coder = cycle.BeginPass(new GpuPassSpec
        {
            Name = "debug-line",
            Color = new GpuTintAffix(
                Target: null,
                Load: GpuPullOp.Load,
                Store: GpuVaultOp.Store,
                ClearColor: default),
            ZDepth = null,
            SampleCount = 1,
        });
        coder.BindPipeline(_pipe);

        var constants = GpuShoveConstants.Default;
        constants.LensMirror = lens * proj;
        coder.AssignPushConstants(constants);

        int byteTally = _buf.Count * sizeof(float);
        var alloc = cycle.ReserveLoop(byteTally, GpuLoopPurpose.Vertex);
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buf).CopyTo(alloc.AsSpan<float>());
        coder.AttachVertBuf(0, alloc.Buffer, alloc.ShiftOctets);
        coder.Draw((uint)_vertTally, 1, 0, 0);
    }

    public void Dispose() => _pipe.Dispose();
}
