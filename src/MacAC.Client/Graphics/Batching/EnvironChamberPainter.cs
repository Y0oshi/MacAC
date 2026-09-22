using System.Collections.Concurrent;
using System.Numerics;
using MacAC.Mechanics.Geometry;

namespace MacAC.Client.Graphics.Batching;

[Flags]
internal enum EnvironChamberSeeThruCourse : byte
{
    None = 0,
    Immediate = 1 << 0,
    Clip = 1 << 1,
    Alpha = 1 << 2,
    All = Immediate | Clip | Alpha,
}

public sealed partial class EnvironChamberPainter :
    IDisposable,
    IEnvCellLandblockHerald
{
    private readonly object _bulletinHolder = new();

    private readonly ThingTriMeshKeeper _meshManager;

    private readonly BatchFrustum _frustum;

    private readonly ConcurrentDictionary<uint, EnvironChamberLandblock> _lbs = new();

    private readonly object _rasterizeMutex = new();

    private EnvCellVisibilityCapture _activeSnapshot = new();

    private Matrix4x4 _previousLensProj = Matrix4x4.Identity;

    private bool _initialized;

    private readonly List<List<InstanceData>> _rosterReservoir = [];

    private int _poolIndex = 0;

    private readonly ThreadLocal<ReadyTemp> _readyTemp =
        new(() => new ReadyTemp(), trackAllValues: true);

    private Matrix4x4[] _gpuInstanceTransforms = [];

    private uint[] _clipSocketBlob = [];

    private float[] _instAlphaBlob = [];

    private float[] _globalLampBlob = new float[MacAC.Mechanics.Illumination.SceneLightPacker.FloatsPerLamp * 16];

    private int[] _lampSetBlob = new int[1024 * MacAC.Mechanics.Illumination.LightKeeper.UpperLightsPerEnvCell];

    private System.Collections.Generic.IReadOnlyList<MacAC.Mechanics.Illumination.LightEmitter>? _ptCapture;

    private sealed class ShelvedCellLightSet
    {
        public int CycleGen;
        public readonly int[] Indices = new int[MacAC.Mechanics.Illumination.LightKeeper.UpperLightsPerEnvCell];
    }

    private readonly System.Collections.Generic.Dictionary<uint, ShelvedCellLightSet> _chamberLampSetStash = [];

    private readonly List<uint> _chamberLampDeletionTemp = [];

    private int _lampCycleGen;

    // Per-GPU-fenced-frame-slot draw bookkeeping
    private int _dynamicCycleSocket;

    private bool _dynamicCycleBegun;

    // Reusable scratch arrays - avoid per-frame allocation.
    private DrawElementsIndirectDirective[] _commands = [];

    private ModernLotBlob[] _modernBatches = [];

    private uint[] _specificsBucketBlob = [];

    private readonly List<EnvironChamberLandblock> _readyLbs = [];

    private readonly List<InstanceData> _rasterizeInsts = [];

    private readonly List<(ThingRasterizeBlob renderData, ulong gfxObjId, int count, int offset)> _rasterizePaintCalls = [];

    private readonly Dictionary<ulong, List<InstanceData>> _filteredClusters = [];

    private readonly HashSet<List<InstanceData>> _filteredPossessedRosters = [];

    private const int PruneClusterTally = 4;

    private const int AdditiveClusterBase = 4;

    private const int ClipDdsClusterBase = 8;

    private const int ClipPalettedClusterBase = 12;

    private const int LotClusterTally = 16;

    private readonly List<(ThingRasterizeLot batch, int instanceCount, int instanceOffset)>[] _lotsByPruneCluster =
        [.. Enumerable.Range(0, LotClusterTally).Select(_ => new List<(ThingRasterizeLot, int, int)>())];

    private readonly List<int> _engagedPruneClusters = new(8);

    private readonly HashSet<uint> _transparentCellIds = [];

    private readonly List<PaintCallSpan> _paintCallSpans = [];

    private readonly List<MdiPaintSpan> _mdiDrawRanges = [];

    private readonly record struct PaintCallSpan(int First, int Count);

    internal readonly record struct MdiPaintSpan(
        int GroupIndex,
        int FirstCommand,
        int CommandCount,
        CanonSurfaceMaterialState MaterialState);

    private readonly Dictionary<ulong, List<InstanceData>> _engagedCaptureGlobalClusters = [];

    private readonly List<ulong> _engagedCaptureGlobalGfxObjRefIdents = [];

    public bool NeedsReady { get; private set; } = true;

    private Matrix4x4 _readiedLensProj;

    private Vector3 _readiedCamLocus;

    private readonly HashSet<uint> _readiedSift = [];

    private bool _readiedSiftWasNull;

    private (int? X, int? Y, int? Radius) _readiedTrim;

    private long _readiedTriMeshVer = -1;

    private bool _hasReadiedCapture;

    private RetryableAssetFreeRegister? _teardownAssetList;

    private bool _disposing;

    public struct PreviousCycleStats { public int ChambersRendered; public int TrianglesDrawn; }

    private PreviousCycleStats _previousCycleStats;

    private void CaptureReadiedFeeds(
        in Matrix4x4 lensProj,
        Vector3 camLocus,
        HashSet<uint>? sift,
        int? middleLbX,
        int? middleLbY,
        int? rasterizeRadius,
        long triMeshVer)
    {
        _readiedLensProj = lensProj;
        _readiedCamLocus = camLocus;
        _readiedSiftWasNull = sift is null;
        _readiedSift.Clear();
        if (sift is not null)
            _readiedSift.UnionWith(sift);
        _readiedTrim = (middleLbX, middleLbY, rasterizeRadius);
        _readiedTriMeshVer = triMeshVer;
        _hasReadiedCapture = true;
        ++CaptureGen;
    }

    private sealed class ReadyTemp
    {
        public readonly Dictionary<uint, Dictionary<ulong, List<InstanceData>>> BatchedByChamber = [];
        public readonly HashSet<uint> VisibleCells = [];

        private readonly List<Dictionary<ulong, List<InstanceData>>> _gfxDictionaryReservoir = [];
        private readonly List<List<InstanceData>> _rosterReservoir = [];
        private int _gfxDictionaryOrdinal;
        private int _rosterOrdinal;

        public void Reset()
        {
            BatchedByChamber.Clear();
            VisibleCells.Clear();
            _gfxDictionaryOrdinal = 0;
            _rosterOrdinal = 0;
        }

        public Dictionary<ulong, List<InstanceData>> RentGfxDictionary()
        {
            if (_gfxDictionaryOrdinal == _gfxDictionaryReservoir.Count)
                _gfxDictionaryReservoir.Add([]);
            var val = _gfxDictionaryReservoir[_gfxDictionaryOrdinal++];
            val.Clear();
            return val;
        }

        public List<InstanceData> RentRoster()
        {
            if (_rosterOrdinal == _rosterReservoir.Count)
                _rosterReservoir.Add([]);
            var val = _rosterReservoir[_rosterOrdinal++];
            val.Clear();
            return val;
        }
    }
}
