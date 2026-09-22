namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static class ChunkCompressionCodec
{
    // Edge of a BC block in texels
    internal const int ChunkReach = 4;

    internal static int ChunkByteSize(GpuBitmapFmt format)
    {
        return format switch
        {
            GpuBitmapFmt.Bc1Unorm => 8,
            GpuBitmapFmt.Bc2Unorm or GpuBitmapFmt.Bc3Unorm => 16,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a block-compressed format"),
        };
    }

    internal static bool IsChunkCompressed(GpuBitmapFmt fmt)
    {
        return fmt is GpuBitmapFmt.Bc1Unorm or GpuBitmapFmt.Bc2Unorm or GpuBitmapFmt.Bc3Unorm;
    }

    internal static int ChunkTally(int reach) => Math.Max(1, (reach + ChunkReach - 1) / ChunkReach);

    internal static int TierByteSize(GpuBitmapFmt fmt, int width, int height) =>
        ChunkTally(width) * ChunkTally(height) * ChunkByteSize(fmt);

    internal static byte[] UnpackTier(
        GpuBitmapFmt fmt,
        ReadOnlySpan<byte> blocks,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int chunkDims = ChunkByteSize(fmt);
        int chunksX = ChunkTally(width);
        int chunksY = ChunkTally(height);
        if (blocks.Length < chunksX * chunksY * chunkDims)
        {
            throw new ArgumentException(
                $"A {width}x{height} {fmt} level needs {chunksX * chunksY * chunkDims} bytes; " +
                $"{blocks.Length} were supplied",
                nameof(blocks));
        }

        byte[] rgba = new byte[width * height * 4];
        Span<byte> texels = stackalloc byte[ChunkReach * ChunkReach * 4];
        for (int by = 0; by < chunksY; ++by)
        {
            for (int bx = 0; bx < chunksX; ++bx)
            {
                int shift = ((by * chunksX) + bx) * chunkDims;
                UnpackChunk(fmt, blocks.Slice(shift, chunkDims), texels);

                for (int y = 0; y < ChunkReach; ++y)
                {
                    int markY = (by * ChunkReach) + y;
                    if (markY >= height)
                        break;
                    for (int x = 0; x < ChunkReach; ++x)
                    {
                        int markX = (bx * ChunkReach) + x;
                        if (markX >= width)
                            break;
                        int src = ((y * ChunkReach) + x) * 4;
                        int dest = ((markY * width) + markX) * 4;
                        texels.Slice(src, 4).CopyTo(rgba.AsSpan(dest, 4));
                    }
                }
            }
        }

        return rgba;
    }

    // Decodes one block into 16 RGBA8 texels in row-major order
    internal static void UnpackChunk(GpuBitmapFmt fmt, ReadOnlySpan<byte> chunk, Span<byte> rgba)
    {
        if (rgba.Length < ChunkReach * ChunkReach * 4)
            throw new ArgumentException("A decoded block needs 64 bytes", nameof(rgba));

        int colourShift = fmt == GpuBitmapFmt.Bc1Unorm ? 0 : 8;
        UnpackColourChunk(
            chunk.Slice(colourShift, 8),
            allowSeeThruManner: fmt == GpuBitmapFmt.Bc1Unorm,
            rgba);

        switch (fmt)
        {
            case GpuBitmapFmt.Bc2Unorm:
                UnpackExplicitAlpha(chunk[..8], rgba);
                break;
            case GpuBitmapFmt.Bc3Unorm:
                UnpackInterpolatedAlpha(chunk[..8], rgba);
                break;
        }
    }

    internal static byte[] PackTier(
        GpuBitmapFmt fmt,
        ReadOnlySpan<byte> rgba,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length < width * height * 4)
            throw new ArgumentException($"A {width}x{height} RGBA8 level needs {width * height * 4} bytes.", nameof(rgba));

        int chunkDims = ChunkByteSize(fmt);
        int chunksX = ChunkTally(width);
        int chunksY = ChunkTally(height);
        byte[] product = new byte[chunksX * chunksY * chunkDims];

        Span<byte> texels = stackalloc byte[ChunkReach * ChunkReach * 4];
        for (int by = 0; by < chunksY; ++by)
        {
            for (int bx = 0; bx < chunksX; ++bx)
            {
                CollectChunk(rgba, width, height, bx, by, texels);
                PackChunk(fmt, texels, product.AsSpan(((by * chunksX) + bx) * chunkDims, chunkDims));
            }
        }

        return product;
    }

    internal static void PackChunk(GpuBitmapFmt format, ReadOnlySpan<byte> rgba, Span<byte> chunk)
    {
        chunk.Clear();
        switch (format)
        {
            case GpuBitmapFmt.Bc1Unorm:
                PackColourChunk(rgba, allowSeeThruManner: true, chunk[..8]);
                break;
            case GpuBitmapFmt.Bc2Unorm:
                PackExplicitAlpha(rgba, chunk[..8]);
                PackColourChunk(rgba, allowSeeThruManner: false, chunk.Slice(8, 8));
                break;
            case GpuBitmapFmt.Bc3Unorm:
                PackInterpolatedAlpha(rgba, chunk[..8]);
                PackColourChunk(rgba, allowSeeThruManner: false, chunk.Slice(8, 8));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Not a block-compressed format");
        }
    }

    private static void UnpackColourChunk(ReadOnlySpan<byte> chunk, bool allowSeeThruManner, Span<byte> rgba)
    {
        ushort c0 = (ushort)(chunk[0] | (chunk[1] << 8));
        ushort c1 = (ushort)(chunk[2] | (chunk[3] << 8));
        uint ordinals = (uint)(chunk[4] | (chunk[5] << 8) | (chunk[6] << 16) | (chunk[7] << 24));

        Span<byte> swatch = stackalloc byte[4 * 4];
        Unpack565(c0, swatch[..4]);
        Unpack565(c1, swatch.Slice(4, 4));

        bool seeThru = allowSeeThruManner && c0 <= c1;
        if (seeThru)
        {
            for (int lane = 0; lane < 3; ++lane)
                swatch[8 + lane] = (byte)((swatch[lane] + swatch[4 + lane]) / 2);
            swatch[11] = 255;
            swatch[12] = 0;
            swatch[13] = 0;
            swatch[14] = 0;
            swatch[15] = 0;
        }
        else
        {
            for (int lane = 0; lane < 3; ++lane)
            {
                swatch[8 + lane] = (byte)(((2 * swatch[lane]) + swatch[4 + lane]) / 3);
                swatch[12 + lane] = (byte)((swatch[lane] + (2 * swatch[4 + lane])) / 3);
            }

            swatch[11] = 255;
            swatch[15] = 255;
        }

        for (int texel = 0; texel < 16; ++texel)
        {
            int ordinal = (int)((ordinals >> (texel * 2)) & 0x3);
            swatch.Slice(ordinal * 4, 4).CopyTo(rgba.Slice(texel * 4, 4));
        }
    }

    private static void UnpackExplicitAlpha(ReadOnlySpan<byte> alphaChunk, Span<byte> rgba)
    {
        for (int texel = 0; texel < 16; ++texel)
        {
            int nibble = (alphaChunk[texel / 2] >> ((texel % 2) * 4)) & 0xF;
            // 4-bit alpha replicated into 8 bits, the standard BC2 expansion
            rgba[(texel * 4) + 3] = (byte)((nibble * 255) / 15);
        }
    }

    private static void UnpackInterpolatedAlpha(ReadOnlySpan<byte> alphaChunk, Span<byte> rgba)
    {
        byte a0 = alphaChunk[0];
        byte a1 = alphaChunk[1];
        Span<byte> swatch = stackalloc byte[8];
        swatch[0] = a0;
        swatch[1] = a1;
        if (a0 > a1)
        {
            for (int idx = 1; idx <= 6; ++idx)
                swatch[idx + 1] = (byte)((((7 - idx) * a0) + (idx * a1)) / 7);
        }
        else
        {
            for (int idx = 1; idx <= 4; ++idx)
                swatch[idx + 1] = (byte)((((5 - idx) * a0) + (idx * a1)) / 5);
            swatch[6] = 0;
            swatch[7] = 255;
        }

        ulong bitset = 0;
        for (int idx = 0; idx < 6; ++idx)
            bitset |= (ulong)alphaChunk[2 + idx] << (idx * 8);

        for (int texel = 0; texel < 16; ++texel)
        {
            int ordinal = (int)((bitset >> (texel * 3)) & 0x7);
            rgba[(texel * 4) + 3] = swatch[ordinal];
        }
    }

    private static ushort Pack565(int r, int g, int b)
    {
        int r5 = ((Math.Clamp(r, 0, 255) * 31) + 127) / 255;
        int g6 = ((Math.Clamp(g, 0, 255) * 63) + 127) / 255;
        int b5 = ((Math.Clamp(b, 0, 255) * 31) + 127) / 255;
        return (ushort)((r5 << 11) | (g6 << 5) | b5);
    }

    private static void Unpack565(ushort dense, Span<byte> rgba)
    {
        int r = (dense >> 11) & 0x1F;
        int g = (dense >> 5) & 0x3F;
        int b = dense & 0x1F;
        rgba[0] = (byte)((r << 3) | (r >> 2));
        rgba[1] = (byte)((g << 2) | (g >> 4));
        rgba[2] = (byte)((b << 3) | (b >> 2));
        rgba[3] = 255;
    }

    private static void CollectChunk(
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        int chunkX,
        int chunkY,
        Span<byte> texels)
    {
        for (int y = 0; y < ChunkReach; ++y)
        {
            int srcY = Math.Min((chunkY * ChunkReach) + y, height - 1);
            for (int x = 0; x < ChunkReach; ++x)
            {
                int srcX = Math.Min((chunkX * ChunkReach) + x, width - 1);
                int src = ((srcY * width) + srcX) * 4;
                rgba.Slice(src, 4).CopyTo(texels.Slice(((y * ChunkReach) + x) * 4, 4));
            }
        }
    }

    private static void PackColourChunk(ReadOnlySpan<byte> rgba, bool allowSeeThruManner, Span<byte> chunk)
    {
        bool needsTransparency = false;
        if (allowSeeThruManner)
        {
            for (int texel = 0; texel < 16 && !needsTransparency; ++texel)
                needsTransparency = rgba[(texel * 4) + 3] < 128;
        }

        SeekColourExtremes(rgba, needsTransparency, out int lowerR, out int lowerG, out int lowerB, out int upperR, out int upperG, out int upperB);
        ushort c0 = Pack565(upperR, upperG, upperB);
        ushort c1 = Pack565(lowerR, lowerG, lowerB);

        if (needsTransparency)
        {
            if (c0 > c1)
                (c0, c1) = (c1, c0);
        }
        else if (c0 <= c1)
        {
            if (c0 == c1)
            {
                if (c1 is 0)
                    c0 = 1;
                else
                    c1 = (ushort)(c1 - 1);
            }
            else
            {
                (c0, c1) = (c1, c0);
            }
        }

        Span<byte> swatch = stackalloc byte[4 * 4];
        Unpack565(c0, swatch[..4]);
        Unpack565(c1, swatch.Slice(4, 4));
        if (needsTransparency)
        {
            for (int lane = 0; lane < 3; ++lane)
                swatch[8 + lane] = (byte)((swatch[lane] + swatch[4 + lane]) / 2);
        }
        else
        {
            for (int lane = 0; lane < 3; ++lane)
            {
                swatch[8 + lane] = (byte)(((2 * swatch[lane]) + swatch[4 + lane]) / 3);
                swatch[12 + lane] = (byte)((swatch[lane] + (2 * swatch[4 + lane])) / 3);
            }
        }

        uint ordinals = 0;
        int swatchDims = needsTransparency ? 3 : 4;
        for (int texel = 0; texel < 16; ++texel)
        {
            int ordinal = needsTransparency && rgba[(texel * 4) + 3] < 128 ? 3 : ClosestSwatchListing(rgba.Slice(texel * 4, 3), swatch, swatchDims);
            ordinals |= (uint)ordinal << (texel * 2);
        }

        chunk[0] = (byte)(c0 & 0xFF);
        chunk[1] = (byte)(c0 >> 8);
        chunk[2] = (byte)(c1 & 0xFF);
        chunk[3] = (byte)(c1 >> 8);
        chunk[4] = (byte)(ordinals & 0xFF);
        chunk[5] = (byte)((ordinals >> 8) & 0xFF);
        chunk[6] = (byte)((ordinals >> 16) & 0xFF);
        chunk[7] = (byte)((ordinals >> 24) & 0xFF);
    }

    private static void SeekColourExtremes(
        ReadOnlySpan<byte> rgba,
        bool ignoreSeeThruTexels,
        out int lowerR, out int lowerG, out int lowerB,
        out int upperR, out int upperG, out int upperB)
    {
        lowerR = lowerG = lowerB = 255;
        upperR = upperG = upperB = 0;
        bool any = false;
        for (int texel = 0; texel < 16; ++texel)
        {
            if (ignoreSeeThruTexels && rgba[(texel * 4) + 3] < 128)
                continue;
            any = true;
            int r = rgba[texel * 4];
            int g = rgba[(texel * 4) + 1];
            int b = rgba[(texel * 4) + 2];
            lowerR = Math.Min(lowerR, r);
            lowerG = Math.Min(lowerG, g);
            lowerB = Math.Min(lowerB, b);
            upperR = Math.Max(upperR, r);
            upperG = Math.Max(upperG, g);
            upperB = Math.Max(upperB, b);
        }

        if (any)
            return;

        lowerR = lowerG = lowerB = 0;
        upperR = upperG = upperB = 0;
    }

    private static int ClosestSwatchListing(ReadOnlySpan<byte> colour, ReadOnlySpan<byte> swatch, int swatchDims)
    {
        int finest = 0;
        int finestGap = int.MaxValue;
        for (int listing = 0; listing < swatchDims; ++listing)
        {
            int dr = colour[0] - swatch[listing * 4];
            int dg = colour[1] - swatch[(listing * 4) + 1];
            int db = colour[2] - swatch[(listing * 4) + 2];
            int gap = (dr * dr) + (dg * dg) + (db * db);
            if (gap >= finestGap)
                continue;
            finestGap = gap;
            finest = listing;
        }

        return finest;
    }

    private static void PackExplicitAlpha(ReadOnlySpan<byte> rgba, Span<byte> alphaChunk)
    {
        for (int texel = 0; texel < 16; ++texel)
        {
            int nibble = (rgba[(texel * 4) + 3] * 15 + 127) / 255;
            int ordinal = texel / 2;
            alphaChunk[ordinal] = texel % 2 is 0 ? (byte)((alphaChunk[ordinal] & 0xF0) | nibble) : (byte)((alphaChunk[ordinal] & 0x0F) | (nibble << 4));
        }
    }

    private static void PackInterpolatedAlpha(ReadOnlySpan<byte> rgba, Span<byte> alphaChunk)
    {
        byte lower = 255;
        byte upper = 0;
        for (int texel = 0; texel < 16; ++texel)
        {
            byte a = rgba[(texel * 4) + 3];
            lower = Math.Min(lower, a);
            upper = Math.Max(upper, a);
        }

        byte a0 = upper;
        byte a1 = lower;
        if (a0 == a1)
        {
            if (a1 > 0)
                a1 = (byte)(a1 - 1);
            else
                a0 = 1;
        }

        Span<byte> swatch = stackalloc byte[8];
        swatch[0] = a0;
        swatch[1] = a1;
        for (int idx = 1; idx <= 6; ++idx)
            swatch[idx + 1] = (byte)((((7 - idx) * a0) + (idx * a1)) / 7);

        ulong bitset = 0;
        for (int texel = 0; texel < 16; ++texel)
        {
            byte a = rgba[(texel * 4) + 3];
            int finest = 0;
            int finestGap = int.MaxValue;
            for (int listing = 0; listing < 8; ++listing)
            {
                int gap = Math.Abs(a - swatch[listing]);
                if (gap >= finestGap)
                    continue;
                finestGap = gap;
                finest = listing;
            }

            bitset |= (ulong)finest << (texel * 3);
        }

        alphaChunk[0] = a0;
        alphaChunk[1] = a1;
        for (int idx = 0; idx < 6; ++idx)
            alphaChunk[2 + idx] = (byte)((bitset >> (idx * 8)) & 0xFF);
    }
}
