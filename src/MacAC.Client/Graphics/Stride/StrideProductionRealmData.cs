using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics.Stride;

internal sealed class StrideProductionRealmData : IStrideFrameRealmData
{
    private readonly StrollStructureRegistry _structures;
    private readonly ProxyRegistry _shades;
    private RenderStageProbe _tableau;
    private uint _tupleLbIdent;

    private readonly Dictionary<uint, StrideFrameStaticRecords> _chamberStash = [];
    private readonly Dictionary<uint, StrideFrameStaticRecords> _chamberDynamicStash = [];
    private readonly Dictionary<uint, StrideFrameStaticRecords> _chamberObjectsStash = [];
    private readonly Dictionary<uint, List<RenderMirrorRecord>> _shellsByMooring = [];
    private readonly Dictionary<uint, StrideFrameStaticRecords> _exteriorMaterialized = [];
    private readonly Dictionary<uint, StrideFrameStaticRecords> _exteriorDynamicsMaterialized = [];
    private readonly Dictionary<uint, StrideFrameStaticRecords> _exteriorObjectsMaterialized = [];
    private readonly Dictionary<uint, StrideFrameStaticRecords> _shellMaterialized = [];

    private readonly HashSet<uint> _unregisteredActorsThisCycle = [];

    private RenderMirrorRecord[] _sweepTemp = new RenderMirrorRecord[1024];

    private RenderMirrorRecord[] _chamberLensTemp = new RenderMirrorRecord[64];

    private RenderMirrorRecord[] _arena = new RenderMirrorRecord[4096];
    private int _arenaLen;

    internal StrideProductionRealmData(
        StrollStructureRegistry buildings,
        ProxyRegistry shadows)
    {
        _structures = buildings ?? throw new ArgumentNullException(nameof(buildings));
        _shades = shadows ?? throw new ArgumentNullException(nameof(shadows));
    }

    public int UnregisteredRasterizeMembershipTally { get; private set; }

    public (RenderStageEpoch Generation, uint TupleLandblockId) KeptCtx =>
        (_tableau.Generation, _tupleLbIdent);

    public ulong? FetchExteriorChamberRasterizeRev(uint chamberIdent) =>
        _shades.FetchChamberRasterizeRev(chamberIdent);

    public bool TryFetchLatestProj(uint ownActorIdent, out RenderMirrorRecord capture) =>
        _tableau.TryFetchByOwnActorIdent(ownActorIdent, out capture);

    public StrideFrameStaticRecords FetchChamberStatics(uint chamberIdent)
    {
        if (_chamberStash.TryGetValue(chamberIdent, out StrideFrameStaticRecords stashed))
            return stashed;
        var records = LocateChamberLens(chamberIdent, dynamic: false);
        _chamberStash[chamberIdent] = records;
        return records;
    }

    public StrideFrameStaticRecords FetchChamberObjects(uint chamberIdent)
    {
        if (_chamberObjectsStash.TryGetValue(chamberIdent, out StrideFrameStaticRecords stashed))
            return stashed;
        var records = LocateChamberLens(chamberIdent, dynamic: null);
        _chamberObjectsStash[chamberIdent] = records;
        return records;
    }

    public StrideFrameStaticRecords FetchChamberDynamics(uint chamberIdent)
    {
        if (_chamberDynamicStash.TryGetValue(chamberIdent, out StrideFrameStaticRecords stashed))
            return stashed;
        var records = LocateChamberLens(chamberIdent, dynamic: true);
        _chamberDynamicStash[chamberIdent] = records;
        return records;
    }

    public StrideFrameStaticRecords FetchExteriorStatics(uint chamberIdent)
    {
        if (_exteriorMaterialized.TryGetValue(chamberIdent, out StrideFrameStaticRecords stashed))
            return stashed;
        var records = LocateChamberLens(chamberIdent, dynamic: false);
        _exteriorMaterialized[chamberIdent] = records;
        return records;
    }

    public StrideFrameStaticRecords FetchExteriorDynamics(uint chamberIdent)
    {
        if (_exteriorDynamicsMaterialized.TryGetValue(
                chamberIdent,
                out StrideFrameStaticRecords stashed))

            return stashed;

        var records = LocateChamberLens(chamberIdent, dynamic: true);
        _exteriorDynamicsMaterialized[chamberIdent] = records;
        return records;
    }

    public StrideFrameStaticRecords FetchExteriorObjects(uint chamberIdent)
    {
        if (_exteriorObjectsMaterialized.TryGetValue(
                chamberIdent,
                out StrideFrameStaticRecords stashed))

            return stashed;

        var records = LocateChamberLens(chamberIdent, dynamic: null);
        _exteriorObjectsMaterialized[chamberIdent] = records;
        return records;
    }

    public StrideFrameStaticRecords FetchStructureShellStatics(StrollStructure structure)
    {
        uint mooring = StructureShellBinChamberIdent(structure);
        if (mooring is 0)
            return StrideFrameStaticRecords.Empty with { TupleLandblockId = _tupleLbIdent };
        if (_shellMaterialized.TryGetValue(mooring, out StrideFrameStaticRecords stashed))
            return stashed;
        StrideFrameStaticRecords records =
            _shellsByMooring.TryGetValue(mooring, out List<RenderMirrorRecord>? shells)
                && shells.Count > 0
                ? new StrideFrameStaticRecords(
                    AffixToArena(CollectionsMarshal.AsSpan(shells)), _tupleLbIdent)
                : StrideFrameStaticRecords.Empty with { TupleLandblockId = _tupleLbIdent };
        _shellMaterialized[mooring] = records;
        return records;
    }

    public Matrix4x4 FetchStructureRealmXform(StrollStructure structure)
    {
        return !_structures.TryFetchListing(structure, out StrideBuildingMint.Entry? listing)
            ? throw new InvalidOperationException(
                $"walk building 0x{structure.LocusChamberIdent:X8} has no committed registry entry")
            : listing.PieceZeroRealmXform;
    }

    internal void BeginFrame(
        RenderStageProbe tableau,
        uint tupleLbIdent,
        int rasterizeMiddleLbX,
        int rasterizeMiddleLbY)
    {
        _tableau = tableau;
        _tupleLbIdent = tupleLbIdent;
        _chamberStash.Clear();
        _chamberDynamicStash.Clear();
        _chamberObjectsStash.Clear();
        _exteriorMaterialized.Clear();
        _exteriorDynamicsMaterialized.Clear();
        _exteriorObjectsMaterialized.Clear();
        _shellMaterialized.Clear();
        _arenaLen = 0;
        UnregisteredRasterizeMembershipTally = 0;
        _unregisteredActorsThisCycle.Clear();
        foreach (List<RenderMirrorRecord> bin in _shellsByMooring.Values)
            bin.Clear();

        int needed = _tableau.OrdinalCounts.For(RenderStageIndex.OutdoorStatic);
        if (needed > _sweepTemp.Length)
        {
            _sweepTemp = new RenderMirrorRecord[
                Math.Max(needed, _sweepTemp.Length * 2)];
        }
        int tally = _tableau.DuplicateOrdinalTo(RenderStageIndex.OutdoorStatic, _sweepTemp);
        for (int idx = 0; idx < tally; ++idx)
        {
            ref readonly RenderMirrorRecord capture = ref _sweepTemp[idx];
            if (!capture.EntityPayload.IsBuildingShell)
                continue;
            uint mooring = StructureShellBinChamberIdent(in capture);
            if (!_shellsByMooring.TryGetValue(mooring, out List<RenderMirrorRecord>? shells))
                _shellsByMooring[mooring] = shells = [];
            shells.Add(capture);
        }
    }

    internal static uint MooringChamberIdent(StrollStructure structure)
    {
        foreach (ref readonly StrideBldPortal gateway in structure.Portals.AsSpan())
        {
            if (gateway.OtherCellId != 0xFFFFFFFFu)
                return gateway.OtherCellId;
        }
        return 0;
    }

    internal static uint StructureShellBinChamberIdent(in RenderMirrorRecord capture)
    {
        return capture.Source.BuildingShellAnchorCellId is not 0
                ? capture.Source.BuildingShellAnchorCellId
                : capture.Source.EffectCellId;
    }

    internal static uint StructureShellBinChamberIdent(StrollStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        uint mooring = MooringChamberIdent(structure);
        return mooring is not 0 ? mooring : structure.LocusChamberIdent;
    }

    private StrideFrameStaticRecords LocateChamberLens(uint chamberIdent, bool? dynamic)
    {
        var listings =
            _shades.FetchCanonPieceListingsInChamber(chamberIdent);
        if (listings.Count is 0)
            return StrideFrameStaticRecords.Empty with { TupleLandblockId = _tupleLbIdent };

        int written = 0;
        bool done = true;
        uint earlierActorIdent = 0;
        bool haveEarlier = false;
        for (int idx = 0; idx < listings.Count; ++idx)
        {
            uint actorIdent = listings[idx].EntityId;
            if (haveEarlier && actorIdent == earlierActorIdent)
                continue;
            earlierActorIdent = actorIdent;
            haveEarlier = true;

            if (!_tableau.TryFetchByOwnActorIdent(actorIdent, out RenderMirrorRecord capture))
            {
                done = false;
                if (_unregisteredActorsThisCycle.Add(actorIdent))
                    ++UnregisteredRasterizeMembershipTally;
                continue;
            }
            if (capture.EntityPayload.IsBuildingShell)
                continue; // buildings draw at their own shell turn
            if (dynamic.HasValue
                && IsDynamicProjClass(capture.ProjectionClass) != dynamic.Value)
                continue;

            if (written == _chamberLensTemp.Length)
            {
                RenderMirrorRecord[] grown = new RenderMirrorRecord[_chamberLensTemp.Length * 2];
                Array.Copy(_chamberLensTemp, grown, written);
                _chamberLensTemp = grown;
            }
            _chamberLensTemp[written++] = capture;
        }

        return written is 0
            ? StrideFrameStaticRecords.Empty with { TupleLandblockId = _tupleLbIdent, IsComplete = done }
            : new StrideFrameStaticRecords(
                AffixToArena(_chamberLensTemp.AsSpan(0, written)), _tupleLbIdent, done);
    }

    private static bool IsDynamicProjClass(RenderMirrorClass projClass)
    {
        return projClass is RenderMirrorClass.LiveDynamicRoot
            or RenderMirrorClass.EquippedChild;
    }

    private ArraySegment<RenderMirrorRecord> AffixToArena(
        ReadOnlySpan<RenderMirrorRecord> src)
    {
        if (src.Length is 0)
            return ArraySegment<RenderMirrorRecord>.Empty;

        int needed = _arenaLen + src.Length;
        if (needed > _arena.Length)
        {
            RenderMirrorRecord[] grown = new RenderMirrorRecord[Math.Max(needed, _arena.Length * 2)];
            Array.Copy(_arena, grown, _arenaLen);
            _arena = grown;
        }

        src.CopyTo(_arena.AsSpan(_arenaLen, src.Length));
        var segment = new ArraySegment<RenderMirrorRecord>(_arena, _arenaLen, src.Length);
        _arenaLen += src.Length;
        return segment;
    }
}
