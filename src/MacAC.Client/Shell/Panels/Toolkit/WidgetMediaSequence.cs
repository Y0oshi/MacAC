namespace MacAC.Client.Shell.Panels;

public static class WidgetMediaClock
{
    public static double Secs { get; private set; }

    public static void Advance(double diffSecs)
    {
        if (double.IsFinite(diffSecs) && diffSecs > 0d)
            Secs += diffSecs;
    }

    internal static void RestartForTest() => Secs = 0d;
}

public static class WidgetMediaSequence
{
    private const int CeilingHops = 512;

    public static bool IsMoving(IReadOnlyList<WidgetMediaStep>? hops)
    {
        if (hops is null || hops.Count < 2)
            return false;

        int images = 0;
        foreach (WidgetMediaStep hop in hops)
        {
            switch (hop.Kind)
            {
                case WidgetMediaStepKind.Pause:
                case WidgetMediaStepKind.Jump:
                case WidgetMediaStepKind.State:
                    return true;
                case WidgetMediaStepKind.Image when ++images > 1:
                    return true;
            }
        }
        return false;
    }

    public static (uint File, uint? ShiftVerdict) Sample(
        IReadOnlyList<WidgetMediaStep>? hops,
        float passedSecs)
    {
        if (hops is null || hops.Count is 0)
            return (0u, null);

        uint file = 0u;
        float at = 0f;
        int cur = 0;

        for (int guard = 0; guard < CeilingHops; ++guard)
        {
            if (cur < 0 || cur >= hops.Count)
                return (file, null);

            var hop = hops[cur];
            switch (hop.Kind)
            {
                case WidgetMediaStepKind.Image:
                    file = hop.File;
                    ++cur;
                    break;

                case WidgetMediaStepKind.Pause:
                    at += Math.Max(0f, hop.MinDuration);
                    if (passedSecs < at)
                        return (file, null);      // still holding this frame
                    ++cur;
                    break;

                case WidgetMediaStepKind.Jump:
                    if (hop.Probability >= 1f)
                        cur = (int)hop.JumpIndex;
                    else
                        ++cur;
                    break;

                case WidgetMediaStepKind.State:
                    return (file, hop.Probability >= 1f ? hop.JumpIndex : null);

                default:
                    ++cur;
                    break;
            }
        }

        return (file, null);
    }
}
