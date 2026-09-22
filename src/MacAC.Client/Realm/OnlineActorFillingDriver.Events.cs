using MacAC.Client.Graphics;
using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Realm;

internal sealed partial class OnlineActorFillingDriver
{
    public void OnCreate(RealmSession.MoverSpawn summon)
    {
        lock (_datMutex)
        {
            var enrollment =
                _runtime.EnrollOnlineActor(
                    summon,
                    isOwnAvatar: summon.Guid == _identity.SrvOid);
            var outcome = enrollment.Inbound;
            if (outcome.Disposition is
                MacAC.Mechanics.Kinetics.SpawnStampVerdict.StaleGeneration)

                return;

            if (enrollment.Canonical is not { } canon)
                return;

            ulong buildIntegrationVer = canon.BuildIntegrationVersion;

            try
            {
                _timestamps.Publish(summon.Guid, outcome.Timestamps);
                if (_runtime.IsLatestBuildIntegration(
                        canon,
                        buildIntegrationVer)
                    && _entityObjects.ImposeApprovedSummon(
                        canon,
                        buildIntegrationVer,
                        summon,
                        replaceGen: outcome.Disposition is
                                MacAC.Mechanics.Kinetics.SpawnStampVerdict.NewGeneration)
                    && _runtime.IsLatestBuildIntegration(
                        canon,
                        buildIntegrationVer))
                {
                    if (outcome.Disposition is
                        MacAC.Mechanics.Kinetics.SpawnStampVerdict.ExistingGeneration)
                    {
                        if (enrollment.LogicalRegistrationCreated)
                        {
                            ProjectPrecise(
                                canon,
                                outcome.Snapshot,
                                OnlineMirrorPurpose.LogicalRegistration,
                                anticipatedBuildIntegrationVer:
                                    buildIntegrationVer);
                        }
                        else
                        {
                            if (outcome.SameGenerationEvents is { } renew)
                                _networkUpdates.ImposeSameGen(renew);

                            if (_runtime.IsLatestBuildIntegration(
                                    canon,
                                    buildIntegrationVer))
                            {
                                _runtime.TryFetchProj(
                                    canon,
                                    out OnlineActorRecord? proj);
                                if (proj is not null
                                    && !proj.BuildProjSynchronizationQueued
                                    && proj.StartingHydrationFinished)
                                {
                                    goto AppearanceSynchronization;
                                }
                                ProjectPrecise(
                                    canon,
                                    canon.Snapshot,
                                    proj?.BuildProjSynchronizationQueued
                                        is true
                                        ? OnlineMirrorPurpose.CreateSupersessionRecovery
                                        : OnlineMirrorPurpose.SpatialRecovery,
                                    anticipatedBuildIntegrationVer:
                                        buildIntegrationVer);
                            }
                        }
                    }
                    else
                    {
                        ProjectPrecise(
                            canon,
                            outcome.Snapshot,
                            OnlineMirrorPurpose.LogicalRegistration,
                            anticipatedBuildIntegrationVer:
                                buildIntegrationVer);
                    }

                AppearanceSynchronization:
                    if (_runtime.TryFetchProj(
                            canon,
                            out OnlineActorRecord? latestProj)
                        && latestProj.LooksProjSynchronizationQueued)
                    {
                        SynchronizeLooks(
                            latestProj,
                            latestProj.ObjRefDscArbiterVer);
                    }
                }
            }
            catch (Exception enactMiss)
                when (enrollment.PriorGenerationCleanupFailure is not null)
            {
                throw new AggregateException(
                    $"Live entity 0x{summon.Guid:X8} replacement cleanup and installation both failed",
                    enrollment.PriorGenerationCleanupFailure,
                    enactMiss);
            }

            if (enrollment.PriorGenerationCleanupFailure is { } tidyMiss)
            {
                throw new AggregateException(
                    $"Prior incarnation of live entity 0x{summon.Guid:X8} failed teardown after its replacement was installed",
                    tidyMiss);
            }

            _leadListing?.DriveAll();
            // C4 route 2: same pump for a parked ForcePosition
            _approvedLocusSteer?.Advance();
        }
    }

    public bool OnLooks(ObjDescNotice.Parsed refresh)
    {
        lock (_datMutex)
        {
            if (!_runtime.TryEnactObjRefDsc(refresh, out _))

                return false;

            return !_runtime.TryFetchRecord(
                    refresh.Guid,
                    out OnlineActorRecord capture)
                ? _runtime.TryFetchCanon(
                        refresh.Guid,
                        out SimActorRecord canon)
                    && ProjectPrecise(
                        canon,
                        canon.Snapshot,
                        OnlineMirrorPurpose.SpatialRecovery,
                        canon.BuildIntegrationVersion)
                : SynchronizeLooks(
                capture,
                capture.ObjRefDscArbiterVer);
        }
    }

    public bool OnLift(PickupNotice.Parsed refresh)
    {
        if (!_runtime.TryEnactLift(refresh, out _))
            return false;

        var disposition =
            _relationships.OnChildBecameUnparented(refresh.Guid);
        bool approved = disposition is not DescendantUnparentDisposition.Superseded;
        return approved;
    }

    public bool OnErase(ObjectDeletion.Parsed erase) => _deletion.Delete(erase);

    public bool OnPrune(OnlineActorPruneCandidate contender) =>
        _deletion.Prune(contender);

    public void OnParent(AncestorSignal.Parsed refresh) =>
        _relationships.OnAncestor(refresh);

    // Reprojects retained live objects after their landblock is loaded
    public void OnLbLoaded(uint fetchedLbIdent)
    {
        if (_runtime.Count is 0)
            return;

        uint canonLb =
            (fetchedLbIdent & 0xFFFF0000u) | 0xFFFFu;
        List<SimActorRecord> contenders = new List<SimActorRecord>();
        foreach (SimActorRecord contender in _runtime.CanonRecords)
        {
            _runtime.TryFetchProj(
                contender,
                out OnlineActorRecord? proj);
            if (contender.ServerGuid == _identity.SrvOid
                && proj is not null
                && proj.StartingHydrationFinished
                && !proj.BuildProjSynchronizationQueued
                && !proj.LooksProjSynchronizationQueued)

                continue;

            if (_runtime.AncestorAffixes.HasSealedAncestor(contender.ServerGuid))
                continue;

            uint projChamberIdent = proj?.ProjChamberIdent
                ?? contender.Snapshot.Position?.LandblockId
                ?? contender.WholeChamberTag;
            if (projChamberIdent is not 0
                && ((projChamberIdent & 0xFFFF0000u) | 0xFFFFu)
                    == canonLb
                && contender.Snapshot.SetupTableId is not null)

                contenders.Add(contender);
        }
        if (contenders.Count is 0)
            return;

        lock (_datMutex)
        {
            foreach (SimActorRecord contender in contenders)
            {
                if (!_runtime.IsLatestCanon(contender))
                    continue;

                _runtime.TryFetchProj(
                    contender,
                    out OnlineActorRecord? capture);
                ulong projectedObjRefDscVer =
                    contender.ObjRefDscArbiterVer;
                bool projectedCanonLooks = false;

                if (capture?.BuildProjSynchronizationQueued is true)
                {
                    if (ProjectPrecise(
                            contender,
                            contender.Snapshot,
                            OnlineMirrorPurpose.CreateSupersessionRecovery))

                        projectedCanonLooks = true;
                }
                else if (capture?.WorldEntity is not null
                    && capture.StartingHydrationFinished)
                {
                    _runtime.RebucketLiveEntity(
                        capture.ServerOid,
                        capture.ProjChamberIdent);
                }
                else if (ProjectPrecise(
                    contender,
                    contender.Snapshot,
                    OnlineMirrorPurpose.SpatialRecovery))
                {
                    projectedCanonLooks = true;
                }

                _runtime.TryFetchProj(contender, out capture);
                if (capture is not null
                    && projectedCanonLooks
                    && capture.LooksProjSynchronizationQueued)
                {
                    ConcludeLooksProjSynchronization(
                        capture,
                        projectedObjRefDscVer);
                }

                if (capture is not null
                    && _runtime.IsLatestCapture(capture)
                    && capture.LooksProjSynchronizationQueued)
                {
                    SynchronizeLooks(
                        capture,
                        capture.ObjRefDscArbiterVer);
                }
            }
        }
    }

    public bool OnActorReady(OnlineActorReadyCandidate contender)
    {
        if (!BroadcastPrimed(contender))

            return false;

        if (contender.Record.LooksProjSynchronizationQueued
            && !contender.Record.LooksHydrationInHeadway)
        {
            ConcludeLooksProjSynchronization(
                contender.Record,
                contender.ObjDescAuthorityVersion);
        }

        return contender.IsLatest(_runtime)
            && (!contender.Record.BuildProjSynchronizationQueued
            || _runtime.TryDoneBuildProjSynchronization(
                contender.Record,
                contender.CreateIntegrationVersion));
    }

    internal bool OnBuildAncestorApproved(CreateAnchorUpdate refresh)
    {
        if (!_runtime.TryEnactBuildAncestor(refresh, out _))
            return false;
        _relationships.OnBuildParentAccepted(refresh);
        return true;
    }
}
