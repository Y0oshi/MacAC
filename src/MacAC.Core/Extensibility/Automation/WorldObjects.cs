namespace MacAC.Extensibility.Automation;

/// <summary>VTank's object-class vocabulary.</summary>
public enum EntityClass
{
    Unknown = 0,
    MeleeWeapon = 1,
    Armor = 2,
    Clothing = 3,
    Jewelry = 4,
    Monster = 5,
    Food = 6,
    Money = 7,
    Misc = 8,
    MissileWeapon = 9,
    Container = 10,
    Gem = 11,
    SpellComponent = 12,
    Key = 13,
    Portal = 14,
    TradeNote = 15,
    ManaStone = 16,
    Plant = 17,
    BaseCooking = 18,
    BaseAlchemy = 19,
    BaseFletching = 20,
    CraftedCooking = 21,
    CraftedAlchemy = 22,
    CraftedFletching = 23,
    Player = 24,
    Vendor = 25,
    Door = 26,
    Corpse = 27,
    Lifestone = 28,
    HealingKit = 29,
    Lockpick = 30,
    WandStaffOrb = 31,
    Bundle = 32,
    Book = 33,
    Journal = 34,
    Sign = 35,
    Housing = 36,
    Npc = 37,
    Foci = 38,
    Salvage = 39,
    Ust = 40,
    Services = 41,
    Scroll = 42,
    CombatPet = 43,
}

public readonly record struct WorldObjectEntry(
    uint ObjectId,
    uint WeenieClassId,
    string Name,
    EntityClass ObjectClass,
    uint ItemType,
    uint ContainerObjectId,
    uint WielderObjectId)
{
    public bool IsOwned { get; init; }

    public bool IsScenery { get; init; }

    public bool HasLocus { get; init; }

    public NavigationFix Position { get; init; }

    public bool HasAppraisalBlob { get; init; }

    public int PreviousIdentMoment { get; init; }

    public bool IsDoorOpen { get; init; }

    public int StackSize { get; init; } = 1;

    public int ItemsCapacity { get; init; }

    public int ContainersCapacity { get; init; }

    public IReadOnlyList<uint> ArcanumIdents { get; init; } = Array.Empty<uint>();

    public IReadOnlyList<uint> EngagedArcanumIdents { get; init; } = Array.Empty<uint>();

    public uint IconId { get; init; }
}

public interface IWorldObjectControls
{
    bool IsAvailable => false;

    uint OpenContainerObjectId => 0u;

    IReadOnlyList<WorldObjectEntry> CaptureObjects() => Array.Empty<WorldObjectEntry>();

    bool TryGet(uint objectIdent, out WorldObjectEntry val)
    {
        val = default;
        return false;
    }

    bool TryCaptureProperties(uint objectIdent, out ItemPropertySheet props)
    {
        props = default;
        return false;
    }

    ItemVerdict Identify(uint objectIdent) => new(ItemOutcome.Unavailable);
}
