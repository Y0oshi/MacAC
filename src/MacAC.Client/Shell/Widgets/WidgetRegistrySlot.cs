using System.Numerics;
using MacAC.Client.Graphics;

namespace MacAC.Client.Shell;

public sealed class WidgetRegistrySlot : WidgetGearSlot
{
    public uint ListingTag { get; set; }
    public uint RegistryGlyphTexture { get; set; }
    public uint RegistryTopLayerTexture { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public bool UnhideCaption { get; init; }
    public uint BackgroundSprite { get; init; }
    public WidgetDatFont? LabelFont { get; init; }
    public Vector4 LabelColor { get; init; } = new(0.92f, 0.88f, 0.70f, 1f);

    public bool Outline { get; init; }

    public Vector4 OutlineColor { get; init; } = WidgetRenderScope.DefaultOutlineTint;
    public float GlyphLeft { get; init; }
    public float GlyphTop { get; init; }
    public float GlyphWidth { get; init; }
    public float GlyphHeight { get; init; }
    public float CaptionLeft { get; init; }
    public float CaptionWidth { get; init; }
    public bool PickBehindSubstance { get; init; }

    public new Action? Clicked { get; set; }
    public new Action? DoubleClicked { get; set; }
    public object? RegistryPullCargo { get; init; }
    public Action<object>? PullBegan { get; init; }
    public Action<object>? PullEnded { get; init; }
    public Action<object>? Dropped { get; init; }

    public Func<object, GearDragAcceptance>? PullOverAcceptance { get; init; }

    protected override bool IsShortcutOccupied => ListingTag is not 0u;
    public override bool IsVacantSocket => ListingTag is 0u;

    public override string? FetchHintPhrase() => string.IsNullOrWhiteSpace(Label) ? null : Label;

    public override bool IsPullSrc => RegistryPullCargo is not null;
    public override object? FetchPullCargo() => RegistryPullCargo;
    public override (uint tex, int w, int h)? FetchPullGhost()
    {
        return RegistryPullCargo is not null && RegistryGlyphTexture is not 0u
            ? (RegistryGlyphTexture, 32, 32)
            : null;
    }

    public override bool OnSignal(in WidgetSignal e)
    {
        switch (e.Type)
        {
            case WidgetEventType.PointerDown:
                if (ListingTag is not 0u
                    && SeekRoster() is { PrimaryRegistryListingPressed: { } pressed })
                    pressed(ListingTag);
                return true;
            case WidgetEventType.Click:
                Clicked?.Invoke();
                return true;
            case WidgetEventType.DoublePress:
                DoubleClicked?.Invoke();
                return true;
            case WidgetEventType.RightPress:
                if (ListingTag is 0u
                    || SeekRoster() is not { ExamineRegistryListingAsked: { } examine })
                    return false;
                examine(ListingTag);
                return true;
            case WidgetEventType.PullCommence:
                if (e.Payload is not null) PullBegan?.Invoke(e.Payload);
                return true;
            case WidgetEventType.PullJoin:
                AssignPullAdmitVisual(e.Payload is { } joinCargo
                    ? PullOverAcceptance?.Invoke(joinCargo) switch
                    {
                        GearDragAcceptance.Accept => DragAcceptPhase.Accept,
                        GearDragAcceptance.Reject => DragAcceptPhase.Reject,
                        _ => DragAcceptPhase.None,
                    }
                    : DragAcceptPhase.None);
                return true;
            case WidgetEventType.PullOver:
                AssignPullAdmitVisual(DragAcceptPhase.None);
                return true;
            case WidgetEventType.DiscardReleased:
                AssignPullAdmitVisual(DragAcceptPhase.None);
                if (e.Payload is not null) Dropped?.Invoke(e.Payload);
                return true;
            default:
                return false;
        }
    }

    internal override void AssignPullSrcEngaged(bool engaged, object? cargo)
    {
        if (!engaged && cargo is not null) PullEnded?.Invoke(cargo);
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (BackgroundSprite is not 0u && SpriteResolve is not null)
        {
            var (texture, _, _) = SpriteResolve(BackgroundSprite);
            if (texture is not 0u)
                cx.SketchSprite(texture, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
        }

        if (PickBehindSubstance)
            PaintPick(cx);

        float glyphWidth = GlyphWidth > 0f ? GlyphWidth : UnhideCaption ? MathF.Min(32f, Height) : Width;
        float glyphHeight = GlyphHeight > 0f ? GlyphHeight : UnhideCaption ? MathF.Min(32f, Height) : Height;
        if (RegistryGlyphTexture is not 0)
            cx.SketchSprite(RegistryGlyphTexture, GlyphLeft, GlyphTop, glyphWidth, glyphHeight, 0f, 0f, 1f, 1f, Vector4.One);
        else if (SpriteResolve is not null && VacantSprite is not 0)
        {
            var (texture, _, _) = SpriteResolve(VacantSprite);
            if (texture is not 0)
                cx.SketchSprite(texture, GlyphLeft, GlyphTop, glyphWidth, glyphHeight, 0f, 0f, 1f, 1f, Vector4.One);
        }

        if (RegistryTopLayerTexture is not 0)
            cx.SketchSprite(RegistryTopLayerTexture, GlyphLeft, GlyphTop, glyphWidth, glyphHeight, 0f, 0f, 1f, 1f, Vector4.One);

        PaintShortcutTopLayer(cx);

        if (!PickBehindSubstance)
            PaintPick(cx);

        if (UnhideCaption)
        {
            float captionLeft = CaptionLeft > 0f ? CaptionLeft : GlyphLeft + glyphWidth + 4f;
            float captionWidth = CaptionWidth > 0f ? CaptionWidth : MathF.Max(0f, Width - captionLeft);
            if (LabelFont is not null)
            {
                string phrase = FitPhrase(Label, captionWidth, LabelFont.MeasureWidth);
                float y = MathF.Max(0f, (Height - LabelFont.LineHeight) * 0.5f);
                cx.PaintStringDat(LabelFont, phrase, captionLeft, y, LabelColor, Outline, OutlineColor);
            }
            else
            {
                BitmapFont? typeface = cx.DefaultFont;
                string phrase = typeface is null ? Label : FitPhrase(Label, captionWidth, typeface.MeasureWidth);
                float y = typeface is null ? 3f : MathF.Max(0f, (Height - typeface.LineHeight) * 0.5f);
                cx.SketchString(phrase, captionLeft, y, LabelColor, typeface);
            }
            if (!string.IsNullOrWhiteSpace(Detail))
                cx.SketchString(Detail!, MathF.Max(captionLeft, Width - 48f), 3f, Vector4.One);
        }

        PaintPullAdmitTopLayer(cx);
    }

    private void PaintPick(WidgetRenderScope cx)
    {
        if (!Selected || SpriteResolve is null || ChosenSprite is 0u) return;
        var (texture, _, _) = SpriteResolve(ChosenSprite);
        if (texture is not 0u)
            cx.SketchSprite(texture, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
    }

    private static string FitPhrase(string phrase, float width, Func<string, float> gauge)
    {
        if (width <= 0f || string.IsNullOrEmpty(phrase) || gauge(phrase) <= width)
            return phrase;

        const string ellipsis = "...";
        float ellipsisWidth = gauge(ellipsis);
        if (ellipsisWidth >= width) return string.Empty;
        int lo = 0, hi = phrase.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (gauge(phrase[..mid]) + ellipsisWidth <= width) lo = mid;
            else hi = mid - 1;
        }
        return phrase[..lo] + ellipsis;
    }
}
