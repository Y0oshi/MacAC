namespace MacAC.Client.Graphics.Stride;

public static class LandStrideOrder
{
    private static readonly int[] XConst = [0, 0, 0, 0, 1, 0, -1, 0];
    private static readonly int[] XLoop = [0, -1, 0, 1, 0, -1, 0, 1];
    private static readonly int[] XHop = [-1, 0, 1, 0, 1, 0, -1, 0];
    private static readonly int[] YConst = [0, 0, 0, 0, 0, 1, 0, -1];
    private static readonly int[] YHop = [0, -1, 0, 1, 0, 1, 0, -1];
    private static readonly int[] YLoop = [1, 0, -1, 0, 1, 0, -1, 0];

    public static int FetchChunkOrdering(int beholderX, int beholderY, int width, Span<int> ordering)
    {
        int tally = 0;
        ordering[tally++] = beholderX * width + beholderY;
        int upperLoop = UpperLoopTo(beholderX, beholderY, width, width);
        for (int loop = 1; loop <= upperLoop; ++loop)
        {
            for (int hop = 0; hop < loop; ++hop)
            {
                for (int socket = 0; socket < 8; ++socket)
                {
                    int x = XHop[socket] * hop + XLoop[socket] * loop + XConst[socket] + beholderX;
                    int y = YHop[socket] * hop + YLoop[socket] * loop + YConst[socket] + beholderY;
                    if (x >= 0 && x < width && y >= 0 && y < width)
                        ordering[tally++] = x * width + y;
                }
            }
        }
        return tally;
    }

    public static void PopulateChamberOrderingFarawayToNearby(int closestX, int closestY, int flank, Span<int> ordering)
    {
        int kdx = flank * flank;
        ordering[--kdx] = closestX * flank + closestY;
        int upperLoop = UpperLoopTo(closestX, closestY, flank, flank);
        for (int loop = 1; loop <= upperLoop; ++loop)
        {
            for (int hop = 0; hop < loop; ++hop)
            {
                for (int socket = 0; socket < 8; ++socket)
                {
                    int x = XHop[socket] * hop + XLoop[socket] * loop + XConst[socket] + closestX;
                    int y = YHop[socket] * hop + YLoop[socket] * loop + YConst[socket] + closestY;
                    if (x >= 0 && x < flank && y >= 0 && y < flank)
                        ordering[--kdx] = x * flank + y;
                }
            }
        }
    }

    public static LandDir FetchDir(int dx, int dy)
    {
        if (dx < 0)
        {
            return dy < 0 ? LandDir.SouthWest : dy > 0 ? LandDir.NorthWest : LandDir.West;
        }
        if (dx is 0)
        {
            return dy < 0 ? LandDir.South : dy > 0 ? LandDir.North : LandDir.InViewerBlock;
        }
        return dy < 0 ? LandDir.SouthEast : dy > 0 ? LandDir.NorthEast : LandDir.East;
    }

    public static (int X, int Y) ClosestChamber(
        LandDir dir, int beholderSqX, int beholderSqY, int flank)
    {
        int scaling = 8 / flank;
        return dir switch
        {
            LandDir.InViewerBlock => (beholderSqX / scaling, beholderSqY / scaling),
            LandDir.North => (beholderSqX / scaling, 0),
            LandDir.South => (beholderSqX / scaling, flank - 1),
            LandDir.East => (0, beholderSqY / scaling),
            LandDir.West => (flank - 1, beholderSqY / scaling),
            LandDir.NorthWest => (flank - 1, 0),
            LandDir.SouthWest => (flank - 1, flank - 1),
            LandDir.NorthEast => (0, 0),
            LandDir.SouthEast => (0, flank - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(dir)),
        };
    }

    private static int UpperLoopTo(int x, int y, int width, int height)
    {
        int upper = x;
        if (y > upper) upper = y;
        if (width - 1 - x > upper) upper = width - 1 - x;
        if (height - 1 - y > upper) upper = height - 1 - y;
        return upper;
    }
}

public enum LandDir
{
    InViewerBlock = 0,
    North = 1,
    South = 2,
    East = 3,
    West = 4,
    NorthWest = 5,
    SouthWest = 6,
    NorthEast = 7,
    SouthEast = 8,
}
