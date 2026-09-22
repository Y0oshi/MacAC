using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Data;
using MacAC.Mechanics.Drawing.Batches;

namespace MacAC.Mechanics.Realm;

public static class SceneryGrower
{
    private const int VertsPerFlank = 9;
    private const float ChamberDims = 24.0f;
    private const float LbDims = 192.0f;
    private const int ChambersPerFlank = 8;
    private const double UnitScaling = 2.3283064e-10;

    public readonly record struct SceneryPlacement(
        uint ObjectId,
        Vector3 LocalPosition,
        Quaternion Rotation,
        float Scale);

    public static IReadOnlyList<SceneryPlacement> Produce(
        IDatRecordSource datFiles,
        WorldRegion zone,
        TerrainTile chunk,
        uint lbIdent,
        HashSet<int>? structureChambers = null,
        float[]? heightChart = null)
    {
        _ = heightChart;

        var stances = new List<SceneryPlacement>();
        if (zone.Terrain?.Kinds is not { } landKinds || zone.Scenes?.Choices is not { } tableauKinds)
            return stances;

        var land = BatchSceneryAdapter.AssembleLandListings(chunk);
        uint lbX = lbIdent >> 24;
        uint lbY = (lbIdent >> 16) & 0xFFu;

        for (int x = 0; x < VertsPerFlank; ++x)
        {
            for (int y = 0; y < VertsPerFlank; ++y)
            {
                ushort word = chunk.Samples[x * VertsPerFlank + y];
                if (TableauFor(datFiles, landKinds, tableauKinds, word, lbX * 8 + (uint)x, lbY * 8 + (uint)y) is not { } tableau)
                    continue;
                ExpandVert(zone, land, lbX, lbY, (uint)x, (uint)y, tableau, structureChambers, stances);
            }
        }
        return stances;
    }

    public static bool IsRoadVertex(ushort raw) => (raw & 0x3u) is not 0;

    // terrain type → scene type → scene list → one scene by the vertex hash
    private static SceneryList? TableauFor(
        IDatRecordSource datFiles,
        IReadOnlyList<TerrainKindDesc> landKinds,
        IReadOnlyList<SceneChoice> tableauKinds,
        ushort word,
        uint gx,
        uint gy)
    {
        uint landKind = (uint)((word >> 2) & 0x1F);
        uint tableauSocket = (uint)((word >> 11) & 0x1F);
        if (landKind >= landKinds.Count)
            return null;

        List<uint> sockets = landKinds[(int)landKind].SceneChoices;
        if (tableauSocket >= sockets.Count)
            return null;

        uint tableauKind = sockets[(int)tableauSocket];
        if (tableauKind >= tableauKinds.Count)
            return null;

        var scenes = tableauKinds[(int)tableauKind].SceneryListIds;
        if (scenes.Count is 0)
            return null;

        uint digest = gy * (712977289u * gx + 1813693831u) - 1109124029u * gx + 2139937281u;
        int choose = (int)(scenes.Count * (digest * UnitScaling));
        if (choose >= scenes.Count || choose < 0)
            choose = 0;
        return datFiles.Get<SceneryList>((uint)scenes[choose]);
    }

    private static void ExpandVert(
        WorldRegion zone,
        TerrainCell[] land,
        uint lbX,
        uint lbY,
        uint chamberX,
        uint chamberY,
        SceneryList tableau,
        HashSet<int>? structureChambers,
        List<SceneryPlacement> into)
    {
        uint gx = chamberX + lbX * 8;
        uint gy = chamberY + lbY * 8;

        // The per-object frequency roll, seeded by the global cell coordinates
        uint seedX = unchecked(0u - 1109124029u * gx);
        uint seedY = 1813693831u * gy;
        uint seedXY = 1360117743u * gx * gy + 1888038839u;

        for (uint jdx = 0; jdx < tableau.Items.Count; ++jdx)
        {
            SceneryItem objRef = tableau.Items[(int)jdx];
            if (objRef.WeenieObj is not 0)
                continue;
            double roll = unchecked((uint)(seedX + seedY - seedXY * (23399u + jdx))) * UnitScaling;
            if (roll >= objRef.Frequency)
                continue;

            Vector3 jitter = SceneryTools.Displace(objRef, gx, gy, jdx);
            float lx = chamberX * ChamberDims + jitter.X;
            float ly = chamberY * ChamberDims + jitter.Y;
            if (lx < 0 || ly < 0 || lx >= LbDims || ly >= LbDims)
                continue;
            if (TerrainTools.OnRoad(new Vector3(lx, ly, 0), land))
                continue;

            if (structureChambers is not null)
            {
                int cx = Math.Clamp((int)(lx / ChamberDims), 0, ChambersPerFlank - 1);
                int cy = Math.Clamp((int)(ly / ChamberDims), 0, ChambersPerFlank - 1);
                if (structureChambers.Contains(cx * VertsPerFlank + cy))
                    continue;
            }

            Vector3 norm = TerrainTools.FetchNorm(zone, land, lbX, lbY, new Vector3(lx, ly, 0));
            if (!SceneryTools.VerifySlope(objRef, norm.Z))
                continue;

            float lz = objRef.BaseLoc.Origin.Z;
            Quaternion spin = objRef.Align is not 0
                ? SceneryTools.ObjRefAlign(objRef, norm, lz, jitter)
                : SceneryTools.SpinObjRef(objRef, gx, gy, jdx, jitter);
            float scaling = SceneryTools.ResizeObjRef(objRef, gx, gy, jdx);
            if (scaling <= 0)
                scaling = 1f;

            into.Add(new SceneryPlacement(objRef.ObjectId, new Vector3(lx, ly, lz), spin, scaling));
        }
    }
}
