using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>A GfxObj's resolved collision polygons, serialisable for fixtures and diagnostics.</summary>
public sealed record GfxObjSnapshot(
    uint GfxObjId,
    Vector3Wire BoundingSphereOrigin,
    float BoundingSphereRadius,
    IReadOnlyList<PolygonSnapshot> ResolvedPolygons);

public static class GfxObjSnapshotWriter
{
    private static readonly JsonSerializerOptions Writing = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static readonly JsonSerializerOptions Reading = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static GfxObjSnapshot Capture(uint gfxObjRefIdent, GfxObjKinetics gfx)
    {
        var polygs = new List<PolygonSnapshot>(gfx.Settled.Count);
        foreach ((ushort ident, SettledPolygon polyg) in gfx.Settled)
            polygs.Add(CellSnapshotWriter.Flatten(ident, polyg));

        return new GfxObjSnapshot(
            GfxObjId: gfxObjRefIdent,
            BoundingSphereOrigin: Vector3Wire.From(gfx.BoundingSphere?.Center ?? Vector3.Zero),
            BoundingSphereRadius: gfx.BoundingSphere?.Radius ?? 0f,
            ResolvedPolygons: polygs);
    }

    public static void Write(GfxObjSnapshot print, string fileTrail)
    {
        string? direction = Path.GetDirectoryName(fileTrail);
        if (!string.IsNullOrEmpty(direction) && !Directory.Exists(direction))
            Directory.CreateDirectory(direction);

        using FileStream flow = File.Create(fileTrail);
        JsonSerializer.Serialize(flow, print, Writing);
    }

    public static GfxObjSnapshot Read(string fileTrail)
    {
        using FileStream flow = File.OpenRead(fileTrail);
        return JsonSerializer.Deserialize<GfxObjSnapshot>(flow, Reading)
            ?? throw new InvalidDataException($"GfxObj dump deserialized to null: {fileTrail}");
    }

    public static GfxObjKinetics Hydrate(GfxObjSnapshot print)
    {
        var settled = new Dictionary<ushort, SettledPolygon>(print.ResolvedPolygons.Count);
        foreach (PolygonSnapshot snapshot in print.ResolvedPolygons)
            settled[snapshot.Id] = CellSnapshotWriter.Thaw(snapshot);

        Vector3 origin = print.BoundingSphereOrigin.ToVector3();
        float radius = print.BoundingSphereRadius;
        if (radius <= 0f && settled.Count > 0)
            (origin, radius) = CoveringOrb(settled);

        PhysicsBspNode leaf = new PhysicsBspNode
        {
            Tag = BspTag.Leaf,
            Bounds = new Orb { Center = origin, Radius = radius },
        };
        foreach (ushort ident in settled.Keys)
            leaf.Polygons.Add(ident);

        return new GfxObjKinetics
        {
            BSP = new PhysicsBspTree { Root = leaf },
            PhysicsPolygons = new Dictionary<ushort, Facet>(),
            Vertices = new MeshVertices(),
            Settled = settled,
            BoundingSphere = leaf.Bounds,
        };
    }

    // Centroid of every vertex, with a radius reaching the farthest one
    private static (Vector3 Origin, float Radius) CoveringOrb(Dictionary<ushort, SettledPolygon> settled)
    {
        Vector3 total = Vector3.Zero;
        int tally = 0;
        foreach (SettledPolygon polyg in settled.Values)
        {
            foreach (Vector3 v in polyg.Vertices)
            {
                total += v;
                ++tally;
            }
        }
        if (tally is 0)
            return (Vector3.Zero, 0f);

        Vector3 centroid = total / tally;
        float farthestSq = 0f;
        foreach (SettledPolygon polyg in settled.Values)
        {
            foreach (Vector3 v in polyg.Vertices)
            {
                float distanceSq = Vector3.DistanceSquared(centroid, v);
                if (distanceSq > farthestSq)
                    farthestSq = distanceSq;
            }
        }
        return (centroid, MathF.Sqrt(farthestSq));
    }
}
