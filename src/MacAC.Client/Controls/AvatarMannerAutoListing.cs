using MacAC.Client.Link;
using MacAC.Client.Paging;
using MacAC.Client.Realm;

namespace MacAC.Client.Controls;

internal interface IAvatarModeAutoEntryScope
{
    bool IsOnlineInRealm { get; }
    bool IsAvatarActorPresent { get; }
    bool IsPlayerControllerReady { get; }
    bool IsRealmPrimed { get; }
    bool IsAvatarMannerEngaged { get; }
    void JoinAvatarManner();
}

internal sealed class OnlineAvatarModeAutoEntryScope(
    IOnlineInRealmSource session,
    OnlineActorCore liveEntities,
    IAvatarIdentitySource identity,
    RealmRevealMarshal worldReveal,
    IAvatarModeSource mode,
    AvatarModeDriver playerMode)
        : IAvatarModeAutoEntryScope
{
    private readonly IOnlineInRealmSource _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly RealmRevealMarshal _worldReveal = worldReveal ?? throw new ArgumentNullException(nameof(worldReveal));
    private readonly IAvatarModeSource _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    private readonly AvatarModeDriver _avatarManner = playerMode ?? throw new ArgumentNullException(nameof(playerMode));

    public bool IsOnlineInRealm => _session.IsInWorld;

    public bool IsAvatarActorPresent =>
        _onlineActors.ContainsRealmActor(_identity.SrvOid);

    public bool IsPlayerControllerReady
    {
        get
        {
            return _avatarManner.Driver is { IsRuntimePublished: true }
        && _onlineActors.TryFetchRecord(
            _identity.SrvOid,
            out OnlineActorRecord capture)
        && capture.PhysicsHost is ActorKineticsHarbor;
        }
    }

    public bool IsRealmPrimed
    {
        get
        {
            return (_worldReveal.Snapshot.Kind != MacAC.Sim.SimPortalKind.Login
            || _worldReveal.Snapshot.Completed)
        && _onlineActors.TryGetCapture(
            _identity.SrvOid,
            out MacAC.Wire.RealmSession.MoverSpawn avatar)
        && avatar.Position is { LandblockId: not 0u } locus
        && _worldReveal.Evaluate(locus.LandblockId).IsPrimed;
        }
    }

    public bool IsAvatarMannerEngaged => _mode.IsPlayerMode;

    public void JoinAvatarManner()
    {
        _avatarManner.JoinFromAutoListing();
        _worldReveal.Complete();
    }
}

public sealed class AvatarMannerAutoListing
{
    private sealed class DelegateScope(
        Func<bool> isLiveInWorld,
        Func<bool> isPlayerEntityPresent,
        Func<bool> isPlayerControllerReady,
        Func<bool> isWorldReady,
        Action enterPlayerMode,
        Func<bool>? isAvatarMannerEngaged) : IAvatarModeAutoEntryScope
    {
        private readonly Func<bool> _isOnlineInRealm = isLiveInWorld
                ?? throw new ArgumentNullException(nameof(isLiveInWorld));
        private readonly Func<bool> _isAvatarActorPresent = isPlayerEntityPresent
                ?? throw new ArgumentNullException(nameof(isPlayerEntityPresent));
        private readonly Func<bool> _isAvatarDriverPrimed = isPlayerControllerReady
                ?? throw new ArgumentNullException(nameof(isPlayerControllerReady));
        private readonly Func<bool> _isRealmPrimed = isWorldReady
                ?? throw new ArgumentNullException(nameof(isWorldReady));
        private readonly Action _joinAvatarManner = enterPlayerMode
                ?? throw new ArgumentNullException(nameof(enterPlayerMode));
        private readonly Func<bool> _isAvatarMannerEngaged = isAvatarMannerEngaged ?? (() => false);

        public bool IsOnlineInRealm => _isOnlineInRealm();
        public bool IsAvatarActorPresent => _isAvatarActorPresent();
        public bool IsPlayerControllerReady => _isAvatarDriverPrimed();
        public bool IsRealmPrimed => _isRealmPrimed();
        public bool IsAvatarMannerEngaged => _isAvatarMannerEngaged();
        public void JoinAvatarManner() => _joinAvatarManner();
    }

    private readonly IAvatarModeAutoEntryScope _ctx;

    internal AvatarMannerAutoListing(IAvatarModeAutoEntryScope context) =>
        _ctx = context ?? throw new ArgumentNullException(nameof(context));

    public AvatarMannerAutoListing(
        Func<bool> isOnlineInRealm,
        Func<bool> isAvatarActorPresent,
        Func<bool> isAvatarDriverPrimed,
        Func<bool> isRealmPrimed,
        Action joinAvatarManner,
        Func<bool>? isAvatarMannerEngaged = null)
        : this(new DelegateScope(
            isOnlineInRealm,
            isAvatarActorPresent,
            isAvatarDriverPrimed,
            isRealmPrimed,
            joinAvatarManner,
            isAvatarMannerEngaged))
    {
    }

    public bool IsLoaded { get; private set; }

    public void Arm() => IsLoaded = true;

    public void Cancel() => IsLoaded = false;

    public bool TryEnter()
    {
        if (!IsLoaded) return false;
        if (_ctx.IsAvatarMannerEngaged)
        {
            IsLoaded = false;
            return false;
        }
        if (!_ctx.IsOnlineInRealm) return false;
        if (!_ctx.IsAvatarActorPresent) return false;
        if (!_ctx.IsPlayerControllerReady) return false;
        if (!_ctx.IsRealmPrimed) return false;

        IsLoaded = false;
        _ctx.JoinAvatarManner();
        return true;
    }
}
