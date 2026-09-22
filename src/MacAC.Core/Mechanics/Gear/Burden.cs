namespace MacAC.Mechanics.Gear;

/// <summary>A pack: its capacity, contents and (for the main pack) side packs.</summary>
public sealed class Vessel
{
    public uint ObjectId { get; init; }

    /// <summary>Main inventory defaults to 102 slots.</summary>
    public int Capacity { get; set; } = 102;

    /// <summary>Zero for a side pack.</summary>
    public int FlankCap { get; set; }

    public int BurdenThreshold { get; set; }

    public List<ClientThing> Items { get; } = [];

    public List<Vessel> FlankBundles { get; } = [];

    public bool IsFlankBundle => FlankCap is 0;
}

/// <summary>Burden capacity, load ratio and the movement penalty that follows.</summary>
public static class BurdenRules
{
    public const int BurdenPerStrength = 150;
    public const int AugBurdenPerGrade = 30;
    public const int AugBurdenCap = 150;

    public static int EncumbranceCapacity(int strength, int aug)
    {
        if (strength <= 0)
            return 0;
        int bonus = Math.Clamp(aug * AugBurdenPerGrade, 0, AugBurdenCap);
        return strength * BurdenPerStrength + bonus * strength;
    }

    public static float PullRatio(int cap, int burden) => cap <= 0 ? 0f : (float)burden / cap;

    /// <summary>1.0 up to full load, then linearly down to 0 at double load.</summary>
    public static float PullModifier(float pull) => pull <= 1f ? 1f : pull < 2f ? 2f - pull : 0f;

    public static int PullPenaltyPct(float pull) => (10 - (int)(PullModifier(pull) * 10f)) * 10;

    /// <summary>The burden bar fill: a third of the load, clamped to [0, 1].</summary>
    public static float PullToPopulate(float pull) => Math.Clamp(pull / 3f, 0f, 1f);

    public static int PullToPct(float pull) => (int)MathF.Floor(PullToPopulate(pull) * 300f);

    public static int CalculateUpper(int strength, int bonusBurden) => BurdenPerStrength * strength + strength * bonusBurden;

    public static int CalculateCarryThreshold(int strength, int bonusBurden) => 3 * CalculateUpper(strength, bonusBurden);

    /// <summary>Flat to half load, then 1.0→0.7 at full and 0.7→0.1 at triple.</summary>
    public static float CalculateEncumbranceMod(int latestBurden, int upperBurden)
    {
        if (upperBurden <= 0)
            return 1f;
        float ratio = (float)latestBurden / upperBurden;
        return ratio switch
        {
            <= 0.5f => 1f,
            <= 1.0f => 1f - (ratio - 0.5f) * 0.6f,
            <= 3.0f => 0.7f - (ratio - 1.0f) * 0.3f,
            _ => 0.1f,
        };
    }
}
