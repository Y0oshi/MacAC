using MacAC.Sim.Presence;
using MacAC.Sim.Realm;

namespace MacAC.Sim;

/// <summary>Frame counter and simulation clock; the clock only advances when the caller says so.</summary>
public interface ISimCoreClock
{
    ulong FrameNumber { get; }

    double SimulationMomentSecs { get; }
}

public readonly record struct SimFrameTime(ulong FrameNumber, double DeltaSeconds, double SimulationTimeSeconds);

public sealed class SimCoreClock : ISimCoreClock
{
    public ulong FrameNumber { get; private set; }

    public double SimulationMomentSecs { get; private set; }

    public SimFrameTime Advance(double hubDiffSecs, bool proceedSimulationMoment = true)
    {
        double diff = StandardizeDeltaSeconds(hubDiffSecs);
        FrameNumber = checked(FrameNumber + 1UL);
        if (proceedSimulationMoment)
            SimulationMomentSecs += diff;
        return new SimFrameTime(FrameNumber, diff, SimulationMomentSecs);
    }

    public static double StandardizeDeltaSeconds(double diffSecs)
    {
        return double.IsFinite(diffSecs) && diffSecs > 0.0 && diffSecs <= float.MaxValue ? diffSecs : 0.0;
    }
}

/// <summary>Everything a read-only observer can learn about the simulation in one frozen record.</summary>
public readonly record struct SimStateWaypoint(
    SimEpochTicket Generation,
    SimLifespanPhase Lifecycle,
    ulong FrameNumber,
    int EntityCount,
    int MaterializedEntityCount,
    int InventoryObjectCount,
    int InventoryContainerCount,
    SimStashStateCapture InventoryState,
    SimToonCapture Character,
    SimSocialCapture Social,
    long ChatRevision,
    int ChatCount,
    SimActionCapture Actions,
    SimLocomotionCapture Movement,
    SimRealmAmbienceCapture Environment,
    SimRealmAmbienceHoldingCapture EnvironmentOwnership,
    SimPortalCapture Portal,
    SimRealmCrossingHoldingCapture TransitOwnership,
    SimFellowsCapture Fellowship,
    SimAllegianceCapture Allegiance);

public interface ISimCoreLens
{
    SimEpochTicket Generation { get; }
    SimLifespanCapture Lifecycle { get; }
    ISimCoreClock Clock { get; }
    ISimActorLens Entities { get; }
    ISimStashLens Inventory { get; }
    ISimStashStateLens InventoryState { get; }
    ISimToonLens Character { get; }
    ISimSocialLens Social { get; }
    ISimCommsLens Chat { get; }
    ISimFellowsLens Fellowship { get; }
    ISimAllegianceLens Allegiance { get; }
    ISimActionLens Actions { get; }
    ISimLocomotionLens Movement { get; }
    ISimRealmAmbienceLens Environment { get; }
    ISimPortalLens Portal { get; }

    ISimToonPickLens CharacterSelection
    {
        get
        {
            throw new NotSupportedException("This runtime view doesn't project character selection");
        }
    }

    ISimLinkLens Connection => SimLinkLedger.Inactive;

    ISimToonGenesisLens CharacterCreation
    {
        get
        {
            throw new NotSupportedException("This runtime view doesn't project character creation");
        }
    }

    SimStateWaypoint GrabCheckpoint();
}
