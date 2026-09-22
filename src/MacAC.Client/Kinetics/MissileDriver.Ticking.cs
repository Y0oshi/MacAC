using System.Numerics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using PeerMotion = MacAC.Sim.Kinetics.PeerMotion;

namespace MacAC.Client.Kinetics;

internal sealed partial class MissileDriver
{
    internal void Tick(
        double latestMoment,
        int onlineMiddleX,
        int onlineMiddleY,
        Vector3? avatarRealmLocus)
    {
        double passed = latestMoment - _previousFinitePlayMoment;
        Tick(
            latestMoment,
            double.IsFinite(passed) && passed > 0.0 ? (float)passed : 0f,
            onlineMiddleX,
            onlineMiddleY,
            avatarRealmLocus);
    }

    internal void Tick(
        double latestMoment,
        float passedSecs,
        int onlineMiddleX,
        int onlineMiddleY,
        Vector3? avatarRealmLocus)
    {
        if (!double.IsFinite(latestMoment))
        {
            DiagnosticSink?.Invoke("Rejected non-finite projectile update clock.");
            return;
        }
        _previousFinitePlayMoment = latestMoment;

        _onlineActors.DuplicateSpatialMissileRecordsTo(_spatialMissileCapture);
        foreach (OnlineActorRecord capture in _spatialMissileCapture)
        {
            if (capture.ProjectileRuntime is not SimMissile core
                || !_onlineActors.IsLatestSpatialMissile(capture, core)
                || capture.WorldEntity is not { } actor)
                continue;

            core.Body.State = capture.FinalKineticsPhase;
            if (!HasShownChamber(capture))
            {
                SuspendBeyondRealm(core, actor.Id);
                continue;
            }

            if (!core.Body.InWorld)
            {
                core.Body.PreviousRefreshMoment = latestMoment;
                core.Body.InWorld = true;
                ProxyPositionSynchronizer.Sync(
                    _shades,
                    actor.Id,
                    core.Body.Position,
                    core.Body.Orientation,
                    capture.WholeChamberIdent,
                    onlineMiddleX,
                    onlineMiddleY);
                _rootPoses?.RenewTrunk(actor);
            }

            if (IsConcealed(capture))
            {
                _shades.Suspend(actor.Id);
                if (capture.AnimationRuntime is null
                    && capture.RemoteMotionRuntime is null)
                {
                    var concealedActivity =
                        CanonActivityGate.Evaluate(
                            capture.ObjectTimer,
                            core.Body,
                            _onlineActors.FetchTrunkObjectTimerDisposition(capture.ServerOid)
                                is CanonClockVerdict.Advance,
                            capture.HasPieceArr,
                            (capture.FinalKineticsPhase & KineticStateFlags.Static) != 0,
                            core.Body.Position,
                            avatarRealmLocus,
                            passedSecs);
                    if (concealedActivity is CanonActivityOutcome.Active)
                        capture.ObjectTimer.Advance(passedSecs);
                }
                continue;
            }

            bool isMissile =
                (capture.FinalKineticsPhase & KineticStateFlags.Missile) != 0;
            if (!isMissile && capture.RemoteMotionRuntime is not null)
                continue;

            if (capture.AnimationRuntime is not null)
                continue;

            var activity = CanonActivityGate.Evaluate(
                capture.ObjectTimer,
                core.Body,
                _onlineActors.FetchTrunkObjectTimerDisposition(capture.ServerOid)
                    is CanonClockVerdict.Advance,
                capture.HasPieceArr,
                (capture.FinalKineticsPhase & KineticStateFlags.Static) != 0,
                core.Body.Position,
                avatarRealmLocus,
                passedSecs);
            if (activity is not CanonActivityOutcome.Active)
                continue;

            var lot = capture.ObjectTimer.Advance(passedSecs);
            for (int qi = 0; qi < lot.Count; ++qi)
            {
                if (!ProgressQuantum(
                        capture,
                        lot.FetchQuantum(qi),
                        onlineMiddleX,
                        onlineMiddleY))
                    break;
                if (capture.RemoteMotionRuntime is PeerMotion distant)
                {
                    CanonObjectKeeperTail.Run(
                        distant.Host?.MarkKeeper,
                        distant.Movement,
                        pieceArr: null,
                        distant.Host?.LocusKeeper);
                }
            }
        }
    }
}
