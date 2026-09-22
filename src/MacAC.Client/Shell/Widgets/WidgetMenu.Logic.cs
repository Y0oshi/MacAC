namespace MacAC.Client.Shell;

public sealed partial class WidgetMenu
{
    public object? Selected { get; set; }

    public Action? PriorOpen { get; set; }

    public Func<object?, bool>? TurnedOnSupplier { get; set; }

    public Func<string>? BtnCaptionSupplier { get; set; }

    public string? TooltipText { get; set; }

    public Func<string?>? HintPhraseSupplier { get; set; }

    public bool Scrollable { get; set; }

    public uint RollFollowSprite { get; set; }

    public uint RollThumbSprite { get; set; }

    public uint RollThumbTopSprite { get; set; }

    public uint RollThumbBottomSprite { get; set; }

    public uint RollUpSprite { get; set; }

    public uint RollDownSprite { get; set; }

    public bool PopupScrollerConcealWhenDisabled { get; set; }

    internal int HoveredPopupOrdinalForTest { get; private set; } = -1;

    public override bool ReceivesHoverPointerRelocate => IsOpen && !CanonBtnArt;

    public uint ArrowCapClosedSprite { get; set; }

    public uint ArrowCapOpenSprite { get; set; }

    public uint LatestArrowCapSprite => IsOpen ? ArrowCapOpenSprite : ArrowCapClosedSprite;

    public uint LatestFaceSpriteForTest => _facePressed ? PressedSprite : NormSprite;

    public WidgetDatFont? DatFont { get; set; }

    public WidgetDatFont? BtnDatTypeface { get; set; }

    public MacAC.Client.Graphics.BitmapFont? Font { get; set; }

    public bool Outline { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    public uint NormSprite { get; set; }

    public uint PressedSprite { get; set; }

    public uint PopupBgSprite { get; set; }

    public uint GearNormSprite { get; set; }

    public uint GearHighlightSprite { get; set; }

    public bool PopupDimsToSubstance { get; set; }

    public bool BtnPhraseCentered { get; set; }

    public bool GearPhraseCentered { get; set; }

    private int ColumnTally
    {
        get
        {
            return Scrollable
        ? 1
        : (Items.Count + RowsPerColumn - 1) / System.Math.Max(1, RowsPerColumn);
        }
    }

    private float InteriorW
    {
        get
        {
            return Scrollable
        ? ColumnWidth + NetScrollerWidth
        : ColumnTally * ColumnWidth;
        }
    }

    private int NetShownRanks
    {
        get
        {
            return Scrollable && PopupDimsToSubstance
        ? System.Math.Max(1, Items.Count)
        : RowsPerColumn;
        }
    }

    private bool PopupSubstanceOverflows => Items.Count > NetShownRanks;

    private float NetScrollerWidth
        => IsPopupScrollerExhibitShown ? ScrollbarWidth : 0f;

    private float InteriorH => NetShownRanks * RowHeight;

    private float OuterW => InteriorW + 2 * Border;

    private float OuterH => InteriorH + 2 * Border;

    public float PopupOuterHeight => OuterH;

    public float PopupOuterWidth => OuterW;

    private float PopupTop => OpenUpward ? -OuterH : Height;

    public override bool ConsumesDatChildren => true;

    protected override bool ClipsDescendants => false;

    protected override bool ExpandsClipForPopup => true;

    public float NaturalBtnWidth()
    {
        string phrase = BtnCaptionSupplier?.Invoke() ?? "";
        WidgetDatFont? font = BtnDatTypeface ?? DatFont;
        float phraseW = font?.MeasureWidth(phrase) ?? Font?.MeasureWidth(phrase) ?? phrase.Length * 7f;
        return BtnPhraseIndent + phraseW + 4f + FaceCapR;
    }

    public override string? FetchHintPhrase()
    {
        string? online = HintPhraseSupplier?.Invoke();
        return !string.IsNullOrWhiteSpace(online)
            ? online
            : string.IsNullOrWhiteSpace(TooltipText)
            ? base.FetchHintPhrase()
            : TooltipText;
    }

    private int ShownTopRank
    {
        get
        {
            int strokeHeight = System.Math.Max(1, (int)MathF.Round(RowHeight));
            int upperBegin = System.Math.Max(0, Items.Count - NetShownRanks);
            int rank = (int)MathF.Round((float)PopupRoll.RollY / strokeHeight);
            return System.Math.Clamp(rank, 0, upperBegin);
        }
    }

    private float GearPhraseX(string caption)
    {
        return GearPhraseCentered
        ? MathF.Max(0f, (ColumnWidth - GaugePhrase(caption)) * 0.5f)
        : PhraseIndent;
    }

    private int HoveredGridOrdinal(float ix, float iy)
    {
        if (ix < 0 || ix >= InteriorW || iy < 0 || iy >= InteriorH) return -1;
        int col = (int)(ix / ColumnWidth);
        int rank = (int)(iy / RowHeight);
        int index = col * RowsPerColumn + rank;
        return rank >= 0 && rank < RowsPerColumn && index >= 0 && index < Items.Count ? index : -1;
    }

    private int HoveredScrollableOrdinal(float ix, float iy)
    {
        if (ix < 0 || ix >= ColumnWidth || iy < 0 || iy >= InteriorH) return -1;
        int rank = (int)(iy / RowHeight);
        int index = ShownTopRank + rank;
        return rank >= 0 && rank < NetShownRanks && index >= 0 && index < Items.Count ? index : -1;
    }

    private float StrokeH() => DatFont?.LineHeight ?? Font?.LineHeight ?? 14f;

    private void PullPopupThumb(float ly)
    {
        float iy = ly - (PopupTop + Border);         // G7: direction-aware
        ConfigurePopupRoll();
        float decReach = System.Math.Clamp(RollButtonExtent, 0f, InteriorH);
        float incReach = System.Math.Clamp(RollButtonExtent, 0f, InteriorH - decReach);
        float followTop = decReach;
        float followLength = MathF.Max(0f, InteriorH - decReach - incReach);
        var (_, thumbH) = WidgetScroller.ThumbRect(PopupRoll, followTop, followLength);
        float travel = MathF.Max(1f, followLength - thumbH);
        float ratio = (iy - _popupThumbPullShift - followTop) / travel;
        PopupRoll.AssignLocusRatio(ratio);
    }

    internal bool IsPopupScrollerExhibitShown
        => !PopupScrollerConcealWhenDisabled || PopupSubstanceOverflows;

    public bool IsOpen { get; private set; }

    private void AssignOpen(bool val)
    {
        if (IsOpen == val) return;
        if (val)
        {
            PriorOpen?.Invoke();
            OnOpen?.Invoke();
        }
        IsOpen = val;
        HoveredPopupOrdinalForTest = -1;   // stale hover from the last time this popup was open
        if (SeekTrunk() is not { } trunk) return;
        if (val) trunk.AssignEngagedPopup(this, () => AssignOpen(false));
        else trunk.WipeEngagedPopup(this);
    }

    private float GaugePhrase(string s)
        => DatFont?.MeasureWidth(s) ?? Font?.MeasureWidth(s) ?? s.Length * 7f;

    private void ConfigurePopupRoll()
    {
        int strokeHeight = System.Math.Max(1, (int)MathF.Round(RowHeight));
        PopupRoll.LineHeight = strokeHeight;
        PopupRoll.AssignExtents(Items.Count * strokeHeight, NetShownRanks * strokeHeight);
    }

    private void RefreshPlainPopupHover(float lx, float ly)
    {
        float ix = lx - Border, iy = ly - (PopupTop + Border);
        HoveredPopupOrdinalForTest = Scrollable ? HoveredScrollableOrdinal(ix, iy) : HoveredGridOrdinal(ix, iy);
    }

    private bool ProcessScrollablePopupPointerDown(float ix, float iy)
    {
        if (ix >= 0 && ix < ColumnWidth && iy >= 0 && iy < InteriorH)
        {
            int rank = (int)(iy / RowHeight);
            int index = ShownTopRank + rank;
            if (rank >= 0 && rank < NetShownRanks && index >= 0 && index < Items.Count
                && (TurnedOnSupplier?.Invoke(Items[index].Payload) ?? true))

                OnSelect?.Invoke(Items[index].Payload);
            AssignOpen(false);
            return true;
        }

        float scrollerX = ColumnWidth;
        if (IsPopupScrollerExhibitShown
            && ix >= scrollerX && ix < scrollerX + ScrollbarWidth
            && iy >= 0 && iy < InteriorH)
        {
            ConfigurePopupRoll();
            float decReach = System.Math.Clamp(RollButtonExtent, 0f, InteriorH);
            float incReach = System.Math.Clamp(RollButtonExtent, 0f, InteriorH - decReach);

            if (iy < decReach) { PopupRoll.RollByStrokes(-1); return true; }
            if (iy >= InteriorH - incReach) { PopupRoll.RollByStrokes(1); return true; }

            float followTop = decReach;
            float followLength = MathF.Max(0f, InteriorH - decReach - incReach);
            var (ty, th) = WidgetScroller.ThumbRect(PopupRoll, followTop, followLength);
            if (iy >= ty && iy <= ty + th)
            {
                _draggingPopupThumb = true;
                _popupThumbPullShift = iy - ty;
            }
            else
            {
                PopupRoll.RollBySheet(iy < ty ? -1 : 1);
            }
            return true;
        }

        AssignOpen(false);
        return true;
    }
}
