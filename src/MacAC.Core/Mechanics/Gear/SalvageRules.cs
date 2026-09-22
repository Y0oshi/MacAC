namespace MacAC.Mechanics.Gear;

public static class SalvageRules
{
    /// <summary>The retail material id ranges that can be salvaged.</summary>
    public static bool IsValidMatl(uint matl)
    {
        return matl is
        1 or 2 or (>= 4 and <= 8) or (>= 10 and <= 55) or (>= 57 and <= 64) or (>= 66 and <= 71) or (>= 73 and <= 77);
    }

    public static bool IsSuitable(ClientThing gear, uint chosenMatl = 0, bool allowMultipleMatls = true)
    {
        uint matl = gear.MaterialType ?? 0u;
        return IsValidMatl(matl)
            && gear.Structure < 100
            && ((gear.PublicWeenieBitfield ?? 0u) & 0xFF000000u) is 0u
            && (allowMultipleMatls || chosenMatl is 0u || chosenMatl == matl);
    }
}
