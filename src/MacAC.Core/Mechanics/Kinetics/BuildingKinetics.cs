using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>A placed building: its world transform and the portals that let cells see through it.</summary>
public sealed class BuildingKinetics
{
    public required Matrix4x4 WorldTransform { get; init; }
    public required Matrix4x4 InverseWorldTransform { get; init; }
    public required IReadOnlyList<BuildingPortalFacts> Portals { get; init; }

    public uint ModelId { get; init; }
}

/// <summary>One building portal and the cell on its far side.</summary>
public readonly struct BuildingPortalFacts(uint anotherChamberIdent, short anotherGatewayIdent, ushort flagSet)
{
    public uint OtherCellId { get; } = anotherChamberIdent;
    public short AnotherPortalId { get; } = anotherGatewayIdent;
    public ushort Flags { get; } = flagSet;

    public bool ExactFit => (Flags & (ushort)DoorwayBits.ExactMatch) is not 0;
}
