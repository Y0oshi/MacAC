using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Kinetics-side face of the burden rules, kept for the movement formulas.</summary>
public static class BurdenSystem
{
    public static int EncumbranceCapacity(int strength, int aug) => BurdenRules.EncumbranceCapacity(strength, aug);

    public static float Load(int cap, int burden) => BurdenRules.PullRatio(cap, burden);

    public static float PullMod(float pull) => BurdenRules.PullModifier(pull);
}
