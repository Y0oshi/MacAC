using MacAC.Mechanics.Gear;

namespace MacAC.Wire.Messages;

public readonly record struct WeenieDescBody(
    string? Name = null,
    uint? ItemType = null,
    uint? ObjectDescriptionFlags = null,
    uint IconId = 0,
    uint WeenieClassId = 0,
    int? Value = null,
    int? StackSize = null,
    int? StackSizeMax = null,
    int? Burden = null,
    int? ItemsCapacity = null,
    int? ContainersCapacity = null,
    uint? HookItemTypes = null,
    uint? HookType = null,
    uint? ContainerId = null,
    uint? WielderId = null,
    uint? ValidLocations = null,
    uint? CurrentWieldedLocation = null,
    uint? Priority = null,
    int? Structure = null,
    int? MaxStructure = null,
    float? Workmanship = null,
    uint? Useability = null,
    float? UseRadius = null,
    uint? TargetType = null,
    uint IconOverlayId = 0,
    uint IconUnderlayId = 0,
    uint UiEffects = 0,
    byte? RadarBlipColor = null,
    byte? RadarBehavior = null,
    byte? CombatUse = null,
    string? PluralName = null,
    uint? PetOwnerId = null,
    ushort? AmmoType = null,
    uint? SpellId = null,
    uint? CooldownId = null,
    double? CooldownDuration = null,
    uint? MaterialType = null,
    uint? HouseOwnerId = null,
    uint? MonarchId = null,
    HouseAccessRecord? Restrictions = null);

public static class WeenieDescReader
{
    private const uint SecondPreambleBit = 0x04000000u;
    private const int UpperLabelLen = 1024;

    // Mutable scratch the two parse phases fill; converted to the record at the end
    private struct Draft
    {
        public string? Name, PluralName;
        public uint? ItemType, ObjectBlurbFlagSet, HookItemTypes, HookType, VesselId, WielderTag, ValidLocations, CurrentWieldedLocation, Priority, Useability, TargetType, PetHolderTag, SpellId, CooldownId, MaterialType, HouseHolderTag, MonarchTag;
        public uint IconId, WeenieClassTag, GlyphTopLayerTag, GlyphUnderlayTag, UiEffects;
        public int? Value, StackSize, PileSizeMax, Burden, ItemsCapacity, ContainersCapacity, Structure, MaxStructure;
        public float? Workmanship, UseRadius;
        public double? CooldownDuration;
        public byte? RadarBlipColor, RadarBehavior, CombatUse;
        public ushort? AmmoType;
        public HouseAccessRecord? Restrictions;

        public readonly WeenieDescBody Seal()
        {
            return new(
            Name: Name,
            ItemType: ItemType,
            ObjectDescriptionFlags: ObjectBlurbFlagSet,
            IconId: IconId,
            WeenieClassId: WeenieClassTag,
            Value: Value,
            StackSize: StackSize,
            StackSizeMax: PileSizeMax,
            Burden: Burden,
            ItemsCapacity: ItemsCapacity,
            ContainersCapacity: ContainersCapacity,
            HookItemTypes: HookItemTypes,
            HookType: HookType,
            ContainerId: VesselId,
            WielderId: WielderTag,
            ValidLocations: ValidLocations,
            CurrentWieldedLocation: CurrentWieldedLocation,
            Priority: Priority,
            Structure: Structure,
            MaxStructure: MaxStructure,
            Workmanship: Workmanship,
            Useability: Useability,
            UseRadius: UseRadius,
            TargetType: TargetType,
            IconOverlayId: GlyphTopLayerTag,
            IconUnderlayId: GlyphUnderlayTag,
            UiEffects: UiEffects,
            RadarBlipColor: RadarBlipColor,
            RadarBehavior: RadarBehavior,
            CombatUse: CombatUse,
            PluralName: PluralName,
            PetOwnerId: PetHolderTag,
            AmmoType: AmmoType,
            SpellId: SpellId,
            CooldownId: CooldownId,
            CooldownDuration: CooldownDuration,
            MaterialType: MaterialType,
            HouseOwnerId: HouseHolderTag,
            MonarchId: MonarchTag,
            Restrictions: Restrictions);
        }
    }

    public static WeenieDescBody Parse(ReadOnlySpan<byte> corpus, ref int spot)
    {
        var cursor = new WireCursor(corpus);
        cursor.Skip(spot);
        var descriptor = Decode(ref cursor);
        spot = cursor.At;
        return descriptor;
    }

    internal static WeenieDescBody Decode(ref WireCursor cursor)
    {
        var draft = new Draft();
        uint flagSet = 0;
        if (cursor.Has(4))
        {
            flagSet = cursor.U32();
            try
            {
                Stem(ref cursor, ref draft);
            }
            catch
            {
                // a cut inside the name: the header word alone still steers the tail
            }
        }

        try
        {
            Rear(ref cursor, ref draft, flagSet);
        }
        catch
        {
            // truncated tail: keep whatever was read
        }
        return draft.Seal();
    }

    private static void Stem(ref WireCursor cursor, ref Draft draft)
    {
        draft.Name = cursor.String16L(upperLen: UpperLabelLen);
        draft.WeenieClassTag = cursor.PackedDword();
        draft.IconId = cursor.PackedDword(ObjectCreation.GlyphKindStem);
        if (cursor.Has(4))
            draft.ItemType = cursor.U32();
        if (cursor.Has(4))
            draft.ObjectBlurbFlagSet = cursor.U32();
        cursor.Align4();
    }

    private static void Rear(ref WireCursor cursor, ref Draft draft, uint flagSet)
    {
        uint flags2 = 0;
        if (draft.ObjectBlurbFlagSet is { } odf && (odf & SecondPreambleBit) is not 0)
            flags2 = Need(ref cursor, 4, "trunc weenieFlags2").U32();
        bool On(uint bit) => (flagSet & bit) is not 0;
        bool On2(uint bit) => (flags2 & bit) is not 0;

        if (On(0x00000001u)) draft.PluralName = cursor.String16L(upperLen: UpperLabelLen);
        if (On(0x00000002u)) draft.ItemsCapacity = unchecked((sbyte)Need(ref cursor, 1, "trunc ItemCap").U8());
        if (On(0x00000004u)) draft.ContainersCapacity = unchecked((sbyte)Need(ref cursor, 1, "trunc ContCap").U8());
        if (On(0x00000100u)) draft.AmmoType = Need(ref cursor, 2, "trunc AmmoKind").U16();
        if (On(0x00000008u)) draft.Value = (int)Need(ref cursor, 4, "trunc Value").U32();
        if (On(0x00000010u)) draft.Useability = Need(ref cursor, 4, "trunc Useability").U32();
        if (On(0x00000020u)) draft.UseRadius = Need(ref cursor, 4, "trunc UseRadius").F32();
        if (On(0x00080000u)) draft.TargetType = Need(ref cursor, 4, "trunc TargetType").U32();
        if (On(0x00000080u)) draft.UiEffects = Need(ref cursor, 4, "trunc UiEffects").U32();
        if (On(0x00000200u)) draft.CombatUse = Need(ref cursor, 1, "trunc FightingUse").U8();
        if (On(0x00000400u)) draft.Structure = Need(ref cursor, 2, "trunc Structure").U16();
        if (On(0x00000800u)) draft.MaxStructure = Need(ref cursor, 2, "trunc MaxStructure").U16();
        if (On(0x00001000u)) draft.StackSize = Need(ref cursor, 2, "trunc StackSize").U16();
        if (On(0x00002000u)) draft.PileSizeMax = Need(ref cursor, 2, "trunc MaxStackSize").U16();
        if (On(0x00004000u)) draft.VesselId = Need(ref cursor, 4, "trunc Container").U32();
        if (On(0x00008000u)) draft.WielderTag = Need(ref cursor, 4, "trunc Wielder").U32();
        if (On(0x00010000u)) draft.ValidLocations = Need(ref cursor, 4, "trunc ValidLocations").U32();
        if (On(0x00020000u)) draft.CurrentWieldedLocation = Need(ref cursor, 4, "trunc CurrentlyWieldedLocation").U32();
        if (On(0x00040000u)) draft.Priority = Need(ref cursor, 4, "trunc Priority").U32();
        if (On(0x00100000u)) draft.RadarBlipColor = Need(ref cursor, 1, "trunc RadarBlipColor").U8();
        if (On(0x00800000u)) draft.RadarBehavior = Need(ref cursor, 1, "trunc MechRadarBehavior").U8();
        if (On(0x08000000u)) Need(ref cursor, 2, "trunc PScript").Skip(2);
        if (On(0x01000000u)) draft.Workmanship = Need(ref cursor, 4, "trunc Workmanship").F32();
        if (On(0x00200000u)) draft.Burden = Need(ref cursor, 2, "trunc Burden").U16();
        if (On(0x00400000u)) draft.SpellId = Need(ref cursor, 2, "trunc Spell").U16();
        if (On(0x02000000u)) draft.HouseHolderTag = Need(ref cursor, 4, "trunc HouseOwner").U32();
        if (On(0x04000000u)) draft.Restrictions = RestrictionDb(ref cursor);
        if (On(0x20000000u)) draft.HookItemTypes = Need(ref cursor, 4, "trunc HookItemTypes").U32();
        if (On(0x00000040u)) draft.MonarchTag = Need(ref cursor, 4, "trunc Monarch").U32();
        if (On(0x10000000u)) draft.HookType = Need(ref cursor, 2, "trunc HookType").U16();
        if (On(0x40000000u)) draft.GlyphTopLayerTag = cursor.PackedDword(ObjectCreation.GlyphKindStem);
        if (On2(0x00000001u)) draft.GlyphUnderlayTag = cursor.PackedDword(ObjectCreation.GlyphKindStem);
        if (On(0x80000000u)) draft.MaterialType = Need(ref cursor, 4, "trunc MaterialType").U32();
        if (On2(0x00000002u)) draft.CooldownId = Need(ref cursor, 4, "trunc CooldownId").U32();
        if (On2(0x00000004u)) draft.CooldownDuration = Need(ref cursor, 8, "trunc CooldownDuration").F64();
        if (On2(0x00000008u)) draft.PetHolderTag = Need(ref cursor, 4, "trunc PetOwner").U32();
    }

    // RestrictionDB: version word, open flag, monarch, then a packed hash table of guest → permission
    private static HouseAccessRecord RestrictionDb(ref WireCursor cursor)
    {
        Need(ref cursor, 12, "trunc RestrictionDB header").Skip(4);
        uint open = cursor.U32();
        uint monarch = cursor.U32();
        uint dense = Need(ref cursor, 4, "trunc RestrictionDB PHashTable header").U32();
        uint tally = dense & 0xFFFFFFu;
        if (cursor.Left < (long)tally * 8)
            throw new FormatException("trunc RestrictionDB entries");
        var guests = new Dictionary<uint, uint>((int)tally);
        for (uint idx = 0; idx < tally; ++idx)
        {
            uint guest = cursor.U32();
            guests[guest] = cursor.U32();
        }
        return new HouseAccessRecord(OpenToPublic: open is not 0, AllegianceMonarchIdent: monarch, Guests: guests);
    }

    private static ref WireCursor Need(ref WireCursor cursor, int octets, string msg)
    {
        if (!cursor.Has(octets))
            throw new FormatException(msg);
        return ref cursor;
    }
}
