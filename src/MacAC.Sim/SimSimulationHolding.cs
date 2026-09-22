using MacAC.Sim.Actors;
using MacAC.Sim.Kinetics;
using MacAC.Sim.Play;

namespace MacAC.Sim;

/// <summary>The three ownership counters that must all read zero before a session may be torn down.</summary>
public readonly record struct SimSimulationHoldingCapture(
    SimActorObjectHoldingCapture EntityObjects,
    SimKineticsHoldingCapture Physics,
    SimGameplayHoldingCapture Gameplay)
{
    public bool IsConverged
    {
        get
        {
            return EntityObjects.IsConverged && Physics.IsConverged && Gameplay.IsConverged;
        }
    }
}

public static class SimSimulationHolding
{
    public static SimSimulationHoldingCapture Capture(
        SimActorObjectLifetime actorObjects,
        SimStashLedger satchel,
        SimToonLedger toon,
        SimCommsLedger communication,
        SimActionLedger acts,
        SimAvatarLocomotionLedger travel,
        SimFellowsLedger fellowship,
        SimAllegianceLedger allegiance,
        SimBarterLedger barter)
    {
        ArgumentNullException.ThrowIfNull(actorObjects);
        var play = SimGameplayHolding.Capture(
            satchel, toon, communication, acts, travel, fellowship, allegiance, barter);
        return new SimSimulationHoldingCapture(
            actorObjects.GrabOwnership(),
            actorObjects.Physics.CaptureOwnership(),
            play);
    }
}
