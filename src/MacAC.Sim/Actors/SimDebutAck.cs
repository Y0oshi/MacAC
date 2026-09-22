using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal static class SimDebutAck
{
    internal static bool IsStillQueued(
        SimSpawnTenancyLedger residences,
        SimSetPositionLedger setLocus,
        SimActorRecord capture,
        in SimSpawnTenancyTicket residenceTicket,
        in SimPlacementMirrorTicket anticipated)
    {
        if (!residences.TryFetchLatest(capture, out SimSpawnTenancyLease tenancy) || tenancy.Token != residenceTicket)
            return false;

        bool superseded = setLocus.TryGlimpseProj(out SimPlacementMirrorCapture front)
            && front.Token.Entity == anticipated.Entity
            && (front.Kind is not SimPlacementMirrorKind.Place || front.Token != anticipated);
        return !superseded;
    }
}
