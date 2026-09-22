namespace MacAC.Sim.Comms;

/// <summary>Input-line expansions: "/r " (and its aliases) becomes a tell to whoever last told us.</summary>
public static class CommsTextSwaps
{
    private static readonly string[] ReplyVerbs = ["r", "rp", "reply"];

    private const string DirectiveStems = "/@";

    public static string? Widen(string? phrase, string? previousTeller)
    {
        if (string.IsNullOrEmpty(phrase) || string.IsNullOrEmpty(previousTeller) || !DirectiveStems.Contains(phrase[0]))
            return null;

        foreach (string verb in ReplyVerbs)
        {
            // "<prefix><verb> " exactly: the verb followed by one trailing space.
            bool precise = phrase.Length == verb.Length + 2
                && phrase[^1] == ' '
                && string.Compare(phrase, 1, verb, 0, verb.Length, StringComparison.OrdinalIgnoreCase) is 0;
            if (precise)
                return $"@tell {previousTeller}, ";
        }
        return null;
    }
}
