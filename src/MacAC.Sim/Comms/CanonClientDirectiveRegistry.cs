using System.Collections.Frozen;

namespace MacAC.Sim.Comms;

public static partial class CanonClientDirectiveRegistry
{
    public readonly record struct Match(ClientDirectiveId Command, string Arguments, string Usage, bool HasValidArguments, string? InvalidArgumentsText);

    public static bool TryFit(string feed, out Match fit)
    {
        fit = default;
        if (string.IsNullOrWhiteSpace(feed))
            return false;

        string stroke = feed.Trim();
        if (stroke.Length < 2 || stroke[0] is not ('/' or '@'))
            return false;

        (string front, string arguments) = Cut(stroke);
        string verb = front[1..].TrimEnd(',');

        if (IsOneOf(verb, HouseVerbs))
            return FitHouse(arguments, out fit);
        if (IsOneOf(verb, AllegianceVerbs))
            return FitAllegiance(arguments, out fit);
        if (!ByVerb.TryGetValue(verb, out SimDefinition? definition))
            return false;

        fit = Of(definition, arguments);
        return true;
    }

    public static bool TryFetchHelpPhrase(string verb, out string helpPhrase)
    {
        string label = verb.TrimEnd(',');
        if (IsOneOf(label, HouseVerbs))
        {
            helpPhrase = CanonDirectiveHelpChart.HouseOverview;
            return true;
        }
        if (IsOneOf(label, AllegianceVerbs))
        {
            helpPhrase = CanonDirectiveHelpChart.AllegianceOverview;
            return true;
        }
        if (ByVerb.TryGetValue(label, out SimDefinition? definition))
        {
            helpPhrase = definition.HelpText;
            return true;
        }
        helpPhrase = string.Empty;
        return false;
    }

    private static bool IsOneOf(string verb, string[] verbs) => verbs.Any(v => verb.Equals(v, StringComparison.OrdinalIgnoreCase));

    // Splits "verb rest" at the first whitespace after the first character
    private static (string Head, string Tail) Cut(string phrase)
    {
        for (int idx = 1; idx < phrase.Length; ++idx)
        {
            if (char.IsWhiteSpace(phrase[idx]))
                return (phrase[..idx], phrase[(idx + 1)..].Trim());
        }
        return (phrase, string.Empty);
    }

    private static Match Of(SimDefinition definition, string arguments)
    {
        return new(definition.Command, arguments, definition.Usage, definition.ValidateArguments(arguments), definition.InvalidArgumentsText);
    }

    private static Match Refused(SimDefinition clan, string arguments)
    {
        return new(clan.Command, arguments, clan.Usage, HasValidArguments: false, clan.InvalidArgumentsText);
    }

    private static bool FitHouse(string arguments, out Match fit)
    {
        (string sub, string rest) = Cut(arguments);
        SimDefinition? definition = sub.ToLowerInvariant() switch
        {
            "recall" or "re" => HouseRecall,
            "mansion_recall" or "alleg_recall" or "ma" => MansionRecall,
            "abandon" => HouseAbandon,
            "open" or "close" => HouseOpenCondition,
            "storage" => HouseDepot,
            "remove" or "boot" => HouseBoot,
            "boot_all" or "remove_all" => HouseBootAll,
            "guest" => HouseGuests,
            "available" => HouseOnHandRoster,
            "hooks" => HouseTaps,
            _ => null,
        };

        if (definition is null)
            fit = Refused(HouseUnrecognizedSubcommand, arguments);
        else fit = definition == HouseRecall || definition == MansionRecall ? new Match(definition.Command, rest, definition.Usage, HasValidArguments: rest.Length is 0, "Please see @help House for more information on how to use this command.") : definition == HouseOnHandRoster ? Of(definition, rest) : new Match(definition.Command, definition == HouseOpenCondition ? sub : rest, definition.Usage, HasValidArguments: true, InvalidArgumentsText: null);
        return true;
    }

    private static bool FitAllegiance(string arguments, out Match fit)
    {
        (string sub, string rest) = Cut(arguments);
        SimDefinition? definition = sub.ToLowerInvariant() switch
        {
            "boot" => AllegianceBoot,
            "info" => AllegianceDetails,
            "chat" or "ch" => AllegianceChat,
            "broadcast" or "br" => AllegianceBroadcast,
            "ban" => AllegianceBan,
            "officer" => AllegianceOfficer,
            "title" => AllegianceOfficerBanner,
            "hometown" or "ho" => AllegianceHometown,
            "motd" => AllegianceMotd,
            "name" => AllegianceName,
            "lock" => AllegianceMutex,
            "house" => AllegianceHouse,
            _ => null,
        };

        fit = definition is null
            ? Refused(AllegianceUnrecognizedSubcommand, arguments)
            : new Match(definition.Command, definition == AllegianceHometown ? string.Empty : rest, definition.Usage, HasValidArguments: true, InvalidArgumentsText: null);
        return true;
    }
}
