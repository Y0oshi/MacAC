using MacAC.Client.Graphics.Effects;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface IMotionHookCaptureSink
{
    void Capture(uint holderOwnIdent, AnimSequencer scheduler);
}

internal sealed class MotionHookCaptureSink(MotionHookFrameQueue queue) : IMotionHookCaptureSink
{
    private readonly MotionHookFrameQueue _fifo = queue ?? throw new ArgumentNullException(nameof(queue));

    public void Capture(uint holderOwnIdent, AnimSequencer scheduler) =>
        _fifo.Capture(holderOwnIdent, scheduler);
}

internal interface IActorRootPoseHerald
{
    void RenewTrunk(RealmActor actor);
}

internal sealed class ActorRootPoseHerald(ActorEffectPoseRegistry poses) : IActorRootPoseHerald
{
    private readonly ActorEffectPoseRegistry _postures = poses ?? throw new ArgumentNullException(nameof(poses));

    public void RenewTrunk(RealmActor actor) => _postures.RefreshTrunk(actor);
}
