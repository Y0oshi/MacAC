namespace MacAC.Client.Shell;

public sealed partial class WidgetMenu
{
    public Action<object?>? OnSelect { get; set; }

    public Action? OnOpen { get; set; }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (Scrollable && IsOpen)
        {
            if (e.Type == WidgetEventType.PointerRelocate && _draggingPopupThumb)
            {
                PullPopupThumb(e.Data2);
                return true;
            }
            if (e.Type == WidgetEventType.PointerUp && _draggingPopupThumb)
            {
                _draggingPopupThumb = false;
                return true;
            }
            if (e.Type == WidgetEventType.Roll)
            {
                ConfigurePopupRoll();
                PopupRoll.RollByStrokes(-e.Data0);
                return true;
            }
        }

        if (!CanonBtnArt && IsOpen && e.Type == WidgetEventType.PointerRelocate)
        {
            RefreshPlainPopupHover(e.Data1, e.Data2);
            return true;
        }

        if (e.Type is WidgetEventType.PointerUp
            or WidgetEventType.HoverDepart
            or WidgetEventType.GrabAltered)
        {
            _facePressed = false;   // the momentary face flick ends here
            if (e.Type == WidgetEventType.HoverDepart)
                HoveredPopupOrdinalForTest = -1;
            return false;
        }

        if (e.Type != WidgetEventType.PointerDown) return false;

        float lx = e.Data1, ly = e.Data2;
        bool clickedInPopup = OpenUpward ? ly < 0 : ly >= Height;
        if (IsOpen && clickedInPopup)
        {
            float ix = lx - Border, iy = ly - (PopupTop + Border);
            if (Scrollable)
                return ProcessScrollablePopupPointerDown(ix, iy);

            if (ix >= 0 && ix < InteriorW && iy >= 0 && iy < InteriorH)
            {
                int col = (int)(ix / ColumnWidth);
                int rank = (int)(iy / RowHeight);
                int index = col * RowsPerColumn + rank;
                if (rank >= 0 && rank < RowsPerColumn && index >= 0 && index < Items.Count
                    && (TurnedOnSupplier?.Invoke(Items[index].Payload) ?? true))

                    OnSelect?.Invoke(Items[index].Payload);
            }
            AssignOpen(false);
            return true;
        }

        _facePressed = true;                       // momentary press flick
        if (!IsOpen && Items.Count is 0) return true;
        AssignOpen(!IsOpen);
        return true;
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (!CanonBtnArt)
        {
            PaintPlainClosedPhase(cx);
            return;
        }

        var locate = SpriteResolve;

        if (locate is not null)
        {
            var (bmp, tw, _) = locate(_facePressed ? PressedSprite : NormSprite);
            if (bmp is not 0 && tw > 0) PaintBtnFace(cx, bmp, tw);
        }
        string legend = BtnCaptionSupplier?.Invoke() ?? "";
        WidgetDatFont? legendTypeface = BtnDatTypeface ?? DatFont;
        float legendW = legendTypeface?.MeasureWidth(legend)
            ?? Font?.MeasureWidth(legend) ?? legend.Length * 7f;
        float legendStrokeH = legendTypeface?.LineHeight ?? Font?.LineHeight ?? 14f;
        float capX = BtnPhraseCentered
            ? MathF.Max(0f, (Width - (ArrowCapClosedSprite is not 0 ? ArrowCapWidth : 0f) - legendW) * 0.5f)
            : BtnPhraseIndent;
        if (legendTypeface is { } font)
            cx.PaintStringDat(font, legend, capX, (Height - legendStrokeH) * 0.5f, WordingTint, Outline, OutlineColor);
        else
            cx.SketchString(legend, capX, (Height - legendStrokeH) * 0.5f, WordingTint, Font);

        if (locate is not null) PaintArrowCap(cx, locate);
    }

    protected override void OnPaintTopLayer(WidgetRenderScope cx)
    {
        if (!IsOpen) return;

        if (!CanonBtnArt)
        {
            cx.PushAlphaAbsolute(1f);
            try
            {
                if (Scrollable)
                    PaintScrollablePopupPlain(cx);
                else
                    PaintGridPopupPlain(cx);
            }
            finally { cx.TakeAlpha(); }
            return;
        }

        var locate = SpriteResolve;
        if (locate is null) return;

        cx.PushAlphaAbsolute(1f);
        try
        {
            if (Scrollable)
                PaintScrollablePopup(cx, locate);
            else
                PaintGridPopup(cx, locate);
        }
        finally { cx.TakeAlpha(); }
    }

    protected override bool OnStrikeTest(float lx, float ly)
    {
        if (!IsOpen) return base.OnStrikeTest(lx, ly);
        if (lx < 0 || lx >= OuterW) return false;
        return OpenUpward ? (ly >= -OuterH && ly < Height) : (ly >= 0 && ly < Height + OuterH);
    }
}
