using System.Collections.Frozen;

namespace MacAC.Sim.Comms;

public static partial class CanonClientDirectiveRegistry
{
    private sealed record SimDefinition(
        ClientDirectiveId Command,
        string Usage,
        string HelpText,
        Func<string, bool> ValidateArguments,
        string? InvalidArgumentsText = null,
        string[]? Aliases = null);

    private static readonly string[] HouseVerbs = ["house", "hou"];
    private static readonly string[] AllegianceVerbs = ["allegiance", "all"];

    public static bool TryLocateEnterDepartKnob(string tag, out uint knobIdent) => EnterDepartTags.TryGetValue(tag.Trim(), out knobIdent);

    public static bool TryLocateHouseKind(string kind, out uint houseKind) => HouseKinds.TryGetValue(kind.Trim(), out houseKind);

    private static SimDefinition NoArgs(ClientDirectiveId directive, string usage, string helpPhrase, string[]? aliases = null)
    {
        return new(directive, usage, helpPhrase, static arguments => arguments.Length == 0, Aliases: aliases);
    }

    private static SimDefinition AnyArgs(ClientDirectiveId directive, string usage, string helpPhrase, string[]? aliases = null) =>
        new(directive, usage, helpPhrase, static _ => true, Aliases: aliases);

    // Channel tags accepted by @join / @leave and the character option each toggles
    private static readonly FrozenDictionary<string, uint> EnterDepartTags = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["allegiance"] = 0x1Bu,
        ["general"] = 0x23u,
        ["trade"] = 0x24u,
        ["lfg"] = 0x25u,
        ["roleplay"] = 0x26u,
        ["society"] = 0x2Eu,
        ["soc"] = 0x2Eu,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, uint> HouseKinds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["cottage"] = 1u,
        ["villa"] = 2u,
        ["mansion"] = 3u,
        ["apartment"] = 4u,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static bool IsSingleTicket(string arguments)
    {
        return arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length is 1;
    }

    private static bool OnOrOff(string arguments)
    {
        return arguments.Equals("on", StringComparison.OrdinalIgnoreCase) || arguments.Equals("off", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly SimDefinition Lifestone = new(
        ClientDirectiveId.LifestoneRecall,
        Usage: "/lifestone",
        HelpText: "/lifestone (/lif, /ls) - Returns you to the last lifestone you used without killing you.",
        ValidateArguments: static arguments => arguments.Length == 0,
        InvalidArgumentsText: "Please see @help lifestone for more information on how to use this command.",
        Aliases: ["lifestone", "lif", "ls"]);

    private static readonly SimDefinition Marketplace = new(
        ClientDirectiveId.MarketplaceRecall,
        Usage: "/marketplace",
        HelpText: "/marketplace (/mar, /mp) - Teleports you to the Marketplace of Dereth.",
        ValidateArguments: static arguments => arguments.Length == 0,
        InvalidArgumentsText: "Please see @help marketplace for more information on how to use this command.",
        Aliases: ["marketplace", "mar", "mp"]);

    private static readonly SimDefinition PkArena = NoArgs(
        ClientDirectiveId.PkArenaRecall,
        "/pkarena",
        "/pkarena (/pka) - Teleports a Player Killer to the PK Arena.",
        ["pkarena", "pka"]);

    private static readonly SimDefinition PkLiteArena = NoArgs(
        ClientDirectiveId.PkLiteArenaRecall,
        "/pklarena",
        "/pklarena (/pla) - Teleports a PKLite player to the PKLite Arena.",
        ["pklarena", "pla"]);

    private static readonly SimDefinition PkLite = NoArgs(
        ClientDirectiveId.EnterPkLite,
        "/pklite",
        "@pklite (@pkl) - Sets your status to Player Killer Lite. Type @help pklite for more details.",
        ["pklite", "pkl"]);

    private static readonly SimDefinition HouseRecall = NoArgs(
        ClientDirectiveId.HouseRecall,
        "/house recall",
        "/house recall (/hor, /hr) - Teleports you to your house.",
        ["hor", "hr"]);

    private static readonly SimDefinition MansionRecall = NoArgs(
        ClientDirectiveId.MansionRecall,
        "/house mansion_recall",
        "/house mansion_recall (/hom, /hoa) - Teleports you to your allegiance mansion.",
        ["hom", "hoa"]);

    private static readonly SimDefinition HouseAbandon = NoArgs(
        ClientDirectiveId.HouseAbandon,
        "/house abandon",
        "@house abandon - Abandons your house.",
        []);

    private static readonly SimDefinition QueryAge = NoArgs(
        ClientDirectiveId.QueryAge,
        "/age",
        "/age - Displays how long your character has been played.",
        ["age"]);

    private static readonly SimDefinition AskBirth = NoArgs(
        ClientDirectiveId.QueryBirth,
        "/birth",
        "/birth - Displays when your character was created.",
        ["birth"]);

    private static readonly SimDefinition CycleRate = NoArgs(
        ClientDirectiveId.ToggleFrameRate,
        "/framerate",
        "/framerate - Toggles the framerate display.",
        ["framerate"]);

    private static readonly SimDefinition Day = AnyArgs(
        ClientDirectiveId.TogglePersistentDaylight,
        "/day",
        CanonDirectiveHelpChart.Day,
        ["day"]);

    private static readonly SimDefinition Render = AnyArgs(
        ClientDirectiveId.RenderOption,
        "/render <option> <value>",
        CanonDirectiveHelpChart.Render,
        ["render"]);

    private static readonly SimDefinition MutexWidget = NoArgs(
        ClientDirectiveId.ToggleUiLock,
        "/lockui",
        "/lockui - Toggles whether the interface can be moved or resized.",
        ["lockui"]);

    private static readonly SimDefinition Version = NoArgs(
        ClientDirectiveId.ShowVersion,
        "/version",
        "/version - Displays the client version.",
        ["version"]);

    private static readonly SimDefinition Location = NoArgs(
        ClientDirectiveId.ShowLocation,
        "/loc",
        "/loc - Displays your current position.",
        ["loc"]);

    private static readonly SimDefinition Corpse = new(
        ClientDirectiveId.ShowLastCorpseLocation,
        Usage: "/corpse",
        HelpText: "/corpse (/cor) - Displays the location of your last outdoor death.",
        ValidateArguments: static _ => true,
        Aliases: ["corpse", "cor"]);

    private static readonly SimDefinition Perish = new(
        ClientDirectiveId.Die,
        Usage: "/die",
        HelpText: "/die - Kills your character after confirmation.",
        ValidateArguments: static arguments => arguments.Length == 0,
        InvalidArgumentsText: "Please see @help die for more information on how to use this command.",
        Aliases: ["die"]);

    private static readonly SimDefinition Clear = AnyArgs(
        ClientDirectiveId.ClearChat,
        "/clear [all]",
        "/clear [all] - Clears the current chat window, or every chat window.",
        ["clear"]);

    private static readonly SimDefinition CommsTraceFile = AnyArgs(
        ClientDirectiveId.ChatLogFile,
        "/log [filename]",
        "/log [filename] - Echoes chat text to a logfile, or stops if already logging.",
        ["log"]);

    private static readonly SimDefinition PersistWidget = AnyArgs(
        ClientDirectiveId.SaveUi,
        "/saveui [filename]",
        "/saveui [filename] - Saves the current interface layout.",
        ["saveui"]);

    private static readonly SimDefinition PullWidget = AnyArgs(
        ClientDirectiveId.LoadUi,
        "/loadui [filename]",
        "/loadui [filename] - Loads a saved interface layout.",
        ["loadui"]);

    private static readonly SimDefinition PersistAutoWidget = AnyArgs(
        ClientDirectiveId.SaveAutoUi,
        "/saveautoui",
        "/saveautoui - Saves the automatic character-and-resolution interface layout.",
        ["saveautoui"]);

    private static readonly SimDefinition PullAutoWidget = AnyArgs(
        ClientDirectiveId.LoadAutoUi,
        "/loadautoui",
        "/loadautoui - Loads the automatic character-and-resolution interface layout.",
        ["loadautoui"]);

    private static readonly SimDefinition Away = AnyArgs(
        ClientDirectiveId.Away,
        "/afk [on|off|msg <message>]",
        "/afk [on|off|msg <message>] - Sets your away-from-keyboard status.",
        ["afk"]);

    private static readonly SimDefinition Consent = AnyArgs(
        ClientDirectiveId.Consent,
        "/consent <on|off|who|clear|remove <name>>",
        "/consent - Manages corpse-looting consent.",
        ["consent"]);

    private static readonly SimDefinition Emote = AnyArgs(
        ClientDirectiveId.Emote,
        "/emote <text>",
        "/emote (/e, /em, /me) - Performs a text emote.",
        ["e", "em", "emote", "me"]);

    private static readonly SimDefinition Emotes = NoArgs(
        ClientDirectiveId.ListEmotes,
        "/emotes",
        "/emotes - Lists all standard emotes.",
        ["emotes"]);

    private static readonly SimDefinition Friends = AnyArgs(
        ClientDirectiveId.Friends,
        "/friends [add|remove|online|old]",
        "/friends - Helps you manage your friends list.",
        ["friends"]);

    private static readonly SimDefinition FriendsAppend = AnyArgs(
        ClientDirectiveId.FriendsAdd,
        "/friends_add <name>",
        "/friends_add <name> - Adds a character to your friends list.",
        ["friends_add"]);

    private static readonly SimDefinition FriendsDrop = AnyArgs(
        ClientDirectiveId.FriendsRemove,
        "/friends_remove <name|-all>",
        "/friends_remove <name|-all> - Removes friends from your list.",
        ["friends_remove"]);

    private static readonly SimDefinition Squelch = AnyArgs(
        ClientDirectiveId.Squelch,
        "/squelch [options] <name>",
        "/squelch - Ignores messages from a player or account.",
        ["squelch"]);

    private static readonly SimDefinition Unsquelch = AnyArgs(
        ClientDirectiveId.Unsquelch,
        "/unsquelch [options] <name>",
        "/unsquelch - Stops ignoring messages from a player or account.",
        ["unsquelch"]);

    private static readonly SimDefinition Filter = AnyArgs(
        ClientDirectiveId.Filter,
        "/filter -<message type>",
        "/filter -<message type> - Globally hides a message category.",
        ["filter"]);

    private static readonly SimDefinition Unfilter = AnyArgs(
        ClientDirectiveId.Unfilter,
        "/unfilter -<message type>",
        "/unfilter -<message type> - Shows a globally hidden message category.",
        ["unfilter"]);

    private static readonly SimDefinition MsgKinds = NoArgs(
        ClientDirectiveId.ListMessageTypes,
        "/messagetypes",
        "/messagetypes (/message_types, /msgtypes, /msg_types) - Lists valid filter and squelch message types.",
        ["messagetypes", "message_types", "msgtypes", "msg_types"]);

    private static readonly SimDefinition PopulateModules = AnyArgs(
        ClientDirectiveId.FillComponents,
        "/fillcomps [component type] [pyreal value]",
        "/fillcomps - Helps you buy components in bulk.",
        ["fillcomps"]);

    private static readonly SimDefinition Endurance = NoArgs(
        ClientDirectiveId.Endurance,
        "/endurance",
        "The endurance attribute has a number of abilities tied to it. Type @help endurance for the full description.",
        ["endurance"]);

    private static readonly SimDefinition Speaker = NoArgs(
        ClientDirectiveId.Speaker,
        "/speaker",
        "This command is no longer in use, please see @allegiance officer.",
        ["speaker"]);

    private static readonly SimDefinition SetTitle = AnyArgs(
        ClientDirectiveId.SetChatTitle,
        "/title <new title>",
        "@title <new title> - Sets the title of the popup chat window.",
        ["title"]);

    private static readonly SimDefinition CommsFlip = new(
        ClientDirectiveId.ChatToggle,
        Usage: "/chat <on|off>",
        HelpText: "@chat <on/off> - Sets whether or not you receive normal chat. When set to \"off\", you will no longer receive any spoken speech (normal chat). However, you will still receive tells.",
        ValidateArguments: OnOrOff,
        Aliases: ["chat"]);

    private static readonly SimDefinition NoTellFlip = new(
        ClientDirectiveId.NoTellToggle,
        Usage: "/notell <on|off>",
        HelpText: "@notell <on/off> - Sets whether or not you receive @tells. When set to \"on\", you will not receive any tells.",
        ValidateArguments: OnOrOff,
        Aliases: ["notell"]);

    private static readonly SimDefinition EnterLane = new(
        ClientDirectiveId.JoinChannel,
        Usage: "/join <channel tag>",
        HelpText: "@join <channel tag> - Allows you to hear and speak on the given channel.",
        ValidateArguments: static arguments => EnterDepartTags.ContainsKey(arguments.Trim()),
        Aliases: ["join"]);

    private static readonly SimDefinition DepartLane = new(
        ClientDirectiveId.LeaveChannel,
        Usage: "/leave <channel tag>",
        HelpText: "@leave <channel tag> - Prevents you from hearing or speaking on the given channel.",
        ValidateArguments: static arguments => EnterDepartTags.ContainsKey(arguments.Trim()),
        Aliases: ["leave"]);

    private static readonly SimDefinition Permit = new(
        ClientDirectiveId.Permit,
        Usage: "/permit <add|remove> <name>",
        HelpText: "@permit add <name> - Allows another player to loot your corpse. @permit remove <name> - Removes permission to access your corpse from the named character.",
        ValidateArguments: static arguments =>
        {
            string[] pieces = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return pieces.Length >= 2
                && (pieces[0].Equals("add", StringComparison.OrdinalIgnoreCase) || pieces[0].Equals("remove", StringComparison.OrdinalIgnoreCase));
        },
        Aliases: ["permit"]);

    private static readonly SimDefinition HouseOnHandRoster = new(
        ClientDirectiveId.HouseAvailableList,
        Usage: "/hslist <house type>",
        HelpText: "@hslist <house type> - Lists the number and, if appropriate, positions of houses currently available for purchase. Types include: Apartment, Cottage, Villa, Mansion",
        ValidateArguments: static arguments => HouseKinds.ContainsKey(arguments.Trim()),
        InvalidArgumentsText: "Please see @help hslist for more information on how to use this command",
        Aliases: ["hslist"]);

    private static readonly SimDefinition OrdinalLanes = AnyArgs(
        ClientDirectiveId.IndexChannels,
        "/index",
        "@index - Requests the channel index (restricted).",
        ["index"]);

    private static readonly SimDefinition RosterLane = new(
        ClientDirectiveId.ListChannel,
        Usage: "/clist <channel>",
        HelpText: "@clist <channel> - Requests the member list of a channel (restricted).",
        ValidateArguments: static arguments => IsSingleTicket(arguments),
        InvalidArgumentsText: "Please specify the channel name.",
        Aliases: ["clist"]);

    private static readonly SimDefinition OnLane = new(
        ClientDirectiveId.OnChannel,
        Usage: "/on <channel>",
        HelpText: "@on <channel> - Joins a channel (restricted).",
        ValidateArguments: static arguments => IsSingleTicket(arguments),
        InvalidArgumentsText: "Please specify the channel name.",
        Aliases: ["on"]);

    private static readonly SimDefinition OffLane = new(
        ClientDirectiveId.OffChannel,
        Usage: "/off <channel>",
        HelpText: "@off <channel> - Leaves a channel (restricted).",
        ValidateArguments: static arguments => IsSingleTicket(arguments),
        InvalidArgumentsText: "Please specify the channel name.",
        Aliases: ["off"]);

    private static readonly SimDefinition AllegianceHometown = NoArgs(
        ClientDirectiveId.AllegianceHometown,
        "/alh",
        "@allegiance hometown (@alh, @ah) - Recalls you to your allegiance bindstone, if your allegiance has tied to one.",
        ["alh", "ah"]);

    private static readonly SimDefinition AllegianceDetails = AnyArgs(
        ClientDirectiveId.AllegianceInfo,
        "/allegiance info <name>",
        "@allegiance info <name> - Requests information on a member of your allegiance.",
        []);

    private static readonly SimDefinition AllegianceBoot = AnyArgs(
        ClientDirectiveId.AllegianceBoot,
        "/allegiance boot [-account] <name>",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceBan = AnyArgs(
        ClientDirectiveId.AllegianceBan,
        "/allegiance ban <add|remove|list> [name]",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceChat = AnyArgs(
        ClientDirectiveId.AllegianceChat,
        "/allegiance chat <on|off|kick|gag|ungag> ...",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceBroadcast = AnyArgs(
        ClientDirectiveId.AllegianceBroadcast,
        "/allegiance broadcast <message>",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceOfficer = AnyArgs(
        ClientDirectiveId.AllegianceOfficer,
        "/allegiance officer [add|set|remove|clear|list] ...",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceOfficerBanner = AnyArgs(
        ClientDirectiveId.AllegianceOfficerTitle,
        "/allegiance title [set|clear|list] ...",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceName = AnyArgs(
        ClientDirectiveId.AllegianceName,
        "/allegiance name [set|clear] ...",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceMutex = AnyArgs(
        ClientDirectiveId.AllegianceLock,
        "/allegiance lock [on|off|toggle|check|bypass] ...",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceHouse = AnyArgs(
        ClientDirectiveId.AllegianceHouse,
        "/allegiance house [guest|storage] [open|close]",
        CanonDirectiveHelpChart.AllegianceOverview,
        []);

    private static readonly SimDefinition AllegianceMotd = AnyArgs(
        ClientDirectiveId.AllegianceMotd,
        "/motd [set <text>|clear]",
        CanonDirectiveHelpChart.Motd,
        ["motd"]);

    private static readonly SimDefinition HouseOpenCondition = AnyArgs(
        ClientDirectiveId.HouseOpenStatus,
        "/house <open|close>",
        CanonDirectiveHelpChart.HouseOverview,
        []);

    private static readonly SimDefinition HouseDepot = AnyArgs(
        ClientDirectiveId.HouseStorage,
        "/house storage <subcommand>",
        CanonDirectiveHelpChart.HouseOverview,
        []);

    private static readonly SimDefinition HouseBoot = AnyArgs(
        ClientDirectiveId.HouseBoot,
        "/house boot <name|-all>",
        CanonDirectiveHelpChart.HouseOverview,
        []);

    private static readonly SimDefinition HouseBootAll = AnyArgs(
        ClientDirectiveId.HouseBootAll,
        "/house boot_all",
        CanonDirectiveHelpChart.HouseOverview,
        []);

    private static readonly SimDefinition HouseGuests = AnyArgs(
        ClientDirectiveId.HouseGuests,
        "/house guest <subcommand>",
        CanonDirectiveHelpChart.HouseOverview,
        []);

    private static readonly SimDefinition HouseTaps = AnyArgs(
        ClientDirectiveId.HouseHooks,
        "/house hooks <on|off>",
        CanonDirectiveHelpChart.HouseOverview,
        []);

    private static readonly SimDefinition AllegianceUnrecognizedSubcommand = new(
        ClientDirectiveId.AllegianceUnrecognizedSubcommand,
        Usage: "/allegiance <sub>",
        HelpText: "Please see @help Allegiance for more information on how to use this command.",
        ValidateArguments: static _ => false,
        InvalidArgumentsText: "Please see @help Allegiance for more information on how to use this command.",
        Aliases: []);

    private static readonly SimDefinition HouseUnrecognizedSubcommand = new(
        ClientDirectiveId.HouseUnrecognizedSubcommand,
        Usage: "/house <sub>",
        HelpText: "Please see @help House for more information on how to use this command.",
        ValidateArguments: static _ => false,
        InvalidArgumentsText: "Please see @help House for more information on how to use this command.",
        Aliases: []);

    // Every definition, whether or not it has a top-level verb (house/allegiance subcommands are
    // matched separately)
    private static readonly SimDefinition[] All =
    [
        Lifestone, Marketplace, PkArena, PkLiteArena, PkLite, HouseRecall, MansionRecall, HouseAbandon, QueryAge, AskBirth, CycleRate, Day, Render, MutexWidget, Version, Location, Corpse, Perish, Clear, CommsTraceFile, PersistWidget, PullWidget, PersistAutoWidget, PullAutoWidget, Away, Consent, Emote, Emotes, Friends, FriendsAppend, FriendsDrop, Squelch, Unsquelch, Filter, Unfilter, MsgKinds, PopulateModules, Endurance, Speaker, SetTitle, CommsFlip, NoTellFlip, EnterLane, DepartLane, Permit, HouseOnHandRoster, OrdinalLanes, RosterLane, OnLane, OffLane, AllegianceHometown, AllegianceDetails, AllegianceBoot, AllegianceBan, AllegianceChat, AllegianceBroadcast, AllegianceOfficer, AllegianceOfficerBanner, AllegianceName, AllegianceMutex, AllegianceHouse, AllegianceMotd, HouseOpenCondition, HouseDepot, HouseBoot, HouseBootAll, HouseGuests, HouseTaps, AllegianceUnrecognizedSubcommand, HouseUnrecognizedSubcommand
    ];

    private static readonly FrozenDictionary<string, SimDefinition> ByVerb =
        All.SelectMany(static definition => (definition.Aliases ?? []).Select(a => new KeyValuePair<string, SimDefinition>(a, definition)))
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> RecognizedVerbs { get; } =
        ByVerb.Keys.Concat(HouseVerbs).Concat(AllegianceVerbs).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
