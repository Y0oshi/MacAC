using System.Numerics;
using MacAC.Client.Graphics.Stride;

namespace MacAC.Client.Graphics;

public sealed class FetchedChamber
{
    public uint CellId;

    public Vector3 RealmPlace;

    public Matrix4x4 WorldTransform;

    public Matrix4x4 InverseWorldTransform;

    public Vector3 OwnLimitsLower;

    public Vector3 OwnLimitsUpper;

    public List<ChamberGatewayDetails> Portals = [];

    public List<GatewayClipFacet> ClipPlanes = [];

    public List<Vector3[]> PortalPolygons = [];

    public uint? StructureTag { get; internal set; }

    public IReadOnlyList<uint> VisibleCells = System.Array.Empty<uint>();

    public bool SeenOutside;

    public bool IsExteriorJoint;

    public StrollChamber? Stroll { get; internal set; }
}

public readonly record struct ChamberGatewayDetails(
    ushort OtherCellId, ushort PolygonId, ushort Flags, ushort OtherPortalId);

public struct GatewayClipFacet
{
    public Vector3 Normal;

    public float D;

    public int InsideFlank;
}

public enum CameraChamberResolution
{
    None,
    Cache,
    Neighbour,
    BruteForce,
    Grace,
}

public sealed class ChamberVis
{

    private const float PtInChamberEpsilon = 0.01f;

    // State

    private readonly Dictionary<uint, List<FetchedChamber>> _chambersByLb = [];

    // Full-ID lookup used by the production frame walk
    private readonly Dictionary<uint, FetchedChamber> _chamberConsult = [];

    public CameraChamberResolution PreviousCamChamberResolution { get; private set; } = CameraChamberResolution.None;

    public void AttachChamber(FetchedChamber chamber)
    {
        uint lbIdent = chamber.CellId >> 16;

        if (!_chambersByLb.TryGetValue(lbIdent, out var roster))
        {
            roster = [];
            _chambersByLb[lbIdent] = roster;
        }

        roster.Add(chamber);
        _chamberConsult[chamber.CellId] = chamber;
    }

    public void LockLb(uint lbIdent, IReadOnlyList<FetchedChamber> cells)
    {
        uint stem = lbIdent >> 16;
        if (cells.Any(chamber => (chamber.CellId >> 16) != stem))
            throw new ArgumentException(
                "A visibility cell belongs to a different landblock",
                nameof(cells));

        if (_chambersByLb.TryGetValue(stem, out var earlier))
        {
            foreach (var cell in earlier)
                _chamberConsult.Remove(cell.CellId);
        }

        List<FetchedChamber> committed = new List<FetchedChamber>(cells);
        _chambersByLb[stem] = committed;
        foreach (var cell in committed)
            _chamberConsult[cell.CellId] = cell;
    }

    public IReadOnlyList<FetchedChamber> FetchChambersForLb(uint lbIdent)
    {
        return _chambersByLb.TryGetValue(lbIdent, out var roster)
            ? roster
            : System.Array.Empty<FetchedChamber>();
    }

    public bool TryFetchChamber(uint chamberIdent, out FetchedChamber? chamber)
        => _chamberConsult.TryGetValue(chamberIdent, out chamber);

    public void RemoveLandblock(uint lbIdent)
    {
        if (!_chambersByLb.TryGetValue(lbIdent, out var roster))
            return;

        foreach (var chamber in roster)
        {
            _chamberConsult.Remove(chamber.CellId);
        }

        _chambersByLb.Remove(lbIdent);
    }

    public static bool PtInChamber(Vector3 realmPt, FetchedChamber chamber)
    {
        if (chamber.OwnLimitsLower.X >= chamber.OwnLimitsUpper.X)
            return false;

        Vector3 own = Vector3.Transform(realmPt, chamber.InverseWorldTransform);

        return own.X >= chamber.OwnLimitsLower.X - PtInChamberEpsilon &&
               own.X <= chamber.OwnLimitsUpper.X + PtInChamberEpsilon &&
               own.Y >= chamber.OwnLimitsLower.Y - PtInChamberEpsilon &&
               own.Y <= chamber.OwnLimitsUpper.Y + PtInChamberEpsilon &&
               own.Z >= chamber.OwnLimitsLower.Z - PtInChamberEpsilon &&
               own.Z <= chamber.OwnLimitsUpper.Z + PtInChamberEpsilon;
    }

    public bool IsInsideAnyChamber(Vector3 realmPt)
    {
        foreach (var chamber in _chamberConsult.Values)
            if (PtInChamber(realmPt, chamber)) return true;
        return false;
    }

}
