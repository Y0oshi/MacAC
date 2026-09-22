using MacAC.Client.Graphics;
using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Realm;

internal sealed partial class OnlineActorFillingDriver
{
    public int RetryQueuedTeardowns() => _runtime.ReattemptPendingTeardowns();

    public bool SecureRealmOrigin(
        OnlineActorRecord anticipatedCapture,
        ulong locusArbiterVer,
        RealmSession.MoverSpawn approvedSummon)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        lock (_datMutex)
        {
            if (!_runtime.IsLatestLocusArbiter(
                    anticipatedCapture,
                    locusArbiterVer))

                return false;

            BootstrapOriginAndRecoverFetched(approvedSummon);
            return _runtime.IsLatestLocusArbiter(
                anticipatedCapture,
                locusArbiterVer);
        }
    }

    public bool ReclaimProj(
        OnlineActorRecord anticipatedCapture,
        ulong locusArbiterVer,
        RealmSession.MoverSpawn approvedSummon)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        lock (_datMutex)
        {
            if (!_runtime.IsLatestLocusArbiter(
                    anticipatedCapture,
                    locusArbiterVer))

                return false;

            ulong projectedObjRefDscVer = anticipatedCapture.ObjRefDscArbiterVer;
            bool projected = ProjectPrecise(
                    anticipatedCapture,
                    approvedSummon,
                    anticipatedCapture.BuildProjSynchronizationQueued
                        ? OnlineMirrorPurpose.CreateSupersessionRecovery
                        : OnlineMirrorPurpose.SpatialRecovery)
                && _runtime.IsLatestLocusArbiter(
                    anticipatedCapture,
                    locusArbiterVer);
            if (!projected)
                return false;

            if (anticipatedCapture.LooksProjSynchronizationQueued)
            {
                if (_runtime.IsLatestObjRefDscArbiter(
                        anticipatedCapture,
                        projectedObjRefDscVer))
                {
                    ConcludeLooksProjSynchronization(
                        anticipatedCapture,
                        projectedObjRefDscVer);
                }
                else if (!SynchronizeLooks(
                    anticipatedCapture,
                    anticipatedCapture.ObjRefDscArbiterVer))
                {
                    return false;
                }
            }

            return _runtime.IsLatestLocusArbiter(
                anticipatedCapture,
                locusArbiterVer);
        }
    }

    public bool ReclaimCanonProj(
        SimActorRecord anticipatedCanon,
        ulong locusArbiterVer,
        out OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCanon);
        lock (_datMutex)
        {
            capture = null!;
            if (!_runtime.IsLatestLocusArbiter(
                    anticipatedCanon,
                    locusArbiterVer))

                return false;

            if (_runtime.TryFetchProj(
                    anticipatedCanon,
                    out capture))
            {
                return _runtime.IsLatestLocusArbiter(
                    capture,
                    locusArbiterVer);
            }

            var canonSummon =
                anticipatedCanon.Snapshot;
            if (canonSummon.Position is null)
                return false;

            BootstrapOriginAndRecoverFetched(canonSummon);
            if (!_runtime.IsLatestLocusArbiter(
                    anticipatedCanon,
                    locusArbiterVer)
                || !ProjectPrecise(
                    anticipatedCanon,
                    canonSummon,
                    OnlineMirrorPurpose.SpatialRecovery,
                    anticipatedCanon.BuildIntegrationVersion)
                || !_runtime.TryFetchProj(
                    anticipatedCanon,
                    out capture))
            {
                capture = null!;
                return false;
            }

            return _runtime.IsLatestLocusArbiter(
                capture,
                locusArbiterVer);
        }
    }

    public void RestartSessCondition() => _materializer.RestartSessPhase();

    internal bool TryAdmitAncestorForProj(AncestorSignal.Parsed refresh) =>
        _runtime.TryEnactAncestor(refresh, out _);

    private bool SynchronizeLooks(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedObjRefDscArbiterVer)
    {
        while (true)
        {
            if (!_runtime.TryCommenceLooksHydration(
                    anticipatedCapture,
                    anticipatedObjRefDscArbiterVer))

                return false;

            bool reattempt;
            bool published;
            try
            {
                if (anticipatedCapture.ProjSort is
                    OnlineActorMirrorKind.Attached)
                {
                    published = _relationships.TryEnactAffixedLooks(
                        anticipatedCapture,
                        anticipatedObjRefDscArbiterVer);
                }
                else
                {
                    var looksPhase =
                        OnlineActorAppearanceWiring.Capture(
                            _runtime,
                            anticipatedCapture.ServerOid);
                    published = looksPhase is null
                        ? ProjectPrecise(
                            anticipatedCapture,
                            anticipatedCapture.Snapshot,
                            OnlineMirrorPurpose.SpatialRecovery)
                        : _materializer.TryMaterialize(
                            anticipatedCapture.Canonical,
                            anticipatedCapture.Snapshot,
                            OnlineMirrorPurpose.AppearanceMutation,
                            anticipatedCapture.BuildIntegrationVer,
                            looksPhase);
                }
            }
            finally
            {
                reattempt = _runtime.FinishLooksHydration(anticipatedCapture);
            }

            if (reattempt && _runtime.IsLatestCapture(anticipatedCapture))
            {
                anticipatedObjRefDscArbiterVer =
                    anticipatedCapture.ObjRefDscArbiterVer;
                continue;
            }

            return published
                && ConcludeLooksProjSynchronization(
                    anticipatedCapture,
                    anticipatedObjRefDscArbiterVer);
        }
    }

    private void BootstrapOriginAndRecoverFetched(
        RealmSession.MoverSpawn canonSummon)
    {
        var initialization =
            _origin.TryBootstrap(canonSummon);
        if (!initialization.IsKnown)
            return;

        for (int idx = 0; idx < initialization.AlreadyLoadedLandblocks.Count; ++idx)
            OnLbLoaded(initialization.AlreadyLoadedLandblocks[idx]);
    }

    private bool BroadcastPrimed(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedBuildIntegrationVer)
    {
        return !_runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            ? false
            : BroadcastPrimed(OnlineActorReadyCandidate.Capture(anticipatedCapture));
    }

    private bool BroadcastPrimed(OnlineActorReadyCandidate contender)
    {
        var anticipatedCapture = contender.Record;
        return !contender.IsLatest(_runtime)
            || !_primed.Publish(contender)
            || !contender.IsLatest(_runtime)
            ? false
            : _runtime.TryFlagStartingHydrationFinished(
                anticipatedCapture,
                contender.CreateIntegrationVersion)
            && contender.IsLatest(_runtime);
    }

    private bool ConcludeLooksProjSynchronization(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedObjRefDscArbiterVer)
    {
        if (!_runtime.TryDoneLooksProjSynchronization(
                anticipatedCapture,
                anticipatedObjRefDscArbiterVer))

            return false;

        AppearanceApplied?.Invoke(anticipatedCapture.ServerOid);
        return _runtime.IsLatestObjRefDscArbiter(
            anticipatedCapture,
            anticipatedObjRefDscArbiterVer);
    }
}
