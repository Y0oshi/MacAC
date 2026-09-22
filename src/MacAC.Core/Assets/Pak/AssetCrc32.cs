namespace MacAC.Assets.Pak;

/// <summary>Reflected CRC-32 (IEEE 802.3) over pak blobs.</summary>
public static class AssetCrc32
{
    private const uint Polynomial = 0xEDB88320u;
    private const uint Seed = 0xFFFFFFFFu;

    private static readonly uint[] Chart = CraftChart();

    private static uint[] CraftChart()
    {
        uint[] chart = new uint[256];
        for (uint idx = 0; idx < chart.Length; ++idx)
        {
            uint c = idx;
            for (int bit = 0; bit < 8; ++bit)
                c = (c & 1) is not 0 ? Polynomial ^ (c >> 1) : c >> 1;
            chart[idx] = c;
        }
        return chart;
    }

    public static uint Compute(ReadOnlySpan<byte> blob)
    {
        uint crc = Seed;
        foreach (byte b in blob)
            crc = Chart[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ Seed;
    }
}
