using System.Globalization;
using System.Text;

namespace MacAC.Mechanics.Shell;

public static class CanonDurationText
{
    private static readonly (long Seconds, string Unit)[] Units =
    [
        (0x278D00, "mo"),   // 2,592,000: a 30-day month
        (0x15180, "d"),     // 86,400
        (0xE10, "h"),       // 3,600
        (0x3C, "m"),        // 60
    ];

    public static string Format(double secs)
    {
        long left = Math.Max(0L, (long)secs);
        StringBuilder phrase = new StringBuilder();

        foreach ((long dims, string unit) in Units)
        {
            long tally = left / dims;
            left %= dims;
            if (tally is not 0)
                Part(phrase, tally, unit);
        }
        Part(phrase, left, "s");

        phrase.Length--; // the separator the last part appended
        return phrase.ToString();
    }

    private static void Part(StringBuilder phrase, long tally, string unit)
    {
        phrase.Append(tally.ToString(CultureInfo.InvariantCulture)).Append(unit).Append(' ');
    }
}
