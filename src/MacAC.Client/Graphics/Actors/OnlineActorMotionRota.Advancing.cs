using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Kinetics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;
using PeerMotion = MacAC.Sim.Kinetics.PeerMotion;

namespace MacAC.Client.Graphics;

internal sealed partial class OnlineActorMotionRota
{
    private OnlineActorMotionSchedule ProgressCapture(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        OnlineActorMotionLedger? anim,
        PeerMotion? distant,
        SimMissile? missile,
        float passedSecs,
        Vector3? avatarLocus,
        bool ownConcealedPiecePostureStale,
        int onlineMiddleX,
        int onlineMiddleY,
        ulong objectTimerEpoch)
    {
        uint srvOid = capture.ServerOid;
        var scheduler = anim?.Sequencer;
        var phase = capture.FinalKineticsPhase;
        bool concealed = (phase & KineticStateFlags.Hidden) != 0;

        if (srvOid == _ownAvatar.SrvOid)
        {
            var readied =
                anim?.ReadiedSeriesCycles;
            bool advanced = anim?.SeriesAdvancedPriorAnimPass == true;
            if (anim is not null)
            {
                anim.ReadiedSeriesCycles = null;
                anim.SeriesAdvancedPriorAnimPass = false;
            }

            bool constructConcealed = concealed && ownConcealedPiecePostureStale;
            if (!IsLatest(
                    core,
                    capture,
                    actor,
                    anim,
                    distant,
                    missile,
                    objectTimerEpoch))

                return default;

            _rootPoses.RenewTrunk(actor);
            return !IsLatest(
                    core,
                    capture,
                    actor,
                    anim,
                    distant,
                    missile,
                    objectTimerEpoch)
                ? default
                : new OnlineActorMotionSchedule(
                readied ?? (constructConcealed && scheduler is not null
                    ? anim?.GrabSeriesCycles(scheduler.ProbeLatestPosture())
                    : null),
                0f,
                ComposeParts: advanced || constructConcealed);
        }

        var activity = CanonActivityGate.Evaluate(
            capture.ObjectTimer,
            distant?.Body ?? missile?.Body ?? capture.KineticBody,
            core.FetchTrunkObjectTimerDisposition(srvOid)
                is CanonClockVerdict.Advance,
            capture.HasPieceArr,
            (phase & KineticStateFlags.Static) != 0,
            actor.Position,
            avatarLocus,
            passedSecs);
        if (activity is not CanonActivityOutcome.Active)
            return default;

        var lot = capture.ObjectTimer.Advance(passedSecs);
        if (lot.Count is 0)
            return default;

        IReadOnlyList<PieceTransform>? cycles = null;
        float legacyPassed = 0f;
        bool finished = true;
        MissileDriver missileDriver = _missiles;
        bool missileHndsTravel = missile is not null
            && missileDriver?.HndsTravel(srvOid) == true;
        float objectScaling = anim?.Scale
            ?? capture.Snapshot.Physics?.Scale
            ?? capture.Snapshot.ObjScale
            ?? actor.Scale;

        for (int qi = 0; qi < lot.Count; ++qi)
        {
            if (!IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))
            {
                finished = false;
                break;
            }

            float quantum = lot.FetchQuantum(qi);
            if (concealed)
            {
                if (distant is not null)
                {
                    if (!_distantKinetics.PulseConcealed(
                        distant,
                        actor,
                        quantum,
                        scheduler?.Manager,
                        _animTaps.Capture,
                        scheduler,
                        core,
                        capture,
                        objectTimerEpoch))
                    {
                        finished = false;
                        break;
                    }
                }
                else
                {
                    if (scheduler is not null)
                    {
                        _animTaps.Capture(actor.Id, scheduler);
                        if (!IsLatest(
                                core,
                                capture,
                                actor,
                                anim,
                                distant,
                                missile,
                                objectTimerEpoch))
                        {
                            finished = false;
                            break;
                        }
                    }
                    ExecuteKeeperRear(distant, scheduler?.Manager);
                }

                if (!IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))
                {
                    finished = false;
                    break;
                }
                continue;
            }

            Pose trunkCycle = anim?.TrunkLocomotionTemp ?? _trunkCycleTemp;
            trunkCycle.Origin = Vector3.Zero;
            trunkCycle.Orientation = Quaternion.Identity;
            if (scheduler is not null)
                cycles = anim?.GrabSeriesCycles(
                    scheduler.Advance(quantum, trunkCycle))
                    ?? scheduler.Advance(quantum, trunkCycle);
            else if (anim is not null)
                legacyPassed += quantum;

            if (!IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))
            {
                finished = false;
                break;
            }

            if (distant is not null && !missileHndsTravel)
            {
                if (anim is not null && quantum > 0f)
                {
                    float trunkLocomotionPace = trunkCycle.Origin.Length()
                        * objectScaling / quantum;
                    distant.UpperTrunkLocomotionPaceSincePreviousUP = MathF.Max(
                        distant.UpperTrunkLocomotionPaceSincePreviousUP,
                        trunkLocomotionPace);
                }

                MotionDeltaPose trunkDiff = anim?.TrunkLocomotionDiffTemp
                    ?? _trunkDiffTemp;
                trunkDiff.Origin = trunkCycle.Origin;
                trunkDiff.Orientation = trunkCycle.Orientation;
                if (!_distantKinetics.Tick(
                    distant,
                    actor,
                    objectScaling,
                    scheduler,
                    anim,
                    quantum,
                    trunkDiff,
                    onlineMiddleX,
                    onlineMiddleY,
                    _animTaps.Capture,
                    core,
                    capture,
                    objectTimerEpoch))
                {
                    finished = false;
                    break;
                }
            }
            else
            {
                bool plainCorpusHndsTravel = missile is null
                    && capture.KineticBody is not null;
                if (plainCorpusHndsTravel)
                {
                    if (!_plainKinetics.Tick(
                            core,
                            capture,
                            actor,
                            trunkCycle,
                            objectScaling,
                            quantum,
                            onlineMiddleX,
                            onlineMiddleY,
                            objectTimerEpoch,
                            scheduler,
                            _animTaps.Capture))
                    {
                        finished = false;
                        break;
                    }
                }
                else
                {
                    ImposeTrunkCycle(capture, actor, trunkCycle, objectScaling);
                }
                if (!IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))
                {
                    finished = false;
                    break;
                }

                MissileDriver.QuantumHop missileHop = default;
                bool beganMissile = missileHndsTravel
                    && missileDriver?.TryCommenceQuantum(
                        capture,
                        quantum,
                        out missileHop) == true;

                if (!plainCorpusHndsTravel && scheduler is not null)
                    _animTaps.Capture(actor.Id, scheduler);
                if (!IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))
                {
                    finished = false;
                    break;
                }

                if (beganMissile
                    && !missileDriver!.ConcludeQuantum(
                        missileHop,
                        onlineMiddleX,
                        onlineMiddleY))
                {
                    finished = false;
                    break;
                }
                if (!IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))
                {
                    finished = false;
                    break;
                }

                ExecuteKeeperRear(distant, scheduler?.Manager);
            }

            if (!IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))
            {
                finished = false;
                break;
            }
        }

        if (!finished
            || !IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))

            return default;

        _rootPoses.RenewTrunk(actor);
        if (!IsLatest(core, capture, actor, anim, distant, missile, objectTimerEpoch))
            return default;

        return concealed
            ? new OnlineActorMotionSchedule(
                scheduler is not null && anim is not null
                    ? anim.GrabSeriesCycles(scheduler.ProbeLatestPosture())
                    : null,
                0f,
                ComposeParts: anim is not null)
            : new OnlineActorMotionSchedule(
            cycles,
            legacyPassed,
            ComposeParts: anim is not null);
    }
}
