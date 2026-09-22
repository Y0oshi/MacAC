namespace MacAC.Wire.Messages;

public static partial class PlaySignals
{
    private const uint UpperBanners = 65_536;

    public readonly record struct ToonAckRequest(uint Type, uint ContextId, string Message);

    public static ToonAckRequest? DecodeToonAckReq(ReadOnlySpan<byte> cargo)
    {
        if (cargo.Length < 8)
            return null;
        return Guarded<ToonAckRequest>(cargo, static (ref WireCursor cursor) =>
        {
            uint kind = cursor.U32();
            uint ctx = cursor.U32();
            return new ToonAckRequest(kind, ctx, cursor.String16L());
        });
    }

    public readonly record struct ToonAckFinished(uint Type, uint ContextId);

    public static ToonAckFinished? DecodeToonAckDone(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(8) ? new ToonAckFinished(cursor.U32(), cursor.U32()) : null;
    }

    public enum AckKind : uint
    {
        SwearAllegiance = 1,
        AlterSkill = 2,
        AlterAttribute = 3,
        Fellowship = 4,
        CraftInteraction = 5,
        Augmentation = 6,
        YesNo = 7,
    }

    public readonly record struct ConfirmReply(AckKind Type, uint ContextId, bool Accepted);

    public static ConfirmReply? DecodeAckResponse(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(12) ? new ConfirmReply((AckKind)cursor.U32(), cursor.U32(), cursor.U32() is not 0) : null;
    }

    public readonly record struct FellowRow(
        uint Guid,
        uint CpCache,
        uint LumCache,
        uint Level,
        uint MaxHealth,
        uint MaxStamina,
        uint MaxMana,
        uint CurrentHealth,
        uint CurrentStamina,
        uint CurrentMana,
        uint ShareLoot,
        string Name);

    /// <summary>One row of the departed-fellows table.</summary>
    public readonly record struct FellowsDeparted(uint Guid, int DepartedTimestamp);

    public readonly record struct FellowshipWholeRefresh(
        IReadOnlyList<FellowRow> Members,
        string Name,
        uint LeaderGuid,
        bool ShareXp,
        bool EvenXpSplit,
        bool OpenFellow,
        bool Locked,
        IReadOnlyList<FellowsDeparted> Departed);

    public static FellowshipWholeRefresh? DecodeFellowshipWholeRefresh(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            ushort participantTally = cursor.Half();
            cursor.Half(); // bucket count
            List<FellowRow> participants = new List<FellowRow>(participantTally);
            for (int idx = 0; idx < participantTally; ++idx)
            {
                uint oid = cursor.Word();
                participants.Add(Fellow(ref cursor, oid));
            }
            string label = cursor.String16L();
            uint leader = cursor.Word();
            bool portionXp = cursor.Word() is not 0u;
            bool evenDivide = cursor.Word() is not 0u;
            bool open = cursor.Word() is not 0u;
            bool bolted = cursor.Word() is not 0u;

            ushort departedTally = cursor.Half();
            cursor.Half();
            var departed = new List<FellowsDeparted>(departedTally);
            for (int idx = 0; idx < departedTally; ++idx)
            {
                uint oid = cursor.Word();
                departed.Add(new FellowsDeparted(oid, unchecked((int)cursor.Word())));
            }
            return new FellowshipWholeRefresh(participants, label, leader, portionXp, evenDivide, open, bolted, departed);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public readonly record struct FellowshipRefreshFellow(uint MemberGuid, FellowRow Member, uint UpdateType);

    public static FellowshipRefreshFellow? DecodeFellowshipRefreshFellow(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            uint oid = cursor.Word();
            FellowRow participant = Fellow(ref cursor, oid);
            return new FellowshipRefreshFellow(oid, participant, cursor.Word());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static FellowsQuit? DecodeFellowshipQuit(ReadOnlySpan<byte> cargo) => LeadWord(cargo) is { } oid ? new FellowsQuit(oid) : null;

    public readonly record struct FellowsQuit(uint QuitterGuid);

    public static FellowsDismissed? DecodeFellowshipDismiss(ReadOnlySpan<byte> cargo) => LeadWord(cargo) is { } oid ? new FellowsDismissed(oid) : null;

    public readonly record struct FellowsDismissed(uint DismissedGuid);

    public static bool DecodeFellowshipDisband(ReadOnlySpan<byte> cargo) => true;

    public static FellowshipFellowRefreshFinished DecodeFellowshipFellowRefreshDone(ReadOnlySpan<byte> cargo) => new(LeadWord(cargo));

    public readonly record struct FellowshipFellowRefreshFinished(uint? RawValue);

    public static FellowshipFellowStatsFinished DecodeFellowshipFellowStatsDone(ReadOnlySpan<byte> cargo) => new(LeadWord(cargo));

    public readonly record struct FellowshipFellowStatsFinished(uint? RawValue);

    public static TitleTable? DecodeToonBannerChart(ReadOnlySpan<byte> cargo)
    {
        try
        {
            WireCursor cursor = new WireCursor(cargo);
            cursor.Word(); // leading marker word
            uint readout = cursor.Word();
            uint tally = cursor.Word();
            if (tally > UpperBanners)
                return null;
            uint[] banners = new uint[tally];
            for (int idx = 0; idx < banners.Length; ++idx)
                banners[idx] = cursor.Word();
            return new TitleTable(readout, banners);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public readonly record struct TitleTable(uint DisplayTitleId, IReadOnlyList<uint> TitleIds);

    public static RefreshBanner? DecodeRefreshBanner(ReadOnlySpan<byte> cargo)
    {
        try
        {
            WireCursor cursor = new WireCursor(cargo);
            uint banner = cursor.Word();
            return new RefreshBanner(banner, cursor.Word() is not 0u);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public readonly record struct RefreshBanner(uint TitleId, bool SetAsDisplay);

    public static AllegianceSigninNotice? DecodeAllegianceSigninNotification(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        return cursor.Has(8) ? new AllegianceSigninNotice(cursor.U32(), cursor.U32() is not 0u) : null;
    }

    public readonly record struct AllegianceSigninNotice(uint CharacterGuid, bool IsLoggedIn);

    public static uint? DecodeAllegianceRefreshDone(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public static uint? DecodeAllegianceRefreshAborted(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    // ShareLoot is kept raw: retail tests it against zero, never against one
    private static FellowRow Fellow(ref WireCursor cursor, uint oid)
    {
        return new(oid, cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), cursor.String16L());
    }
}
