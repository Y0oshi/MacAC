using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetField
{
    public uint ElementId { get; set; }

    public WidgetDatFont? DatTypeface { get; set; }

    public MacAC.Client.Graphics.BitmapFont? Font { get; set; }

    public bool Outline { get; set; }

    public bool Selectable { get; set; }

    public bool Centered { get; set; }

    public bool RightAligned { get; set; }

    public Func<char, bool>? ToonSift { get; set; }

    public bool PickAllOnFocus { get; set; }

    public Action? OnScanSolePress { get; set; }

    public Action<string>? OnSubmit { get; set; }

    public Action? OnFocusGained { get; set; }

    public Action<string>? OnFocusLost { get; set; }

    public Action<string>? OnPhraseAltered { get; set; }

    public bool Editable
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            AcceptsFocus = value;
            IsEditControl = value;
            if (!value && IsFocused)
                SeekTrunk()?.AssignKeyboardFocus(null);
            if (!value)
                _repeatTag = null;
        }
    } = true;

    public Silk.NET.Input.IKeyboard? Keyboard { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteLocate { get; set; }

    public uint BackgroundSprite { get; set; }

    public uint FocusRailLeftSprite { get; set; }

    public uint FocusRailRightSprite { get; set; }

    public uint FocusFieldSprite { get; set; }

    private string _phrase
    {
        get;
        set
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
                return;
            field = value;
            ++_phraseVer;
            OnPhraseAltered?.Invoke(value);
        }
    } = "";

    public WidgetField()
    {
        AcceptsFocus = true;
        IsEditControl = true;
        CapturesPointerDrag = true;   // interior drag selects, doesn't move the window
    }

    public void SlotChar(char c)
    {
        if (!Editable) return;
        if (!OneStroke && (c == '\r' || c == '\n'))
            c = '\n';
        else if (c is < (char)0x20 or (char)0x7F)
            return;
        if (ToonSift is not null && !ToonSift(c)) return;
        ErasePick();
        if (_phrase.Length >= UpperToons) return;
        _phrase = _phrase.Insert(CaretSpot, c.ToString());
        ++CaretSpot;
        _historyOrdinal = -1;

        if (c == ' '
            && PhraseReplacer is { } replace
            && CaretSpot == _phrase.Length
            && replace(_phrase) is { } substitute)

            AssignPhrase(substitute);
    }

    public void Backspace()
    {
        if (!Editable) return;
        if (ErasePick()) return;
        if (CaretSpot is 0) return;
        _phrase = _phrase.Remove(CaretSpot - 1, 1);
        --CaretSpot;
    }

    public void EraseAhead()
    {
        if (!Editable) return;
        if (ErasePick()) return;
        if (CaretSpot >= _phrase.Length) return;
        _phrase = _phrase.Remove(CaretSpot, 1);
    }

    public void RelocateCaret(int diff) => ShiftCaretTo(CaretSpot + diff, false);

    public void PickAllPhrase()
    {
        if (_phrase.Length is 0) { _selMooring = null; return; }
        _selMooring = 0;
        CaretSpot = _phrase.Length;
    }

    public void AssignPhrase(string? phrase)
    {
        _phrase = phrase ?? "";
        if (_phrase.Length > UpperToons)
            _phrase = _phrase[..UpperToons];
        CaretSpot = _phrase.Length;
        _selMooring = null;
        _historyOrdinal = -1;
    }

    public void Submit()
    {
        if (!Editable) return;
        string t = _phrase;
        if (t.Trim().Length is 0)
        {
            if (WipeOnSubmit) Clear();
            return;
        }
        OnSubmit?.Invoke(t);
        if (CaptureHistory) PushHistory(t);
        if (WipeOnSubmit) Clear();
    }

    public void HistoryEarlier()
    {
        if (_history.Count is 0) return;
        _historyOrdinal = _historyOrdinal < 0 ? _history.Count - 1 : Math.Max(0, _historyOrdinal - 1);
        AssignPhraseFromHistory();
    }

    public void HistoryUpcoming()
    {
        if (_historyOrdinal < 0) return;
        ++_historyOrdinal;
        if (_historyOrdinal >= _history.Count) { _historyOrdinal = -1; Clear(); return; }
        AssignPhraseFromHistory();
    }

    public float CaretPixelX() => GaugeTo(CaretSpot);

    public override bool OnSignal(in WidgetSignal e)
    {
        switch (e.Type)
        {
            case WidgetEventType.FocusGained:
                IsFocused = true;
                if (PickAllOnFocus)
                {
                    PickAllPhrase();
                    _preserveFocusPickOnPointerDown = true;
                }
                OnFocusGained?.Invoke();
                return true;
            case WidgetEventType.FocusLost:
                OnFocusLost?.Invoke(_phrase);
                IsFocused = false; _historyOrdinal = -1;
                _selMooring = null; _selecting = false; _repeatTag = null;
                _preserveFocusPickOnPointerDown = false;
                return true;

            case WidgetEventType.Char:
                {
                    char val = (char)e.Data0;
                    if (_suppressUpcomingNewlineChar
                        && (val == '\r' || val == '\n'))
                    {
                        _suppressUpcomingNewlineChar = false;
                        return true;
                    }
                    _suppressUpcomingNewlineChar = false;
                    SlotChar(val);
                    return true;
                }

            case WidgetEventType.PointerDown:
                if (_preserveFocusPickOnPointerDown)
                {
                    _preserveFocusPickOnPointerDown = false;
                    _selecting = false;
                    return true;
                }
                CaretSpot = SmackChar(e.Data1, e.Data2);
                _selMooring = Selectable ? CaretSpot : null;
                _selecting = Selectable;
                return true;
            case WidgetEventType.PointerRelocate:
                if (Selectable && _selecting)
                    CaretSpot = SmackChar(e.Data1, e.Data2);
                return true;
            case WidgetEventType.PointerUp:
                _selecting = false;
                return true;

            case WidgetEventType.TagUp:
                if ((Silk.NET.Input.Key)e.Data0 == _repeatTag) _repeatTag = null;
                return true;

            case WidgetEventType.TagDown:
                {
                    if (!Editable)
                        return true;
                    Silk.NET.Input.Key tag = (Silk.NET.Input.Key)e.Data0;
                    if (CtrlPinned())
                    {
                        switch (tag)
                        {
                            case Silk.NET.Input.Key.A when Selectable: PickAllPhrase(); return true;
                            case Silk.NET.Input.Key.C when Selectable: DuplicatePick(); return true;
                            case Silk.NET.Input.Key.X when Selectable: CutPick(); return true;
                            case Silk.NET.Input.Key.V: Paste(); return true;
                        }
                        return true;
                    }

                    bool shift = Selectable && MovePinned();
                    switch (tag)
                    {
                        case Silk.NET.Input.Key.Escape:
                            SeekTrunk()?.AssignKeyboardFocus(null);
                            return true;

                        case Silk.NET.Input.Key.Enter:
                        case Silk.NET.Input.Key.KeypadEnter:
                            if (!OneStroke)
                            {
                                SlotChar('\n');
                                _suppressUpcomingNewlineChar = true;
                                return true;
                            }
                            Submit();
                            SeekTrunk()?.AssignKeyboardFocus(null);   // exit write mode after sending
                            return true;
                        case Silk.NET.Input.Key.Backspace: Backspace(); BeginRepeat(tag); return true;
                        case Silk.NET.Input.Key.Delete: EraseAhead(); BeginRepeat(tag); return true;
                        case Silk.NET.Input.Key.Left: ShiftCaret(-1, shift); BeginRepeat(tag); return true;
                        case Silk.NET.Input.Key.Right: ShiftCaret(1, shift); BeginRepeat(tag); return true;
                        case Silk.NET.Input.Key.Home: ShiftCaretTo(0, shift); return true;
                        case Silk.NET.Input.Key.End: ShiftCaretTo(_phrase.Length, shift); return true;
                        case Silk.NET.Input.Key.Up: HistoryEarlier(); return true;
                        case Silk.NET.Input.Key.Down: HistoryUpcoming(); return true;
                    }
                    return false;
                }
            case WidgetEventType.Roll:
                if (!OneStroke)
                {
                    Scroll.RollByStrokes(-Math.Sign(e.Data0));
                    return true;
                }
                return false;
            case WidgetEventType.Click:
                if (!Editable)
                {
                    OnScanSolePress?.Invoke();
                    return true;
                }
                return false;
        }
        return false;
    }

    protected override void OnPaint(WidgetRenderScope ctx)
    {
        bool lit = IsFocused && SpriteLocate is not null && FocusFieldSprite is not 0;
        if (lit)
        {
            var (bmp, tw, th) = SpriteLocate!(FocusFieldSprite);
            if (bmp is not 0 && tw > 0) ctx.SketchSprite(bmp, 0, 0, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
            else lit = false;
        }
        if (IsFocused && SpriteLocate is not null)
        {
            if (FocusRailLeftSprite is not 0)
            {
                var (bmp, tw, _) = SpriteLocate(FocusRailLeftSprite);
                if (bmp is not 0 && tw > 0)
                    ctx.SketchSprite(bmp, 0, 0, FocusRailLeftWidth, Height, 0f, 0f, 1f, 1f, Vector4.One);
            }
            if (FocusRailRightSprite is not 0)
            {
                var (bmp, tw, _) = SpriteLocate(FocusRailRightSprite);
                if (bmp is not 0 && tw > 0)
                    ctx.SketchSprite(
                        bmp, Width - FocusRailRightWidth, 0,
                        FocusRailRightWidth, Height, 0f, 0f, 1f, 1f, Vector4.One);
            }
        }
        if (!lit && SpriteLocate is not null && BackgroundSprite is not 0)
        {
            var (bmp, tw, th) = SpriteLocate(BackgroundSprite);
            if (bmp is not 0 && tw > 0 && th > 0)
            {
                ctx.SketchSprite(bmp, 0, 0, Width, Height, 0f, 0f,
                    Width / tw, Height / th, Vector4.One);
                lit = true;
            }
        }
        if (!lit) ctx.SketchPopulate(0, 0, Width, Height, BackgroundTint);

        if (!OneStroke)
        {
            PaintMultiStroke(ctx);
            return;
        }

        float lh = DatTypeface?.LineHeight ?? Font?.LineHeight ?? 14f;
        float ty = (Height - lh) * 0.5f;
        float shownW = MathF.Max(1f, Width - 2f * Padding);

        float caretX = GaugeTo(CaretSpot);
        float wholeW = GaugeTo(_phrase.Length);
        if (caretX - _rollX > shownW) _rollX = caretX - shownW;
        if (caretX < _rollX) _rollX = caretX;
        _rollX = Math.Clamp(_rollX, 0f, MathF.Max(0f, wholeW - shownW));
        float alignX = PhraseAlignmentShift();

        int begin = 0;
        while (begin < _phrase.Length && GaugeTo(begin + 1) <= _rollX) ++begin;
        int finish = begin;
        while (finish < _phrase.Length && GaugeTo(finish + 1) - _rollX <= shownW) ++finish;

        if (HasPick)
        {
            var (lo, hi) = SelSpan();
            float h0 = MathF.Max(alignX + GaugeTo(lo) - _rollX, 0f);
            float h1 = MathF.Min(alignX + GaugeTo(hi) - _rollX, shownW);
            if (h1 > h0) ctx.SketchPopulate(Padding + h0, ty, h1 - h0, lh, PickTint);
        }

        if (finish > begin)
        {
            string vis = _phrase[begin..finish];
            float vx = Padding + alignX + (GaugeTo(begin) - _rollX);
            if (DatTypeface is { } df2) ctx.PaintStringDat(df2, vis, vx, ty, PhraseTint, Outline, OutlineColor);
            else ctx.SketchString(vis, vx, ty, PhraseTint, Font);
        }

        if (IsFocused)
        {
            float cx = Padding + alignX + (caretX - _rollX);
            if (cx >= Padding - 1f && cx <= Width - Padding + 1f)
                ctx.SketchPopulate(cx, ty, 1f, lh, PhraseTint);
        }
    }

    internal void SecureWrappedStrokesLatest()
    {
        if (_wrappedVer == _phraseVer && _wrappedStrokes.Count > 0)
            return;

        float width = _wrappedWidth > 0f
            ? _wrappedWidth
            : MathF.Max(1f, Width - (2f * Padding));
        _wrappedStrokes = AssembleWrappedStrokes(width);
        _wrappedVer = _phraseVer;
    }

    protected override void OnBeat(double diffSecs)
    {
        if (!Editable || _repeatTag is not { } kdx) return;
        _repeatTicker -= diffSecs;
        if (_repeatTicker > 0) return;
        _repeatTicker = RepeatRate;
        bool shift = MovePinned();
        switch (kdx)
        {
            case Silk.NET.Input.Key.Backspace: Backspace(); break;
            case Silk.NET.Input.Key.Delete: EraseAhead(); break;
            case Silk.NET.Input.Key.Left: ShiftCaret(-1, shift); break;
            case Silk.NET.Input.Key.Right: ShiftCaret(1, shift); break;
            default: _repeatTag = null; break;
        }
    }

    private void ShiftCaretTo(int mark, bool shift)
    {
        mark = Math.Clamp(mark, 0, _phrase.Length);
        if (shift) _selMooring ??= CaretSpot;
        else _selMooring = null;
        CaretSpot = mark;
        _historyOrdinal = -1;
    }

    private void ShiftCaret(int diff, bool shift) => ShiftCaretTo(CaretSpot + diff, shift);

    private (int lo, int hi) SelSpan()
    {
        if (_selMooring is not { } a || a == CaretSpot) return (CaretSpot, CaretSpot);
        return (Math.Min(a, CaretSpot), Math.Max(a, CaretSpot));
    }

    private string ChosenPhrase()
    {
        var (lo, hi) = SelSpan();
        return hi > lo ? _phrase[lo..hi] : "";
    }

    // Remove the selected span (if any)
    private bool ErasePick()
    {
        if (!HasPick) { _selMooring = null; return false; }
        var (lo, hi) = SelSpan();
        _phrase = _phrase.Remove(lo, hi - lo);
        CaretSpot = lo;
        _selMooring = null;
        return true;
    }

    private void DuplicatePick()
    {
        string s = ChosenPhrase();
        if (s.Length > 0 && Keyboard is not null) Keyboard.ClipboardText = s;
    }

    private void CutPick()
    {
        if (!Editable) return;
        if (!HasPick) return;
        DuplicatePick();
        ErasePick();
        _historyOrdinal = -1;
    }

    private void Paste()
    {
        if (!Editable) return;
        if (Keyboard is null) return;
        string clip = Keyboard.ClipboardText ?? "";
        if (clip.Length is 0) return;

        var builder = new System.Text.StringBuilder(clip.Length);
        bool earlierCr = false;
        foreach (char ch in clip)
        {
            if (!OneStroke && (ch == '\r' || ch == '\n'))
            {
                if (ch == '\n' && earlierCr)
                {
                    earlierCr = false;
                    continue;
                }
                builder.Append('\n');
                earlierCr = ch == '\r';
                continue;
            }
            earlierCr = false;
            if (ch >= 0x20 && ch != 0x7F
                && (ToonSift is null || ToonSift(ch)))
                builder.Append(ch);
        }
        if (builder.Length is 0) return;

        ErasePick();
        int hall = UpperToons - _phrase.Length;
        if (hall <= 0) return;
        string ins = builder.Length > hall ? builder.ToString(0, hall) : builder.ToString();
        _phrase = _phrase.Insert(CaretSpot, ins);
        CaretSpot += ins.Length;
        _historyOrdinal = -1;
    }

    private void Clear() { _phrase = ""; CaretSpot = 0; _selMooring = null; _historyOrdinal = -1; }

    private void PushHistory(string t)
    {
        _history.Add(t);
        if (_history.Count > 100) _history.RemoveAt(0);
        _historyOrdinal = -1;
    }

    private void AssignPhraseFromHistory()
    {
        _phrase = _history[_historyOrdinal];
        CaretSpot = _phrase.Length;
        _selMooring = null;
    }

    private float GaugeTo(int idx)
    {
        if (idx <= 0) return 0f;
        string s = _phrase[..Math.Min(idx, _phrase.Length)];
        return DatTypeface is { } font ? font.MeasureWidth(s)
             : Font is { } bf ? bf.MeasureWidth(s) : 0f;
    }

    private int SmackCharX(float ownX)
    {
        float mark = ownX - Padding - PhraseAlignmentShift() + _rollX;
        if (mark <= 0f) return 0;
        int finest = 0;
        float finestDistance = float.MaxValue;
        for (int idx = 0; idx <= _phrase.Length; ++idx)
        {
            float d = MathF.Abs(GaugeTo(idx) - mark);
            if (d < finestDistance) { finestDistance = d; finest = idx; }
        }
        return finest;
    }

    private float PhraseAlignmentShift()
    {
        float shownW = MathF.Max(1f, Width - 2f * Padding);
        float spare = MathF.Max(0f, shownW - GaugeTo(_phrase.Length));
        return RightAligned ? spare : Centered ? spare * 0.5f : 0f;
    }

    private void PaintMultiStroke(WidgetRenderScope cx)
    {
        float strokeHeight = DatTypeface?.LineHeight ?? Font?.LineHeight ?? 14f;
        float shownWidth = MathF.Max(1f, Width - (2f * Padding));
        float shownHeight = MathF.Max(1f, Height - (2f * Padding));
        var strokes = AssembleWrappedStrokes(shownWidth);
        _wrappedStrokes = strokes;
        _wrappedVer = _phraseVer;
        _wrappedWidth = shownWidth;
        _wrappedStrokeHeight = strokeHeight;

        Scroll.LineHeight = Math.Max(1, (int)MathF.Round(strokeHeight));
        Scroll.AssignExtents(
            Math.Max(1, (int)MathF.Ceiling(strokes.Count * strokeHeight)),
            Math.Max(1, (int)MathF.Floor(shownHeight)),
            preserveFinish: false);

        int caretStroke = SeekCaretStroke(strokes, CaretSpot);
        if (IsFocused)
        {
            float caretTop = caretStroke * strokeHeight;
            float caretBottom = caretTop + strokeHeight;
            if (caretTop < Scroll.RollY)
                Scroll.AssignRollY((int)MathF.Floor(caretTop));
            else if (caretBottom > Scroll.RollY + shownHeight)
                Scroll.AssignRollY((int)MathF.Ceiling(caretBottom - shownHeight));
        }

        var (pickLo, pickHi) = SelSpan();
        for (int idx = 0; idx < strokes.Count; ++idx)
        {
            WrappedStroke stroke = strokes[idx];
            float y = Padding + (idx * strokeHeight) - Scroll.RollY;
            if (y + strokeHeight <= Padding || y >= Height - Padding)
                continue;

            int strokeFinish = stroke.Start + stroke.Length;
            int highlightLo = Math.Max(pickLo, stroke.Start);
            int highlightHi = Math.Min(pickHi, strokeFinish);
            if (HasPick && highlightHi > highlightLo)
            {
                float x0 = Padding + GaugeSpan(
                    stroke.Start,
                    highlightLo - stroke.Start);
                float x1 = Padding + GaugeSpan(
                    stroke.Start,
                    highlightHi - stroke.Start);
                cx.SketchPopulate(
                    x0,
                    y,
                    MathF.Max(0f, x1 - x0),
                    strokeHeight,
                    PickTint);
            }

            if (DatTypeface is { } dat)
                cx.PaintStringDat(dat, stroke.Text, Padding, y, PhraseTint, Outline, OutlineColor);
            else if (Font is { } texture)
                cx.SketchString(stroke.Text, Padding, y, PhraseTint, texture);
        }

        if (IsFocused && strokes.Count > 0)
        {
            WrappedStroke stroke = strokes[caretStroke];
            int strokeColumn = Math.Clamp(
                CaretSpot - stroke.Start,
                0,
                stroke.Length);
            float x = Padding + GaugeSpan(stroke.Start, strokeColumn);
            float y = Padding + (caretStroke * strokeHeight) - Scroll.RollY;
            cx.SketchPopulate(x, y, 1f, strokeHeight, PhraseTint);
        }
    }

    private IReadOnlyList<WrappedStroke> AssembleWrappedStrokes(float ceilingWidth)
    {
        List<WrappedStroke> strokes = new List<WrappedStroke>();
        if (_phrase.Length is 0)
        {
            strokes.Add(new WrappedStroke(0, 0, string.Empty));
            return strokes;
        }

        int begin = 0;
        while (begin < _phrase.Length)
        {
            if (_phrase[begin] == '\n')
            {
                strokes.Add(new WrappedStroke(begin, 0, string.Empty));
                ++begin;
                continue;
            }

            int paragraphFinish = _phrase.IndexOf('\n', begin);
            if (paragraphFinish < 0)
                paragraphFinish = _phrase.Length;
            int finish = begin;
            int previousWhitespaceFinish = -1;
            while (finish < paragraphFinish)
            {
                int contenderFinish = finish + 1;
                if (GaugeSpan(begin, contenderFinish - begin) > ceilingWidth
                    && finish > begin)
                    break;
                finish = contenderFinish;
                if (char.IsWhiteSpace(_phrase[finish - 1]))
                    previousWhitespaceFinish = finish;
            }

            if (finish < paragraphFinish && previousWhitespaceFinish > begin)
                finish = previousWhitespaceFinish;
            if (finish == begin)
                ++finish;

            int len = finish - begin;
            strokes.Add(new WrappedStroke(
                begin,
                len,
                _phrase.Substring(begin, len)));
            begin = finish;
            if (begin == paragraphFinish && begin < _phrase.Length)
                ++begin;
        }

        if (_phrase.EndsWith('\n'))
            strokes.Add(new WrappedStroke(_phrase.Length, 0, string.Empty));
        return strokes;
    }

    private int SeekCaretStroke(IReadOnlyList<WrappedStroke> strokes, int caret)
    {
        for (int idx = 0; idx < strokes.Count; ++idx)
        {
            WrappedStroke stroke = strokes[idx];
            int finish = stroke.Start + stroke.Length;
            if (caret < finish || caret == finish && idx == strokes.Count - 1)
                return idx;
            if (caret == finish && idx + 1 < strokes.Count
                && strokes[idx + 1].Start > caret)
                return idx;
        }
        return Math.Max(0, strokes.Count - 1);
    }

    private float GaugeSpan(int begin, int len)
    {
        if (len <= 0) return 0f;
        string val = _phrase.Substring(begin, len);
        return DatTypeface?.MeasureWidth(val)
               ?? Font?.MeasureWidth(val)
               ?? val.Length * 8f;
    }

    private int SmackChar(float ownX, float ownY)
    {
        if (OneStroke)
            return SmackCharX(ownX);
        SecureWrappedStrokesLatest();
        if (_wrappedStrokes.Count is 0)
            return _phrase.Length;

        int strokeOrdinal = Math.Clamp(
            (int)MathF.Floor(
                (ownY - Padding + Scroll.RollY)
                / MathF.Max(1f, _wrappedStrokeHeight)),
            0,
            _wrappedStrokes.Count - 1);
        WrappedStroke stroke = _wrappedStrokes[strokeOrdinal];
        float mark = MathF.Max(0f, ownX - Padding);
        int finest = 0;
        float finestGap = float.MaxValue;
        for (int col = 0; col <= stroke.Length; ++col)
        {
            float gap = MathF.Abs(
                GaugeSpan(stroke.Start, col) - mark);
            if (gap >= finestGap) continue;
            finestGap = gap;
            finest = col;
        }
        return stroke.Start + finest;
    }

    private void BeginRepeat(Silk.NET.Input.Key kdx) { _repeatTag = kdx; _repeatTicker = RepeatDelay; }

    private bool CtrlPinned()
    {
        return Keyboard is not null
        && (Keyboard.IsKeyPressed(Silk.NET.Input.Key.ControlLeft)
            || Keyboard.IsKeyPressed(Silk.NET.Input.Key.ControlRight));
    }

    private bool MovePinned()
    {
        return Keyboard is not null
        && (Keyboard.IsKeyPressed(Silk.NET.Input.Key.ShiftLeft)
            || Keyboard.IsKeyPressed(Silk.NET.Input.Key.ShiftRight));
    }

    public string Text => _phrase;

    public Func<string, string?>? PhraseReplacer { get; set; }

    public bool IsFocused { get; private set; }

    public int CaretSpot { get; private set; }

    public int HistoryTally => _history.Count;

    public override bool ConsumesDatChildren => true;

    private bool HasPick => _selMooring is { } a && a != CaretSpot;
}
