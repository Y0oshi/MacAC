using MacAC.Dat;
using MacAC.Mechanics.Drawing.Batches;

namespace MacAC.Mechanics.Realm;

// Re-shapes a DAT landblock's 81 terrain words into the batch builder's cell records
internal static class BatchSceneryAdapter
{
    private const int Samples = 9 * 9;

    public static TerrainCell[] AssembleLandListings(TerrainTile chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        TerrainCell[] chambers = new TerrainCell[Samples];
        for (int idx = 0; idx < Samples; ++idx)
        {
            TerrainSample word = chunk.Samples[idx];
            chambers[idx] = new TerrainCell(height: chunk.Heights[idx], texture: (byte)word.Kind, scenery: word.Scenery, road: word.Road, encounters: null);
        }
        return chambers;
    }
}
