namespace MacAC.Mechanics.Gear;

public static class HotbarAmmoRules
{
    public readonly record struct MechResult(uint ObjectId, int DisplayCount)
    {
        public bool IsVisible => ObjectId is not 0;
    }

    public static MechResult Resolve(IReadOnlyList<ClientThing> satchelStances)
    {
        ArgumentNullException.ThrowIfNull(satchelStances);
        var weapon = WornAt(satchelStances, WieldBitmask.MissileWeapon);
        ClientThing? ammo = weapon is { PileDimsUpper: > 1 } ? weapon : WornAt(satchelStances, WieldBitmask.MissileAmmo);
        return ammo is null ? default : new MechResult(ammo.ObjectId, ammo.StackSize is 0 ? 1 : ammo.StackSize);
    }

    private static ClientThing? WornAt(IReadOnlyList<ClientThing> gearList, WieldBitmask socket)
    {
        foreach (ClientThing gear in gearList)
        {
            if ((gear.CurrentlyEquippedLocale & socket) != 0)
                return gear;
        }
        return null;
    }
}
