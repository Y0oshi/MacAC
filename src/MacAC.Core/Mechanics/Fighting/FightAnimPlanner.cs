using MacAC.Dat;
using System.Collections.Frozen;
using MacAC.Mechanics.Kinetics;
using Drw =  MacAC.Dat.MotionId;

namespace MacAC.Mechanics.Fighting;

public enum FightAnimEvent
{
    CombatCommenceAttack,
    AttackDone,
    AttackerNotification,
    DefenderNotification,
    EvasionAttackerNotification,
    EvasionDefenderNotification,
    VictimNotification,
    KillerNotification,
}

public enum FightAnimKind
{
    None = 0,
    CombatStance,
    MeleeSwing,
    MissileAttack,
    CreatureAttack,
    SpellCast,
    HitReaction,
    Death,
}

public readonly record struct FightAnimPlan(
    FightAnimKind Kind,
    AnimCommandRouteKind RouteKind,
    uint MotionCommand,
    float SpeedMod)
{
    public static FightAnimPlan None { get; } = new(FightAnimKind.None, AnimCommandRouteKind.None, 0u, 0f);

    public bool HasLocomotion => Kind != FightAnimKind.None && MotionCommand is not 0;
}

/// <summary>Sorts full motion commands into the combat animation families the UI cares about.</summary>
public static class FightAnimPlanner
{
    private static readonly FrozenDictionary<uint, FightAnimKind> SortByDirective = AssembleChart();

    public static FightAnimPlan PlanForSignal(FightAnimEvent fightingSignal)
    {
        _ = fightingSignal;
        return FightAnimPlan.None;
    }

    public static FightAnimPlan PlanFromWireDirective(ushort wireDirective, float paceMod = 1f)
    {
        return PlanFromWholeDirective(MotionCommandLookup.ReconstructWholeCommand(wireDirective), paceMod);
    }

    public static FightAnimPlan PlanFromWholeDirective(uint wholeDirective, float paceMod = 1f)
    {
        var sort = ClassifyLocomotionDirective(wholeDirective);
        return sort == FightAnimKind.None
            ? FightAnimPlan.None
            : new FightAnimPlan(sort, AnimCommandRouter.Classify(wholeDirective), wholeDirective, paceMod);
    }

    public static FightAnimKind ClassifyLocomotionDirective(uint wholeDirective) =>
        SortByDirective.GetValueOrDefault(wholeDirective, FightAnimKind.None);

    private static FrozenDictionary<uint, FightAnimKind> AssembleChart()
    {
        var chart = new Dictionary<uint, FightAnimKind>();

        void Clan(FightAnimKind sort, params Drw[] directives)
        {
            foreach (Drw directive in directives)
                chart[(uint)directive] = sort;
        }

        Clan(FightAnimKind.CombatStance,
            Drw.HandCombat, Drw.SwordCombat, Drw.SwordShieldCombat, Drw.TwoHandedSwordCombat,
            Drw.TwoHandedStaffCombat, Drw.BowCombat, Drw.CrossbowCombat, Drw.SlingCombat,
            Drw.DualWieldCombat, Drw.ThrownWeaponCombat, Drw.AtlatlCombat, Drw.ThrownShieldCombat,
            Drw.Magic);

        Clan(FightAnimKind.MeleeSwing,
            Drw.ThrustMed, Drw.ThrustLow, Drw.ThrustHigh,
            Drw.SlashHigh, Drw.SlashMed, Drw.SlashLow,
            Drw.BackhandHigh, Drw.BackhandMed, Drw.BackhandLow,
            Drw.DoubleSlashLow, Drw.DoubleSlashMed, Drw.DoubleSlashHigh,
            Drw.TripleSlashLow, Drw.TripleSlashMed, Drw.TripleSlashHigh,
            Drw.DoubleThrustLow, Drw.DoubleThrustMed, Drw.DoubleThrustHigh,
            Drw.TripleThrustLow, Drw.TripleThrustMed, Drw.TripleThrustHigh,
            Drw.OffhandSlashHigh, Drw.OffhandSlashMed, Drw.OffhandSlashLow,
            Drw.OffhandThrustHigh, Drw.OffhandThrustMed, Drw.OffhandThrustLow,
            Drw.OffhandDoubleSlashLow, Drw.OffhandDoubleSlashMed, Drw.OffhandDoubleSlashHigh,
            Drw.OffhandTripleSlashLow, Drw.OffhandTripleSlashMed, Drw.OffhandTripleSlashHigh,
            Drw.OffhandDoubleThrustLow, Drw.OffhandDoubleThrustMed, Drw.OffhandDoubleThrustHigh,
            Drw.OffhandTripleThrustLow, Drw.OffhandTripleThrustMed, Drw.OffhandTripleThrustHigh,
            Drw.OffhandKick,
            Drw.PunchFastHigh, Drw.PunchFastMed, Drw.PunchFastLow,
            Drw.PunchSlowHigh, Drw.PunchSlowMed, Drw.PunchSlowLow,
            Drw.OffhandPunchFastHigh, Drw.OffhandPunchFastMed, Drw.OffhandPunchFastLow,
            Drw.OffhandPunchSlowHigh, Drw.OffhandPunchSlowMed, Drw.OffhandPunchSlowLow);

        Clan(FightAnimKind.MissileAttack,
            Drw.Shoot, Drw.MissileAttack1, Drw.MissileAttack2, Drw.MissileAttack3, Drw.Reload);

        Clan(FightAnimKind.CreatureAttack,
            Drw.AttackHigh1, Drw.AttackMed1, Drw.AttackLow1,
            Drw.AttackHigh2, Drw.AttackMed2, Drw.AttackLow2,
            Drw.AttackHigh3, Drw.AttackMed3, Drw.AttackLow3,
            Drw.AttackHigh4, Drw.AttackMed4, Drw.AttackLow4,
            Drw.AttackHigh5, Drw.AttackMed5, Drw.AttackLow5,
            Drw.AttackHigh6, Drw.AttackMed6, Drw.AttackLow6);

        Clan(FightAnimKind.SpellCast, Drw.CastSpell, Drw.UseMagicStaff, Drw.UseMagicWand);

        Clan(FightAnimKind.HitReaction,
            Drw.FallDown, Drw.Twitch1, Drw.Twitch2, Drw.Twitch3, Drw.Twitch4,
            Drw.StaggerBackward, Drw.StaggerForward, Drw.Sanctuary);

        chart[LocomotionDirective.Dead] = FightAnimKind.Death;

        return chart.ToFrozenDictionary();
    }
}
