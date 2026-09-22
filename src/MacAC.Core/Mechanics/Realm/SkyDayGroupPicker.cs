namespace MacAC.Mechanics.Realm;

/// <summary>The client's per-day hash that picks which sky day group plays.</summary>
public static class SkyDayGroupPicker
{
    private const float TwoTo32 = 4294967296.0f;

    public static int SelectIndex(int dayClusterTally, int absoluteYear, int daysPerYear, int dayOfYear, int? forcedOrdinal = null)
    {
        if (dayClusterTally <= 0)
            return 0;
        if (forcedOrdinal is { } forced && forced >= 0 && forced < dayClusterTally)
            return forced;
        if (dayClusterTally is 1)
            return 0;

        int mixed = unchecked((absoluteYear * daysPerYear + dayOfYear) * 0x6A42FDB2 + (int)0x8ABE1652);
        float unsigned = mixed;
        if (mixed < 0)
            unsigned += TwoTo32;

        int choose = (int)MathF.Floor(dayClusterTally * unsigned * (1.0f / TwoTo32));
        return choose < 0 || choose >= dayClusterTally ? 0 : choose;
    }
}
