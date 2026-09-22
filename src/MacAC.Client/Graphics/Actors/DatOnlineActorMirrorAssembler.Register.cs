using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Wire;

namespace MacAC.Client.Graphics;

internal sealed partial class DatOnlineActorMirrorAssembler
{
    private void EnrollAnim(
        OnlineActorRecord anticipatedCapture,
        RealmActor actor,
        RigSpec rig,
        RealmSession.MoverSpawn summon,
        GaitResolver.RestCycle? idleCycle,
        float scaling,
        IReadOnlyList<OnlineMotionPartTemplate> pieceBlueprint,
        IReadOnlyList<bool> pieceReadiness,
        bool keptAnim,
        bool synchronizeAnim)
    {
        if (keptAnim
            && synchronizeAnim
            && anticipatedCapture.AnimationRuntime is OnlineActorMotionLedger kept)
        {
            SynchronizeKeptAnim(
                anticipatedCapture,
                kept,
                rig,
                summon,
                idleCycle);
        }
        if (!keptAnim
            && idleCycle is not null
            && idleCycle.Framerate != 0f
            && idleCycle.HighFrame > idleCycle.LowFrame
            && idleCycle.Animation.Frames.Count > 1)
        {
            var scheduler = BuildLocomotionScheduler(rig, summon);
            _runtime.AssignAnimCore(
                summon.Guid,
                new OnlineActorMotionLedger
                {
                    Entity = actor,
                    Setup = rig,
                    Animation = idleCycle.Animation,
                    LoCycle = Math.Max(0, idleCycle.LowFrame),
                    HighFrame = Math.Min(
                        idleCycle.HighFrame,
                        idleCycle.Animation.Frames.Count - 1),
                    Framerate = idleCycle.Framerate,
                    Scale = scaling,
                    PieceBlueprint = pieceBlueprint,
                    PieceReadiness = pieceReadiness,
                    CurrCycle = idleCycle.LowFrame,
                    Sequencer = scheduler,
                });
        }
        else if (!keptAnim)
        {
            uint locomotionChartIdent = summon.MotionTableId ?? (uint)rig.DefaultMotionBookId;
            if (locomotionChartIdent is not 0
                && _datFiles.Get<MotionBook>(locomotionChartIdent) is { } locomotionChart)
            {
                var scheduler = SummonLocomotionInitializer.Create(
                    rig,
                    locomotionChart,
                    _animFetcher,
                    summon.MotionState);
                _runtime.AssignAnimCore(
                    summon.Guid,
                    new OnlineActorMotionLedger
                    {
                        Entity = actor,
                        Setup = rig,
                        Animation = null!,
                        LoCycle = 0,
                        HighFrame = 0,
                        Framerate = 0f,
                        Scale = scaling,
                        PieceBlueprint = pieceBlueprint,
                        PieceReadiness = pieceReadiness,
                        CurrCycle = 0,
                        Sequencer = scheduler,
                    });

                if (KineticTelemetry.ProbeBuildingEnabled)
                {
                    var starting = SummonLocomotionInitializer.LocatePlan(
                        locomotionChart,
                        summon.MotionState);
                    Console.WriteLine(
                        $"[reactive-anim] registered guid=0x{summon.Guid:X8} "
                        + $"entityId=0x{actor.Id:X8} mtable=0x{locomotionChartIdent:X8} "
                        + $"initialStyle=0x{starting.Style:X8} initialCycle=0x{starting.Motion:X8}");
                }
            }
        }

        bool kineticsStatic = (anticipatedCapture.FinalKineticsPhase
            & KineticStateFlags.Static) != 0;
        if (!keptAnim
            && anticipatedCapture.AnimationRuntime is null
            && (uint)rig.DefaultClipId is not 0)
        {
            AnimSequencer scheduler = new AnimSequencer(
                rig,
                new MotionBook(),
                _animFetcher);
            if (scheduler.HasLatestJoint)
            {
                _runtime.AssignAnimCore(
                    summon.Guid,
                    new OnlineActorMotionLedger
                    {
                        Entity = actor,
                        Setup = rig,
                        Animation = null!,
                        LoCycle = 0,
                        HighFrame = 0,
                        Framerate = 0f,
                        Scale = scaling,
                        PieceBlueprint = pieceBlueprint,
                        PieceReadiness = pieceReadiness,
                        CurrCycle = 0,
                        Sequencer = scheduler,
                    });
            }
        }

        if (anticipatedCapture.AnimationRuntime is not OnlineActorMotionLedger anim)
            return;

        _animPresenter.ReadyAnim(anticipatedCapture, anim);
        if (!kineticsStatic
            || anim.Sequencer is not { } staticScheduler
            || summon.Position is not { } staticLocus)

            return;

        if (_runtime.HasEngagedStartingBuildResidence(anticipatedCapture.Canonical))

            return;

        KineticBody corpus = _runtime.GetOrCreatePhysicsBody(
            summon.Guid,
            incarnation =>
            {
                KineticBody built = new KineticBody { Orientation = actor.Rotation };
                built.SnapToChamber(
                    staticLocus.LandblockId,
                    actor.Position,
                    new Vector3(
                        staticLocus.PositionX,
                        staticLocus.PositionY,
                        staticLocus.PositionZ));
                return built;
            });
        _staticAnims.AttachOnlineHolder(
            actor,
            anim,
            corpus);
    }
}
