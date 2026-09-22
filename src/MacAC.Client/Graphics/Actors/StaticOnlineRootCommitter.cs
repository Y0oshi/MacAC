using MacAC.Client.Graphics.Effects;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed class StaticOnlineRootCommitter(
    IOnlineActorEngineSource runtime,
    ProxyRegistry shadows,
    OnlineRealmOriginLedger origin,
    ActorEffectPoseRegistry effectPoses)
{
    private readonly IOnlineActorEngineSource _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly ProxyRegistry _shades = shadows ?? throw new ArgumentNullException(nameof(shadows));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly ActorEffectPoseRegistry _fxPostures = effectPoses ?? throw new ArgumentNullException(nameof(effectPoses));

    public bool Seal(RealmActor actor, KineticBody corpus)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(corpus);
        var core = _runtime.Current;
        if (core is null
            || !core.TryFetchRecord(actor.ServerGuid, out OnlineActorRecord capture)
            || !ReferenceEquals(capture.WorldEntity, actor)
            || !ReferenceEquals(capture.KineticBody, corpus))

            return false;

        _fxPostures.RefreshTrunk(actor);

        if (!core.TryFetchRecord(actor.ServerGuid, out OnlineActorRecord latest)
            || !ReferenceEquals(latest, capture)
            || !ReferenceEquals(latest.WorldEntity, actor)
            || !ReferenceEquals(latest.KineticBody, corpus))

            return false;

        if (capture.ProjSort is not OnlineActorMirrorKind.World
            || !capture.IsSpatiallyProjected
            || !capture.IsSpatiallyVisible
            || core.IsHidden(actor.ServerGuid))

            return true;

        uint chamberIdent = corpus.CellPosition.ObjCellId is not 0
            ? corpus.CellPosition.ObjCellId
            : capture.WholeChamberIdent;
        ProxyPositionSynchronizer.Sync(
            _shades,
            actor.Id,
            corpus.Position,
            corpus.Orientation,
            chamberIdent,
            _origin.CenterX,
            _origin.CenterY);
        return true;
    }
}
