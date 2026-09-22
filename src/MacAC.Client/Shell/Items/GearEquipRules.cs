using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

internal static class GearEquipRules
{
    public const WieldBitmask AutoDonBitmask =
        WieldBitmask.HeadWear
        | WieldBitmask.ChestWear
        | WieldBitmask.AbdomenWear
        | WieldBitmask.UpperArmWear
        | WieldBitmask.LowerArmWear
        | WieldBitmask.HandWear
        | WieldBitmask.UpperLegWear
        | WieldBitmask.LowerLegWear
        | WieldBitmask.FootWear
        | WieldBitmask.ChestArmor
        | WieldBitmask.AbdomenArmor
        | WieldBitmask.UpperArmArmor
        | WieldBitmask.LowerArmArmor
        | WieldBitmask.UpperLegArmor
        | WieldBitmask.LowerLegArmor
        | WieldBitmask.Cloak;

    public static bool IsAutoDonGear(ClientThing gear)
        => (gear.ValidLocations & AutoDonBitmask) != WieldBitmask.None;

    public static WieldBitmask LocatePaperdollDiscardWieldBitmask(ClientThing gear, WieldBitmask markBitmask)
    {
        return (gear.ValidLocations & markBitmask) == WieldBitmask.None
            ? WieldBitmask.None
            : IsAutoDonGear(gear)
            ? gear.ValidLocations
            : gear.ValidLocations & markBitmask;
    }
}
