using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Paging;

public sealed partial class LandblockBuildMint
{
    private readonly IDatAccess _datFiles;

    private readonly IBakedContactSource _readiedImpacts;

    private readonly object _datMutex;

    private readonly float[] _heightTable;

    private readonly bool _printSceneryZ;

    public LandblockBuildMint(
        IDatAccess dats,
        IBakedContactSource preparedCollisions,
        object datLock,
        float[] heightTable,
        bool printSceneryZ = false)
    {
        _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
        _readiedImpacts = preparedCollisions ??
            throw new ArgumentNullException(nameof(preparedCollisions));
        _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));
        ArgumentNullException.ThrowIfNull(heightTable);
        if (heightTable.Length < 256)
            throw new ArgumentException(
                "The retail terrain height table must contain no fewer than 256 entries",
                nameof(heightTable));
        _heightTable = (float[])heightTable.Clone();
        _printSceneryZ = printSceneryZ;
    }

    private (float MaxZ, float MinZ) CalculateStrollZSlab(byte[] heights)
    {
        byte upperByte = 0, lowerByte = 255;
        foreach (byte h in heights)
        {
            if (h > upperByte) upperByte = h;
            if (h < lowerByte) lowerByte = h;
        }
        return (_heightTable[upperByte] + 200f, _heightTable[lowerByte] - 1f);
    }

    private static float ProbeLandZ(TerrainTile chunk, float[] heightChart, float ownX, float ownY)
    {
        uint lbX = (chunk.Id >> 24) & 0xFFu;
        uint lbY = (chunk.Id >> 16) & 0xFFu;
        return MacAC.Mechanics.Kinetics.LandCanvas.SampleZFromHeightmap(
            chunk.Heights, heightChart, lbX, lbY, ownX, ownY);
    }
}
