using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using Environment =  MacAC.Dat.InteriorShell;

namespace MacAC.Client.Graphics.Stride;

public static class StrideCellMint
{
    public static StrollChamber? AssembleChamber(IDatAccess datFiles, uint chamberIdent, Vector3 chunkShift)
    {
        if (datFiles.Get<RoomCell>(chamberIdent) is not RoomCell environChamber)
            return null;
        if (datFiles.Get<Environment>(0x0D000000u | environChamber.ShellId) is not Environment surroundings
            || !surroundings.Cells.TryGetValue(environChamber.ShellCellIndex, out ShellCell? chamberStruct)
            || chamberStruct is null)

            return null;

        Matrix4x4 realmXform =
            Matrix4x4.CreateFromQuaternion(environChamber.Position.Orientation)
            * Matrix4x4.CreateTranslation(
                environChamber.Position.Origin.X + chunkShift.X,
                environChamber.Position.Origin.Y + chunkShift.Y,
                environChamber.Position.Origin.Z + chunkShift.Z);
        Matrix4x4.Invert(realmXform, out Matrix4x4 inv);
        return FromDecoded(chamberIdent, environChamber, chamberStruct, realmXform, inv);
    }

    public static StrollChamber FromDecoded(
        uint chamberIdent, RoomCell environChamber, ShellCell chamberStruct,
        Matrix4x4 realmXform, Matrix4x4 invRealmXform)
    {
        uint lbBitmask = chamberIdent & 0xFFFF0000u;
        int gatewayTally = environChamber.Doorways.Count;
        StrideCellPortal[] gateways = new StrideCellPortal[gatewayTally];
        StridePolygon[] polygs = new StridePolygon[gatewayTally];
        for (int idx = 0; idx < gatewayTally; ++idx)
        {
            CellDoorway gateway = environChamber.Doorways[idx];
            gateways[idx] = new StrideCellPortal
            {
                OtherCellId = gateway.OtherCellId == 0xFFFF
                    ? 0xFFFFFFFFu
                    : lbBitmask | gateway.OtherCellId,
                PolygIdx = idx,
                PortalSide = ((ushort)gateway.Bits & 0x2) is not 0 ? 0 : 1,
                PreciseMatch = ((ushort)gateway.Bits & 0x1) != 0,
                AnotherGatewayTag = unchecked((short)gateway.OtherPortalId),
            };
            polygs[idx] = AssemblePolyg(chamberStruct, gateway.PolygonId) ?? new StridePolygon();
        }

        return new StrollChamber
        {
            CellId = chamberIdent,
            Portals = gateways,
            PortalPolygons = polygs,
            StabList = [.. environChamber.VisibleCells.Select(v => lbBitmask | v)],
            WorldTransform = realmXform,
            InverseWorldTransform = invRealmXform,
        };
    }

    public static Dictionary<uint, StrollChamber> AssembleInteriorChambers(
        IDatAccess datFiles, uint lbIdent, Vector3 chunkShift,
        Dictionary<uint, StrollChamber>? into = null)
    {
        uint lbBitmask = lbIdent & 0xFFFF0000u;
        Dictionary<uint, StrollChamber> chambers = into ?? [];
        if (datFiles.Get<TerrainTileExtras>(lbBitmask | 0xFFFEu) is not TerrainTileExtras details)
            return chambers;

        uint leadChamberIdent = lbBitmask | 0x0100u;
        for (uint shift = 0; shift < details.CellCount; ++shift)
        {
            StrollChamber? chamber = AssembleChamber(datFiles, leadChamberIdent + shift, chunkShift);
            if (chamber is not null)
                chambers[chamber.CellId] = chamber;
        }
        return chambers;
    }

    private static StridePolygon? AssemblePolyg(ShellCell chamberStruct, ushort polygIdent)
    {
        return !chamberStruct.Facets.TryGetValue(polygIdent, out Facet? poly)
            || poly is null || poly.VertexIds.Count < 3
            ? null
            : AssemblePolygFromVerts(
            poly.VertexIds,
            ident => chamberStruct.Vertices.ByIndex.TryGetValue((ushort)ident, out MeshVertex? vertex)
                ? new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z)
                : null);
    }

    private static StridePolygon? AssemblePolygFromVerts(
        IReadOnlyList<short> vertIdents, Func<short, Vector3?> locate)
    {
        Vector3[] verts = new Vector3[vertIdents.Count];
        for (int idx = 0; idx < vertIdents.Count; ++idx)
        {
            Vector3? v = locate(vertIdents[idx]);
            if (v is null) return null;
            verts[idx] = v.Value;
        }
        Vector3 norm = Vector3.Normalize(
            Vector3.Cross(verts[1] - verts[0], verts[2] - verts[0]));
        return new StridePolygon
        {
            Vertices = verts,
            Plane = new StridePlane(norm, -Vector3.Dot(norm, verts[0])),
        };
    }
}
