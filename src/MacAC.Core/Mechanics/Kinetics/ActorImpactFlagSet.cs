using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Kinetics;

[Flags]
public enum ActorImpactFlagSet : byte
{
    None = 0x00,
    IsPlayer = 0x01,
    IsCreature = 0x02,
    IsPK = 0x04,
    IsPKLite = 0x08,
    IsImpenetrable = 0x10,
    HasWeenie = 0x20,
    CanBypassMoveRestrictions = 0x40,
}

public static class EntityContactFlagsExt
{
    private const uint BfAvatar = 0x8u;
    private const uint BfAvatarKiller = 0x20u;
    private const uint BfAdmin = 0x100000u;
    private const uint BfImpenetrable = 0x200000u;
    private const uint BfSentinel = 0x400000u;
    private const uint BfPkLite = 0x2000000u;

    public const ActorImpactFlagSet PwdBitfieldDerivedBitmask =
        ActorImpactFlagSet.IsPlayer
        | ActorImpactFlagSet.IsPK
        | ActorImpactFlagSet.IsPKLite
        | ActorImpactFlagSet.IsImpenetrable
        | ActorImpactFlagSet.CanBypassMoveRestrictions;

    public static ActorImpactFlagSet FromPwdBitfield(uint bitfield)
    {
        var flagSet = ActorImpactFlagSet.None;
        if ((bitfield & BfAvatar) is not 0) flagSet |= ActorImpactFlagSet.IsPlayer;
        if ((bitfield & BfAvatarKiller) is not 0) flagSet |= ActorImpactFlagSet.IsPK;
        if ((bitfield & BfImpenetrable) is not 0) flagSet |= ActorImpactFlagSet.IsImpenetrable;
        if ((bitfield & BfPkLite) is not 0) flagSet |= ActorImpactFlagSet.IsPKLite;
        if ((bitfield & (BfAdmin | BfSentinel)) == (BfAdmin | BfSentinel))
            flagSet |= ActorImpactFlagSet.CanBypassMoveRestrictions;
        return flagSet;
    }

    public static MoverState ToCarrierPhase(this ActorImpactFlagSet flagSet)
    {
        MoverState phase = MoverState.None;
        if ((flagSet & ActorImpactFlagSet.IsPK) != 0) phase |= MoverState.IsPK;
        if ((flagSet & ActorImpactFlagSet.IsPKLite) != 0) phase |= MoverState.IsPKLite;
        if ((flagSet & ActorImpactFlagSet.IsImpenetrable) != 0) phase |= MoverState.IsImpenetrable;
        if ((flagSet & ActorImpactFlagSet.CanBypassMoveRestrictions) != 0) phase |= MoverState.CanBypassMoveRestrictions;
        return phase;
    }

    public static MoverState LocateCarrierPvpPhase(this ClientThingChart objects, uint srvOid)
    {
        ArgumentNullException.ThrowIfNull(objects);
        return objects.Get(srvOid)?.PublicWeenieBitfield is { } bitfield
            ? FromPwdBitfield(bitfield).ToCarrierPhase()
            : MoverState.None;
    }
}
