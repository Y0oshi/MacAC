namespace MacAC.Extensibility.Automation;

public readonly record struct PaletteFacts(
    uint PaletteId,
    byte Offset,
    byte Length,
    byte Red,
    byte Green,
    byte Blue);

/// <summary>One item the local player owns, in a pack or wielded.</summary>
public readonly record struct PackEntry(
    uint ObjectId,
    uint WeenieClassId,
    string Name,
    uint ItemType,
    uint ContainerObjectId,
    uint WielderObjectId,
    uint ValidLocations,
    uint EquippedLocation,
    uint Useability,
    uint TargetType,
    uint PublicFlags,
    int StackSize,
    int Structure,
    int MaximumStructure,
    uint SpellId,
    int PetClass,
    int SummoningMastery,
    uint ProcSpellId,
    bool ProcSpellSelfTargeted,
    double ProcSpellRate,
    int WeaponSkill,
    int DamageType,
    int Damage,
    double DamageVariance,
    int UseRequiresSkill,
    int UseRequiresSkillLevel,
    int UseRequiresSkillSpecialized)
{
    public bool IsEquipped => EquippedLocation is not 0u;

    public bool IsPetDev => PetClass is not 0;

    public bool HasCastingOnHit => ProcSpellId is not 0u && ProcSpellRate > 0d;

    public int CombatUse { get; init; }

    public int ItemSpellcraft { get; init; }

    public int WieldRequirements { get; init; }

    public int WieldAptitudeKind { get; init; }

    public int WieldDifficulty { get; init; }

    public int AttackType { get; init; }

    public int WeaponType { get; init; }

    public int BoosterVital { get; init; }

    public int BoostValue { get; init; }

    public double MendKitModifier { get; init; }

    public IReadOnlyList<uint> AppraisedArcanumIdents { get; init; } = Array.Empty<uint>();

    public int GearDamage { get; init; }

    public int GearHarmResistance { get; init; }

    public int GearCriticalChance { get; init; }

    public int GearCriticalResistance { get; init; }

    public int GearCriticalHarm { get; init; }

    public int GearCriticalHarmResistance { get; init; }

    public int CeilingPileDims { get; init; } = 1;

    public int VesselSocket { get; init; } = -1;

    public int ItemsCapacity { get; init; }

    public int ContainersCapacity { get; init; }

    public int Burden { get; init; }

    public int Value { get; init; }

    public int GearLatestMana { get; init; }

    public int GearCeilingMana { get; init; }

    public float Workmanship { get; init; }

    public uint MaterialType { get; init; }

    public EntityClass ObjectClass { get; init; }

    public IReadOnlyList<PaletteFacts> Swatches { get; init; } = Array.Empty<PaletteFacts>();

    public uint IconId { get; init; }
}

/// <summary>Every appraised property of one object, keyed by property id.</summary>
public readonly record struct ItemPropertySheet(
    IReadOnlyDictionary<uint, int> Ints,
    IReadOnlyDictionary<uint, long> Int64s,
    IReadOnlyDictionary<uint, bool> Bools,
    IReadOnlyDictionary<uint, double> Floats,
    IReadOnlyDictionary<uint, string> Strings,
    IReadOnlyDictionary<uint, uint> DataIds,
    IReadOnlyDictionary<uint, uint> InstanceIds);

/// <summary>One server <c>UseDone</c> for an extension-issued item action.</summary>
public readonly record struct ItemUseReceipt(
    long Revision,
    uint SourceObjectId,
    uint TargetObjectId,
    uint WeenieError)
{
    public bool IsSuccess => Revision is not 0 && WeenieError is 0u;
}

public enum ItemOutcome
{
    Unavailable = 0,
    InvalidItem,
    InvalidTarget,
    Busy,
    Started,
    Refused,
}

public readonly record struct ItemVerdict(
    ItemOutcome Status,
    string? Notice = null)
{
    public bool Accepted => Status == ItemOutcome.Started;
}

public enum InventoryVerb
{
    Unknown = 0,
    Pickup,
    PutInContainer,
    SplitToContainer,
    Merge,
    Move,
    DropToWorld,
    SplitToWorld,
    Wield,
    Give,
}

public readonly record struct InventoryReceipt(
    long Revision,
    InventoryVerb Kind,
    uint SourceObjectId,
    uint WeenieError)
{
    public bool IsSuccess => Revision is not 0 && WeenieError is 0u;
}

public interface IItemControls
{
    bool IsAvailable => false;

    bool IsBusy => false;

    int ActiveOwnedPetCount => 0;

    uint EngagedMerchantObjectIdent => 0u;

    ItemUseReceipt LastCompletion => default;

    InventoryReceipt LastInventoryCompletion => default;

    IReadOnlyList<PackEntry> GrabPossessedGearList() => Array.Empty<PackEntry>();

    bool TryCaptureProperties(uint objectIdent, out ItemPropertySheet props)
    {
        props = default;
        return false;
    }

    ItemVerdict Use(uint objectIdent) => new(ItemOutcome.Unavailable);

    ItemVerdict Apply(uint objectIdent, uint markObjectIdent) => new(ItemOutcome.Unavailable);

    ItemVerdict ShiftToVessel(
        uint objectIdent,
        uint vesselObjectIdent,
        uint quantity = 0u,
        int stance = 0) => new(ItemOutcome.Unavailable);

    ItemVerdict Merge(uint srcObjectIdent, uint markObjectIdent, uint quantity = 0u) =>
        new(ItemOutcome.Unavailable);

    /// <summary>Drop the whole stack, or exactly <paramref name="quantity"/>, on the ground.</summary>
    ItemVerdict Discard(uint objectIdent, uint quantity = 0u) => new(ItemOutcome.Unavailable);

    /// <summary>Give the whole stack, or exactly <paramref name="quantity"/>, to a world target.</summary>
    ItemVerdict Hand(uint objectIdent, uint markObjectIdent, uint quantity = 0u) =>
        new(ItemOutcome.Unavailable);

    ItemVerdict Salvage(uint toolObjectIdent, IReadOnlyList<uint> gearObjectIdents) =>
        new(ItemOutcome.Unavailable);

    ItemVerdict Vend(uint objectIdent, uint quantity = 0u) => new(ItemOutcome.Unavailable);
}
