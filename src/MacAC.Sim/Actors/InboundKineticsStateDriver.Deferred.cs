using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class InboundKineticsStateDriver
{
    internal bool TryAdmitPostponedLocus(
        RealmSession.MoverPositionUpdate refresh,
        bool isOwnAvatar,
        out PoseStampVerdict disposition,
        out GrantedKineticsTimestamps timestamps,
        out bool hasStampAlteration)
    {
        if (!Consult(
                refresh.Guid,
                out KineticStampGate? latch,
                out RealmSession.MoverSpawn former))
        {
            disposition = PoseStampVerdict.Rejected;
            timestamps = default;
            hasStampAlteration = false;
            return false;
        }

        ushort earlierLocus = latch.LocusStamp;
        ushort earlierWarp = latch.WarpStamp;
        ushort earlierForceLocus = latch.ForceLocusStamp;
        bool advancesWarp = KineticStampGate.IsNewer(
            earlierWarp,
            refresh.TeleportSequence);
        disposition = latch.TryAdmitLocusSignal(
            refresh.InstanceSequence,
            refresh.PositionSequence,
            refresh.TeleportSequence,
            refresh.ForcePositionSequence,
            isOwnAvatar);
        timestamps = GrantedStamps(
            latch,
            warpAdvanced: disposition is PoseStampVerdict.Apply
                && advancesWarp,
            earlierWarp: earlierWarp);
        hasStampAlteration = earlierLocus != latch.LocusStamp
            || earlierWarp != latch.WarpStamp
            || earlierForceLocus != latch.ForceLocusStamp;
        return true;
    }

    internal bool TryAdmitPostponedObjRefDsc(
        ObjDescNotice.Parsed refresh,
        out GrantedKineticsTimestamps timestamps)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out _)
            || !latch.TryAdmitObjRefDscSignal(
                refresh.InstanceSequence,
                refresh.ObjDescSequence))
        {
            timestamps = default;
            return false;
        }
        timestamps = GrantedStamps(latch);
        return true;
    }

    internal bool TryAdmitPostponedLift(
        PickupNotice.Parsed refresh,
        out GrantedKineticsTimestamps timestamps)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out _)
            || !latch.TryAdmitLocusLaneSignal(
                refresh.InstanceSequence,
                refresh.PositionSequence))
        {
            timestamps = default;
            return false;
        }
        timestamps = GrantedStamps(latch);
        return true;
    }

    internal bool TryAdmitPostponedBuildAncestor(
        CreateAnchorUpdate refresh,
        out GrantedKineticsTimestamps timestamps)
    {
        if (!Consult(refresh.ChildGuid, out KineticStampGate? latch, out _)
            || !latch.TryAdmitLocusLaneSignal(
                refresh.ChildInstanceSequence,
                refresh.ChildPositionSequence))
        {
            timestamps = default;
            return false;
        }
        timestamps = GrantedStamps(latch);
        return true;
    }

    internal bool TryAdmitPostponedAncestor(
        AncestorSignal.Parsed refresh,
        out GrantedKineticsTimestamps timestamps)
    {
        if (!_stampLatches.TryGetValue(
                refresh.ParentGuid,
                out KineticStampGate? ancestorLatch)
            || !ancestorLatch.IsLatestInst(refresh.ParentInstanceSequence)
            || !Consult(
                refresh.ChildGuid,
                out KineticStampGate? descendantLatch,
                out _)
            || !descendantLatch.TryAdmitLocusLaneSignal(
                descendantLatch.InstStamp,
                refresh.ChildPositionSequence))
        {
            timestamps = default;
            return false;
        }
        timestamps = GrantedStamps(descendantLatch);
        return true;
    }

    internal bool TryAdmitPostponedLocomotion(
        RealmSession.MoverMotionUpdate refresh,
        out GrantedKineticsTimestamps timestamps,
        out bool hasStampAlteration)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out _))
        {
            timestamps = default;
            hasStampAlteration = false;
            return false;
        }

        ushort earlierTravel = latch.TravelStamp;
        ushort earlierSrvControl = latch.SrvControlledRelocateStamp;
        bool approved = latch.TryAdmitTravelSignal(
            refresh.InstanceSequence,
            refresh.MovementSequence,
            refresh.ServerControlSequence);
        timestamps = GrantedStamps(latch);
        hasStampAlteration = earlierTravel != latch.TravelStamp
            || earlierSrvControl != latch.SrvControlledRelocateStamp;
        return approved;
    }

    internal bool TryAdmitPostponedPhase(
        GroupPhase.Parsed refresh,
        out GrantedKineticsTimestamps timestamps)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out _)
            || !latch.TryAdmitPhaseSignal(
                refresh.InstanceSequence,
                refresh.StateSequence))
        {
            timestamps = default;
            return false;
        }
        timestamps = GrantedStamps(latch);
        return true;
    }

    internal bool TryAdmitPostponedVector(
        VelocityUpdate.Parsed refresh,
        out GrantedKineticsTimestamps timestamps)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out _)
            || !latch.TryAdmitVectorSignal(
                refresh.InstanceSequence,
                refresh.VectorSequence))
        {
            timestamps = default;
            return false;
        }
        timestamps = GrantedStamps(latch);
        return true;
    }
}
