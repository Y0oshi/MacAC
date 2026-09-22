using BCnEncoder.Shared;
using BCnEncoder.ImageSharp;
using MacAC.Dat;
using MacAC.Mechanics.Drawing.Batches;
using MacAC.Mechanics.Geometry;
using PixelFormat =  MacAC.Dat.PixelLayout;

namespace MacAC.Assets;

public sealed partial class MeshHarvester
{
    private enum Lane
    {
        // Missing dependencies throw; clip-map alpha only on decoded DXT; translucency scales alpha
        GfxObj,

        // Missing dependencies drop the slot; clip-map alpha on every decoded texture; no translucency
        // scaling
        CellShell,
    }

    // Everything a batch needs to know about one resolved surface
    private sealed class SurfacePixels
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required TexelLayout Format { get; init; }
        public required byte[] Data { get; init; }
        public PushPixelFmt? PixelFormat { get; init; }
        public uint PaletteId { get; init; }
        public bool Textured { get; init; }
        public bool Solid { get; init; }
        public bool SeeThru { get; init; }
        public bool Additive { get; init; }

        public (int Width, int Height, TexelLayout Format) Shape => (Width, Height, Format);

        public TextureHarvestBatch NewLot(Skin canvas, BitmapTag tag)
        {
            return new()
            {
                Key = tag,
                TextureData = Data,
                UploadPixelFormat = PixelFormat,
                UploadPixelType = null,
                Translucency = SeeThroughKindExtensions.FromCanvasKind(canvas.Bits),
                SurfaceOpacity = SeeThroughKindExtensions.DensityFromCanvasSeeThrough(canvas.Bits, canvas.Translucency),
                MaterialState = CanonSurfaceMaterialState.Resolve(canvas.Bits, Textured, PaletteId is not 0),
                IsTransparent = SeeThru,
                IsAdditive = Additive,
            };
        }
    }

    // One shared RGBA fill per ARGB colour; callers must never mutate the result
    internal byte[] SolidPx(Argb tint, int width, int height)
    {
        uint argb = ((uint)tint.Alpha << 24) | ((uint)tint.Red << 16) | ((uint)tint.Green << 8) | tint.Blue;
        return _solids.GetOrAdd(argb, _ => TextureTools.BuildSolidTintTexture(tint, width, height));
    }

    private static bool Has(SkinBits kind, SkinBits bit) => (kind & bit) == bit;

    private SurfacePixels? LocateCanvas(Skin canvas, uint canvasIdent, Lane lane)
    {
        bool solid = CanonBareSurfacePolicy.IsUntextured(canvas.Bits);
        if (solid)
        {
            // Bare colour: a shared 32×32 fill. The GfxObj lane never
            // classified these as transparent; the shell lane goes by alpha.
            return new SurfacePixels
            {
                Width = 32,
                Height = 32,
                Format = TexelLayout.RGBA8,
                Data = SolidPx(canvas.Color, 32, 32),
                PixelFormat = PushPixelFmt.Rgba,
                Solid = true,
                SeeThru = lane == Lane.CellShell && canvas.Color.Alpha < 255,
            };
        }

        if (!_datFiles.Portal.TryGet<SkinTexture>(canvas.TextureId, out var canvasTexture))
        {
            Console.WriteLine(lane == Lane.GfxObj
                ? $"[tex-skip] gfxobj SurfaceTexture 0x{canvas.TextureId:X8} miss -> poly batch dropped (surface 0x{canvasIdent:X8})"
                : $"[tex-skip] cellstruct SurfaceTexture 0x{canvas.TextureId:X8} miss -> WALL poly batch dropped (surface 0x{canvasIdent:X8})");
            return null;
        }

        uint rasterizeCanvasIdent = canvasTexture.BitmapIds.First();
        if (!_datFiles.Portal.TryGet<Bitmap>(rasterizeCanvasIdent, out var rs) && !_datFiles.HighRes.TryGet<Bitmap>(rasterizeCanvasIdent, out rs))
        {
            if (lane == Lane.GfxObj)
                throw new Exception($"Unable to load RenderSurface: 0x{rasterizeCanvasIdent:X8}");
            Console.WriteLine($"[tex-skip] cellstruct RenderSurface 0x{rasterizeCanvasIdent:X8} miss (portal+highres) -> WALL poly batch dropped");
            return null;
        }

        int width = rs.Width;
        int height = rs.Height;
        bool clip = Has(canvas.Bits, SkinBits.Base1ClipMap);
        bool additive = Has(canvas.Bits, SkinBits.Additive);
        bool compressed = TextureTools.IsCompressedFmt(rs.Layout);
        bool dxt35 = rs.Layout is PixelFormat.PFID_DXT3 or PixelFormat.PFID_DXT5;

        byte[] blob;
        TexelLayout format;
        PushPixelFmt? push = null;
        bool shared = false; // true while `data` is the shelf's own array and must be cloned before mutation

        if (compressed && !clip && canvas.Translucency <= 0.0f)
        {
            // Block-compressed and untouched: upload the DAT bytes as-is.
            blob = rs.Pixels;
            format = rs.Layout switch
            {
                PixelFormat.PFID_DXT1 => TexelLayout.DXT1,
                PixelFormat.PFID_DXT3 => TexelLayout.DXT3,
                PixelFormat.PFID_DXT5 => TexelLayout.DXT5,
                _ => throw new ArgumentOutOfRangeException(nameof(format), rs.Layout, "The source isn't a supported block-compressed texture"),
            };
        }
        else
        {
            format = TexelLayout.RGBA8;
            push = PushPixelFmt.Rgba;
            var tag = PixelTagFor(rasterizeCanvasIdent, rs.Layout, clip, additive);

            if (compressed)
            {
                Bitmap src = rs;
                blob = _px.FetchOrBuild(tag, () => UnpackChunks(src, width, height), out shared);
            }
            else if (_px.TryGet(tag, out byte[] pinned))
            {
                blob = pinned;
                shared = true;
            }
            else
            {
                byte[]? unpacked = UnpackPx(rs, width, height, clip, additive, lane);
                if (unpacked is null)
                    return null;
                blob = _px.KeepOrUse(tag, unpacked, out shared);
            }

            if (clip && (lane == Lane.CellShell || compressed))
            {
                if (shared)
                {
                    blob = (byte[])blob.Clone();
                    shared = false;
                }
                PunchBlackToWipe(blob);
            }
        }

        if (lane == Lane.GfxObj && canvas.Translucency > 0.0f)
        {
            if (shared)
                blob = (byte[])blob.Clone();
            float keep = 1.0f - canvas.Translucency;
            for (int idx = 3; idx < blob.Length; idx += 4)
                blob[idx] = (byte)(blob[idx] * keep);
        }

        bool seeThru =
            Has(canvas.Bits, SkinBits.Translucent)
            || clip
            || ((uint)canvas.Bits & 0x100) is not 0 // Alpha
            || ((uint)canvas.Bits & 0x200) is not 0 // InvAlpha
            || additive
            || (canvas.Translucency > 0.0f && canvas.Translucency < 1.0f)
            || format == TexelLayout.A8
            || format == TexelLayout.Rgba32f
            || dxt35
            || rs.Layout is PixelFormat.PFID_A8R8G8B8 or PixelFormat.PFID_A4R4G4B4 or PixelFormat.PFID_DXT3 or PixelFormat.PFID_DXT5;

        return new SurfacePixels
        {
            Width = width,
            Height = height,
            Format = format,
            Data = blob,
            PixelFormat = push,
            PaletteId = rs.DefaultColorTableId,
            Textured = true,
            SeeThru = seeThru,
            Additive = additive,
        };
    }

    // Clip maps key out pure black: any such texel becomes fully transparent
    private static void PunchBlackToWipe(byte[] rgba)
    {
        for (int idx = 0; idx < rgba.Length; idx += 4)
        {
            if (rgba[idx] is 0 && rgba[idx + 1] is 0 && rgba[idx + 2] is 0)
                rgba[idx + 3] = 0;
        }
    }

    // Expands an uncompressed DAT pixel format to RGBA8
    private byte[]? UnpackPx(Bitmap surface, int width, int height, bool clip, bool additive, Lane lane)
    {
        byte[] rgba = new byte[width * height * 4];
        switch (surface.Layout)
        {
            case PixelFormat.PFID_A8R8G8B8:
                TextureTools.PopulateA8R8G8B8(surface.Pixels, rgba.AsSpan(), width, height);
                break;
            case PixelFormat.PFID_R8G8B8:
                TextureTools.PopulateR8G8B8(surface.Pixels, rgba.AsSpan(), width, height);
                break;
            case PixelFormat.PFID_INDEX16:
                {
                    if (SwatchFor(surface, lane) is not { } swatch)
                        return null;
                    TextureTools.PopulateIndex16(surface.Pixels, swatch, rgba.AsSpan(), width, height, clip);
                    break;
                }
            case PixelFormat.PFID_P8:
                {
                    if (SwatchFor(surface, lane) is not { } swatch)
                        return null;
                    TextureTools.PopulateP8(surface.Pixels, swatch, rgba.AsSpan(), width, height, clip);
                    break;
                }
            case PixelFormat.PFID_R5G6B5:
                TextureTools.PopulateR5G6B5(surface.Pixels, rgba.AsSpan(), width, height);
                break;
            case PixelFormat.PFID_A4R4G4B4:
                TextureTools.PopulateA4R4G4B4(surface.Pixels, rgba.AsSpan(), width, height);
                break;
            case PixelFormat.PFID_A8:
            case PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA:
                if (additive)
                    TextureTools.PopulateA8Additive(surface.Pixels, rgba.AsSpan(), width, height);
                else
                    TextureTools.PopulateA8(surface.Pixels, rgba.AsSpan(), width, height);
                break;
            default:
                if (lane == Lane.GfxObj)
                    throw new NotSupportedException($"Not supported surface format: {surface.Layout}");
                return null;
        }
        return rgba;
    }

    private ColorTable? SwatchFor(Bitmap surface, Lane lane)
    {
        if (_datFiles.Portal.TryGet<ColorTable>(surface.DefaultColorTableId, out var swatch))
            return swatch;
        if (lane == Lane.GfxObj)
            throw new Exception($"Unable to load Palette: 0x{surface.DefaultColorTableId:X8}");
        return null;
    }

    private byte[] UnpackChunks(Bitmap surface, int width, int height)
    {
        CompressionFormat chunks = surface.Layout switch
        {
            PixelFormat.PFID_DXT1 => CompressionFormat.Bc1,
            PixelFormat.PFID_DXT3 => CompressionFormat.Bc2,
            PixelFormat.PFID_DXT5 => CompressionFormat.Bc3,
            _ => throw new NotSupportedException($"Not supported compressed format: {surface.Layout}"),
        };

        byte[] rgba = new byte[width * height * 4];
        using var image = _bc.Value!.DecodeRawToImageRgba32(surface.Pixels, width, height, chunks);
        image.CopyPixelDataTo(rgba);
        return rgba;
    }

    private static TexturePixelKey PixelTagFor(uint rasterizeCanvasIdent, PixelFormat fmt, bool clip, bool additive)
    {
        bool clipMatters = fmt is PixelFormat.PFID_INDEX16 or PixelFormat.PFID_P8;
        bool additiveMatters = fmt is PixelFormat.PFID_A8 or PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA;
        return new TexturePixelKey(rasterizeCanvasIdent, clipMatters && clip, additiveMatters && additive);
    }
}
