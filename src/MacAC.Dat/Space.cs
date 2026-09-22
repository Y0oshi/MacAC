using System.Numerics;

#nullable disable

namespace MacAC.Dat;

// Texel layouts a decoded bitmap can be handed over in.
public enum TexelLayout
{
    RGBA8,
    RGB8,
    A8,
    Rgba32f,
    DXT1,
    DXT3,
    DXT5,
}

// An axis-aligned box.
public struct Bounds3
{
    public Vector3 Min;
    public Vector3 Max;
    public readonly Vector3 Center => (Min + Max) * 0.5f;
    public readonly Vector3 Size => Max - Min;
    public Bounds3(Vector3 min, Vector3 max) { Min = min; Max = max; }
}
