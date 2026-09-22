using System.Collections.Concurrent;
using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics.Tenancy;
using Microsoft.Extensions.Logging;
using BoundingBox =  MacAC.Dat.Bounds3;

namespace MacAC.Client.Graphics.Batching;

public class ThingRasterizeBlob
{
    public uint VAO { get; set; }
    public uint VBO { get; set; }
    public int VertTally { get; set; }
    public List<ThingRasterizeLot> Batches { get; set; } = [];

    public bool HasCutoutSubset { get; set; }

    internal GlobalTriMeshAlloc? GlobalAlloc { get; set; }
    public bool IsSetup { get; set; }
    public List<(ulong GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = [];

    public List<QueuedEmitter> ParticleEmitters { get; set; } = [];

    public Vector3[] CPULoci { get; set; } = [];

    public ushort[] CPUOrdinals { get; set; } = [];

    public Vector3[] CPURimStrokes { get; set; } = [];

    public BoundingBox BoundingBox { get; set; }

    public Vector3 SortCenter { get; set; }

    public uint DIDDegrade { get; set; }

    public Orb? SelectionSphere { get; set; }

    public long MemoryDims { get; set; }

    internal long NonArenaGpuOctets { get; set; }
}

/// <summary>A single GPU draw batch: IBO + texture array layer.</summary>
public class ThingRasterizeLot
{
    public uint IBO { get; set; }
    public int OrdinalTally { get; set; }
    public TextureAtlasKeeper Tileset { get; set; } = null!;
    public int TextureIndex { get; set; }
    public (int Width, int Height) TextureDims { get; set; }
    public TexelLayout TextureFormat { get; set; }
    public uint SurfaceId { get; set; }
    public byte RetailSurfaceMask { get; set; }
    public BitmapTag Key { get; set; }
    public FaceCulling CullMode { get; set; }
    public MacAC.Mechanics.Geometry.SeeThroughKind Translucency { get; set; }
    public MacAC.Mechanics.Geometry.CanonSurfaceMaterialState MaterialState { get; set; }
        = MacAC.Mechanics.Geometry.CanonSurfaceMaterialState.Opaque;
    public bool IsTransparent { get; set; }
    public bool IsAdditive { get; set; }
    public bool HasWrappingUVs { get; set; }
    public float SurfaceOpacity { get; set; } = 1f;

    public uint LeadIdx { get; set; }
    public uint BaseVertex { get; set; }

    internal MacAC.Client.Graphics.Gpu.GpuTextureSlot TextureSlot { get; set; }
        = MacAC.Client.Graphics.Gpu.GpuTextureSlot.Unassigned;
}

public partial class ThingTriMeshKeeper : IDisposable
{
    private readonly ITriMeshPipeDevice _visualsDev;

    private readonly IBakedAssetSource _preparedAssets;

    private readonly ILogger _logger;

    private readonly MacAC.Client.Graphics.Gpu.IClientGpuDevice _gpuDev;

    private readonly IRealmTextureArrayMint _tilesetArrs;

    private readonly object _teardownLatch = new();

    private bool _teardownFinished;

    private bool _teardownRunning;

    private bool _workersQuiesced;

    private bool _jobSignalDestroyed;

    private readonly ConcurrentDictionary<ulong, ThingRasterizeBlob> _renderData = new();

    private long _rasterizeBlobReadinessVer;

    private readonly Dictionary<ulong, ThingFreeTicket> _objectReleases = [];

    private readonly Queue<ulong> _objectFreeFifo = new();

    private readonly Dictionary<ulong, RetryableAssetFreeRegister> _pushRollbacks = [];

    private readonly Queue<ulong> _pushUndoFifo = new();

    private readonly TriMeshOwnershipCounter _ownership = new();

    private readonly ConcurrentDictionary<ulong, Task<HarvestedMesh?>> _prepTasks = new();

    private readonly LinkedList<ulong> _lruRoster = new();

    private readonly long _upperGpuMemory;

    private readonly int _upperStashedObjects;

    private long _latestNonArenaGpuMemory;

    private readonly Dictionary<(int Width, int Height, TexelLayout Format), List<TextureAtlasKeeper>> _globalTilesets = [];

    private readonly HashSet<TextureAtlasKeeper> _staleTilesets = [];

    private long _tilesetUseSeries;

    private const long KeptVacantTilesetAllowanceOctets = 64L * 1024 * 1024;

    private const int KeptVacantTilesetTallyThreshold = 32;

    private readonly MacAC.Client.Graphics.BoundedUnownedResourceShelf<TextureAtlasKeeper>
        _safeVacantTilesets = new(KeptVacantTilesetAllowanceOctets, KeptVacantTilesetTallyThreshold);

    private readonly List<TextureAtlasKeeper> _sunsettingTilesets = [];

    private readonly CpuMeshUploadShelf _cpuTriMeshStash;

    private readonly TriMeshPushLoadingFifo _linedTriMeshBlob;

    private volatile bool _arenaBackpressured;

    public const int UpperPushReattempts = 3;

    private sealed class PreparationAsk(
        BakedAssetRequest asset,
        HarvestedMesh? stashedBlob,
        TaskCompletionSource<HarvestedMesh?> wrapUp,
        CancellationTokenSource abort)
    {
        private readonly object _abortLatch = new();
        private bool _abortBegun;
        private bool _abortInHeadway;
        private bool _teardownAsked;
        private bool _abortDestroyed;

        public BakedAssetRequest Asset { get; } = asset;
        public ulong Id => Asset.RuntimeObjectId;
        public HarvestedMesh? StashedBlob { get; } = stashedBlob;
        public TaskCompletionSource<HarvestedMesh?> Completion { get; } = wrapUp;
        public CancellationTokenSource Abort { get; } = abort;

        public void Cancel()
        {
            lock (_abortLatch)
            {
                if (_abortDestroyed || _abortBegun)
                    return;
                _abortBegun = true;
                _abortInHeadway = true;
            }

            try
            {
                Abort.Cancel();
            }
            finally
            {
                bool teardown;
                lock (_abortLatch)
                {
                    _abortInHeadway = false;
                    teardown = _teardownAsked && !_abortDestroyed;
                    if (teardown)
                        _abortDestroyed = true;
                }
                if (teardown)
                    Abort.Dispose();
            }
        }

        public void TeardownAbort()
        {
            lock (_abortLatch)
            {
                if (_abortDestroyed)
                    return;
                if (_abortInHeadway)
                {
                    _teardownAsked = true;
                    return;
                }
                _abortDestroyed = true;
            }
            Abort.Dispose();
        }
    }

    private readonly LinkedList<PreparationAsk> _queuedReqs = new();

    private readonly Dictionary<ulong, LinkedListNode<PreparationAsk>> _queuedReqByIdent = [];

    private readonly Dictionary<ulong, PreparationAsk> _engagedPrepByIdent = [];

    private readonly Dictionary<ulong, EnvCellGeomAsk> _environChamberDescriptors = [];

    private readonly HashSet<ulong> _terminalPrepMisses = [];

    private readonly HashSet<Task> _workerTasks = [];

    private readonly ManualResetEventSlim _prepJobOnHand = new(false);

    private const int UpperParallelLoads = 4;

    internal enum PrepWorkerWakeAct
    {
        Process,
        ResetAndWait,
        Exit,
    }

    private sealed class ThingFreeTicket(
        ulong ident,
        ThingRasterizeBlob blob,
        long reclaimableOctets,
        RetryableAssetFreeRegister assetList)
    {
        public ulong Id { get; } = ident;
        public ThingRasterizeBlob Data { get; } = blob;
        public long ReclaimableOctets { get; } = reclaimableOctets;
        public RetryableAssetFreeRegister Resources { get; } = assetList;
        public bool IsQueued { get; set; }
    }

    internal ThingTriMeshKeeper(
        ITriMeshPipeDevice graphicsDevice,
        MacAC.Client.Graphics.Gpu.IClientGpuDevice gpuDev,
        IBakedAssetSource preparedAssets,
        ILogger<ThingTriMeshKeeper> logger,
        TenancyAllowanceKnobs? budgets = null)
    {
        budgets ??= TenancyAllowanceKnobs.Default;
        _visualsDev = graphicsDevice
            ?? throw new ArgumentNullException(nameof(graphicsDevice));
        ArgumentNullException.ThrowIfNull(gpuDev);
        _gpuDev = gpuDev;
        _tilesetArrs = IRealmTextureArrayMint.For(
            graphicsDevice,
            gpuDev,
            logger ?? throw new ArgumentNullException(nameof(logger)));
        _preparedAssets = preparedAssets
            ?? throw new ArgumentNullException(nameof(preparedAssets));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
        _upperGpuMemory = budgets.ObjectMeshGpuBytes;
        _upperStashedObjects = budgets.ObjectMeshUnownedEntries;
        _cpuTriMeshStash = new CpuMeshUploadShelf(
            budgets.PreparedMeshCpuEntries,
            budgets.PreparedMeshCpuBytes);
        _linedTriMeshBlob = new TriMeshPushLoadingFifo(
            budgets.MeshStagingEntries,
            budgets.MeshStagingBytes);
        if (_visualsDev.HasOpenGL43 && _visualsDev.HasBindless)
        {
            GlobalBuf = new GlobalTriMeshBuffer(
                gpuDev,
                _visualsDev.ResourceRetirement);
        }
    }

    public struct EnvCellGeomAsk
    {
        public uint SrcChamberIdent;
        public uint EnvironmentId;
        public ushort CellStructure;
        public List<ushort> Surfaces;
    }

    private sealed class PushTilesetScheme
    {
        public TextureAtlasKeeper? Existing { get; init; }
        public required int Capacity { get; init; }
        public required long SumArrOctets { get; init; }
        public int OnHandSockets { get; set; }
        public bool Touched { get; set; }
        public HashSet<BitmapTag> PlannedTags { get; } = [];

        public bool HasTexture(BitmapTag tag) =>
            PlannedTags.Contains(tag) || Existing?.HasTexture(tag) == true;
    }

    internal static List<((int Width, int Height, TexelLayout Format) Format, TextureHarvestBatch Batch)> SequencedPushLots(HarvestedMesh triMeshBlob)
    {
        var duos = new List<((int Width, int Height, TexelLayout Format) Format, TextureHarvestBatch Batch)>();
        bool chamberShell = false;
        foreach (var (fmt, lots) in triMeshBlob.TextureBatches)
        {
            foreach (var lot in lots)
            {
                duos.Add((fmt, lot));
                chamberShell |= lot.IsCellShell;
            }
        }
        if (chamberShell)
            return [.. duos.OrderBy(p => p.Batch.SourceSurfaceIndex)]; // stable: OrderBy preserves storage order on ties
        return duos;
    }
}
