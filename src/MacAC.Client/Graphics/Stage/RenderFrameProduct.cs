using System.Numerics;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Graphics.Stage;

internal enum RasterizeCycleBlendClass : byte
{
    Opaque,
    Alpha,
}

internal enum RasterizeCycleContenderCourse : byte
{
    LandscapeOutdoorStatic,
    LandscapeBuildingShell,
    LandscapeOutsideDynamic,
    LookInObject,
    CellStatic,
    DynamicLast,
}

internal readonly record struct RasterizeCycleContenderSpan(
    RasterizeCycleContenderCourse Route,
    int RouteIndex,
    uint CellId,
    int Offset,
    int Count);

internal readonly record struct RasterizeCycleChamberSpan(
    uint CellId,
    int PViewOrder,
    int Offset,
    int Count);

internal readonly record struct RasterizeCycleTransformCapture(
    RenderMirrorId ProjectionId,
    int PartIndex,
    Matrix4x4 LocalToWorld);

internal readonly record struct RenderFrameActorCandidate(
    RenderMirrorRecord Projection,
    int MeshPartOffset,
    int MeshPartCount,
    bool Animated);

internal readonly record struct RasterizeCycleTriMeshPiece(
    RenderMirrorId ProjectionId,
    int PartIndex,
    TriMeshRef MeshRef);

internal readonly record struct RasterizeCycleTaxonomyCapture(
    RenderMirrorId ProjectionId,
    int CandidateIndex,
    int MeshRefIndex,
    RasterizeCycleBlendClass BlendClass,
    float Opacity);

internal readonly record struct RasterizeCycleThingLampGroup(
    RenderMirrorId ProjectionId,
    int L0,
    int L1,
    int L2,
    int L3,
    int L4,
    int L5,
    int L6,
    int L7);

internal readonly record struct RenderFramePickingPart(
    RenderMirrorId ProjectionId,
    uint ServerGuid,
    uint LocalEntityId,
    int PartIndex,
    uint GfxObjId,
    Matrix4x4 LocalToWorld,
    CanonPickMesh Mesh);

internal readonly record struct RenderFrameTelemetryCounts(
    int OutdoorStaticCandidates,
    int CellStaticCandidates,
    int DynamicCandidates,
    int TransformCount,
    int OpaqueClassificationCount,
    int AlphaClassificationCount,
    int LightSetCount,
    int SelectionPartCount,
    int RouteCandidateCount,
    int EntityCandidateCount,
    int MeshPartCount);

internal readonly struct RasterizeCycleLens
{
    internal RasterizeCycleLens(
        RasterizeCycleArena arena,
        ulong arenaEpoch,
        ulong borrowTicket)
    {
        Arena = arena;
        _arenaEpoch = arenaEpoch;
        _borrowTicket = borrowTicket;
    }

    public RenderStageEpoch Generation
    {
        get
        {
            var arena = Arena;
            return arena.Generation;
        }
    }

    public ulong CycleSeries
    {
        get
        {
            var arena = Arena;
            return arena.CycleSequence;
        }
    }

    public ReadOnlySpan<RenderMirrorRecord> ExteriorStaticContenders =>
        Arena.ExteriorStaticContenders;

    public ReadOnlySpan<RenderMirrorRecord> ChamberStaticContenders =>
        Arena.ChamberStaticContenders;

    public ReadOnlySpan<RasterizeCycleChamberSpan> ChamberSpans =>
        Arena.ChamberSpans;

    public ReadOnlySpan<RenderMirrorRecord> DynamicContenders =>
        Arena.DynamicContenders;

    public ReadOnlySpan<RasterizeCycleTransformCapture> Transforms =>
        Arena.Xforms;

    public ReadOnlySpan<RenderFrameActorCandidate> ActorContenders =>
        Arena.ActorContenders;

    public ReadOnlySpan<RasterizeCycleTriMeshPiece> TriMeshPieces =>
        Arena.TriMeshPieces;

    public ReadOnlySpan<RasterizeCycleTaxonomyCapture> Classifications =>
        Arena.Classifications;

    public ReadOnlySpan<RasterizeCycleThingLampGroup> LightSets =>
        Arena.LampSets;

    public ReadOnlySpan<RenderFramePickingPart> PickParts =>
        Arena.PickPieces;

    public ReadOnlySpan<RenderMirrorRecord> CourseContenders =>
        Arena.CourseContenders;

    public ReadOnlySpan<RasterizeCycleContenderSpan> CourseSpans =>
        Arena.CourseSpans;

    public RenderFrameTelemetryCounts ProbeCounts =>
        Arena.DiagnosticCounts;

    public RenderStageDigest SrcDigest => Arena.SourceDigest;

    internal RasterizeCycleArena Owner =>
        Arena;

    private readonly ulong _arenaEpoch;

    internal ulong ArenaEpoch => _arenaEpoch;
    private readonly ulong _borrowTicket;

    internal ulong BorrowTicket => _borrowTicket;
    private RasterizeCycleArena Arena
    {
        get
        {
            var arena = field;
            return arena is null
                || !arena.IsBorrowValid(_arenaEpoch, _borrowTicket)
                ? throw new InvalidOperationException(
                    "The render-frame view is no longer borrowed from its arena")
                : arena;
        }
    }
}

internal sealed partial class RasterizeCycleArena
{
    private RenderMirrorRecord[] _exterior = [];

    private RenderMirrorRecord[] _chamberStatics = [];

    private RasterizeCycleChamberSpan[] _chamberSpans = [];

    private RenderMirrorRecord[] _dynamics = [];

    private RasterizeCycleTransformCapture[] _xforms = [];

    private RenderFrameActorCandidate[] _actorContenders = [];

    private RasterizeCycleTriMeshPiece[] _triMeshPieces = [];

    private RasterizeCycleTaxonomyCapture[] _classifications = [];

    private RasterizeCycleThingLampGroup[] _lampSets = [];

    private RenderFramePickingPart[] _pickPieces = [];

    private RenderMirrorRecord[] _courseContenders = [];

    private RasterizeCycleContenderSpan[] _courseSpans = [];

    private int _exteriorTally;

    private int _chamberStaticTally;

    private int _chamberSpanTally;

    private int _dynamicTally;

    private int _xformTally;

    private int _actorContenderTally;

    private int _triMeshPieceTally;

    private int _taxonomyTally;

    private int _lampSetTally;

    private int _pickPieceTally;

    private int _courseContenderTally;

    private int _courseSpanTally;

    private int _solidTaxonomyTally;

    private int _alphaTaxonomyTally;
    private ulong _engagedBorrowTicket;

    private bool _srcDigestSet;

    private ArenaPhase _phase;

    private static void SecureCap<T>(ref T[] vals, int needed)
    {
        if (vals.Length >= needed)
            return;

        int cap = vals.Length is 0 ? 4 : vals.Length;
        while (cap < needed)
            cap = checked(cap * 2);
        Array.Resize(ref vals, cap);
    }

    private enum ArenaPhase : byte
    {
        Available,
        Building,
        Published,
        Borrowed,
    }
}

internal readonly struct RasterizeCycleEmitter
{
    private readonly int _arenaOrdinal;
    private readonly ulong _epoch;

    internal RasterizeCycleEmitter(
        RasterizeCycleSwap holder,
        RasterizeCycleArena arena,
        int arenaOrdinal,
        ulong epoch)
    {
        Owner = holder;
        Arena = arena;
        _arenaOrdinal = arenaOrdinal;
        _epoch = epoch;
    }

    public void AssignSrcDigest(in RenderStageDigest digest) =>
        Arena.AssignSrcDigest(_epoch, in digest);

    public void AppendExterior(in RenderMirrorRecord capture) =>
        Arena.AppendExterior(_epoch, in capture);

    public void AddCellRange(
        uint chamberIdent,
        int pviewOrdering,
        ReadOnlySpan<RenderMirrorRecord> records) =>
        Arena.AppendChamberSpan(_epoch, chamberIdent, pviewOrdering, records);

    public void AddCellRange(
        uint chamberIdent,
        int pviewOrdering,
        in RenderMirrorRecord capture)
    {
        Arena.AppendChamberSpan(_epoch, chamberIdent, pviewOrdering, in capture);
    }

    public void AppendDynamic(in RenderMirrorRecord capture) =>
        Arena.AppendDynamic(_epoch, in capture);

    public void AppendXform(in RasterizeCycleTransformCapture xform) =>
        Arena.AppendXform(_epoch, in xform);

    public void AppendActorContender(
        in RenderMirrorRecord proj,
        bool moving) =>
        Arena.AppendActorContender(_epoch, in proj, moving);

    public void AppendTaxonomy(
        in RasterizeCycleTaxonomyCapture taxonomy) =>
        Arena.AppendTaxonomy(_epoch, in taxonomy);

    public void AppendLampSet(in RasterizeCycleThingLampGroup lampSet) =>
        Arena.AppendLampSet(_epoch, in lampSet);

    public void AppendPickPiece(in RenderFramePickingPart piece) =>
        Arena.AppendPickPiece(_epoch, in piece);

    public void AppendCourseSpan(
        RasterizeCycleContenderCourse course,
        int courseOrdinal,
        uint chamberIdent,
        ReadOnlySpan<RenderMirrorRecord> records)
    {
        Arena.AppendCourseSpan(
            _epoch,
            course,
            courseOrdinal,
            chamberIdent,
            records);
    }

    public void Publish() =>
        Owner.Publish(_arenaOrdinal, _epoch);

    public void Scrap() =>
        Owner.Cancel(_arenaOrdinal, _epoch);

    private RasterizeCycleSwap Owner
    {
        get
        {
            return field
        ?? throw new InvalidOperationException(
            "The render-frame writer is uninitialized");
        }
    }

    private RasterizeCycleArena Arena
    {
        get
        {
            return field
        ?? throw new InvalidOperationException(
            "The render-frame writer is uninitialized");
        }
    }
}

// Owns exactly two reusable arenas
internal sealed class RasterizeCycleSwap
{
    private readonly RasterizeCycleArena[] _arenas =
        [new RasterizeCycleArena(), new RasterizeCycleArena()];
    private int _structureOrdinal = -1;
    private int _publishedOrdinal = -1;
    private ulong _upcomingBorrowTicket;

    public RasterizeCycleEmitter CommenceAssemble(
        RenderStageEpoch gen,
        ulong cycleSeries)
    {
        if (_structureOrdinal >= 0)
        {
            throw new InvalidOperationException(
                "A render-frame build is by now in progress");
        }

        int ordinal = _publishedOrdinal < 0
            ? 0
            : 1 - _publishedOrdinal;
        if (!_arenas[ordinal].CanAssemble)
            ordinal = -1;
        if (ordinal < 0)
        {
            throw new InvalidOperationException(
                "The non-published render-frame arena is still borrowed");
        }

        var arena = _arenas[ordinal];
        ulong epoch = arena.CommenceEmit(gen, cycleSeries);
        _structureOrdinal = ordinal;
        return new RasterizeCycleEmitter(this, arena, ordinal, epoch);
    }

    public RasterizeCycleLens BorrowCurrent(
        RenderStageEpoch anticipatedGen,
        ulong anticipatedCycleSeries)
    {
        if (_publishedOrdinal < 0)
        {
            throw new InvalidOperationException(
                "No completed render frame has been published");
        }

        var arena = _arenas[_publishedOrdinal];
        if (arena.Generation != anticipatedGen
            || arena.CycleSequence != anticipatedCycleSeries)
        {
            throw new InvalidOperationException(
                $"Published frame {arena.Generation}/{arena.CycleSequence} "
                + $"doesn't match requested {anticipatedGen}/{anticipatedCycleSeries}.");
        }

        ulong ticket = checked(++_upcomingBorrowTicket);
        arena.Borrow(arena.Epoch, ticket);
        return new RasterizeCycleLens(arena, arena.Epoch, ticket);
    }

    public void Release(in RasterizeCycleLens lens)
    {
        var arena = lens.Owner;
        if (!ReferenceEquals(arena, _arenas[0])
            && !ReferenceEquals(arena, _arenas[1]))
        {
            throw new InvalidOperationException(
                "The render-frame view belongs to another exchange");
        }

        arena.Release(lens.ArenaEpoch, lens.BorrowTicket);
    }

    internal void Publish(int ordinal, ulong epoch)
    {
        SecureWriter(ordinal);
        var arena = _arenas[ordinal];
        arena.Publish(epoch);
        _publishedOrdinal = ordinal;
        _structureOrdinal = -1;
    }

    internal void Cancel(int ordinal, ulong epoch)
    {
        SecureWriter(ordinal);
        _arenas[ordinal].Cancel(epoch);
        _structureOrdinal = -1;
    }

    private void SecureWriter(int ordinal)
    {
        if (_structureOrdinal != ordinal)
        {
            throw new InvalidOperationException(
                "The render-frame writer is stale or has by now completed");
        }
    }
}
