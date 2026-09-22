namespace MacAC.Mechanics.Kinetics.Gait;

/// <summary>How far a tethered object may stray before the leash starts, and where it stops.</summary>
public static class TetherDistance
{
    private readonly record struct Leash(float Start, float Max);

    private static readonly Leash Outdoors = new(10.0f, 50.0f);
    private static readonly Leash Indoors = new(5.0f, 20.0f);

    public static bool IsInsideChamber(uint objRefChamberIdent) => (objRefChamberIdent & 0xFFFFu) >= 0x0100u;

    public static float FetchBeginConstraintGap(uint objRefChamberIdent) => For(objRefChamberIdent).Start;

    public static float FetchUpperConstraintGap(uint objRefChamberIdent) => For(objRefChamberIdent).Max;

    private static Leash For(uint objRefChamberIdent) => IsInsideChamber(objRefChamberIdent) ? Indoors : Outdoors;
}
