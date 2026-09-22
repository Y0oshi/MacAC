using System.Numerics;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public class WidgetGearSlot : WidgetElem
{
    public WidgetGearSlot()
    {
        ClickThrough = false;
        AuthoredHintTrunkElemIdent = Panels.CanonTooltipExhibitor.SharedPopupSkinTrunkElemIdent;
        AuthoredHintArrangementDid = Panels.CanonTooltipExhibitor.SharedPopupSkinArrangementDid;
    }

    public override bool ConsumesDatChildren => true;

    public uint GearIdent { get; private set; }

    public Func<uint, string?>? HintPhraseLocate { get; set; }

    public override string? FetchHintPhrase()
        => GearIdent is not 0 ? HintPhraseLocate?.Invoke(GearIdent) : null;

    public uint GlyphTexture { get; private set; }

    public uint PullGlyphTexture { get; private set; }

    public HotbarSlot? Shortcut { get; private set; }

    public int SocketIdx { get; set; } = -1;

    public GearDragSource SrcSort { get; set; } = GearDragSource.Inventory;

    public uint PullAdmitSprite { get; set; } = 0x060011F9u;
    public uint PullRejectSprite { get; set; } = 0x060011F8u;

    public bool IsOpenVessel { get; set; }
    public uint OpenVesselSprite { get; set; } = 0x06005D9Cu;

    public bool Selected { get; set; }
    public uint ChosenSprite { get; set; } = 0x06004D21u;

    public float CapPopulate { get; set; } = -1f;
    public uint CapBackSprite { get; set; } = 0x06004D22u;
    public uint CapFrontSprite { get; set; } = 0x06004D23u;

    public float StructurePopulate { get; set; } = -1f;
    public uint StructureBackSprite { get; set; } = 0x06004D24u;
    public uint StructureFrontSprite { get; set; } = 0x06004D25u;

    public void AssignStructure(int structure, int upperStructure)
    {
        StructurePopulate = upperStructure > 0 && structure < upperStructure
                ? Math.Clamp(structure / (float)upperStructure, 0f, 1f)
                : -1f;
    }

    public enum DragAcceptPhase { None, Accept, Reject }

    internal DragAcceptPhase PullAdmitVisual { get; private set; } = DragAcceptPhase.None;

    public void AssignGear(
        uint gearIdent,
        uint glyphTexture,
        HotbarSlot? shortcut = null,
        uint pullGlyphTexture = 0)
    {
        GearIdent = gearIdent;
        GlyphTexture = glyphTexture;
        PullGlyphTexture = pullGlyphTexture;
        Shortcut = shortcut;
    }

    public uint VacantSprite { get; set; } = 0x060074CFu;

    public uint WaitingSprite { get; set; } = 0x0600109Au;
    internal bool WaitingVisual { get; private set; }
    private bool _primaryPressConsumed;

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    public bool UnhideBarterTopLayer { get; set; }

    public uint BarterTopLayerSprite { get; set; }

    public IReadOnlyList<uint>? CooldownSprites { get; set; }

    public Func<uint, int>? CooldownHopProvider { get; set; }

    public void Clear()
    {
        GearIdent = 0;
        GlyphTexture = 0;
        PullGlyphTexture = 0;
        Shortcut = null;
        WaitingVisual = false;
        _primaryPressConsumed = false;
    }

    public override object? FetchPullCargo()
    {
        return AllowPullSrc && GearIdent is not 0 && !_primaryPressConsumed
                ? new GearDragPayload(GearIdent, SrcSort, SocketIdx, this, Shortcut)
                : null;
    }

    public override (uint tex, int w, int h)? FetchPullGhost()
    {
        if (GearIdent is 0) return null;
        if (PullGlyphTexture is not 0) return (PullGlyphTexture, 32, 32);
        return GlyphTexture is not 0 ? (GlyphTexture, (int)Width, (int)Height) : null;
    }

    public void AssignShortcutCount(int ordinal, bool ghosted)
    {
        ShortcutCount = ordinal;
        ShortcutGhosted = ghosted;
    }

    public void WipeShortcutCount() => ShortcutCount = -1;

    public override bool OnSignal(in WidgetSignal e)
    {
        switch (e.Type)
        {
            case WidgetEventType.PointerDown:
                _primaryPressConsumed = GearIdent is not 0
                    && SeekRoster() is { PrimaryGearPressed: { } pressed }
                    && pressed(GearIdent);
                return true;
            case WidgetEventType.Click:
                if (!_primaryPressConsumed)
                    Clicked?.Invoke();
                return true;
            case WidgetEventType.DoublePress:
                if (!_primaryPressConsumed)
                    DoubleClicked?.Invoke();
                return true;
            case WidgetEventType.RightPress:
                if (GearIdent is not 0
                    && SeekRoster() is { ExamineGearAsked: { } examine })
                    examine(GearIdent);
                return true;

            case WidgetEventType.PullCommence:
                if (SeekRoster() is { PullHandler: { } handler } liftRoster && e.Payload is GearDragPayload payload)
                    handler.OnPullLift(liftRoster, this, payload);
                return true;

            case WidgetEventType.PullJoin:            // pointer entered me mid-drag → ask the list's handler
                PullAdmitVisual = SeekRoster() is { PullHandler: { } h } roster
                              && e.Payload is GearDragPayload p
                    ? h.OnPullOver(roster, this, p) switch
                    {
                        GearDragAcceptance.Accept => DragAcceptPhase.Accept,
                        GearDragAcceptance.Reject => DragAcceptPhase.Reject,
                        _ => DragAcceptPhase.None,
                    }
                    : DragAcceptPhase.Reject;
                return true;

            case WidgetEventType.PullOver:             // UiRoot fires this on LEAVE → neutral
                PullAdmitVisual = DragAcceptPhase.None;
                return true;

            case WidgetEventType.DiscardReleased:
                PullAdmitVisual = DragAcceptPhase.None;
                if (SeekRoster() is { PullHandler: { } dh } dl && e.Payload is GearDragPayload dp)
                    dh.ProcessDiscardFree(dl, this, dp);
                return true;
        }
        return false;
    }

    public bool AllowPullSrc { get; set; } = true;

    public override bool IsPullSrc => GearIdent is not 0 && AllowPullSrc;

    public override bool HndsPress => GearIdent is not 0;

    private protected void AssignPullAdmitVisual(DragAcceptPhase phase) => PullAdmitVisual = phase;

    public int ShortcutCount { get; private set; } = -1;

    public bool ShortcutGhosted { get; private set; }

    public uint[]? RegularDigits { get; set; }

    public uint[]? GhostedDigits { get; set; }

    public uint[]? VacantDigits { get; set; }

    protected virtual bool IsShortcutOccupied => GearIdent is not 0;

    public virtual bool IsVacantSocket => GearIdent is 0;

    internal override void AssignPullSrcEngaged(bool engaged, object? cargo)
    {
        AssignWaitingPhase(engaged && SrcSort != GearDragSource.ShortcutBar);
    }

    internal void AssignWaitingPhase(bool waiting)
        => WaitingVisual = waiting && GearIdent is not 0;

    protected WidgetGearRoster? SeekRoster()
    {
        WidgetElem? element = Ancestor;
        while (element is not null) { if (element is WidgetGearRoster list) return list; element = element.Ancestor; }
        return null;
    }

    public Action? Clicked { get; set; }

    public Action? DoubleClicked { get; set; }

    internal uint[]? EngagedDigitArr()
    {
        return IsShortcutOccupied
            ? (ShortcutGhosted ? GhostedDigits : RegularDigits)
            : VacantDigits;
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (GearIdent is not 0 && GlyphTexture is not 0)
        {
            cx.SketchSprite(GlyphTexture, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
        }
        else if (SpriteResolve is not null && VacantSprite is not 0)
        {
            var (bmp, _, _) = SpriteResolve(VacantSprite);
            if (bmp is not 0)
                cx.SketchSprite(bmp, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
        }

        if (UnhideBarterTopLayer
            && GearIdent is not 0
            && SpriteResolve is not null
            && BarterTopLayerSprite is not 0)
        {
            var (topLayerBmp, _, _) = SpriteResolve(BarterTopLayerSprite);
            if (topLayerBmp is not 0)
                cx.SketchSprite(topLayerBmp, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
        }

        PaintShortcutTopLayer(cx);

        PaintGauge(cx, CapPopulate, CapBackSprite, CapFrontSprite);

        if (StructurePopulate is >= 0f and < 1f)
            PaintGauge(cx, StructurePopulate, StructureBackSprite, StructureFrontSprite);

        if (WaitingVisual && SpriteResolve is not null && WaitingSprite is not 0)
        {
            var (bmp, _, _) = SpriteResolve(WaitingSprite);
            if (bmp is not 0)
                cx.SketchSprite(bmp, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
        }

        if (IsOpenVessel && SpriteResolve is not null && OpenVesselSprite is not 0)
        {
            var (bmp, _, _) = SpriteResolve(OpenVesselSprite);
            if (bmp is not 0)
                cx.SketchSprite(bmp, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
        }
        if (Selected && SpriteResolve is not null && ChosenSprite is not 0)
        {
            var (bmp, _, _) = SpriteResolve(ChosenSprite);
            if (bmp is not 0)
                cx.SketchSprite(bmp, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
        }

        PaintPullAdmitTopLayer(cx);

        uint cooldownSprite = EngagedCooldownSprite();
        if (cooldownSprite is not 0u && SpriteResolve is not null)
        {
            var (texture, _, _) = SpriteResolve(cooldownSprite);
            if (texture is not 0u)
                cx.SketchSprite(
                    texture,
                    0f,
                    0f,
                    Width,
                    Height,
                    0f,
                    0f,
                    1f,
                    1f,
                    Vector4.One);
        }
    }

    protected void PaintPullAdmitTopLayer(WidgetRenderScope cx)
    {
        if (PullAdmitVisual == DragAcceptPhase.None || SpriteResolve is null)
            return;
        uint ident = PullAdmitVisual == DragAcceptPhase.Accept ? PullAdmitSprite : PullRejectSprite;
        if (ident is 0)
            return;
        var (bmp, _, _) = SpriteResolve(ident);
        if (bmp is not 0)
            cx.SketchSprite(bmp, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
    }

    protected void PaintShortcutTopLayer(WidgetRenderScope cx)
    {
        if (ShortcutCount < 0 || SpriteResolve is null)
            return;

        uint[]? digits = EngagedDigitArr();
        if (digits is null || ShortcutCount >= digits.Length)
            return;

        uint did = digits[ShortcutCount];
        if (did is 0)
            return;

        var (texture, _, _) = SpriteResolve(did);
        if (texture is not 0)
            cx.SketchSprite(texture, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
    }

    internal uint EngagedCooldownSprite()
    {
        if (GearIdent is 0u
            || CooldownSprites is null
            || CooldownHopProvider is null)
            return 0u;

        int hop = CooldownHopProvider(GearIdent);
        return hop is >= 1 and <= 10 && hop <= CooldownSprites.Count
            ? CooldownSprites[hop - 1]
            : 0u;
    }

    private void PaintGauge(WidgetRenderScope cx, float populate, uint backSprite, uint frontSprite)
    {
        if (populate < 0f || SpriteResolve is null)
            return;

        const float by = 1f, bw = 5f, bh = 30f;
        float bx = Width - bw;
        if (backSprite is not 0)
        {
            var (bt, _, _) = SpriteResolve(backSprite);
            if (bt is not 0) cx.SketchSprite(bt, bx, by, bw, bh, 0f, 0f, 1f, 1f, Vector4.One);
        }
        float f = Math.Clamp(populate, 0f, 1f);
        if (f > 0f && frontSprite is not 0)
        {
            var (ft, _, _) = SpriteResolve(frontSprite);
            if (ft is not 0)
            {
                float fh = bh * f;
                cx.SketchSprite(ft, bx, by + (bh - fh), bw, fh, 0f, 1f - f, 1f, 1f, Vector4.One);
            }
        }
    }
}
