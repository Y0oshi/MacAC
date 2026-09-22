using System.Numerics;
using MacAC.Cockpit.Panels.Chat;
using MacAC.Mechanics.Comms;

namespace MacAC.Client.Shell.Panels;

internal static class CommsTranscriptPainter
{
    private static Vector4 StampTint
    {
        get
        {
            return CanonChatColorTable.TryFetchColor(0x0Cu, out Vector4 grey)
            ? grey
            : new Vector4(0.5f, 0.5f, 0.5f, 1f);
        }
    }

    public const int UpperTranscriptToons = 0x2710;

    public static List<WidgetPhrase.Line> AssembleStrokes(
        IReadOnlyList<StyledLine> detailed,
        float upperW,
        Func<string, float> gauge,
        Func<uint, bool>? admit,
        Vector4 defaultTint,
        Vector4? tagTint = null,
        List<IReadOnlyList<WidgetPhrase.PhraseExec>?>? executionsPerStroke = null,
        List<IReadOnlyList<(int Start, int Length, TextTag Tag)>?>? tagsPerStroke = null)
    {
        List<WidgetPhrase.Line> outcome = new List<WidgetPhrase.Line>(detailed.Count);
        executionsPerStroke?.Clear();
        tagsPerStroke?.Clear();
        if (detailed.Count is 0)
            return outcome;

        Vector4 latestTint = defaultTint;
        var begin = SeekAllowanceBegin(detailed, admit);
        for (int strokeOrdinal = begin.LineIndex; strokeOrdinal < detailed.Count; ++strokeOrdinal)
        {
            StyledLine line = detailed[strokeOrdinal];
            if (admit is not null && !admit(line.LogTextType))
                continue;
            if (strokeOrdinal == begin.LineIndex && begin.CharacterOffset > 0)
                line = SliceStroke(line, begin.CharacterOffset);
            if (CanonChatColorTable.TryFetchColor(line.LogTextType, out Vector4 settled))
                latestTint = settled;
            int huntFrom = 0;
            foreach (string frag in EnclosePhrase(line.Text, upperW, gauge))
            {
                outcome.Add(new WidgetPhrase.Line(frag, latestTint));

                if (executionsPerStroke is null && tagsPerStroke is null)
                    continue;

                if (line.Spans is not { Count: > 0 } spans || frag.Length is 0)
                {
                    executionsPerStroke?.Add(null);
                    tagsPerStroke?.Add(null);
                    continue;
                }

                int at = line.Text.IndexOf(frag, huntFrom, StringComparison.Ordinal);
                if (at < 0)
                {
                    executionsPerStroke?.Add(null);
                    tagsPerStroke?.Add(null);
                    continue;
                }
                huntFrom = at + frag.Length;

                executionsPerStroke?.Add(ExecutionsForFragment(
                    spans,
                    at,
                    frag.Length,
                    latestTint,
                    tagTint ?? latestTint));
                tagsPerStroke?.Add(TaggedSpansForFragment(spans, at, frag.Length));
            }
        }
        return outcome;
    }

    private readonly record struct AllowanceStart(int LineIndex, int CharacterOffset);

    public static IEnumerable<string> EnclosePhrase(string phrase, float upperW, Func<string, float> gauge)
    {
        if (string.IsNullOrEmpty(phrase))
        {
            yield return string.Empty;
            yield break;
        }

        string normalized = phrase.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string segment in normalized.Split('\n'))
        {
            foreach (string frag in EncloseSingleStroke(segment, upperW, gauge))
                yield return frag;
        }
    }

    internal static int LeadStrokeWithinAllowance(
        IReadOnlyList<StyledLine> detailed,
        Func<uint, bool>? admit,
        int allowance = UpperTranscriptToons)
        => SeekAllowanceBegin(detailed, admit, allowance).LineIndex;

    internal static IReadOnlyList<WidgetPhrase.PhraseExec>? ExecutionsForFragment(
        IReadOnlyList<TextSpan> spans,
        int fragmentBegin,
        int fragmentLen,
        Vector4 strokeTint,
        Vector4 tagTint)
    {
        int fragmentFinish = fragmentBegin + fragmentLen;
        List<WidgetPhrase.PhraseExec> executions = new List<WidgetPhrase.PhraseExec>();
        bool sawTag = false;
        int at = 0;

        foreach (TextSpan span in spans)
        {
            int spanBegin = at;
            int spanFinish = at + span.Text.Length;
            at = spanFinish;

            int from = Math.Max(spanBegin, fragmentBegin);
            int to = Math.Min(spanFinish, fragmentFinish);
            if (to <= from)
                continue;

            bool tagged = span.Tag is not null;
            bool stamped = span.Role == SpanRole.Timestamp;
            sawTag |= tagged || stamped;

            Vector4 tint = tagged
                ? tagTint
                : stamped
                    ? StampTint
                    : strokeTint;

            executions.Add(new WidgetPhrase.PhraseExec(
                span.Text.Substring(from - spanBegin, to - from),
                tint));
        }

        return sawTag ? executions : null;
    }

    internal static IReadOnlyList<(int Start, int Length, TextTag Tag)>?
        TaggedSpansForFragment(
            IReadOnlyList<TextSpan> spans,
            int fragmentBegin,
            int fragmentLen)
    {
        int fragmentFinish = fragmentBegin + fragmentLen;
        List<(int Start, int Length, TextTag Tag)>? ranges = null;
        int at = 0;

        foreach (TextSpan span in spans)
        {
            int spanBegin = at;
            int spanFinish = at + span.Text.Length;
            at = spanFinish;

            if (span.Tag is not { } tag)
                continue;

            int from = Math.Max(spanBegin, fragmentBegin);
            int to = Math.Min(spanFinish, fragmentFinish);
            if (to <= from)
                continue;

            (ranges ??= []).Add((from - fragmentBegin, to - from, tag));
        }

        return ranges;
    }

    private static AllowanceStart SeekAllowanceBegin(
        IReadOnlyList<StyledLine> detailed,
        Func<uint, bool>? admit,
        int allowance = UpperTranscriptToons)
    {
        long consumed = 0;
        for (int idx = detailed.Count - 1; idx >= 0; --idx)
        {
            if (admit is not null && !admit(detailed[idx].LogTextType))
                continue;

            long price = detailed[idx].Text.Length + 1L;
            if (consumed + price <= allowance)
            {
                consumed += price;
                continue;
            }

            int onHand = (int)Math.Max(0L, allowance - consumed - 1L);
            if (onHand > 0)
            {
                string phrase = detailed[idx].Text;
                int floorShift = Math.Max(0, phrase.Length - onHand);
                int shift = LeadToonFollowingStrokeBreak(phrase, floorShift);
                if (shift < phrase.Length)
                    return new AllowanceStart(idx, shift);

                if (consumed is 0 && phrase.Length > 0)
                    return new AllowanceStart(idx, floorShift);
            }
            return new AllowanceStart(idx + 1, 0);
        }
        return new AllowanceStart(0, 0);
    }

    private static int LeadToonFollowingStrokeBreak(string phrase, int begin)
    {
        for (int idx = Math.Clamp(begin, 0, phrase.Length); idx < phrase.Length; ++idx)
        {
            if (phrase[idx] is not ('\r' or '\n'))
                continue;
            if (phrase[idx] == '\r' && idx + 1 < phrase.Length && phrase[idx + 1] == '\n')
                ++idx;
            return idx + 1;
        }
        return phrase.Length;
    }

    private static StyledLine SliceStroke(StyledLine stroke, int shift)
    {
        if (shift <= 0)
            return stroke;

        string phrase = stroke.Text[shift..];
        if (stroke.Spans is not { Count: > 0 } spans)
            return stroke with { Text = phrase };

        List<TextSpan> sliced = new List<TextSpan>();
        int at = 0;
        foreach (TextSpan span in spans)
        {
            at = SliceStrokeLoop(at, span, shift, sliced);
        }
        return stroke with { Text = phrase, Spans = sliced };
    }

    private static int SliceStrokeLoop(int at, TextSpan span, int shift, List<TextSpan> sliced)
    {
        int finish = at + span.Text.Length;
        if (finish > shift)
        {
            int from = Math.Max(shift, at) - at;
            sliced.Add(span with { Text = span.Text[from..] });
        }
        at = finish;
        return at;
    }

    private static IEnumerable<string> EncloseSingleStroke(string phrase, float upperW, Func<string, float> gauge)
    {
        if (phrase.Length is 0 || upperW <= 0f || gauge(phrase) <= upperW)
        {
            yield return phrase;
            yield break;
        }

        var stroke = new System.Text.StringBuilder();
        foreach (var word in phrase.Split(' '))
        {
            string sep = stroke.Length > 0 ? " " : string.Empty;
            if (gauge(stroke.ToString() + sep + word) <= upperW)
            {
                stroke.Append(sep).Append(word);
                continue;
            }
            if (stroke.Length > 0 && gauge(word) <= upperW)
            {
                yield return stroke.ToString();            // word fits alone → push to a new line
                stroke.Clear();
                stroke.Append(word);
                continue;
            }
            if (stroke.Length > 0) stroke.Append(' ');
            foreach (char ch in word)
            {
                if (stroke.Length > 0 && gauge(stroke.ToString() + ch) > upperW)
                {
                    yield return stroke.ToString();
                    stroke.Clear();
                }
                stroke.Append(ch);
            }
        }
        if (stroke.Length > 0) yield return stroke.ToString();
    }
}
