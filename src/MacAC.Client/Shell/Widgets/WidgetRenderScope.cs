using MacAC.Dat;
using System.Numerics;
using MacAC.Client.Graphics;

namespace MacAC.Client.Shell;

internal readonly record struct WidgetClipRect(float Left, float Top, float Right, float Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public static WidgetClipRect Intersect(WidgetClipRect rect, WidgetClipRect b)
    {
        return new(
                MathF.Max(rect.Left, b.Left),
                MathF.Max(rect.Top, b.Top),
                MathF.Min(rect.Right, b.Right),
                MathF.Min(rect.Bottom, b.Bottom));
    }

    public static bool TryClipSprite(
        WidgetClipRect clip,
        ref float x, ref float y, ref float w, ref float h,
        ref float u0, ref float v0, ref float u1, ref float v1)
    {
        return ClientQuadClipper.TryClip(
                clip.Left, clip.Top, clip.Right, clip.Bottom,
                ref x, ref y, ref w, ref h,
                ref u0, ref v0, ref u1, ref v1);
    }
}

public sealed class WidgetRenderScope(PhrasePainter renderer, Vector2 monitorDims, BitmapFont? defaultTypeface = null)
{
    public PhrasePainter TextRenderer { get; } = renderer;
    public BitmapFont? DefaultFont { get; set; } = defaultTypeface;
    public Vector2 MonitorDims { get; } = monitorDims;

    private readonly System.Collections.Generic.List<Vector2> _pile = [];
    private Vector2 _latest;
    private readonly System.Collections.Generic.List<WidgetClipRect?> _clipPile = [];
    private WidgetClipRect? _clip;

    private readonly System.Collections.Generic.List<float> _alphaPile = [];

    public float AlphaMod { get; private set; } = 1f;

    public void PushAlpha(float a) { _alphaPile.Add(AlphaMod); AlphaMod *= a; }

    public void PushAlphaAbsolute(float a) { _alphaPile.Add(AlphaMod); AlphaMod = a; }

    public void TakeAlpha()
    {
        if (_alphaPile.Count is 0) return;
        AlphaMod = _alphaPile[^1];
        _alphaPile.RemoveAt(_alphaPile.Count - 1);
    }

    public void PushXform(float dx, float dy)
    {
        _pile.Add(_latest);
        _latest += new Vector2(dx, dy);
    }

    public void TakeXform()
    {
        if (_pile.Count is 0) return;
        _latest = _pile[^1];
        _pile.RemoveAt(_pile.Count - 1);
    }

    public Vector2 LatestOrigin => _latest;

    public void PushClip(float x, float y, float w, float h)
    {
        _clipPile.Add(_clip);
        WidgetClipRect upcoming = new WidgetClipRect(
            _latest.X + x,
            _latest.Y + y,
            _latest.X + x + MathF.Max(0f, w),
            _latest.Y + y + MathF.Max(0f, h));
        _clip = _clip is { } latest
            ? WidgetClipRect.Intersect(latest, upcoming)
            : upcoming;
    }

    public void TakeClip()
    {
        if (_clipPile.Count is 0) return;
        _clip = _clipPile[^1];
        _clipPile.RemoveAt(_clipPile.Count - 1);
    }

    internal int ClipPileZDepth => _clipPile.Count;

    public bool LatestClipIsVacant => _clip is { } rect && rect.IsEmpty;

    public void PushClipUnbounded()
    {
        _clipPile.Add(_clip);
        _clip = new WidgetClipRect(0f, 0f, MonitorDims.X, MonitorDims.Y);
    }

    public void CommenceTopLayerStratum() => TextRenderer.TopLayerManner = true;
    public void FinishTopLayerStratum() => TextRenderer.TopLayerManner = false;

    public void SketchRect(float x, float y, float w, float h, Vector4 tint) => SketchPopulate(x, y, w, h, tint);

    public void SketchPopulate(float x, float y, float w, float h, Vector4 tint)
    {
        x += _latest.X;
        y += _latest.Y;
        if (!ClipRect(ref x, ref y, ref w, ref h)) return;
        TextRenderer.PaintPopulate(x, y, w, h, ImposeAlpha(tint));
    }

    public void SketchRectOutline(float x, float y, float w, float h, Vector4 tint, float thickness = 1f)
    {
        if (thickness <= 0f || w <= 0f || h <= 0f) return;
        float t = MathF.Min(thickness, MathF.Min(w, h) * 0.5f);
        SketchRect(x, y, w, t, tint);
        SketchRect(x, y + h - t, w, t, tint);
        SketchRect(x, y + t, t, h - 2f * t, tint);
        SketchRect(x + w - t, y + t, t, h - 2f * t, tint);
    }

    public void SketchSprite(uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint)
    {
        x += _latest.X;
        y += _latest.Y;
        PaintSpriteAbsolute(texture, x, y, w, h, u0, v0, u1, v1, tint, enactAlpha: true);
    }

    public void SketchString(string phrase, float x, float y, Vector4 tint, BitmapFont? typeface = null)
    {
        BitmapFont? f = typeface ?? DefaultFont;
        if (f is null) return;
        float monitorX = _latest.X + x;
        float monitorY = _latest.Y + y;
        Vector4 alphaTint = ImposeAlpha(tint);
        if (_clip is { } clip)
        {
            TextRenderer.PaintStringClipped(
                f, phrase, monitorX, monitorY, alphaTint,
                clip.Left, clip.Top, clip.Right, clip.Bottom);
            return;
        }
        TextRenderer.PaintString(f, phrase, monitorX, monitorY, alphaTint);
    }

    public void PaintStringDat(
        WidgetDatFont typeface, string phrase, float x, float y, Vector4 tint,
        bool outline = false, Vector4? outlineTint = null)
    {
        if (typeface is null || string.IsNullOrEmpty(phrase)) return;

        if (outline)
        {
            PaintStringDatPass(typeface, phrase, x, y, outlineTint ?? DefaultOutlineTint, isOutlinePass: true);
        }

        // PASS 1 (or the only pass, when outline is off) - fill, whole string
        PaintStringDatPass(typeface, phrase, x, y, tint, isOutlinePass: false);
    }

    public void PaintStringDatPass(
        WidgetDatFont typeface, string phrase, float x, float y, Vector4 tint, bool isOutlinePass)
    {
        if (typeface is null || string.IsNullOrEmpty(phrase)) return;

        float originX = _latest.X + x;
        float originY = _latest.Y + y;

        float baseY = System.MathF.Floor(originY + 0.5f);

        float pen = originX;
        for (int idx = 0; idx < phrase.Length; ++idx)
        {
            if (!typeface.TryGetGlyph(phrase[idx], out GlyphDesc desc))
                continue;

            // Horizontal: snap each glyph's dest X to a whole pixel (the pen keeps its true fractional
            // advance).
            float gx = System.MathF.Floor(pen + desc.HorizontalOffsetBefore + 0.5f);
            float gy = baseY + desc.VerticalOffsetBefore;
            float gw = desc.Width;
            float gh = desc.Height;

            if (gw > 0f && gh > 0f)
            {
                if (isOutlinePass)
                    PaintOutlineGlyph(typeface, desc, gx, gy, gw, gh, tint);
                else
                    PaintPopulateGlyph(typeface, desc, gx, gy, gw, gh, tint);
            }

            pen += WidgetDatFont.GlyphProceed(desc);
        }
    }

    private void PaintSpriteAbsolute(
        uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint, bool enactAlpha)
    {
        if (_clip is { } clip
            && !WidgetClipRect.TryClipSprite(
                clip, ref x, ref y, ref w, ref h, ref u0, ref v0, ref u1, ref v1))
            return;
        TextRenderer.PaintSprite(
            texture, x, y, w, h, u0, v0, u1, v1,
            enactAlpha ? ImposeAlpha(tint) : tint);
    }

    public static readonly Vector4 DefaultOutlineTint = new(0f, 0f, 0f, 1f);

    public static readonly Vector4 VaultSoleLegendTint = new(0.5f, 0.5f, 0.5f, 1f);

    private bool ClipRect(ref float x, ref float y, ref float w, ref float h)
    {
        if (_clip is not { } clip)
            return w > 0f && h > 0f;
        float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
        return WidgetClipRect.TryClipSprite(
            clip, ref x, ref y, ref w, ref h, ref u0, ref v0, ref u1, ref v1);
    }

    private Vector4 ImposeAlpha(Vector4 c) => AlphaMod >= 1f ? c : new Vector4(c.X, c.Y, c.Z, c.W * AlphaMod);

    private void PaintPopulateGlyph(
        WidgetDatFont typeface, GlyphDesc desc,
        float gx, float gy, float gw, float gh, Vector4 tint)
    {
        var (fu0, fv0, fu1, fv1) = TilesetUv(
            desc.OffsetX, desc.OffsetY, desc.Width, desc.Height,
            typeface.ForegroundWidth, typeface.ForegroundHeight);
        PaintSpriteAbsolute(typeface.ForegroundTexture, gx, gy, gw, gh, fu0, fv0, fu1, fv1, tint, enactAlpha: true);
    }

    private void PaintOutlineGlyph(
        WidgetDatFont typeface, GlyphDesc desc,
        float gx, float gy, float gw, float gh, Vector4 tint)
    {
        if (typeface.BackgroundTexture is not 0)
        {
            int bx = typeface.BorderX, by = typeface.BorderY;
            float ix = gx - bx;
            float iy = gy - by;
            float iw = gw + 2f * bx;
            float ih = gh + 2f * by;

            float srcX = desc.OffsetX - bx, srcY = desc.OffsetY - by;
            float srcW = iw, srcH = ih;
            float destX = ix, destY = iy, destW = iw, destH = ih;
            if (srcX < 0f) { destX -= srcX; destW += srcX; srcW += srcX; srcX = 0f; }
            if (srcY < 0f) { destY -= srcY; destH += srcY; srcH += srcY; srcY = 0f; }
            if (srcX + srcW > typeface.BackgroundWidth)
            {
                float over = srcX + srcW - typeface.BackgroundWidth;
                srcW -= over; destW -= over;
            }
            if (srcY + srcH > typeface.BackgroundHeight)
            {
                float over = srcY + srcH - typeface.BackgroundHeight;
                srcH -= over; destH -= over;
            }
            if (srcW <= 0f || srcH <= 0f) return;

            var (bu0, bv0, bu1, bv1) = TilesetUv(
                (int)srcX, (int)srcY, (int)srcW, (int)srcH,
                typeface.BackgroundWidth, typeface.BackgroundHeight);
            PaintSpriteAbsolute(typeface.BackgroundTexture, destX, destY, destW, destH, bu0, bv0, bu1, bv1, tint, enactAlpha: true);
        }
        else
        {
            var (fu0, fv0, fu1, fv1) = TilesetUv(
                desc.OffsetX, desc.OffsetY, desc.Width, desc.Height,
                typeface.ForegroundWidth, typeface.ForegroundHeight);
            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    if (dx is 0 && dy is 0) continue;
                    PaintSpriteAbsolute(
                        typeface.ForegroundTexture, gx + dx, gy + dy, gw, gh,
                        fu0, fv0, fu1, fv1, tint, enactAlpha: true);
                }
            }
        }
    }

    private static (float u0, float v0, float u1, float v1) TilesetUv(
        int shiftX, int shiftY, int width, int height, int tilesetW, int tilesetH)
    {
        if (tilesetW <= 0 || tilesetH <= 0) return (0f, 0f, 0f, 0f);
        float u0 = shiftX / (float)tilesetW;
        float v0 = shiftY / (float)tilesetH;
        float u1 = (shiftX + width) / (float)tilesetW;
        float v1 = (shiftY + height) / (float)tilesetH;
        return (u0, v0, u1, v1);
    }
}
