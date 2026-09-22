namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static class ChunkCompressionMipSequence
{
    internal readonly record struct Tier(int MipLevel, int Width, int Height, byte[] Data);

    internal static IReadOnlyList<Tier> AssembleCompressed(
        GpuBitmapFmt format,
        ReadOnlySpan<byte> level0,
        int width,
        int height,
        int mipTierTally)
    {
        if (!ChunkCompressionCodec.IsChunkCompressed(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "This chain builder is for BC formats");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mipTierTally);
        if (mipTierTally is 1)
            return [];

        byte[] rgba = ChunkCompressionCodec.UnpackTier(format, level0, width, height);
        return AssembleFromRgba(format, rgba, width, height, mipTierTally);
    }

    internal static IReadOnlyList<Tier> AssembleFromRgba(
        GpuBitmapFmt fmt,
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        int mipTierTally)
    {
        List<Tier> tiers = new List<Tier>(Math.Max(0, mipTierTally - 1));
        byte[] latest = rgba.ToArray();
        int latestWidth = width;
        int latestHeight = height;

        for (int tier = 1; tier < mipTierTally; ++tier)
        {
            (byte[] upcoming, int upcomingWidth, int upcomingHeight) = Downsample(latest, latestWidth, latestHeight);
            byte[] encoded = ChunkCompressionCodec.IsChunkCompressed(fmt)
                ? ChunkCompressionCodec.PackTier(fmt, upcoming, upcomingWidth, upcomingHeight)
                : upcoming;
            tiers.Add(new Tier(tier, upcomingWidth, upcomingHeight, encoded));
            latest = upcoming;
            latestWidth = upcomingWidth;
            latestHeight = upcomingHeight;
        }

        return tiers;
    }

    internal static (byte[] Rgba, int Width, int Height) Downsample(
        ReadOnlySpan<byte> rgba,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int upcomingWidth = Math.Max(1, width / 2);
        int upcomingHeight = Math.Max(1, height / 2);
        byte[] product = new byte[upcomingWidth * upcomingHeight * 4];

        for (int y = 0; y < upcomingHeight; ++y)
        {
            int y0 = Math.Min((y * 2) + 0, height - 1);
            int y1 = Math.Min((y * 2) + 1, height - 1);
            for (int x = 0; x < upcomingWidth; ++x)
            {
                int x0 = Math.Min((x * 2) + 0, width - 1);
                int x1 = Math.Min((x * 2) + 1, width - 1);

                int a = ((y0 * width) + x0) * 4;
                int b = ((y0 * width) + x1) * 4;
                int c = ((y1 * width) + x0) * 4;
                int d = ((y1 * width) + x1) * 4;
                int dest = ((y * upcomingWidth) + x) * 4;

                for (int lane = 0; lane < 4; ++lane)
                {
                    int total = rgba[a + lane] + rgba[b + lane] + rgba[c + lane] + rgba[d + lane];
                    product[dest + lane] = (byte)((total + 2) / 4);
                }
            }
        }

        return (product, upcomingWidth, upcomingHeight);
    }
}
