using MacAC.Dat;
using MacAC.Mechanics.Kinetics;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics;

internal static class SummonLocomotionInitializer
{
    internal readonly record struct Scheme(uint Style, uint Motion);

    public static AnimSequencer Create(
        RigSpec rig,
        MotionBook locomotionChart,
        IAnimReader fetcher,
        ObjectCreation.RemoteMotionState? wirePhase)
    {
        AnimSequencer scheduler = new AnimSequencer(rig, locomotionChart, fetcher);
        Scheme plan = LocatePlan(locomotionChart, wirePhase);

        scheduler.BootstrapPhase();
        scheduler.SetCycle(plan.Style, plan.Motion);

        scheduler.Manager.HandleEnterWorld();
        return scheduler;
    }

    public static void Reinitialize(
        AnimSequencer scheduler,
        MotionBook locomotionChart,
        ObjectCreation.RemoteMotionState? wirePhase)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(locomotionChart);
        Scheme plan = LocatePlan(locomotionChart, wirePhase);
        scheduler.Reset();
        ReinitializeRest(scheduler, plan);
    }

    private static void ReinitializeRest(AnimSequencer scheduler, Scheme plan)
    {
        scheduler.BootstrapPhase();
        scheduler.SetCycle(plan.Style, plan.Motion);
        scheduler.Manager.HandleEnterWorld();
    }

    internal static Scheme LocatePlan(
        MotionBook locomotionChart,
        ObjectCreation.RemoteMotionState? wirePhase)
    {
        uint styling = wirePhase is { Stance: > 0 } phase
            ? 0x80000000u | phase.Stance
            : (uint)locomotionChart.DefaultStyle;

        uint locomotion = LocomotionDirective.Ready;
        if (wirePhase?.ForwardCommand is ushort directive && directive > 0)
        {
            uint settled = MotionCommandLookup.ReconstructWholeCommand(directive);
            locomotion = settled is not 0
                ? settled
                : 0x40000000u | directive;
        }

        return new Scheme(styling, locomotion);
    }
}
