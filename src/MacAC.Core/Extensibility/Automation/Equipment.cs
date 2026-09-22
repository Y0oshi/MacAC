namespace MacAC.Extensibility.Automation;

/// <summary>One owned item that VTank equipment policy may consider.</summary>
public readonly record struct EquipmentEntry(
    uint ObjectId,
    string Name,
    uint ItemType,
    uint ValidLocations,
    uint EquippedLocation,
    uint ContainerObjectId,
    uint WielderObjectId,
    byte CombatUse,
    int DamageType,
    int WeaponSkill,
    int Damage,
    double DamageVariance)
{
    public bool IsEquipped => EquippedLocation is not 0u;

    public uint AmmoType { get; init; }

    public int StackSize { get; init; } = 1;

    public int WeaponType { get; init; }
}

public enum EquipOutcome
{
    Unavailable = 0,
    InvalidItem,
    Busy,
    AlreadyEquipped,
    Started,
    Refused,
}

public readonly record struct EquipVerdict(
    EquipOutcome Status,
    string? Notice = null)
{
    public bool Accepted => Status is EquipOutcome.AlreadyEquipped or EquipOutcome.Started;
}

public interface IEquipmentControls
{
    bool IsAvailable => false;

    bool IsBusy => false;

    IReadOnlyList<EquipmentEntry> GrabPossessedEquipment() => Array.Empty<EquipmentEntry>();

    EquipVerdict Wield(uint objectIdent, uint askedLocale = 0u) =>
        new(EquipOutcome.Unavailable);
}
