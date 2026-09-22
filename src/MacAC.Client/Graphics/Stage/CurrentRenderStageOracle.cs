using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Graphics.Stage;

internal readonly record struct RenderStageHash128(ulong Low, ulong High)
{
    public static RenderStageHash128 Empty { get; } = new(0, 0);

    public override string ToString() => $"{High:X16}{Low:X16}";
}

internal readonly record struct CurrentRenderMirrorFingerprint(
    InteriorActorPartition.ProjClass ProjectionClass,
    uint LandblockId,
    uint EntityId,
    uint ServerGuid,
    uint SourceId,
    uint ParentCellId,
    uint EffectCellId,
    uint BuildingShellAnchorCellId,
    uint Flags,
    int MeshCount,
    RenderStageHash128 Transform,
    RenderStageHash128 Geometry,
    RenderStageHash128 Appearance);

internal enum LatestRasterizePLensCourse : byte
{
    LandscapeOutdoorStatic,
    LandscapeBuildingShell,
    LandscapeOutsideDynamic,
    LookInObject,
    CellStatic,
    DynamicLast,
}

internal readonly record struct LatestRasterizePLensContenderFingerprint(
    int Sequence,
    LatestRasterizePLensCourse Route,
    int RouteIndex,
    uint CellId,
    CurrentRenderMirrorFingerprint Projection);

internal readonly record struct CurrentRenderRouterFingerprint(
    int Sequence,
    int DrawSequence,
    RealmPaintRouter.ActorSet Set,
    int MeshRefIndex,
    uint TupleLandblockId,
    uint CacheLandblockId,
    CurrentRenderMirrorFingerprint Projection,
    uint GfxObjId,
    Matrix4x4 PartTransform,
    RenderStageHash128 SurfaceOverrides);

internal readonly record struct CurrentRenderRouterSubmission(
    int VisibleInstanceCount,
    int ImmediateInstanceCount,
    int OpaqueGroupCount,
    int TransparentGroupCount,
    bool TransparentDeferred,
    RenderStageHash128 OpaqueDigest,
    RenderStageHash128 TransparentDigest,
    RenderStageHash128 TransparentSetDigest,
    RenderStageHash128 Digest);

internal readonly record struct CurrentRenderPickingFingerprint(
    int Sequence,
    uint ServerGuid,
    uint LocalEntityId,
    int PartIndex,
    uint GfxObjId,
    Matrix4x4 LocalToWorld,
    RenderStageHash128 Geometry);

internal readonly record struct CurrentRenderStageOracleCapture(
    bool Enabled,
    ulong CompletedFrameSequence,
    int AbortedFrames,
    int ProjectionCount,
    int OutdoorStaticCount,
    int CellStaticCount,
    int DynamicCount,
    int CellBucketCount,
    RenderStageHash128 Digest,
    ulong CompletedPViewFrameSequence,
    int AbortedPViewFrames,
    int PViewCandidateCount,
    RenderStageHash128 PViewDigest,
    ulong DispatcherFrameSequence,
    int AbortedDispatcherFrames,
    int DispatcherDrawCount,
    int DispatcherEntityCount,
    int DispatcherMeshRefCount,
    int DispatcherInstanceCount,
    int DispatcherOpaqueGroupCount,
    int DispatcherTransparentGroupCount,
    RenderStageHash128 DispatcherDigest,
    ulong CompletedSelectionFrameSequence,
    int AbortedSelectionFrames,
    int SelectionPartCount,
    RenderStageHash128 SelectionDigest)
{
    public static CurrentRenderStageOracleCapture Disabled { get; } = new(
        Enabled: false,
        CompletedFrameSequence: 0,
        AbortedFrames: 0,
        ProjectionCount: 0,
        OutdoorStaticCount: 0,
        CellStaticCount: 0,
        DynamicCount: 0,
        CellBucketCount: 0,
        Digest: RenderStageHash128.Empty,
        CompletedPViewFrameSequence: 0,
        AbortedPViewFrames: 0,
        PViewCandidateCount: 0,
        PViewDigest: RenderStageHash128.Empty,
        DispatcherFrameSequence: 0,
        AbortedDispatcherFrames: 0,
        DispatcherDrawCount: 0,
        DispatcherEntityCount: 0,
        DispatcherMeshRefCount: 0,
        DispatcherInstanceCount: 0,
        DispatcherOpaqueGroupCount: 0,
        DispatcherTransparentGroupCount: 0,
        DispatcherDigest: RenderStageHash128.Empty,
        CompletedSelectionFrameSequence: 0,
        AbortedSelectionFrames: 0,
        SelectionPartCount: 0,
        SelectionDigest: RenderStageHash128.Empty);
}

internal interface ICurrentRenderStageOracleCaptureSource
{
    CurrentRenderStageOracleCapture Snapshot { get; }
}

internal interface ICurrentRenderPViewWatcher
{
    void CommencePLensCycle();

    void WatchPLensBin(
        LatestRasterizePLensCourse course,
        int courseOrdinal,
        uint chamberIdent,
        IReadOnlyList<RealmActor> actors);

    void ConcludePLensCycle();

    void CancelPLensCycle();
}

internal interface ICurrentRenderRouterWatcher
{
    void CommenceRouterCycle();

    void WatchRouterPaint(
        RealmPaintRouter.ActorSet set,
        int actorsWalked,
        IReadOnlyList<(
            RealmActor Entity,
            int MeshRefIndex,
            uint LandblockId)> approved);

    void WatchRouterSubmission(
        in CurrentRenderRouterSubmission submission);

    void CancelRouterCycle();
}

internal interface ICurrentRenderPickingWatcher
{
    void CommencePickCycle();

    void WatchPickPiece(
        uint srvOid,
        uint ownActorIdent,
        int pieceOrdinal,
        uint gfxObjRefIdent,
        Matrix4x4 ownToRealm,
        CanonPickMesh triMesh);

    void ConcludePickCycle();

    void CancelPickCycle();
}

internal sealed partial class CurrentRenderStageOracle :
    InteriorActorPartition.IWatcher,
    ICurrentRenderPViewWatcher,
    ICurrentRenderRouterWatcher,
    ICurrentRenderPickingWatcher,
    ICurrentRenderStageOracleCaptureSource
{
    private readonly List<CurrentRenderMirrorFingerprint> _projections = [];

    private readonly Dictionary<RealmActor, CurrentRenderMirrorFingerprint>
        _projByActor =
            new(ReferenceEqualityComparer.Instance);

    private readonly List<LatestRasterizePLensContenderFingerprint>
        _pviewContenders = [];

    private readonly List<CurrentRenderRouterFingerprint>
        _routerContenders = [];

    private readonly List<CurrentRenderRouterSubmission>
        _routerSubmissions = [];

    private readonly Dictionary<RealmActor, CurrentRenderMirrorFingerprint>
        _routerProjByActor =
            new(ReferenceEqualityComparer.Instance);

    private readonly List<CurrentRenderPickingFingerprint>
        _pickPieces = [];

    private readonly Dictionary<uint, PickingGeometryShelfEntry>
        _pickGeoByGfxObjRef = [];

    private ulong _cycleSeries;

    private int _abortedCycles;

    private int _exteriorStaticTally;

    private int _chamberStaticTally;

    private int _dynamicTally;

    private bool _cycleOpen;

    private ulong _pviewCycleSeries;

    private int _abortedPLensCycles;

    private bool _pviewCycleOpen;

    private StableRasterizeHash128 _pviewDigest;

    private ulong _routerCycleSeries;

    private int _abortedRouterCycles;

    private int _routerPaintTally;

    private int _routerActorTally;

    private int _routerInstTally;

    private int _routerSolidClusterTally;

    private int _routerSeeThruClusterTally;

    private bool _routerCycleOpen;

    private StableRasterizeHash128 _routerDigest;

    private ulong _pickCycleSeries;

    private int _abortedPickCycles;

    private bool _pickCycleOpen;

    private StableRasterizeHash128 _pickDigest;

    public CurrentRenderStageOracleCapture Snapshot { get; private set; } =
        CurrentRenderStageOracleCapture.Disabled with { Enabled = true };

    private readonly record struct PickingGeometryShelfEntry(
        CanonPickMesh Mesh,
        RenderStageHash128 Fingerprint);

    private sealed class CurrentRenderMirrorFingerprintComparer :
        IComparer<CurrentRenderMirrorFingerprint>
    {
        public static CurrentRenderMirrorFingerprintComparer Instance { get; } =
            new();

        public int Compare(
            CurrentRenderMirrorFingerprint fingerprint,
            CurrentRenderMirrorFingerprint y)
        {
            int val = ((int)fingerprint.ProjectionClass).CompareTo((int)y.ProjectionClass);
            if (val is not 0) return val;
            val = fingerprint.LandblockId.CompareTo(y.LandblockId);
            if (val is not 0) return val;
            val = fingerprint.ParentCellId.CompareTo(y.ParentCellId);
            if (val is not 0) return val;
            val = fingerprint.EntityId.CompareTo(y.EntityId);
            if (val is not 0) return val;
            val = fingerprint.ServerGuid.CompareTo(y.ServerGuid);
            if (val is not 0) return val;
            val = fingerprint.SourceId.CompareTo(y.SourceId);
            if (val is not 0) return val;
            val = fingerprint.EffectCellId.CompareTo(y.EffectCellId);
            if (val is not 0) return val;
            val = fingerprint.BuildingShellAnchorCellId.CompareTo(
                y.BuildingShellAnchorCellId);
            if (val is not 0) return val;
            val = fingerprint.Flags.CompareTo(y.Flags);
            if (val is not 0) return val;
            val = fingerprint.MeshCount.CompareTo(y.MeshCount);
            if (val is not 0) return val;
            val = fingerprint.Transform.High.CompareTo(y.Transform.High);
            if (val is not 0) return val;
            val = fingerprint.Transform.Low.CompareTo(y.Transform.Low);
            if (val is not 0) return val;
            val = fingerprint.Geometry.High.CompareTo(y.Geometry.High);
            if (val is not 0) return val;
            val = fingerprint.Geometry.Low.CompareTo(y.Geometry.Low);
            if (val is not 0) return val;
            val = fingerprint.Appearance.High.CompareTo(y.Appearance.High);
            return val is not 0 ? val : fingerprint.Appearance.Low.CompareTo(y.Appearance.Low);
        }
    }
}

internal struct StableRasterizeHash128
{
    private const ulong LoShift = 14695981039346656037UL;
    private const ulong LoPrime = 1099511628211UL;
    private const ulong HiShift = 7809847782465536322UL;
    private const ulong HiPrime = 14029467366897019727UL;

    private ulong _lo;
    private ulong _hi;

    public static StableRasterizeHash128 Create()
    {
        return new()
        {
            _lo = LoShift,
            _hi = HiShift,
        };
    }

    public readonly RenderStageHash128 Finish() => new(_lo, _hi);

    public void Add(bool val) => Add(val ? (byte)1 : (byte)0);

    public void Add(byte val) => Add((ulong)val);

    public void Add(int val) => Add(unchecked((uint)val));

    public void Add(uint val) => Add((ulong)val);

    public void Add(float val) => Add(BitConverter.SingleToUInt32Bits(val));

    public void Add(ulong val)
    {
        _lo = unchecked((_lo ^ val) * LoPrime);
        ulong folded = val ^ BitOperations.RotateLeft(val, 29);
        _hi = unchecked((_hi ^ folded) * HiPrime);
    }

    public void Add(Vector3 val)
    {
        Add(val.X);
        Add(val.Y);
        Add(val.Z);
    }

    public void Add(Vector2 val)
    {
        Add(val.X);
        Add(val.Y);
    }

    public void Add(Quaternion val)
    {
        Add(val.X);
        Add(val.Y);
        Add(val.Z);
        Add(val.W);
    }

    public void Add(Matrix4x4 val)
    {
        Add(val.M11); Add(val.M12); Add(val.M13); Add(val.M14);
        Add(val.M21); Add(val.M22); Add(val.M23); Add(val.M24);
        Add(val.M31); Add(val.M32); Add(val.M33); Add(val.M34);
        Add(val.M41); Add(val.M42); Add(val.M43); Add(val.M44);
    }
}
