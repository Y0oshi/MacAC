namespace MacAC.Wire.Messages;

public static partial class CommandReplies
{
    private const uint LoggedInBit = 0x1u;
    private const uint HasAllegianceAgeBit = 0x4u;
    private const uint HasDenseTierBit = 0x8u;
    private const uint MayPassupExperienceBit = 0x10u;

    public readonly record struct AllegianceMemberRow(
        uint CharacterId,
        uint ParentGuid,
        bool IsLoggedIn,
        string Name,
        ushort Rank = 0,
        uint Level = 0,
        ushort Loyalty = 0,
        ushort Leadership = 0,
        uint CpCached = 0,
        uint CpTithed = 0,
        byte Gender = 0,
        byte HeritageGroup = 0,
        bool MayPassupExperience = false);

    // Lookups over a monarch + member rows; shared by both reply shapes
    internal static class AllegianceProfileIndex
    {
        public static AllegianceMemberRow? SeekBlob(AllegianceMemberRow? monarch, IReadOnlyList<AllegianceMemberRow> records, uint oid)
        {
            if (monarch is { } m && m.CharacterId == oid)
                return monarch;
            foreach (AllegianceMemberRow capture in records)
            {
                if (capture.CharacterId == oid)
                    return capture;
            }
            return null;
        }

        public static AllegianceMemberRow? SeekPatron(AllegianceMemberRow? monarch, IReadOnlyList<AllegianceMemberRow> records, uint oid)
        {
            if (monarch is { } m && m.CharacterId == oid)
                return null;
            foreach (AllegianceMemberRow capture in records)
            {
                if (capture.CharacterId == oid)
                    return SeekBlob(monarch, records, capture.ParentGuid);
            }
            return null;
        }

        // Newest first, as retail prints them
        public static IEnumerable<AllegianceMemberRow> SeekVassals(IReadOnlyList<AllegianceMemberRow> records, uint oid)
        {
            for (int idx = records.Count - 1; idx >= 0; --idx)
            {
                if (records[idx].ParentGuid == oid)
                    yield return records[idx];
            }
        }
    }

    public readonly record struct AllegianceDetailsReply(
        uint TargetGuid,
        uint TotalMembers,
        uint TotalVassals,
        ushort RecordCount,
        string AllegianceName,
        AllegianceMemberRow? Monarch,
        IReadOnlyList<AllegianceMemberRow> Records,
        ushort OldVersion = 0,
        string Motd = "",
        string MotdSetBy = "",
        uint ChatRoomId = 0,
        uint NameLastSetTime = 0,
        bool IsLocked = false,
        uint ApprovedVassal = 0)
    {
        public AllegianceMemberRow? SearchBlob(uint oid) => AllegianceProfileIndex.SeekBlob(Monarch, Records, oid);
        public AllegianceMemberRow? SearchPatron(uint oid) => AllegianceProfileIndex.SeekPatron(Monarch, Records, oid);
        public IEnumerable<AllegianceMemberRow> SearchVassals(uint oid) => AllegianceProfileIndex.SeekVassals(Records, oid);
    }

    public readonly record struct AllegianceRefresh(
        uint Rank,
        uint TotalMembers,
        uint TotalVassals,
        ushort RecordCount,
        string AllegianceName,
        AllegianceMemberRow? Monarch,
        IReadOnlyList<AllegianceMemberRow> Records,
        ushort OldVersion = 0,
        string Motd = "",
        string MotdSetBy = "",
        uint ChatRoomId = 0,
        uint NameLastSetTime = 0,
        bool IsLocked = false,
        uint ApprovedVassal = 0)
    {
        public AllegianceMemberRow? SeekData(uint oid) => AllegianceProfileIndex.SeekBlob(Monarch, Records, oid);
        public AllegianceMemberRow? FindPatron(uint oid) => AllegianceProfileIndex.SeekPatron(Monarch, Records, oid);
        public IEnumerable<AllegianceMemberRow> FindVassals(uint oid) => AllegianceProfileIndex.SeekVassals(Records, oid);
    }

    // The profile fields common to both replies
    private readonly record struct Profile(
        uint TotalMembers,
        uint TotalVassals,
        ushort RecordCount,
        ushort OldVersion,
        string Motd,
        string MotdSetBy,
        uint ChatRoomId,
        string AllegianceName,
        uint NameLastSetTime,
        bool IsLocked,
        uint ApprovedVassal,
        AllegianceMemberRow? Monarch,
        IReadOnlyList<AllegianceMemberRow> Records);

    public static AllegianceDetailsReply? DecodeAllegianceDetailsResponse(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            uint mark = cursor.Word();
            if (ScanProfile(ref cursor) is not { } profile)
                return null;
            return new AllegianceDetailsReply(
                mark, profile.TotalMembers, profile.TotalVassals, profile.RecordCount, profile.AllegianceName, profile.Monarch, profile.Records,
                profile.OldVersion, profile.Motd, profile.MotdSetBy, profile.ChatRoomId, profile.NameLastSetTime, profile.IsLocked, profile.ApprovedVassal);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static AllegianceRefresh? DecodeAllegianceRefresh(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            uint grade = cursor.Word();
            if (ScanProfile(ref cursor) is not { } profile)
                return null;
            return new AllegianceRefresh(
                grade, profile.TotalMembers, profile.TotalVassals, profile.RecordCount, profile.AllegianceName, profile.Monarch, profile.Records,
                profile.OldVersion, profile.Motd, profile.MotdSetBy, profile.ChatRoomId, profile.NameLastSetTime, profile.IsLocked, profile.ApprovedVassal);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static IEnumerable<string> ComposeAllegianceDetailsStrokes(AllegianceDetailsReply response)
    {
        if (response.SearchBlob(response.TargetGuid) is not { } self)
            yield break;

        yield return "Note: An asterisk (*) indicates that the character is currently online.";
        yield return $"Allegiance information for {self.Name}{OnlineMarker(self)}:";
        if (response.SearchPatron(response.TargetGuid) is { } patron)
            yield return $"   Patron: {patron.Name}{OnlineMarker(patron)}";

        bool headed = false;
        foreach (AllegianceMemberRow vassal in response.SearchVassals(response.TargetGuid))
        {
            if (!headed)
            {
                yield return "   Vassals: ";
                headed = true;
            }
            yield return $"      {vassal.Name}{OnlineMarker(vassal)}";
        }
    }

    private static Profile? ScanProfile(ref WireCursor cursor)
    {
        uint sumParticipants = cursor.Word();
        uint sumVassals = cursor.Word();
        ushort captureTally = cursor.Half();
        ushort ver = cursor.Half();

        // Version gates in the order retail packs them; skipped fields are read and dropped.
        if (ver >= 6)
        {
            ushort officers = cursor.Half();
            cursor.Half();
            for (int idx = 0; idx < officers; ++idx)
            {
                cursor.Word(); // guid
                cursor.Word(); // officer level
            }
        }
        else if (ver >= 1)
        {
            cursor.Word(); // old single spokesperson id
        }
        if (ver >= 9)
        {
            int banners = unchecked((int)cursor.Word());
            for (int idx = 0; idx < banners; ++idx)
                cursor.String16L();
        }
        if (ver >= 2)
        {
            for (int idx = 0; idx < 4; ++idx)
                cursor.Word(); // monarch/spokes broadcast time and counts
        }

        string motd = "", motdSetBy = "";
        if (ver >= 3)
        {
            motd = cursor.String16L();
            motdSetBy = cursor.String16L();
        }
        uint commsHall = ver >= 4 ? cursor.Word() : 0;
        if (ver >= 7)
        {
            for (int idx = 0; idx < 8; ++idx)
                cursor.Word();
        }
        string label = "";
        uint labelSetAt = 0;
        if (ver >= 8)
        {
            label = cursor.String16L();
            labelSetAt = cursor.Word();
        }
        bool bolted = ver >= 10 && cursor.Word() is not 0u;
        uint approvedVassal = ver >= 11 ? cursor.Word() : 0;

        AllegianceMemberRow? monarch = null;
        var records = new List<AllegianceMemberRow>();
        if (captureTally > 0)
        {
            var trunk = ScanParticipant(ref cursor, ancestorOid: 0u);
            if (trunk.CharacterId is 0u)
                return null;
            HashSet<uint> recognized = new HashSet<uint> { trunk.CharacterId };
            for (int idx = 1; idx < captureTally; ++idx)
            {
                uint ancestor = cursor.Word();
                var rank = ScanParticipant(ref cursor, ancestor);
                bool orphanOrDuplicate = rank.CharacterId is 0u || !recognized.Contains(ancestor) || ancestor == rank.CharacterId || recognized.Contains(rank.CharacterId);
                if (orphanOrDuplicate)
                    return null;
                recognized.Add(rank.CharacterId);
                records.Add(rank);
            }
            monarch = trunk with { MayPassupExperience = false };
        }

        return new Profile(sumParticipants, sumVassals, captureTally, ver, motd, motdSetBy, commsHall, label, labelSetAt, bolted, approvedVassal, monarch, records);
    }

    private static AllegianceMemberRow ScanParticipant(ref WireCursor cursor, uint ancestorOid)
    {
        uint toonIdent = cursor.Word();
        uint cpStashed = cursor.Word();
        uint cpTithed = cursor.Word();
        uint bitset = cursor.Word();
        byte gender = cursor.Octet();
        byte lineage = cursor.Octet();
        ushort grade = cursor.Half();
        uint tier = (bitset & HasDenseTierBit) is not 0u ? cursor.Word() : 0;
        ushort loyalty = cursor.Half();
        ushort leadership = cursor.Half();
        cursor.Word(); // allegiance age, or the legacy uTimeOnline low half
        cursor.Word(); // (same) or the legacy high half
        string label = cursor.String16L();
        bool mayPassup = (bitset & MayPassupExperienceBit) is not 0u || (bitset & HasDenseTierBit) is 0u;
        return new AllegianceMemberRow(toonIdent, ancestorOid, (bitset & LoggedInBit) is not 0u, label, grade, tier, loyalty, leadership, cpStashed, cpTithed, gender, lineage, mayPassup);
    }

    private static string OnlineMarker(AllegianceMemberRow participant) => participant.IsLoggedIn ? " *" : "";
}
