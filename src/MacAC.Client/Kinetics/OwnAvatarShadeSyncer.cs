using MacAC.Client.Controls;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Kinetics;

internal sealed class OwnAvatarShadeSyncer(
    KineticEngine physics,
    OnlineActorCore liveEntities,
    IAvatarIdentitySource identity,
    OnlineRealmOriginLedger origin,
    AvatarShadeLedger state)
{
    private readonly KineticEngine _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly AvatarShadeLedger _phase = state ?? throw new ArgumentNullException(nameof(state));

    public void Sync(RealmActor avatarActor, uint chamberIdent, bool force = false)
    {
        SyncPose(
                avatarActor,
                avatarActor.Position,
                avatarActor.Rotation,
                chamberIdent,
                force);
    }

    public AvatarShadeLedger.ClientSnapshot? Capture() => _phase.Current;

    public void SyncPose(
        RealmActor avatarActor,
        System.Numerics.Vector3 locus,
        System.Numerics.Quaternion facing,
        uint chamberIdent,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(avatarActor);
        if (_onlineActors.IsHidden(_identity.SrvOid)
            || chamberIdent is 0
            || !IsLatestVisibleProjection(avatarActor))
        {
            Suspend(avatarActor);
            return;
        }

        if (!force
            && _phase.Current is { } previous
            && previous.CellId == chamberIdent
            && System.Numerics.Vector3.DistanceSquared(
                previous.Position,
                locus) <= 1e-4f
            && MathF.Abs(System.Numerics.Quaternion.Dot(
                previous.Orientation,
                facing)) >= 0.99999f)

            return;

        ProxyPositionSynchronizer.Sync(
            _physics.ShadeObjects,
            avatarActor.Id,
            locus,
            facing,
            chamberIdent,
            _origin.CenterX,
            _origin.CenterY);
        _phase.Set(locus, facing, chamberIdent);
    }

    public void Restore(
        RealmActor avatarActor,
        AvatarShadeLedger.ClientSnapshot? capture)
    {
        ArgumentNullException.ThrowIfNull(avatarActor);
        if (capture is not { } preceding || !IsLatestVisibleProjection(avatarActor))
        {
            Suspend(avatarActor);
            return;
        }

        SyncPose(
            avatarActor,
            preceding.Position,
            preceding.Orientation,
            preceding.CellId,
            force: true);
    }

    public bool IsLatestVisibleProjection(RealmActor avatarActor)
    {
        return _onlineActors.TryFetchRecord(avatarActor.ServerGuid, out OnlineActorRecord capture)
        && ReferenceEquals(capture.WorldEntity, avatarActor)
        && _onlineActors.IsLatestSpatialTrunkObject(capture);
    }

    public void Suspend(RealmActor avatarActor)
    {
        ArgumentNullException.ThrowIfNull(avatarActor);
        _physics.ShadeObjects.Suspend(avatarActor.Id);
        _phase.Clear();
    }

    public void ResetSession() => _phase.Clear();
}
