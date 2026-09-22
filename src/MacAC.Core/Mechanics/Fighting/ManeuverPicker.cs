using MacAC.Dat;
using DatAttackHeight =  MacAC.Dat.StrikeHeight;
using DatAttackType =  MacAC.Dat.StrikeKind;
using DatMotionCommand =  MacAC.Dat.MotionId;
using DatMotionStance =  MacAC.Dat.Stance;

namespace MacAC.Mechanics.Fighting;

public readonly record struct ManeuverPick(
    bool Found,
    DatMotionCommand Motion,
    IReadOnlyList<DatMotionCommand> Candidates,
    DatAttackType EffectiveAttackType,
    float Subdivision)
{
    public static ManeuverPick None { get; } = new(
        Found: false,
        Motion: DatMotionCommand.Invalid,
        Candidates: [],
        EffectiveAttackType: DatAttackType.Undef,
        Subdivision: 0f);
}

public static class ManeuverPicker
{
    public const float DefaultSubdivision = 0.33f;
    public const float ThrustSlashSubdivision = 0.66f;

    public static ManeuverPick PickLocomotion(
        ManeuverBook chart,
        DatMotionStance stance,
        DatAttackHeight assaultHeight,
        DatAttackType assaultKind,
        float strengthTier,
        bool isThrustSlashWeapon = false)
    {
        var fits = SeekMotions(chart, stance, assaultHeight, assaultKind);
        if (fits.Count is 0)
            return ManeuverPick.None;

        float subdivision = isThrustSlashWeapon ? ThrustSlashSubdivision : DefaultSubdivision;
        bool weakSwing = fits.Count > 1 && strengthTier < subdivision;
        return new ManeuverPick(
            Found: true,
            Motion: fits[weakSwing ? 1 : 0],
            Candidates: fits,
            EffectiveAttackType: assaultKind,
            Subdivision: subdivision);
    }

    public static IReadOnlyList<DatMotionCommand> SeekMotions(
        ManeuverBook chart,
        DatMotionStance stance,
        DatAttackHeight assaultHeight,
        DatAttackType assaultKind)
    {
        var motions = new List<DatMotionCommand>();
        foreach (Maneuver maneuver in chart.Maneuvers)
        {
            if (maneuver.Stance == stance && maneuver.Height == assaultHeight && maneuver.Attack == assaultKind)
                motions.Add(maneuver.Motion);
        }
        return motions;
    }
}
