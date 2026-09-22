using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

public sealed partial class GatewayZDepthBitmaskPainter
{
    private readonly IClientGpuDevice? _device;
    private readonly ILatestGpuCycleOrigin? _cycles;
    private readonly IRealmPassScope? _ambit;
    private IGpuPipe? _zDepthEmitPipe;
    private bool _rhiCycleBegun;

    // One position per vertex - the only attribute portal_depth.vert reads
    internal static GpuVertexArrangement GatewayVertArrangement { get; } = GpuVertexArrangement.Interleaved(
        strideOctets: 3 * sizeof(float),
        [new GpuVertexAttribute(0, GpuVertFmt.Float3, 0)]);

    internal GatewayZDepthBitmaskPainter(
        IClientGpuDevice device,
        ILatestGpuCycleOrigin frames,
        IRealmPassScope scope)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
        _ambit = scope ?? throw new ArgumentNullException(nameof(scope));
        _assetList = new AssetTidyCluster();

        try
        {
            int specimens = scope.SampleCount;
            _zDepthEmitPipe = BuildGatewayPipe(
                device,
                "portal-depth-write",
                GpuContrastOp.Always,
                zDepthEmit: true,
                stencilTest: false,
                GpuStencilLedger.Default,
                specimens);
        }
        catch
        {
            TeardownRhiAssetList();
            throw;
        }
    }

    private static IGpuPipe BuildGatewayPipe(
        IClientGpuDevice dev,
        string label,
        GpuContrastOp zDepthContrast,
        bool zDepthEmit,
        bool stencilTest,
        GpuStencilLedger stencil,
        int specimenTally)
    {
        return dev.BuildPipe(new GpuPipeSpec
        {
            Name = label,
            Shaders = new GpuShaderGroup("gateway_depth"),
            VertArrangement = GatewayVertArrangement,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = GpuBlendManner.None,
            Depth = new GpuDepthLedger(Test: true, Write: zDepthEmit, zDepthContrast),
            Cull = GpuPruneManner.None,
            FrontFace = GpuFrontFacet.CounterClockwise,
            AlphaToCoverage = false,
            TintEmit = false,
            StencilTest = stencilTest,
            Stencil = stencil,
            SampleCount = specimenTally,
        });
    }

    private void PaintZDepthFanRhi(
        ReadOnlySpan<Vector3> realmVerts,
        in Matrix4x4 lensProj,
        ReadOnlySpan<Vector4> planes,
        bool forceFarawayZ)
    {
        if (!_rhiCycleBegun)
            throw new InvalidOperationException("BeginFrame has to be called prior to drawing portal depth masks");

        int num = Math.Min(realmVerts.Length, UpperFanVerts);
        int planeTally = Math.Min(planes.Length, ClipCycle.UpperPlanes);
        var coder = _ambit!.DemandCoder();
        IGpuCycle cycle = _cycles!.LatestCycle
            ?? throw new InvalidOperationException(
                "GatewayZDepthBitmaskPainter needs an open IGpuCycle (see GpuDeviceCycleLifespan)");

        int triangleTally = num - 2;
        int vertTally = triangleTally * 3;
        var verts = cycle.ReserveLoop(
            vertTally * 3 * sizeof(float),
            GpuLoopPurpose.Vertex);
        Span<float> loci = verts.AsSpan<float>();
        for (int triangle = 0; triangle < triangleTally; ++triangle)
        {
            EmitLocus(loci, triangle * 9, realmVerts[0]);
            EmitLocus(loci, triangle * 9 + 3, realmVerts[triangle + 1]);
            EmitLocus(loci, triangle * 9 + 6, realmVerts[triangle + 2]);
        }

        var clip = cycle.ReserveLoop(
            ClipCycle.LandUboOctets,
            GpuLoopPurpose.Uniform);
        clip.Data.Clear();
        MemoryMarshal.Write(clip.Data, in planeTally);
        var clipPlanes = MemoryMarshal.Cast<byte, Vector4>(
            clip.Data[ClipCycle.ChamberClipPlanesShift..]);
        for (int idx = 0; idx < planeTally; ++idx)
            clipPlanes[idx] = planes[idx];

        CaptureGatewayPass(
            coder,
            _zDepthEmitPipe!,
            clip,
            verts,
            vertTally,
            in lensProj,
            rasterizePass: forceFarawayZ ? 1 : 0);
    }

    private static void CaptureGatewayPass(
        IGpuSweepCoder coder,
        IGpuPipe pipe,
        in GpuLoopAlloc clip,
        in GpuLoopAlloc verts,
        int vertTally,
        in Matrix4x4 lensProj,
        int rasterizePass)
    {
        coder.BindPipeline(pipe);
        coder.AssignPushConstants(new GpuShoveConstants
        {
            LensMirror = lensProj,
            PaintIdentShift = 0,
            IlluminationManner = 0,
            RasterizePass = rasterizePass,
            LampDiag = 0,
            TextureIndexA = 0,
            TextureOrdinalB = 0,
            ParamA = 0f,
            ParameterB = 0f,
        });
        coder.AttachUniformBuf(
            ClipCycle.LandClipUboMapping,
            clip.Buffer,
            clip.ShiftOctets,
            (uint)ClipCycle.LandUboOctets);
        coder.AttachVertBuf(0, verts.Buffer, verts.ShiftOctets);
        coder.Draw((uint)vertTally, 1, 0, 0);
    }

    private static void EmitLocus(Span<float> dest, int shift, Vector3 locus)
    {
        dest[shift] = locus.X;
        dest[shift + 1] = locus.Y;
        dest[shift + 2] = locus.Z;
    }

    private void TeardownRhiAssetList()
    {
        List<Exception>? misses = null;
        void Attempt(Action act)
        {
            try { act(); }
            catch (Exception problem) { (misses ??= []).Add(problem); }
        }

        Attempt(() => _zDepthEmitPipe?.Dispose());
        _zDepthEmitPipe = null;
        _rhiCycleBegun = false;

        if (misses is not null)
        {
            throw new AggregateException(
                "The portal depth mask's RHI resources didn't fully release",
                misses);
        }
    }
}
