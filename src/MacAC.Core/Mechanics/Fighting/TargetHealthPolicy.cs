using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Fighting;

/// <summary>Who may be attacked, and whose health bar is worth asking the server for.</summary>
public static class TargetHealthPolicy
{
    public const uint BfAvatar = 0x00000008u;
    public const uint BfAttackable = 0x00000010u;
    public const uint BfAvatarKiller = 0x00000020u;
    public const uint BfSparePkCondition = 0x00200000u;
    public const uint BfPkLiteCondition = 0x02000000u;

    public static bool ShouldAskHealth(uint avatarIdent, ClientThing? avatar, ClientThing? chosen)
    {
        if (chosen is null)
            return false;
        uint bitset = chosen.PublicWeenieBitfield ?? 0u;
        return (bitset & BfAvatar) is not 0
            || chosen.PetHolderIdent is not 0
            || ObjectIsAttackable(avatarIdent, avatar, chosen.ObjectId, chosen);
    }

    public static bool ObjectIsAttackable(uint avatarIdent, ClientThing? avatar, uint markIdent, ClientThing? mark)
    {
        if (markIdent is 0 || markIdent == avatarIdent)
            return true;
        if (mark is null || (mark.Type & GearKind.Creature) == 0)
            return false;

        uint theirs = mark.PublicWeenieBitfield ?? 0u;
        if ((theirs & BfSparePkCondition) is not 0)
            return true;
        if (avatar is null)
            return false;

        uint ours = avatar.PublicWeenieBitfield ?? 0u;
        if ((ours & BfSparePkCondition) is not 0)
            return true;

        if ((theirs & BfAvatar) is not 0)
        {
            // Player versus player needs a shared PK or PK-lite status
            return Both(theirs, ours, BfAvatarKiller) || Both(theirs, ours, BfPkLiteCondition);
        }

        return mark.PetHolderIdent is 0 && (theirs & BfAttackable) is not 0;
    }

    private static bool Both(uint a, uint b, uint bit) => (a & b & bit) is not 0;
}

/// <summary>Eligibility for automatic monster acquisition.</summary>
public static class FightTargetPolicy
{
    public static bool IsHostileMonster(uint avatarIdent, ClientThing? avatar, ClientThing? contender)
    {
        if (contender is null || contender.ObjectId == avatarIdent)
            return false;
        if ((contender.Type & GearKind.Creature) == 0)
            return false;
        if ((contender.PublicWeenieBitfield.GetValueOrDefault() & TargetHealthPolicy.BfAvatar) is not 0)
            return false;
        if (contender.PetHolderIdent is not 0)
            return false;
        return TargetHealthPolicy.ObjectIsAttackable(avatarIdent, avatar, contender.ObjectId, contender);
    }
}
