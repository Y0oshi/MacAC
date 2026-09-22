using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Play;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class SimActorObjectLifetime
{

    public bool TryTranslateStartingResidenceToCellessCourse(
        SimActorRecord canon)
    {
        Live();
        return StartingBuildResidences.TryTranslateToCellessCourse(canon);
    }
    internal bool TryFetchStartingBuildResidence(
        SimActorRecord canon,
        out SimSpawnTenancyLease tenancy)
    {
        Live();
        return StartingBuildResidences.TryFetchLatest(canon, out tenancy);
    }

    internal SimSpawnTenancyFinishStatus ConcludeStartingBuildResidence(
            SimActorRecord canon,
            in SimSpawnTenancyTicket ticket,
            out SimSpawnTenancyStub receipt)
    {
        Live();
        return StartingBuildResidences.Complete(
            canon,
            ticket,
            out receipt);
    }

    internal bool AcknowledgeStartingBuildResidenceAdoption(
        SimActorRecord canon,
        in SimSpawnTenancyAdoptionTicket ticket)
    {
        Live();
        return StartingBuildResidences.AcknowledgeAdoption(
            canon,
            ticket);
    }

    private SimPlacementAbortStub DiscardTenancy(
        SimActorRecord canon)
    {
        bool forgotten = StartingBuildResidences.Drop(
            canon,
            out _,
            out SimPlacementAbortStub abort);
        if (canon.Key is { } tag)
            StartingBuildExecution.TossHeadway(tag);
        return forgotten ? abort : default;
    }

    private static SimPlacementAbortStub StrongerCancel(
        in SimPlacementAbortStub starting,
        in SimPlacementAbortStub plain) =>
        starting.IsValid ? starting : plain;
}
