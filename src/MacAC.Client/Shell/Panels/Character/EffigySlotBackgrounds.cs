using MacAC.Assets;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public static class EffigySlotBackgrounds
{
    internal readonly record struct Spec(
        uint Element,
        WieldBitmask Mask,
        uint EmptyPrototype,
        AetheriaSlotState UnlockBit = AetheriaSlotState.None);

    private const WieldBitmask WeaponSocketBitmask =
        WieldBitmask.MeleeWeapon | WieldBitmask.MissileWeapon | WieldBitmask.Held | WieldBitmask.TwoHanded;

    internal static readonly Spec[] Definitions =
    [
        new(0x100005ABu, WieldBitmask.HeadWear,        0x100005B4u),
        new(0x100001E2u, WieldBitmask.ChestWear,       0x1000044Eu),
        new(0x100001E3u, WieldBitmask.UpperLegWear,    0x1000044Fu),
        new(0x100005B0u, WieldBitmask.HandWear,        0x100005B9u),
        new(0x100005B3u, WieldBitmask.FootWear,        0x100005BDu),
        new(0x100005ACu, WieldBitmask.ChestArmor,      0x100005B5u),
        new(0x100005ADu, WieldBitmask.AbdomenArmor,    0x100005B6u),
        new(0x100005AEu, WieldBitmask.UpperArmArmor,   0x100005B7u),
        new(0x100005AFu, WieldBitmask.LowerArmArmor,   0x100005B8u),
        new(0x100005B1u, WieldBitmask.UpperLegArmor,   0x100005BAu),
        new(0x100005B2u, WieldBitmask.LowerLegArmor,   0x100005BBu),
        new(0x100001DAu, WieldBitmask.NeckWear,        0x10000446u),
        new(0x100001DBu, WieldBitmask.WristWearLeft,   0x10000447u),
        new(0x100001DDu, WieldBitmask.WristWearRight,  0x10000449u),
        new(0x100001DCu, WieldBitmask.FingerWearLeft,  0x10000448u),
        new(0x100001DEu, WieldBitmask.FingerWearRight, 0x1000044Au),
        new(0x100001E1u, WieldBitmask.Shield,          0x1000044Du),
        new(0x100001E0u, WieldBitmask.MissileAmmo,     0x1000044Cu),
        new(0x100001DFu, WeaponSocketBitmask,            0x1000044Bu),
        new(0x1000058Eu, WieldBitmask.TrinketOne,      0x1000058Fu),
        new(0x100005E9u, WieldBitmask.Cloak,           0x100005EAu),
        new(0x10000595u, WieldBitmask.SigilOne,         0x10000592u, AetheriaSlotState.Blue),
        new(0x10000596u, WieldBitmask.SigilTwo,         0x10000593u, AetheriaSlotState.Yellow),
        new(0x10000597u, WieldBitmask.SigilThree,       0x10000594u, AetheriaSlotState.Red),
    ];

    public static IReadOnlyDictionary<uint, uint> LocateVacantSprites(IDatAccess datFiles)
    {
        var outcome = new Dictionary<uint, uint>(Definitions.Length);
        foreach (Spec definition in Definitions)
        {
            uint sprite = GearListCellTemplate.LocatePrototypeVacantSprite(
                datFiles,
                definition.EmptyPrototype);
            if (sprite is 0)
            {
                throw new InvalidOperationException(
                    $"Retail paperdoll UIItem prototype 0x{definition.EmptyPrototype:X8} " +
                    $"for slot 0x{definition.Element:X8} has no ItemSlot_Empty surface");
            }
            outcome.Add(definition.Element, sprite);
        }
        return outcome;
    }

    public static bool TryFetchPrototype(uint socketElemIdent, out uint prototypeElemIdent)
    {
        foreach (Spec definition in Definitions)
        {
            if (definition.Element != socketElemIdent) continue;
            prototypeElemIdent = definition.EmptyPrototype;
            return true;
        }
        prototypeElemIdent = 0;
        return false;
    }
}
