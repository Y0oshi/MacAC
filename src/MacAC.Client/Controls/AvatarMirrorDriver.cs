using MacAC.Client.Kinetics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Controls;

internal interface IAvatarMirrorEngine
{
    RealmActor? LocateActor();
    int OnlineMiddleX { get; }
    int OnlineMiddleY { get; }
    void SynchronizeShade(RealmActor actor, uint chamberIdent);
    void Rebucket(uint srvOid, uint lbIdent);
    bool IsLatestShownProj(RealmActor actor);
    void SuspendShade(RealmActor actor);
}

internal sealed class OnlineAvatarMirrorEngine(
    OnlineActorCore liveEntities,
    IAvatarIdentitySource identity,
    OnlineRealmOriginLedger origin,
    OwnAvatarShadeSyncer shadow)
        : IAvatarMirrorEngine
{
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly OwnAvatarShadeSyncer _shade = shadow ?? throw new ArgumentNullException(nameof(shadow));

    public RealmActor? LocateActor()
    {
        return _onlineActors.TryFetchRealmActor(_identity.SrvOid, out var actor)
            ? actor
            : null;
    }

    public int OnlineMiddleX => _origin.CenterX;
    public int OnlineMiddleY => _origin.CenterY;

    public void SynchronizeShade(RealmActor actor, uint chamberIdent) =>
        _shade.Sync(actor, chamberIdent);

    public void Rebucket(uint srvOid, uint lbIdent) =>
        _onlineActors.RebucketLiveEntity(srvOid, lbIdent);

    public bool IsLatestShownProj(RealmActor actor) =>
        _shade.IsLatestVisibleProjection(actor);

    public void SuspendShade(RealmActor actor) => _shade.Suspend(actor);
}

public sealed class AvatarMirrorDriver
{
    private readonly IAvatarMirrorEngine _runtime;

    internal AvatarMirrorDriver(IAvatarMirrorEngine runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public void Project(
        AvatarLocomotionDriver driver,
        LocomotionResult travel,
        bool concealed)
    {
        var actor = _runtime.LocateActor();
        if (actor is null)
            return;

        actor.SetPosition(travel.RenderPosition);
        actor.ParentCellId = travel.CellId;
        actor.Rotation = driver.CorpusFacing;

        uint latestLb;
        if (travel.CellId is not 0 && (travel.CellId & 0xFFFFu) >= 0x0100u)
        {
            latestLb = (travel.CellId & 0xFFFF0000u) | 0xFFFFu;
        }
        else
        {
            var locus = driver.Position;
            int lbX = _runtime.OnlineMiddleX + (int)Math.Floor(locus.X / 192f);
            int lbY = _runtime.OnlineMiddleY + (int)Math.Floor(locus.Y / 192f);
            latestLb = (uint)((lbX << 24) | (lbY << 16) | 0xFFFF);
        }

        if (driver.State == AvatarPhase.PortalSpace)
            return;

        _runtime.Rebucket(actor.ServerGuid, latestLb);
        if (concealed
            || travel.CellId is 0
            || !_runtime.IsLatestShownProj(actor))
        {
            _runtime.SuspendShade(actor);
            return;
        }

        _runtime.SynchronizeShade(actor, travel.CellId);
    }
}
