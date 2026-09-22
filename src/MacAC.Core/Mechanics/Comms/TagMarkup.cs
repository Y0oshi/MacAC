using System.Globalization;

namespace MacAC.Mechanics.Comms;

/// <summary>A <c>&lt;TYPE:FORMAT:DATA&gt;</c> marker embedded in server text.</summary>
public readonly record struct TextTag(string Type, string Format, string Data)
{
    /// <summary>An IIDString payload is "objectId:name".</summary>
    public bool TryFetchIidString(out uint objectIdent, out string label)
    {
        objectIdent = 0;
        label = string.Empty;
        if (!string.Equals(Format, "IIDString", StringComparison.Ordinal))
            return false;

        int colon = Data.IndexOf(':');
        if (colon <= 0 || colon == Data.Length - 1)
            return false;
        if (!uint.TryParse(Data.AsSpan(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out objectIdent))
            return false;

        label = Data[(colon + 1)..];
        return true;
    }
}

public enum SpanRole
{
    Body,
    Timestamp,
}

public readonly record struct TextSpan(
    string Text,
    TextTag? Tag,
    SpanRole Role = SpanRole.Body);

public static class TagMarkup
{
    public static IReadOnlyList<TextSpan> Parse(string? phrase)
    {
        if (string.IsNullOrEmpty(phrase))
            return [];
        if (!phrase.Contains('<'))
            return [new TextSpan(phrase, null)];

        List<TextSpan> spans = new List<TextSpan>();
        TextTag? latest = null;
        int execBegin = 0;

        for (int at = phrase.IndexOf('<'); at >= 0; at = phrase.IndexOf('<', at + 1))
        {
            int finish = phrase.IndexOf('>', at + 1);
            if (finish < 0)
                break; // unterminated marker: the rest stays literal

            if (at > execBegin)
                spans.Add(new TextSpan(phrase[execBegin..at], latest));

            latest = ScanTag(phrase.AsSpan(at + 1, finish - at - 1));
            execBegin = finish + 1;
            at = finish;
        }

        if (execBegin < phrase.Length)
            spans.Add(new TextSpan(phrase[execBegin..], latest));
        return spans;
    }

    private static TextTag? ScanTag(ReadOnlySpan<char> interior)
    {
        int lead = interior.IndexOf(':');
        if (lead <= 0 || lead == interior.Length - 1)
            return null;

        var rest = interior[(lead + 1)..];
        int second = rest.IndexOf(':');
        return second < 0
            ? new TextTag(interior[..lead].ToString(), rest.ToString(), string.Empty)
            : new TextTag(interior[..lead].ToString(), rest[..second].ToString(), rest[(second + 1)..].ToString());
    }
}
