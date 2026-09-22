using System.Numerics;

namespace MacAC.Client.Shell.Panels;

internal static class DatRichPhrase
{
    public readonly record struct Piece(string? Text, Vector4 Color);

    public static IReadOnlyList<WidgetPhrase.Line> Compose(
        WidgetPhrase mark,
        IReadOnlyList<Piece> segments)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ArgumentNullException.ThrowIfNull(segments);

        List<WidgetPhrase.Line> strokes = new List<WidgetPhrase.Line>();
        float ceilingWidth = MathF.Max(
            1f,
            mark.Width - (mark.Padding + mark.MarginLeft) - (mark.Padding + mark.MarginRight));
        Func<string, float> gauge = mark.DatFont is { } typeface
            ? typeface.MeasureWidth
            : static val => val.Length * 8f;

        foreach (Piece segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Text))
                continue;

            foreach (string wrapped in WidgetPhrase.EncloseWords(segment.Text, gauge, ceilingWidth))
                strokes.Add(new WidgetPhrase.Line(wrapped, segment.Color));
        }

        return strokes;
    }

    public static Vector4 SwatchTint(WidgetPhrase mark, int ordinal, Vector4 backup)
    {
        return ordinal >= 0 && ordinal < mark.TypefaceTintSwatch.Count
            ? mark.TypefaceTintSwatch[ordinal]
            : backup;
    }
}
