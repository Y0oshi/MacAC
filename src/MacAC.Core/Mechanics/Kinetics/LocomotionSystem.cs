namespace MacAC.Mechanics.Kinetics;

/// <summary>The retail run-rate, jump-height and jump-stamina formulas.</summary>
public static class LocomotionSystem
{
    private const int MaxedExecAptitude = 800;
    private const float LowerLeapHeight = 0.35f;

    public static float FetchExecRate(float burden, int execAptitude, float scaling = 1f)
    {
        if (execAptitude == MaxedExecAptitude)
            return 18f / 4f;

        float pullMod = BurdenSystem.PullMod(burden);
        float aptitudeTerm = (float)execAptitude / (execAptitude + 200) * 11f;
        return ((pullMod * aptitudeTerm + 4f) / scaling) / 4f;
    }

    public static float FetchLeapHeight(float burden, int leapAptitude, float strength, float scaling = 1f)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        float pullMod = BurdenSystem.PullMod(burden);
        float aptitudeTerm = (float)leapAptitude / (leapAptitude + 1300f) * 22.2f + 0.05f;
        float height = pullMod * aptitudeTerm * strength / scaling;
        return height < LowerLeapHeight ? LowerLeapHeight : height;
    }

    public static int LeapStaminaPrice(float strength, float burden, bool pk)
    {
        if (pk)
            return (int)((strength + 1.0f) * 100.0f);
        return (int)Math.Ceiling((burden + 0.5f) * strength * 8f + 2f);
    }

    public static float FetchLeapStrength(uint stamina, float burden, bool pk)
    {
        return pk ? stamina / 100.0f - 1.0f : (stamina - 2.0f) / (burden * 8.0f + 4.0f);
    }
}
