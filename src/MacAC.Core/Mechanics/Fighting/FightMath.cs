namespace MacAC.Mechanics.Fighting;

public static class FightMath
{
    public const double PhysicalCritBase = 0.10;
    public const double MagicCritBase = 0.05;

    /// <summary>Melee power bar: [0.5, 1.5].</summary>
    public static float StrengthModMelee(float strengthTier) => strengthTier + 0.5f;

    /// <summary>Missile accuracy bar: [0.6, 1.6].</summary>
    public static float AccuracyModMissile(float accuracyTier) => accuracyTier + 0.6f;

    public static double SmackChancePhysical(int assaultAptitude, int defenseAptitude) =>
        Logistic(0.03 * (assaultAptitude - defenseAptitude));

    public static double SmackChanceMagic(int assaultAptitude, int defenseAptitude) =>
        Logistic(0.07 * (assaultAptitude - defenseAptitude));

    public static int CalculateHarm(
        float weaponHarmLower,
        float weaponHarmUpper,
        int attrBonus,
        float strengthMod,
        float aptitudeMod,
        bool isCritical,
        float critMultiplier,
        float armorReduction,
        float resistMultiplier,
        Random rng)
    {
        double roll = rng.NextDouble() * (weaponHarmUpper - weaponHarmLower) + weaponHarmLower;
        double harm = (roll + attrBonus) * strengthMod * aptitudeMod;
        if (isCritical)
            harm *= critMultiplier;
        harm = Math.Max(0, harm - armorReduction);
        harm *= resistMultiplier;
        return (int)Math.Max(0, Math.Round(harm));
    }

    private static double Logistic(double x) => 1.0 - 1.0 / (1.0 + Math.Exp(x));
}

/// <summary>Armour level per body zone.</summary>
public sealed class ArmorComposition
{
    public int ALFront { get; set; }

    public int ALChest { get; set; }

    public int ALAbdomen { get; set; }

    public int ALUpperArm { get; set; }

    public int ALLowerArm { get; set; }

    public int ALHand { get; set; }

    public int ALUpperLeg { get; set; }

    public int ALLowerLeg { get; set; }

    public int ALFoot { get; set; }

    public int Get(BodyZone zone)
    {
        return zone switch
        {
            BodyZone.Head => ALFront,
            BodyZone.Chest => ALChest,
            BodyZone.Abdomen => ALAbdomen,
            BodyZone.UpperArm => ALUpperArm,
            BodyZone.LowerArm => ALLowerArm,
            BodyZone.Hand => ALHand,
            BodyZone.UpperLeg => ALUpperLeg,
            BodyZone.LowerLeg => ALLowerLeg,
            BodyZone.Foot => ALFoot,
            _ => 0,
        };
    }
}
