using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Realm.Cells;

/// <summary>One of the 64 outdoor cells of a landblock, synthesized from the heightmap.</summary>
public sealed class GroundCell : ObjRefChamber
{
    public const float CellDims = 24f;

    private readonly Vector3 _chunkOrigin;

    private GroundCell(
        uint ident,
        LandCanvas land,
        Vector3 realmOrigin,
        int cx,
        int cy,
        Matrix4x4 realmXform,
        Matrix4x4 invRealmXform,
        Vector3 ownLimitsLower,
        Vector3 ownLimitsUpper)
        : base(ident, realmXform, invRealmXform, ownLimitsLower, ownLimitsUpper, [], [], observedBeyond: false)
    {
        Terrain = land;
        Cx = cx;
        Cy = cy;
        _chunkOrigin = realmOrigin;
        StructureChamberIdent = null;
    }

    public LandCanvas Terrain { get; }

    public int Cx { get; }

    public int Cy { get; }

    public uint? StructureChamberIdent { get; }

    public static GroundCell Synthesize(uint ident, LandCanvas land, Vector3 realmOrigin, int cx, int cy)
    {
        float ox = cx * CellDims;
        float oy = cy * CellDims;
        float z0 = land.ProbeZ(ox, oy);
        float z1 = land.ProbeZ(ox + CellDims, oy);
        float z2 = land.ProbeZ(ox, oy + CellDims);
        float z3 = land.ProbeZ(ox + CellDims, oy + CellDims);
        Vector3 lower = new Vector3(ox, oy, MathF.Min(MathF.Min(z0, z1), MathF.Min(z2, z3)));
        Vector3 upper = new Vector3(ox + CellDims, oy + CellDims, MathF.Max(MathF.Max(z0, z1), MathF.Max(z2, z3)));

        Matrix4x4 xform = Matrix4x4.CreateTranslation(realmOrigin);
        Matrix4x4.Invert(xform, out Matrix4x4 inv);
        return new GroundCell(ident, land, realmOrigin, cx, cy, xform, inv, lower, upper);
    }

    public override bool PtInCell(Vector3 realmPt)
    {
        float lx = realmPt.X - _chunkOrigin.X;
        float ly = realmPt.Y - _chunkOrigin.Y;
        return lx >= Cx * CellDims && lx < (Cx + 1) * CellDims && ly >= Cy * CellDims && ly < (Cy + 1) * CellDims;
    }
}
