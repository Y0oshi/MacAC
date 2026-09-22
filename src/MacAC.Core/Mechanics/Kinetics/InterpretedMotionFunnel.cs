namespace MacAC.Mechanics.Kinetics;

/// <summary>Where decoded network motions are delivered.</summary>
public interface IDecodedMotionTap
{
    bool EnactLocomotion(uint locomotion, float pace);

    bool HaltLocomotion(uint locomotion);

    bool StopCompletely() => true;
}

/// <summary>One action verb from an inbound movement update.</summary>
public readonly record struct InboundMotionVerb(uint Command, int Stamp, bool Autonomous, float Speed);

/// <summary>The interpreted-motion block of an inbound movement update, before it is applied.</summary>
public struct InboundDecodedState
{
    private const uint NonFightingStance = 0x8000003Du;
    private const uint PrimedDirective = 0x41000003u;

    public uint CurrentStyling;
    public uint ForwardCommand;
    public float ForwardSpeed;
    public uint FlankStepCommand;
    public float FlankStepSpeed;
    public uint PivotCommand;
    public float PivotSpeed;
    public IReadOnlyList<InboundMotionVerb>? Actions;

    /// <summary>Standing still in the non-combat stance at unit speed.</summary>
    public static InboundDecodedState Default()
    {
        return new()
        {
            CurrentStyling = NonFightingStance,
            ForwardCommand = PrimedDirective,
            ForwardSpeed = 1.0f,
            FlankStepSpeed = 1.0f,
            PivotSpeed = 1.0f,
        };
    }
}
