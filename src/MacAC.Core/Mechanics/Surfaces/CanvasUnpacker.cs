using System.Collections.Concurrent;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using MacAC.Dat;
using MacAC.Mechanics.Drawing.Batches;
using StbImageSharp;

namespace MacAC.Mechanics.Surfaces;

public static class CanvasUnpacker
{
    private const int Rgba = 4;

    private static readonly BcDecoder ChunkDecoder = new();

    private static readonly ConcurrentDictionary<uint, byte> ReportedPlaceholders = new();

    public static UnpackedTexture DecodeRenderSurface(Bitmap surface)
    {
        return DecodeRenderSurface(surface, swatch: null, isClipLookup: false, isAdditive: false);
    }

    public static UnpackedTexture DecodeRenderSurface(
        Bitmap surface,
        ColorTable? swatch,
        bool isClipLookup = false,
        bool isAdditive = false)
    {
        if (surface.Pixels is null)
            return Placeholder(surface, "null SourceData");

        if (surface.Layout == PixelLayout.PFID_CUSTOM_RAW_JPEG)
        {
            try
            {
                return FromJpeg(surface);
            }
            catch (Exception problem)
            {
                return Placeholder(surface, $"JPEG decode failed: {problem.Message}");
            }
        }

        if (surface.Width <= 0 || surface.Height <= 0)
            return Placeholder(surface, "non-positive Width/Height");

        try
        {
            return surface.Layout switch
            {
                PixelLayout.PFID_R8G8B8 => Packed(surface, 3, TextureTools.PopulateR8G8B8),
                PixelLayout.PFID_A8R8G8B8 => Packed(surface, 4, TextureTools.PopulateA8R8G8B8),
                PixelLayout.PFID_X8R8G8B8 => FromX8R8G8B8(surface),
                PixelLayout.PFID_R5G6B5 => Packed(surface, 2, TextureTools.PopulateR5G6B5),
                PixelLayout.PFID_A4R4G4B4 => Packed(surface, 2, TextureTools.PopulateA4R4G4B4),
                PixelLayout.PFID_DXT1 => FromChunks(surface, CompressionFormat.Bc1, isClipLookup),
                PixelLayout.PFID_DXT3 => FromChunks(surface, CompressionFormat.Bc2, isClipLookup),
                PixelLayout.PFID_DXT5 => FromChunks(surface, CompressionFormat.Bc3, isClipLookup),
                PixelLayout.PFID_A8 or PixelLayout.PFID_CUSTOM_LSCAPE_ALPHA => FromAlpha(surface, isAdditive),
                PixelLayout.PFID_P8 when swatch is null => Placeholder(surface, "PFID_P8 with no palette"),
                PixelLayout.PFID_P8 => Indexed(surface, swatch, 1, isClipLookup, TextureTools.PopulateP8),
                PixelLayout.PFID_INDEX16 when swatch is null => Placeholder(surface, "PFID_INDEX16 with no palette"),
                PixelLayout.PFID_INDEX16 => Indexed(surface, swatch, 2, isClipLookup, TextureTools.PopulateIndex16),
                _ => Placeholder(surface, $"unsupported PixelFormat {surface.Layout}"),
            };
        }
        catch (Exception problem)
        {
            return Placeholder(surface, $"decode threw: {problem.Message}");
        }
    }

    public static UnpackedTexture UnpackSolidTint(Argb tint, float seeThrough)
    {
        if (tint is null)
            return UnpackedTexture.Magenta;
        float density = Math.Clamp(1f - seeThrough, 0f, 1f);
        byte alpha = (byte)Math.Clamp(tint.Alpha * density, 0f, 255f);
        return new UnpackedTexture([tint.Red, tint.Green, tint.Blue, alpha], 1, 1);
    }

    /// <summary>Scales every alpha byte in place by (1 - translucency).</summary>
    public static UnpackedTexture ImposeAuthoredSeeThrough(UnpackedTexture texture, float seeThrough)
    {
        if (seeThrough <= 0f)
            return texture;
        float keep = Math.Clamp(1f - seeThrough, 0f, 1f);
        byte[] px = texture.Rgba8;
        for (int alpha = 3; alpha < px.Length; alpha += Rgba)
            px[alpha] = (byte)(px[alpha] * keep);
        return texture;
    }

    private delegate void PackedFill(byte[] source, Span<byte> rgba, int width, int height);

    private delegate void IndexedFill(
        byte[] source, ColorTable palette, Span<byte> rgba, int width, int height, bool clip);

    private static UnpackedTexture Packed(Bitmap surface, int octetsPerPixel, PackedFill populate)
    {
        int px = surface.Width * surface.Height;
        if (surface.Pixels.Length < px * octetsPerPixel)
            return UnpackedTexture.Magenta;
        byte[] rgba = new byte[px * Rgba];
        populate(surface.Pixels, rgba, surface.Width, surface.Height);
        return new UnpackedTexture(rgba, surface.Width, surface.Height);
    }

    private static UnpackedTexture Indexed(
        Bitmap surface,
        ColorTable swatch,
        int octetsPerPixel,
        bool isClipLookup,
        IndexedFill populate)
    {
        int px = surface.Width * surface.Height;
        if (surface.Pixels.Length < px * octetsPerPixel || swatch.Colors.Count is 0)
            return UnpackedTexture.Magenta;
        byte[] rgba = new byte[px * Rgba];
        populate(surface.Pixels, swatch, rgba, surface.Width, surface.Height, isClipLookup);
        return new UnpackedTexture(rgba, surface.Width, surface.Height);
    }

    // One byte per pixel
    private static UnpackedTexture FromAlpha(Bitmap surface, bool isAdditive)
    {
        return Packed(surface, 1, isAdditive ? TextureTools.PopulateA8Additive : TextureTools.PopulateA8);
    }

    private static UnpackedTexture FromX8R8G8B8(Bitmap surface)
    {
        int px = surface.Width * surface.Height;
        if (surface.Pixels.Length < px * Rgba)
            return UnpackedTexture.Magenta;

        byte[] rgba = new byte[px * Rgba];
        byte[] src = surface.Pixels;
        // On disk each pixel is B, G, R, X (little-endian; X is padding)
        for (int at = 0; at < rgba.Length; at += Rgba)
        {
            FromX8R8G8B8Loop(rgba, at, src);
        }
        return new UnpackedTexture(rgba, surface.Width, surface.Height);
    }

    private static void FromX8R8G8B8Loop(byte[] rgba, int at, byte[] src)
    {
        rgba[at] = src[at + 2];
        rgba[at + 1] = src[at + 1];
        rgba[at + 2] = src[at];
        rgba[at + 3] = 0xFF;
    }

    private static UnpackedTexture FromChunks(Bitmap surface, CompressionFormat fmt, bool isClipLookup)
    {
        ColorRgba32[] decoded = ChunkDecoder.DecodeRaw(surface.Pixels, surface.Width, surface.Height, fmt);
        byte[] rgba = new byte[surface.Width * surface.Height * Rgba];
        for (int idx = 0; idx < decoded.Length; ++idx)
        {
            ColorRgba32 rgba32 = decoded[idx];
            int at = idx * Rgba;
            rgba[at] = rgba32.r;
            rgba[at + 1] = rgba32.g;
            rgba[at + 2] = rgba32.b;
            rgba[at + 3] = isClipLookup && (rgba32.r | rgba32.g | rgba32.b) is 0 ? (byte)0 : rgba32.a;
        }
        return new UnpackedTexture(rgba, surface.Width, surface.Height);
    }

    private static UnpackedTexture FromJpeg(Bitmap surface)
    {
        ImageResult image = ImageResult.FromMemory(surface.Pixels!, ColorComponents.RedGreenBlueAlpha);
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidDataException(
                $"JPEG surface 0x{surface.Id:X8} decoded to {image.Width}x{image.Height}.");
        }
        return new UnpackedTexture(image.Data, image.Width, image.Height);
    }

    private static UnpackedTexture Placeholder(Bitmap surface, string cause)
    {
        if (ReportedPlaceholders.TryAdd(surface.Id, 0))
        {
            Console.WriteLine(
                $"[UI] CanvasUnpacker: RenderSurface 0x{surface.Id:X8} decoded to the 1x1 "
                + $"magenta placeholder ({cause}; format={surface.Layout} "
                + $"{surface.Width}x{surface.Height}).");
        }
        return UnpackedTexture.Magenta;
    }
}
