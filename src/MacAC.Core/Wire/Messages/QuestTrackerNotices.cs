namespace MacAC.Wire.Messages;

public enum QuestStage : uint
{
    Available = 1,
    InProgress = 2,
    DoneOrPendingRepeat = 3,
    ProgressCounter = 4,
}

public readonly record struct QuestTracker(uint Version, uint ContractId, QuestStage Stage, double TimeWhenDone, double TimeWhenRepeats, DateTime ReceivedAt)
{
    internal const int WireDims = 4 + 4 + 4 + 8 + 8;

    public bool HasHeadwayCounter => (uint)Stage >= (uint)QuestStage.ProgressCounter;

    public uint Progress
    {
        get
        {
            return HasHeadwayCounter ? (uint)Stage - (uint)QuestStage.ProgressCounter : 0u;
        }
    }
}

public readonly record struct QuestTrackerUpdate(QuestTracker Tracker, bool Delete, bool SetAsDisplay);

public static class QuestTrackerNotices
{
    private const int UpperChartListings = 4096;

    public static QuestTrackerUpdate? DecodeRefresh(ReadOnlySpan<byte> cargo, DateTime receivedAt)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            var tracker = Tracker(ref cursor, receivedAt);
            bool erase = cursor.Word() is not 0u;
            bool readout = cursor.Word() is not 0u;
            return new QuestTrackerUpdate(tracker, erase, readout);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static IReadOnlyDictionary<uint, QuestTracker>? DecodeChart(ReadOnlySpan<byte> cargo, DateTime receivedAt)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            (ushort tally, ushort bins) = DigestPreamble(ref cursor);
            if (bins is 0 && tally is not 0)
                throw new FormatException("not valid contract tracker table");
            if (tally > UpperChartListings)
                throw new FormatException("implausible contract tracker count");

            var chart = new Dictionary<uint, QuestTracker>(tally);
            for (int idx = 0; idx < tally; ++idx)
            {
                uint tag = cursor.Word();
                chart[tag] = Tracker(ref cursor, receivedAt);
            }
            return chart;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Retail's PackableHashTable header: count in the low half, bucket count in the high half
    internal static (ushort Count, ushort Buckets) DigestPreamble(ref WireCursor cursor)
    {
        uint preamble = cursor.Word();
        return ((ushort)(preamble & 0xFFFFu), (ushort)(preamble >> 16));
    }

    private static QuestTracker Tracker(ref WireCursor cursor, DateTime receivedAt)
    {
        uint ver = cursor.Word();
        uint contract = cursor.Word();
        QuestStage juncture = (QuestStage)cursor.Word();
        double done = Double(ref cursor);
        return new QuestTracker(ver, contract, juncture, done, Double(ref cursor), receivedAt);
    }

    private static double Double(ref WireCursor cursor)
    {
        return cursor.Has(8) ? cursor.F64() : throw new FormatException("truncated double");
    }
}
