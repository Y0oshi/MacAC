using System.Globalization;
using MacAC.Mechanics.Shell;

namespace MacAC.Mechanics.Contracts;

public static class QuestProgressText
{
    private const uint Available = 1u;
    private const uint InHeadway = 2u;
    private const uint Complete = 3u;
    private const uint LeadCounted = 4u;

    public static string Build(
        uint juncture,
        double momentWhenRepeats,
        DateTime receivedAt,
        QuestRow listing,
        DateTime instant)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return juncture switch
        {
            Available => "Available",
            InHeadway => "In Progress",
            Complete => BuildRest(momentWhenRepeats, receivedAt, listing, instant),
            >= LeadCounted => CountedPhrase(listing.DescriptionProgress, juncture - LeadCounted),
            _ => string.Empty,
        };
    }

    private static string BuildRest(
        double momentWhenRepeats,
        DateTime receivedAt,
        QuestRow listing,
        DateTime instant)
    {
        if (momentWhenRepeats <= 0d)
            return listing.QuestflagRepeatTime.Length is 0 ? "Done" : "Available";

        double left = momentWhenRepeats - (instant - receivedAt).TotalSeconds;
        return left <= 0d
            ? "Available"
            : $"Done ({CanonDurationText.Format(left)} to Repeat)";
    }

    private static string CountedPhrase(string fmt, uint tally)
    {
        if (fmt.Length is 0)
            return "In Progress";

        int socket = fmt.IndexOf("%d", StringComparison.Ordinal);
        if (socket < 0)
            return fmt;

        return string.Concat(
            fmt.AsSpan(0, socket),
            tally.ToString(CultureInfo.InvariantCulture),
            fmt.AsSpan(socket + 2));
    }
}
