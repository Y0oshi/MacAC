using MacAC.Mechanics.Fellows;

namespace MacAC.Wire.Messages;

/// <summary>Friends-list deltas (GameEvent 0x0021) and the squelch database (0x01F4).</summary>
public static class SocialStateNotices
{
    private const uint UpperFriends = 65_536;
    private const uint UpperRosterLen = 10_000;
    private const uint UpperVlongWords = 4;

    public static FriendsDelta? DecodeFriendsRefresh(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            uint tally = cursor.Word();
            if (tally > UpperFriends)
                return null;

            List<FriendRow> ranks = new List<FriendRow>((int)tally);
            for (uint idx = 0; idx < tally; ++idx)
            {
                uint ident = cursor.Word();
                bool online = cursor.Word() is not 0;
                bool concealed = cursor.Word() is not 0;
                string label = cursor.String16L();
                IReadOnlyList<uint> friends = Words(ref cursor);
                IReadOnlyList<uint> friendOf = Words(ref cursor);
                ranks.Add(new FriendRow(ident, label, online, concealed, friends, friendOf));
            }

            FriendsDeltaKind sort = (FriendsDeltaKind)cursor.Word();
            return Enum.IsDefined(sort) ? new FriendsDelta(sort, ranks) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static SquelchBook? DecodeSquelchDatabase(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            var accts = AcctChart(ref cursor);
            var toons = ToonChart(ref cursor);
            var global = SquelchDetails(ref cursor);
            return cursor.At <= cursor.Length ? new SquelchBook(accts, toons, global) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, uint> AcctChart(ref WireCursor cursor)
    {
        (ushort tally, ushort bins) = QuestTrackerNotices.DigestPreamble(ref cursor);
        if (bins is 0 && tally is not 0)
            throw new FormatException("not valid account squelch table");
        var chart = new Dictionary<string, uint>(tally, StringComparer.OrdinalIgnoreCase);
        for (int idx = 0; idx < tally; ++idx)
        {
            string label = cursor.String16L();
            chart[label] = cursor.Word();
        }
        return chart;
    }

    private static IReadOnlyDictionary<uint, SquelchFacts> ToonChart(ref WireCursor cursor)
    {
        (ushort tally, ushort bins) = QuestTrackerNotices.DigestPreamble(ref cursor);
        if (bins is 0 && tally is not 0)
            throw new FormatException("not valid character squelch table");
        var chart = new Dictionary<uint, SquelchFacts>(tally);
        for (int idx = 0; idx < tally; ++idx)
        {
            uint ident = cursor.Word();
            chart[ident] = SquelchDetails(ref cursor);
        }
        return chart;
    }

    // A vlong bit set of message types (up to four words), the name, and the account-wide flag
    private static SquelchFacts SquelchDetails(ref WireCursor cursor)
    {
        uint words = cursor.Word();
        if (words > UpperVlongWords)
            throw new FormatException("not valid vlong word count");
        HashSet<uint> kinds = new HashSet<uint>();
        for (uint w = 0; w < words; ++w)
        {
            uint bitset = cursor.Word();
            for (int bit = 0; bit < 32; ++bit)
            {
                if ((bitset & (1u << bit)) is not 0)
                    kinds.Add(w * 32 + (uint)bit);
            }
        }
        string label = cursor.String16L();
        return new SquelchFacts(label, cursor.Word() is not 0, kinds);
    }

    private static uint[] Words(ref WireCursor cursor)
    {
        uint tally = cursor.Word();
        if (tally > UpperRosterLen)
            throw new FormatException("not valid packed list count");
        uint[] roster = new uint[tally];
        for (int idx = 0; idx < roster.Length; ++idx)
            roster[idx] = cursor.Word();
        return roster;
    }
}
