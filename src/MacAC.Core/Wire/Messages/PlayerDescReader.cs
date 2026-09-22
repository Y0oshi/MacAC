using MacAC.Mechanics.Gear;

namespace MacAC.Wire.Messages;

public static partial class PlayerDescReader
{
    [Flags]
    public enum DescPropertyBits : uint
    {
        None = 0x0000,
        PropertyInt32 = 0x0001,
        PropertyBool = 0x0002,
        PropertyDouble = 0x0004,
        PropertyDid = 0x0008,
        PropertyString = 0x0010,
        Position = 0x0020,
        PropertyIid = 0x0040,
        PropertyInt64 = 0x0080,
    }

    [Flags]
    public enum DescVectorBits : uint
    {
        None = 0x0000,
        Attribute = 0x0001,
        Skill = 0x0002,
        Spell = 0x0100,
        Enchantment = 0x0200,
    }

    public readonly record struct AttributeRow(uint AtType, uint Ranks, uint Start, uint Xp, uint? Current);

    public readonly record struct SkillSlotRow(uint SkillId, uint Ranks, uint Status, uint Xp, uint Init, uint Resistance, double LastUsed);

    public readonly record struct RealmLocus(uint LandblockId, float X, float Y, float Z, float Qw, float Qx, float Qy, float Qz);

    public readonly record struct EnchantmentRow(
        ushort SpellId,
        ushort Layer,
        ushort SpellCategory,
        ushort HasSpellSetId,
        uint PowerLevel,
        double StartTime,
        double Duration,
        uint CasterGuid,
        float DegradeModifier,
        float DegradeLimit,
        double LastTimeDegraded,
        uint StatModType,
        uint StatModKey,
        float StatModValue,
        uint? SpellSetId,
        EnchantmentShelf Bucket);

    public enum EnchantmentShelf : uint
    {
        Multiplicative = 1,
        Additive = 2,
        Vitae = 4,
        Cooldown = 8,
    }

    [Flags]
    public enum EnchantmentBitmask : uint
    {
        None = 0,
        Multiplicative = 0x01,
        Additive = 0x02,
        Vitae = 0x04,
        Cooldown = 0x08,
    }

    [Flags]
    public enum ToonKnobBlobBit : uint
    {
        None = 0,
        Shortcut = 0x00000001,
        SquelchList = 0x00000002,
        MultiSpellList = 0x00000004,
        DesiredComps = 0x00000008,
        ExtendedMultiSpellLists = 0x00000010,
        SpellbookFilters = 0x00000020,
        CharacterOptions2 = 0x00000040,
        TimestampFormat = 0x00000080,
        GenericQualitiesData = 0x00000100,
        GameplayOptions = 0x00000200,
        SpellLists8 = 0x00000400,
    }

    [Flags]
    public enum ToonOptions1 : uint
    {
        None = 0,
        AllowGive = 0x00000040,
        HearAllegianceChat = 0x40000000,
        DragItemOnPlayerOpensSecureTrade = 0x04000000,
        Default = 0x50C4A54A,
    }

    [Flags]
    public enum ToonOptions2 : uint
    {
        None = 0,
        HearGeneralChat = 0x00000100,
        HearTradeChat = 0x00000200,
        HearLFGChat = 0x00000400,
        HearRoleplayChat = 0x00000800,
        HearSocietyChat = 0x00080000,
    }

    public readonly record struct InventoryRow(uint Guid, uint ContainerType);

    public readonly record struct WornRow(uint Guid, uint EquipLocation, uint Priority);

    public readonly record struct Parsed(
        uint WeenieType,
        DescPropertyBits PropertyFlags,
        DescVectorBits VectorFlags,
        bool HasHealth,
        TraitBundle Properties,
        IReadOnlyDictionary<uint, RealmLocus> Positions,
        IReadOnlyList<AttributeRow> Attributes,
        IReadOnlyList<SkillSlotRow> Skills,
        IReadOnlyDictionary<uint, float> Spells,
        IReadOnlyList<EnchantmentRow> Enchantments,
        ToonKnobBlobBit OptionFlags,
        uint Options1,
        uint Options2,
        IReadOnlyList<HotbarSlot> Shortcuts,
        IReadOnlyList<IReadOnlyList<uint>> HotbarSpells,
        IReadOnlyList<(uint Id, uint Amount)> DesiredComps,
        uint SpellbookFilters,
        ReadOnlyMemory<byte> GameplayOptions,
        IReadOnlyList<InventoryRow> Inventory,
        IReadOnlyList<WornRow> Equipped,
        bool TrailerTruncated);

    private const uint DefaultGrimoireFilters = 0x3FFFu;
    private const uint UpperRosterTally = 10_000;
    private const int HotbarTabs = 8;

    public static Parsed? TryParse(ReadOnlySpan<byte> cargo)
    {
        if (cargo.Length < 8)
            return null;
        try
        {
            var cursor = new WireCursor(cargo);
            DescPropertyBits propBitset = (DescPropertyBits)cursor.Word();
            uint weenieKind = cursor.Word();
            bool HasProp(DescPropertyBits bit) => (propBitset & bit) != 0;

            TraitBundle traits = new TraitBundle();
            var loci = new Dictionary<uint, RealmLocus>();
            List<AttributeRow> attrs = new List<AttributeRow>();
            List<SkillSlotRow> aptitudes = new List<SkillSlotRow>();
            var arcana = new Dictionary<uint, float>();
            List<EnchantmentRow> enchantments = new List<EnchantmentRow>();

            if (HasProp(DescPropertyBits.PropertyInt32)) Chart(ref cursor, traits.Ints, static (ref WireCursor r) => (int)r.Word());
            if (HasProp(DescPropertyBits.PropertyInt64)) Chart(ref cursor, traits.Int64s, static (ref WireCursor r) => Long(ref r));
            if (HasProp(DescPropertyBits.PropertyBool)) Chart(ref cursor, traits.Bools, static (ref WireCursor r) => r.Word() != 0);
            if (HasProp(DescPropertyBits.PropertyDouble)) Chart(ref cursor, traits.Floats, static (ref WireCursor r) => Double(ref r));
            if (HasProp(DescPropertyBits.PropertyString)) Chart(ref cursor, traits.Texts, static (ref WireCursor r) => Text(ref r));
            if (HasProp(DescPropertyBits.PropertyDid)) Chart(ref cursor, traits.BlobIdents, static (ref WireCursor r) => r.Word());
            if (HasProp(DescPropertyBits.PropertyIid)) Chart(ref cursor, traits.InstIdents, static (ref WireCursor r) => r.Word());
            if (HasProp(DescPropertyBits.Position)) Chart(ref cursor, loci, static (ref WireCursor r) => Position(ref r));

            if (!cursor.Has(8))
            {
                // No vector block at all: a bare property dump
                return new Parsed(weenieKind, propBitset, DescVectorBits.None, false, traits, loci, attrs, aptitudes, arcana,
                    [], ToonKnobBlobBit.None, 0u, 0u, [], [], [], 0u, ReadOnlyMemory<byte>.Empty, [], [], TrailerTruncated: false);
            }
            DescVectorBits vectorBitset = (DescVectorBits)cursor.U32();
            bool hasHealth = cursor.U32() is not 0;
            bool HasVector(DescVectorBits bit) => (vectorBitset & bit) != 0;

            if (HasVector(DescVectorBits.Attribute)) Attributes(ref cursor, attrs);
            if (HasVector(DescVectorBits.Skill)) Chart(ref cursor, aptitudes, static (ref WireCursor r) => Skill(ref r));
            if (HasVector(DescVectorBits.Spell)) Chart(ref cursor, arcana, static (ref WireCursor r) => Single(ref r));
            if (HasVector(DescVectorBits.Enchantment)) Enchantments(ref cursor, enchantments);

            Trailer rear = ScanTrailer(ref cursor);
            return new Parsed(
                weenieKind, propBitset, vectorBitset, hasHealth,
                traits, loci, attrs, aptitudes, arcana, enchantments,
                rear.KnobFlagSet, rear.Options1, rear.Options2,
                rear.Shortcuts, rear.HotbarArcana, rear.WantedComps, rear.SpellbookFilters,
                rear.GameplayKnobs, rear.Inventory, rear.Equipped, rear.Truncated);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // The PlayerModule block plus manifests; whatever was read before a cut is kept
    private sealed class Trailer
    {
        public ToonKnobBlobBit KnobFlagSet;
        public uint Options1, Options2;
        public uint SpellbookFilters = DefaultGrimoireFilters;
        public readonly List<HotbarSlot> Shortcuts = [];
        public readonly List<IReadOnlyList<uint>> HotbarArcana = [];
        public readonly List<(uint, uint)> WantedComps = [];
        public ReadOnlyMemory<byte> GameplayKnobs = ReadOnlyMemory<byte>.Empty;
        public readonly List<InventoryRow> Inventory = [];
        public readonly List<WornRow> Equipped = [];
        public bool Truncated;
    }

    private static Trailer ScanTrailer(ref WireCursor cursor)
    {
        Trailer trailer = new Trailer();
        try
        {
            if (!cursor.Has(8))
                return trailer;
            trailer.KnobFlagSet = (ToonKnobBlobBit)cursor.Word();
            trailer.Options1 = cursor.Word();
            bool Has(ToonKnobBlobBit bit) => (trailer.KnobFlagSet & bit) != 0;

            if (Has(ToonKnobBlobBit.Shortcut))
            {
                uint tally = cursor.Word();
                if (tally > UpperRosterTally)
                    throw new FormatException("unreasonable shortcut count");
                for (uint idx = 0; idx < tally; ++idx)
                {
                    int ordinal = unchecked((int)cursor.Word());
                    uint objectIdent = cursor.Word();
                    trailer.Shortcuts.Add(new HotbarSlot(ordinal, objectIdent, cursor.Word()));
                }
            }

            // Modern payloads carry eight hotbar tabs; older ones a single list
            if (Has(ToonKnobBlobBit.SpellLists8))
            {
                for (int tab = 0; tab < HotbarTabs; ++tab)
                    trailer.HotbarArcana.Add(Hotbar(ref cursor));
            }
            else if (cursor.Has(4))
            {
                trailer.HotbarArcana.Add(Hotbar(ref cursor));
            }

            if (Has(ToonKnobBlobBit.DesiredComps))
            {
                if (!cursor.Has(4))
                    throw new FormatException("truncated desired_comps header");
                ushort tally = cursor.Half();
                cursor.Half(); // bucket count
                if (tally > UpperRosterTally)
                    throw new FormatException("unreasonable desired_comps count");
                for (int idx = 0; idx < tally; ++idx)
                {
                    uint ident = cursor.Word();
                    trailer.WantedComps.Add((ident, cursor.Word()));
                }
            }

            if (cursor.Has(4))
                trailer.SpellbookFilters = cursor.Word();
            if (Has(ToonKnobBlobBit.CharacterOptions2))
                trailer.Options2 = cursor.Word();

            if (Has(ToonKnobBlobBit.GameplayOptions))
            {
                // The gameplay-options blob has no length prefix: find the
                // manifests by scanning for a word-aligned start that
                // unpacks exactly to the end of the payload.
                int blobBegin = cursor.At;
                if (SeekManifests(cursor, blobBegin, out int manifestBegin, out int finish, trailer.Inventory, trailer.Equipped))
                {
                    trailer.GameplayKnobs = cursor.Rest.Slice(0, manifestBegin - blobBegin).ToArray();
                    cursor.Skip(finish - cursor.At);
                }
            }
            else
            {
                Manifests(ref cursor, trailer.Inventory, trailer.Equipped);
            }
        }
        catch (FormatException)
        {
            trailer.Truncated = true;
        }
        return trailer;
    }

    private static List<uint> Hotbar(ref WireCursor cursor)
    {
        uint tally = cursor.Word();
        if (tally > UpperRosterTally)
            throw new FormatException("unreasonable hotbar count");
        List<uint> roster = new List<uint>((int)tally);
        for (uint idx = 0; idx < tally; ++idx)
            roster.Add(cursor.Word());
        return roster;
    }
}
