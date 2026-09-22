namespace MacAC.Mechanics.Gear;

[Flags]
public enum GearKind : uint
{
    None = 0,
    MeleeWeapon = 0x00000001,
    Armor = 0x00000002,
    Clothing = 0x00000004,
    Jewelry = 0x00000008,
    Creature = 0x00000010,
    Food = 0x00000020,
    Money = 0x00000040,
    Misc = 0x00000080,
    MissileWeapon = 0x00000100,
    Container = 0x00000200,
    Useless = 0x00000400,
    Gem = 0x00000800,
    SpellComponents = 0x00001000,
    Writable = 0x00002000,
    Key = 0x00004000,
    Caster = 0x00008000,
    Portal = 0x00010000,
    Lockable = 0x00020000,
    PromissoryNote = 0x00040000,
    ManaStone = 0x00080000,
    Service = 0x00100000,
    MagicWieldable = 0x00200000,
    CraftCookingBase = 0x00400000,
    CraftAlchemyBase = 0x00800000,
    CraftFletchingBase = 0x01000000,
    CraftAlchemyIntermediate = 0x04000000,
    CraftFletchingIntermediate = 0x08000000,
    LifeStone = 0x10000000,
    TinkeringTool = 0x20000000,
    TinkeringMaterial = 0x40000000,
    Gameboard = 0x80000000u,

    // The retail composite masks.
    Vestements = 0x00000006,
    Weapon = 0x00000101,
    WeaponOrCaster = 0x00008101,
    LockableMagicTarget = 0x00000280,
    RedirectableItemEnchantmentTarget = 0x00008107,
    PortalMagicTarget = 0x10010000,
    ItemEnchantableTarget = 0x00088B8F,
    Item = 0x002DFBEF,
    VendorShopkeep = 0x480467A7,
    VendorGrocer = 0x00446220,
}

[Flags]
public enum WieldBitmask : uint
{
    None = 0x00000000,
    HeadWear = 0x00000001,
    ChestWear = 0x00000002,
    AbdomenWear = 0x00000004,
    UpperArmWear = 0x00000008,
    LowerArmWear = 0x00000010,
    HandWear = 0x00000020,
    UpperLegWear = 0x00000040,
    LowerLegWear = 0x00000080,
    FootWear = 0x00000100,
    ChestArmor = 0x00000200,
    AbdomenArmor = 0x00000400,
    UpperArmArmor = 0x00000800,
    LowerArmArmor = 0x00001000,
    UpperLegArmor = 0x00002000,
    LowerLegArmor = 0x00004000,
    NeckWear = 0x00008000,
    WristWearLeft = 0x00010000,
    WristWearRight = 0x00020000,
    FingerWearLeft = 0x00040000,
    FingerWearRight = 0x00080000,
    MeleeWeapon = 0x00100000,
    Shield = 0x00200000,
    MissileWeapon = 0x00400000,
    MissileAmmo = 0x00800000,
    Held = 0x01000000,
    TwoHanded = 0x02000000,
    TrinketOne = 0x04000000,
    Cloak = 0x08000000,
    SigilOne = 0x10000000,
    SigilTwo = 0x20000000,
    SigilThree = 0x40000000,

    Clothing = 0x080001FF,
    Armor = 0x00007E00,
    Jewelry = 0x7C0F8000,
    WristWear = 0x00030000,
    FingerWear = 0x000C0000,
    Sigil = 0x70000000,
    ReadySlot = 0x03F00000,
    Weapon = 0x02500000,
    WeaponReadySlot = 0x03500000,
    All = 0x7FFFFFFF,
    CanGoInReadySlot = 0x7FFFFFFF,
}

[Flags]
public enum AmmoKind : uint
{
    None = 0x000,
    Arrow = 0x001,
    Bolt = 0x002,
    Atlatl = 0x004,
    ArrowCrystal = 0x008,
    BoltCrystal = 0x010,
    AtlatlCrystal = 0x020,
    ArrowChorizite = 0x040,
    BoltChorizite = 0x080,
    AtlatlChorizite = 0x100,
}

public enum FightingUse : uint
{
    None = 0,
    Melee = 1,
    Missile = 2,
    Ammo = 3,
    Shield = 4,
    TwoHanded = 5,
}

/// <summary>Low word: how the source may be used; high word: what it may be used on.</summary>
[Flags]
public enum GearUseable : uint
{
    Undef = 0x0,
    No = 0x1,
    Self = 0x2,
    Wielded = 0x4,
    Contained = 0x8,
    Viewed = 0x10,
    Remote = 0x20,
    NeverWalk = 0x40,
    ObjSelf = 0x80,

    ContainedViewed = 0x18,
    ViewedRemote = 0x30,
    ContainedViewedRemote = 0x38,
    RemoteNeverWalk = 0x60,
    ViewedRemoteNeverWalk = 0x70,
    ContainedViewedRemoteNeverWalk = 0x78,

    SourceWieldedTargetWielded = 0x00040004,
    SourceWieldedTargetContained = 0x00080004,
    SourceWieldedTargetViewed = 0x00100004,
    SourceWieldedTargetRemote = 0x00200004,
    SourceWieldedTargetRemoteNeverWalk = 0x00600004,

    SourceContainedTargetWielded = 0x00040008,
    SourceContainedTargetContained = 0x00080008,
    SourceContainedTargetSelfOrContained = 0x000A0008,
    SourceContainedTargetViewed = 0x00100008,
    SourceContainedTargetRemote = 0x00200008,
    SourceContainedTargetRemoteOrSelf = 0x00220008,
    SourceContainedTargetRemoteNeverWalk = 0x00600008,
    SourceContainedTargetObjSelfOrContained = 0x00880008,

    SourceViewedTargetWielded = 0x00040010,
    SourceViewedTargetContained = 0x00080010,
    SourceViewedTargetViewed = 0x00100010,
    SourceViewedTargetRemote = 0x00200010,

    SourceRemoteTargetWielded = 0x00040020,
    SourceRemoteTargetContained = 0x00080020,
    SourceRemoteTargetViewed = 0x00100020,
    SourceRemoteTargetRemote = 0x00200020,
    SourceRemoteTargetRemoteNeverWalk = 0x00600020,

    SourceMask = 0x0000FFFF,
    TargetMask = 0xFFFF0000,
}

/// <summary>The raw useability word, taken apart.</summary>
public static class GearUseability
{
    public const uint Undef = 0x0u;
    public const uint No = 0x1u;
    public const uint Self = 0x2u;
    public const uint Wielded = 0x4u;
    public const uint Contained = 0x8u;
    public const uint Viewed = 0x10u;
    public const uint Remote = 0x20u;
    public const uint NeverWalk = 0x40u;
    public const uint ObjSelf = 0x80u;

    public const uint SourceMask = 0x0000FFFFu;
    public const uint TargetMask = 0xFFFF0000u;

    private const int MarkShift = 16;

    public static bool IsTargeted(uint useability) => (useability & TargetMask) is not 0;

    public static bool AllowsSelfMark(uint useability) => ((useability >> MarkShift) & Self) is not 0;

    public static bool AllowsObjectSelfMark(uint useability) => ((useability >> MarkShift) & ObjSelf) is not 0;

    public static uint SrcFlagSet(uint useability) => useability & SourceMask;

    public static uint MarkFlagSet(uint useability) => (useability & TargetMask) >> MarkShift;

    public static bool IsUseable(uint useability) => (useability & No) is 0;

    public static bool IsStraightUseable(uint useability) => !IsTargeted(useability) && IsUseable(useability);

    public static uint LeastLimitedSrcUse(uint useability) => Loosest(SrcFlagSet(useability), Undef);

    /// <summary>As for the source, except an object-self-only target keeps that bit.</summary>
    public static uint LeastLimitedMarkUse(uint useability)
    {
        uint t = MarkFlagSet(useability);
        return Loosest(t, t & ObjSelf);
    }

    private static uint Loosest(uint bitset, uint backup)
    {
        if ((bitset & Remote) is not 0) return Remote;
        if ((bitset & Viewed) is not 0) return Viewed;
        if ((bitset & Contained) is not 0) return Contained;
        if ((bitset & Wielded) is not 0) return Wielded;
        return (bitset & Self) is not 0 ? Self : backup;
    }
}

/// <summary>Folds a PK status byte into the public weenie bitfield the way the client did.</summary>
public static class PkStatusBits
{
    public const int Pk = 0x04;
    public const int Release = 0x20;
    public const int PkLite = 0x40;

    public static uint Apply(uint bitfield, int pkCondition)
    {
        return pkCondition switch
        {
            Pk => (bitfield & 0xfddfffffu) | 0x20u,
            PkLite => (bitfield & 0xffdfffdfu) | 0x2000000u,
            Release => (bitfield & 0xfdffffdfu) | 0x200000u,
            _ => bitfield & 0xfddfffdfu,
        };
    }
}
