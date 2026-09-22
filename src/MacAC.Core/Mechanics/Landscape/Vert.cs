using System.Numerics;

namespace MacAC.Mechanics.Landscape;

public readonly record struct MechVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 TexCoord,
    uint TerrainLayer);
