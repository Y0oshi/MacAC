using MacAC.Dat;

namespace MacAC.Assets;

/// <summary>Identifies one decoded texture variant: surface, palette, stipple and solid-colour flag.</summary>
public struct BitmapTag : IEquatable<BitmapTag>
{
    public uint SurfaceId;
    public uint PaletteId;
    public StippleBits Stippling;
    public bool IsSolid;

    public readonly bool Equals(BitmapTag another)
    {
        return SurfaceId == another.SurfaceId && PaletteId == another.PaletteId && Stippling == another.Stippling && IsSolid == another.IsSolid;
    }

    public override readonly bool Equals(object? objRef) => objRef is BitmapTag another && Equals(another);

    public override readonly int GetHashCode() => HashCode.Combine(SurfaceId, PaletteId, Stippling, IsSolid);
}
