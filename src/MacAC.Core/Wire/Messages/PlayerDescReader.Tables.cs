namespace MacAC.Wire.Messages;

/// <summary>The hash tables, vectors and inventory manifests inside a PlayerDescription.</summary>
public static partial class PlayerDescReader
{
    private delegate T RowReader<T>(ref WireCursor c);

    // A packed hash table: u16 count, u16 buckets, then u32 key / value rows
    private static void Chart<T>(ref WireCursor cursor, Dictionary<uint, T> into, RowReader<T> val)
    {
        cursor.Demand(4, "table header");
        ushort tally = cursor.U16();
        cursor.Skip(2);
        for (int idx = 0; idx < tally; ++idx)
        {
            uint tag = cursor.Word();
            into[tag] = val(ref cursor);
        }
    }

    // A packed list with the same header, where the key is part of the row
    private static void Chart<T>(ref WireCursor cursor, List<T> into, RowReader<T> rank)
    {
        cursor.Demand(4, "table header");
        ushort tally = cursor.U16();
        cursor.Skip(2);
        for (int idx = 0; idx < tally; ++idx)
            into.Add(rank(ref cursor));
    }

    private static long Long(ref WireCursor cursor)
    {
        cursor.Demand(8, "i64");
        return cursor.Idx64();
    }

    private static double Double(ref WireCursor cursor)
    {
        cursor.Demand(8, "f64");
        return cursor.F64();
    }

    private static float Single(ref WireCursor cursor)
    {
        cursor.Demand(4, "f32");
        return cursor.F32();
    }

    private static string Text(ref WireCursor cursor)
    {
        cursor.Demand(2, "string length");
        ushort len = cursor.U16();
        cursor.Demand(len, "string body");
        string phrase = WireEncodings.Windows1252.GetString(cursor.Rest.Slice(0, len));
        cursor.Skip(len + ((4 - ((2 + len) & 3)) & 3));
        return phrase;
    }

    private static RealmLocus Position(ref WireCursor cursor)
    {
        return new(cursor.Word(), Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor));
    }

    // Skill row: id, u16 ranks + u16 filler, then status/xp/init/resistance and the last-used time
    private static SkillSlotRow Skill(ref WireCursor cursor)
    {
        uint ident = cursor.Word();
        cursor.Demand(4, "skill ranks/const");
        uint ranks = cursor.U16();
        cursor.Skip(2);
        return new SkillSlotRow(ident, ranks, cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), Double(ref cursor));
    }

    // Primary attributes (1-6) carry ranks/start/xp; vitals (7-9) add the current value
    private static void Attributes(ref WireCursor cursor, List<AttributeRow> into)
    {
        uint present = cursor.Word();
        for (uint ident = 1; ident <= 9; ++ident)
        {
            if ((present & (1u << (int)(ident - 1))) is 0)
                continue;
            uint ranks = cursor.Word();
            uint begin = cursor.Word();
            uint xp = cursor.Word();
            uint? latest = ident >= 7 ? cursor.Word() : null;
            into.Add(new AttributeRow(ident, ranks, begin, xp, latest));
        }
    }

    // A mask word, then the multiplicative, additive and cooldown lists, then a lone vitae record
    private static void Enchantments(ref WireCursor cursor, List<EnchantmentRow> into)
    {
        if (!cursor.Has(4))
            return;
        EnchantmentBitmask bitmask = (EnchantmentBitmask)cursor.U32();
        if ((bitmask & EnchantmentBitmask.Multiplicative) != 0)
            into.AddRange(EnchantmentReader.ScanRoster(ref cursor, EnchantmentShelf.Multiplicative));
        if ((bitmask & EnchantmentBitmask.Additive) != 0)
            into.AddRange(EnchantmentReader.ScanRoster(ref cursor, EnchantmentShelf.Additive));
        if ((bitmask & EnchantmentBitmask.Cooldown) != 0)
            into.AddRange(EnchantmentReader.ScanRoster(ref cursor, EnchantmentShelf.Cooldown));
        if ((bitmask & EnchantmentBitmask.Vitae) != 0)
            into.Add(EnchantmentReader.Read(ref cursor, EnchantmentShelf.Vitae));
    }

    // Inventory (guid, container type ≤ 2) then equipment (guid, location, priority); false on any
    // shortfall
    private static bool Manifests(ref WireCursor cursor, List<InventoryRow> satchel, List<WornRow> equipped)
    {
        satchel.Clear();
        equipped.Clear();
        if (!cursor.Has(4))
            return false;
        uint gearList = cursor.U32();
        if (gearList > UpperRosterTally)
            return false;
        for (uint idx = 0; idx < gearList; ++idx)
        {
            if (!cursor.Has(8))
                return false;
            uint oid = cursor.U32();
            uint kind = cursor.U32();
            if (kind > 2)
                return false;
            satchel.Add(new InventoryRow(oid, kind));
        }

        if (!cursor.Has(4))
            return false;
        uint worn = cursor.U32();
        if (worn > UpperRosterTally)
            return false;
        for (uint idx = 0; idx < worn; ++idx)
        {
            if (!cursor.Has(12))
                return false;
            equipped.Add(new WornRow(cursor.U32(), cursor.U32(), cursor.U32()));
        }
        return true;
    }

    // Tries every word-aligned offset from begin for a manifest pair that ends exactly at the payload
    // end
    private static bool SeekManifests(WireCursor cursor, int begin, out int manifestBegin, out int finish, List<InventoryRow> satchel, List<WornRow> equipped)
    {
        manifestBegin = finish = 0;
        satchel.Clear();
        equipped.Clear();
        if (begin + 8 > cursor.Length)
            return false;

        int contender = begin;
        if ((contender & 3) is not 0)
            contender += 4 - (contender & 3);
        for (int previous = cursor.Length - 8; contender <= previous; contender += 4)
        {
            var sensor = cursor;
            sensor.Skip(contender - sensor.At);
            List<InventoryRow> gearList = new List<InventoryRow>();
            List<WornRow> worn = new List<WornRow>();
            if (Manifests(ref sensor, gearList, worn) && sensor.At == cursor.Length)
            {
                manifestBegin = contender;
                finish = sensor.At;
                satchel.AddRange(gearList);
                equipped.AddRange(worn);
                return true;
            }
        }
        return false;
    }
}
