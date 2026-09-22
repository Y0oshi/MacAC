using MacAC.Mechanics.Gear;

namespace MacAC.Wire.Messages;

public static partial class PlaySignals
{
    private const int UpperPaymentPrealloc = 4096;

    public readonly record struct HouseRefreshRestrictions(byte Sequence, uint SenderId, HouseAccessRecord Restrictions);

    public static HouseRefreshRestrictions? DecodeHouseRefreshRestrictions(ReadOnlySpan<byte> cargo)
    {
        WireCursor cursor = new WireCursor(cargo);
        if (!cursor.Has(21))
            return null;
        byte series = cursor.U8();
        uint sender = cursor.U32();
        cursor.Skip(4); // RestrictionDB version
        uint open = cursor.U32();
        uint monarch = cursor.U32();
        uint tally = cursor.U32() & 0xFFFFFFu;
        if (cursor.Left < (long)tally * 8)
            return null;

        var guests = new Dictionary<uint, uint>((int)tally);
        for (uint idx = 0; idx < tally; ++idx)
        {
            uint guest = cursor.U32();
            guests[guest] = cursor.U32();
        }
        return new HouseRefreshRestrictions(series, sender, new HouseAccessRecord(OpenToPublic: open is not 0, AllegianceMonarchIdent: monarch, Guests: guests));
    }

    public readonly record struct HouseDue(int Num, int Paid, uint WeenieID, string Name, string PluralName);

    public readonly record struct HouseBlob(uint BuyTime, uint RentTime, uint Type, bool MaintenanceFree, IReadOnlyList<HouseDue> Buy, IReadOnlyList<HouseDue> Rent, ObjectCreation.RemotePosition Position);

    public static HouseBlob? DecodeHouseBlob(ReadOnlySpan<byte> cargo)
    {
        try
        {
            var cursor = new WireCursor(cargo);
            if (!cursor.Has(16))
                return null;
            uint purchaseMoment = cursor.U32();
            uint rentMoment = cursor.U32();
            uint kind = cursor.U32();
            bool maintenanceSpare = cursor.U32() is not 0;
            if (Dues(ref cursor) is not { } purchase || Dues(ref cursor) is not { } rent)
                return null;
            if (!cursor.Has(32))
                return null;
            var locus = new ObjectCreation.RemotePosition(cursor.U32(), cursor.F32(), cursor.F32(), cursor.F32(), cursor.F32(), cursor.F32(), cursor.F32(), cursor.F32());
            return new HouseBlob(purchaseMoment, rentMoment, kind, maintenanceSpare, purchase, rent, locus);
        }
        catch
        {
            return null;
        }
    }

    public static uint? DecodeHouseCondition(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public static uint? DecodeRefreshRentMoment(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public static IReadOnlyList<HouseDue>? DecodeRefreshRentPayment(ReadOnlySpan<byte> cargo)
    {
        var cursor = new WireCursor(cargo);
        return Dues(ref cursor);
    }

    // count, then (num, paid, weenie, name, plural) rows; null when a row is cut, throws on a cut
    // string
    private static List<HouseDue>? Dues(ref WireCursor cursor)
    {
        if (!cursor.Has(4))
            return null;
        uint tally = cursor.U32();
        List<HouseDue> dues = new List<HouseDue>((int)Math.Min(tally, UpperPaymentPrealloc));
        for (uint idx = 0; idx < tally; ++idx)
        {
            if (!cursor.Has(8))
                return null;
            int num = cursor.I32();
            int paid = cursor.I32();
            if (!cursor.Has(4))
                return null;
            uint weenie = cursor.U32();
            string label = cursor.String16L();
            dues.Add(new HouseDue(num, paid, weenie, label, cursor.String16L()));
        }
        return dues;
    }
}
