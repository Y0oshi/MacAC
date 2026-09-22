using MacAC.Client.Graphics.Gpu;
using StbTrueTypeSharp;

namespace MacAC.Client.Graphics;

public sealed unsafe class BitmapFont : IDisposable
{
    public readonly struct Sigil(float umn, float vmn, float umx, float vmx,
                 float ox, float oy, float w, float h, float adv)
    {
        public readonly float UvLowerX = umn;
        public readonly float UvLowerY = vmn;
        public readonly float UvUpperX = umx;
        public readonly float UvUpperY = vmx;
        public readonly float ShiftX = ox;
        public readonly float ShiftY = oy;
        public readonly float Width = w;     // pixels
        public readonly float Height = h;
        public readonly float Advance = adv;
    }

    private readonly Sigil[] _glyphs;
    private readonly int _leadChar;
    private readonly int _countChars;
    private readonly IGpuBitmap _texture;

    public uint TextureId { get; }
    public float PixelHeight { get; }
    public float LineHeight { get; }
    public float Ascent { get; }
    public int TilesetWidth { get; }
    public int TilesetHeight { get; }

    internal BitmapFont(IClientGpuDevice dev, byte[] ttfOctets, float pixelHeight,
        int tilesetDims = 512, int leadChar = 32, int countChars = 96)
    {
        ArgumentNullException.ThrowIfNull(dev);
        PixelHeight = pixelHeight;
        TilesetWidth = tilesetDims;
        TilesetHeight = tilesetDims;
        _leadChar = leadChar;
        _countChars = countChars;

        var bakedChars = new StbTrueType.stbtt_bakedchar[countChars];
        byte[] px = new byte[TilesetWidth * TilesetHeight];
        bool ok = StbTrueType.stbtt_BakeFontBitmap(
            ttfOctets, 0, pixelHeight,
            px, TilesetWidth, TilesetHeight,
            leadChar, countChars, bakedChars);
        if (!ok)
            throw new InvalidOperationException(
                $"stbtt_BakeFontBitmap failed: atlas {tilesetDims}x{tilesetDims} " +
                $"too small for pixelHeight={pixelHeight}");

        using var details = StbTrueType.CreateFont(ttfOctets, 0)
            ?? throw new InvalidOperationException("stbtt_InitFont failed");
        float scaling = StbTrueType.stbtt_ScaleForPixelHeight(details, pixelHeight);
        int ascent, descent, strokeGap;
        StbTrueType.stbtt_GetFontVMetrics(details, &ascent, &descent, &strokeGap);
        Ascent = ascent * scaling;
        LineHeight = (ascent - descent + strokeGap) * scaling;

        _glyphs = new Sigil[countChars];
        for (int idx = 0; idx < countChars; ++idx)
        {
            var bakedchar = bakedChars[idx];
            float w = bakedchar.x1 - bakedchar.x0;
            float h = bakedchar.y1 - bakedchar.y0;
            _glyphs[idx] = new Sigil(
                umn: bakedchar.x0 / (float)TilesetWidth,
                vmn: bakedchar.y0 / (float)TilesetHeight,
                umx: bakedchar.x1 / (float)TilesetWidth,
                vmx: bakedchar.y1 / (float)TilesetHeight,
                ox: bakedchar.xoff,
                oy: bakedchar.yoff,
                w: w, h: h,
                adv: bakedchar.xadvance);
        }

        IGpuBitmap texture = dev.BuildTexture(new GpuBitmapSpec(
            "bitmap-font-atlas",
            GpuBitmapFlavor.Texture2D,
            GpuBitmapFmt.R8Unorm,
            Width: TilesetWidth,
            Height: TilesetHeight,
            LayerCount: 1,
            MipLevelCount: 1));
        GpuTextureSlot socket;
        try
        {
            fixed (byte* pointer = px)
                texture.Upload(0, 0, new ReadOnlySpan<byte>(pointer, TilesetWidth * TilesetHeight));

            IClientGpuSampler sampler = dev.BuildSampler(GpuSamplerSpec.RealmClamp);
            socket = dev.EnrollTexture(texture, sampler);
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        _texture = texture;
        TextureId = WidgetTextureChartHandle.FromSocket(socket);
    }

    public bool TryFetchGlyph(char c, out Sigil glyph)
    {
        int index = c - _leadChar;
        if ((uint)index >= (uint)_countChars)
        {
            glyph = default;
            return false;
        }
        glyph = _glyphs[index];
        return true;
    }

    public float MeasureWidth(string s)
    {
        float w = 0;
        for (int idx = 0; idx < s.Length; ++idx)
        {
            if (TryFetchGlyph(s[idx], out var glyph))
                w += glyph.Advance;
        }
        return w;
    }

    public void Dispose() => _texture.Dispose();

    public static byte[]? TryPullSysMonospaceTypeface()
    {
        string[] contenders =
        [
            @"C:\Windows\Fonts\consola.ttf",
            @"C:\Windows\Fonts\cour.ttf",
            @"C:\Windows\Fonts\arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
            "/usr/share/fonts/TTF/DejaVuSansMono.ttf",
            "/System/Library/Fonts/SFNSMono.ttf",
            "/System/Library/Fonts/Monaco.ttf",
        ];
        foreach (var trail in contenders)
        {
            try
            {
                if (!File.Exists(trail))
                    continue;

                byte[] octets = File.ReadAllBytes(trail);
                using StbTrueType.stbtt_fontinfo? typeface =
                    StbTrueType.CreateFont(octets, 0);
                if (typeface is not null)
                    return octets;
            }
            catch
            {
            }
        }
        return null;
    }
}
