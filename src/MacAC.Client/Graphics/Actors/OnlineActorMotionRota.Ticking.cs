using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;
using PeerMotion = MacAC.Sim.Kinetics.PeerMotion;

namespace MacAC.Client.Graphics;

internal sealed partial class OnlineActorMotionRota
{
    public IReadOnlyDictionary<SimActorKey, OnlineActorMotionSchedule> Tick(
        float passedSecs,
        Vector3? avatarLocus,
        bool ownConcealedPiecePostureStale,
        int onlineMiddleX,
        int onlineMiddleY,
        Action<OnlineActorRecord, OnlineActorMotionLedger>? readyAnim = null)
    {
        _schedules.Clear();
        var core = _onlineActors;

        core.DuplicateSpatialTrunkObjectRecordsTo(_trunkCapture);
        foreach (OnlineActorRecord capture in _trunkCapture)
        {
            if (!core.IsLatestSpatialTrunkObject(capture)
                || capture.WorldEntity is not { } actor)

                continue;

            OnlineActorMotionLedger? anim = capture.AnimationRuntime as OnlineActorMotionLedger;
            PeerMotion? distant = capture.RemoteMotionRuntime as PeerMotion;
            SimMissile? missile =
                capture.ProjectileRuntime as SimMissile;
            ulong objectTimerEpoch = capture.ObjectTimerEpoch;

            if (!IsLatest(
                    core,
                    capture,
                    actor,
                    anim,
                    distant,
                    missile,
                    objectTimerEpoch))
                continue;

            if (anim is not null)
            {
                readyAnim?.Invoke(capture, anim);
                if (!IsLatest(
                        core,
                        capture,
                        actor,
                        anim,
                        distant,
                        missile,
                        objectTimerEpoch))

                    continue;
            }

            var plan = ProgressCapture(
                core,
                capture,
                actor,
                anim,
                distant,
                missile,
                passedSecs,
                avatarLocus,
                ownConcealedPiecePostureStale,
                onlineMiddleX,
                onlineMiddleY,
                objectTimerEpoch);

            if (anim is not null
                && plan.ComposeParts
                && IsLatest(
                    core,
                    capture,
                    actor,
                    anim,
                    distant,
                    missile,
                    objectTimerEpoch))
            {
                IReadOnlyList<PieceTransform>? possessedCycles = plan.SequenceFrames is { } produced
                    ? anim.GrabPlanCycles(produced)
                    : null;
                SimActorKey tag = capture.ProjTag
                    ?? throw new InvalidOperationException(
                        $"Live entity 0x{capture.ServerOid:X8}/" +
                        $"{capture.Generation} has no exact projection key");
                _schedules[tag] = plan with
                {
                    SequenceFrames = possessedCycles,
                    Record = capture,
                    Entity = actor,
                    Animation = anim,
                    ObjectClockEpoch = objectTimerEpoch,
                    ProjectionMutationVersion = capture.ProjAlterationVer,
                    PresentationRevision = anim.ExhibitRev,
                };
            }
        }

        return _schedules;
    }

    private static void ImposeTrunkCycle(
        OnlineActorRecord capture,
        RealmActor actor,
        Pose trunkCycle,
        float objectScaling)
    {
        var corpus = capture.KineticBody;
        Vector3 locus = corpus?.Position ?? actor.Position;
        Quaternion facing = corpus?.Orientation ?? actor.Rotation;
        Vector3 ownOrigin = corpus?.OnWalkable == true
            ? trunkCycle.Origin * objectScaling
            : Vector3.Zero;

        if (ownOrigin != Vector3.Zero)
            locus += Vector3.Transform(ownOrigin, facing);
        if (!trunkCycle.Orientation.IsIdentity)
        {
            facing = PoseOps.AssignSpin(
                locus,
                facing,
                facing * trunkCycle.Orientation);
        }

        ImposeTrunkCycleRest(corpus, locus, actor, facing);
    }

    private static void ImposeTrunkCycleRest(KineticBody? corpus, Vector3 locus, RealmActor actor, Quaternion facing)
    {
        if (corpus is not null)
        {
            corpus.Position = locus;
            corpus.Orientation = facing;
        }
        actor.SetPosition(locus);
        actor.Rotation = facing;
    }

    private static bool IsLatest(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        OnlineActorMotionLedger? anim,
        PeerMotion? distant,
        SimMissile? missile,
        ulong objectTimerEpoch)
    {
        return core.IsLatestSpatialTrunkObject(capture)
        && capture.ObjectTimerEpoch == objectTimerEpoch
        && ReferenceEquals(capture.WorldEntity, actor)
        && ReferenceEquals(capture.AnimationRuntime, anim)
        && ReferenceEquals(capture.RemoteMotionRuntime, distant)
        && ReferenceEquals(capture.ProjectileRuntime, missile)
        && (anim is null || core.IsLatestSpatialAnim(capture, anim));
    }

    private static void ExecuteKeeperRear(
        PeerMotion? distant,
        MotionTableKeeper? pieceArr)
    {
        if (distant is not null)
        {
            CanonObjectKeeperTail.Run(
                distant.Host?.MarkKeeper,
                distant.Movement,
                pieceArr,
                distant.Host?.LocusKeeper);
        }
        else
        {
            pieceArr?.WieldMoment();
        }
    }
}
