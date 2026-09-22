using System.Numerics;
using MacAC.Client.Graphics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetPhrase
{
    internal bool PaintPhraseFollowingDescendants { get; private set; }

    private void PaintPhrase(WidgetRenderScope cx) => PaintClippedPhrase(cx);

    private void PaintClippedPhrase(WidgetRenderScope ctx)
    {
        if (OneLine && ExecutionsSupplier is { } executionsSupplier)
        {
            PaintSingleStrokeExecutions(ctx, executionsSupplier());
            return;
        }

        if (OneLine && Centered)
        {
            IReadOnlyList<Line> cStrokes = StrokesSupplier();
            if (cStrokes.Count is 0) return;
            Line line0 = cStrokes[0];
            if (DatFont is { } cdf)
            {
                float cx = (Width - cdf.MeasureWidth(line0.Text)) * 0.5f;
                float cy = VShift(Height, cdf.LineHeight, Padding, VerticalJustify);
                ctx.PaintStringDat(cdf, line0.Text, cx, cy, line0.Color, Outline, OutlineColor);
            }
            else if ((Font ?? ctx.DefaultFont) is { } cbf)
            {
                float cx = (Width - cbf.MeasureWidth(line0.Text)) * 0.5f;
                float cy = VShift(Height, cbf.LineHeight, Padding, VerticalJustify);
                ctx.SketchString(line0.Text, cx, cy, line0.Color, cbf);
            }
            return;
        }

        if (OneLine && RightAligned)
        {
            IReadOnlyList<Line> rStrokes = StrokesSupplier();
            if (rStrokes.Count is 0) return;
            Line line0 = rStrokes[0];
            if (DatFont is { } rdf)
            {
                float rx = Width - rdf.MeasureWidth(line0.Text) - Padding;
                float ry = VShift(Height, rdf.LineHeight, Padding, VerticalJustify);
                ctx.PaintStringDat(rdf, line0.Text, rx, ry, line0.Color, Outline, OutlineColor);
            }
            else if ((Font ?? ctx.DefaultFont) is { } rbf)
            {
                float rx = Width - rbf.MeasureWidth(line0.Text) - Padding;
                float ry = VShift(Height, rbf.LineHeight, Padding, VerticalJustify);
                ctx.SketchString(line0.Text, rx, ry, line0.Color, rbf);
            }
            return;
        }

        if (OneLine)
        {
            IReadOnlyList<Line> singleStrokes = StrokesSupplier();
            if (singleStrokes.Count is 0) return;
            Line line0 = singleStrokes[0];
            if (DatFont is { } datSingle)
            {
                float y = VShift(Height, datSingle.LineHeight, Padding, VerticalJustify);
                ctx.PaintStringDat(datSingle, line0.Text, Padding, y, line0.Color, Outline, OutlineColor);
            }
            else if ((Font ?? ctx.DefaultFont) is { } bitmapSingle)
            {
                float y = VShift(Height, bitmapSingle.LineHeight, Padding, VerticalJustify);
                ctx.SketchString(line0.Text, Padding, y, line0.Color, bitmapSingle);
            }
            return;
        }

        var datTypeface = DatFont;
        var bitmapTypeface = datTypeface is null ? (Font ?? ctx.DefaultFont) : null;
        if (datTypeface is null && bitmapTypeface is null) return;

        IReadOnlyList<Line> strokes = StrokesSupplier();

        _previousStrokes = strokes;
        _previousDatTypeface = datTypeface;
        _previousTypeface = bitmapTypeface;
        _previousStrokeHeight = datTypeface is not null ? datTypeface.LineHeight : bitmapTypeface!.LineHeight;
        _previousPadding = Padding;

        if (strokes.Count is 0) return;

        float lh = _previousStrokeHeight;
        float top = Padding + MarginTop, bottom = Height - Padding - MarginBottom;
        float interiorH = bottom - top;
        float substanceH = strokes.Count * lh;

        Scroll.LineHeight = (int)MathF.Round(lh);
        Scroll.AssignExtents(
            (int)MathF.Ceiling(substanceH),
            (int)MathF.Floor(interiorH),
            preserveFinish: PreserveFinishOnArrangement);

        float baseY = SubstanceBaseY(
            top,
            bottom,
            substanceH,
            Scroll.UpperRoll,
            Scroll.RollY,
            VerticalJustify,
            _honorDatVerticalJustification || HonorVerticalJustification);
        _previousBaseY = baseY;

        bool hasSel = TryFetchSequencedPick(out Spot selBegin, out Spot selFinish);

        List<(string Text, float X, float Y, Vector4 Color)>? datStrokes = null;

        for (int idx = 0; idx < strokes.Count; ++idx)
        {
            float y = baseY + idx * lh;
            if (!StrokeIntersectsViewRect(y, lh, top, bottom)) continue;

            string phrase = strokes[idx].Text;
            float strokeX = HorizontalShift(phrase, datTypeface, bitmapTypeface);

            if (hasSel && idx >= selBegin.Line && idx <= selFinish.Line)
            {
                int c0 = idx == selBegin.Line ? selBegin.Col : 0;
                int c1 = idx == selFinish.Line ? selFinish.Col : phrase.Length;
                c0 = Math.Clamp(c0, 0, phrase.Length);
                c1 = Math.Clamp(c1, 0, phrase.Length);
                if (c1 > c0)
                {
                    float hx, hw;
                    if (datTypeface is not null)
                    {
                        hx = strokeX + datTypeface.MeasureWidth(phrase[..c0]);
                        hw = datTypeface.MeasureWidth(phrase[c0..c1]);
                    }
                    else
                    {
                        hx = strokeX + bitmapTypeface!.MeasureWidth(phrase[..c0]);
                        hw = bitmapTypeface.MeasureWidth(phrase[c0..c1]);
                    }
                    ctx.SketchPopulate(hx, y, hw, lh, PickColor);
                }
            }

            var executions = StrokeExecutionsSupplier?.Invoke(idx);
            if (executions is { Count: > 0 } && !ExecutionsFitStroke(executions, phrase))
                executions = null;   // never draw text that disagrees with what we select

            if (datTypeface is not null)
            {
                datStrokes ??= [];
                if (executions is { Count: > 0 })
                {
                    foreach (var placed in ArrangementExecutions(executions, strokeX, datTypeface.MeasureWidth))
                        datStrokes.Add((placed.Text, placed.X, y, placed.Color));
                }
                else
                {
                    datStrokes.Add((phrase, strokeX, y, strokes[idx].Color));
                }
            }
            else if (executions is { Count: > 0 })
            {
                foreach (var placed in ArrangementExecutions(executions, strokeX, bitmapTypeface!.MeasureWidth))
                    ctx.SketchString(placed.Text, placed.X, y, placed.Color, bitmapTypeface);
            }
            else
            {
                ctx.SketchString(phrase, strokeX, y, strokes[idx].Color, bitmapTypeface);
            }
        }

        if (datStrokes is not null)
        {
            if (Outline)
                foreach (var stroke in datStrokes)
                    ctx.PaintStringDatPass(datTypeface!, stroke.Text, stroke.X, stroke.Y, OutlineColor, isOutlinePass: true);
            foreach (var stroke in datStrokes)
                ctx.PaintStringDatPass(datTypeface!, stroke.Text, stroke.X, stroke.Y, stroke.Color, isOutlinePass: false);
        }
    }

    private void PaintSingleStrokeExecutions(
        WidgetRenderScope cx,
        IReadOnlyList<PhraseExec> executions)
    {
        if (executions.Count is 0) return;

        var datTypeface = DatFont;
        BitmapFont? bitmapTypeface = datTypeface is null
            ? Font ?? cx.DefaultFont
            : null;
        if (datTypeface is null && bitmapTypeface is null) return;

        float sumWidth = 0f;
        foreach (PhraseExec exec in executions)
        {
            sumWidth += datTypeface is not null
                ? datTypeface.MeasureWidth(exec.Text)
                : bitmapTypeface!.MeasureWidth(exec.Text);
        }

        float x = Centered
            ? Math.Max(Padding, (Width - sumWidth) * 0.5f)
            : RightAligned
                ? Math.Max(Padding, Width - Padding - sumWidth)
                : Padding;
        float strokeHeight = datTypeface?.LineHeight ?? bitmapTypeface!.LineHeight;
        float y = VShift(
            Height,
            strokeHeight,
            Padding,
            VerticalJustify);

        if (datTypeface is not null)
        {
            var execGeo = new List<(string Text, float X, Vector4 Color)>();
            float penX = x;
            foreach (PhraseExec exec in executions)
            {
                if (exec.Text.Length is 0) continue;
                execGeo.Add((exec.Text, penX, exec.Color));
                penX += datTypeface.MeasureWidth(exec.Text);
            }

            if (Outline)
                foreach (var exec in execGeo)
                    cx.PaintStringDatPass(datTypeface, exec.Text, exec.X, y, OutlineColor, isOutlinePass: true);
            foreach (var exec in execGeo)
                cx.PaintStringDatPass(datTypeface, exec.Text, exec.X, y, exec.Color, isOutlinePass: false);
        }
        else
        {
            foreach (PhraseExec exec in executions)
            {
                if (exec.Text.Length is 0) continue;
                cx.SketchString(exec.Text, x, y, exec.Color, bitmapTypeface);
                x += bitmapTypeface!.MeasureWidth(exec.Text);
            }
        }
    }
}
