using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Arcana;

public readonly record struct SpellTargetRuling(bool Allowed, string? Message)
{
    public static SpellTargetRuling Admit { get; } = new(true, null);

    public static SpellTargetRuling Deny(string msg) => new(false, msg);
}

/// <summary>The client-side checks that refuse a cast before it reaches the server.</summary>
public static class CanonSpellTargetRules
{
    private const uint SpecialMarkBitmask = 0x00008107u;

    public static SpellTargetRuling Evaluate(uint ownAvatarIdent, ClientThing mark, SpellMeta arcanum)
    {
        uint bitmask = arcanum.TargetMask;
        bool special = (bitmask & SpecialMarkBitmask) is not 0u;

        if (mark.ObjectId == ownAvatarIdent && !special)
            return SpellTargetRuling.Deny("You cannot cast this spell upon yourself.");
        if (mark.StackSize > 1)
            return SpellTargetRuling.Deny("Cannot cast spell on a stack of items.");
        if (((uint)mark.Type & bitmask) is 0u && !special)
            return SpellTargetRuling.Deny($"This spell cannot be cast on {mark.Name}.");

        PublicWeenieBits bitset = (PublicWeenieBits)mark.PublicWeenieBitfield.GetValueOrDefault();
        bool avatarOrAttackable = (bitset & (PublicWeenieBits.Player | PublicWeenieBits.Attackable)) != 0;
        if (!avatarOrAttackable || mark.PetHolderIdent is not 0u)
            return SpellTargetRuling.Deny($"This spell cannot be cast on {mark.Name}.");

        return SpellTargetRuling.Admit;
    }
}
