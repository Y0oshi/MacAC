using System.Numerics;

namespace MacAC.Extensibility.World;

/// <summary>One world object as last seen by the host.</summary>
public readonly record struct EntityFrame(
    uint Id,
    uint SourceId,
    Vector3 Position,
    Quaternion Rotation);

/// <summary>One quest contract on the player's tracker.</summary>
public readonly record struct QuestContractFrame(
    uint ContractId,
    uint Stage,
    uint Progress,
    bool IsDisplayed,
    string Name = "",
    string Description = "",
    string Status = "");

/// <summary>Read-only world facts the host keeps current.</summary>
public interface IWorldLedger
{
    IReadOnlyList<EntityFrame> Entities { get; }

    IReadOnlyList<QuestContractFrame> Contracts => [];
}

/// <summary>Host-raised notifications an extension may subscribe to.</summary>
public interface IWorldPulse
{
    event Action<EntityFrame> EntitySpawned;

    event Action<double> Tick;
}
