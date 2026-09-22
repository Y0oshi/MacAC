namespace MacAC.Client.Graphics.Stride;

public sealed class StrideLandBlock
{
    public uint LbTag;

    public int FlankChamberTally = 8;
    public float UpperZ;
    public float LowerZ;

    public int Ring;

    public StrollStructure?[] ChamberStructures = [];
    public StrollStructure[][] CoarseChamberStructures = [];

    public StrideBoundingType InLens;
    public StrideBoundingType[] ChamberInLens = [];

    public int[] PaintArr = [];
    public int ClosestX = -1;
    public int ClosestY = -1;

    public void SecureChamberArrs()
    {
        int num = FlankChamberTally * FlankChamberTally;
        if (ChamberInLens.Length < num) ChamberInLens = new StrideBoundingType[num];
        if (PaintArr.Length < num) PaintArr = new int[num];
        if (ChamberStructures.Length < num) ChamberStructures = new StrollStructure?[num];
    }
}

public sealed class StrideLandscape
{
    public const float ChunkLen = 192f;
    public const float ChamberLen = 24f;

    public int MidWidth = 11;
    public StrideLandBlock?[] Chunks = [];      // [x * MidWidth + y]
    public int BeholderChunkX;
    public int BeholderChunkY;
    public int BeholderChamberX;
    public int BeholderChamberY;

    public float BeholderRealmOriginX;
    public float BeholderRealmOriginY;

    public int[] ChunkPaintRoster = [];
    public int ChunkPaintTally;

    public StrideLandBlock? ChunkAt(int gridX, int gridY)
    {
        return gridX >= 0 && gridX < MidWidth && gridY >= 0 && gridY < MidWidth
                ? Chunks[gridX * MidWidth + gridY]
                : null;
    }

    public void CalcPaintOrdering()
    {
        if (ChunkPaintRoster.Length < MidWidth * MidWidth)
            ChunkPaintRoster = new int[MidWidth * MidWidth];
        ChunkPaintTally = LandStrideOrder.FetchChunkOrdering(
            BeholderChunkX, BeholderChunkY, MidWidth, ChunkPaintRoster);

        for (int x = 0; x < MidWidth; ++x)
        {
            for (int y = 0; y < MidWidth; ++y)
            {
                var chunk = Chunks[x * MidWidth + y];
                if (chunk is null) continue;
                chunk.SecureChamberArrs();
                var direction = LandStrideOrder.FetchDir(
                    x - BeholderChunkX, y - BeholderChunkY);
                (int cx, int cy) = LandStrideOrder.ClosestChamber(
                    direction, BeholderChamberX, BeholderChamberY, chunk.FlankChamberTally);
                if (cx == chunk.ClosestX && cy == chunk.ClosestY) continue;
                chunk.ClosestX = cx;
                chunk.ClosestY = cy;
                LandStrideOrder.PopulateChamberOrderingFarawayToNearby(
                    cx, cy, chunk.FlankChamberTally,
                    chunk.PaintArr.AsSpan(0, chunk.FlankChamberTally * chunk.FlankChamberTally));
            }
        }
    }

    public void VerifyChunks(in StridePlane cyPlane, StridePortalView engagedViews)
    {
        foreach (StrideLandBlock? chunk in Chunks)
        {
            if (chunk is null) continue;
            chunk.SecureChamberArrs();
            chunk.InLens = StrideBoundingType.Outside;
            Array.Clear(chunk.ChamberInLens, 0, chunk.FlankChamberTally * chunk.FlankChamberTally);
        }

        int lensTally = engagedViews.ViewCount;
        Span<float> limitsTemp = stackalloc float[32];
        int cornerRank = MidWidth + 1;
        float[][] intervals = new float[2 * cornerRank][];
        for (int idx = 0; idx < intervals.Length; ++idx) intervals[idx] = new float[32];

        int v = 0;
        while (true)
        {
            StridePlane[] rimPlanes;
            int rimTally;
            bool previous;
            if (lensTally is 0)
            {
                rimPlanes = [];
                rimTally = 0;
                previous = true;
            }
            else
            {
                var poly = engagedViews.View.Polys[v];
                rimPlanes = new StridePlane[poly.VertexCount];
                for (int kdx = 0; kdx < poly.VertexCount; ++kdx)
                    rimPlanes[kdx] = engagedViews.View.Vertices[poly.VertexIndex + kdx].Plane;
                rimTally = poly.VertexCount;
                ++v;
                previous = v == lensTally;
            }

            for (int jdx = 0; jdx <= MidWidth; ++jdx)
                StrideVisibilityMath.PopulateClipHeights(
                    BeholderRealmOriginX + (0 - BeholderChunkX) * ChunkLen,
                    BeholderRealmOriginY + (jdx - BeholderChunkY) * ChunkLen,
                    cyPlane, rimPlanes, intervals[jdx]);
            for (int bx = 0; bx < MidWidth; ++bx)
            {
                int westRank = (bx & 1) * cornerRank;
                int eastRank = ((bx - 1) & 1) * cornerRank;
                for (int jdx = 0; jdx <= MidWidth; ++jdx)
                    StrideVisibilityMath.PopulateClipHeights(
                        BeholderRealmOriginX + (bx + 1 - BeholderChunkX) * ChunkLen,
                        BeholderRealmOriginY + (jdx - BeholderChunkY) * ChunkLen,
                        cyPlane, rimPlanes, intervals[eastRank + jdx]);
                for (int by = 0; by < MidWidth; ++by)
                {
                    var chunk = Chunks[bx * MidWidth + by];
                    if (chunk is null) continue;
                    var bt = StrideVisibilityMath.ChunkVerify(
                        intervals[westRank + by], intervals[westRank + by + 1],
                        intervals[eastRank + by], intervals[eastRank + by + 1],
                        rimTally, chunk.UpperZ, chunk.LowerZ);
                    if (bt != StrideBoundingType.Outside)
                    {
                        chunk.InLens = bt;
                        LandChamberVerify(chunk, bx, by, cyPlane, rimPlanes);
                    }
                }
            }
            if (previous) return;
        }
    }

    private static readonly float[][] ChamberGridTemp = BuildChamberGridTemp();

    private static float[][] BuildChamberGridTemp()
    {
        float[][] grid = new float[2 * 9][];
        for (int idx = 0; idx < grid.Length; ++idx) grid[idx] = new float[32];
        return grid;
    }

    private void LandChamberVerify(
        StrideLandBlock chunk, int bx, int by,
        in StridePlane cyPlane, StridePlane[] rimPlanes)
    {
        int num = chunk.FlankChamberTally;
        if (num is not 8)
        {
            for (int idx = 0; idx < num * num; ++idx)
                chunk.ChamberInLens[idx] = StrideBoundingType.PartiallyInside;
            return;
        }
        if (chunk.InLens == StrideBoundingType.EntirelyInside)
        {
            for (int idx = 0; idx < num * num; ++idx)
                chunk.ChamberInLens[idx] = StrideBoundingType.EntirelyInside;
            return;
        }
        float x0 = BeholderRealmOriginX + (bx - BeholderChunkX) * ChunkLen;
        float y0 = BeholderRealmOriginY + (by - BeholderChunkY) * ChunkLen;
        int cornerRank = num + 1;
        float[][] grid = ChamberGridTemp;
        for (int jdx = 0; jdx <= num; ++jdx)
            StrideVisibilityMath.PopulateClipHeights(
                x0, jdx * ChamberLen + y0, cyPlane, rimPlanes, grid[jdx]);
        for (int cx = 0; cx < num; ++cx)
        {
            int westRank = (cx & 1) * cornerRank;
            int eastRank = ((cx - 1) & 1) * cornerRank;
            for (int jdx = 0; jdx <= num; ++jdx)
                StrideVisibilityMath.PopulateClipHeights(
                    (cx + 1) * ChamberLen + x0, jdx * ChamberLen + y0,
                    cyPlane, rimPlanes, grid[eastRank + jdx]);
            for (int cy = 0; cy < num; ++cy)
            {
                if (chunk.ChamberInLens[num * cx + cy] != StrideBoundingType.Outside)
                    continue;   // union across views: never downgrade
                chunk.ChamberInLens[num * cx + cy] = StrideVisibilityMath.ChunkVerify(
                    grid[westRank + cy], grid[westRank + cy + 1],
                    grid[eastRank + cy], grid[eastRank + cy + 1],
                    rimPlanes.Length, chunk.UpperZ, chunk.LowerZ);
            }
        }
    }
}
