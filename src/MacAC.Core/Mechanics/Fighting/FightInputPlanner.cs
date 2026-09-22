using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Fighting;

public readonly record struct DefaultStanceDecision(
    FightingManner Mode,
    ClientThing? IncompatibleHeldItem);

/// <summary>Which stance the player drops into, given what they are holding.</summary>
public static class FightInputPlanner
{
    public const uint DualWieldFightingStyling = 0x80000046u;
    public const uint PrimedAheadDirective = 0x41000003u;
    public const double AssaultStrengthUpSecs = 1.0;
    public const double DualWieldStrengthUpSecs = 0.8;

    private const int MissileFightingUse = 2;

    private const WieldBitmask WeaponHands = WieldBitmask.MeleeWeapon | WieldBitmask.MissileWeapon | WieldBitmask.TwoHanded;

    // Missile styles whose "ready" pose lets an attack begin immediately
    private static readonly HashSet<uint> PrimedMissileStylings =
        [0x8000003Fu, 0x80000041u, 0x80000043u, 0x80000047u, 0x80000138u, 0x80000139u];

    public static bool AvatarInPrimedLocusForAssault(FightingManner manner, uint latestStyling, uint aheadDirective)
    {
        if (manner == FightingManner.Melee)
            return true;
        return manner == FightingManner.Missile
            && aheadDirective == PrimedAheadDirective
            && PrimedMissileStylings.Contains(latestStyling);
    }

    public static FightingManner FetchDefaultFightingManner(IReadOnlyList<ClientThing> sequencedAvatarInsides) =>
        FetchDefaultFightingMannerDecision(sequencedAvatarInsides).Mode;

    public static DefaultStanceDecision FetchDefaultFightingMannerDecision(IReadOnlyList<ClientThing> sequencedAvatarInsides)
    {
        ArgumentNullException.ThrowIfNull(sequencedAvatarInsides);

        if (LeadWornAt(sequencedAvatarInsides, WeaponHands) is { } weapon)
        {
            FightingManner manner = weapon.CombatUse == MissileFightingUse ? FightingManner.Missile : FightingManner.Melee;
            return new DefaultStanceDecision(manner, IncompatibleHeldItem: null);
        }

        if (LeadWornAt(sequencedAvatarInsides, WieldBitmask.Held) is not { } pinned)
            return new DefaultStanceDecision(FightingManner.Melee, IncompatibleHeldItem: null);

        return (pinned.Type & GearKind.Caster) != 0
            ? new DefaultStanceDecision(FightingManner.Magic, IncompatibleHeldItem: null)
            : new DefaultStanceDecision(FightingManner.NonCombat, pinned);
    }

    public static FightingManner FlipManner(FightingManner latestManner, FightingManner defaultFightingManner = FightingManner.Melee)
    {
        return latestManner == FightingManner.NonCombat ? defaultFightingManner : FightingManner.NonCombat;
    }

    public static bool SupportsTargetedAssault(FightingManner manner) => manner is FightingManner.Melee or FightingManner.Missile;

    public static AssaultElevation HeightFor(StrikeAction act)
    {
        return act switch
        {
            StrikeAction.Low => AssaultElevation.Low,
            StrikeAction.High => AssaultElevation.High,
            _ => AssaultElevation.Medium,
        };
    }

    private static ClientThing? LeadWornAt(IReadOnlyList<ClientThing> insides, WieldBitmask sockets)
    {
        foreach (ClientThing gear in insides)
        {
            if ((gear.CurrentlyEquippedLocale & sockets) != 0)
                return gear;
        }
        return null;
    }
}
