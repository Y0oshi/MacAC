using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Stage;

namespace MacAC.Client.Graphics.Stride;

internal readonly record struct StrideFrameStaticRecords(
    ArraySegment<RenderMirrorRecord> Records, uint TupleLandblockId,
    bool IsComplete = true)
{
    public static readonly StrideFrameStaticRecords Empty =
        new(ArraySegment<RenderMirrorRecord>.Empty, 0);
}

internal interface IStrideFrameRealmData
{
    (RenderStageEpoch Generation, uint TupleLandblockId) KeptCtx => default;

    ulong? FetchExteriorChamberRasterizeRev(uint chamberIdent) => null;

    bool TryFetchLatestProj(uint ownActorIdent, out RenderMirrorRecord capture)
    {
        capture = default;
        return false;
    }

    StrideFrameStaticRecords FetchChamberObjects(uint chamberIdent);

    StrideFrameStaticRecords FetchExteriorObjects(uint chamberIdent);

    StrideFrameStaticRecords FetchChamberStatics(uint chamberIdent);

    StrideFrameStaticRecords FetchChamberDynamics(uint chamberIdent);

    StrideFrameStaticRecords FetchExteriorStatics(uint chamberIdent);

    StrideFrameStaticRecords FetchExteriorDynamics(uint chamberIdent);

    StrideFrameStaticRecords FetchStructureShellStatics(StrollStructure structure);

    Matrix4x4 FetchStructureRealmXform(StrollStructure structure);
}

internal interface IStrideFrameLeafPainter
{
    void SketchHeavens();

    void PaintLandChamberLot(
        IReadOnlyList<(uint LandblockId, int SideCellCount, int CellIndex)> chambers);

    bool HasRenderableSpoutsInChamber(uint chamberIdent);

    void PaintChamberShell(uint chamberIdent);

    ReadOnlySpan<PreparedMoteAlphaSubmission> ReadyStaticMotes(uint chamberIdent);

    ReadOnlySpan<PreparedMoteAlphaSubmission> ReadyChamberMotes(uint chamberIdent);

    void WipeInteriorDepth();

    int PaintQuitSeals();

    void DrainScenery();

    void PaintPunchFan(StridePolygon realmPolyg, int engagedLensOrdinal);

    void AlphaBarrier();

    void DrainOrderChamberQuit();
}

internal interface IStrideFrameDriverTrace
{
    void OnDrain(int directiveTally, IReadOnlyList<StrollPaintJuncture> junctures);
}

internal enum StrideFrameEventKind : byte
{
    StreamMark,

    AlphaSubmitMark,

    Sky,

    GroundCell,

    CellShell,

    PunchFan,

    AlphaBarrier,

    LandscapeFlush,

    ClearInteriorDepth,

    ExitSeals,

    StaticParticles,

    CellParticles,

    SortCellExit,
}

internal interface IStrideLookInViewSource
{
    IReadOnlyList<uint> GazeInCellTurns { get; }

    bool OrbShownInGazeInPivot(
        int courseOrdinal,
        in Vector3 middle,
        float radius,
        bool testOrb = true);

    string DepictGazeInPivot(
        int courseOrdinal,
        in Vector3 middle,
        float radius) => "unavailable";
}

internal readonly record struct StrideLookInSlice(
    int PlaneStart,
    int PlaneCount,
    uint ClipSlot);

internal readonly record struct StrideLookInTurn(
    uint CellId,
    int SliceStart,
    int SliceCount);

internal readonly struct StrideFrameEvent
{
    private StrideFrameEvent(
        StrideFrameEventKind sort, int intArgument, uint chamberIdent, float floatArgument, StridePolygon? polyg)
    {
        Kind = sort;
        IntArgument = intArgument;
        CellId = chamberIdent;
        FloatArgument = floatArgument;
        Polygon = polyg;
    }

    internal StrideFrameEventKind Kind { get; }

    internal int IntArgument { get; }

    internal uint CellId { get; }

    internal float FloatArgument { get; }

    internal StridePolygon? Polygon { get; }

    internal static StrideFrameEvent Flag(int exclusiveFinish) =>
        new(StrideFrameEventKind.StreamMark, exclusiveFinish, 0, 0f, null);

    internal static StrideFrameEvent AlphaSubmitFlag(int exclusiveFinish)
    {
        return new(StrideFrameEventKind.AlphaSubmitMark, exclusiveFinish, 0, 0f, null);
    }

    internal static StrideFrameEvent Sky() =>
        new(StrideFrameEventKind.Sky, 0, 0, 0f, null);

    internal static StrideFrameEvent TerrainChamber(uint lbIdent, int flankChamberTally, int chamberOrdinal)
    {
        return new(StrideFrameEventKind.GroundCell, (flankChamberTally << 8) | chamberOrdinal, lbIdent, 0f, null);
    }

    internal static StrideFrameEvent ChamberShell(uint chamberIdent) =>
        new(StrideFrameEventKind.CellShell, 0, chamberIdent, 0f, null);

    internal static StrideFrameEvent PunchFan(StridePolygon realmPolyg, int engagedLensOrdinal)
    {
        return new(StrideFrameEventKind.PunchFan, engagedLensOrdinal, 0, 0f, realmPolyg);
    }

    internal static StrideFrameEvent AlphaBarrier() =>
        new(StrideFrameEventKind.AlphaBarrier, 0, 0, 0f, null);

    internal static StrideFrameEvent SceneryChamberMotes(
        uint chamberIdent, int alphaExclusiveFinish, bool includeMotes)
    {
        return new(
            StrideFrameEventKind.StaticParticles,
            alphaExclusiveFinish,
            chamberIdent,
            includeMotes ? 1f : 0f,
            null);
    }

    internal static StrideFrameEvent ChamberMotes(
        uint chamberIdent, int alphaExclusiveFinish, bool includeMotes)
    {
        return new(
            StrideFrameEventKind.CellParticles,
            alphaExclusiveFinish,
            chamberIdent,
            includeMotes ? 1f : 0f,
            null);
    }

    internal static StrideFrameEvent SceneryDrain() =>
        new(StrideFrameEventKind.LandscapeFlush, 0, 0, 0f, null);

    internal static StrideFrameEvent WipeInteriorZDepth() =>
        new(StrideFrameEventKind.ClearInteriorDepth, 0, 0, 0f, null);

    internal static StrideFrameEvent QuitSeals() =>
        new(StrideFrameEventKind.ExitSeals, 0, 0, 0f, null);

    internal static StrideFrameEvent OrderChamberQuit() =>
        new(StrideFrameEventKind.SortCellExit, 0, 0, 0f, null);
}

internal sealed partial class StrollCycleDriver : IStrollSignalDrain, IStrideLookInViewSource
{
    private readonly RealmPaintRouter _router;

    private readonly StrideStaticStreamFiller _populator;

    private readonly FarLandscapeDrawShelf _farDrawCache;

    private IStrideFrameLeafPainter _leafPainter;

    private readonly IStrideFrameRealmData _realmBlob;

    private readonly IStrideFrameDriverTrace? _trace;

    private ClipCycle? _clipCycle;

    private readonly SequencedPaintFlow _flow = new();

    private readonly List<StrideFrameEvent> _signals = [];

    private readonly List<int> _flagLoci = [];

    private readonly List<RealmPaintRouter.StrideClassifiedBatch> _alphaSubmissions = [];

    private int _alphaSubmitFlag;

    internal HashSet<uint> VisitedChambers { get; } = [];

    internal List<uint> GazeInChamberTurns { get; } = [];

    private readonly List<StrideLookInTurn> _gazeInTurns = [];

    private readonly List<StrideLookInSlice> _gazeInSlices = [];

    private readonly List<StridePlane> _gazeInPlanes = [];

    private readonly List<int> _floodLensCourseTemp = [];

    private StridePlane _gazeInCyPlane;

    private readonly List<(uint LandblockId, int SideCellCount, int CellIndex)> _queuedLandLot = [];

    private readonly HashSet<uint> _chamberShellsDrawnThisCycle = [];

    private readonly HashSet<uint> _chamberMoteTurnsDrawnThisCycle = [];

    internal int GatewaysDrawnTally;

    internal HashSet<uint> GazeInChambers { get; } = [];

    internal List<uint> InteriorFloodChambers { get; } = [];

    internal List<StrollStructure> VisitedStructures { get; } = [];

    internal HashSet<uint> VisitedSceneryChamberIdents { get; } = [];

    private IStrideBuildingFrameScope? _cx;

    private Matrix4x4 _lensProj;

    private Vector3 _camRealmLocus;

    private int _sceneryTurnsThisCycle;

    private StrollPaintJuncture? _latestDcJuncture;

    private bool _primedToRerun;

    private int _chamberLensCourseOrdinal;

    private int _sceneryLensCourseOrdinal;

    private int _transcriptCycleNumber;

    internal StrollCycleDriver(
        RealmPaintRouter dispatcher,
        IStrideFrameLeafPainter leafRenderer,
        IStrideFrameRealmData worldData,
        IStrideFrameDriverTrace? trace = null,
        ClipCycle? clipCycle = null)
    {
        _router = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _leafPainter = leafRenderer ?? throw new ArgumentNullException(nameof(leafRenderer));
        _realmBlob = worldData ?? throw new ArgumentNullException(nameof(worldData));
        _trace = trace;
        _clipCycle = clipCycle;
        _populator = new StrideStaticStreamFiller(dispatcher);
        _farDrawCache = new FarLandscapeDrawShelf(dispatcher, worldData);
    }
}
