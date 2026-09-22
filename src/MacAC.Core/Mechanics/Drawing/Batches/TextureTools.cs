using MacAC.Dat;

namespace MacAC.Mechanics.Drawing.Batches;

/// <summary>Pixel-format converters that expand DAT surfaces to RGBA8.</summary>
public static class TextureTools
{
    private const int Rgba = 4;
    private const int ClipLookupReservedSwatch = 8;

    public static byte[] BuildSolidTintTexture(Argb tint, int width, int height)
    {
        byte[] px = new byte[width * height * Rgba];
        for (int at = 0; at < px.Length; at += Rgba)
            Place(px, at, tint.Red, tint.Green, tint.Blue, tint.Alpha);
        return px;
    }

    public static void PopulateIndex16(byte[] src, ColorTable swatch, Span<byte> dst, int width, int height, bool isClipLookup = false)
    {
        int tally = width * height;
        for (int idx = 0; idx < tally; ++idx)
        {
            ushort ordinal = (ushort)(src[idx * 2] | (src[idx * 2 + 1] << 8));
            Paletted(dst, idx * Rgba, swatch, ordinal, isClipLookup);
        }
    }

    public static void PopulateP8(byte[] src, ColorTable swatch, Span<byte> dst, int width, int height, bool isClipLookup = false)
    {
        int tally = width * height;
        for (int idx = 0; idx < tally; ++idx)
            Paletted(dst, idx * Rgba, swatch, src[idx], isClipLookup);
    }

    public static void PopulateR5G6B5(byte[] src, Span<byte> dst, int width, int height)
    {
        int tally = width * height;
        for (int idx = 0; idx < tally; ++idx)
        {
            ushort v = (ushort)(src[idx * 2] | (src[idx * 2 + 1] << 8));
            Place(dst, idx * Rgba,
                (byte)(((v & 0xF800) >> 11) << 3),
                (byte)(((v & 0x7E0) >> 5) << 2),
                (byte)((v & 0x1F) << 3),
                255);
        }
    }

    public static void PopulateA4R4G4B4(byte[] src, Span<byte> dst, int width, int height)
    {
        int tally = width * height;
        for (int idx = 0; idx < tally; ++idx)
        {
            ushort v = (ushort)(src[idx * 2] | (src[idx * 2 + 1] << 8));
            Place(dst, idx * Rgba,
                (byte)(((v >> 8) & 0x0F) * 17),
                (byte)(((v >> 4) & 0x0F) * 17),
                (byte)((v & 0x0F) * 17),
                (byte)(((v >> 12) & 0x0F) * 17));
        }
    }

    public static void PopulateA8R8G8B8(byte[] src, Span<byte> dst, int width, int height)
    {
        int tally = width * height;
        for (int idx = 0; idx < tally; ++idx)
        {
            int s = idx * 4;
            Place(dst, idx * Rgba, src[s + 2], src[s + 1], src[s], src[s + 3]);
        }
    }

    public static void PopulateR8G8B8(byte[] src, Span<byte> dst, int width, int height)
    {
        int tally = width * height;
        for (int idx = 0; idx < tally; ++idx)
        {
            int s = idx * 3;
            Place(dst, idx * Rgba, src[s + 2], src[s + 1], src[s], 255);
        }
    }

    public static void PopulateA8(byte[] src, Span<byte> dst, int width, int height)
    {
        int tally = width * height;
        for (int idx = 0; idx < tally; ++idx)
            Place(dst, idx * Rgba, 255, 255, 255, src[idx]);
    }

    public static void PopulateA8Additive(byte[] src, Span<byte> dst, int width, int height)
    {
        int tally = width * height;
        for (int idx = 0; idx < tally; ++idx)
        {
            byte v = src[idx];
            Place(dst, idx * Rgba, v, v, v, v);
        }
    }

    public static bool IsCompressedFmt(PixelLayout fmt)
    {
        return fmt is PixelLayout.PFID_DXT1 or PixelLayout.PFID_DXT3 or PixelLayout.PFID_DXT5;
    }

    public static byte[] Color565ToRgba(ushort color565)
    {
        int r = (color565 >> 11) & 31;
        int g = (color565 >> 5) & 63;
        int b = color565 & 31;
        return [(byte)(r * 255 / 31), (byte)(g * 255 / 63), (byte)(b * 255 / 31), 255];
    }

    public static int FetchCompressedStratumDims(int width, int height, TexelLayout fmt)
    {
        int chunksWide = Math.Max(1, (width + 3) / 4);
        int chunksHi = Math.Max(1, (height + 3) / 4);
        return chunksWide * chunksHi * (fmt == TexelLayout.DXT1 ? 8 : 16);
    }

    private static void Paletted(Span<byte> dst, int at, ColorTable swatch, int ordinal, bool clipLookup)
    {
        if (clipLookup && ordinal < ClipLookupReservedSwatch)
        {
            Place(dst, at, 0, 0, 0, 0);
            return;
        }
        Argb aRGB = swatch.Colors[ordinal];
        Place(dst, at, aRGB.Red, aRGB.Green, aRGB.Blue, aRGB.Alpha);
    }

    private static void Place(Span<byte> dst, int at, byte r, byte g, byte b, byte a)
    {
        dst[at] = r;
        dst[at + 1] = g;
        dst[at + 2] = b;
        dst[at + 3] = a;
    }
}
