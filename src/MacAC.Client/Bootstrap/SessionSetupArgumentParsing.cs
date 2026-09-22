namespace MacAC.Client.Bootstrap;

// Minimal --flag value handling for the graphical host's command line
internal static class SessionSetupArgumentParsing
{
    // The value after bit; null when the flag is absent or is the last token
    internal static string? DistillBitVal(string[] arguments, string bit, out bool present)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(bit);

        int at = Array.IndexOf(arguments, bit);
        present = at >= 0;
        return present && at < arguments.Length - 1 ? arguments[at + 1] : null;
    }

    // The arguments with every bit and the token after it removed
    internal static string[] WithoutBitAndVal(string[] arguments, string bit)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(bit);

        List<string> kept = new List<string>(arguments.Length);
        for (int idx = 0; idx < arguments.Length; ++idx)
        {
            if (string.Equals(arguments[idx], bit, StringComparison.Ordinal))
                ++idx;
            else
                kept.Add(arguments[idx]);
        }

        return [.. kept];
    }
}
