namespace MacAC.Mechanics.Gear;

/// <summary>Which vendor wares sell one at a time, and how many to offer otherwise.</summary>
public static class VendorSplitRules
{
    public const uint DivideExemptBitmask = 0x0DC41CB0u;

    public static bool IsDivideExempt(GearKind gearKind) => ((uint)gearKind & DivideExemptBitmask) is not 0u;

    public static int SeedQty(GearKind gearKind, int? authoredPileDims)
    {
        return IsDivideExempt(gearKind) ? 1 : authoredPileDims is > 0 and { } dims ? dims : 1;
    }

    public static int LocateAuthoredPileDims(int? dscPileDims, int? upperPileDims)
    {
        return upperPileDims is > 0 and { } upper ? upper : dscPileDims is > 0 and { } descriptor ? descriptor : 1;
    }
}
