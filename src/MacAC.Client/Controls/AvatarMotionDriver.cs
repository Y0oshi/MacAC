using MacAC.Dat;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Client.Controls;

internal sealed class AvatarMotionDriver(
    OnlineActorCore liveEntities,
    IAvatarIdentitySource identity,
    OnlineActorMotionEngineView<OnlineActorMotionLedger> animations,
    OnlineActorMotionExhibitor presenter,
    MotionHookFrameQueue hooks)
{
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger> _anims = animations ?? throw new ArgumentNullException(nameof(animations));
    private readonly OnlineActorMotionExhibitor _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
    private readonly MotionHookFrameQueue _taps = hooks ?? throw new ArgumentNullException(nameof(hooks));

    public void ProgressTrunk(float diffSecs, MotionDeltaPose product)
    {
        ArgumentNullException.ThrowIfNull(product);
        product.Reset();

        uint avatarOid = _identity.SrvOid;
        if (!_onlineActors.TryFetchRealmActor(avatarOid, out var actor)
            || !_anims.TryGetValue(actor.Id, out var anim)
            || anim.Sequencer is not { } scheduler
            || !_onlineActors.ShouldProceedTrunkCore(avatarOid)
            || _onlineActors.IsHidden(avatarOid))

            return;

        if (_onlineActors.TryFetchRecord(avatarOid, out OnlineActorRecord capture))
            _presenter.ReadyAnim(capture, anim);

        Pose trunkCycle = anim.TrunkLocomotionTemp;
        ProgressTrunkRest(product, anim, scheduler, trunkCycle, diffSecs);
    }

    private void ProgressTrunkRest(MotionDeltaPose product, OnlineActorMotionLedger anim, Mechanics.Kinetics.AnimSequencer scheduler, Pose trunkCycle, float diffSecs)
    {
        trunkCycle.Origin = System.Numerics.Vector3.Zero;
        trunkCycle.Orientation = System.Numerics.Quaternion.Identity;
        anim.ReadiedSeriesCycles = anim.GrabSeriesCycles(
                    scheduler.Advance(diffSecs, trunkCycle));
        ProgressTrunkTail(trunkCycle, anim, product);
    }

    private void ProgressTrunkTail(Pose trunkCycle, OnlineActorMotionLedger anim, MotionDeltaPose product)
    {
        anim.SeriesAdvancedPriorAnimPass = true;
        product.Origin = trunkCycle.Origin;
        product.Orientation = trunkCycle.Orientation;
    }

    public void GrabTaps()
    {
        uint avatarOid = _identity.SrvOid;
        if (_onlineActors.TryFetchRealmActor(avatarOid, out var actor)
            && _anims.TryGetValue(actor.Id, out var anim)
            && anim.Sequencer is { } scheduler)

            _taps.Capture(anim.Entity.Id, scheduler);
    }
}
