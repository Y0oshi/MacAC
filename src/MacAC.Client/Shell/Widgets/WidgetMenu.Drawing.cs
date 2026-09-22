using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetMenu
{
    private void PaintPlainClosedPhase(WidgetRenderScope cx)
    {
        cx.SketchPopulate(0f, 0f, Width, Height, PlainBackgroundTint);
        Vector4 border = (IsOpen || _facePressed) ? PlainOpenBorderTint : PlainBorderTint;
        cx.SketchRectOutline(0f, 0f, Width, Height, border, 1f);

        string legend = BtnCaptionSupplier?.Invoke() ?? "";
        WidgetDatFont? legendTypeface = BtnDatTypeface ?? DatFont;
        float legendStrokeH = legendTypeface?.LineHeight ?? Font?.LineHeight ?? 14f;
        float phraseY = (Height - legendStrokeH) * 0.5f;
        if (legendTypeface is { } font)
            cx.PaintStringDat(font, legend, PlainPadding, phraseY, PlainPhraseTint, Outline, OutlineColor);
        else
            cx.SketchString(legend, PlainPadding, phraseY, PlainPhraseTint, Font);

        PaintPlainTriangle(cx);
    }

    private void PaintPlainTriangle(WidgetRenderScope cx)
    {
        const float w = 7f, rightMargin = 6f;
        float x = Width - rightMargin - w;
        float y = (Height - 4f) * 0.5f;
        cx.SketchPopulate(x, y, w, 1f, PlainTriangleTint);
        cx.SketchPopulate(x + 1f, y + 1f, w - 2f, 1f, PlainTriangleTint);
        cx.SketchPopulate(x + 2f, y + 2f, w - 4f, 1f, PlainTriangleTint);
        cx.SketchPopulate(x + 3f, y + 3f, w - 6f, 1f, PlainTriangleTint);
    }

    private void PaintBtnFace(WidgetRenderScope cx, uint bmp, float tw)
    {
        float uL = FaceCapL / tw, uR = (tw - FaceCapR) / tw;
        float midDest = Width - FaceCapL - FaceCapR;
        cx.SketchSprite(bmp, 0f, 0f, FaceCapL, Height, 0f, 0f, uL, 1f, Vector4.One); // LED cap
        if (midDest > 0f)
            cx.SketchSprite(bmp, FaceCapL, 0f, midDest, Height, uL, 0f, uR, 1f, Vector4.One); // gold body (stretched)
        cx.SketchSprite(bmp, Width - FaceCapR, 0f, FaceCapR, Height, uR, 0f, 1f, 1f, Vector4.One); // arrow cap
    }

    private void PaintArrowCap(WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate)
    {
        uint ident = IsOpen ? ArrowCapOpenSprite : ArrowCapClosedSprite;
        if (ident is 0) return;
        var (bmp, tw, th) = locate(ident);
        if (bmp is 0 || tw is 0 || th is 0) return;
        float dx = Width - ArrowCapWidth;
        cx.SketchSprite(bmp, dx, 0f, ArrowCapWidth, ArrowCapHeight, 0f, 0f, 1f, 1f, Vector4.One);
    }

    private void PaintGridPopup(WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate)
    {
        float outerTop = PopupTop;                 // G7: direction-aware (see PopupTop's doc)
        float inX = Border, inY = outerTop + Border; // interior origin (inside the bevel)

        PaintBevel(cx, locate, 0f, outerTop, OuterW, OuterH);
        PaintSprite(cx, locate, PopupBgSprite, inX, inY, InteriorW, InteriorH);  // panel fill behind rows

        for (int idx = 0; idx < Items.Count; ++idx)
        {
            int col = idx / RowsPerColumn, rank = idx % RowsPerColumn;
            float x = inX + col * ColumnWidth, y = inY + rank * RowHeight;
            bool chosen = Equals(Items[idx].Payload, Selected);
            PaintSprite(cx, locate, chosen ? GearHighlightSprite : GearNormSprite, x, y, ColumnWidth, RowHeight);
        }

        float phraseY = (RowHeight - StrokeH()) * 0.5f;
        for (int idx = 0; idx < Items.Count; ++idx)
        {
            int col = idx / RowsPerColumn, rank = idx % RowsPerColumn;
            bool avail = TurnedOnSupplier?.Invoke(Items[idx].Payload) ?? true;
            PaintCaption(cx, Items[idx].Label, inX + col * ColumnWidth + GearPhraseX(Items[idx].Label),
                      inY + rank * RowHeight + phraseY,
                      avail ? PhraseTintOnHand : PhraseTintGhosted);
        }
    }

    private void PaintScrollablePopup(WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate)
    {
        ConfigurePopupRoll();

        float outerTop = PopupTop;                 // G7: direction-aware (see PopupTop's doc)
        float inX = Border, inY = outerTop + Border;

        PaintBevel(cx, locate, 0f, outerTop, OuterW, OuterH);
        PaintSprite(cx, locate, PopupBgSprite, inX, inY, ColumnWidth, InteriorH);

        int begin = ShownTopRank;
        int tally = System.Math.Min(NetShownRanks, Items.Count - begin);
        float phraseY = (RowHeight - StrokeH()) * 0.5f;
        for (int i = 0; i < tally; ++i)
        {
            int index = begin + i;
            float y = inY + i * RowHeight;
            bool chosen = Equals(Items[index].Payload, Selected);
            PaintSprite(cx, locate, chosen ? GearHighlightSprite : GearNormSprite, inX, y, ColumnWidth, RowHeight);
        }
        for (int i = 0; i < tally; ++i)
        {
            int index = begin + i;
            float y = inY + i * RowHeight;
            bool avail = TurnedOnSupplier?.Invoke(Items[index].Payload) ?? true;
            PaintCaption(cx, Items[index].Label, inX + GearPhraseX(Items[index].Label), y + phraseY,
                      avail ? PhraseTintOnHand : PhraseTintGhosted);
        }

        PaintPopupScroller(cx, locate, inX + ColumnWidth, inY);
    }

    private void PaintPopupScroller(
        WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate, float x, float y)
    {
        if (!IsPopupScrollerExhibitShown) return;

        PaintSprite(cx, locate, RollFollowSprite, x, y, ScrollbarWidth, InteriorH);

        float decReach = System.Math.Clamp(RollButtonExtent, 0f, InteriorH);
        float incReach = System.Math.Clamp(RollButtonExtent, 0f, InteriorH - decReach);
        PaintSprite(cx, locate, RollUpSprite, x, y, ScrollbarWidth, decReach);
        PaintSprite(cx, locate, RollDownSprite, x, y + InteriorH - incReach, ScrollbarWidth, incReach);

        if (!PopupRoll.HasOverflow) return;

        float followTop = decReach;
        float followLength = MathF.Max(0f, InteriorH - decReach - incReach);
        var (ty, th) = WidgetScroller.ThumbRect(PopupRoll, followTop, followLength);
        const float capH = 3f;
        if (RollThumbTopSprite is not 0 && RollThumbBottomSprite is not 0 && th >= 2f * capH)
        {
            PaintSprite(cx, locate, RollThumbTopSprite, x, y + ty, ScrollbarWidth, capH);
            PaintSprite(cx, locate, RollThumbSprite, x, y + ty + capH, ScrollbarWidth, th - 2f * capH);
            PaintSprite(cx, locate, RollThumbBottomSprite, x, y + ty + th - capH, ScrollbarWidth, capH);
        }
        else
        {
            PaintSprite(cx, locate, RollThumbSprite, x, y + ty, ScrollbarWidth, th);
        }
    }

    private void PaintGridPopupPlain(WidgetRenderScope cx)
    {
        float outerTop = PopupTop;
        float inX = Border, inY = outerTop + Border;

        cx.SketchPopulate(0f, outerTop, OuterW, OuterH, PlainBackgroundTint);
        cx.SketchRectOutline(0f, outerTop, OuterW, OuterH, PlainBorderTint, 1f);

        for (int idx = 0; idx < Items.Count; ++idx)
        {
            int col = idx / RowsPerColumn, rank = idx % RowsPerColumn;
            float x = inX + col * ColumnWidth, y = inY + rank * RowHeight;
            bool chosen = Equals(Items[idx].Payload, Selected);
            if (chosen)
                cx.SketchPopulate(x, y, ColumnWidth, RowHeight, PlainSelectedColor);
            else if (idx == HoveredPopupOrdinalForTest)
                cx.SketchPopulate(x, y, ColumnWidth, RowHeight, PlainHoverColor);
        }

        float phraseY = (RowHeight - StrokeH()) * 0.5f;
        for (int idx = 0; idx < Items.Count; ++idx)
        {
            int col = idx / RowsPerColumn, rank = idx % RowsPerColumn;
            bool avail = TurnedOnSupplier?.Invoke(Items[idx].Payload) ?? true;
            PaintCaption(cx, Items[idx].Label, inX + col * ColumnWidth + PlainPadding,
                      inY + rank * RowHeight + phraseY,
                      avail ? PlainPhraseTint : PhraseTintGhosted);
        }
    }

    private void PaintScrollablePopupPlain(WidgetRenderScope cx)
    {
        ConfigurePopupRoll();

        float outerTop = PopupTop;
        float inX = Border, inY = outerTop + Border;

        cx.SketchPopulate(0f, outerTop, OuterW, OuterH, PlainBackgroundTint);
        cx.SketchRectOutline(0f, outerTop, OuterW, OuterH, PlainBorderTint, 1f);

        int begin = ShownTopRank;
        int tally = System.Math.Min(NetShownRanks, Items.Count - begin);
        float phraseY = (RowHeight - StrokeH()) * 0.5f;
        for (int i = 0; i < tally; ++i)
        {
            int index = begin + i;
            float y = inY + i * RowHeight;
            bool chosen = Equals(Items[index].Payload, Selected);
            if (chosen)
                cx.SketchPopulate(inX, y, ColumnWidth, RowHeight, PlainSelectedColor);
            else if (index == HoveredPopupOrdinalForTest)
                cx.SketchPopulate(inX, y, ColumnWidth, RowHeight, PlainHoverColor);
        }
        for (int i = 0; i < tally; ++i)
        {
            int index = begin + i;
            bool avail = TurnedOnSupplier?.Invoke(Items[index].Payload) ?? true;
            PaintCaption(cx, Items[index].Label, inX + PlainPadding, inY + i * RowHeight + phraseY,
                      avail ? PlainPhraseTint : PhraseTintGhosted);
        }

        if (SpriteResolve is { } locate)
            PaintPopupScroller(cx, locate, inX + ColumnWidth, inY);
        else
            PaintPopupScrollerPlain(cx, inX + ColumnWidth, inY);
    }

    private void PaintPopupScrollerPlain(WidgetRenderScope cx, float x, float y)
    {
        if (!IsPopupScrollerExhibitShown) return;

        cx.SketchPopulate(x, y, ScrollbarWidth, InteriorH, PlainBackgroundTint);
        cx.SketchRectOutline(x, y, ScrollbarWidth, InteriorH, PlainBorderTint, 1f);

        if (!PopupRoll.HasOverflow) return;

        float decReach = System.Math.Clamp(RollButtonExtent, 0f, InteriorH);
        float incReach = System.Math.Clamp(RollButtonExtent, 0f, InteriorH - decReach);
        float followTop = decReach;
        float followLength = MathF.Max(0f, InteriorH - decReach - incReach);
        var (ty, th) = WidgetScroller.ThumbRect(PopupRoll, followTop, followLength);
        cx.SketchPopulate(x + 1f, y + ty, MathF.Max(0f, ScrollbarWidth - 2f), th, PlainBorderTint);
    }

    private void PaintBevel(WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        float x, float y, float w, float h)
    {
        var rects = WidgetNineSlicePane.CalculateCycleRects(w, h, Border);
        void P(uint ident, in WidgetNineSlicePane.ClientRect rect) => PaintSprite(cx, locate, ident, x + rect.X, y + rect.Y, rect.W, rect.H);
        P(CanonChromeSprites.MiddlePopulate, rects.Center);
        P(CanonChromeSprites.TopRim, rects.Top);
        P(CanonChromeSprites.BottomRim, rects.Bottom);
        P(CanonChromeSprites.LeftRim, rects.Left);
        P(CanonChromeSprites.RightRim, rects.Right);
        P(CanonChromeSprites.CornerTL, rects.TL);
        P(CanonChromeSprites.CornerTR, rects.TR);
        P(CanonChromeSprites.CornerBL, rects.BL);
        P(CanonChromeSprites.CornerBR, rects.BR);
    }

    private void PaintSprite(WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint ident, float x, float y, float w, float h)
    {
        if (ident is 0) return;
        var (bmp, tw, th) = locate(ident);
        if (bmp is 0 || tw is 0 || th is 0) return;
        // Tile at native size (the panel fill is 191×2; rows are 191×17 = 1:1)
        cx.SketchSprite(bmp, x, y, w, h, 0f, 0f, w / tw, h / th, Vector4.One);
    }

    private void PaintCaption(WidgetRenderScope cx, string s, float x, float y, Vector4 tint)
    {
        if (DatFont is { } font) cx.PaintStringDat(font, s, x, y, tint, Outline, OutlineColor);
        else cx.SketchString(s, x, y, tint, Font);
    }
}
