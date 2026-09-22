using System.Collections.Immutable;
using MacAC.Assets;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Effects;

namespace MacAC.Client.Graphics;

public sealed unsafe partial class MotePainter
{
    private readonly IClientGpuDevice? _device;

    private readonly ILatestGpuCycleOrigin? _cycles;

    private readonly IRealmPassScope? _ambit;

    private IGpuPipe? _billboardAlphaPipe;

    private IGpuPipe? _billboardAdditivePipe;

    private IGpuPipe? _triMeshSolidPipe;

    private IGpuPipe? _triMeshAlphaPipe;

    private IGpuPipe? _triMeshAdditivePipe;

    private IGpuPipe? _triMeshInvPipe;

    private IClientGpuBuffer? _quadVertBuf;

    private IClientGpuBuffer? _quadOrdinalBuf;

    private static readonly float[] QuadVerts =
    [
        -0.5f, -0.5f, 0f, 0f,
         0.5f, -0.5f, 1f, 0f,
         0.5f,  0.5f, 1f, 1f,
        -0.5f,  0.5f, 0f, 1f,
    ];

    private static readonly uint[] QuadOrdinals = [0, 1, 2, 0, 2, 3];

    private const uint QuadStrideOctets = 4 * sizeof(float);

    private static readonly uint TriMeshInstStrideOctets =
        (uint)sizeof(MeshMoteGpuInstance);

    internal static GpuVertexArrangement BillboardVertArrangement { get; } = new(
        [
            new GpuVertexWiring(0, QuadStrideOctets, GpuVertFeedRate.Vertex),
            new GpuVertexWiring(
                1,
                (uint)sizeof(BillboardGpuInst),
                GpuVertFeedRate.Instance),
        ],
        ImmutableArray.Create(
            new GpuVertexAttribute(0, GpuVertFmt.Float2, 0, Binding: 0),
            new GpuVertexAttribute(1, GpuVertFmt.Float2, 8, Binding: 0),
            new GpuVertexAttribute(2, GpuVertFmt.Float4, 0, Binding: 1),
            new GpuVertexAttribute(3, GpuVertFmt.Float4, 16, Binding: 1),
            new GpuVertexAttribute(4, GpuVertFmt.Float4, 32, Binding: 1),
            new GpuVertexAttribute(5, GpuVertFmt.Float4, 48, Binding: 1),
            new GpuVertexAttribute(6, GpuVertFmt.UInt1, 64, Binding: 1),
            new GpuVertexAttribute(7, GpuVertFmt.UInt1, 68, Binding: 1)));

    internal static GpuVertexArrangement TriMeshVertArrangement { get; } = new(
        [
            new GpuVertexWiring(
                        0,
                        GpuVertexArrangement.RealmTriMesh.StrideOctets,
                        GpuVertFeedRate.Vertex),
            new GpuVertexWiring(1, TriMeshInstStrideOctets, GpuVertFeedRate.Instance),
        ],
        ImmutableArray.Create(
            new GpuVertexAttribute(0, GpuVertFmt.Float3, 0, Binding: 0),
            new GpuVertexAttribute(1, GpuVertFmt.Float3, 12, Binding: 0),
            new GpuVertexAttribute(2, GpuVertFmt.Float2, 24, Binding: 0),
            new GpuVertexAttribute(3, GpuVertFmt.Float4, 0, Binding: 1),
            new GpuVertexAttribute(4, GpuVertFmt.Float4, 16, Binding: 1),
            new GpuVertexAttribute(5, GpuVertFmt.Float4, 32, Binding: 1),
            new GpuVertexAttribute(6, GpuVertFmt.Float4, 48, Binding: 1),
            new GpuVertexAttribute(7, GpuVertFmt.Float4, 64, Binding: 1),
            new GpuVertexAttribute(8, GpuVertFmt.UInt1, 80, Binding: 1)));

    internal MotePainter(
        IClientGpuDevice device,
        ILatestGpuCycleOrigin frames,
        IRealmPassScope scope,
        MoteSys particles,
        BitmapStash? textures = null,
        IDatAccess? datFiles = null,
        RealmTriMeshBridge? triMeshBridge = null,
        CanonAlphaFifo? alphaFifo = null,
        long? alphaTempAllowanceOctets = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
        _ambit = scope ?? throw new ArgumentNullException(nameof(scope));
        _textures = textures;
        _datFiles = datFiles;
        _triMeshBridge = triMeshBridge;
        _motes = particles ?? throw new ArgumentNullException(nameof(particles));
        _alphaFifo = alphaFifo;
        _alphaSrc = new AlphaPaintSource(this);
        _allocatePostponedMotePaint = EarmarkRelayPostponedMote;
        _drawImmediateParticle = PaintImmediateMoteSubmissionRhi;
        long tempAllowance = alphaTempAllowanceOctets
            ?? MacAC.Client.Graphics.Tenancy.AlphaScratchAllowanceProfile.Create(
                MacAC.Client.Graphics.Tenancy.TenancyAllowanceKnobs.Default.AlphaScratchBytes)
                .ParticleBytes;
        _alphaTempRule =
            new MacAC.Client.Graphics.Tenancy.RetainedScratchCapacityRule(tempAllowance);
        if (_triMeshBridge is not null)
        {
            _triMeshReferences = new MoteMeshReferenceLedger(
                gfxObjRefIdent => _triMeshBridge.IncrementRefTally(gfxObjRefIdent),
                gfxObjRefIdent => _triMeshBridge.DecrementRefTally(gfxObjRefIdent));
        }
        _spoutRetirements = new MoteEmitterRetirementLedger(
            hnd => _triMeshReferences?.Release(hnd),
            hnd => _moteGfxDetailsBySpout.Remove(hnd),
            hnd => _textures?.FreeMoteTextureHolder(hnd),
            problem => Console.Error.WriteLine($"[particles] {problem}"));

        try
        {
            BuildRhiAssetList(device, scope.SampleCount);
            _motes.EmitterDied += OnSpoutDied;
        }
        catch
        {
            TeardownRhiAssetList();
            throw;
        }
    }

    internal readonly record struct MeshMotePipelineLedger(
        GpuBlendManner Blend,
        GpuDepthLedger Depth);

    private readonly record struct RhiVertSegment(IClientGpuBuffer? Buffer, uint OffsetBytes);

    private RhiVertSegment _readiedBillboardInsts;

    private RhiVertSegment _readiedTriMeshInsts;
}
