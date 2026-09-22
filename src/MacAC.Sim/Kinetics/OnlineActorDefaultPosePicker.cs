using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Dat;

namespace MacAC.Sim.Kinetics;

// Resolves the default-stance part frames of a motion table, for entities that arrive without a
// movement blob
internal sealed class OnlineActorDefaultPosePicker(Func<uint, MotionBook?> loadMotionTable, IAnimReader animationLoader, bool printLocomotion)
{
    private readonly Func<uint, MotionBook?> _pullLocomotionChart = loadMotionTable ?? throw new ArgumentNullException(nameof(loadMotionTable));
    private readonly IAnimReader _anims = animationLoader ?? throw new ArgumentNullException(nameof(animationLoader));
    private readonly bool _printLocomotion = printLocomotion;

    public IReadOnlyList<Pose>? Resolve(uint locomotionChartIdent, int pieceTally)
    {
        if (locomotionChartIdent is 0u || pieceTally is 0)
            return null;
        if (_pullLocomotionChart(locomotionChartIdent) is not { } chart)
            return null;

        var posture = MotionTableStance.DefaultPhasePieceCycles(chart, _anims.PullAnim);
        if (_printLocomotion)
        {
            string summary = posture is null
                ? "null->placement-fallback"
                : FormattableString.Invariant($"part0=({posture[0].Origin.X:F2},{posture[0].Origin.Y:F2},{posture[0].Origin.Z:F2})");
            Console.WriteLine(FormattableString.Invariant($"[shape-pose] mt=0x{locomotionChartIdent:X8} parts={pieceTally} {summary}"));
        }
        return posture;
    }
}
