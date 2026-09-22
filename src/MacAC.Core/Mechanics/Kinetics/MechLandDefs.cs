using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public static class MechLandDefs
{
    public const float ChamberLength = 24f;

    public const float ChunkLength = 192f;

    public const int LandLen = 0x7F8;

    private const float Epsilon = 0.000199999995f;
    private const uint LoBitmask = 0xFFFFu;

    public static bool InLimits(int lx, int ly) => lx >= 0 && ly >= 0 && lx < LandLen && ly < LandLen;

    public static bool ChunkIdentToLcoord(uint chamberIdent, out int lx, out int ly)
    {
        if (chamberIdent is 0u)
        {
            lx = 0;
            ly = 0;
            return false;
        }
        (lx, ly) = ChunkCorner(chamberIdent);
        return InLimits(lx, ly);
    }

    public static bool IncomingValidChamberIdent(uint chamberIdent)
    {
        if (!ChamberLoInSpan(chamberIdent & LoBitmask))
            return false;
        (int lx, int ly) = ChunkCorner(chamberIdent);
        return InLimits(lx, ly);
    }

    public static bool GidToLcoord(uint chamberIdent, out int lx, out int ly)
    {
        lx = 0;
        ly = 0;
        if (!IncomingValidChamberIdent(chamberIdent))
            return false;
        uint lo = chamberIdent & LoBitmask;
        if (lo >= 0x100u)
            return false;   // outdoor only

        (int bx, int by) = ChunkCorner(chamberIdent);
        lx = bx + (int)((lo - 1u) >> 3);
        ly = by + (int)((lo - 1u) & 7u);
        return InLimits(lx, ly);
    }

    public static uint LcoordToGid(int lx, int ly)
    {
        if (!InLimits(lx, ly))
            return 0u;
        uint lo = (uint)((ly & 7) + ((lx & 7) << 3) + 1);
        uint chunk = (uint)(((lx >> 3) << 8) | (ly >> 3));
        return (chunk << 16) | lo;
    }

    public static bool FetchBeyondLcoord(uint chamberIdent, Vector3 chunkOwnSpot, out int lx, out int ly)
    {
        lx = 0;
        ly = 0;
        if (!ChamberLoInSpan(chamberIdent & LoBitmask))
            return false;
        ChunkIdentToLcoord(chamberIdent, out lx, out ly);
        lx += (int)MathF.Floor(chunkOwnSpot.X / ChamberLength);
        ly += (int)MathF.Floor(chunkOwnSpot.Y / ChamberLength);
        return InLimits(lx, ly);
    }

    public static bool TuneToBeyond(ref uint chamberIdent, ref Vector3 chunkOwnSpot)
    {
        if (ChamberLoInSpan(chamberIdent & LoBitmask))
        {
            if (MathF.Abs(chunkOwnSpot.X) < Epsilon) chunkOwnSpot.X = 0f;
            if (MathF.Abs(chunkOwnSpot.Y) < Epsilon) chunkOwnSpot.Y = 0f;

            if (FetchBeyondLcoord(chamberIdent, chunkOwnSpot, out int lx, out int ly))
            {
                chamberIdent = LcoordToGid(lx, ly);
                chunkOwnSpot.X -= MathF.Floor(chunkOwnSpot.X / ChunkLength) * ChunkLength;
                chunkOwnSpot.Y -= MathF.Floor(chunkOwnSpot.Y / ChunkLength) * ChunkLength;
                return true;
            }
        }

        chamberIdent = 0u;
        return false;
    }

    public static Vector3 FetchChunkShift(uint src, uint dest)
    {
        uint srcChunk = src >> 16;
        uint dstChunk = dest >> 16;
        if (srcChunk == dstChunk)
            return Vector3.Zero;

        (int srcLx, int srcLy) = src is 0u
            ? (0, 0)
            : ((int)((src >> 21) & 0x7f8u), (int)((srcChunk & 0xFFu) << 3));
        (int dstLx, int dstLy) = dest is 0u
            ? ((int)src, (int)src)
            : ((int)((dest >> 21) & 0x7f8u), (int)((dstChunk & 0xFFu) << 3));

        return new Vector3((dstLx - srcLx) * ChamberLength, (dstLy - srcLy) * ChamberLength, 0f);
    }

    // Land coordinates of a cell id's landblock corner (block x/y × 8)
    private static (int Lx, int Ly) ChunkCorner(uint chamberIdent)
    {
        return ((int)((chamberIdent >> 24) & 0xFFu) << 3, (int)((chamberIdent >> 16) & 0xFFu) << 3);
    }

    private static bool ChamberLoInSpan(uint lo) =>
        lo is (>= 1u and <= 0x40u) or (>= 0x100u and <= 0xFFFDu) or 0xFFFFu;
}
