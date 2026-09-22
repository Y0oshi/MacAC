using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class SimActorIndex
{
    public void RenewCapture(
        SimActorRecord capture,
        RealmSession.MoverSpawn approved,
        bool renewLocus = false)
    {
        DemandFollowed(capture);
        uint earlierChamber = capture.WholeChamberTag;
        capture.Snapshot = approved;
        capture.RenewDerivedPhase(renewLocus);
        if (capture.WholeChamberTag != earlierChamber)
        {
            FloodChamberToDescendants(
                capture,
                capture.WholeChamberTag,
                capture.CanonLbTag);
        }
    }

    public void ProgressBuildArbiter(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ProgressBuildArbiter();
    }

    public void ProgressLocusArbiter(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ProgressLocusArbiter();
    }

    public void ProgressVectorArbiter(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ProgressVectorArbiter();
    }

    public void ProgressTravelArbiter(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ProgressTravelArbiter();
    }

    public void ProgressTravelSeal(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ProgressTravelSeal();
    }

    public void ProgressStanceSeal(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ProgressStanceSeal();
    }

    public void ProgressAncestorSeal(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ProgressAncestorSeal();
    }

    public void ProgressObjRefDscArbiter(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ProgressObjRefDscArbiter();
    }

    public CanonKineticShift ImposeRawKineticsPhase(
        SimActorRecord capture,
        uint rawPhase)
    {
        DemandFollowed(capture);
        return capture.ImposeRawKineticsPhase(rawPhase);
    }

    public bool TryDequeuePhaseChangeover(
        SimActorRecord capture,
        out CanonKineticShift changeover)
    {
        DemandFollowed(capture);
        return capture.TryDequeuePhaseChangeover(out changeover);
    }

    public void AssignDescendantNoPaint(SimActorRecord capture, bool noPaint)
    {
        DemandFollowed(capture);
        capture.AssignDescendantNoPaint(noPaint);
    }

    public void SuspendObjectTimer(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.SuspendObjectTimer();
    }

    public void ReactivateObjectTimer(SimActorRecord capture)
    {
        DemandFollowed(capture);
        capture.ReactivateObjectTimer();
    }

    public void RestartObjectTimerForJoinRealm(SimActorRecord capture, bool isStatic)
    {
        DemandFollowed(capture);
        capture.RestartObjectTimerForJoinRealm(isStatic);
    }

    public void AssignWholeChamber(
        SimActorRecord capture,
        uint wholeChamberIdent,
        uint canonLbIdent)
    {
        DemandFollowed(capture);
        capture.AssignWholeChamber(wholeChamberIdent, canonLbIdent);
        FloodChamberToDescendants(capture, wholeChamberIdent, canonLbIdent);
    }

    private readonly Stack<SimActorRecord> _chamberFloodPile = new();

    public void AssignFinalKineticsPhase(
        SimActorRecord capture,
        KineticStateFlags phase)
    {
        DemandFollowed(capture);
        capture.AssignFinalKineticsPhase(phase);
    }

    public void AssignHasPieceArr(SimActorRecord capture, bool val)
    {
        DemandFollowed(capture);
        capture.HasPieceArray = val;
    }

    public void AssignKineticsCorpus(
        SimActorRecord capture,
        MacAC.Mechanics.Kinetics.KineticBody? corpus)
    {
        DemandFollowed(capture);
        capture.AssignKineticsCorpus(corpus);
    }

    public void AssignKineticsCorpusAcquisitionInHeadway(
        SimActorRecord capture,
        bool val)
    {
        DemandFollowed(capture);
        capture.KineticsCorpusAcquisitionInHeadway = val;
    }

    public void AssignDistantLocomotion(
        SimActorRecord capture,
        ISimPeerMotion? distant)
    {
        DemandFollowed(capture);
        capture.PeerMotion = distant;
    }

    public void AssignDistantLocomotionMappingInHeadway(
        SimActorRecord capture,
        bool val)
    {
        DemandFollowed(capture);
        capture.DistantLocomotionMappingInHeadway = val;
    }

    public void AssignMissile(
        SimActorRecord capture,
        ISimMissile? missile)
    {
        DemandFollowed(capture);
        capture.Projectile = missile;
    }

    public void AssignMissileMappingInHeadway(
        SimActorRecord capture,
        bool val)
    {
        DemandFollowed(capture);
        capture.MissileMappingInHeadway = val;
    }

    public void AssignRequiresDistantStanceCore(
        SimActorRecord capture,
        bool val)
    {
        DemandFollowed(capture);
        capture.RequiresDistantStanceCore = val;
    }

    public void AssignKineticsHub(
        SimActorRecord capture,
        MacAC.Mechanics.Kinetics.Gait.IKineticObjHost? hub)
    {
        DemandFollowed(capture);
        capture.PhysicsHost = hub;
    }

    public void AssignEraseApprovedForTeardown(
        SimActorRecord capture,
        bool val)
    {
        DemandFollowed(capture);
        capture.EraseApprovedForTeardown = val;
    }

    internal bool HaltMissileFollowingImpact(
        SimActorRecord capture,
        bool demandLatestMissile)
    {
        DemandFollowed(capture);
        return capture.HaltMissileFollowingImpact(demandLatestMissile);
    }

    private void FloodChamberToDescendants(
        SimActorRecord trunk,
        uint wholeChamberIdent,
        uint canonLbIdent)
    {
        _chamberFloodPile.Clear();
        _chamberFloodPile.Push(trunk);
        while (_chamberFloodPile.Count > 0)
        {
            var latest = _chamberFloodPile.Pop();
            var descendants = AncestorAttachments.DescendantsAffixedToAncestor(
                latest.ServerGuid,
                latest.Incarnation);
            for (int idx = 0; idx < descendants.Count; ++idx)
            {
                if (!TryFetchEngaged(descendants[idx], out SimActorRecord descendant)
                    || (descendant.WholeChamberTag == wholeChamberIdent
                        && descendant.CanonLbTag == canonLbIdent))

                    continue;
                if (KineticTelemetry.ProbeChildCellEnabled)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[child-cell] parent=0x{latest.ServerGuid:X8} child=0x{descendant.ServerGuid:X8} old=0x{descendant.WholeChamberTag:X8} new=0x{wholeChamberIdent:X8} cause={(wholeChamberIdent is 0u ? "withdraw" : "propagate")}"));
                }
                descendant.AssignWholeChamber(wholeChamberIdent, canonLbIdent);
                _chamberFloodPile.Push(descendant);
            }
        }
    }
}
