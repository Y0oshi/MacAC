namespace MacAC.Mechanics.Kinetics.Gait;

/// <summary>Routes decoded network motions straight into an <see cref="AnimSequencer"/>.</summary>
public sealed class MotionTableDispatchTap(AnimSequencer sequencer) : IDecodedMotionTap
{
    private readonly AnimSequencer _scheduler = sequencer ?? throw new ArgumentNullException(nameof(sequencer));

    public bool EnactLocomotion(uint locomotion, float pace) => Perform(MotionTableGait.Interpreted(locomotion, pace));

    public bool HaltLocomotion(uint locomotion) => Perform(MotionTableGait.HaltInterpreted(locomotion, 1f));

    public bool StopCompletely() => Perform(MotionTableGait.StopCompletely());

    private bool Perform(in MotionTableGait gait) => _scheduler.PerformTravel(gait) == MotionTableKeeperError.Success;
}
