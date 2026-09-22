using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Landscape;

public sealed record LandblockTessellationData(TerrainVert[] Vertices, uint[] Indices);

public static class LandblockTessellation
{
    public const int HeightmapFlank = 9;
    public const int ChambersPerSide = 8;
    public const int VertsPerChamber = 6;
    public const int VertsPerLb = ChambersPerSide * ChambersPerSide * VertsPerChamber;
    public const float ChamberSize = 24.0f;
    public const float LbDims = ChambersPerSide * ChamberSize;

    private const int HeightChartListings = 256;
    private const int RoadBitmask = 0x3;
    private const int KindBitmask = 0x1F;

    // The four corners of one cell, bottom-left first, counter-clockwise
    private readonly ref struct Quad
    {
        public readonly Vector3 BL, BR, TR, TL;
        public readonly Vector3 NBL, NBR, NTR, NTL;

        public Quad(float[,] heights, Vector3[,] norms, int cx, int cy)
        {
            BL = At(heights, cx, cy);
            BR = At(heights, cx + 1, cy);
            TR = At(heights, cx + 1, cy + 1);
            TL = At(heights, cx, cy + 1);
            NBL = norms[cx, cy];
            NBR = norms[cx + 1, cy];
            NTR = norms[cx + 1, cy + 1];
            NTL = norms[cx, cy + 1];
        }

        private static Vector3 At(float[,] heights, int x, int y) =>
            new(x * ChamberSize, y * ChamberSize, heights[x, y]);
    }

    public static LandblockTessellationData Build(
        TerrainTile chunk,
        uint lbX,
        uint lbY,
        float[] heightTable,
        TerrainBlendContext ctx,
        IDictionary<uint, SurfaceFacts> canvasStash)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(heightTable);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(canvasStash);
        if (heightTable.Length < HeightChartListings)
            throw new ArgumentException("heightTable must have 256 entries", nameof(heightTable));

        float[,] heights = ProbeHeights(chunk, heightTable);
        Vector3[,] norms = SmoothNorms(heights, lbX, lbY);

        TerrainVert[] verts = new TerrainVert[VertsPerLb];
        int cur = 0;
        for (int cy = 0; cy < ChambersPerSide; ++cy)
        {
            for (int cx = 0; cx < ChambersPerSide; ++cx)
            {
                uint palCode = PalCodeFor(chunk, cx, cy);
                if (!canvasStash.TryGetValue(palCode, out SurfaceFacts recipe))
                {
                    recipe = TerrainBlend.AssembleCanvas(palCode, ctx);
                    canvasStash[palCode] = recipe;
                }

                var divide = TerrainBlend.WorkDivideDir(lbX, (uint)cx, lbY, (uint)cy);
                (uint d0, uint d1, uint d2, uint d3) = TerrainBlend.PopulateChamberBlob(recipe, divide);
                Quad quad = new Quad(heights, norms, cx, cy);

                void Write(Vector3 p, Vector3 num) => verts[cur++] = new TerrainVert(p, num, d0, d1, d2, d3);

                if (divide == CellSplitAxis.SWtoNE)
                {
                    Write(quad.BL, quad.NBL); Write(quad.BR, quad.NBR); Write(quad.TR, quad.NTR);
                    Write(quad.BL, quad.NBL); Write(quad.TR, quad.NTR); Write(quad.TL, quad.NTL);
                }
                else
                {
                    Write(quad.BL, quad.NBL); Write(quad.BR, quad.NBR); Write(quad.TL, quad.NTL);
                    Write(quad.BR, quad.NBR); Write(quad.TR, quad.NTR); Write(quad.TL, quad.NTL);
                }
            }
        }

        // No vertex sharing, so the index buffer is the identity
        uint[] ordinals = new uint[VertsPerLb];
        for (uint idx = 0; idx < ordinals.Length; ++idx)
            ordinals[idx] = idx;

        return new LandblockTessellationData(verts, ordinals);
    }

    private static float[,] ProbeHeights(TerrainTile chunk, float[] heightChart)
    {
        float[,] heights = new float[HeightmapFlank, HeightmapFlank];
        for (int x = 0; x < HeightmapFlank; ++x)
        {
            for (int y = 0; y < HeightmapFlank; ++y)
                heights[x, y] = heightChart[chunk.Heights[x * HeightmapFlank + y]];
        }
        return heights;
    }

    private static uint PalCodeFor(TerrainTile chunk, int cx, int cy)
    {
        TerrainSample info = chunk.Samples[cx * HeightmapFlank + cy];
        TerrainSample br = chunk.Samples[(cx + 1) * HeightmapFlank + cy];
        TerrainSample tr = chunk.Samples[(cx + 1) * HeightmapFlank + cy + 1];
        TerrainSample tl = chunk.Samples[cx * HeightmapFlank + cy + 1];
        return TerrainBlend.FetchPalCode(
            info.Road & RoadBitmask, br.Road & RoadBitmask, tr.Road & RoadBitmask, tl.Road & RoadBitmask,
            (int)info.Kind & KindBitmask, (int)br.Kind & KindBitmask, (int)tr.Kind & KindBitmask, (int)tl.Kind & KindBitmask);
    }

    // Area-weighted face normals summed per heightmap sample, then normalised
    private static Vector3[,] SmoothNorms(float[,] heights, uint lbX, uint lbY)
    {
        Vector3[,] sums = new Vector3[HeightmapFlank, HeightmapFlank];
        Vector3[,] none = new Vector3[HeightmapFlank, HeightmapFlank];

        for (int cy = 0; cy < ChambersPerSide; ++cy)
        {
            for (int cx = 0; cx < ChambersPerSide; ++cx)
            {
                Quad quad = new Quad(heights, none, cx, cy);
                var divide = TerrainBlend.WorkDivideDir(lbX, (uint)cx, lbY, (uint)cy);
                if (divide == CellSplitAxis.SWtoNE)
                {
                    AppendFace(sums, quad.BL, cx, cy, quad.BR, cx + 1, cy, quad.TR, cx + 1, cy + 1);
                    AppendFace(sums, quad.BL, cx, cy, quad.TR, cx + 1, cy + 1, quad.TL, cx, cy + 1);
                }
                else
                {
                    AppendFace(sums, quad.BL, cx, cy, quad.BR, cx + 1, cy, quad.TL, cx, cy + 1);
                    AppendFace(sums, quad.BR, cx + 1, cy, quad.TR, cx + 1, cy + 1, quad.TL, cx, cy + 1);
                }
            }
        }

        Vector3[,] norms = new Vector3[HeightmapFlank, HeightmapFlank];
        for (int x = 0; x < HeightmapFlank; ++x)
        {
            for (int y = 0; y < HeightmapFlank; ++y)
            {
                Vector3 total = sums[x, y];
                norms[x, y] = total.LengthSquared() > 0f ? Vector3.Normalize(total) : Vector3.UnitZ;
            }
        }
        return norms;
    }

    private static void AppendFace(
        Vector3[,] sums,
        Vector3 a, int ax, int ay,
        Vector3 b, int bx, int by,
        Vector3 c, int cx, int cy)
    {
        Vector3 cross = Vector3.Cross(b - a, c - a);
        if (cross.LengthSquared() <= 0f)
            return;
        Vector3 num = Vector3.Normalize(cross);
        sums[ax, ay] += num;
        sums[bx, by] += num;
        sums[cx, cy] += num;
    }
}
