using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetMarkupList
{
    public Func<IReadOnlyList<uint>>? GlyphIdentsSrc { get; set; }

    public Func<uint, (uint tex, int w, int h)>? GlyphLocate { get; set; }

    public Action<int>? PickAltered { get; set; }

    public bool PickBandTurnedOn { get; set; }

    public IReadOnlyList<WidgetMarkupListColumn>? Columns
    {
        get;
        set
        {
            field = value;
            int tally = value?.Count ?? 0;
            _stashedPhraseRanks = new IReadOnlyList<string>?[tally];
            _stashedTintRanks = new IReadOnlyList<uint>?[tally];
            _stashedVerifyRanks = new IReadOnlyList<bool>?[tally];
            _stashedGlyphRanks = new IReadOnlyList<uint>?[tally];
            _cachedLayout = new (float x, float w)[tally];
            _tempIsAuto = new bool[tally];
            _tempFixedWidth = new float[tally];
            _stashedRankTally = 0;
        }
    }

    public WidgetDatFont? DatFont { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    public WidgetMarkupList() { CapturesPointerDrag = true; }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (Columns is { Count: > 0 } columns)
            return OnSignalColumns(e, columns);

        var gearList = GearListSrc();
        int shownRanks = ShownRanks;
        float substanceWidth = gearList.Count > shownRanks
            ? MathF.Max(0f, Width - ScrollerWidth)
            : Width;
        if (TryHndScrollerSignal(e, substanceWidth, gearList.Count, shownRanks))
            return true;

        if (e.Type == WidgetEventType.Roll)
        {
            _topRank -= Math.Sign(e.Data0);
            LimitTop(gearList.Count, shownRanks);
            return true;
        }
        if (e.Type != WidgetEventType.PointerDown || !Enabled)
            return false;
        int rank = (int)MathF.Floor(e.Data2 / MathF.Max(1f, RowHeight));
        int ordinal = _topRank + rank;
        if (rank >= 0 && rank < shownRanks && ordinal >= 0 && ordinal < gearList.Count)
            PickAltered?.Invoke(ordinal);
        return true;
    }

    protected override void OnPaint(WidgetRenderScope ctx)
    {
        if (Columns is { Count: > 0 } columns)
        {
            PaintColumns(ctx, columns);
            return;
        }

        var gearList = GearListSrc();
        var gearTints = GearTintsSrc();
        var glyphIdents = GlyphIdentsSrc?.Invoke();
        float glyphColumn = glyphIdents is not null
            ? MathF.Max(0f, RowHeight - 2f)
            : 0f;
        int shownRanks = ShownRanks;
        int chosen = ChosenOrdinalSrc();
        if (chosen != _previousRevealedChosen)
        {
            _previousRevealedChosen = chosen;
            if (chosen >= 0 && chosen < gearList.Count)
            {
                if (chosen < _topRank)
                    _topRank = chosen;
                else if (chosen >= _topRank + shownRanks)
                    _topRank = chosen - shownRanks + 1;
            }
        }
        LimitTop(gearList.Count, shownRanks);

        bool unhideScroller = gearList.Count > shownRanks;
        float substanceWidth = unhideScroller ? MathF.Max(0f, Width - ScrollerWidth) : Width;

        ctx.SketchPopulate(0f, 0f, Width, Height, BackgroundColor);
        ctx.SketchRectOutline(0f, 0f, Width, Height, BorderColor, 1f);
        int finish = Math.Min(gearList.Count, _topRank + shownRanks);
        for (int ordinal = _topRank; ordinal < finish; ++ordinal)
        {
            float y = (ordinal - _topRank) * RowHeight;
            if (ordinal == chosen && PickBandTurnedOn)
                ctx.SketchPopulate(1f, y + 1f, substanceWidth - 2f, RowHeight - 1f, SelectedColor);

            if (glyphIdents is not null && ordinal < glyphIdents.Count && GlyphLocate is { } locate)
            {
                uint glyphIdent = glyphIdents[ordinal];
                if (glyphIdent is not 0u)
                {
                    (uint bmp, int w, int h) = locate(glyphIdent);
                    if (bmp is not 0u && w > 0 && h > 0)
                    {
                        float reach = MathF.Max(0f, glyphColumn - 2f);
                        float scaling = MathF.Min(reach / w, reach / h);
                        float paintWidth = w * scaling;
                        float paintHeight = h * scaling;
                        ctx.SketchSprite(
                            bmp,
                            1f + (reach - paintWidth) * 0.5f,
                            y + (RowHeight - paintHeight) * 0.5f,
                            paintWidth, paintHeight,
                            0f, 0f, 1f, 1f, Vector4.One);
                    }
                }
            }

            string phrase = gearList[ordinal];
            Vector4 phraseTint = ordinal < gearTints.Count
                ? Rgb(gearTints[ordinal])
                : TextColor;
            float phraseX = Padding + glyphColumn;
            float phraseY = y + MathF.Max(0f,
                (RowHeight - (DatFont?.LineHeight ?? 14f)) * 0.5f);
            if (DatFont is { } typeface)
                ctx.PaintStringDat(typeface, phrase, phraseX, phraseY, phraseTint, true);
            else
                ctx.SketchString(phrase, phraseX, phraseY, phraseTint);
        }

        if (unhideScroller)
            PaintScroller(ctx, substanceWidth, gearList.Count, shownRanks);
    }

    private void LimitTop(int tally, int shownRanks) =>
        _topRank = Math.Clamp(_topRank, 0, Math.Max(0, tally - shownRanks));

    private static Vector4 Rgb(uint val)
    {
        return new(
        ((val >> 16) & 0xFFu) / 255f,
        ((val >> 8) & 0xFFu) / 255f,
        (val & 0xFFu) / 255f,
        1f);
    }

    private void CalculateColumnArrangement(IReadOnlyList<WidgetMarkupListColumn> columns, float sumWidth)
    {
        int num = columns.Count;
        float x = 0f;
        float totalFixed = 0f;
        int autoTally = 0;
        for (int idx = 0; idx < num; ++idx)
        {
            bool previous = idx == num - 1;
            bool auto = previous || columns[idx].IsAutoWidth;
            _tempIsAuto[idx] = auto;
            if (auto)
            {
                ++autoTally;
                continue;
            }
            float avail = MathF.Max(0f, sumWidth - x);
            float w = MathF.Min(MathF.Max(0f, columns[idx].Width), avail);
            _tempFixedWidth[idx] = w;
            x += w;
            totalFixed += w;
        }

        float leftover = MathF.Max(0f, sumWidth - totalFixed);
        float portion = autoTally > 0 ? MathF.Floor(leftover / autoTally) : 0f;

        float cur = 0f;
        for (int idx = 0; idx < num; ++idx)
        {
            float w;
            if (_tempIsAuto[idx])
            {
                bool isPrevious = idx == num - 1;
                w = isPrevious
                    ? MathF.Max(0f, leftover - portion * (autoTally - 1))
                    : portion;
            }
            else
            {
                w = _tempFixedWidth[idx];
            }
            _cachedLayout[idx] = (cur, w);
            cur += w;
        }
    }

    private void PaintColumns(WidgetRenderScope ctx, IReadOnlyList<WidgetMarkupListColumn> columns)
    {
        int rankTally = 0;
        for (int c = 0; c < columns.Count; ++c)
        {
            WidgetMarkupListColumn col = columns[c];
            switch (col.Kind)
            {
                case WidgetMarkupListColumnKind.Text:
                    _stashedPhraseRanks[c] = col.PhraseSource!();
                    _stashedTintRanks[c] = col.TintsSrc?.Invoke();
                    rankTally = Math.Max(rankTally, _stashedPhraseRanks[c]!.Count);
                    break;
                case WidgetMarkupListColumnKind.Check:
                    _stashedVerifyRanks[c] = col.VerifySrc!();
                    rankTally = Math.Max(rankTally, _stashedVerifyRanks[c]!.Count);
                    break;
                case WidgetMarkupListColumnKind.Icon:
                    _stashedGlyphRanks[c] = col.GlyphValsSrc!();
                    rankTally = Math.Max(rankTally, _stashedGlyphRanks[c]!.Count);
                    break;
            }
        }
        _stashedRankTally = rankTally;

        int shownRanks = ShownRanks;
        bool unhideScroller = rankTally > shownRanks;
        float substanceWidth = unhideScroller ? MathF.Max(0f, Width - ScrollerWidth) : Width;
        CalculateColumnArrangement(columns, substanceWidth);

        int chosen = ChosenOrdinalSrc();
        if (chosen != _previousRevealedChosen)
        {
            _previousRevealedChosen = chosen;
            if (chosen >= 0 && chosen < rankTally)
            {
                if (chosen < _topRank)
                    _topRank = chosen;
                else if (chosen >= _topRank + shownRanks)
                    _topRank = chosen - shownRanks + 1;
            }
        }
        LimitTop(rankTally, shownRanks);

        ctx.SketchPopulate(0f, 0f, Width, Height, BackgroundColor);
        ctx.SketchRectOutline(0f, 0f, Width, Height, BorderColor, 1f);

        int finish = Math.Min(rankTally, _topRank + shownRanks);
        for (int ordinal = _topRank; ordinal < finish; ++ordinal)
        {
            float y = (ordinal - _topRank) * RowHeight;
            if (ordinal == chosen && PickBandTurnedOn)
                ctx.SketchPopulate(1f, y + 1f, substanceWidth - 2f, RowHeight - 1f, SelectedColor);

            for (int c = 0; c < columns.Count; ++c)
            {
                (float chamberX, float chamberW) = _cachedLayout[c];
                if (chamberW <= 0f)
                    continue;

                ctx.PushClip(chamberX, y, chamberW, RowHeight);
                try
                {
                    switch (columns[c].Kind)
                    {
                        case WidgetMarkupListColumnKind.Text:
                            PaintPhraseChamber(ctx, _stashedPhraseRanks[c], _stashedTintRanks[c], ordinal, chamberX, y);
                            break;
                        case WidgetMarkupListColumnKind.Check:
                            PaintVerifyChamber(ctx, _stashedVerifyRanks[c], ordinal, chamberX, chamberW, y);
                            break;
                        case WidgetMarkupListColumnKind.Icon:
                            PaintGlyphChamber(ctx, columns[c], _stashedGlyphRanks[c], ordinal, chamberX, chamberW, y);
                            break;
                    }
                }
                finally
                {
                    ctx.TakeClip();
                }
            }
        }

        if (unhideScroller)
            PaintScroller(ctx, substanceWidth, rankTally, shownRanks);
    }

    private void PaintPhraseChamber(
        WidgetRenderScope ctx, IReadOnlyList<string>? texts, IReadOnlyList<uint>? tints,
        int ordinal, float chamberX, float y)
    {
        if (texts is null || ordinal >= texts.Count)
            return;
        string phrase = texts[ordinal];
        Vector4 tint = tints is { } list && ordinal < list.Count ? Rgb(list[ordinal]) : TextColor;
        float phraseX = chamberX + Padding;
        float phraseY = y + MathF.Max(0f, (RowHeight - (DatFont?.LineHeight ?? 14f)) * 0.5f);
        if (DatFont is { } typeface)
            ctx.PaintStringDat(typeface, phrase, phraseX, phraseY, tint, true);
        else
            ctx.SketchString(phrase, phraseX, phraseY, tint);
    }

    private void PaintVerifyChamber(
        WidgetRenderScope ctx, IReadOnlyList<bool>? flagSet, int ordinal, float chamberX, float chamberW, float y)
    {
        bool isChecked = flagSet is not null && ordinal < flagSet.Count && flagSet[ordinal];
        float reach = MathF.Max(0f, chamberW - 2f);
        float lampX = chamberX + 1f + MathF.Max(0f, reach - WidgetCheckLamp.LampDims) * 0.5f;
        float lampY = y + MathF.Max(1f, (RowHeight - WidgetCheckLamp.LampDims) * 0.5f);
        WidgetCheckLamp.Draw(ctx, lampX, lampY, isChecked);
    }

    private void PaintGlyphChamber(
        WidgetRenderScope ctx, WidgetMarkupListColumn column, IReadOnlyList<uint>? idents,
        int ordinal, float chamberX, float chamberW, float y)
    {
        if (idents is null || ordinal >= idents.Count || column.GlyphResolve is not { } locate)
            return;
        uint ident = idents[ordinal];
        if (ident is 0u)
            return;
        (uint bmp, int w, int h) = locate(ident);
        if (bmp is 0u || w <= 0 || h <= 0)
            return;
        float reachW = MathF.Max(0f, chamberW - 2f);
        float reachH = MathF.Max(0f, RowHeight - 2f);
        float scaling = MathF.Min(reachW / w, reachH / h);
        float paintWidth = w * scaling;
        float paintHeight = h * scaling;
        ctx.SketchSprite(
            bmp,
            chamberX + 1f + (reachW - paintWidth) * 0.5f,
            y + (RowHeight - paintHeight) * 0.5f,
            paintWidth, paintHeight,
            0f, 0f, 1f, 1f, Vector4.One);
    }

    private bool OnSignalColumns(in WidgetSignal e, IReadOnlyList<WidgetMarkupListColumn> columns)
    {
        int rankTally = _stashedRankTally;
        int shownRanks = ShownRanks;
        float substanceWidth = rankTally > shownRanks
            ? MathF.Max(0f, Width - ScrollerWidth)
            : Width;
        if (TryHndScrollerSignal(e, substanceWidth, rankTally, shownRanks))
            return true;

        if (e.Type == WidgetEventType.Roll)
        {
            _topRank -= Math.Sign(e.Data0);
            LimitTop(rankTally, shownRanks);
            return true;
        }
        if (e.Type != WidgetEventType.PointerDown || !Enabled)
            return false;

        int rank = (int)MathF.Floor(e.Data2 / MathF.Max(1f, RowHeight));
        int ordinal = _topRank + rank;
        if (rank < 0 || rank >= shownRanks || ordinal < 0 || ordinal >= rankTally)
            return true;

        float ownX = e.Data1;
        for (int c = 0; c < columns.Count; ++c)
        {
            (float chamberX, float chamberW) = _cachedLayout[c];
            if (ownX < chamberX || ownX >= chamberX + chamberW)
                continue;
            switch (columns[c].Kind)
            {
                case WidgetMarkupListColumnKind.Text:
                    if (columns[c].PhraseClicked is { } onPhrasePress)
                    {
                        if (ordinal < (_stashedPhraseRanks[c]?.Count ?? 0))
                            onPhrasePress(ordinal);
                    }
                    else
                    {
                        PickAltered?.Invoke(ordinal);
                    }
                    break;
                case WidgetMarkupListColumnKind.Check:
                    if (ordinal < (_stashedVerifyRanks[c]?.Count ?? 0))
                        columns[c].VerifyAltered?.Invoke(ordinal);
                    break;
                case WidgetMarkupListColumnKind.Icon:
                    if (ordinal < (_stashedGlyphRanks[c]?.Count ?? 0))
                        columns[c].GlyphClicked?.Invoke(ordinal);
                    break;
            }
            break;
        }
        return true;
    }

    private void ConfigureRoll(int rankTally, int shownRanks)
    {
        int strokeHeight = Math.Max(1, (int)MathF.Round(RowHeight));
        _roll.LineHeight = strokeHeight;
        _roll.AssignExtents(rankTally * strokeHeight, shownRanks * strokeHeight);
        _roll.AssignRollY(_topRank * strokeHeight);
    }

    private void PaintScroller(WidgetRenderScope cx, float x, int rankTally, int shownRanks)
    {
        if (SpriteResolve is not { } locate) return;
        ConfigureRoll(rankTally, shownRanks);

        float decReach = Math.Clamp(RollBtnReach, 0f, Height);
        float incReach = Math.Clamp(RollBtnReach, 0f, Height - decReach);

        PaintTiledSprite(cx, locate, CanonScrollbarChrome.Follow, x, 0f, ScrollerWidth, Height);
        PaintPlanarSprite(cx, locate, CanonScrollbarChrome.UpNorm, x, 0f, ScrollerWidth, decReach);
        PaintPlanarSprite(cx, locate, CanonScrollbarChrome.DownNorm, x, Height - incReach, ScrollerWidth, incReach);

        float followTop = decReach;
        float followLength = MathF.Max(0f, Height - decReach - incReach);
        var (ty, th) = WidgetScroller.ThumbRect(_roll, followTop, followLength);
        const float capH = 3f;
        if (th >= 2f * capH)
        {
            PaintPlanarSprite(cx, locate, CanonScrollbarChrome.ThumbTopNorm, x, ty, ScrollerWidth, capH);
            PaintTiledSprite(cx, locate, CanonScrollbarChrome.ThumbMidNorm, x, ty + capH, ScrollerWidth, th - 2f * capH);
            PaintPlanarSprite(cx, locate, CanonScrollbarChrome.ThumbBotNorm, x, ty + th - capH, ScrollerWidth, th <= 0f ? 0f : capH);
        }
        else
        {
            PaintPlanarSprite(cx, locate, CanonScrollbarChrome.ThumbMidNorm, x, ty, ScrollerWidth, th);
        }
    }

    private static void PaintPlanarSprite(
        WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint ident, float x, float y, float w, float h)
    {
        if (ident is 0 || w <= 0f || h <= 0f) return;
        var (bmp, _, _) = locate(ident);
        if (bmp is 0) return;
        cx.SketchSprite(bmp, x, y, w, h, 0f, 0f, 1f, 1f, Vector4.One);
    }

    private static void PaintTiledSprite(
        WidgetRenderScope cx, Func<uint, (uint tex, int w, int h)> locate,
        uint ident, float x, float y, float w, float h)
    {
        if (ident is 0 || w <= 0f || h <= 0f) return;
        var (bmp, tw, th) = locate(ident);
        if (bmp is 0 || tw is 0 || th is 0) return;
        cx.SketchSprite(bmp, x, y, w, h, 0f, 0f, w / tw, h / th, Vector4.One);
    }

    private bool TryHndScrollerSignal(in WidgetSignal e, float substanceWidth, int rankTally, int shownRanks)
    {
        if (_draggingThumb)
        {
            if (e.Type == WidgetEventType.PointerRelocate)
            {
                PullThumb(e.Data2, rankTally, shownRanks);
                return true;
            }
            if (e.Type is WidgetEventType.PointerUp or WidgetEventType.GrabAltered)
            {
                _draggingThumb = false;
                return true;
            }
        }

        if (rankTally <= shownRanks) return false;
        if (e.Type != WidgetEventType.PointerDown || !Enabled) return false;
        if (e.Data1 < substanceWidth) return false;

        ConfigureRoll(rankTally, shownRanks);
        float decReach = Math.Clamp(RollBtnReach, 0f, Height);
        float incReach = Math.Clamp(RollBtnReach, 0f, Height - decReach);
        float ly = e.Data2;

        if (ly < decReach) { TickRank(-1, rankTally, shownRanks); return true; }
        if (ly >= Height - incReach) { TickRank(1, rankTally, shownRanks); return true; }

        float followTop = decReach;
        float followLength = MathF.Max(0f, Height - decReach - incReach);
        var (ty, th) = WidgetScroller.ThumbRect(_roll, followTop, followLength);
        if (ly >= ty && ly <= ty + th)
        {
            _draggingThumb = true;
            _thumbPullShift = ly - ty;
        }
        else
        {
            SheetRank(ly < ty ? -1 : 1, rankTally, shownRanks);
        }
        return true;
    }

    private void PullThumb(float ly, int rankTally, int shownRanks)
    {
        ConfigureRoll(rankTally, shownRanks);
        float decReach = Math.Clamp(RollBtnReach, 0f, Height);
        float incReach = Math.Clamp(RollBtnReach, 0f, Height - decReach);
        float followTop = decReach;
        float followLength = MathF.Max(0f, Height - decReach - incReach);
        var (_, thumbH) = WidgetScroller.ThumbRect(_roll, followTop, followLength);
        float travel = MathF.Max(1f, followLength - thumbH);
        float ratio = (ly - _thumbPullShift - followTop) / travel;
        _roll.AssignLocusRatio(ratio);

        int strokeHeight = Math.Max(1, (int)MathF.Round(RowHeight));
        _topRank = (int)MathF.Round((float)_roll.RollY / strokeHeight);
        LimitTop(rankTally, shownRanks);
    }

    private void TickRank(int strokes, int rankTally, int shownRanks)
    {
        _topRank += strokes;
        LimitTop(rankTally, shownRanks);
    }

    private void SheetRank(int sheets, int rankTally, int shownRanks)
    {
        _topRank += sheets * shownRanks;
        LimitTop(rankTally, shownRanks);
    }

    public override bool HndsPress => true;

    private int ShownRanks
    {
        get
        {
            return Math.Max(1, (int)MathF.Floor(
        Height / MathF.Max(1f, RowHeight)));
        }
    }
}
