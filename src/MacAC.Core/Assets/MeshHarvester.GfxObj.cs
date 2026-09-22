using System.Numerics;
using MacAC.Dat;
using BoundingBox =  MacAC.Dat.Bounds3;
using CullMode =  MacAC.Dat.FaceCulling;
using Sphere =  MacAC.Dat.Orb;

namespace MacAC.Assets;

/// <summary>GfxObj harvesting: one texture batch per (surface, palette, stippling, cull) combination.</summary>
public sealed partial class MeshHarvester
{
    // Vertex dedup key for GfxObj lanes: the DAT vertex, its UV set, and which face it is on
    private readonly record struct GfxVertexKey(ushort VertexId, ushort UvIndex, bool Negative);

    private HarvestedMesh? HarvestGfxObjRef(ulong ident, PartMesh gfxObjRef, Vector3 scaling, CancellationToken token)
    {
        var verts = new List<VertLocusNormBitmap>();
        var consult = new Dictionary<GfxVertexKey, ushort>();
        var clusters = new Dictionary<(int Width, int Height, TexelLayout Format), List<TextureHarvestBatch>>();

        (Vector3 lower, Vector3 upper) = ReachOf(gfxObjRef, scaling);
        BoundingBox bbox = new BoundingBox(lower, upper);

        foreach (Facet poly in gfxObjRef.Facets.Values)
        {
            token.ThrowIfCancellationRequested();
            if (poly.VertexIds.Count < 3)
                continue;

            FileFace(poly, poly.FrontSurface, negative: false);
            if (ShowsBackFace(poly))
                FileFace(poly, poly.BackSurface, negative: true);
        }

        return new HarvestedMesh
        {
            ObjectId = ident,
            IsSetup = false,
            Vertices = [.. verts],
            TextureBatches = clusters,
            BoundingBox = bbox,
            SortCenter = gfxObjRef.SortCenter,
            DIDDegrade = (gfxObjRef.Bits & PartMeshBits.HasDIDDegrade) == PartMeshBits.HasDIDDegrade ? gfxObjRef.LodTableId : 0,
            SelectionSphere = gfxObjRef.DrawTree?.Root?.Bounds ?? new Sphere
            {
                Center = bbox.Center,
                Radius = Vector3.Distance(bbox.Max, bbox.Min) / 2.0f,
            },
        };

        // Appends one face of `poly` to the batch its surface resolves to.
        void FileFace(Facet poly, short canvasOrdinal, bool negative)
        {
            if (canvasOrdinal < 0 || canvasOrdinal >= gfxObjRef.SkinIds.Count)
                return;

            uint canvasIdent = gfxObjRef.SkinIds[canvasOrdinal];
            if (!_datFiles.Portal.TryGet<Skin>(canvasIdent, out var canvas))
            {
                Console.WriteLine($"[tex-skip] gfxobj Surface 0x{canvasIdent:X8} miss -> poly batch dropped (obj 0x{gfxObjRef.Id:X8})");
                return;
            }
            if (LocateCanvas(canvas, canvasIdent, Lane.GfxObj) is not { } px)
                return;

            BitmapTag tag = new BitmapTag
            {
                SurfaceId = canvasIdent,
                PaletteId = px.PaletteId,
                Stippling = poly.Stippling,
                IsSolid = px.Solid,
            };

            if (!clusters.TryGetValue(px.Shape, out List<TextureHarvestBatch>? lots))
                clusters[px.Shape] = lots = [];

            TextureHarvestBatch? lot = null;
            foreach (TextureHarvestBatch contender in lots)
            {
                if (contender.Key.Equals(tag) && contender.CullMode == poly.Culling)
                {
                    lot = contender;
                    break;
                }
            }
            if (lot is null)
            {
                lot = px.NewLot(canvas, tag);
                lot.CullMode = poly.Culling;
                lots.Add(lot);
            }

            bool wraps = lot.HasWrappingUVs;
            FanGfxPolyg(poly, gfxObjRef, scaling, consult, verts, lot.Indices, negative, ref wraps);
            lot.HasWrappingUVs = wraps;
        }
    }

    // Retail draws the negative face when stippling says so, or for clockwise polys not marked NoNeg
    private static bool ShowsBackFace(Facet poly)
    {
        StippleBits s = poly.Stippling;
        return (s & StippleBits.Negative) == StippleBits.Negative
            || (s & StippleBits.Both) == StippleBits.Both
            || ((s & StippleBits.NoNeg) != StippleBits.NoNeg && poly.Culling == CullMode.Clockwise);
    }

    private static void FanGfxPolyg(
        Facet poly,
        PartMesh gfxObjRef,
        Vector3 scaling,
        Dictionary<GfxVertexKey, ushort> consult,
        List<VertLocusNormBitmap> verts,
        List<ushort> ordinals,
        bool negative,
        ref bool wraps)
    {
        List<ushort> loop = new List<ushort>();
        for (int idx = 0; idx < poly.VertexIds.Count; ++idx)
        {
            ushort vertIdent = (ushort)poly.VertexIds[idx];
            ushort uvOrdinal = 0;
            if (negative && poly.BackUvIndices is not null && idx < poly.BackUvIndices.Count)
                uvOrdinal = poly.BackUvIndices[idx];
            else if (!negative && poly.FrontUvIndices is not null && idx < poly.FrontUvIndices.Count)
                uvOrdinal = poly.FrontUvIndices[idx];

            if (!gfxObjRef.Vertices.ByIndex.TryGetValue(vertIdent, out var vert))
                continue;
            if (uvOrdinal >= vert.TexCoords.Count)
                uvOrdinal = 0;

            Vector2 uv = vert.TexCoords.Count > 0 ? new Vector2(vert.TexCoords[uvOrdinal].U, vert.TexCoords[uvOrdinal].V) : Vector2.Zero;
            if (!wraps && (uv.X < 0f || uv.X > 1f || uv.Y < 0f || uv.Y > 1f))
                wraps = true;

            GfxVertexKey tag = new GfxVertexKey(vertIdent, uvOrdinal, negative);
            if (!consult.TryGetValue(tag, out ushort ordinal))
            {
                Vector3 norm = Vector3.Normalize(vert.Normal);
                if (negative)
                    norm = -norm;
                ordinal = (ushort)verts.Count;
                verts.Add(new VertLocusNormBitmap(vert.Position * scaling, norm, uv));
                consult[tag] = ordinal;
            }
            loop.Add(ordinal);
        }

        // Back faces fan the other way round so they read as front-facing from behind.
        for (int idx = 2; idx < loop.Count; ++idx)
        {
            if (negative)
            {
                ordinals.Add(loop[0]);
                ordinals.Add(loop[idx - 1]);
                ordinals.Add(loop[idx]);
            }
            else
            {
                ordinals.Add(loop[idx]);
                ordinals.Add(loop[idx - 1]);
                ordinals.Add(loop[0]);
            }
        }
    }
}
