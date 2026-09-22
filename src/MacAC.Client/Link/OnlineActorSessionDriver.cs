using MacAC.Client.Graphics.Effects;
using MacAC.Client.Kinetics;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Sim.Presence;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Link;

internal sealed class OnlineActorSessionDriver(
    CanonInboundEventRouter inbound,
    OnlineActorFillingDriver hydration,
    OnlineActorNetworkRefreshDriver updates,
    IAvatarWarpWireSink teleport,
    ActorEffectDriver effects)
{
    private readonly CanonInboundEventRouter _incoming = inbound ?? throw new ArgumentNullException(nameof(inbound));
    private readonly OnlineActorFillingDriver _hydration = hydration ?? throw new ArgumentNullException(nameof(hydration));
    private readonly OnlineActorNetworkRefreshDriver _updates = updates ?? throw new ArgumentNullException(nameof(updates));
    private readonly IAvatarWarpWireSink _warp = teleport ?? throw new ArgumentNullException(nameof(teleport));
    private readonly ActorEffectDriver _fxList = effects ?? throw new ArgumentNullException(nameof(effects));

    public OnlineActorSessionSink BuildDrain()
    {
        return new(
        OnSpawned,
        OnDeleted,
        OnPickedUp,
        OnLocomotionUpdated,
        OnLocusUpdated,
        OnVectorUpdated,
        OnPhaseUpdated,
        OnAncestorUpdated,
        OnWarpBegun,
        OnLooksUpdated,
        OnPlayKineticsProgram,
        OnPlayKineticsProgramKind,
        OnSfxSignal);
    }

    private void OnSpawned(RealmSession.MoverSpawn val)
    {
        _incoming.Run(_hydration, val,
            static (holder, msg) => holder.OnCreate(msg));
    }

    private void OnDeleted(ObjectDeletion.Parsed val)
    {
        _incoming.Run(_hydration, val,
            static (holder, msg) => holder.OnErase(msg));
    }

    private void OnPickedUp(PickupNotice.Parsed val)
    {
        _incoming.Run(_hydration, val,
            static (holder, msg) => holder.OnLift(msg));
    }

    private void OnLocomotionUpdated(RealmSession.MoverMotionUpdate val)
    {
        _incoming.Run(_updates, val,
            static (holder, msg) => holder.OnLocomotion(msg));
    }

    private void OnLocusUpdated(RealmSession.MoverPositionUpdate val)
    {
        _incoming.Run(_updates, val,
            static (holder, msg) => holder.OnPosition(msg));
    }

    private void OnVectorUpdated(VelocityUpdate.Parsed val)
    {
        _incoming.Run(_updates, val,
            static (holder, msg) => holder.OnVector(msg));
    }

    private void OnPhaseUpdated(GroupPhase.Parsed val)
    {
        _incoming.Run(_updates, val,
            static (holder, msg) => holder.OnState(msg));
    }

    private void OnAncestorUpdated(AncestorSignal.Parsed val)
    {
        _incoming.Run(_hydration, val,
            static (holder, msg) => holder.OnParent(msg));
    }

    private void OnWarpBegun(uint val)
    {
        _incoming.Run(_warp, val,
            static (holder, msg) => holder.OnWarpBegun(msg));
    }

    private void OnLooksUpdated(ObjDescNotice.Parsed val)
    {
        _incoming.Run(_hydration, val,
            static (holder, msg) => holder.OnLooks(msg));
    }

    private void OnPlayKineticsProgram(PlayKineticsProgram val)
    {
        _incoming.Run(_fxList, val,
            static (holder, msg) => holder.ProcessStraight(msg));
    }

    private void OnPlayKineticsProgramKind(PlayKineticsProgramKind val)
    {
        _incoming.Run(_fxList, val,
            static (holder, msg) => holder.ProcessTyped(msg));
    }

    private void OnSfxSignal(SfxSignal val)
    {
        _incoming.Run(_fxList, val,
            static (holder, msg) => holder.ProcessSfx(msg));
    }
}
