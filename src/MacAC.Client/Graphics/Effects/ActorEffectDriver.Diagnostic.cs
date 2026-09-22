using System.Numerics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics.Effects;

public sealed partial class ActorEffectDriver
{
    public Action<string>? DiagnosticSink { get; set; } = Console.WriteLine;

    public int QueuedPacketTally => _queuedBySrvOid.Values.Sum(fifo => fifo.Count);

    public int PrimedHolderTally => _liveProfiles.Count + _staticProfiles.Count;

    internal int PreviousPostureRenewHolderTourTally { get; private set; }

    public void ProcessStraight(PlayKineticsProgram msg)
    {
        if (msg.Guid is 0)
            return;
        if (TryFetchPrimedOwnIdent(msg.Guid, out uint ownIdent))
        {
            RenewOnlineMooring(msg.Guid, ownIdent);
            if (CanBeginHolder(ownIdent))
                PlayStraight(ownIdent, msg.ScriptDid);
            else if (IsWaitingForStartingExhibit(msg.Guid))
                Queue(msg.Guid, QueuedFx.Direct(msg.ScriptDid));
            return;
        }
        Queue(msg.Guid, QueuedFx.Direct(msg.ScriptDid));
    }

    public void ProcessSfx(SfxSignal msg)
    {
        if (msg.Guid is 0)
            return;
        if (MacAC.Mechanics.Sound.AudioTelemetry.SensorWireSfxListTurnedOn)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[sound-wire] recv guid=0x{msg.Guid:X8} slot=0x{msg.SoundType:X2} vol={msg.Volume:F2}"));
        }
        if (TryFetchPrimedOwnIdent(msg.Guid, out uint ownIdent))
        {
            RenewOnlineMooring(msg.Guid, ownIdent);
            if (CanBeginHolder(ownIdent))
                PlaySrvSfx(ownIdent, msg.SoundType, msg.Volume);
            else if (IsWaitingForStartingExhibit(msg.Guid))
                Queue(msg.Guid, QueuedFx.Sound(msg.SoundType, msg.Volume));
            return;
        }
        Queue(msg.Guid, QueuedFx.Sound(msg.SoundType, msg.Volume));
    }

    public void ProcessTyped(PlayKineticsProgramKind msg)
    {
        if (msg.Guid is 0)
            return;
        if (TryFetchPrimedOwnIdent(msg.Guid, out uint ownIdent))
        {
            RenewOnlineMooring(msg.Guid, ownIdent);
            if (CanBeginHolder(ownIdent))
                PlayTyped(ownIdent, msg.RawScriptType, msg.Intensity);
            else if (IsWaitingForStartingExhibit(msg.Guid))
            {
                Queue(
                    msg.Guid,
                    QueuedFx.Typed(msg.RawScriptType, msg.Intensity));
            }
            return;
        }
        Queue(msg.Guid, QueuedFx.Typed(msg.RawScriptType, msg.Intensity));
    }

    public bool ReadyOnlineActorHolder(uint srvOid)
    {
        if (!_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor
            || !capture.ResourcesRegistered
            || capture.EffectProfile is not ActorEffectProfile profile)

            return false;

        SimActorKey tag = DemandProjTag(capture);
        _readyLiveOwners.Add(tag);
        _liveProfiles[tag] = profile;
        if (capture.MaterializationResidence is
                OnlineActorMaterializationResidence.AwaitRuntimePlacement
            && !capture.IsSpatiallyProjected)
        {
            _startingExhibitBarriers.Add(tag);
        }
        else
        {
            _startingExhibitBarriers.Remove(tag);
        }
        _runner.AssignHolderMooring(actor.Id, actor.Position);
        _holderSfxChartAltered(actor.Id, profile.LatestSfxChartDid);
        return true;
    }

    public bool RerunQueuedForOnlineActor(uint srvOid)
    {
        if (!TryFetchPrimedOwnIdent(srvOid, out uint ownIdent))
            return false;
        TryRerunQueued(srvOid, ownIdent);
        return true;
    }

    public void EnrollSyntheticHolder(uint holderOwnIdent)
    {
        if (holderOwnIdent is not 0)
            _syntheticHolders.Add(holderOwnIdent);
    }

    public void WithdrawSyntheticHolder(uint holderOwnIdent) =>
        _syntheticHolders.Remove(holderOwnIdent);

    public void DropUnknownHolder(uint srvOid) =>
        _queuedBySrvOid.Remove(srvOid);

    public void WipeNetworkPhase()
    {
        _primedTagCapture.Clear();
        _primedTagCapture.AddRange(_readyLiveOwners);
        foreach (SimActorKey tag in _primedTagCapture)
        {
            _runner.HaltAllForActor(tag.LocalEntityId);
            _holderUnregistered(tag.LocalEntityId);
        }
        _readyLiveOwners.Clear();
        _liveProfiles.Clear();
        _startingExhibitBarriers.Clear();
        _queuedBySrvOid.Clear();
        _staleOnlineHolders.Clear();
        _staleOnlineHolderOrdering.Clear();
        _staleOnlineHolderCapture.Clear();
    }

    public void RefreshLiveOwnerPoses()
    {
        PreviousPostureRenewHolderTourTally = 0;
        if (_staleOnlineHolderOrdering.Count is 0)
            return;

        _staleOnlineHolderCapture.Clear();
        _staleOnlineHolderCapture.AddRange(_staleOnlineHolderOrdering);
        _staleOnlineHolderOrdering.Clear();
        _staleOnlineHolders.Clear();
        foreach (OnlineActorRecord capture in _staleOnlineHolderCapture)
        {
            if (!_onlineActors.TryFetchRecord(capture.ServerOid, out OnlineActorRecord latest)
                || !ReferenceEquals(latest, capture)
                || !TryFetchPrimedOwnIdent(capture.ServerOid, out uint ownIdent)
                || capture.WorldEntity?.Id != ownIdent)

                continue;

            ++PreviousPostureRenewHolderTourTally;
            RenewOnlineMooring(capture.ServerOid, ownIdent);
            TryRerunQueued(capture.ServerOid, ownIdent);
        }
    }

    public void StampOnlineHolderPostureStale(uint srvOid)
    {
        if (_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture))
            FlagOnlineHolderPostureStale(capture);
    }

    public bool PlayStraight(uint holderOwnIdent, uint programDid)
    {
        return !CanBeginHolder(holderOwnIdent) ? false : _runner.PlayDirect(holderOwnIdent, programDid);
    }

    public bool PlayTyped(uint holderOwnIdent, uint rawProgramKind, float intensity)
    {
        return !CanBeginHolder(holderOwnIdent) ? false : LocateAndFifoTyped(holderOwnIdent, rawProgramKind, intensity);
    }

    public bool PlayTypedFromConcealedChangeover(
        uint holderOwnIdent,
        uint rawProgramKind,
        float intensity)
        => LocateAndFifoTyped(holderOwnIdent, rawProgramKind, intensity);

    public bool PlayDefault(uint holderOwnIdent)
    {
        return !CanBeginHolder(holderOwnIdent)
            || !TryFetchProfile(holderOwnIdent, out ActorEffectProfile? profile)
            ? false
            : PlayTyped(
            holderOwnIdent,
            profile.RawDefaultProgramKind,
            profile.DefaultProgramIntensity);
    }

    public bool CanProceedHolder(uint holderOwnIdent)
    {
        if (_staticHolders.ContainsKey(holderOwnIdent) || _syntheticHolders.Contains(holderOwnIdent))
            return true;

        return _ancestorOfAffixedDescendant(holderOwnIdent) is { } ancestorOwnIdent
            ? CanProceedOnlineTrunk(ancestorOwnIdent)
            : CanProceedOnlineTrunk(holderOwnIdent);
    }

    private void RenewOnlineMooring(uint srvOid, uint ownIdent)
    {
        if (_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            && capture.WorldEntity is { } actor
            && actor.Id == ownIdent)
        {
            if (capture.ProjSort is OnlineActorMirrorKind.World)
            {
                uint earlierBroadcast = _postureBroadcastOwnIdent;
                _postureBroadcastOwnIdent = ownIdent;
                try
                {
                    _postures.RefreshTrunk(actor);
                }
                finally
                {
                    _postureBroadcastOwnIdent = earlierBroadcast;
                }
            }

            Vector3 mooring = _postures.TryFetchTrunkPosture(ownIdent, out Matrix4x4 trunkRealm)
                ? trunkRealm.Translation
                : actor.Position;
            _runner.AssignHolderMooring(ownIdent, mooring);
        }
    }

    private void FlagOnlineHolderPostureStale(OnlineActorRecord capture)
    {
        if (capture.ProjTag is not { } tag
            || !_readyLiveOwners.Contains(tag)
            || !_staleOnlineHolders.Add(capture))

            return;

        _staleOnlineHolderOrdering.Add(capture);
    }

    private void PlaySrvSfx(uint holderOwnIdent, uint sfxKind, float volume)
    {
        if (!CanBeginHolder(holderOwnIdent))
            return;

        Vector3 mooring = _postures.TryFetchTrunkPosture(holderOwnIdent, out Matrix4x4 trunkRealm)
            ? trunkRealm.Translation
            : Vector3.Zero;
        _playSrvSfx(holderOwnIdent, mooring, sfxKind, volume);
    }

    private bool LocateAndFifoTyped(
        uint holderOwnIdent,
        uint rawProgramKind,
        float intensity)
    {
        if (!TryFetchProfile(holderOwnIdent, out ActorEffectProfile? profile)
            || profile.LatestKineticsProgramChartDid is not { } chartDid)
        {
            DiagnosticSink?.Invoke(
                $"No PhysicsScriptTable for owner 0x{holderOwnIdent:X8}, type 0x{rawProgramKind:X8}.");
            return false;
        }

        uint? programDid = _charts.Resolve(
            chartDid,
            rawProgramKind,
            intensity,
            out Exception? pullMiss);
        if (programDid is not { } settled)
        {
            string specifics = pullMiss is null
                ? string.Empty
                : $" Load failed: {pullMiss.GetType().Name}: {pullMiss.Message}";
            DiagnosticSink?.Invoke(
                $"No typed KineticsProgram for owner 0x{holderOwnIdent:X8}, table 0x{chartDid:X8}, " +
                $"type 0x{rawProgramKind:X8}, intensity {intensity:R}.{specifics}");
            return false;
        }
        return _runner.PlayDirect(holderOwnIdent, settled);
    }

    private bool CanBeginHolder(uint holderOwnIdent)
    {
        if (_staticHolders.ContainsKey(holderOwnIdent) || _syntheticHolders.Contains(holderOwnIdent))
            return true;
        return _ancestorOfAffixedDescendant(holderOwnIdent) is { } ancestorOwnIdent ? IsOnlineTrunkInChamber(ancestorOwnIdent) : IsOnlineTrunkInChamber(holderOwnIdent);
    }

    private bool CanProceedOnlineTrunk(uint holderOwnIdent)
    {
        return !TryFetchOnlineTrunk(holderOwnIdent, out OnlineActorRecord capture)
            ? false
            : IsOnlineTrunkInChamber(capture)
            && (capture.FinalKineticsPhase & KineticStateFlags.Frozen) == 0;
    }

    private bool IsOnlineTrunkInChamber(uint holderOwnIdent)
    {
        return TryFetchOnlineTrunk(holderOwnIdent, out OnlineActorRecord capture)
        && IsOnlineTrunkInChamber(capture);
    }

    private static bool IsOnlineTrunkInChamber(OnlineActorRecord capture)
    {
        return capture.ResourcesRegistered
        && capture.IsSpatiallyProjected
        && capture.IsSpatiallyVisible
        && capture.WholeChamberIdent is not 0;
    }

    private bool IsWaitingForStartingExhibit(uint srvOid)
    {
        return _onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
        && capture.ProjTag is { } tag
        && _startingExhibitBarriers.Contains(tag);
    }

    private bool TryFetchOnlineTrunk(uint holderOwnIdent, out OnlineActorRecord capture)
    {
        if (_onlineActors.TryFetchCaptureByOwnActorIdent(
                holderOwnIdent,
                out capture!))

            return true;
        capture = null!;
        return false;
    }

    private bool TryFetchPrimedOwnIdent(uint srvOid, out uint ownIdent)
    {
        if (_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            && capture.ProjTag is { } tag
            && _readyLiveOwners.Contains(tag)
            && _liveProfiles.ContainsKey(tag))
        {
            ownIdent = tag.LocalEntityId;
            return true;
        }

        ownIdent = 0;
        return false;
    }

    private bool TryFetchProfile(
        uint holderOwnIdent,
        out ActorEffectProfile profile)
    {
        return _onlineActors.TryFetchCaptureByOwnActorIdent(
                holderOwnIdent,
                out OnlineActorRecord capture)
            && capture.ProjTag is { } tag
            && _liveProfiles.TryGetValue(tag, out profile!)
            ? true
            : _staticProfiles.TryGetValue(holderOwnIdent, out profile!);
    }

    private void Queue(uint srvOid, QueuedFx fx)
    {
        if (!_queuedBySrvOid.TryGetValue(srvOid, out Queue<QueuedFx>? fifo))
        {
            fifo = new Queue<QueuedFx>();
            _queuedBySrvOid.Add(srvOid, fifo);
        }
        fifo.Enqueue(fx);
    }

    private void TryRerunQueued(uint srvOid, uint ownIdent)
    {
        if (!CanBeginHolder(ownIdent)
            || !_queuedBySrvOid.Remove(srvOid, out Queue<QueuedFx>? queued))

            return;

        while (queued.Count > 0)
            Perform(ownIdent, queued.Dequeue());
    }

    private void Perform(uint ownIdent, QueuedFx fx)
    {
        switch (fx.Kind)
        {
            case QueuedFxFlavor.Direct:
                PlayStraight(ownIdent, fx.ScriptDid);
                break;
            case QueuedFxFlavor.Typed:
                PlayTyped(ownIdent, fx.RawScriptType, fx.Intensity);
                break;
            case QueuedFxFlavor.Sound:
                PlaySrvSfx(ownIdent, fx.RawScriptType, fx.Intensity);
                break;
        }
    }

    private static SimActorKey DemandProjTag(
        OnlineActorRecord capture)
    {
        return capture.ProjTag
        ?? throw new InvalidOperationException(
            $"Live entity 0x{capture.ServerOid:X8}/{capture.Generation} " +
            "has no exact projection key");
    }
}
