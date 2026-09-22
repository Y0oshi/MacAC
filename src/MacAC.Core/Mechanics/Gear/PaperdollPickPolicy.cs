namespace MacAC.Mechanics.Gear;

/// <summary>Which worn item a click on a paperdoll body location selects.</summary>
public static class PaperdollPickPolicy
{
    private const uint ArmorSocketsLo = 0x200u;
    private const uint ArmorSocketsHi = 0x4000u;
    private const uint ArmorDefaultPrecedence = 0x7Fu;

    public static uint FetchUpperSatchelObject(ClientThingChart objects, uint avatarIdent, WieldBitmask corpusLocaleBitmask)
    {
        if (avatarIdent is 0 || corpusLocaleBitmask == WieldBitmask.None || objects.Get(avatarIdent) is null)
            return 0;

        ClientThing? finest = null;
        foreach (ClientThing contender in objects.Objects)
        {
            if ((contender.CurrentlyEquippedLocale & corpusLocaleBitmask) == WieldBitmask.None)
                continue;
            if (contender.WielderIdent != avatarIdent && contender.VesselTag != avatarIdent)
                continue;
            if (finest is null || Rank(contender, corpusLocaleBitmask) > Rank(finest, corpusLocaleBitmask))
                finest = contender;
        }
        return finest?.ObjectId ?? avatarIdent;
    }

    // Armour without an explicit priority outranks clothing
    private static uint Rank(ClientThing gear, WieldBitmask corpusLocaleBitmask)
    {
        uint covered = (uint)(gear.CurrentlyEquippedLocale & corpusLocaleBitmask);
        return gear.Priority is 0 && covered is >= ArmorSocketsLo and <= ArmorSocketsHi ? ArmorDefaultPrecedence : gear.Priority;
    }
}
