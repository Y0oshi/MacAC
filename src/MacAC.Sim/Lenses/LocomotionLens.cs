using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Play;

namespace MacAC.Sim;

public readonly record struct SimLocomotionCapture(
    bool HasController,
    uint LocalEntityId,
    Locus Position,
    Vector3 Velocity,
    bool IsAirborne,
    double SimulationTimeSeconds,
    long Revision = 0,
    bool AutoRunActive = false,
    bool HasCommandInput = false,
    LocomotionInput CommandInput = default);

public interface ISimLocomotionLens
{
    SimLocomotionCapture Snapshot { get; }

    bool IsStandingStill { get; }

    JumpChargeCapture LeapCharge { get; }
}
