namespace MacAC.Mechanics.Gear;

/// <summary>One slot of a server-announced container listing.</summary>
public readonly record struct ContainerSlotRow(uint Guid, uint ContainerType);

public readonly record struct WornItemRow(
    uint Guid,
    WieldBitmask EquipLocation,
    uint Priority);

/// <summary>The server's answer to a move request it would not honour.</summary>
public readonly record struct RelocationRefusal(
    uint ItemId,
    uint WeenieError,
    bool RolledBack);

/// <summary>Where an object sits: its pack, slot, wielder and worn location.</summary>
public readonly record struct ObjectPlacement(
    uint ContainerId,
    int ContainerSlot,
    uint WielderId,
    WieldBitmask EquipLocation)
{
    public static ObjectPlacement From(ClientThing gear)
    {
        return new(
        gear.VesselTag,
        gear.VesselSlot,
        gear.WielderIdent,
        gear.CurrentlyEquippedLocale);
    }

    /// <summary>A placement with no pack, no slot and no wielder.</summary>
    public static ObjectPlacement Nowhere => default;
}

public enum ObjectRelocationOrigin
{
    LocalProjection,
    ServerRollbackProjection,
    AuthoritativeResponse,
}

public readonly record struct ObjectRelocation(
    uint ItemId,
    ClientThing? Item,
    ObjectPlacement Previous,
    ObjectPlacement Current,
    ObjectRelocationOrigin Origin);

public enum ObjectRemovalCause
{
    Ordinary,
    LogicalDelete,
    GenerationReplacement,
}

public readonly record struct ObjectRemoval(
    ClientThing Object,
    ObjectRemovalCause Reason,
    ushort Generation);
