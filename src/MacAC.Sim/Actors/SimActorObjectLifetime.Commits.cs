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
    public Exception? RetireCanonSole(SimActorRecord canon)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        try
        {
            Entities.HoldTeardown(canon);
            try
            {
                CompleteProjectionRetirement(canon);
            }
            finally
            {
                Entities.RelinquishTeardown(canon);
            }
            return null;
        }
        catch (Exception problem)
        {
            return problem;
        }
    }

    public bool SealRebucket(
        SimActorRecord canon,
        uint wholeChamberIdent,
        uint canonLbIdent,
        Action<SimActorRecord>? acknowledgeProj = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        if (!Entities.IsCurrent(canon))
            return false;

        uint earlier = canon.WholeChamberTag;
        Entities.AssignWholeChamber(
            canon,
            wholeChamberIdent,
            canonLbIdent);
        ulong spatialVer = canon.SpatialAuthorityVersion;
        if (earlier == wholeChamberIdent)
        {
            acknowledgeProj?.Invoke(canon);
            return SameCanon(
                canon,
                () => canon.SpatialAuthorityVersion
                    == spatialVer);
        }

        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Rebucketed,
            () => canon.SpatialAuthorityVersion == spatialVer);
    }

    public bool SealWireChamberRebucket(
        SimActorRecord canon,
        uint spatialChamberOrLbIdent,
        Action<SimActorRecord>? acknowledgeProj = null)
    {
        ArgumentNullException.ThrowIfNull(canon);
        uint sealedWholeChamber =
            (spatialChamberOrLbIdent & 0xFFFFu) != 0xFFFFu
                ? spatialChamberOrLbIdent
                : canon.WholeChamberTag;
        uint sealedLb = spatialChamberOrLbIdent is 0
            ? 0u
            : (spatialChamberOrLbIdent & 0xFFFF0000u) | 0xFFFFu;
        return SealRebucket(
            canon,
            sealedWholeChamber,
            sealedLb,
            acknowledgeProj);
    }

    public bool SealWithdrawal(
        SimActorRecord canon,
        Action<SimActorRecord>? acknowledgeProj = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        if (!Entities.IsCurrent(canon))
            return false;

        var startingAbort =
            DiscardTenancy(canon);
        Physics.ImpactDossiers.ExitRealm(canon);
        var plainAbort =
            Physics.SetPosition.Drop(canon);
        var abort =
            StrongerCancel(startingAbort, plainAbort);
        Entities.SuspendObjectTimer(canon);
        Entities.AssignWholeChamber(canon, 0u, 0u);
        ulong spatialVer = canon.SpatialAuthorityVersion;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Withdrawn,
            () => canon.SpatialAuthorityVersion == spatialVer,
            abort);
    }

    public bool SealDescendantNoPaint(
        SimActorRecord canon,
        bool noPaint,
        Action<SimActorRecord>? acknowledgeProj = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        if (!Entities.IsCurrent(canon))
            return false;

        Entities.AssignDescendantNoPaint(canon, noPaint);
        ulong kineticsAlterationVer =
            canon.KineticsPhaseAlterationVer;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Updated,
            () => canon.KineticsPhaseAlterationVer
                == kineticsAlterationVer);
    }

    public bool RetireFollowingProjAcquisitionMiss(
        SimActorRecord canon)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        if (!Entities.RemoveActive(canon))
            return false;

        Entities.ProgressLifespanAlteration(canon.ServerGuid);
        Entities.HoldTeardown(canon);
        PublishEntity(SimActorChange.Deleted, canon);
        return true;
    }

    internal void CompleteProjectionRetirement(
        SimActorRecord canon)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        var startingAbort =
            DiscardTenancy(canon);
        var plainAbort =
            Physics.SetPosition.Drop(
                canon,
                freeReadiedCarrier: true);
        var abort =
            StrongerCancel(startingAbort, plainAbort);
        Physics.ImpactDossiers.Drop(canon);
        Physics.DropSpatialProj(canon);
        Entities.AssignDistantLocomotion(canon, null);
        Entities.AssignDistantLocomotionMappingInHeadway(canon, false);
        Entities.AssignMissile(canon, null);
        Entities.AssignMissileMappingInHeadway(canon, false);
        Entities.AssignRequiresDistantStanceCore(canon, false);
        Entities.AssignKineticsHub(canon, null);
        Entities.AssignKineticsCorpus(canon, null);
        Entities.AssignKineticsCorpusAcquisitionInHeadway(canon, false);
        Entities.AssignHasPieceArr(canon, false);
        Entities.FreeOwnIdent(canon);
        Physics.SetPosition.BroadcastAbort(abort);
    }

    private bool SealLocusLane(
        bool imposed,
        uint oid,
        RealmSession.MoverSpawn approved,
        Action<SimActorRecord>? acknowledgeProj)
    {
        if (!imposed
            || !Entities.TryFetchEngaged(
                oid,
                out SimActorRecord canon))

            return imposed;

        Entities.RenewCapture(canon, approved);
        var startingAbort =
            DiscardTenancy(canon);
        Entities.ProgressLocusArbiter(canon);
        Physics.ImpactDossiers.ExitRealm(canon);
        var plainAbort =
            Physics.SetPosition.Drop(canon);
        var abort =
            StrongerCancel(startingAbort, plainAbort);
        ulong locusVer = canon.PositionAuthorityVersion;
        ulong spatialVer = canon.SpatialAuthorityVersion;
        return AckProjThenBroadcast(
            canon,
            () => acknowledgeProj?.Invoke(canon),
            SimActorChange.Updated,
            () => canon.PositionAuthorityVersion == locusVer
                && canon.SpatialAuthorityVersion == spatialVer,
            abort);
    }

    private void PublishEntity(
        SimActorChange edit,
        SimActorRecord canon) =>
        Events.PublishEntity(edit, canon);

    private bool AckProjThenBroadcast(
        SimActorRecord canon,
        Action acknowledgeProj,
        SimActorChange edit,
        Func<bool> fitsSealedAlteration,
        SimPlacementAbortStub abort = default)
    {
        Physics.SetPosition.BroadcastAbort(abort);
        if (!SameCanon(canon, fitsSealedAlteration))
            return false;
        try
        {
            acknowledgeProj();
        }
        finally
        {
            if (SameCanon(
                    canon,
                    fitsSealedAlteration))

                PublishEntity(edit, canon);
        }

        return SameCanon(
            canon,
            fitsSealedAlteration);
    }

    private bool SameCanon(
        SimActorRecord canon,
        Func<bool> fitsSealedAlteration)
    {
        return Entities.IsCurrent(canon)
        && fitsSealedAlteration();
    }

    private static bool Finite(System.Numerics.Vector3 val)
    {
        return float.IsFinite(val.X)
        && float.IsFinite(val.Y)
        && float.IsFinite(val.Z);
    }
}
