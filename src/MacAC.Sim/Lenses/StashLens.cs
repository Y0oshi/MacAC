namespace MacAC.Sim;

public readonly record struct SimStashItemCapture(
    uint ObjectId,
    ushort Incarnation,
    string Name,
    uint ContainerId,
    int ContainerSlot,
    uint WielderId,
    uint EquipLocation,
    int StackSize,
    int Value);

public interface ISimStashVisitor
{
    void Tour(in SimStashItemCapture gear);
}

/// <summary>The object table as an inventory: what is carried, and where.</summary>
public interface ISimStashLens
{
    int ObjectTally { get; }

    int VesselCount { get; }

    bool TryGet(uint objectIdent, out SimStashItemCapture gear);

    void Visit(ISimStashVisitor visitor);
}

public readonly record struct SimPendingStashRequestCapture(ulong Token, int Kind, uint ItemId, bool Dispatched);

public readonly record struct SimStashStateCapture(
    uint RequestedExternalContainerId,
    uint CurrentExternalContainerId,
    int BusyCount,
    bool CanBeginRequest,
    SimPendingStashRequestCapture? PendingRequest,
    int ShortcutCount,
    long ShortcutRevision,
    int ItemManaCount,
    long ItemManaRevision);

public readonly record struct SimHotkeyCapture(int Index, uint ObjectId, uint SpellId);

public interface ISimStashStateLens
{
    SimStashStateCapture Snapshot { get; }

    bool TryFetchShortcut(int ordinal, out SimHotkeyCapture shortcut);

    bool TryFetchGearMana(uint objectIdent, out float ratio);
}
