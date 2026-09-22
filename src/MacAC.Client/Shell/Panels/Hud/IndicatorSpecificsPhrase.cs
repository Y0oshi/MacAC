namespace MacAC.Client.Shell.Panels;

internal static class IndicatorSpecificsPhrase
{
    public static IReadOnlyList<WidgetPhrase.Line> Shape(WidgetPhrase mark, string phrase)
    {
        ArgumentNullException.ThrowIfNull(mark);
        phrase ??= string.Empty;

        float upperWidth = Math.Max(1f, mark.Width - (2f * mark.Padding));
        float Gauge(string val)
            => mark.DatFont?.MeasureWidth(val)
               ?? mark.Font?.MeasureWidth(val)
               ?? val.Length * 8f;

        List<WidgetPhrase.Line> strokes = new List<WidgetPhrase.Line>();
        foreach (string paragraph in phrase.Split('\n'))
        {
            if (paragraph.Length is 0)
            {
                strokes.Add(new WidgetPhrase.Line(string.Empty, mark.DefaultTint));
                continue;
            }

            foreach (string stroke in CommsTranscriptPainter.EnclosePhrase(paragraph, upperWidth, Gauge))
                strokes.Add(new WidgetPhrase.Line(stroke, mark.DefaultTint));
        }
        return strokes;
    }
}
