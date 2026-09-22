using System.Numerics;
using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Kinetics;

// The terrain triangle under a point, lifted into world space with its water reading
internal readonly record struct TerrainFootingSample(
    Plane Plane,
    TerrainTriVerts Vertices,
    float WaterDepth,
    bool IsWater,
    uint CellId);

public sealed partial class KineticEngine
{
    private const uint StemBitmask = 0xFFFF0000u;
    private const uint LoBitmask = 0xFFFFu;
    private const float ChunkFlank = 192f;

    private ContactWorldStateSlot _world;
    private readonly ShiftScratch? _temp;
    private readonly HashSet<uint> _residencyTemp = [];
    private KineticAssetCache? _blobStash;
    private ClientThingChart? _objects;
    private ulong _objectsMappingRev;

    public KineticEngine() : this(reuseChangeoverTemp: true)
    {
    }

    internal KineticEngine(bool reuseChangeoverTemp)
    {
        _world = new ContactWorldStateSlot();
        ShadeObjects = new ProxyRegistry(_world);
        _temp = reuseChangeoverTemp ? new ShiftScratch() : null;
    }

    private Dictionary<uint, LandblockKinetics> Landblocks => _world.Current.Landblocks;
    private List<uint> LbSockets => _world.Current.LbSockets;
    private Dictionary<uint, int> LbOrdinals => _world.Current.LbOrdinals;
    private Stack<int> LbSpareSockets => _world.Current.LbSpareSockets;

    public bool IsLbLandHoused(uint chamberOrLbIdent)
    {
        uint stem = chamberOrLbIdent & StemBitmask;
        foreach ((uint tag, _) in Landblocks)
        {
            if ((tag & StemBitmask) == stem)
                return true;
        }
        return false;
    }

    public bool IsNeighborhoodLandHoused(uint chamberOrLbIdent, int radius)
    {
        var housed = _residencyTemp;
        housed.Clear();
        foreach ((uint tag, _) in Landblocks)
            housed.Add(tag & StemBitmask);

        int cx = (int)((chamberOrLbIdent >> 24) & 0xFFu);
        int cy = (int)((chamberOrLbIdent >> 16) & 0xFFu);
        for (int dx = -radius; dx <= radius; ++dx)
        {
            for (int dy = -radius; dy <= radius; ++dy)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || nx > 254 || ny < 0 || ny > 254)
                    continue;   // off-map: skip
                if (!housed.Contains(((uint)nx << 24) | ((uint)ny << 16)))
                    return false;
            }
        }
        return true;
    }

    public int LandblockTally => Landblocks.Count;

    internal ContactWorldStateSlot ImpactRealm => _world;

    public ProxyRegistry ShadeObjects { get; }

    public Action<string>? ProbeTrace { get; set; }

    internal Func<Changeover, ShiftCellContactPhase, uint, ShiftVerdict, ShiftVerdict>? ChangeoverChamberImpactTestTap { get; set; }

    internal Func<double> SetLocusRandomUnit { get; set; } = Random.Shared.NextDouble;

    internal ulong ObjectsMappingRev => _objectsMappingRev;

    public ClientThingChart? Objects
    {
        get => _objects;
        set
        {
            if (ReferenceEquals(_objects, value))
                return;
            _objects = value;
            _objectsMappingRev = checked(_objectsMappingRev + 1UL);
        }
    }

    public KineticAssetCache? DataCache
    {
        get => _blobStash;
        set
        {
            if (value is not null && !ReferenceEquals(_world, value.ImpactRealm))
            {
                if (ShadeObjects.SumRegistered is not 0)
                    throw new InvalidOperationException("A populated physics engine can't change collision roots");
                if (Landblocks.Count is not 0)
                {
                    if (_blobStash is not null)
                        throw new InvalidOperationException("A populated physics engine can't change collision roots");
                    CarryLbsTo(value.ImpactRealm);
                }
                _world = value.ImpactRealm;
                ShadeObjects.FastenImpactRealm(_world);
            }
            _blobStash = value;
            ShadeObjects.DataCache = value;
        }
    }

    public bool TryFetchLbCtx(float realmX, float realmY, out uint lbIdent, out float realmShiftX, out float realmShiftY)
    {
        if (TryLocalize(realmX, realmY, out lbIdent, out LandblockKinetics kinetics, out _, out _))
        {
            realmShiftX = kinetics.WorldOffsetX;
            realmShiftY = kinetics.WorldOffsetY;
            return true;
        }
        realmShiftX = 0f;
        realmShiftY = 0f;
        return false;
    }

    public float? TasteLandZ(float realmX, float realmY)
    {
        return TryLocalize(realmX, realmY, out _, out LandblockKinetics kinetics, out float lx, out float ly) ? kinetics.Terrain.ProbeZ(lx, ly) : null;
    }

    internal sealed record LandblockKinetics(
        LandCanvas Terrain,
        IReadOnlyList<CellFacet> Cells,
        IReadOnlyList<PortalFace> Portals,
        float WorldOffsetX,
        float WorldOffsetY);

    public float ProbeWaterZDepth(float realmX, float realmY)
    {
        return TryLocalize(realmX, realmY, out _, out LandblockKinetics kinetics, out float lx, out float ly) ? kinetics.Terrain.TasteWaterZDepth(lx, ly) : 0f;
    }

    public Plane? ProbeLandPlane(float realmX, float realmY)
    {
        if (!TryLocalize(realmX, realmY, out _, out LandblockKinetics kinetics, out float lx, out float ly))
            return null;

        (float z, Vector3 norm) = kinetics.Terrain.ProbeCanvas(lx, ly);
        float d = -(norm.X * realmX + norm.Y * realmY + norm.Z * z);
        return new Plane(norm, d);
    }

    public void RefreshAvatarCurrChamber(uint chamberIdent)
    {
        if (DataCache?.ChamberGraph is { } graph && graph.ObtainShown(chamberIdent) is { } chamber)
            graph.CurrChamber = chamber;
    }

    public bool IsSummonChamberPrimed(uint chamberIdent)
    {
        return (chamberIdent & LoBitmask) < 0x0100u || DataCache?.FetchChamberStruct(chamberIdent) is not null;
    }

    public (uint cellId, bool found) TuneLocus(uint seedChamberIdent, Vector3 realmPt)
    {
        if (seedChamberIdent is 0u)
            return (seedChamberIdent, false);

        if ((seedChamberIdent & LoBitmask) >= 0x0100u)
        {
            if (DataCache is null)
                return (seedChamberIdent, false);
            uint descendant = CellHop.FindVisibleChildCell(DataCache, seedChamberIdent, realmPt, useStabRoster: true);
            if (descendant is not 0u)
                return (descendant, true);

            // Only a cell that opens to the outside may fall through to terrain
            var claimed = DataCache.FetchChamberStruct(seedChamberIdent);
            if (claimed is null || !claimed.SeenOutside)
                return (seedChamberIdent, false);
        }

        return TryExteriorChamber(realmPt, out uint chamberIdent) ? (chamberIdent, true) : (seedChamberIdent, false);
    }

    internal void AddLandblock(uint lbIdent, LandCanvas land,
        IReadOnlyList<CellFacet> chambers, IReadOnlyList<PortalFace> gateways,
        float realmShiftX, float realmShiftY)
    {
        SetupLbReplicate(lbIdent, new LandblockKinetics(land, chambers, gateways, realmShiftX, realmShiftY));

        DataCache?.ChamberGraph.EnrollLand(lbIdent, land, new Vector3(realmShiftX, realmShiftY, 0f));
    }

    // Remove a previously registered landblock, including its shadow objects
    internal void RemoveLandblock(uint lbIdent)
    {
        Landblocks.Remove(lbIdent);
        FreeSocket(lbIdent);
        EvictLbInsides(lbIdent, insideLatestSole: false);

        DataCache?.ChamberGraph.RemoveLandblock(lbIdent);
    }

    internal void Clear()
    {
        if (Landblocks.Count is not 0)
        {
            uint[] idents = new uint[Landblocks.Count];
            Landblocks.Keys.CopyTo(idents, 0);
            foreach (uint lbIdent in idents)
                RemoveLandblock(lbIdent);
        }
        ShadeObjects.Clear();
    }

    // Keeps a landblock's terrain but sheds its interiors, buildings and statics
    internal void DemoteLandblockToTerrain(uint lbIdent)
    {
        uint canon = (lbIdent & StemBitmask) | LoBitmask;
        if (Landblocks.TryGetValue(canon, out LandblockKinetics? lb))
            Landblocks[canon] = lb with { Cells = [], Portals = [] };

        ShadeObjects.DeregisterStaticHoldersForLb(canon);
        ShadeObjects.RemoveLandblock(canon);
        DataCache?.DropChambersForLb(canon);
        DataCache?.DropStructuresForLb(canon);
        DataCache?.ChamberGraph.DropEnvironChambersForLb(canon);

        if (DataCache?.ChamberGraph is { CurrChamber: { } latest } graph
            && (latest.Id & StemBitmask) == (canon & StemBitmask)
            && (latest.Id & LoBitmask) >= 0x0100u)

            graph.CurrChamber = null;
    }

    internal TerrainFootingSample? ProbeLandPassable(float realmX, float realmY)
    {
        return TryLocalize(realmX, realmY, out uint ident, out LandblockKinetics kinetics, out float lx, out float ly)
            ? Footing(ident, kinetics, lx, ly)
            : null;
    }

    // Like ProbeLandPassable, but only if the point lies inside the named land cell
    internal TerrainFootingSample? ProbeLandPassableInChamber(uint chamberIdent, float realmX, float realmY)
    {
        uint lo = chamberIdent & LoBitmask;
        if (lo is < 1u or > 0x40u)
            return null;

        uint wantedStem = chamberIdent & StemBitmask;
        foreach ((uint ident, LandblockKinetics kinetics) in Landblocks)
        {
            if (wantedStem is not 0u && (ident & StemBitmask) != wantedStem)
                continue;

            float lx = realmX - kinetics.WorldOffsetX;
            float ly = realmY - kinetics.WorldOffsetY;
            if (wantedStem is 0u && (lx < 0f || lx >= ChunkFlank || ly < 0f || ly >= ChunkFlank))
                continue;

            int chamberOrdinal = (int)lo - 1;
            float lowerX = (chamberOrdinal / LandCanvas.ChambersPerFlank) * LandCanvas.ChamberDims;
            float lowerY = (chamberOrdinal % LandCanvas.ChambersPerFlank) * LandCanvas.ChamberDims;
            bool insideChamber = lx >= lowerX && lx < lowerX + LandCanvas.ChamberDims && ly >= lowerY && ly < lowerY + LandCanvas.ChamberDims;
            return insideChamber ? Footing(ident, kinetics, lx, ly) : null;
        }

        return null;
    }

    internal uint LocateChamberIdent(Vector3 realmSpot, float orbRadius, uint backupChamberIdent)
    {
        if (backupChamberIdent is 0)
            return 0;

        if ((backupChamberIdent & LoBitmask) >= 0x0100u)
            return backupChamberIdent;

        return TryExteriorChamber(realmSpot, out uint chamberIdent) ? chamberIdent : backupChamberIdent;
    }

    private Changeover RentChangeover() => _temp?.Rent() ?? new Changeover();

    private void YieldChangeover(Changeover changeover) => _temp?.Yield(changeover);

    private void CarryLbsTo(ContactWorldStateSlot destSocket)
    {
        var src = _world.Current;
        var dest = destSocket.Current;
        bool destOwnsLbs = dest.Landblocks.Count is not 0
            || dest.LbSockets.Count is not 0
            || dest.LbOrdinals.Count is not 0
            || dest.LbSpareSockets.Count is not 0;
        if (destOwnsLbs)
            throw new InvalidOperationException("A cache collision root by now owns engine landblocks");

        foreach ((uint lbIdent, LandblockKinetics lb) in src.Landblocks)
            dest.Landblocks.Add(lbIdent, lb);
        CarryLbsToRest(src, dest);
    }

    private void CarryLbsToRest(ContactWorldState src, ContactWorldState dest)
    {
        dest.LbSockets.AddRange(src.LbSockets);
        foreach ((uint lbIdent, int socket) in src.LbOrdinals)
            dest.LbOrdinals.Add(lbIdent, socket);
        int[] release = src.LbSpareSockets.ToArray();
        for (int idx = release.Length - 1; idx >= 0; --idx)
            dest.LbSpareSockets.Push(release[idx]);
    }

    private KineticAssetCache DemandStash(string role)
    {
        return DataCache ?? throw new InvalidOperationException($"{role} collision engine has no data cache");
    }

    private void ClaimSocket(uint lbIdent)
    {
        if (LbOrdinals.ContainsKey(lbIdent))
            return;
        if (LbSpareSockets.TryPop(out int reused))
        {
            LbSockets[reused] = lbIdent;
            LbOrdinals[lbIdent] = reused;
            return;
        }
        LbOrdinals[lbIdent] = LbSockets.Count;
        LbSockets.Add(lbIdent);
    }

    private void FreeSocket(uint lbIdent)
    {
        if (!LbOrdinals.Remove(lbIdent, out int socket))
            return;
        LbSockets[socket] = 0u;
        LbSpareSockets.Push(socket);
    }

    private void SetupLbReplicate(uint lbIdent, LandblockKinetics lb)
    {
        Landblocks[lbIdent] = lb;
        ClaimSocket(lbIdent);
    }

    // Drops the cache's cells, buildings and shadows for a landblock, and forgets the current cell if
    // it lived there
    private void EvictLbInsides(uint lbIdent, bool insideLatestSole)
    {
        ShadeObjects.DeregisterStaticHoldersForLb(lbIdent);
        ShadeObjects.RemoveLandblock(lbIdent);
        DataCache?.DropChambersForLb(lbIdent);
        DataCache?.DropStructuresForLb(lbIdent);

        if (DataCache?.ChamberGraph is { CurrChamber: { } latest } graph
            && (latest.Id & StemBitmask) == (lbIdent & StemBitmask)
            && (!insideLatestSole || (latest.Id & LoBitmask) >= 0x0100u))

            graph.CurrChamber = null;
    }

    // The first resident landblock whose 192 m square holds the point, with the point in block-local
    // coordinates
    private bool TryLocalize(float realmX, float realmY, out uint lbIdent, out LandblockKinetics landblock, out float ownX, out float ownY)
    {
        foreach ((uint ident, LandblockKinetics kinetics) in Landblocks)
        {
            float lx = realmX - kinetics.WorldOffsetX;
            float ly = realmY - kinetics.WorldOffsetY;
            if (lx >= 0f && lx < ChunkFlank && ly >= 0f && ly < ChunkFlank)
            {
                lbIdent = ident;
                landblock = kinetics;
                ownX = lx;
                ownY = ly;
                return true;
            }
        }

        lbIdent = 0;
        landblock = null!;
        ownX = 0f;
        ownY = 0f;
        return false;
    }

    private static TerrainFootingSample Footing(uint lbIdent, LandblockKinetics kinetics, float ownX, float ownY)
    {
        var facet = kinetics.Terrain.ProbeCanvasPolyg(ownX, ownY);
        TerrainTriVerts corners = new TerrainTriVerts(
            ToRealm(facet.Vertices.V0, kinetics),
            ToRealm(facet.Vertices.V1, kinetics),
            ToRealm(facet.Vertices.V2, kinetics));

        Vector3 norm = facet.Normal;
        Plane plane = new Plane(norm, -Vector3.Dot(norm, corners[0]));

        float waterZDepth = kinetics.Terrain.TasteWaterZDepth(ownX, ownY);
        uint chamberIdent = (lbIdent & StemBitmask) | kinetics.Terrain.ComputeOutdoorCellId(ownX, ownY);
        return new TerrainFootingSample(plane, corners, waterZDepth, waterZDepth >= 0.45f, chamberIdent);
    }

    private static Vector3 ToRealm(Vector3 vert, LandblockKinetics kinetics)
    {
        return new(vert.X + kinetics.WorldOffsetX, vert.Y + kinetics.WorldOffsetY, vert.Z);
    }

    // The outdoor land cell under a world point, when resident
    private bool TryExteriorChamber(Vector3 realmPt, out uint chamberIdent)
    {
        if (TryLocalize(realmPt.X, realmPt.Y, out uint ident, out LandblockKinetics kinetics, out float lx, out float ly))
        {
            chamberIdent = (ident & StemBitmask) | kinetics.Terrain.ComputeOutdoorCellId(lx, ly);
            return true;
        }
        chamberIdent = 0u;
        return false;
    }
}
