using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

public sealed partial class SimKineticsLedger
{
    public void AcknowledgeSpatialProj(
        SimActorRecord capture,
        bool spatial)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag)
        {
            if (spatial)
            {
                throw new InvalidOperationException(
                    $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} can't enter the physics workset without a local identity");
            }
            return;
        }

        if (spatial && Entities.IsCurrent(capture))
        {
            _trunks[tag] = capture;
            if (capture.PeerMotion is { } distant)
                _remotes[tag] = distant;
            else
                _remotes.Remove(tag);
            if (capture.Projectile is { } missile)
                _missiles[tag] = missile;
            else
                _missiles.Remove(tag);
            return;
        }

        DropSpatialProj(capture);
    }

    public void DropSpatialProj(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag)
            return;

        if (_trunks.TryGetValue(tag, out SimActorRecord? trunk)
            && ReferenceEquals(trunk, capture))

            _trunks.Remove(tag);
        DiscardDistant(capture);
        DiscardMissile(capture);
    }

    public bool IsSpatialTrunk(SimActorRecord capture)
    {
        return capture.Key is { } tag
        && Entities.IsCurrent(capture)
        && _trunks.TryGetValue(tag, out SimActorRecord? indexed)
        && ReferenceEquals(indexed, capture);
    }

    public bool IsSpatialDistant(
        SimActorRecord capture,
        ISimPeerMotion distant)
    {
        return IsSpatialTrunk(capture)
        && ReferenceEquals(capture.PeerMotion, distant)
        && capture.Key is { } tag
        && _remotes.TryGetValue(tag, out ISimPeerMotion? indexed)
        && ReferenceEquals(indexed, distant);
    }

    public bool IsSpatialMissile(
        SimActorRecord capture,
        ISimMissile missile)
    {
        return IsSpatialTrunk(capture)
        && ReferenceEquals(capture.Projectile, missile)
        && capture.Key is { } tag
        && _missiles.TryGetValue(tag, out ISimMissile? indexed)
        && ReferenceEquals(indexed, missile);
    }

    public void DuplicateSpatialTrunksTo(List<SimActorRecord> dest)
    {
        Live();
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach ((SimActorKey tag, SimActorRecord capture)
                 in _trunks)
        {
            if (capture.Key == tag
                && Entities.IsCurrent(capture))

                dest.Add(capture);
        }
    }

    public void DuplicateSpatialRemotesTo(List<SimActorRecord> dest)
    {
        Live();
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach ((SimActorKey tag, ISimPeerMotion distant)
                 in _remotes)
        {
            if (Entities.TryFetchByOwnTag(
                    tag.LocalEntityId,
                    out SimActorRecord capture)
                && capture.Key == tag
                && ReferenceEquals(capture.PeerMotion, distant)
                && IsSpatialTrunk(capture))

                dest.Add(capture);
        }
    }

    public void DuplicateSpatialMissilesTo(List<SimActorRecord> dest)
    {
        Live();
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach ((SimActorKey tag, ISimMissile missile)
                 in _missiles)
        {
            if (Entities.TryFetchByOwnTag(
                    tag.LocalEntityId,
                    out SimActorRecord capture)
                && capture.Key == tag
                && ReferenceEquals(capture.Projectile, missile)
                && IsSpatialTrunk(capture))

                dest.Add(capture);
        }
    }

    public void WipeSpatialWorksets()
    {
        Live();
        _remotes.Clear();
        _missiles.Clear();
        _trunks.Clear();
    }

    internal bool SealPlainChamber(
        SimActorRecord capture,
        KineticBody corpus,
        ulong objectTimerEpoch,
        uint wholeChamberIdent,
        Func<bool>? externalHolderValid)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        bool IsPreciseHolder() =>
            IsSpatialTrunk(capture)
            && capture.ObjectTimerEpoch == objectTimerEpoch
            && ReferenceEquals(capture.KineticBody, corpus)
            && capture.PeerMotion is null
            && (externalHolderValid?.Invoke() ?? true);
        return SealChamber(
            capture,
            wholeChamberIdent,
            IsPreciseHolder);
    }

    internal bool SealMissileChamber(
        SimActorRecord capture,
        ISimMissile missile,
        ulong predictionArbiterVer,
        uint wholeChamberIdent,
        Func<bool>? externalHolderValid)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(missile);
        bool IsPreciseHolder() =>
            Entities.IsCurrent(capture)
            && ReferenceEquals(capture.Projectile, missile)
            && ReferenceEquals(capture.KineticBody, missile.Body)
            && missile.PredictionArbiterVer
                == predictionArbiterVer
            && (externalHolderValid?.Invoke() ?? true);
        return SealChamber(
            capture,
            wholeChamberIdent,
            IsPreciseHolder);
    }

    internal void RestartSessKinetics()
    {
        Live();
        foreach ((_, StagedLandblockContactEpoch readied) in
                 _linedLinkEpochs)
        {
            readied.Dispose();
        }
        _linedLinkEpochs.Clear();
        _stemEdits.Clear();
        _linkIntakes.Clear();
        SetPosition.RestartSess();
        ImpactDossiers.RestartSess();
        _cycleMiddleLb = 0u;
        _avatarBuildObserved = false;
        BumpLinkRealmArbiter();
        Volatile.Write(ref _linkThreadIdent, 0);
    }

    private void SealChamber(
        SimActorRecord capture,
        ISimPeerMotion anticipatedCore,
        uint wholeChamberIdent)
    {
        _ = SealChamber(
            capture,
            wholeChamberIdent,
            () => Entities.IsCurrent(capture)
                && ReferenceEquals(capture.PeerMotion, anticipatedCore));
    }

    private bool SealChamber(
        SimActorRecord capture,
        uint wholeChamberIdent,
        Func<bool> preciseHolderValid)
    {
        if (wholeChamberIdent is 0 || !preciseHolderValid())
            return false;
        if (wholeChamberIdent == capture.WholeChamberTag)
            return true;
        uint earlierChamberIdent = capture.WholeChamberTag;
        Entities.AssignWholeChamber(
            capture,
            wholeChamberIdent,
            (wholeChamberIdent & 0xFFFF0000u) | 0xFFFFu);
        CellCommitted?.Invoke(
            new SimKineticsCellCommit(
                capture,
                earlierChamberIdent,
                wholeChamberIdent,
                capture.SpatialAuthorityVersion));
        return preciseHolderValid()
            && capture.WholeChamberTag == wholeChamberIdent;
    }

    private void DiscardDistant(SimActorRecord capture)
    {
        if (capture.Key is not { } tag)
            return;
        if (_remotes.TryGetValue(
                tag,
                out ISimPeerMotion? distant)
            && (capture.PeerMotion is null
                || ReferenceEquals(distant, capture.PeerMotion)
                || !Entities.IsCurrent(capture)))

            _remotes.Remove(tag);
    }

    private void DiscardMissile(SimActorRecord capture)
    {
        if (capture.Key is not { } tag)
            return;
        if (_missiles.TryGetValue(
                tag,
                out ISimMissile? missile)
            && (capture.Projectile is null
                || ReferenceEquals(missile, capture.Projectile)
                || !Entities.IsCurrent(capture)))

            _missiles.Remove(tag);
    }
}
