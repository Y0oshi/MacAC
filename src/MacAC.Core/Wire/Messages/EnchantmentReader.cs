namespace MacAC.Wire.Messages;

/// <summary>One 60-byte (or 64 with a spell-set id) enchantment record and the counted lists of them.</summary>
public static class EnchantmentReader
{
    private const int CaptureOctets = 60;
    private const uint UpperRosterTally = 0x4000;

    public static PlayerDescReader.EnchantmentRow Read(ReadOnlySpan<byte> src, ref int locus, PlayerDescReader.EnchantmentShelf bin = 0)
    {
        var cursor = new WireCursor(src);
        cursor.Skip(locus);
        var rank = Read(ref cursor, bin);
        locus = cursor.At;
        return rank;
    }

    public static IReadOnlyList<PlayerDescReader.EnchantmentRow> PullRoster(ReadOnlySpan<byte> src, ref int locus, PlayerDescReader.EnchantmentShelf bin = 0)
    {
        var cursor = new WireCursor(src);
        cursor.Skip(locus);
        IReadOnlyList<PlayerDescReader.EnchantmentRow> ranks = ScanRoster(ref cursor, bin);
        locus = cursor.At;
        return ranks;
    }

    internal static PlayerDescReader.EnchantmentRow Read(ref WireCursor cursor, PlayerDescReader.EnchantmentShelf bin = 0)
    {
        if (cursor.At < 0 || !cursor.Has(CaptureOctets))
            throw new FormatException("truncated enchantment record");
        ushort arcanumIdent = cursor.U16();
        ushort stratum = cursor.U16();
        ushort category = cursor.U16();
        ushort hasSetIdent = cursor.U16();
        uint strength = cursor.U32();
        double beginMoment = cursor.F64();
        double interval = cursor.F64();
        uint invoker = cursor.U32();
        float downgradeModifier = cursor.F32();
        float downgradeThreshold = cursor.F32();
        double previousDegraded = cursor.F64();
        uint statModKind = cursor.U32();
        uint statModTag = cursor.U32();
        float statModVal = cursor.F32();
        uint? setIdent = null;
        if (hasSetIdent is not 0)
        {
            if (!cursor.Has(4))
                throw new FormatException("truncated enchantment record");
            setIdent = cursor.U32();
        }

        bool finite = double.IsFinite(beginMoment) && double.IsFinite(interval) && float.IsFinite(downgradeModifier)
            && float.IsFinite(downgradeThreshold) && double.IsFinite(previousDegraded) && float.IsFinite(statModVal);
        if (!finite)
            throw new FormatException("non-finite enchantment value");

        return new PlayerDescReader.EnchantmentRow(
            arcanumIdent, stratum, category, hasSetIdent, strength, beginMoment, interval, invoker,
            downgradeModifier, downgradeThreshold, previousDegraded, statModKind, statModTag, statModVal, setIdent, bin);
    }

    internal static List<PlayerDescReader.EnchantmentRow> ScanRoster(ref WireCursor cursor, PlayerDescReader.EnchantmentShelf bin = 0)
    {
        if (!cursor.Has(4))
            throw new FormatException("truncated enchantment list count");
        uint tally = cursor.U32();
        if (tally > UpperRosterTally)
            throw new FormatException("unreasonable enchantment list count");
        var ranks = new List<PlayerDescReader.EnchantmentRow>((int)tally);
        for (uint idx = 0; idx < tally; ++idx)
            ranks.Add(Read(ref cursor, bin));
        return ranks;
    }
}
