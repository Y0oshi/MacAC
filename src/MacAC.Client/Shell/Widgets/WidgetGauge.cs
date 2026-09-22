using System.Numerics;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public enum WidgetMeterLabelAlign : byte { Left = 0, Center = 1, Right = 2 }

internal readonly record struct WidgetMeterDetailOverlaySpec(
    uint Sprite,
    float X, float Y, float W, float H,
    uint LeftMode, uint TopMode, uint RightMode, uint BottomMode,
    float ParentW, float ParentH);

public sealed class WidgetGauge : WidgetElem, IWidgetDatStateful
{
    private readonly Dictionary<uint, uint> _phasePopulateSprites = [];
    private readonly Dictionary<uint, (string Text, WidgetMeterLabelAlign Align)> _phaseCaptions = [];
    private (string Text, WidgetMeterLabelAlign Align)? _engagedPhaseCaption;
    private WidgetLoopingImageMotion? _movingBack;
    private WidgetLoopingImageMotion? _movingFront;
    private double? _animBegun;

    public bool TrySetCanonPhase(uint phaseIdent)
    {
        if (HasSpecificsTopLayer
            && phaseIdent is CanonWidgetStateIds.HideDetail or CanonWidgetStateIds.ShowDetail)
        {
            EngagedCanonPhaseIdent = phaseIdent;
            if (_specificsPassToDescendants)
                foreach (WidgetElem descendant in Children)
                    if (descendant is IWidgetDatStateful stateful)
                        stateful.TrySetCanonPhase(phaseIdent);
            return true;
        }

        bool hasPopulate = _phasePopulateSprites.TryGetValue(phaseIdent, out uint spriteIdent);
        bool hasCaption = _phaseCaptions.TryGetValue(phaseIdent, out var legend);
        if (!hasPopulate && !hasCaption)
            return false;

        EngagedCanonPhaseIdent = phaseIdent;
        if (hasPopulate)
        {
            FrontLeft = 0;
            FrontTile = spriteIdent;
            FrontRight = 0;
        }
        _engagedPhaseCaption = hasCaption ? legend : null;
        return true;
    }

    public static (float x, float y, float w, float h) CalculatePopulateRect(
        float pct, float w, float h)
    {
        if (pct < 0f) pct = 0f;
        if (pct > 1f) pct = 1f;
        return (0f, 0f, w * pct, h);
    }

    public static (float x, float y, float w, float h) CalculateVPopulateRect(
        float pct, float w, float h, bool fromBottom)
    {
        if (pct < 0f) pct = 0f;
        if (pct > 1f) pct = 1f;
        float fh = h * pct;
        return (0f, fromBottom ? h - fh : 0f, w, fh);
    }

    private bool _specificsPassToDescendants;
    private WidgetMeterDetailOverlaySpec _specificsBack;
    private WidgetMeterDetailOverlaySpec _specificsFront;

    // True when this meter absorbed the vitals detail-icon overlays
    internal bool HasSpecificsTopLayer { get; private set; }
    internal uint SpecificsBackSprite => _specificsBack.Sprite;
    internal uint SpecificsFrontSprite => _specificsFront.Sprite;
    // The back overlay's authored meter-local rect
    internal (float X, float Y, float W, float H) SpecificsBackRect
    {
        get
        {
            return (_specificsBack.X, _specificsBack.Y, _specificsBack.W, _specificsBack.H);
        }
    }

    internal WidgetMeterDetailOverlaySpec SpecificsBack => _specificsBack;
    internal WidgetMeterDetailOverlaySpec SpecificsFront => _specificsFront;

    public uint ElementId { get; set; }

    public Func<float?> Populate { get; set; } = () => 0f;
    public Func<string?> Label { get; set; } = () => null;
    public Vector4 BarTint { get; set; } = new(1f, 0f, 0f, 1f);
    public Vector4 BgTint { get; set; } = new(0f, 0f, 0f, 0.5f);
    public Vector4 LabelTint { get; set; } = new(1f, 1f, 1f, 1f);

    public bool Outline { get; set; }

    public Vector4 OutlineColor { get; set; } = WidgetRenderScope.DefaultOutlineTint;

    public WidgetDatFont? DatFont { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    public uint BackLeft { get; set; }
    public uint BackTile { get; set; }
    public uint BackRight { get; set; }
    public uint FrontLeft { get; set; }
    public uint FrontTile { get; set; }
    public uint FrontRight { get; set; }

    public uint EngagedCanonPhaseIdent { get; private set; }

    internal string? EngagedPhaseCaption => _engagedPhaseCaption?.Text;

    internal WidgetMeterLabelAlign? EngagedPhaseCaptionAlign => _engagedPhaseCaption?.Align;

    internal void ConfigureMovingTracks(WidgetLoopingImageMotion back, WidgetLoopingImageMotion front)
    {
        _movingBack = back;
        _movingFront = front;
        BackTile = back.Sample(0d);
        FrontTile = front.Sample(0d);
        _animBegun = null;
    }

    internal (uint Back, uint Front) ProbeMovingTracks(double passedSecs)
    {
        return (_movingBack?.Sample(passedSecs) ?? BackTile,
                _movingFront?.Sample(passedSecs) ?? FrontTile);
    }

    internal void ProgressMovingTracks(double instant)
    {
        if (_movingBack is null || _movingFront is null)
            return;
        if (_animBegun is null || instant - _animBegun.Value >= _movingBack.Duration)
            _animBegun = instant;
        (BackTile, FrontTile) = ProbeMovingTracks(instant - _animBegun.Value);
    }

    internal void ConfigurePhasePopulate(uint phaseIdent, uint spriteIdent)
    {
        if (phaseIdent is not 0 && spriteIdent is not 0)
            _phasePopulateSprites[phaseIdent] = spriteIdent;
    }

    internal void ConfigurePhaseCaption(uint phaseIdent, string phrase, WidgetMeterLabelAlign align)
    {
        if (phaseIdent is not 0 && !string.IsNullOrEmpty(phrase))
            _phaseCaptions[phaseIdent] = (phrase, align);
    }

    public bool Vertical { get; set; }

    public bool PopulateFromBottom { get; set; } = true;

    public WidgetGauge() { ClickThrough = true; }

    public override bool ConsumesDatChildren => true;

    internal void ConfigureSpecificsTopLayer(
        in WidgetMeterDetailOverlaySpec back,
        in WidgetMeterDetailOverlaySpec front,
        bool passToDescendants)
    {
        _specificsBack = back;
        _specificsFront = front;
        HasSpecificsTopLayer = back.Sprite is not 0 || front.Sprite is not 0;
        _specificsPassToDescendants = passToDescendants;
    }

    internal static (float X, float Y, float W, float H) CalculateSpecificsTopLayerRect(
        in WidgetMeterDetailOverlaySpec topLayer, float gaugeW, float gaugeH)
    {
        int ancestorW = (int)gaugeW, ancestorH = (int)gaugeH;
        int origAncestorW = (int)topLayer.ParentW, origAncestorH = (int)topLayer.ParentH;
        if (origAncestorW <= 0 || origAncestorH <= 0
            || (ancestorW == origAncestorW && ancestorH == origAncestorH))

            return (topLayer.X, topLayer.Y, topLayer.W, topLayer.H);

        WidgetPixelRect originalDescendant = WidgetPixelRect.FromLocusAndDims(
            (int)topLayer.X, (int)topLayer.Y, (int)topLayer.W, (int)topLayer.H);
        WidgetPixelRect originalAncestor = WidgetPixelRect.FromLocusAndDims(
            0, 0, origAncestorW, origAncestorH);
        WidgetPixelRect latestAncestor = WidgetPixelRect.FromLocusAndDims(
            0, 0, ancestorW, ancestorH);
        var net = WidgetArrangementRule.Apply(
            topLayer.LeftMode,
            topLayer.TopMode,
            topLayer.RightMode,
            topLayer.BottomMode,
            originalDescendant,
            originalAncestor,
            originalDescendant,
            latestAncestor);
        return (net.X0, net.Y0, net.Width, net.Height);
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        ProgressMovingTracks(WidgetMediaClock.Secs);
        float? pct = Populate();
        float p = pct is float pf ? (pf < 0f ? 0f : pf > 1f ? 1f : pf) : 0f;

        if (SpriteResolve is { } locate && (BackLeft is not 0 || BackTile is not 0 || FrontTile is not 0))
        {
            if (Vertical)
            {
                PaintVBar(cx, locate, BackTile, Height, fromBottom: PopulateFromBottom, isPopulate: false);
                if (pct is not null && p > 0f)
                    PaintVBar(cx, locate, FrontTile, Height * p, fromBottom: PopulateFromBottom, isPopulate: true);
            }
            else
            {
                bool specifics = HasSpecificsTopLayer
                    && EngagedCanonPhaseIdent == CanonWidgetStateIds.ShowDetail;
                PaintHBar(cx, locate, BackLeft, BackTile, BackRight, Width);
                if (specifics)
                {
                    PaintSpecificsGlyph(
                        cx, locate, _specificsBack.Sprite,
                        CalculateSpecificsTopLayerRect(in _specificsBack, Width, Height),
                        Width);
                }
                if (pct is not null && p > 0f)
                {
                    PaintHBar(cx, locate, FrontLeft, FrontTile, FrontRight, Width * p);
                    if (specifics)
                    {
                        PaintSpecificsGlyph(
                            cx, locate, _specificsFront.Sprite,
                            CalculateSpecificsTopLayerRect(in _specificsFront, Width, Height),
                            Width * p);
                    }
                }
            }
        }
        else
        {
            cx.SketchRect(0, 0, Width, Height, BgTint);
            if (pct is not null && p > 0f)
            {
                var (fx, fy, fw, fh) = CalculatePopulateRect(p, Width, Height);
                if (fw > 0f) cx.SketchRect(fx, fy, fw, fh, BarTint);
            }
        }

        string? caption = Label();
        var align = WidgetMeterLabelAlign.Center;
        if (string.IsNullOrEmpty(caption) && _engagedPhaseCaption is { } phaseCaption)
            (caption, align) = phaseCaption;
        if (!string.IsNullOrEmpty(caption))
        {
            if (DatFont is { } datTypeface)
            {
                float tw = datTypeface.MeasureWidth(caption);
                float tx = LineX(align, Width, tw);
                float ty = (Height - datTypeface.LineHeight) * 0.5f;
                cx.PaintStringDat(datTypeface, caption, tx, ty, LabelTint, Outline, OutlineColor);
            }
            else if (cx.DefaultFont is { } typeface)
            {
                float tw = typeface.MeasureWidth(caption);
                float tx = LineX(align, Width, tw);
                float ty = (Height - typeface.LineHeight) * 0.5f;
                cx.SketchString(caption, tx, ty, LabelTint);
            }
        }
    }

    private static float LineX(WidgetMeterLabelAlign align, float width, float phraseWidth)
    {
        return align switch
        {
            WidgetMeterLabelAlign.Left => 0f,
            WidgetMeterLabelAlign.Right => width - phraseWidth,
            _ => (width - phraseWidth) * 0.5f,
        };
    }

    private void PaintHBar(
        WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint leftIdent, uint midIdent, uint rightIdent, float clipW)
    {
        if (clipW <= 0f) return;
        float w = Width, h = Height;
        var (lt, lw, _) = leftIdent is not 0 ? locate(leftIdent) : (0u, 0, 0);
        var (mt, mw, _) = midIdent is not 0 ? locate(midIdent) : (0u, 0, 0);
        var (rt, rw, _) = rightIdent is not 0 ? locate(rightIdent) : (0u, 0, 0);

        float capL = lt is not 0 ? MathF.Min(lw, w) : 0f;
        float capR = rt is not 0 ? MathF.Min(rw, w - capL) : 0f;
        float midW = w - capL - capR;

        PaintPiece(cx, lt, 0f, capL, lw, h, clipW);
        PaintPiece(cx, mt, capL, midW, mw, h, clipW);
        PaintPiece(cx, rt, w - capR, capR, rw, h, clipW);
    }

    private void PaintVBar(WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint tileIdent, float shownH, bool fromBottom, bool isPopulate)
    {
        if (tileIdent is 0 || shownH <= 0f) return;
        var (bmp, _, _) = locate(tileIdent);
        if (bmp is 0) return;
        float w = Width, h = Height;
        if (shownH > h) shownH = h;
        float frac = h > 0f ? shownH / h : 0f;
        float y = isPopulate && fromBottom ? h - shownH : 0f;
        float v0 = isPopulate && fromBottom ? 1f - frac : 0f;
        float v1 = isPopulate && fromBottom ? 1f : frac;
        cx.SketchSprite(bmp, 0f, y, w, shownH, 0f, v0, 1f, v1, System.Numerics.Vector4.One);
    }

    private void PaintSpecificsGlyph(
        WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint spriteIdent, (float X, float Y, float W, float H) rect, float clipW)
    {
        if (spriteIdent is 0 || rect.W <= 0f || rect.H <= 0f) return;
        var (bmp, _, _) = locate(spriteIdent);
        if (bmp is 0) return;
        float shownW = MathF.Min(rect.W, clipW - rect.X);
        if (shownW <= 0f) return;
        float h = MathF.Min(rect.H, Height - rect.Y);
        if (h <= 0f) return;
        float u1 = shownW / rect.W;
        cx.SketchSprite(bmp, rect.X, rect.Y, shownW, h, 0f, 0f, u1, 1f, Vector4.One);
    }

    private static void PaintPiece(
        WidgetRenderScope cx, uint bmp, float pieceX, float pieceW, float nativeW, float h, float clipW)
    {
        if (bmp is 0 || pieceW <= 0f || nativeW <= 0f) return;
        float shownW = MathF.Min(pieceW, clipW - pieceX);
        if (shownW <= 0f) return;
        float u1 = shownW / nativeW;
        cx.SketchSprite(bmp, pieceX, 0f, shownW, h, 0f, 0f, u1, 1f, Vector4.One);
    }
}
