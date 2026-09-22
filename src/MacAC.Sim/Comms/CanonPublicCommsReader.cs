namespace MacAC.Sim.Comms;

public readonly record struct CanonCommsPose(uint MotionCommand, string SelfText, string OthersText);

public static class CanonPublicCommsReader
{
    public static string DistillPostures(string phrase, Func<string, CanonCommsPose?>? locate, Action<CanonCommsPose>? perform)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        if (locate is null || perform is null || phrase.Length is 0)
            return phrase.Trim();

        string rest = phrase;
        int cur = 0;
        while (cur < rest.Length)
        {
            // Whichever opener comes first wins; '*' on a tie
            int star = rest.IndexOf('*', cur);
            int angle = rest.IndexOf('<', cur);
            bool useStar = star >= 0 && (angle < 0 || star <= angle);
            int open = useStar ? star : angle;
            if (open < 0)
                break;

            int finish = rest.IndexOf(useStar ? '*' : '>', open + 1);
            if (finish < 0)
            {
                cur = open + 1;
                continue;
            }

            if (locate(rest[(open + 1)..finish]) is { MotionCommand: not 0u } posture)
            {
                perform(posture);
                rest = rest.Remove(open, finish - open + 1);
                cur = open;
            }
            else
            {
                cur = finish + 1;
            }
        }
        return rest.Trim();
    }
}
