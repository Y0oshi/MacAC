using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

internal sealed partial class AutoWieldDriver
{
    public bool TryWield(ClientThing gear)
    {
        _queuedFightingSettlement = null;
        _fightingChangeoverObservedDuringSwitch = false;
        return TryWield(gear, WieldBitmask.None, fightingMannerFollowingWield: null);
    }

    public bool TryWield(ClientThing gear, WieldBitmask askedBitmask)
    {
        _queuedFightingSettlement = null;
        _fightingChangeoverObservedDuringSwitch = false;
        return TryWield(gear, askedBitmask, fightingMannerFollowingWield: null);
    }

    private bool TryWield(
        ClientThing gear,
        WieldBitmask askedBitmask,
        FightingManner? fightingMannerFollowingWield)
    {
        ArgumentNullException.ThrowIfNull(gear);

        if (_queuedSwitch is not null)
            return true;
        if (gear.ValidLocations == WieldBitmask.None)
            return false;
        if (askedBitmask != WieldBitmask.None
            && (askedBitmask & gear.ValidLocations) != askedBitmask)
            return false;

        if (gear.CurrentlyEquippedLocale != WieldBitmask.None)
        {
            if (askedBitmask == WieldBitmask.None
                || askedBitmask == gear.CurrentlyEquippedLocale)
                return false;
            return FetchEquippedObjectAtLocale(
                askedBitmask, precedence: 0, gear.ObjectId) is { } blocker
                ? CommenceWeaponSubstitute(
                    gear.ObjectId,
                    blocker,
                    askedBitmask,
                    fightingMannerFollowingWield)
                : TransmitWield(gear, askedBitmask, fightingMannerFollowingWield);
        }

        if (GearEquipRules.IsAutoDonGear(gear))
        {
            if (!AutoDonIsLegal(gear, out ClientThing? blocker))
            {
                if (blocker is not null)
                {
                    _sysMsg?.Invoke(
                        $"You must remove your {blocker.FetchAppropriateLabel()} to wear that");
                }
                return false;
            }

            return TransmitWield(
                gear,
                gear.ValidLocations,
                fightingMannerFollowingWield: null);
        }

        WieldBitmask weaponLocale = askedBitmask == WieldBitmask.None
            ? gear.ValidLocations & WeaponPrimedBitmask
            : askedBitmask & WeaponPrimedBitmask;
        if (weaponLocale != WieldBitmask.None)
        {
            fightingMannerFollowingWield ??= (_fightingPhase?.LatestMode ?? FightingManner.NonCombat)
                == FightingManner.NonCombat
                ? null
                : FightingMannerForWeaponLocale(weaponLocale);

            var blocker = FetchEquippedObjectAtLocale(
                WeaponPrimedBitmask, precedence: 0, gear.ObjectId);
            if (blocker is not null)
                return CommenceWeaponSubstitute(
                    gear.ObjectId,
                    blocker,
                    weaponLocale,
                    fightingMannerFollowingWield);

            if (ChunksUseOfShield(gear)
                && FetchEquippedObjectAtLocale(
                    WieldBitmask.Shield, precedence: 0, gear.ObjectId) is { } shield)
                return CommenceWeaponSubstitute(
                    gear.ObjectId,
                    shield,
                    weaponLocale,
                    fightingMannerFollowingWield);

            return gear.AmmoType is > 0
                && FetchEquippedObjectAtLocale(
                    WieldBitmask.MissileAmmo, precedence: 0, gear.ObjectId) is { } ammo
                && ammo.AmmoType != gear.AmmoType
                ? CommenceWeaponSubstitute(
                    gear.ObjectId,
                    ammo,
                    weaponLocale,
                    fightingMannerFollowingWield)
                : TransmitWield(gear, weaponLocale, fightingMannerFollowingWield);
        }

        WieldBitmask bitmask = askedBitmask != WieldBitmask.None
            ? askedBitmask
            : FinestOnHandWieldBitmask(gear);
        if (bitmask == WieldBitmask.None)
        {
            bitmask = LeadCompatibleWieldBitmask(gear);
            var blocker = FetchEquippedObjectAtLocale(
                bitmask, precedence: 0, gear.ObjectId);
            return blocker is not null
                && CommenceWeaponSubstitute(
                    gear.ObjectId,
                    blocker,
                    bitmask,
                    fightingMannerFollowingWield: null);
        }

        return TransmitWield(gear, bitmask, fightingMannerFollowingWield: null);
    }
}
