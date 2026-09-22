using MacAC.Client.Graphics.Effects;
using MacAC.Client.Kinetics;
using MacAC.Extensibility.World;
using MacAC.Mechanics.PluginHosting;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Realm;

namespace MacAC.Client.Realm;

internal sealed class EnginePlacementDisplaySink
    : ISimPlacementMirrorSink
{
    private readonly OnlineActorCore _onlineActors;
    private readonly SimRealmCrossingLedger _passage;
    private readonly RealmPlayPhase _realmPhase;
    private readonly RealmSignals _realmSignals;
    private readonly ActorEffectPoseRegistry _fxPostures;
    private readonly OwnAvatarShadeSyncer _ownAvatarShadeSynchronize;
    private readonly Func<uint> _ownAvatarOid;
    private readonly Action<uint> _wipePickForUnavailableActor;
    private readonly Action<OnlineActorRecord, bool>[] _visDrains;

    public EnginePlacementDisplaySink(
        OnlineActorCore liveEntities,
        SimRealmCrossingLedger transit,
        RealmPlayPhase worldState,
        RealmSignals worldEvents,
        ActorEffectPoseRegistry effectPoses,
        OwnAvatarShadeSyncer localPlayerShadowSync,
        Func<uint> localPlayerGuid,
        Action<uint> clearSelectionForUnavailableEntity,
        IEnumerable<Action<OnlineActorRecord, bool>>? visibilitySinks = null)
    {
        _onlineActors = liveEntities
            ?? throw new ArgumentNullException(nameof(liveEntities));
        _passage = transit ?? throw new ArgumentNullException(nameof(transit));
        _realmPhase = worldState ?? throw new ArgumentNullException(nameof(worldState));
        _realmSignals = worldEvents ?? throw new ArgumentNullException(nameof(worldEvents));
        _fxPostures = effectPoses
            ?? throw new ArgumentNullException(nameof(effectPoses));
        _ownAvatarShadeSynchronize = localPlayerShadowSync
            ?? throw new ArgumentNullException(nameof(localPlayerShadowSync));
        _ownAvatarOid = localPlayerGuid
            ?? throw new ArgumentNullException(nameof(localPlayerGuid));
        _wipePickForUnavailableActor = clearSelectionForUnavailableEntity
            ?? throw new ArgumentNullException(
                nameof(clearSelectionForUnavailableEntity));
        _visDrains = visibilitySinks?.ToArray()
            ?? [];
        if (_visDrains.Any(static drain => drain is null))
            throw new ArgumentException(
                "Presentation visibility sinks can't contain null",
                nameof(visibilitySinks));
    }

    public bool TryApply(in SimPlacementMirrorCapture proj)
    {
        if (proj.Kind is SimPlacementMirrorKind.ExecutorCompleted)

            return TryEnactStartingBuildWrapUp(in proj);

        if (proj.Kind
            is SimPlacementMirrorKind.WithdrawalRestored)

            return TryEnactWithdrawalRestoration(in proj);

        if (proj.Kind is SimPlacementMirrorKind.Place
            or SimPlacementMirrorKind.Withdraw
            && _onlineActors.HasEngagedStartingBuildResidence(
                proj.Token.Entity))

            return false;

        if (proj.Kind is SimPlacementMirrorKind.Place
            && !_passage.IsLatestStanceArbiter(
                proj.Token.Portal,
                proj.Token.ExactCellId))

            return true;

        if (!_onlineActors.TryEnactCoreStanceProj(in proj))
            return false;
        if (proj.Kind is SimPlacementMirrorKind.Discard)

            return true;
        return !_onlineActors.TryFetchCapture(
                proj.Token.Entity,
                out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor
            ? false
            : proj.Kind switch
            {
                SimPlacementMirrorKind.Place =>
                    TryBroadcastPlace(capture, actor),
                SimPlacementMirrorKind.Withdraw =>
                    TryBroadcastWithdrawal(capture, actor),
                _ => false,
            };
    }

    private bool TryEnactStartingBuildWrapUp(
        in SimPlacementMirrorCapture proj)
    {
        if (proj.Token.ExactCellId is 0u)
            return true;
        if (!_onlineActors.TryEnactStartingBuildWrapUpExhibit(
                in proj))

            return false;
        return !_onlineActors.TryFetchCapture(
                proj.Token.Entity,
                out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor
            ? true
            : TryBroadcastPlace(capture, actor);
    }

    private bool TryEnactWithdrawalRestoration(
        in SimPlacementMirrorCapture proj)
    {
        if (_onlineActors.TryEnactCoreStanceProj(in proj)
            && _onlineActors.TryFetchCapture(
                proj.Token.Entity,
                out OnlineActorRecord capture)
            && capture.WorldEntity is { } actor)

            _ = TryBroadcastPlace(capture, actor);
        return true;
    }

    private bool TryBroadcastPlace(OnlineActorRecord capture, RealmActor actor)
    {
        if (!IsLatest(capture, actor))
            return false;

        EntityFrame snapshot = Freeze(actor);
        _realmPhase.Add(snapshot);
        if (!IsLatest(capture, actor))
            return false;
        _realmSignals.UpsertLatest(snapshot);
        if (!IsLatest(capture, actor))
            return false;
        _fxPostures.BroadcastTriMeshRefs(actor);
        if (!IsLatest(capture, actor))
            return false;

        if (capture.ServerOid == _ownAvatarOid())
        {
            _ownAvatarShadeSynchronize.SyncPose(
                actor,
                actor.Position,
                actor.Rotation,
                capture.WholeChamberIdent,
                force: true);
        }

        for (int idx = 0; idx < _visDrains.Length; ++idx)
        {
            _visDrains[idx](capture, true);
            if (!IsLatest(capture, actor))
                return false;
        }
        return true;
    }

    private bool TryBroadcastWithdrawal(
        OnlineActorRecord capture,
        RealmActor actor)
    {
        if (!IsLatest(capture, actor))
            return false;

        for (int idx = 0; idx < _visDrains.Length; ++idx)
        {
            _visDrains[idx](capture, false);
            if (!IsLatest(capture, actor))
                return false;
        }

        _realmPhase.DropByIdent(actor.Id);
        if (!IsLatest(capture, actor))
            return false;
        _realmSignals.DropActor(actor.Id);
        if (!IsLatest(capture, actor))
            return false;
        _fxPostures.Delete(actor.Id);
        if (!IsLatest(capture, actor))
            return false;
        if (capture.ServerOid == _ownAvatarOid())

            _ownAvatarShadeSynchronize.Suspend(actor);
        if (!IsLatest(capture, actor))
            return false;
        _wipePickForUnavailableActor(capture.ServerOid);
        return IsLatest(capture, actor);
    }

    private bool IsLatest(OnlineActorRecord capture, RealmActor actor)
    {
        return _onlineActors.TryFetchCapture(
            capture.ProjTag!.Value,
            out OnlineActorRecord latest)
        && ReferenceEquals(latest, capture)
        && ReferenceEquals(latest.WorldEntity, actor);
    }

    private static EntityFrame Freeze(RealmActor actor)
    {
        return new(
        actor.Id,
        actor.SrcGfxObjRefOrRigIdent,
        actor.Position,
        actor.Rotation);
    }
}
