using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Data;
using MacAC.Mechanics.Landscape;

namespace MacAC.Mechanics.Geometry;

public static class GfxObjRefTriMesh
{
    private sealed class Patch
    {
        public readonly List<MechVertex> Vertices = [];
        public readonly List<uint> Indices = [];
        public readonly Dictionary<(int Pos, int Uv, bool Back), uint> Shared = [];

        public bool AnyUvBeyondUnitSquare()
        {
            foreach (MechVertex vertex in Vertices)
            {
                if (vertex.TexCoord.X is < 0f or > 1f || vertex.TexCoord.Y is < 0f or > 1f)
                    return true;
            }
            return false;
        }
    }

    public static IReadOnlyList<GfxObjPatch> Build(PartMesh gfxObjRef, IDatRecordSource? datFiles = null)
    {
        var patches = new Dictionary<(int Surface, bool Back), Patch>();

        foreach (Facet poly in gfxObjRef.Facets.Values)
        {
            if (poly.VertexIds.Count < 3)
                continue;

            Fan(gfxObjRef, patches, poly, poly.FrontSurface, back: false);
            if (HasBackFace(poly))
                Fan(gfxObjRef, patches, poly, poly.BackSurface, back: true);
        }

        List<GfxObjPatch> outcome = new List<GfxObjPatch>(patches.Count);
        foreach (((int canvasOrdinal, _), Patch patch) in patches)
        {
            uint canvasIdent = (uint)gfxObjRef.SkinIds[canvasOrdinal];
            Skin? canvas = datFiles?.Get<Skin>(canvasIdent);
            outcome.Add(new GfxObjPatch(canvasIdent, [.. patch.Vertices], [.. patch.Indices])
            {
                Translucency = canvas is null ? SeeThroughKind.Opaque : SeeThroughKindExtensions.FromCanvasKind(canvas.Bits),
                Luminosity = canvas?.Luminosity ?? 0f,
                Diffuse = canvas?.Diffuse ?? 1f,
                NeedsUvRepeat = patch.AnyUvBeyondUnitSquare(),
                SurfOpacity = canvas is null
                    ? 1f
                    : SeeThroughKindExtensions.DensityFromCanvasSeeThrough(canvas.Bits, canvas.Translucency),
                DisableFog = canvas is not null && SeeThroughKindExtensions.DisablesFixedFunctionFog(canvas.Bits),
            });
        }
        return outcome;
    }

    private static bool HasBackFace(Facet poly)
    {
        return poly.Stippling.HasFlag(StippleBits.Negative)
        || poly.Stippling.HasFlag(StippleBits.Both)
        || (!poly.Stippling.HasFlag(StippleBits.NoNeg) && poly.Culling == FaceCulling.Clockwise);
    }

    private static void Fan(
        PartMesh gfxObjRef,
        Dictionary<(int Surface, bool Back), Patch> patches,
        Facet poly,
        short canvasOrdinal,
        bool back)
    {
        if (canvasOrdinal < 0 || canvasOrdinal >= gfxObjRef.SkinIds.Count)
            return;

        var tag = ((int)canvasOrdinal, back);
        if (!patches.TryGetValue(tag, out Patch? patch))
            patches[tag] = patch = new Patch();

        Span<uint> loop = poly.VertexIds.Count <= 32 ? stackalloc uint[poly.VertexIds.Count] : new uint[poly.VertexIds.Count];
        for (int idx = 0; idx < poly.VertexIds.Count; ++idx)
        {
            int spot = poly.VertexIds[idx];
            int uv = UvOrdinal(poly, idx, back);
            if (!gfxObjRef.Vertices.ByIndex.TryGetValue((ushort)spot, out MeshVertex? vertex))
                return; // a polygon referencing a missing vertex is dropped whole

            var portion = (pos: spot, uv, back);
            if (!patch.Shared.TryGetValue(portion, out uint ordinal))
            {
                Vector2 texcoord = uv >= 0 && uv < vertex.TexCoords.Count ? new Vector2(vertex.TexCoords[uv].U, vertex.TexCoords[uv].V) : Vector2.Zero;
                Vector3 norm = Vector3.Normalize(back ? -vertex.Normal : vertex.Normal);
                ordinal = (uint)patch.Vertices.Count;
                patch.Vertices.Add(new MechVertex(vertex.Position, norm, texcoord, TerrainLayer: 0));
                patch.Shared[portion] = ordinal;
            }
            loop[idx] = ordinal;
        }

        // The back face winds the other way so the two faces look outward.
        for (int idx = 1; idx < loop.Length - 1; ++idx)
        {
            if (back)
            {
                patch.Indices.Add(loop[idx + 1]);
                patch.Indices.Add(loop[idx]);
                patch.Indices.Add(loop[0]);
            }
            else
            {
                patch.Indices.Add(loop[0]);
                patch.Indices.Add(loop[idx]);
                patch.Indices.Add(loop[idx + 1]);
            }
        }
    }

    // Back faces use NegUVIndices when present and otherwise borrow the front's
    private static int UvOrdinal(Facet poly, int idx, bool back)
    {
        if (back && poly.BackUvIndices.Count > 0 && idx < poly.BackUvIndices.Count)
            return poly.BackUvIndices[idx];
        return idx < poly.FrontUvIndices.Count ? poly.FrontUvIndices[idx] : 0;
    }
}
