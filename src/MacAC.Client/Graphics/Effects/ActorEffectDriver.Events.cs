using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Effects;

public sealed partial class ActorEffectDriver
{
    public bool OnLiveActorReady(uint srvOid)
    {
        if (!ReadyOnlineActorHolder(srvOid))
            return false;
        RerunQueuedForOnlineActor(srvOid);
        return true;
    }

    public bool OnExhibitTied(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.ProjTag is not { } tag
            || !_onlineActors.TryFetchCapture(tag, out OnlineActorRecord latest)
            || !ReferenceEquals(latest, capture))

            return false;

        _startingExhibitBarriers.Remove(tag);
        if (!TryFetchPrimedOwnIdent(capture.ServerOid, out uint ownIdent))
            return true;

        RenewOnlineMooring(capture.ServerOid, ownIdent);
        TryRerunQueued(capture.ServerOid, ownIdent);
        return true;
    }

    public bool OnLiveActorDescriptionChanged(uint srvOid)
    {
        if (!TryFetchPrimedOwnIdent(srvOid, out uint ownIdent)
            || !_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.EffectProfile is not ActorEffectProfile profile)

            return false;

        _liveProfiles[DemandProjTag(capture)] = profile;
        _holderSfxChartAltered(ownIdent, profile.LatestSfxChartDid);
        return true;
    }

    public void OnDatStaticActorPrimed(
        uint holderOwnIdent,
        RealmActor actor,
        ActorEffectProfile profile)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(profile);
        if (holderOwnIdent is 0)
            return;
        _staticHolders[holderOwnIdent] = actor;
        _staticProfiles[holderOwnIdent] = profile;
        _runner.AssignHolderMooring(holderOwnIdent, actor.Position);
        _holderSfxChartAltered(holderOwnIdent, profile.LatestSfxChartDid);
    }

    public void OnDatStaticActorRemoved(uint ownIdent)
    {
        if (!_staticHolders.Remove(ownIdent))
            return;
        _staticProfiles.Remove(ownIdent);
        _runner.HaltAllForActor(ownIdent);
        _holderUnregistered(ownIdent);
    }

    public void OnLiveActorUnregistered(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!_onlineActors.TryFetchRecord(capture.ServerOid, out OnlineActorRecord latest)
            || ReferenceEquals(latest, capture))

            _queuedBySrvOid.Remove(capture.ServerOid);
        if (capture.ProjTag is { } tag)
        {
            _readyLiveOwners.Remove(tag);
            _liveProfiles.Remove(tag);
            _startingExhibitBarriers.Remove(tag);
        }
        if (capture.OwnActorIdent is not { } ownIdent)
            return;
        _runner.HaltAllForActor(ownIdent);
        _holderUnregistered(ownIdent);
    }

    public void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue tap)
    {
        _runner.AssignHolderMooring(actorIdent, actorRealmLocus);
        switch (tap)
        {
            case CallEffectCue call:
                if (CanBeginHolder(actorIdent))
                    _runner.PlanCallPes(actorIdent, call.EffectId, call.Pause);
                break;
            case DefaultScriptCue:
                PlayDefault(actorIdent);
                break;
            case DefaultScriptPartCue piece:
                if (_descendantAtPiece(actorIdent, piece.PartIndex) is { } descendantOwnIdent)
                    PlayDefault(descendantOwnIdent);
                break;
        }
    }

    private void OnFxPostureAltered(uint ownIdent)
    {
        if (_postureBroadcastOwnIdent == ownIdent)
            return;
        if (_onlineActors.TryFetchCaptureByOwnActorIdent(
                ownIdent,
                out OnlineActorRecord capture))

            FlagOnlineHolderPostureStale(capture);
    }

    private void OnProjVisAltered(OnlineActorRecord capture, bool shown)
    {
        if (shown)
            FlagOnlineHolderPostureStale(capture);
    }
}
