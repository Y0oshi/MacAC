namespace MacAC.Client.Shell;

public sealed partial class WidgetScroller
{

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.GrabAltered)
        {
            bool wasDragging = IsDragging;
            IsDragging = false;
            _pressedBtn = FinishBtn.None;
            if (wasDragging) PullFinished?.Invoke();
            return false;
        }

        if (!IsExhibitShown)
        {
            IsDragging = false;
            _hoveredThumb = false;
            _hoveredBtn = FinishBtn.None;
            _pressedBtn = FinishBtn.None;
            return false;
        }

        if (e.Type == WidgetEventType.HoverJoin)
        {
            _hoveredBtn = BtnAt(e.Data1, e.Data2);
            _hoveredThumb = ThumbAt(e.Data1, e.Data2);
            return true;
        }
        if (e.Type == WidgetEventType.HoverDepart)
        {
            _hoveredBtn = FinishBtn.None;
            _hoveredThumb = false;
            return true;
        }
        if (e.Type == WidgetEventType.PointerRelocate)
        {
            _hoveredBtn = BtnAt(e.Data1, e.Data2);
            _hoveredThumb = ThumbAt(e.Data1, e.Data2);
        }

        if (ScalarAltered is not null)
            return Horizontal ? OnScalarSignal(e) : OnVerticalScalarSignal(e);

        if (Horizontal && Model is not null)
            return OnHorizontalModelSignal(e);

        if (Model is not { } scrollable) return false;

        switch (e.Type)
        {
            case WidgetEventType.PointerDown:
                {
                    float ly = e.Data2;
                    _pressedBtn = BtnAt(e.Data1, e.Data2);
                    float decrementReach = AxisReach(DecrementBtnReach, Height);
                    float incrementReach = AxisReach(IncrementBtnReach, Height);

                    if (ly < decrementReach) { scrollable.RollByStrokes(-1); return true; }

                    // Down-button region: authored bottom rows
                    if (ly >= Height - incrementReach) { scrollable.RollByStrokes(1); return true; }

                    float followTop = decrementReach;
                    float followLength = MathF.Max(0f, Height - decrementReach - incrementReach);
                    var (ty, th) = ThumbRect(scrollable, followTop, followLength);

                    if (ly >= ty && ly <= ty + th)
                    {
                        IsDragging = true;
                        _pullShiftY = ly - ty;
                    }
                    else
                    {
                        scrollable.RollBySheet(ly < ty ? -1 : 1);
                    }
                    return true;
                }

            case WidgetEventType.PointerRelocate when IsDragging:
                {
                    float followTop = AxisReach(DecrementBtnReach, Height);
                    float followLength = MathF.Max(
                        0f,
                        Height
                        - AxisReach(DecrementBtnReach, Height)
                        - AxisReach(IncrementBtnReach, Height));
                    float thumbH = MathF.Max(LowerThumb, followLength * scrollable.ThumbRatio);
                    float travel = MathF.Max(1f, followLength - thumbH);
                    float newRatio = ((float)e.Data2 - _pullShiftY - followTop) / travel;
                    scrollable.AssignLocusRatio(newRatio);
                    return true;
                }

            case WidgetEventType.PointerUp:
                {
                    bool wasDragging = IsDragging;
                    IsDragging = false;
                    _pressedBtn = FinishBtn.None;
                    if (wasDragging) PullFinished?.Invoke();
                    return true;
                }
        }

        return false;
    }
    protected override void OnBeat(double diffSecs)
    {
        base.OnBeat(diffSecs);
        if (!IsDragging && ScalarLocusSrc?.Invoke() is { } val)
            AssignScalarLocus(val);
    }

    protected override bool OnStrikeTest(float ownX, float ownY)
        => IsExhibitShown && base.OnStrikeTest(ownX, ownY);

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (!IsExhibitShown) return;
        if (!CanonArt)
        {
            PaintPlainScalar(cx);
            return;
        }
        if (SpriteResolve is not { } locate) return;
        if (Horizontal)
        {
            if (ScalarAltered is null && Model is { } horizontalModel)
            {
                PaintHorizontalModel(cx, locate, horizontalModel);
                return;
            }
            PaintTiled(cx, locate, FollowSprite, 0f, 0f, Width, Height);
            (float spanLeft, float spanWidth) = ScalarSpanRect();
            if (ScalarSpanSprite is not 0)
                PaintTiled(cx, locate, ScalarSpanSprite,
                    spanLeft, 0f, spanWidth, Height);
            float thumbWidth = ScalarThumbWidth(locate);
            if (ScalarPopulate() is float populate && ScalarPopulateSprite is not 0)
            {
                var (populateX, shownWidth) = ScalarFillRect(
                    Width, populate, ScalarPopulateFromRight);
                PaintTiledClipped(cx, locate, ScalarPopulateSprite,
                    0f, populateX, shownWidth, Height);
            }
            float travel = MathF.Max(0f, Width - thumbWidth);
            float x = travel * ScalarLocus;
            PaintSprite(cx, locate, EngagedThumbSprite, x, 0f, thumbWidth, Height);
            return;
        }

        if (ScalarAltered is not null)
        {
            PaintVerticalScalar(cx, locate);
            return;
        }

        if (Model is not { } scrollable) return;

        PaintTiled(cx, locate, FollowSprite, 0f, 0f, Width, Height);

        float decrementReach = AxisReach(DecrementBtnReach, Height);
        float incrementReach = AxisReach(IncrementBtnReach, Height);

        // Decrement/up and increment/down use their authored button heights
        PaintSprite(cx, locate, EngagedBeginSprite, 0f, 0f, Width, decrementReach);
        PaintSprite(cx, locate, EngagedFinishSprite,
            0f, Height - incrementReach, Width, incrementReach);

        {
            float followTop = decrementReach;
            float followLength = MathF.Max(0f, Height - decrementReach - incrementReach);
            var (ty, th) = ThumbRect(scrollable, followTop, followLength);
            if (ThumbTopSprite is not 0 && ThumbBotSprite is not 0 && th >= 2f * CapH)
            {
                PaintSprite(cx, locate, EngagedThumbTopSprite, 0f, ty, Width, CapH);
                PaintTiled(cx, locate, EngagedThumbSprite, 0f, ty + CapH, Width, th - 2f * CapH);
                PaintSprite(cx, locate, EngagedThumbBotSprite, 0f, ty + th - CapH, Width, CapH);
            }
            else
            {
                PaintThumbMarker(cx, locate, EngagedThumbSprite, 0f, ty, Width, th, vertical: true);
            }
        }
    }

    private bool OnHorizontalModelSignal(in WidgetSignal e)
    {
        var scrollable = Model!;
        switch (e.Type)
        {
            case WidgetEventType.PointerDown:
                {
                    float x = e.Data1;
                    _pressedBtn = BtnAt(e.Data1, e.Data2);
                    float decrementReach = AxisReach(DecrementBtnReach, Width);
                    float incrementReach = AxisReach(IncrementBtnReach, Width);
                    if (x < decrementReach) { scrollable.RollByStrokes(-1); return true; }
                    if (x >= Width - incrementReach) { scrollable.RollByStrokes(1); return true; }

                    float followLeft = decrementReach;
                    float followLen = MathF.Max(0f, Width - decrementReach - incrementReach);
                    var (tx, tw) = ThumbRect(scrollable, followLeft, followLen);
                    if (x >= tx && x <= tx + tw)
                    {
                        IsDragging = true;
                        _pullShiftX = x - tx;
                    }
                    else
                    {
                        scrollable.RollBySheet(x < tx ? -1 : 1);
                    }
                    return true;
                }

            case WidgetEventType.PointerRelocate when IsDragging:
                {
                    float followLeft = AxisReach(DecrementBtnReach, Width);
                    float followLen = MathF.Max(
                        0f,
                        Width
                        - AxisReach(DecrementBtnReach, Width)
                        - AxisReach(IncrementBtnReach, Width));
                    float thumbWidth = MathF.Max(LowerThumb, followLen * scrollable.ThumbRatio);
                    float travel = MathF.Max(1f, followLen - thumbWidth);
                    float ratio = ((float)e.Data1 - _pullShiftX - followLeft) / travel;
                    scrollable.AssignLocusRatio(ratio);
                    return true;
                }

            case WidgetEventType.PointerUp:
                {
                    bool wasDragging = IsDragging;
                    IsDragging = false;
                    _pressedBtn = FinishBtn.None;
                    if (wasDragging) PullFinished?.Invoke();
                    return true;
                }
        }
        return false;
    }

    private bool OnScalarSignal(in WidgetSignal e)
    {
        switch (e.Type)
        {
            case WidgetEventType.PointerDown:
                {
                    float thumbWidth = ScalarThumbWidth(SpriteResolve);
                    float travel = MathF.Max(1f, Width - thumbWidth);
                    float thumbX = travel * ScalarLocus;
                    float x = e.Data1;
                    IsDragging = true;
                    if (x >= thumbX && x <= thumbX + thumbWidth)
                    {
                        _pullShiftX = x - thumbX;
                    }
                    else
                    {
                        _pullShiftX = thumbWidth * 0.5f;
                        AlterScalarLocus((x - _pullShiftX) / travel);
                    }
                    return true;
                }

            case WidgetEventType.PointerRelocate when IsDragging:
                {
                    float thumbWidth = ScalarThumbWidth(SpriteResolve);
                    float travel = MathF.Max(1f, Width - thumbWidth);
                    AlterScalarLocus(((float)e.Data1 - _pullShiftX) / travel);
                    return true;
                }

            case WidgetEventType.PointerUp:
                {
                    bool wasDragging = IsDragging;
                    IsDragging = false;
                    _pressedBtn = FinishBtn.None;
                    if (wasDragging) PullFinished?.Invoke();
                    return true;
                }
        }

        return false;
    }

    private bool OnVerticalScalarSignal(in WidgetSignal e)
    {
        switch (e.Type)
        {
            case WidgetEventType.PointerDown:
                {
                    float thumbHeight = ScalarThumbReach(SpriteResolve, Height);
                    float travel = MathF.Max(1f, Height - thumbHeight);
                    float thumbY = travel * ScalarLocus;
                    float y = e.Data2;
                    IsDragging = true;
                    if (y >= thumbY && y <= thumbY + thumbHeight)
                    {
                        _pullShiftY = y - thumbY;
                    }
                    else
                    {
                        _pullShiftY = thumbHeight * 0.5f;
                        AlterScalarLocus((y - _pullShiftY) / travel);
                    }
                    return true;
                }

            case WidgetEventType.PointerRelocate when IsDragging:
                {
                    float thumbHeight = ScalarThumbReach(SpriteResolve, Height);
                    float travel = MathF.Max(1f, Height - thumbHeight);
                    AlterScalarLocus(((float)e.Data2 - _pullShiftY) / travel);
                    return true;
                }

            case WidgetEventType.PointerUp:
                {
                    bool wasDragging = IsDragging;
                    IsDragging = false;
                    _pressedBtn = FinishBtn.None;
                    if (wasDragging) PullFinished?.Invoke();
                    return true;
                }
        }

        return false;
    }
}
