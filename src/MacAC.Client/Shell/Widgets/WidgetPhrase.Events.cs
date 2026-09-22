using MacAC.Dat;
using System.Numerics;
using System.Text;
using MacAC.Client.Graphics;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed partial class WidgetPhrase
{
    public Action? OnClick
    {
        get;
        set
        {
            field = value;
            if (value is not null)
                ClickThrough = false;
        }
    }

    public Func<Spot, bool>? OnCharPress { get; set; }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.Click && OnCharPress is not null)
        {
            if (OnCharPress(SmackChar(e.Data1, e.Data2)))
                return true;
        }

        if (e.Type == WidgetEventType.Click && OnClick is not null)
        {
            OnClick();
            return true;
        }
        switch (e.Type)
        {
            case WidgetEventType.Roll:
                {
                    if (!Selectable && !WheelRollTurnedOn) return false;
                    Scroll.RollByStrokes((int)(-e.Data0 * WheelStrokes));
                    return true;
                }

            case WidgetEventType.PointerDown:
                {
                    if (!Selectable) return false;
                    Spot p = SmackChar(e.Data1, e.Data2);
                    _selMooring = p;
                    _selCaret = p;
                    _selecting = true;
                    return true;
                }

            case WidgetEventType.PointerRelocate:
                {
                    if (!Selectable) return false;
                    if (_selecting)
                    {
                        _selCaret = SmackChar(e.Data1, e.Data2);
                        return true;
                    }
                    return false;
                }

            case WidgetEventType.PointerUp:
                {
                    if (!Selectable) return false;
                    _selecting = false;
                    return true;
                }

            case WidgetEventType.TagDown:
                {
                    if (!Selectable) return false;
                    Silk.NET.Input.Key tag = (Silk.NET.Input.Key)e.Data0;
                    bool ctrl = Keyboard is not null
                    && (Keyboard.IsKeyPressed(Silk.NET.Input.Key.ControlLeft)
                        || Keyboard.IsKeyPressed(Silk.NET.Input.Key.ControlRight));
                    if (ctrl && tag == Silk.NET.Input.Key.C)
                    {
                        if (Keyboard is not null)
                        {
                            string sel = SelectedText();
                            if (sel.Length > 0) Keyboard.ClipboardText = sel;
                        }
                        return true;
                    }
                    if (ctrl && tag == Silk.NET.Input.Key.A)
                    {
                        PickAll();
                        return true;
                    }
                    return false;
                }
        }
        return false;
    }

    public bool TrySetCanonPhase(uint phaseIdent)
        => ImposeDatPhase(phaseIdent, propagate: true);

    public static float LimitRoll(float roll, float substanceHeight, float lensHeight)
    {
        float upper = Math.Max(0f, substanceHeight - lensHeight);
        return roll < 0f ? 0f : roll > upper ? upper : roll;
    }

    public override bool HndsPress
    {
        get
        {
            return OnClick is not null
                || OnCharPress is not null
                || WheelRollTurnedOn
                || base.HndsPress;
        }
    }

    public uint ElementId { get; set; }

    public Func<IReadOnlyList<PhraseExec>>? ExecutionsSupplier { get; set; }

    public static float SubstanceShiftX(
        float elemWidth,
        float padding,
        float marginLeft,
        float marginRight,
        float strokeWidth,
        bool centered,
        bool rightAligned)
    {
        float substanceLeft = padding + marginLeft;
        float substanceRight = elemWidth - padding - marginRight;
        if (centered)
            return Math.Max(substanceLeft, substanceLeft + (substanceRight - substanceLeft - strokeWidth) * 0.5f);
        return rightAligned ? Math.Max(substanceLeft, substanceRight - strokeWidth) : substanceLeft;
    }

    public Func<int, IReadOnlyList<PhraseExec>?>? StrokeExecutionsSupplier { get; set; }

    public static float SubstanceBaseY(
        float top,
        float bottom,
        float substanceHeight,
        float upperRoll,
        float rollY,
        ClientVJustify justification,
        bool honorJustification)
    {
        float lensHeight = Math.Max(0f, bottom - top);
        return !honorJustification || substanceHeight > lensHeight
            ? bottom - substanceHeight + (upperRoll - rollY)
            : justification switch
            {
                ClientVJustify.Top => top,
                ClientVJustify.Bottom => bottom - substanceHeight,
                _ => top + (lensHeight - substanceHeight) * 0.5f,
            };
    }

    public BitmapFont? Font { get; set; }

    public WidgetDatFont? DatFont { get; set; }

    public Silk.NET.Input.IKeyboard? Keyboard { get; set; }

    public Vector4? TagTint { get; set; }

    public bool Outline { get; set; }

    public uint BackgroundSprite { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    public float Padding { get; set; }

    public float MarginLeft { get; set; }

    public float MarginRight { get; set; }

    public float MarginTop { get; set; }

    public float MarginBottom { get; set; }

    public bool OneLine { get; set; }

    public bool Selectable
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            ClickThrough = !value;
            AcceptsFocus = value;
            IsEditControl = value;
            CapturesPointerDrag = value;
            if (!value)
            {
                _selecting = false;
                _selMooring = null;
                _selCaret = null;
            }
        }
    }

    public bool Centered { get; set; }

    public bool RightAligned { get; set; }

    public bool HonorVerticalJustification { get; set; }

    public bool WheelRollTurnedOn { get; set; }

    public override bool ConsumesDatChildren
    {
        get
        {
            if (_datDetails is null) return true;
            foreach (WidgetStateInfo phase in _datDetails.States.Values)
                if (phase.PassToChildren)
                    return false;
            return true;
        }
    }

    public uint EngagedCanonPhaseIdent { get; private set; } = WidgetStateInfo.StraightPhaseIdent;

    public override string EngagedCurPhaseLabel => _engagedDatPhaseLabel;

    public string SelectedText()
    {
        return !TryFetchSequencedPick(out var begin, out var finish) ? string.Empty : SelectedText(_previousStrokes, begin, finish);
    }

    public static string SelectedText(IReadOnlyList<Line> strokes, Spot begin, Spot finish)
    {
        if (strokes.Count is 0) return string.Empty;
        (begin, finish) = Order(begin, finish);

        int sl = Math.Clamp(begin.Line, 0, strokes.Count - 1);
        int elem = Math.Clamp(finish.Line, 0, strokes.Count - 1);

        if (sl == elem)
        {
            string t = strokes[sl].Text;
            int c0 = Math.Clamp(begin.Col, 0, t.Length);
            int c1 = Math.Clamp(finish.Col, 0, t.Length);
            return c1 <= c0 ? string.Empty : t[c0..c1];
        }

        StringBuilder builder = new StringBuilder();

        {
            string t = strokes[sl].Text;
            int c0 = Math.Clamp(begin.Col, 0, t.Length);
            builder.Append(t.AsSpan(c0));
        }

        // Whole middle lines
        for (int idx = sl + 1; idx < elem; ++idx)
        {
            builder.Append('\n');
            builder.Append(strokes[idx].Text);
        }

        // Last line: up to end.Col
        {
            builder.Append('\n');
            string t = strokes[elem].Text;
            int c1 = Math.Clamp(finish.Col, 0, t.Length);
            builder.Append(t.AsSpan(0, c1));
        }

        return builder.ToString();
    }

    public static float VShift(float height, float strokeHeight, float padding, ClientVJustify justify)
    {
        return justify switch
        {
            ClientVJustify.Top => padding,
            ClientVJustify.Bottom => height - strokeHeight - padding,
            _ => (height - strokeHeight) * 0.5f,
        };
    }

    public static (Spot start, Spot end) Order(Spot a, Spot b)
    {
        if (a.Line < b.Line || (a.Line == b.Line && a.Col <= b.Col)) return (a, b);
        return (b, a);
    }

    public static IReadOnlyList<string> EncloseWords(
        string phrase,
        Func<string, float> gaugeWidth,
        float maximumWidth)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        ArgumentNullException.ThrowIfNull(gaugeWidth);
        if (maximumWidth <= 0f) throw new ArgumentOutOfRangeException(nameof(maximumWidth));

        List<string> outcome = new List<string>();
        string[] paragraphs = phrase.Replace("\r", string.Empty).Split('\n');
        foreach (string paragraph in paragraphs)
        {
            if (paragraph.Length is 0)
            {
                outcome.Add(string.Empty);
                continue;
            }

            string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            StringBuilder stroke = new StringBuilder();
            foreach (string word in words)
            {
                string contender = stroke.Length is 0 ? word : $"{stroke} {word}";
                if (gaugeWidth(contender) <= maximumWidth)
                {
                    if (stroke.Length is not 0) stroke.Append(' ');
                    stroke.Append(word);
                    continue;
                }

                if (stroke.Length is not 0 && gaugeWidth(word) <= maximumWidth)
                {
                    outcome.Add(stroke.ToString());
                    stroke.Clear();
                    stroke.Append(word);
                    continue;
                }

                for (int idx = 0; idx < word.Length; ++idx)
                {
                    EncloseWordsLoop(idx, stroke, gaugeWidth, word, maximumWidth, outcome);
                }
            }

            outcome.Add(stroke.ToString());
        }

        return outcome;
    }

    private static void EncloseWordsLoop(int idx, StringBuilder stroke, Func<string, float> gaugeWidth, string word, float maximumWidth, List<string> outcome)
    {
        string stem = idx is 0 && stroke.Length is not 0 ? " " : string.Empty;
        if (stroke.Length is not 0
                                && gaugeWidth(stroke + stem + word[idx]) > maximumWidth)
        {
            outcome.Add(stroke.ToString());
            stroke.Clear();
            stem = string.Empty;
        }
        stroke.Append(stem).Append(word[idx]);
    }

    public static int CharOrdinalAt(string phrase, Func<char, float> proceedOf, float x)
    {
        if (string.IsNullOrEmpty(phrase) || x <= 0f) return 0;

        float cur = 0f;
        for (int idx = 0; idx < phrase.Length; ++idx)
        {
            float adv = proceedOf(phrase[idx]);
            float mid = cur + adv * 0.5f;
            if (x < mid) return idx;
            cur += adv;
        }
        return phrase.Length;
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (BackgroundSprite is not 0 && SpriteResolve is { } sr)
        {
            var (bmp, tw, th) = sr(BackgroundSprite);
            if (bmp is not 0 && tw is not 0 && th is not 0)
                cx.SketchSprite(bmp, 0, 0, Width, Height, 0, 0, Width / tw, Height / th, Vector4.One);
        }

        // Background must draw UNDER the transcript text. DrawStringDat emits into the sprite bucket which
        // flushes BEFORE rects, so a DrawRect background would wash over the text.
        cx.SketchPopulate(0, 0, Width, Height, BackgroundColor);

        if (!PaintPhraseFollowingDescendants)
            PaintPhrase(cx);
    }

    protected override void OnPaintFollowingDescendants(WidgetRenderScope cx)
    {
        if (PaintPhraseFollowingDescendants)
            PaintPhrase(cx);
    }

    internal static bool ExecutionsFitStroke(IReadOnlyList<PhraseExec> executions, string stroke)
    {
        int at = 0;
        for (int idx = 0; idx < executions.Count; ++idx)
        {
            string phrase = executions[idx].Text;
            if (at + phrase.Length > stroke.Length
                || string.CompareOrdinal(stroke, at, phrase, 0, phrase.Length) is not 0)

                return false;
            at += phrase.Length;
        }
        return at == stroke.Length;
    }

    internal static bool StrokeIntersectsViewRect(
        float strokeTop,
        float strokeHeight,
        float viewRectTop,
        float viewRectBottom)
    {
        return strokeHeight > 0f
                && strokeTop < viewRectBottom
                && strokeTop + strokeHeight > viewRectTop;
    }

    internal void AssignAuthoredPhaseTexts(Dictionary<uint, string> texts)
        => _authoredPhaseTexts = texts;

    internal void ConfigureDatPhase(ElemDetails details)
    {
        _datDetails = details;
        _honorDatVerticalJustification = true;
        PaintPhraseFollowingDescendants = false;
        foreach (WidgetStateInfo phase in details.States.Values)
        {
            if (!phase.PassToChildren) continue;
            PaintPhraseFollowingDescendants = true;
            break;
        }
        EngagedCanonPhaseIdent = details.NetDefaultPhaseIdent();
        ImposeDatPhase(EngagedCanonPhaseIdent, propagate: false);
    }

    private bool ImposeDatPhase(uint phaseIdent, bool propagate)
    {
        if (_datDetails is null) return false;

        WidgetStateInfo? phase = null;
        string phaseLabel;
        if (phaseIdent == WidgetStateInfo.StraightPhaseIdent)
        {
            if (!_datDetails.States.TryGetValue(phaseIdent, out phase)
                && !_datDetails.StateMedia.ContainsKey(""))
                return false;
            phaseLabel = "";
        }
        else if (_datDetails.States.TryGetValue(phaseIdent, out phase))
        {
            phaseLabel = phase.Name;
        }
        else
        {
            phaseLabel = WidgetButtonStateMachine.PhaseMoniker(phaseIdent);
            if (string.IsNullOrEmpty(phaseLabel))
                phaseLabel = CanonWidgetStateIds.PhaseLabel(phaseIdent);
            if (string.IsNullOrEmpty(phaseLabel)
                || !_datDetails.StateMedia.ContainsKey(phaseLabel))
                return false;
        }

        EngagedCanonPhaseIdent = phaseIdent;
        _engagedDatPhaseLabel = phaseLabel;
        BackgroundSprite = _datDetails.StateMedia.TryGetValue(phaseLabel, out var media)
            ? media.File
            : _datDetails.StateMedia.TryGetValue("", out var straight) ? straight.File : 0u;

        if (_datDetails.TryFetchNetProp(0x1Bu, out WidgetPropertyValue tint, phaseIdent)
            && TryTint(tint, out Vector4 settledTint))
            DefaultTint = settledTint;

        if (phase is not null
            && phase.Properties.Values.TryGetValue(0x3Bu, out var invisibleProp)
            && invisibleProp.Kind == WidgetPropertyKind.Bool)
            Visible = !invisibleProp.BoolValue;

        if (_authoredPhaseTexts is { } phaseTexts
            && phaseTexts.TryGetValue(phaseIdent, out string? authoredStroke))
        {
            StrokesSupplier = () => [new Line(authoredStroke, DefaultTint)];
        }

        if (propagate && phase?.PassToChildren == true)
        {
            foreach (WidgetElem descendant in Children)
                if (descendant is IWidgetDatStateful stateful)
                    stateful.TrySetCanonPhase(phaseIdent);
        }

        return true;
    }

    private static bool TryTint(WidgetPropertyValue prop, out Vector4 tint)
    {
        WidgetPropertyValue? val = prop.Kind == WidgetPropertyKind.Color
            ? prop
            : prop.Kind == WidgetPropertyKind.Array
                && prop.ArrayValue.Count > 0
                && prop.ArrayValue[0].Kind == WidgetPropertyKind.Color
                    ? prop.ArrayValue[0]
                    : null;
        if (val is null)
        {
            tint = default;
            return false;
        }

        var c = val.ColorValue;
        float alpha = c.Alpha is 0 ? 1f : c.Alpha / 255f;
        tint = new Vector4(c.Red / 255f, c.Green / 255f, c.Blue / 255f, alpha);
        return true;
    }

    private float HorizontalShift(string phrase, WidgetDatFont? datTypeface, BitmapFont? bitmapTypeface)
    {
        float width = datTypeface is not null
            ? datTypeface.MeasureWidth(phrase)
            : bitmapTypeface?.MeasureWidth(phrase) ?? 0f;
        return SubstanceShiftX(Width, Padding, MarginLeft, MarginRight, width, Centered, RightAligned);
    }

    private void PickAll()
    {
        IReadOnlyList<Line> strokes = _previousStrokes;
        if (strokes.Count is 0)
        {
            _selMooring = _selCaret = null;
            return;
        }
        int previous = strokes.Count - 1;
        _selMooring = new Spot(0, 0);
        _selCaret = new Spot(previous, strokes[previous].Text.Length);
    }

    private bool TryFetchSequencedPick(out Spot begin, out Spot finish)
    {
        begin = default; finish = default;
        if (_selMooring is not { } a || _selCaret is not { } c) return false;
        (begin, finish) = Order(a, c);
        return !(begin.Line == finish.Line && begin.Col == finish.Col);
    }

    private Spot SmackChar(float ownX, float ownY)
    {
        IReadOnlyList<Line> strokes = _previousStrokes;
        if (strokes.Count is 0) return new Spot(0, 0);

        float lh = _previousStrokeHeight <= 0f ? 16f : _previousStrokeHeight;
        int stroke = (int)MathF.Floor((ownY - _previousBaseY) / lh);
        stroke = Math.Clamp(stroke, 0, strokes.Count - 1);

        string phrase = strokes[stroke].Text;
        float strokeX = HorizontalShift(phrase, _previousDatTypeface, _previousTypeface);
        int col = _previousDatTypeface is { } font
            ? CharOrdinalAt(phrase, ch => font.TryGetGlyph(ch, out GlyphDesc desc) ? WidgetDatFont.GlyphProceed(desc) : 0f,
                          ownX - strokeX)
            : (_previousTypeface is { } bf
                ? CharOrdinalAt(phrase, ch => bf.TryFetchGlyph(ch, out var glyph) ? glyph.Advance : 0f,
                              ownX - strokeX)
                : 0);
        return new Spot(stroke, col);
    }
}
