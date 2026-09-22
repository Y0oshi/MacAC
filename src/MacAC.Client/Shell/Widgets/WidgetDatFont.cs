using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics;

namespace MacAC.Client.Shell;

public sealed class WidgetDatFont
{
    public const uint DefaultTypefaceIdent = 0x40000000u;

    public uint ForegroundTexture { get; }
    public int ForegroundWidth { get; }
    public int ForegroundHeight { get; }

    public uint BackgroundTexture { get; }
    public int BackgroundWidth { get; }
    public int BackgroundHeight { get; }

    public float LineHeight { get; }

    public float BaselineShift { get; }

    public int BorderX { get; }
    public int BorderY { get; }

    private readonly Dictionary<char, GlyphDesc> _glyphs;

    internal WidgetDatFont(
        uint fgBmp, int fgW, int fgH,
        uint bgBmp, int bgW, int bgH,
        float strokeHeight, float baselineShift,
        Dictionary<char, GlyphDesc> glyphs,
        int borderX = 0, int borderY = 0)
    {
        ForegroundTexture = fgBmp; ForegroundWidth = fgW; ForegroundHeight = fgH;
        BackgroundTexture = bgBmp; BackgroundWidth = bgW; BackgroundHeight = bgH;
        LineHeight = strokeHeight;
        BaselineShift = baselineShift;
        _glyphs = glyphs;
        BorderX = borderX;
        BorderY = borderY;
    }

    public bool HasBackground => BackgroundTexture is not 0;

    public bool TryGetGlyph(char c, out GlyphDesc glyph) => _glyphs.TryGetValue(c, out glyph!);

    public static WidgetDatFont? Load(IDatAccess datFiles, BitmapStash stash, uint typefaceIdent = DefaultTypefaceIdent)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(stash);

        if (!datFiles.TryGet<GlyphSet>(typefaceIdent, out var typeface) || typeface is null)
            return null;

        // Foreground atlas is required; without it there are no glyph pixels
        if (typeface.ForegroundBitmapId is 0)
            return null;

        uint fgBmp = stash.FetchOrPushRasterizeCanvas(typeface.ForegroundBitmapId, out int fgW, out int fgH, closest: true);

        uint bgBmp = 0; int bgW = 0, bgH = 0;
        if (typeface.BackgroundBitmapId is not 0)
            bgBmp = stash.FetchOrPushRasterizeCanvas(typeface.BackgroundBitmapId, out bgW, out bgH, closest: true);

        var glyphs = new Dictionary<char, GlyphDesc>(typeface.Glyphs.Count);
        foreach (GlyphDesc desc in typeface.Glyphs)
            glyphs[(char)desc.Unicode] = desc;

        return new WidgetDatFont(
            fgBmp, fgW, fgH,
            bgBmp, bgW, bgH,
            strokeHeight: typeface.MaxCharHeight,
            baselineShift: typeface.BaselineOffset,
            glyphs,
            borderX: (int)typeface.HorizontalBorderPixels,
            borderY: (int)typeface.VerticalBorderPixels);
    }

    public float MeasureWidth(string phrase)
    {
        if (string.IsNullOrEmpty(phrase)) return 0f;

        float width = 0f;
        for (int ordinal = 0; ordinal < phrase.Length; ++ordinal)
        {
            if (_glyphs.TryGetValue(phrase[ordinal], out GlyphDesc? glyph))
                width += GlyphProceed(glyph);
        }

        return width;
    }

    public static float MeasureWidth(string? phrase, Func<char, GlyphDesc?> consult)
    {
        ArgumentNullException.ThrowIfNull(consult);
        if (string.IsNullOrEmpty(phrase)) return 0f;
        float w = 0f;
        for (int idx = 0; idx < phrase.Length; ++idx)
            if (consult(phrase[idx]) is { } desc)
                w += GlyphProceed(desc);
        return w;
    }

    public static float GlyphProceed(GlyphDesc desc)
        => desc.HorizontalOffsetBefore + desc.Width + desc.HorizontalOffsetAfter;
}
