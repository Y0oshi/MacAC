using MacAC.Dat;

namespace MacAC.Mechanics.Geometry;

public static class CanonBareSurfacePolicy
{
    /// <summary>A surface with neither an image nor a clip map is a flat colour.</summary>
    public static bool IsUntextured(SkinBits kind) =>
        (kind & (SkinBits.Base1Image | SkinBits.Base1ClipMap)) == 0;
}

public static class CanonBareSubsetPolicy
{
    /// <summary>Building shells skip their untextured subsets; everything else draws.</summary>
    public static bool Draws(bool isStructureShell, bool isUntextured) => !(isUntextured && isStructureShell);
}
