using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Kinetics;

internal sealed partial class MissileDriver
{
    internal bool ImposeAuthoritativeVector(
        OnlineActorRecord anticipatedCapture,
        Vector3 vel,
        Vector3 angularVel,
        double latestMoment)
    {
        return ImposeAuthoritativeVector(
            anticipatedCapture,
            anticipatedCapture.VectorArbiterVer,
            anticipatedCapture.VelArbiterVer,
            vel,
            angularVel,
            latestMoment);
    }

    internal bool ImposeAuthoritativeVector(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedVectorArbiterVer,
        ulong anticipatedVelArbiterVer,
        Vector3 vel,
        Vector3 angularVel,
        double latestMoment)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        uint srvOid = anticipatedCapture.ServerOid;
        if (!TryFetchLatest(srvOid, out OnlineActorRecord capture, out SimMissile core))
            return false;
        if (!ReferenceEquals(capture, anticipatedCapture)
            || capture.VectorArbiterVer != anticipatedVectorArbiterVer
            || capture.VelArbiterVer != anticipatedVelArbiterVer)
            return false;

        if ((capture.FinalKineticsPhase & KineticStateFlags.Missile) == 0
            && capture.RemoteMotionRuntime is not null)
            return false;

        if (!IsFinite(vel)
            || !IsFinite(angularVel)
            || !double.IsFinite(latestMoment))
        {
            DiagnosticSink?.Invoke(
                $"Rejected invalid VelocityUpdate for missile 0x{srvOid:X8}.");
            return true;
        }
        _previousFinitePlayMoment = latestMoment;

        return _runtimeUpdater.ImposeAuthoritativeVector(
                capture.Canonical,
                anticipatedVectorArbiterVer,
                anticipatedVelArbiterVer,
                vel,
                angularVel,
                latestMoment,
                () => TryFetchLatest(
                        srvOid,
                        out OnlineActorRecord latest,
                        out SimMissile latestCore)
                    && ReferenceEquals(latest, capture)
                    && ReferenceEquals(latestCore, core));
    }

    internal bool ImposeAuthoritativePhase(
        OnlineActorRecord anticipatedCapture,
        KineticStateFlags phase,
        double latestMoment,
        int onlineMiddleX,
        int onlineMiddleY)
    {
        return ImposeAuthoritativePhase(
            anticipatedCapture,
            anticipatedCapture.PhaseArbiterVer,
            phase,
            latestMoment,
            onlineMiddleX,
            onlineMiddleY);
    }

    internal bool ImposeAuthoritativePhase(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedPhaseArbiterVer,
        KineticStateFlags phase,
        double latestMoment,
        int onlineMiddleX,
        int onlineMiddleY)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        uint srvOid = anticipatedCapture.ServerOid;
        if (!_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture))
            return false;
        if (!ReferenceEquals(capture, anticipatedCapture)
            || capture.PhaseArbiterVer != anticipatedPhaseArbiterVer)
            return false;

        bool validTimer = double.IsFinite(latestMoment);
        double netTimer = validTimer
            ? latestMoment
            : _previousFinitePlayMoment;
        if (validTimer)
            _previousFinitePlayMoment = latestMoment;
        if (!validTimer)
            DiagnosticSink?.Invoke(
                $"Ignored invalid State update clock for live object 0x{srvOid:X8}; state remains authoritative.");

        if (capture.ProjectileRuntime is SimMissile core)
        {
            return _runtimeUpdater.ImposeAuthoritativePhase(
                capture.Canonical,
                anticipatedPhaseArbiterVer,
                phase,
                netTimer,
                onlineMiddleX,
                onlineMiddleY,
                () => _onlineActors.TryFetchRecord(
                        srvOid,
                        out OnlineActorRecord latest)
                    && ReferenceEquals(latest, capture)
                    && ReferenceEquals(
                        latest.ProjectileRuntime,
                        core));
        }

        if (capture.RemoteMotionRuntime is { } distant)
            distant.Body.State = phase;

        if ((phase & KineticStateFlags.Missile) == 0
            || capture.WorldEntity is not { } actor)
            return false;

        RigSpec? rig = _setupResolver?.Resolve(actor.SrcGfxObjRefOrRigIdent);
        if (rig is null)
        {
            DiagnosticSink?.Invoke(
                $"Cannot activate missile 0x{srvOid:X8}: Setup 0x{actor.SrcGfxObjRefOrRigIdent:X8} is unavailable.");
            return true;
        }

        TryAttach(capture, rig, netTimer, onlineMiddleX, onlineMiddleY);
        return true;
    }
}
