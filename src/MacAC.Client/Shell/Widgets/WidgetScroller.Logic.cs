using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetScroller
{
    public override bool ReceivesHoverPointerRelocate => true;

    public WidgetScrollable? Model { get; set; }

    public float ScalarLocus { get; private set; }

    public Action<float>? ScalarAltered { get; set; }

    public Func<float?>? ScalarLocusSrc { get; set; }

    public bool Horizontal { get; set; }

    public Action? PullFinished { get; set; }

    public uint ScalarPopulateSprite { get; set; }

    public uint ScalarSpanSprite { get; set; }

    public float ScalarSpanLeft { get; set; }

    public WidgetArrangementRule? ScalarSpanArrangementRule { get; set; }

    public bool ScalarPopulateFromRight { get; set; }

    public string? TooltipText { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    public uint FollowSprite { get; set; }

    public uint ThumbSprite { get; set; }

    public uint ThumbTopSprite { get; set; }

    public uint ThumbBotSprite { get; set; }

    public uint ThumbRolloverSprite { get; set; }

    public uint ThumbPressedSprite { get; set; }

    public uint ThumbTopRolloverSprite { get; set; }

    public uint ThumbTopPressedSprite { get; set; }

    public uint ThumbBotRolloverSprite { get; set; }

    public uint ThumbBotPressedSprite { get; set; }

    public uint UpSprite { get; set; }

    public uint DownSprite { get; set; }

    public uint UpRolloverSprite { get; set; }

    public uint UpPressedSprite { get; set; }

    public uint DownRolloverSprite { get; set; }

    public uint DownPressedSprite { get; set; }

    public override bool ConsumesDatChildren => true;

    public static (float y, float h) ThumbRect(WidgetScrollable scrollable, float followTop, float followLength)
    {
        float h = MathF.Max(LowerThumb, followLength * scrollable.ThumbRatio);
        float travel = followLength - h;
        float y = followTop + travel * scrollable.LocusRatio;
        return (y, h);
    }

    public static (float x, float width) ScalarFillRect(
        float sumWidth, float populate, bool fromRight)
        => ScalarFillRect(0f, sumWidth, populate, fromRight);

    public static (float x, float width) ScalarFillRect(
        float spanLeft, float spanWidth, float populate, bool fromRight)
    {
        float safeWidth = MathF.Max(0f, spanWidth);
        float shownWidth = safeWidth * Math.Clamp(populate, 0f, 1f);
        return (fromRight ? spanLeft + safeWidth - shownWidth : spanLeft, shownWidth);
    }

    public void AssignScalarLocus(float locus)
        => ScalarLocus = Math.Clamp(locus, 0f, 1f);

    private uint EngagedThumbSprite => EngagedThumb(ThumbSprite, ThumbRolloverSprite, ThumbPressedSprite);

    private uint EngagedThumbTopSprite
    {
        get
        {
            return EngagedThumb(ThumbTopSprite, ThumbTopRolloverSprite, ThumbTopPressedSprite);
        }
    }

    private uint EngagedThumbBotSprite
    {
        get
        {
            return EngagedThumb(ThumbBotSprite, ThumbBotRolloverSprite, ThumbBotPressedSprite);
        }
    }

    public override string? FetchHintPhrase()
    {
        return string.IsNullOrWhiteSpace(TooltipText)
            ? base.FetchHintPhrase()
            : TooltipText;
    }

    internal (float left, float width) ScalarSpanRect()
    {
        float configuredLeft = ScalarSpanLeft;
        float configuredWidth = ScalarSpanWidth;
        if (ScalarSpanArrangementRule is { } rule)
        {
            WidgetPixelRect latestAncestor = WidgetPixelRect.FromLocusAndDims(
                0, 0, (int)Width, (int)Height);
            var span = rule.Apply(rule.OriginalDescendant, latestAncestor);
            configuredLeft = span.X0;
            configuredWidth = span.Width;
        }
        float left = Math.Clamp(configuredLeft, 0f, Width);
        float askedWidth = float.IsPositiveInfinity(configuredWidth)
            ? Width - left
            : MathF.Max(0f, configuredWidth);
        float width = Math.Clamp(askedWidth, 0f, Width - left);
        return (left, width);
    }

    private uint EngagedThumb(uint norm, uint rollover, uint pressed)
    {
        return IsDragging && pressed is not 0u
                ? pressed
                : _hoveredThumb && !IsDragging && rollover is not 0u
                    ? rollover
                    : norm;
    }

    private float ScalarThumbWidth(Func<uint, (uint tex, int w, int h)>? locate) =>
        ScalarThumbReach(locate, Width);

    private float ScalarThumbReach(
        Func<uint, (uint tex, int w, int h)>? locate, float axisLen)
    {
        if (locate is not null && ThumbSprite is not 0)
        {
            var (_, width, height) = locate(ThumbSprite);
            int native = Horizontal ? width : height;
            if (native > 0) return MathF.Min(native, axisLen);
        }
        return MathF.Min(16f, axisLen);
    }

    private void AlterScalarLocus(float locus)
    {
        AssignScalarLocus(locus);
        ScalarAltered?.Invoke(ScalarLocus);
    }

    private uint EngagedBeginSprite
    {
        get
        {
            return _pressedBtn == FinishBtn.Decrement
                && _hoveredBtn == FinishBtn.Decrement
                && UpPressedSprite is not 0u
                ? UpPressedSprite
                : _hoveredBtn == FinishBtn.Decrement && UpRolloverSprite is not 0u
                    ? UpRolloverSprite
                    : UpSprite;
        }
    }

    private uint EngagedFinishSprite
    {
        get
        {
            return _pressedBtn == FinishBtn.Increment
                && _hoveredBtn == FinishBtn.Increment
                && DownPressedSprite is not 0u
                ? DownPressedSprite
                : _hoveredBtn == FinishBtn.Increment && DownRolloverSprite is not 0u
                    ? DownRolloverSprite
                    : DownSprite;
        }
    }

    internal uint EngagedBeginSpriteForTest => EngagedBeginSprite;

    internal uint EngagedFinishSpriteForTest => EngagedFinishSprite;

    internal uint EngagedThumbSpriteForTest => EngagedThumbSprite;

    internal uint EngagedThumbTopSpriteForTest => EngagedThumbTopSprite;

    internal uint EngagedThumbBotSpriteForTest => EngagedThumbBotSprite;

    private bool ThumbAt(float x, float y)
    {
        if (x < 0f || x >= Width || y < 0f || y >= Height)
            return false;

        if (ScalarAltered is not null)
        {
            if (Horizontal)
            {
                float thumbWidth = ScalarThumbWidth(SpriteResolve);
                float thumbX = MathF.Max(0f, Width - thumbWidth) * ScalarLocus;
                return x >= thumbX && x <= thumbX + thumbWidth;
            }
            float thumbHeight = ScalarThumbReach(SpriteResolve, Height);
            float thumbY = MathF.Max(0f, Height - thumbHeight) * ScalarLocus;
            return y >= thumbY && y <= thumbY + thumbHeight;
        }

        if (Model is not { } scrollable) return false;

        if (Horizontal)
        {
            float followLeft = AxisReach(DecrementBtnReach, Width);
            float followLen = MathF.Max(
                0f,
                Width
                - AxisReach(DecrementBtnReach, Width)
                - AxisReach(IncrementBtnReach, Width));
            var (tx, tw) = ThumbRect(scrollable, followLeft, followLen);
            return x >= tx && x <= tx + tw;
        }

        float followTop = AxisReach(DecrementBtnReach, Height);
        float followLength = MathF.Max(
            0f,
            Height
            - AxisReach(DecrementBtnReach, Height)
            - AxisReach(IncrementBtnReach, Height));
        var (ty, th) = ThumbRect(scrollable, followTop, followLength);
        return y >= ty && y <= ty + th;
    }

    public bool IsDragging { get; private set; }

    internal bool IsModelDisabled
        => ScalarAltered is null && Model is { HasOverflow: false };

    internal bool IsExhibitShown => !HideWhenDisabled || !IsModelDisabled;

    private FinishBtn BtnAt(float x, float y)
    {
        if (x < 0f || x >= Width || y < 0f || y >= Height)
            return FinishBtn.None;

        if (Horizontal)
        {
            if (x < AxisReach(DecrementBtnReach, Width))
                return FinishBtn.Decrement;
            return x >= Width - AxisReach(IncrementBtnReach, Width) ? FinishBtn.Increment : FinishBtn.None;
        }

        if (y < AxisReach(DecrementBtnReach, Height))
            return FinishBtn.Decrement;
        return y >= Height - AxisReach(IncrementBtnReach, Height) ? FinishBtn.Increment : FinishBtn.None;
    }

    private static float AxisReach(float authoredReach, float axisLen)
        => Math.Clamp(authoredReach, 0f, MathF.Max(0f, axisLen));

    public bool HideWhenDisabled { get; set; }

    private void PaintThumbMarker(
        WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint ident, float rectX, float rectY, float rectW, float rectH, bool vertical)
    {
        if (ident is 0 || rectW <= 0f || rectH <= 0f) return;
        var (bmp, nativeW, nativeH) = locate(ident);
        if (bmp is 0 || nativeW is 0 || nativeH is 0) return;

        if (vertical)
        {
            float paintH = MathF.Min(nativeH, rectH);
            float y = rectY + (rectH - paintH) * 0.5f;
            cx.SketchSprite(bmp, rectX, y, rectW, paintH, 0f, 0f, rectW / nativeW, paintH / nativeH, Vector4.One);
        }
        else
        {
            float paintW = MathF.Min(nativeW, rectW);
            float x = rectX + (rectW - paintW) * 0.5f;
            cx.SketchSprite(bmp, x, rectY, paintW, rectH, 0f, 0f, paintW / nativeW, rectH / nativeH, Vector4.One);
        }
    }

    private void PaintHorizontalModel(
        WidgetRenderScope cx,
        Func<uint, (uint tex, int w, int h)> locate,
        WidgetScrollable model)
    {
        float decrementReach = AxisReach(DecrementBtnReach, Width);
        float incrementReach = AxisReach(IncrementBtnReach, Width);
        PaintTiled(cx, locate, FollowSprite, 0f, 0f, Width, Height);
        PaintSprite(cx, locate, EngagedBeginSprite, 0f, 0f, decrementReach, Height);
        PaintSprite(cx, locate, EngagedFinishSprite,
            Width - incrementReach, 0f, incrementReach, Height);

        float followLeft = decrementReach;
        float followLen = MathF.Max(0f, Width - decrementReach - incrementReach);
        var (tx, tw) = ThumbRect(model, followLeft, followLen);
        if (ThumbTopSprite is not 0 && ThumbBotSprite is not 0 && tw >= 2f * CapH)
        {
            PaintSprite(cx, locate, EngagedThumbTopSprite, tx, 0f, CapH, Height);
            PaintTiled(cx, locate, EngagedThumbSprite, tx + CapH, 0f, tw - 2f * CapH, Height);
            PaintSprite(cx, locate, EngagedThumbBotSprite, tx + tw - CapH, 0f, CapH, Height);
        }
        else
        {
            PaintThumbMarker(cx, locate, EngagedThumbSprite, tx, 0f, tw, Height, vertical: false);
        }
    }

    private void PaintVerticalScalar(
        WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate)
    {
        PaintTiled(cx, locate, FollowSprite, 0f, 0f, Width, Height);
        float thumbHeight = ScalarThumbReach(locate, Height);
        float travel = MathF.Max(0f, Height - thumbHeight);
        float y = travel * ScalarLocus;
        PaintSprite(cx, locate, EngagedThumbSprite, 0f, y, Width, thumbHeight);
    }

    private void PaintPlainScalar(WidgetRenderScope cx)
    {
        cx.SketchPopulate(0f, 0f, Width, Height, PlainFollowTint);
        cx.SketchRectOutline(0f, 0f, Width, Height, PlainBorderColor, 1f);

        if (Horizontal)
        {
            float nubWidth = MathF.Min(6f, Width);
            float travel = MathF.Max(0f, Width - nubWidth);
            float x = travel * ScalarLocus;
            cx.SketchPopulate(x, 0f, nubWidth, Height, PlainNubTint);
        }
        else
        {
            float nubHeight = MathF.Min(6f, Height);
            float travel = MathF.Max(0f, Height - nubHeight);
            float y = travel * ScalarLocus;
            cx.SketchPopulate(0f, y, Width, nubHeight, PlainNubTint);
        }
    }

    // Draw a sprite stretched 1:1 to the dest rect
    private void PaintSprite(WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint ident, float x, float y, float w, float h)
    {
        if (ident is 0 || w <= 0f || h <= 0f) return;
        var (bmp, _, _) = locate(ident);
        if (bmp is 0) return;
        cx.SketchSprite(bmp, x, y, w, h, 0f, 0f, 1f, 1f, Vector4.One);
    }

    // Draw a sprite TILED to fill the dest rect (UV-repeat at native size on both axes - the UI
    // texture is GL_REPEAT-wrapped)
    private void PaintTiled(WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint ident, float x, float y, float w, float h)
    {
        if (ident is 0 || w <= 0f || h <= 0f) return;
        var (bmp, tw, th) = locate(ident);
        if (bmp is 0 || tw is 0 || th is 0) return;
        cx.SketchSprite(bmp, x, y, w, h, 0f, 0f, w / tw, h / th, Vector4.One);
    }

    private void PaintTiledClipped(
        WidgetRenderScope cx,
        Func<uint, (uint tex, int w, int h)> locate,
        uint ident,
        float spanLeft,
        float x,
        float w,
        float h)
    {
        if (ident is 0 || w <= 0f || h <= 0f) return;
        var (bmp, tw, th) = locate(ident);
        if (bmp is 0 || tw is 0 || th is 0) return;
        float u0 = (x - spanLeft) / tw;
        float u1 = u0 + w / tw;
        cx.SketchSprite(bmp, x, 0f, w, h, u0, 0f, u1, h / th, Vector4.One);
    }
}
