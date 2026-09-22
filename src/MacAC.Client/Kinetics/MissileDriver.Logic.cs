using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Wire.Messages;

namespace MacAC.Client.Kinetics;

internal sealed partial class MissileDriver
{
    internal Action<string>? DiagnosticSink { get; set; }

    internal int Count
    {
        get
        {
            return _onlineActors.Records.Count(
        static capture => capture.ProjectileRuntime is not null);
        }
    }

    internal bool HndsTravel(uint srvOid)
    {
        return TryFetchLatest(srvOid, out OnlineActorRecord capture, out _)
        && ((capture.FinalKineticsPhase & KineticStateFlags.Missile) != 0
            || capture.RemoteMotionRuntime is null);
    }

    internal bool ExitRealm(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.ProjectileRuntime is not SimMissile core
            || capture.WorldEntity is not { } actor)
            return false;
        SuspendBeyondRealm(core, actor.Id);
        return true;
    }

    internal bool CanAdmitVectorCargo(
        uint srvOid,
        Vector3 vel,
        Vector3 angularVel)
    {
        bool valid = IsFinite(vel) && IsFinite(angularVel);
        if (!valid)
            DiagnosticSink?.Invoke(
                $"Rejected invalid VelocityUpdate for live object 0x{srvOid:X8}.");
        return valid;
    }

    internal bool CanAdmitLocusCargo(
        uint srvOid,
        ObjectCreation.RemotePosition locus,
        Vector3? vel)
    {
        Vector3 origin = new Vector3(
            locus.PositionX,
            locus.PositionY,
            locus.PositionZ);
        Quaternion facing = new Quaternion(
            locus.RotationX,
            locus.RotationY,
            locus.RotationZ,
            locus.RotationW);
        bool valid = IsFinite(origin)
            && PoseValidation.IsValid(
                locus.LandblockId,
                origin,
                facing)
            && (vel is null || IsFinite(vel.Value));
        if (!valid)
            DiagnosticSink?.Invoke(
                $"Rejected invalid PositionUpdate for live object 0x{srvOid:X8}.");
        return valid;
    }

    internal bool SynchronizeExhibitFromSettledCorpus(
        OnlineActorRecord anticipatedCapture,
        double latestMoment)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (!TryFetchLatest(
                anticipatedCapture.ServerOid,
                out OnlineActorRecord capture,
                out SimMissile core)
            || !ReferenceEquals(capture, anticipatedCapture)
            || capture.WorldEntity is not { } actor)

            return false;

        if (double.IsFinite(latestMoment))
            _previousFinitePlayMoment = latestMoment;

        actor.SetPosition(core.Body.Position);
        actor.Rotation = core.Body.Orientation;
        actor.ParentCellId = capture.WholeChamberIdent;
        _rootPoses?.RenewTrunk(actor);
        return true;
    }

    internal bool ProgressQuantum(
        OnlineActorRecord capture,
        float quantum,
        int onlineMiddleX,
        int onlineMiddleY)
    {
        return !TryCommenceQuantum(capture, quantum, out QuantumHop hop) ? false : ConcludeQuantum(hop, onlineMiddleX, onlineMiddleY);
    }

    internal bool ConcludeQuantum(
        in QuantumHop hop,
        int onlineMiddleX,
        int onlineMiddleY)
    {
        var latest = hop.Record;
        RealmActor actor = hop.Entity;
        if (!IsLatestQuantumPersona(hop))
            return false;
        QuantumHop preciseHop = hop;

        return _runtimeUpdater.Complete(
            preciseHop.RuntimeCommit,
            onlineMiddleX,
            onlineMiddleY,
            capture =>
            {
                if (!IsLatestQuantumPersona(preciseHop))
                    return false;
                actor.SetPosition(capture.Position);
                actor.Rotation = capture.Orientation;
                actor.ParentCellId = capture.FullCellId;
                if (HasShownChamber(latest))
                    _rootPoses?.RenewTrunk(actor);
                return IsLatestQuantumPersona(preciseHop);
            });
    }

    private void SuspendBeyondRealm(SimMissile core, uint ownActorIdent)
    {
        core.Body.InWorld = false;
        Disengage(core.Body);
        _shades.Suspend(ownActorIdent);
    }

    private bool IsLatestQuantumPersona(in QuantumHop hop)
    {
        return TryFetchLatest(
            hop.Record.ServerOid,
            out OnlineActorRecord latest,
            out SimMissile core)
        && ReferenceEquals(latest, hop.Record)
        && ReferenceEquals(latest.WorldEntity, hop.Entity)
        && ReferenceEquals(core, hop.RuntimeCommit.Projectile)
        && core.PredictionArbiterVer
            == hop.RuntimeCommit.PredictionArbiterVer;
    }

    private static bool IsConcealed(OnlineActorRecord capture) =>
        (capture.FinalKineticsPhase & KineticStateFlags.Hidden) != 0;

    private static bool IsFinite(Vector3 val)
    {
        return float.IsFinite(val.X)
        && float.IsFinite(val.Y)
        && float.IsFinite(val.Z);
    }

    private void OnProjVisAltered(OnlineActorRecord capture, bool shown)
    {
        if (capture.WorldEntity is not { } actor
            || !_onlineActors.TryFetchRecord(capture.ServerOid, out OnlineActorRecord latest)
            || !ReferenceEquals(latest, capture))

            return;

        if (shown
            && capture.ProjectileRuntime is null
            && capture.KineticBody is not null
            && (capture.FinalKineticsPhase & KineticStateFlags.Missile) != 0)
        {
            RigSpec? rig = _setupResolver?.Resolve(
                actor.SrcGfxObjRefOrRigIdent);
            if (rig is not null)
            {
                int onlineMiddleX = _origin?.CenterX ?? 0;
                int onlineMiddleY = _origin?.CenterY ?? 0;
                _ = TryAttach(
                    capture,
                    rig,
                    _previousFinitePlayMoment,
                    onlineMiddleX,
                    onlineMiddleY);
            }
        }

        if (capture.ProjectileRuntime is not SimMissile core
            || !ReferenceEquals(latest.ProjectileRuntime, core))

            return;

        if (shown)
        {
            core.Body.State = capture.FinalKineticsPhase;
            core.Body.InWorld = true;
            if (IsConcealed(capture))
            {
                _shades.Suspend(actor.Id);
                return;
            }

            int onlineMiddleX = _origin?.CenterX ?? 0;
            int onlineMiddleY = _origin?.CenterY ?? 0;
            ProxyPositionSynchronizer.Sync(
                _shades,
                actor.Id,
                core.Body.Position,
                core.Body.Orientation,
                capture.WholeChamberIdent,
                onlineMiddleX,
                onlineMiddleY);
            _rootPoses?.RenewTrunk(actor);
            return;
        }

        SuspendBeyondRealm(core, actor.Id);
    }

    private static bool HasShownChamber(OnlineActorRecord capture)
    {
        return capture.IsSpatiallyProjected
        && capture.IsSpatiallyVisible
        && capture.WholeChamberIdent is not 0;
    }

    private static void Disengage(KineticBody corpus) =>
        corpus.TransientState &= ~TransientPhaseFlagSet.Active;

    private static float StandardizeFriction(float? val)
    {
        return val is >= 0f and <= 1f && float.IsFinite(val.Value)
            ? val.Value
            : KineticBody.DefaultFriction;
    }

    private static float StandardizeElasticity(float val) => float.IsNaN(val) || val <= 0f ? 0f : MathF.Min(val, 0.1f);
}
