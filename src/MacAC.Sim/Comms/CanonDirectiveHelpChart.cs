using System.Collections.Frozen;
using MacAC.Mechanics.Comms;

namespace MacAC.Sim.Comms;

public static partial class CanonDirectiveHelpChart
{
    // One help paragraph and every verb spelling that reaches it
    private readonly record struct SimEntry(string Text, params string[] Verbs);

    private static readonly (CanonLogTextType Type, string Name)[] SquelchLegalLanes =
    [
        (CanonLogTextType.Speech, "Speech"),
        (CanonLogTextType.Tell, "Tell"),
        (CanonLogTextType.Combat, "Combat"),
        (CanonLogTextType.Magic, "Magic"),
        (CanonLogTextType.Emote, "Emote"),
        (CanonLogTextType.Appraisal, "Appraisal"),
        (CanonLogTextType.Spellcasting, "Spellcasting"),
        (CanonLogTextType.Allegiance, "Allegiance"),
        (CanonLogTextType.Fellowship, "Fellowship"),
        (CanonLogTextType.CombatEnemy, "Combat_Enemy"),
        (CanonLogTextType.CombatSelf, "Combat_Self"),
        (CanonLogTextType.Recall, "Recall"),
        (CanonLogTextType.Craft, "Craft"),
        (CanonLogTextType.Salvaging, "Salvaging"),
    ];

    public static readonly string MsgKindsSpecifics =
        "Squelch channels are as follows:\n  "
        + string.Join(", ", Array.ConvertAll(SquelchLegalLanes, static c => c.Name))
        + "\n";

    private static readonly SimEntry[] RegistryListings =
    [
        new(Log, "log"),
        new(Day, "day"),
        new(Render, "render"),
        new(Motd, "motd"),
        new(LifestoneSpecifics, "lifestone", "lif", "ls"),
        new(MarketplaceSpecifics, "marketplace", "mar", "mp"),
        new(PkArenaSpecifics, "pkarena", "pka"),
        new(PkLiteArenaSpecifics, "pklarena", "pla"),
        new(PkLiteSpecifics, "pklite", "pkl"),
        new(HouseOverview, "hor", "hr", "hom", "hoa"),
        new(AgeSpecifics, "age"),
        new(BirthSpecifics, "birth"),
        new(CycleRateSpecifics, "framerate"),
        new(LockWidgetSpecifics, "lockui"),
        new(VerSpecifics, "version"),
        new(LocSpecifics, "loc"),
        new(CorpseSpecifics, "corpse", "cor"),
        new(PerishSpecifics, "die"),
        new(WipeSpecifics, "clear"),
        new(PersistWidgetSpecifics, "saveui"),
        new(PullWidgetSpecifics, "loadui"),
        new(PersistAutoWidgetSpecifics, "saveautoui"),
        new(PullAutoWidgetSpecifics, "loadautoui"),
        new(AfkSpecifics, "afk"),
        new(ConsentSpecifics, "consent"),
        new(EmoteSpecifics, "e", "em", "emote", "me"),
        new(EmoteRosterSpecifics, "emotes"),
        new(FriendsSpecifics, "friends", "friends_add", "friends_remove"),
        new(SquelchSpecifics, "squelch", "unsquelch"),
        new(SiftSpecifics, "filter"),
        new(UnfilterSpecifics, "unfilter"),
        new(MsgKindsSpecifics, "messagetypes", "message_types", "msgtypes", "msg_types"),
        new(PopulateCompsSpecifics, "fillcomps"),
        new(EnduranceSpecifics, "endurance"),
        new(SpeakerSpecifics, "speaker"),
        new(BannerSpecifics, "title"),
        new(CommsFlipSpecifics, "chat"),
        new(NoTellSpecifics, "notell"),
        new(EnterCommsSpecifics, "join"),
        new(DepartCommsSpecifics, "leave"),
        new(PermitSpecifics, "permit"),
        new(HslistSpecifics, "hslist"),
        new(AllegianceOverview, "alh", "ah"),
    ];

    private const string SayHelp = "@say <text> - Speaks the text aloud to nearby players.";
    private const string FellowshipHelp = "Sends text to your Fellowship channel.";
    private const string AllegianceCommsHelp = "@a - Sends a message to your Allegiance. Also: @guild, @gu";

    private static readonly SimEntry[] VerbListings =
    [
        new(SayHelp, "say", "s"),
        new(Tell, "tell", "t", "send", "whisper", "w"),
        new(Reply, "reply", "r", "rp"),
        new(Retell, "retell", "rt"),
        new(MonarchReply, "mr"),
        new(PatronReply, "pr"),
        new(Day, "day"),
        new(Log, "log"),
        new(Render, "render"),
        new(Motd, "motd"),
        new(DirectivesClusterSpecifics, "commands"),
        new(AllegiancesClusterSpecifics, "allegiances"),
        new(LanesClusterSpecifics, "channels"),
        new(ChattingClusterSpecifics, "chatting"),
        new(DeathClusterSpecifics, "death"),
        new(ConditionClusterSpecifics, "status"),
        new(PhraseClusterSpecifics, "text"),
        new(FellowshipHelp, "f", "fellow", "fellows", "fellowship", "g", "group", "party"),
        new(AllegianceCommsHelp, "a", "guild", "gu"),
        new("Broadcasts text to your entire allegiance (monarch/speaker permission). Also @allegiance broadcast.", "ab"),
        new("@general - Sends a message to the global General chat channel. Also: @cg", "general", "cg"),
        new("@trade - Sends a message to the global Trade chat channel. Also: @ct", "trade", "ct"),
        new("@lfg - Sends a message to the global Looking For Group (LFG) chat channel. Also: @clfg", "lfg", "clfg"),
        new("@roleplay - Sends a message to the global Roleplay chat channel. Also: @crp", "roleplay", "crp"),
        new("@society - Sends a message to the your Society chat channel. Also: @soc", "society", "soc"),
        new("@olthoi - If you are an Olthoi, sends a message to the global Olthoi chat channel. Also: @o", "olthoi", "o"),
        new("Sends text to your Monarch.", "m", "monarch"),
        new("Sends text to your Patron.", "p", "patron"),
        new("Sends text to your Vassals.", "v", "vassal", "vassals"),
        new("Sends text to your Co-vassals.", "c", "covassal", "covassals", "co-vassals"),
    ];

    private static readonly FrozenDictionary<string, string> RegistryVerbSpecificsByVerb = Index(RegistryListings);
    private static readonly FrozenDictionary<string, string> ByVerb = Index(VerbListings);

    public static readonly FrozenSet<string> RegistryVerbsWithNoCanonHelp =
        new[] { "index", "clist", "on", "off" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool TryFetchRegistryVerbSpecificsPhrase(string verb, out string specificsPhrase)
    {
        return RegistryVerbSpecificsByVerb.TryGetValue(verb.TrimEnd(','), out specificsPhrase!);
    }

    public static bool TryFetchHelpWording(string verb, out string helpPhrase) =>
        ByVerb.TryGetValue(verb.TrimEnd(','), out helpPhrase!);

    private static FrozenDictionary<string, string> Index(SimEntry[] listings)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (phrase, verbs) in listings)
            foreach (string verb in verbs)
                lookup[verb] = phrase;
        return lookup.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
