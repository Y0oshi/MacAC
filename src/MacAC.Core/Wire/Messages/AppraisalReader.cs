using MacAC.Mechanics.Gear;

namespace MacAC.Wire.Messages;

public static class AppraisalReader
{
    [Flags]
    public enum AppraisalFlags : uint
    {
        None = 0x0000_0000,
        IntStatsTable = 0x0000_0001,
        BoolStatsTable = 0x0000_0002,
        FloatStatsTable = 0x0000_0004,
        StringStatsTable = 0x0000_0008,
        SpellBook = 0x0000_0010,
        WeaponProfile = 0x0000_0020,
        HookProfile = 0x0000_0040,
        ArmorProfile = 0x0000_0080,
        CreatureProfile = 0x0000_0100,
        ArmorEnchantmentBitfield = 0x0000_0200,
        ResistEnchantmentBitfield = 0x0000_0400,
        WeaponEnchantmentBitfield = 0x0000_0800,
        DidStatsTable = 0x0000_1000,
        Int64StatsTable = 0x0000_2000,
        ArmorLevels = 0x0000_4000,
    }

    private const uint BeastShowsAttrs = 0x08u;
    private const uint BeastShowsBuffs = 0x01u;
    private const uint UpperArcanumBook = 4096;

    public readonly record struct ArmorSheet(
        float SlashingProtection,
        float PiercingProtection,
        float BludgeoningProtection,
        float ColdProtection,
        float FireProtection,
        float AcidProtection,
        float NetherProtection,
        float LightningProtection);

    public readonly record struct ArmorTier(int Head, int Chest, int Abdomen, int UpperArm, int LowerArm, int Hand, int UpperLeg, int LowerLeg, int Foot);

    public readonly record struct WeaponSheet(
        uint DamageType,
        uint WeaponTime,
        uint WeaponSkill,
        uint Damage,
        double DamageVariance,
        double DamageMod,
        double WeaponLength,
        double MaxVelocity,
        double WeaponOffense,
        uint MaxVelocityEstimated);

    public readonly record struct TapSheet(uint Flags, uint ValidLocations, uint AmmoType);

    public readonly record struct BeastSheet(
        uint Flags,
        uint Health,
        uint HealthMax,
        uint? Strength,
        uint? Endurance,
        uint? Quickness,
        uint? Coordination,
        uint? Focus,
        uint? Self,
        uint? Stamina,
        uint? Mana,
        uint? StaminaMax,
        uint? ManaMax,
        ushort? AttributeHighlights,
        ushort? AttributeColors);

    public readonly record struct WireParsed(
        uint Guid,
        AppraisalFlags Flags,
        bool Success,
        TraitBundle Properties,
        uint[] SpellBook,
        ArmorSheet? ArmorProfile,
        BeastSheet? CreatureProfile,
        WeaponSheet? WeaponProfile,
        TapSheet? HookProfile,
        ArmorTier? ArmorLevels,
        (ushort Highlight, ushort Color)? ArmorEnchantments,
        (ushort Highlight, ushort Color)? WeaponEnchantments,
        (ushort Highlight, ushort Color)? ResistEnchantments);

    public static WireParsed? TryParse(ReadOnlySpan<byte> cargo)
    {
        var cursor = new WireCursor(cargo);
        if (!cursor.Has(12))
            return null;
        uint oid = cursor.U32();
        AppraisalFlags flagSet = (AppraisalFlags)cursor.U32();
        bool success = cursor.U32() is not 0;
        bool Has(AppraisalFlags bit) => (flagSet & bit) != 0;

        TraitBundle traits = new TraitBundle();
        uint[] arcana = [];
        ArmorSheet? armor = null;
        BeastSheet? beast = null;
        WeaponSheet? weapon = null;
        TapSheet? tap = null;
        ArmorTier? tiers = null;
        (ushort, ushort)? armorEnc = null, weaponEnc = null, resistEnc = null;
        try
        {
            if (Has(AppraisalFlags.IntStatsTable)) Chart(ref cursor, traits.Ints, static (ref WireCursor r) => (int)r.Word());
            if (Has(AppraisalFlags.Int64StatsTable)) Chart(ref cursor, traits.Int64s, static (ref WireCursor r) => Long(ref r));
            if (Has(AppraisalFlags.BoolStatsTable)) Chart(ref cursor, traits.Bools, static (ref WireCursor r) => r.Word() != 0);
            if (Has(AppraisalFlags.FloatStatsTable)) Chart(ref cursor, traits.Floats, static (ref WireCursor r) => Double(ref r));
            if (Has(AppraisalFlags.StringStatsTable)) Chart(ref cursor, traits.Texts, static (ref WireCursor r) => Text(ref r));
            if (Has(AppraisalFlags.DidStatsTable)) Chart(ref cursor, traits.BlobIdents, static (ref WireCursor r) => r.Word());
            if (Has(AppraisalFlags.SpellBook)) arcana = SpellBook(ref cursor);
            if (Has(AppraisalFlags.ArmorProfile)) armor = new ArmorSheet(Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor), Single(ref cursor));
            if (Has(AppraisalFlags.CreatureProfile)) beast = Creature(ref cursor);
            if (Has(AppraisalFlags.WeaponProfile)) weapon = new WeaponSheet(cursor.Word(), cursor.Word(), cursor.Word(), cursor.Word(), Double(ref cursor), Double(ref cursor), Double(ref cursor), Double(ref cursor), Double(ref cursor), cursor.Word());
            if (Has(AppraisalFlags.HookProfile)) tap = new TapSheet(cursor.Word(), cursor.Word(), cursor.Word());
            if (Has(AppraisalFlags.ArmorEnchantmentBitfield)) armorEnc = Bitfield(ref cursor);
            if (Has(AppraisalFlags.WeaponEnchantmentBitfield)) weaponEnc = Bitfield(ref cursor);
            if (Has(AppraisalFlags.ResistEnchantmentBitfield)) resistEnc = Bitfield(ref cursor);
            if (Has(AppraisalFlags.ArmorLevels)) tiers = new ArmorTier((int)cursor.Word(), (int)cursor.Word(), (int)cursor.Word(), (int)cursor.Word(), (int)cursor.Word(), (int)cursor.Word(), (int)cursor.Word(), (int)cursor.Word(), (int)cursor.Word());
        }
        catch (FormatException)
        {
            return null;
        }

        return new WireParsed(oid, flagSet, success, traits, arcana, armor, beast, weapon, tap, tiers, armorEnc, weaponEnc, resistEnc);
    }

    private delegate T ValueReader<T>(ref WireCursor c);

    // A packed hash table: u16 count + u16 buckets, then key/value rows
    private static void Chart<T>(ref WireCursor cursor, Dictionary<uint, T> into, ValueReader<T> val)
    {
        cursor.Demand(4, "table header");
        ushort tally = cursor.U16();
        cursor.Skip(2); // bucket count
        for (int idx = 0; idx < tally; ++idx)
        {
            uint tag = cursor.Word();
            into[tag] = val(ref cursor);
        }
    }

    private static uint[] SpellBook(ref WireCursor cursor)
    {
        cursor.Demand(4, "spellbook count");
        uint tally = cursor.U32();
        if (tally > UpperArcanumBook)
            throw new FormatException("unreasonable spellbook count");
        if (cursor.Left < tally * 4)
            throw new FormatException("truncated spellbook body");
        uint[] arcana = new uint[tally];
        for (int idx = 0; idx < arcana.Length; ++idx)
            arcana[idx] = cursor.U32();
        return arcana;
    }

    private static BeastSheet Creature(ref WireCursor cursor)
    {
        uint flagSet = cursor.Word();
        uint health = cursor.Word();
        uint healthUpper = cursor.Word();
        uint? text = null, finish = null, quick = null, coord = null, focus = null, self = null, sta = null, mana = null, staUpper = null, manaUpper = null;
        if ((flagSet & BeastShowsAttrs) is not 0)
        {
            text = cursor.Word(); finish = cursor.Word(); quick = cursor.Word(); coord = cursor.Word(); focus = cursor.Word(); self = cursor.Word();
            sta = cursor.Word(); mana = cursor.Word(); staUpper = cursor.Word(); manaUpper = cursor.Word();
        }
        ushort? highlights = null, tints = null;
        if ((flagSet & BeastShowsBuffs) is not 0)
        {
            cursor.Demand(4, "creature buffs");
            highlights = cursor.U16();
            tints = cursor.U16();
        }
        return new BeastSheet(flagSet, health, healthUpper, text, finish, quick, coord, focus, self, sta, mana, staUpper, manaUpper, highlights, tints);
    }

    private static (ushort Highlight, ushort Color) Bitfield(ref WireCursor cursor)
    {
        cursor.Demand(4, "enchantment bitfield");
        return (cursor.U16(), cursor.U16());
    }

    private static float Single(ref WireCursor cursor)
    {
        cursor.Demand(4, "f32");
        return cursor.F32();
    }

    private static double Double(ref WireCursor cursor)
    {
        cursor.Demand(8, "f64");
        return cursor.F64();
    }

    private static long Long(ref WireCursor cursor)
    {
        cursor.Demand(8, "i64");
        return cursor.Idx64();
    }

    private static string Text(ref WireCursor cursor)
    {
        cursor.Demand(2, "string length");
        ushort len = cursor.U16();
        cursor.Demand(len, "string body");
        var raw = cursor.Rest.Slice(0, len);
        string phrase = WireEncodings.Windows1252.GetString(raw);
        cursor.Skip(len + ((4 - ((2 + len) & 3)) & 3));
        return phrase;
    }
}
