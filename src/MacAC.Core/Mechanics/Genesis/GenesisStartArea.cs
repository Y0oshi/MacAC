using System.Numerics;

namespace MacAC.Mechanics.Genesis;

public readonly record struct GenesisSpawnPoint(uint CellId, Vector3 Origin, Quaternion Orientation);

public sealed record GenesisStartArea(
    int Index,
    string Name,
    IReadOnlyList<GenesisSpawnPoint> Locations);
