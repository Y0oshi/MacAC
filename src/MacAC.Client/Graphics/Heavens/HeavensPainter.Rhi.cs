using System.Runtime.InteropServices;
using MacAC.Assets;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Landscape;

namespace MacAC.Client.Graphics.Heavens;

public sealed unsafe partial class HeavensPainter
{
    private readonly IClientGpuDevice? _device;
    private readonly ILatestGpuCycleOrigin? _cycles;
    private readonly IRealmPassScope? _ambit;
    private IGpuPipe? _alphaPipeline;
    private IGpuPipe? _additivePipeline;

    private readonly Dictionary<(uint SurfaceId, bool Repeat), GpuTextureSlot>
        _socketByCanvasAndEnclose = [];

    internal static readonly GpuVertexArrangement HeavensVertArrangement = GpuVertexArrangement.Interleaved(
        strideOctets: (uint)sizeof(MechVertex),
        [
            new GpuVertexAttribute(0, GpuVertFmt.Float3, 0),
            new GpuVertexAttribute(1, GpuVertFmt.Float3, 12),
            new GpuVertexAttribute(2, GpuVertFmt.Float2, 24),
        ]);

    internal HeavensPainter(
        IClientGpuDevice device,
        ILatestGpuCycleOrigin frames,
        IRealmPassScope scope,
        IDatAccess dats,
        BitmapStash textures)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
        _ambit = scope ?? throw new ArgumentNullException(nameof(scope));
        _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));

        _alphaPipeline = BuildHeavensPipe("sky-alpha", GpuBlendManner.StraightAlpha);
        try
        {
            _additivePipeline = BuildHeavensPipe("sky-additive", GpuBlendManner.Additive);
        }
        catch
        {
            _alphaPipeline.Dispose();
            _alphaPipeline = null;
            throw;
        }
    }

    private IGpuPipe BuildHeavensPipe(string label, GpuBlendManner blend) =>
        _device!.BuildPipe(new GpuPipeSpec
        {
            Name = label,
            Shaders = new GpuShaderGroup("heavens"),
            VertArrangement = HeavensVertArrangement,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = blend,
            Depth = GpuDepthLedger.Disabled,
            Cull = GpuPruneManner.None,
            FrontFace = GpuFrontFacet.CounterClockwise,
            AlphaToCoverage = false,
            TintEmit = true,
            SampleCount = _ambit!.SampleCount,
        });

    // Uploads one submesh into its own device-local vertex/index buffer pair
    private SubTriMeshGpu PushSubTriMeshRhi(GfxObjPatch patch)
    {
        IClientGpuDevice dev = _device!;
        ReadOnlySpan<byte> vertOctets = MemoryMarshal.AsBytes<MechVertex>(patch.Vertices);
        ReadOnlySpan<byte> ordinalOctets = MemoryMarshal.AsBytes<uint>(patch.Indices);

        IClientGpuBuffer verts = dev.BuildBuf(new GpuBufferSpec(
            $"sky-vertices-0x{patch.SurfaceId:X8}",
            Math.Max(vertOctets.Length, 32),
            GpuBufferPurpose.Vertex | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        IClientGpuBuffer ordinals;
        try
        {
            ordinals = dev.BuildBuf(new GpuBufferSpec(
                $"sky-indices-0x{patch.SurfaceId:X8}",
                Math.Max(ordinalOctets.Length, 4),
                GpuBufferPurpose.Index | GpuBufferPurpose.TransferDestination,
                GpuMemoryTenancy.DeviceLocal));
        }
        catch
        {
            verts.Dispose();
            throw;
        }

        try
        {
            if (!vertOctets.IsEmpty)
                verts.Upload(0, vertOctets);
            if (!ordinalOctets.IsEmpty)
                ordinals.Upload(0, ordinalOctets);
        }
        catch
        {
            ordinals.Dispose();
            verts.Dispose();
            throw;
        }

        return new SubTriMeshGpu
        {
            VertBuf = verts,
            OrdinalBuf = ordinals,
            IndexTally = patch.Indices.Length,
            SurfaceId = patch.SurfaceId,
            IsAdditive = patch.Translucency == SeeThroughKind.Additive,
            SurfLuminosity = patch.Luminosity,
            SurfDiffuse = patch.Diffuse,
            NeedsUvRepeat = patch.NeedsUvRepeat,
            SurfOpacity = patch.SurfOpacity,
            DisableFog = patch.DisableFog,
        };
    }

    private uint RhiTextureChartSocket(uint canvasIdent, bool repeat)
    {
        var tag = (surfaceId: canvasIdent, repeat);
        if (!_socketByCanvasAndEnclose.TryGetValue(tag, out GpuTextureSlot socket))
        {
            socket = _textures.EnrollRealmCanvas(canvasIdent, repeat);
            _socketByCanvasAndEnclose.Add(tag, socket);
        }

        return socket.Index;
    }

    // Records one submesh into the borrowed world pass
    private void PaintSubTriMeshRhi(SubTriMeshGpu sub, uint textureSocket, bool nightHeavens = false)
    {
        if (sub.IndexTally == 0 || sub.VertBuf is null || sub.OrdinalBuf is null)
            return;

        IRealmPassScope ambit = _ambit!;
        IGpuSweepCoder coder = ambit.DemandCoder();
        IGpuCycle cycle = _cycles!.LatestCycle
            ?? throw new InvalidOperationException(
                "HeavensPainter needs an open IGpuCycle (see GpuDeviceCycleLifespan)");

        coder.BindPipeline(nightHeavens || sub.IsAdditive ? _additivePipeline! : _alphaPipeline!);
        var pushConstants = new GpuShoveConstants
        {
            LensMirror = System.Numerics.Matrix4x4.Identity,
            PaintIdentShift = 0,
            IlluminationManner = 0,
            RasterizePass = 0,
            LampDiag = 0,
            TextureIndexA = textureSocket,
            TextureOrdinalB = 0,
            ParamA = nightHeavens ? 1f : 0f,
            ParameterB = NightHeavensSeed,
        };
        coder.AssignPushConstants(in pushConstants);
        coder.AttachVertBuf(0, sub.VertBuf, 0);
        coder.AttachOrdinalBuf(sub.OrdinalBuf, 0, GpuOrdinalKind.UInt32);

        GpuLoopAlloc parameters = cycle.ReserveLoop(
            HeavensParams.SizeInBytes,
            GpuLoopPurpose.Uniform);
        MemoryMarshal.Write(parameters.Data, in _params);
        coder.AttachUniformBuf(
            GpuBindingModel.UniformHeavensParameters,
            parameters.Buffer,
            parameters.ShiftOctets,
            HeavensParams.SizeInBytes);

        RealmFrameSectionWiring.AttachTableauIllumination(coder, ambit.Sections, cycle);

        coder.PaintIndexed((uint)sub.IndexTally, 1, 0, 0, 0);
    }

    private void TeardownRhi()
    {
        List<Exception>? misses = null;
        void Attempt(Action act)
        {
            try { act(); }
            catch (Exception problem) { (misses ??= []).Add(problem); }
        }

        foreach (List<SubTriMeshGpu> subs in _gpuByGfxObjRef.Values)
        {
            foreach (SubTriMeshGpu sub in subs)
            {
                Attempt(() => sub.VertBuf?.Dispose());
                Attempt(() => sub.OrdinalBuf?.Dispose());
            }
        }

        _gpuByGfxObjRef.Clear();
        _socketByCanvasAndEnclose.Clear();

        Attempt(() => _alphaPipeline?.Dispose());
        _alphaPipeline = null;
        Attempt(() => _additivePipeline?.Dispose());
        _additivePipeline = null;

        if (misses is not null)
            throw new AggregateException("The sky renderer's RHI resources didn't fully release", misses);
    }
}
