using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Geometry;
using Microsoft.Extensions.Logging;
using BoundingBox =  MacAC.Dat.Bounds3;
using CullMode =  MacAC.Dat.FaceCulling;
using Sphere =  MacAC.Dat.Orb;

namespace MacAC.Assets;

public sealed partial class MeshHarvester
{
    // Vertex dedup key for cell shells: the DAT vertex, its UV (or "none"), and normal direction
    private readonly record struct ShellVertexKey(ushort VertexId, ushort UvKey, bool Inverted);

    // Per-slot state: the Surface record, its stippling mask, and the batch once resolved
    private sealed class ShellSlot(Skin canvas, uint canvasIdent, int bitmask)
    {
        public Skin Surface { get; } = canvas;
        public uint SurfaceId { get; } = canvasIdent;
        public int Bitmask = bitmask;
        public bool Tried;
        public TextureHarvestBatch? Batch;
        public (int Width, int Height, TexelLayout Format) Form;

        public bool Untextured => CanonBareSurfacePolicy.IsUntextured(Surface.Bits);
    }

    public HarvestedMesh? HarvestChamberStruct(ulong ident, ShellCell chamberStruct, IReadOnlyList<ushort> canvasSubstitutions, Matrix4x4 xform, CancellationToken token)
    {
        var verts = new List<VertLocusNormBitmap>();
        var consult = new Dictionary<ShellVertexKey, ushort>();
        var clusters = new Dictionary<(int Width, int Height, TexelLayout Format), List<TextureHarvestBatch>>();
        var sockets = new Dictionary<int, ShellSlot?>();

        Extent reach = new Extent();
        foreach (MeshVertex vert in chamberStruct.Vertices.ByIndex.Values)
            reach.Shield(Vector3.Transform(vert.Position, xform));
        BoundingBox bbox = new BoundingBox(reach.Min, reach.Max);

        int oddFlanks = 0;
        foreach (Facet poly in chamberStruct.Facets.Values)
        {
            token.ThrowIfCancellationRequested();

            if (poly.FrontSurface >= 0 && SocketFor(poly.FrontSurface) is { } front)
                front.Bitmask = CellStructSideOptions.ImposeStipplingBitmaskBit(front.Bitmask, CellStructFacetSide.Positive, poly.Stippling);

            if (poly.VertexIds.Count < 3)
                continue;

            int rawFlanks = (int)poly.Culling;
            if (!CellStructSideOptions.IsCanonDefinedFlanksKind(rawFlanks))
                ++oddFlanks;

            foreach (CellStructSideOption contender in CellStructSideOptions.FetchContenders(rawFlanks))
            {
                short socketOrdinal = contender.SurfaceSlot == CellStructFacetSide.Positive ? poly.FrontSurface : poly.BackSurface;
                if (socketOrdinal < 0)
                    continue;

                ShellSlot? socket = SocketFor(socketOrdinal);
                if (socket is null || socket.Untextured) // lookup failures were logged once when the slot was made
                    continue;
                if (!socket.Tried)
                    Locate(socket);
                if (socket.Batch is null) // a dependency was missing; logged once per slot
                    continue;

                bool wraps = socket.Batch.HasWrappingUVs;
                FanShellPolyg(
                    poly,
                    chamberStruct,
                    consult,
                    verts,
                    socket.Batch.Indices,
                    negativeUv: contender.UvSlot == CellStructFacetSide.Negative,
                    uvAbsent: CellStructSideOptions.IsUvAbsent(contender, poly.Stippling),
                    invert: contender.NormalSign < 0,
                    reverse: contender.ReverseWinding,
                    xform,
                    ref wraps);
                socket.Batch.HasWrappingUVs = wraps;
            }
        }

        int bareSockets = 0;
        foreach (int ordinal in sockets.Keys.Order())
        {
            ShellSlot? socket = sockets[ordinal];
            if (socket is null)
                continue;
            if (socket.Untextured)
            {
                ++bareSockets;
                continue;
            }
            if (socket.Batch is not { } lot)
                continue;

            lot.SourceSurfaceIndex = ordinal;
            lot.RawSurfaceType = (uint)socket.Surface.Bits;
            lot.RetailSurfaceMask = (byte)socket.Bitmask;
            lot.IsCellShell = true;
            lot.CullMode = CullMode.Clockwise;

            if (!clusters.TryGetValue(socket.Form, out List<TextureHarvestBatch>? roster))
                clusters[socket.Form] = roster = [];
            roster.Add(lot);
        }

        if (oddFlanks > 0)
        {
            _logger.LogWarning(
                "CellStruct id=0x{Id:X16}: {Count} polygon(s) had a raw sides_type beyond 0/1/2 (OH2 contract §3.4); they were constructed as retail's default single-side shape",
                ident, oddFlanks);
        }
        if (bareSockets > 0)
        {
            _logger.LogDebug(
                "CellStruct id=0x{Id:X16}: {Count} surface slot(s) constructed but not emitted - untextured under retail's built-EnvCell admission (Surface.Type & 6) == 0 (OH2 contract §4)",
                ident, bareSockets);
        }

        return new HarvestedMesh
        {
            ObjectId = ident,
            IsSetup = false,
            Vertices = [.. verts],
            TextureBatches = clusters,
            BoundingBox = bbox,
            SortCenter = Vector3.Zero,
            SelectionSphere = new Sphere { Center = bbox.Center, Radius = Vector3.Distance(bbox.Max, bbox.Min) / 2.0f },
        };

        // Memoised: a slot that fails to resolve is remembered as null so it logs once.
        ShellSlot? SocketFor(int idx)
        {
            if (sockets.TryGetValue(idx, out ShellSlot? recognized))
                return recognized;

            ShellSlot? made = null;
            if (idx < canvasSubstitutions.Count)
            {
                uint canvasIdent = 0x08000000u | canvasSubstitutions[idx];
                if (_datFiles.Portal.TryGet<Skin>(canvasIdent, out var canvas))
                    made = new ShellSlot(canvas, canvasIdent, CellStructSideOptions.StartingCanvasBitmask(canvas.Bits));
                else
                    Console.WriteLine($"[tex-skip] cellstruct Surface 0x{canvasIdent:X8} miss -> slot {idx} dropped (cellstruct id=0x{ident:X16})");
            }
            else
            {
                _logger.LogWarning($"Could not find surface override for index {idx} in CellStruct id=0x{ident:X16}");
            }
            sockets[idx] = made;
            return made;
        }

        void Locate(ShellSlot slot)
        {
            slot.Tried = true;
            if (LocateCanvas(slot.Surface, slot.SurfaceId, Lane.CellShell) is not { } px)
                return;
            slot.Batch = px.NewLot(slot.Surface, new BitmapTag
            {
                SurfaceId = slot.SurfaceId,
                PaletteId = px.PaletteId,
                Stippling = StippleBits.None,
                IsSolid = px.Solid,
            });
            slot.Form = px.Shape;
        }
    }

    // Fans one cell polygon into a slot's index list
    private static void FanShellPolyg(
        Facet poly,
        ShellCell chamberStruct,
        Dictionary<ShellVertexKey, ushort> consult,
        List<VertLocusNormBitmap> verts,
        List<ushort> ordinals,
        bool negativeUv,
        bool uvAbsent,
        bool invert,
        bool reverse,
        Matrix4x4 xform,
        ref bool wraps)
    {
        List<ushort> loop = new List<ushort>();
        for (int idx = 0; idx < poly.VertexIds.Count; ++idx)
        {
            ushort vertIdent = (ushort)poly.VertexIds[idx];

            int uvOrdinal = 0;
            if (!uvAbsent)
            {
                if (negativeUv && poly.BackUvIndices is not null && idx < poly.BackUvIndices.Count)
                    uvOrdinal = unchecked((sbyte)(byte)poly.BackUvIndices[idx]);
                else if (!negativeUv && poly.FrontUvIndices is not null && idx < poly.FrontUvIndices.Count)
                    uvOrdinal = unchecked((sbyte)(byte)poly.FrontUvIndices[idx]);
            }

            if (!chamberStruct.Vertices.ByIndex.TryGetValue(vertIdent, out var vert))
                continue;

            bool hasUv = uvOrdinal >= 0 && uvOrdinal < vert.TexCoords.Count;
            Vector2 uv = hasUv ? new Vector2(vert.TexCoords[uvOrdinal].U, vert.TexCoords[uvOrdinal].V) : Vector2.Zero;
            if (!wraps && hasUv && (uv.X < 0f || uv.X > 1f || uv.Y < 0f || uv.Y > 1f))
                wraps = true;

            ShellVertexKey tag = new ShellVertexKey(vertIdent, uvOrdinal >= 0 ? (ushort)uvOrdinal : ushort.MaxValue, invert);
            if (!consult.TryGetValue(tag, out ushort ordinal))
            {
                Vector3 norm = Vector3.Normalize(Vector3.TransformNormal(vert.Normal, xform));
                if (invert)
                    norm = -norm;
                ordinal = (ushort)verts.Count;
                verts.Add(new VertLocusNormBitmap(Vector3.Transform(vert.Position, xform), norm, uv));
                consult[tag] = ordinal;
            }
            loop.Add(ordinal);
        }

        for (int t = 0; t < loop.Count - 2; ++t)
        {
            (int a, int b, int c) = CellStructSideOptions.TriangleFanOrdinals(t, reverse);
            ordinals.Add(loop[a]);
            ordinals.Add(loop[b]);
            ordinals.Add(loop[c]);
        }
    }
}
