using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class SimActorIndex
{
    public IncomingBuildOutcome AdmitBuild(RealmSession.MoverSpawn incoming) =>
        _incoming.AllowBuild(incoming);

    public SpawnStampVerdict PreviewCreateDisposition(
        RealmSession.MoverSpawn incoming) =>
        _incoming.PreviewBuildDisposition(incoming);

    public bool TryDelete(
        MacAC.Wire.Messages.ObjectDeletion.Parsed erase,
        bool isOwnAvatar) =>
        _incoming.TryErase(erase, isOwnAvatar);

    public bool TryApplyObjRefDesc(
        MacAC.Wire.Messages.ObjDescNotice.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.TryEnactObjDesc(refresh, out approved);

    public bool TryApplyLift(
        MacAC.Wire.Messages.PickupNotice.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.TryEnactPickup(refresh, out approved);

    public bool TryApplyBuildParent(
        CreateAnchorUpdate refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.TryEnactCreateParent(refresh, out approved);

    public bool TryApplyAncestor(
        MacAC.Wire.Messages.AncestorSignal.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.TryEnactParent(refresh, out approved);

    public bool TrySealParent(
        uint descendantOid,
        uint ancestorOid,
        uint ancestorLocale,
        uint stanceIdent,
        ushort locusSeries,
        out RealmSession.MoverSpawn approved)
    {
        return _incoming.TrySealAncestor(
            descendantOid,
            ancestorOid,
            ancestorLocale,
            stanceIdent,
            locusSeries,
            out approved);
    }

    public bool TryApplyLocomotion(
        RealmSession.MoverMotionUpdate refresh,
        bool retainCargo,
        out RealmSession.MoverSpawn approved,
        out GrantedKineticsTimestamps timestamps)
    {
        return _incoming.TryEnactMotion(refresh, retainCargo, out approved, out timestamps);
    }

    public bool TryApplyVector(
        MacAC.Wire.Messages.VelocityUpdate.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.TryApplyVector(refresh, out approved);

    public bool TryEnactCondition(
        MacAC.Wire.Messages.GroupPhase.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.TryEnactPhase(refresh, out approved);

    public bool TryEnactPosition(
        RealmSession.MoverPositionUpdate refresh,
        bool isOwnAvatar,
        System.Numerics.Quaternion? forceLocusSpin,
        System.Numerics.Vector3? latestOwnVel,
        out PoseStampVerdict disposition,
        out RealmSession.MoverSpawn approved,
        out GrantedKineticsTimestamps timestamps)
    {
        return _incoming.TryEnactPlace(
            refresh,
            isOwnAvatar,
            forceLocusSpin,
            latestOwnVel,
            out disposition,
            out approved,
            out timestamps);
    }

    public bool IsFreshTeleportStart(uint oid, ushort warpSeries) =>
        _incoming.IsFreshTeleportBegin(oid, warpSeries);

    internal IncomingBuildOutcome AdmitBuildPostponedSameGen(
        RealmSession.MoverSpawn incoming) =>
        _incoming.AdmitBuildPostponedSameGen(incoming);

    internal bool TryAdmitPostponedLocus(
        RealmSession.MoverPositionUpdate refresh,
        bool isOwnAvatar,
        out PoseStampVerdict disposition,
        out GrantedKineticsTimestamps timestamps,
        out bool hasStampAlteration)
    {
        return _incoming.TryAdmitPostponedLocus(
            refresh,
            isOwnAvatar,
            out disposition,
            out timestamps,
            out hasStampAlteration);
    }

    internal bool TryAdmitPostponedObjRefDsc(
        ObjDescNotice.Parsed refresh,
        out GrantedKineticsTimestamps timestamps) =>
        _incoming.TryAdmitPostponedObjRefDsc(refresh, out timestamps);

    internal bool TryAdmitPostponedLift(
        PickupNotice.Parsed refresh,
        out GrantedKineticsTimestamps timestamps) =>
        _incoming.TryAdmitPostponedLift(refresh, out timestamps);

    internal bool TryAdmitPostponedBuildAncestor(
        CreateAnchorUpdate refresh,
        out GrantedKineticsTimestamps timestamps) =>
        _incoming.TryAdmitPostponedBuildAncestor(refresh, out timestamps);

    internal bool TryAdmitPostponedAncestor(
        AncestorSignal.Parsed refresh,
        out GrantedKineticsTimestamps timestamps) =>
        _incoming.TryAdmitPostponedAncestor(refresh, out timestamps);

    internal bool TryAdmitPostponedLocomotion(
        RealmSession.MoverMotionUpdate refresh,
        out GrantedKineticsTimestamps timestamps,
        out bool hasStampAlteration)
    {
        return _incoming.TryAdmitPostponedLocomotion(
            refresh,
            out timestamps,
            out hasStampAlteration);
    }

    internal bool TryAdmitPostponedPhase(
        GroupPhase.Parsed refresh,
        out GrantedKineticsTimestamps timestamps) =>
        _incoming.TryAdmitPostponedPhase(refresh, out timestamps);

    internal bool TryAdmitPostponedVector(
        VelocityUpdate.Parsed refresh,
        out GrantedKineticsTimestamps timestamps) =>
        _incoming.TryAdmitPostponedVector(refresh, out timestamps);

    internal bool ImposeApprovedObjRefDscCapture(
        uint oid,
        ObjDescNotice.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.ImposeApprovedObjRefDscCapture(oid, refresh, out approved);

    internal bool ImposeApprovedLiftCapture(
        uint oid,
        PickupNotice.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.ImposeApprovedLiftCapture(oid, refresh, out approved);

    internal bool ImposeApprovedBuildAncestorCapture(
        uint oid,
        CreateAnchorUpdate refresh,
        out RealmSession.MoverSpawn approved)
    {
        return _incoming.ImposeApprovedBuildAncestorCapture(oid, refresh, out approved);
    }

    internal bool ImposeApprovedAncestorCapture(
        uint oid,
        AncestorSignal.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.ImposeApprovedAncestorCapture(oid, refresh, out approved);

    internal bool ImposeApprovedLocomotionCapture(
        uint oid,
        ushort travelSeries,
        ushort approvedSrvControlledRelocate,
        RealmSession.MoverMotionUpdate refresh,
        bool retainCargo,
        out RealmSession.MoverSpawn approved)
    {
        return _incoming.ImposeApprovedLocomotionCapture(
            oid,
            travelSeries,
            approvedSrvControlledRelocate,
            refresh,
            retainCargo,
            out approved);
    }

    internal bool ImposeApprovedPhaseCapture(
        uint oid,
        GroupPhase.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.ImposeApprovedPhaseCapture(oid, refresh, out approved);

    internal bool ImposeApprovedVectorCapture(
        uint oid,
        VelocityUpdate.Parsed refresh,
        out RealmSession.MoverSpawn approved) =>
        _incoming.ImposeApprovedVectorCapture(oid, refresh, out approved);

    internal bool ImposeApprovedLocusCapture(
        uint oid,
        RealmSession.MoverPositionUpdate refresh,
        PoseStampVerdict disposition,
        GrantedKineticsTimestamps timestamps,
        bool isOwnAvatar,
        System.Numerics.Quaternion? forceLocusSpin,
        System.Numerics.Vector3? latestOwnVel,
        bool installStanceCycle,
        bool wipeAncestor,
        out RealmSession.MoverSpawn approved)
    {
        return _incoming.ImposeApprovedLocusCapture(
            oid,
            refresh,
            disposition,
            timestamps,
            isOwnAvatar,
            forceLocusSpin,
            latestOwnVel,
            installStanceCycle,
            wipeAncestor,
            out approved);
    }

    internal bool ImposeApprovedLocusExecutionRejectedCapture(
        uint oid,
        ushort approvedLocusSeries,
        GrantedKineticsTimestamps timestamps,
        out RealmSession.MoverSpawn approved)
    {
        return _incoming.ImposeApprovedLocusExecutionRejectedCapture(
            oid,
            approvedLocusSeries,
            timestamps,
            out approved);
    }

    internal bool ImposeApprovedWeenieBlurbCapture(
        uint oid,
        RealmSession.MoverSpawn incoming,
        out RealmSession.MoverSpawn merged) =>
        _incoming.ImposeApprovedWeenieBlurbCapture(oid, incoming, out merged);

    internal bool TryRenewObjectBlurbFlagSet(
        uint oid,
        uint bitfield,
        out RealmSession.MoverSpawn merged) =>
        _incoming.TryRenewObjectBlurbFlagSet(oid, bitfield, out merged);
}
