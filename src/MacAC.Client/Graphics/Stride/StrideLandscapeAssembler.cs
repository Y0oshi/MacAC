using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public sealed class StrideLandscapeAssembler
{
    public const int MidRadius = 25;

    public const int GridWidth = MidRadius * 2 + 1;

    private sealed class ChunkBlob
    {
        public required float MaxZ;
        public required float MinZ;
        public required IReadOnlyList<StrideBuildingMint.Entry> Buildings;
    }

    private readonly Dictionary<(int X, int Y), ChunkBlob> _chunks = [];
    private int _beholderChunkX = int.MinValue;
    private int _beholderChunkY = int.MinValue;

    public StrideLandscape Landscape { get; } = new()
    {
        MidWidth = GridWidth,
        Chunks = new StrideLandBlock?[GridWidth * GridWidth],
        BeholderChunkX = MidRadius,
        BeholderChunkY = MidRadius,
    };

    public void BroadcastLb(
        uint lbIdent, float upperZ, float lowerZ,
        IReadOnlyList<StrideBuildingMint.Entry> structures)
    {
        (int bx, int by) = ChunkCoords(lbIdent);
        _chunks[(bx, by)] = new ChunkBlob { MaxZ = upperZ, MinZ = lowerZ, Buildings = structures };
        RenewSocketIfWindowed(bx, by);
    }

    public void WipeStructures(uint lbIdent)
    {
        (int bx, int by) = ChunkCoords(lbIdent);
        if (!_chunks.TryGetValue((bx, by), out ChunkBlob? blob))
            return;
        blob.Buildings = Array.Empty<StrideBuildingMint.Entry>();
        RenewSocketIfWindowed(bx, by);
    }

    public void RetireLb(uint lbIdent)
    {
        (int bx, int by) = ChunkCoords(lbIdent);
        _chunks.Remove((bx, by));
        RenewSocketIfWindowed(bx, by);
    }

    public void AssignBeholder(uint camChamberIdent, Vector3 camOrigin)
    {
        int camChunkX = (int)(camChamberIdent >> 24);
        int camChunkY = (int)((camChamberIdent >> 16) & 0xFF);
        if (camChunkX != _beholderChunkX || camChunkY != _beholderChunkY)
        {
            _beholderChunkX = camChunkX;
            _beholderChunkY = camChunkY;
            ReassemblePane();
        }

        uint lo = camChamberIdent & 0xFFFFu;
        if (lo is >= 1 and <= 0x40)
        {
            int chamberOrdinal = (int)lo - 1;
            Landscape.BeholderChamberX = chamberOrdinal / 8;
            Landscape.BeholderChamberY = chamberOrdinal % 8;
            Landscape.BeholderRealmOriginX = DeriveBeholderChunkOrigin(
                camOrigin.X, Landscape.BeholderChamberX);
            Landscape.BeholderRealmOriginY = DeriveBeholderChunkOrigin(
                camOrigin.Y, Landscape.BeholderChamberY);
        }
        else
        {
            Landscape.BeholderRealmOriginX = MathF.Floor(
                camOrigin.X / StrideLandscape.ChunkLen) * StrideLandscape.ChunkLen;
            Landscape.BeholderRealmOriginY = MathF.Floor(
                camOrigin.Y / StrideLandscape.ChunkLen) * StrideLandscape.ChunkLen;
            Landscape.BeholderChamberX = Math.Clamp((int)MathF.Floor(
                (camOrigin.X - Landscape.BeholderRealmOriginX) / StrideLandscape.ChamberLen), 0, 7);
            Landscape.BeholderChamberY = Math.Clamp((int)MathF.Floor(
                (camOrigin.Y - Landscape.BeholderRealmOriginY) / StrideLandscape.ChamberLen), 0, 7);
        }
    }

    internal static int FlankChamberTallyForLoop(int loop)
        => loop <= 1 ? 8 : loop is 2 ? 4 : loop <= 4 ? 2 : 1;

    internal static int LoopOf(int gridX, int gridY)
        => Math.Max(Math.Abs(gridX - MidRadius), Math.Abs(gridY - MidRadius));

    private static float DeriveBeholderChunkOrigin(float camAxis, int chamberAxis)
    {
        float chamberMiddle = (chamberAxis + 0.5f) * StrideLandscape.ChamberLen;
        return MathF.Round(
            (camAxis - chamberMiddle) / StrideLandscape.ChunkLen,
            MidpointRounding.AwayFromZero) * StrideLandscape.ChunkLen;
    }

    private void ReassemblePane()
    {
        for (int gx = 0; gx < GridWidth; ++gx)
        {
            for (int gy = 0; gy < GridWidth; ++gy)
            {
                int bx = _beholderChunkX + gx - MidRadius;
                int by = _beholderChunkY + gy - MidRadius;
                Landscape.Chunks[gx * GridWidth + gy] = AssembleSocket(bx, by, gx, gy);
            }
        }
    }

    private void RenewSocketIfWindowed(int bx, int by)
    {
        if (_beholderChunkX == int.MinValue)
            return;   // SetViewer has never run - no window to refresh yet
        int gx = bx - _beholderChunkX + MidRadius;
        int gy = by - _beholderChunkY + MidRadius;
        if (gx < 0 || gx >= GridWidth || gy < 0 || gy >= GridWidth)
            return;
        Landscape.Chunks[gx * GridWidth + gy] = AssembleSocket(bx, by, gx, gy);
    }

    private StrideLandBlock? AssembleSocket(int bx, int by, int gx, int gy)
    {
        if (bx < 0 || bx > 0xFF || by < 0 || by > 0xFF)
            return null;
        if (!_chunks.TryGetValue((bx, by), out ChunkBlob? blob))
            return null;

        int flankChamberTally = FlankChamberTallyForLoop(LoopOf(gx, gy));
        StrideLandBlock chunk = new StrideLandBlock
        {
            LbTag = (uint)bx << 24 | (uint)by << 16,
            FlankChamberTally = flankChamberTally,
            Ring = LoopOf(gx, gy),
            UpperZ = blob.MaxZ,
            LowerZ = blob.MinZ,
        };
        chunk.SecureChamberArrs();

        if (flankChamberTally is 8)
        {
            foreach (StrideBuildingMint.Entry listing in blob.Buildings)
            {
                int chamberOrdinal = (int)(listing.Building.LocusChamberIdent & 0xFFFFu) - 1;
                if (chamberOrdinal is >= 0 and < 64)
                    chunk.ChamberStructures[chamberOrdinal] = listing.Building;
            }
        }
        else if (flankChamberTally > 1 && blob.Buildings.Count > 0)
        {
            AssembleSocketBranch(flankChamberTally, blob, chunk);
        }
        return chunk;
    }

    private void AssembleSocketBranch(int flankChamberTally, ChunkBlob blob, StrideLandBlock chunk)
    {
        int span = 8 / flankChamberTally;
        var bins = new List<StrollStructure>?[flankChamberTally * flankChamberTally];
        foreach (StrideBuildingMint.Entry listing in blob.Buildings)
        {
            int mooring = (int)(listing.Building.LocusChamberIdent & 0xFFFFu) - 1;
            if ((uint)mooring >= 64u)
                continue;
            int chamber = (mooring / 8 / span) * flankChamberTally + mooring % 8 / span;
            (bins[chamber] ??= []).Add(listing.Building);
        }
        chunk.CoarseChamberStructures = new StrollStructure[bins.Length][];
        for (int idx = 0; idx < bins.Length; ++idx)
            chunk.CoarseChamberStructures[idx] = bins[idx]?.ToArray() ?? [];
    }

    private static (int X, int Y) ChunkCoords(uint lbIdent)
        => ((int)((lbIdent >> 24) & 0xFFu), (int)((lbIdent >> 16) & 0xFFu));
}
