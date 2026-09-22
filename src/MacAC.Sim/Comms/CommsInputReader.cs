namespace MacAC.Sim.Comms;

public static class CommsInputReader
{
    public readonly record struct DecodedFeed(CommsChannelKind Channel, string? TargetName, string Text);

    private static readonly string[] SayVerbs = ["/say", "/s"];
    private static readonly string[] TellVerbs = ["/tell", "/t", "/send", "/whisper", "/w"];
    private static readonly string[] ReplyVerbs = ["/reply", "/r", "/rp"];
    private static readonly string[] RetellVerbs = ["/retell", "/rt"];

    private static readonly (string Verb, CommsChannelKind Channel)[] LaneVerbs =
    [
        ("/general", CommsChannelKind.General), ("/cg", CommsChannelKind.General),
        ("/f", CommsChannelKind.Fellowship), ("/fellow", CommsChannelKind.Fellowship), ("/fellows", CommsChannelKind.Fellowship),
        ("/fellowship", CommsChannelKind.Fellowship), ("/g", CommsChannelKind.Fellowship), ("/group", CommsChannelKind.Fellowship), ("/party", CommsChannelKind.Fellowship),
        ("/a", CommsChannelKind.Allegiance), ("/guild", CommsChannelKind.Allegiance), ("/gu", CommsChannelKind.Allegiance),
        ("/ab", CommsChannelKind.AllegianceBroadcast),
        ("/m", CommsChannelKind.Monarch), ("/monarch", CommsChannelKind.Monarch),
        ("/p", CommsChannelKind.Patron), ("/patron", CommsChannelKind.Patron),
        ("/v", CommsChannelKind.Vassals), ("/vassal", CommsChannelKind.Vassals), ("/vassals", CommsChannelKind.Vassals),
        ("/c", CommsChannelKind.CoVassals), ("/covassal", CommsChannelKind.CoVassals), ("/covassals", CommsChannelKind.CoVassals), ("/co-vassals", CommsChannelKind.CoVassals),
        ("/lfg", CommsChannelKind.Lfg), ("/clfg", CommsChannelKind.Lfg),
        ("/trade", CommsChannelKind.Trade), ("/ct", CommsChannelKind.Trade),
        ("/crp", CommsChannelKind.Roleplay), ("/roleplay", CommsChannelKind.Roleplay),
        ("/society", CommsChannelKind.Society), ("/soc", CommsChannelKind.Society),
        ("/olthoi", CommsChannelKind.Olthoi), ("/o", CommsChannelKind.Olthoi),
    ];

    // Channels that still go over the legacy channel-id path and therefore refuse an empty message
    private static readonly HashSet<CommsChannelKind> LegacyLanes =
    [
        CommsChannelKind.Fellowship, CommsChannelKind.Allegiance, CommsChannelKind.AllegianceBroadcast,
        CommsChannelKind.Vassals, CommsChannelKind.Patron, CommsChannelKind.Monarch, CommsChannelKind.CoVassals,
    ];

    private static readonly HashSet<string> AllVerbs = new(
        [.. SayVerbs, .. TellVerbs, .. ReplyVerbs, .. RetellVerbs, .. LaneVerbs.Select(static v => v.Verb)],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> KnownVerbs => AllVerbs;

    public static bool IsRecognizedVerb(string verb) => AllVerbs.Contains(verb.TrimEnd(','));

    public static string FetchVerbTicket(string directive) => Head(directive).Verb;

    // A line split at its first whitespace: the verb (comma stripped) and what follows
    private readonly record struct Split(string Verb, string? Rest)
    {
        public string Token => Verb.TrimEnd(',');
        public bool Is(string[] verbs) => Array.IndexOf(verbs, Token) >= 0;
    }

    public static bool IsBareRegisteredLaneVerb(string trimmed)
    {
        foreach ((string verb, CommsChannelKind lane) in LaneVerbs)
        {
            if (LegacyLanes.Contains(lane) && Bare(trimmed, [verb]))
                return true;
        }
        return false;
    }

    public static bool IsReplyAbsentPreviousTeller(string trimmed, string? previousTellSender)
    {
        return string.IsNullOrEmpty(previousTellSender) && Message(trimmed, ReplyVerbs, out _);
    }

    public static DecodedFeed? Parse(string raw, CommsChannelKind defaultLane, string? previousTellSender, string? previousOutgoingTellMark = null, string? defaultTellMark = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        string stroke = raw.Trim();

        if (stroke.StartsWith('@'))
        {
            string slashed = "/" + stroke[1..];
            return IsRecognizedVerb(Head(slashed).Verb)
                ? Parse(slashed, defaultLane, previousTellSender, previousOutgoingTellMark, defaultTellMark)
                : new DecodedFeed(CommsChannelKind.Say, null, stroke);
        }

        if (Message(stroke, SayVerbs, out string say))
            return new DecodedFeed(CommsChannelKind.Say, null, say);
        if (Bare(stroke, SayVerbs))
            return null;

        if (Targeted(stroke, TellVerbs, out string tellMark, out string tell))
            return new DecodedFeed(CommsChannelKind.Tell, tellMark, tell);
        if (Bare(stroke, TellVerbs) || VerbAndOneTicket(stroke, TellVerbs))
            return null;

        if (Message(stroke, ReplyVerbs, out string reply))
            return string.IsNullOrEmpty(previousTellSender) ? null : new DecodedFeed(CommsChannelKind.Tell, previousTellSender, reply);
        if (Bare(stroke, ReplyVerbs))
            return null;

        if (Message(stroke, RetellVerbs, out string retell))
            return string.IsNullOrEmpty(previousOutgoingTellMark) ? null : new DecodedFeed(CommsChannelKind.Tell, previousOutgoingTellMark, retell);
        if (Bare(stroke, RetellVerbs))
            return null;

        foreach ((string verb, CommsChannelKind lane) in LaneVerbs)
        {
            if (Message(stroke, [verb], out string phrase))
                return new DecodedFeed(lane, null, phrase);
            if (Bare(stroke, [verb]))
                return null;
        }

        // Any other slash verb is spoken as an @command.
        if (stroke.Length > 1 && stroke[0] == '/' && char.IsLetter(stroke[1]))
            return new DecodedFeed(CommsChannelKind.Say, null, "@" + stroke[1..]);

        if (defaultLane == CommsChannelKind.Tell)
            return string.IsNullOrEmpty(defaultTellMark) ? null : new DecodedFeed(CommsChannelKind.Tell, defaultTellMark, stroke);

        return new DecodedFeed(defaultLane, null, stroke);
    }

    private static Split Head(string directive)
    {
        for (int idx = 0; idx < directive.Length; ++idx)
        {
            if (char.IsWhiteSpace(directive[idx]))
                return new Split(directive[..idx], directive[(idx + 1)..].TrimStart());
        }
        return new Split(directive, null);
    }

    private static bool Message(string directive, string[] verbs, out string msg)
    {
        Split split = Head(directive);
        msg = split.Rest ?? string.Empty;
        return split.Rest is not null && split.Is(verbs) && msg.Length > 0;
    }

    private static bool Bare(string directive, string[] verbs) => Array.IndexOf(verbs, directive.TrimEnd(',')) >= 0;

    private static bool VerbAndOneTicket(string directive, string[] verbs)
    {
        Split split = Head(directive);
        if (split.Rest is null || !split.Is(verbs))
            return false;
        return split.Rest.Length is 0 || Head(split.Rest).Rest is null;
    }

    // "/tell target, message" or "/tell target message"; the target's trailing punctuation is dropped
    private static bool Targeted(string directive, string[] verbs, out string mark, out string msg)
    {
        mark = string.Empty;
        msg = string.Empty;
        Split split = Head(directive);
        if (split.Rest is null || !split.Is(verbs) || split.Rest.Length is 0)
            return false;

        int comma = split.Rest.IndexOf(',');
        if (comma < 0)
        {
            Split t = Head(split.Rest);
            if (t.Rest is null)
                return false; // target only, no message
            mark = t.Verb.TrimEnd(',', ';', ':', '.', '!', '?');
            msg = t.Rest;
        }
        else
        {
            mark = split.Rest[..comma].TrimEnd();
            msg = split.Rest[(comma + 1)..].TrimStart();
        }
        return mark.Length is not 0 && msg.Length is not 0;
    }
}
