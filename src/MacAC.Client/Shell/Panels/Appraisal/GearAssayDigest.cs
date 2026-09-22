using System.Numerics;
using System.Text;

namespace MacAC.Client.Shell.Panels;

public enum GearAssayFontStyle
{
    Normal = 0,
    Beneficial = 1,
    Detrimental = 2,
}

public enum GearAssaySeparator
{
    None,
    Line,
    Paragraph,
}

public readonly record struct GearAssayFragment(
    string Text,
    GearAssaySeparator Separator,
    GearAssayFontStyle Style);

public sealed class GearAssayDigest
{
    public static GearAssayDigest Empty { get; } = new([]);

    public GearAssayDigest(IReadOnlyList<GearAssayFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        Fragments = fragments;
    }

    public IReadOnlyList<GearAssayFragment> Fragments { get; }
    public bool IsEmpty => Fragments.Count is 0;

    public override string ToString()
    {
        StringBuilder phrase = new StringBuilder();
        foreach (GearAssayFragment fragment in Fragments)
        {
            if (phrase.Length is not 0)
            {
                phrase.Append(fragment.Separator == GearAssaySeparator.Paragraph
                    ? "\n\n"
                    : "\n");
            }
            phrase.Append(fragment.Text);
        }
        return phrase.ToString();
    }
}

internal sealed class GearAssayDigestAssembler
{
    private readonly List<GearAssayFragment> _fragments = [];

    public void Line(
        string val,
        GearAssayFontStyle styling = GearAssayFontStyle.Normal)
        => Add(val, sameParagraph: true, styling);

    public void Paragraph(
        string val,
        GearAssayFontStyle styling = GearAssayFontStyle.Normal)
        => Add(val, sameParagraph: false, styling);

    public void BlankStroke(
        GearAssayFontStyle styling = GearAssayFontStyle.Normal)
    {
        if (_fragments.Count is 0)
            return;

        _fragments.Add(new GearAssayFragment(
            string.Empty,
            GearAssaySeparator.Line,
            styling));
    }

    public GearAssayDigest Build()
    {
        return _fragments.Count is 0
                ? GearAssayDigest.Empty
                : new GearAssayDigest(_fragments.ToArray());
    }

    private void Add(
        string val,
        bool sameParagraph,
        GearAssayFontStyle styling)
    {
        if (string.IsNullOrWhiteSpace(val))
            return;

        _fragments.Add(new GearAssayFragment(
            val,
            _fragments.Count is 0
                ? GearAssaySeparator.None
                : sameParagraph
                    ? GearAssaySeparator.Line
                    : GearAssaySeparator.Paragraph,
            styling));
    }
}

internal static class GearAssayTextArrangement
{
    public static IReadOnlyList<WidgetPhrase.Line> Shape(
        WidgetPhrase mark,
        GearAssayDigest dossier)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ArgumentNullException.ThrowIfNull(dossier);

        float upperWidth = Math.Max(1f, mark.Width - (2f * mark.Padding));
        float Gauge(string val)
            => mark.DatFont?.MeasureWidth(val)
               ?? mark.Font?.MeasureWidth(val)
               ?? val.Length * 8f;

        List<WidgetPhrase.Line> strokes = new List<WidgetPhrase.Line>();
        foreach (GearAssayFragment fragment in dossier.Fragments)
        {
            if (strokes.Count is not 0
                && fragment.Separator == GearAssaySeparator.Paragraph)
            {
                strokes.Add(new WidgetPhrase.Line(string.Empty, LocateTint(mark, fragment.Style)));
            }

            Vector4 tint = LocateTint(mark, fragment.Style);
            string normalized = fragment.Text.Replace(
                "\\n",
                "\n",
                StringComparison.Ordinal);
            foreach (string logicalStroke in normalized.Split('\n'))
            {
                if (logicalStroke.Length is 0)
                {
                    strokes.Add(new WidgetPhrase.Line(string.Empty, tint));
                    continue;
                }

                foreach (string wrapped in CommsTranscriptPainter.EnclosePhrase(
                             logicalStroke,
                             upperWidth,
                             Gauge))
                {
                    strokes.Add(new WidgetPhrase.Line(wrapped, tint));
                }
            }
        }

        return strokes;
    }

    private static Vector4 LocateTint(
        WidgetPhrase mark,
        GearAssayFontStyle styling)
    {
        int ordinal = (int)styling;
        return ordinal >= 0 && ordinal < mark.TypefaceTintSwatch.Count
            ? mark.TypefaceTintSwatch[ordinal]
            : mark.DefaultTint;
    }
}
