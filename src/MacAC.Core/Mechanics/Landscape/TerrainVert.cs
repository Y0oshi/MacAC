using System.Numerics;

namespace MacAC.Mechanics.Landscape;

/// <summary>One terrain vertex; the four data words carry the packed layer recipe.</summary>
public readonly record struct TerrainVert(
    Vector3 Position,
    Vector3 Normal,
    uint Data0,
    uint Data1,
    uint Data2,
    uint Data3);
