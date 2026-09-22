using MacAC.Sim.Actors;
using MacAC.Wire;

namespace MacAC.Client.Realm;

internal sealed partial class OnlineActorFillingDriver
{
    private bool ProjectPrecise(
        OnlineActorRecord anticipatedCapture,
        RealmSession.MoverSpawn approvedSummon,
        OnlineMirrorPurpose purpose,
        ulong? anticipatedBuildIntegrationVer = null)
    {
        return ProjectPrecise(
            anticipatedCapture.Canonical,
            approvedSummon,
            purpose,
            anticipatedBuildIntegrationVer);
    }

    private bool ProjectPrecise(
        SimActorRecord anticipatedCanon,
        RealmSession.MoverSpawn approvedSummon,
        OnlineMirrorPurpose purpose,
        ulong? anticipatedBuildIntegrationVer = null)
    {
        if (_projectionOperations.TryGetValue(
                anticipatedCanon,
                out CanonicalMirrorOperation? engaged))
        {
            if (anticipatedCanon.BuildIntegrationVersion
                != engaged.BuildIntegrationVer)

                engaged.ReattemptAsked = true;
            return false;
        }

        CanonicalMirrorOperation op = new CanonicalMirrorOperation();
        _projectionOperations.Add(anticipatedCanon, op);
        try
        {
            while (true)
            {
                ulong buildIntegrationVer = anticipatedBuildIntegrationVer
                    ?? anticipatedCanon.BuildIntegrationVersion;
                if (!_runtime.IsLatestBuildIntegration(
                        anticipatedCanon,
                        buildIntegrationVer))

                    return false;
                if (purpose is OnlineMirrorPurpose.CreateSupersessionRecovery
                    && _runtime.TryFetchProj(
                        anticipatedCanon,
                        out OnlineActorRecord? synchronizedProj)
                    && !_runtime.TryFlagBuildProjSynchronizationQueued(
                        synchronizedProj,
                        buildIntegrationVer))

                    return false;

                op.ReattemptAsked = false;
                op.BuildIntegrationVer =
                    buildIntegrationVer;
                bool outcome = ProjectPreciseOnce(
                    anticipatedCanon,
                    approvedSummon,
                    purpose,
                    buildIntegrationVer);

                bool reattempt = op.ReattemptAsked
                    || (_runtime.IsLatestCanon(anticipatedCanon)
                        && anticipatedCanon.BuildIntegrationVersion
                            != buildIntegrationVer);
                if (!reattempt || !_runtime.IsLatestCanon(anticipatedCanon))
                    return outcome;

                if (_runtime.TryFetchProj(
                        anticipatedCanon,
                        out OnlineActorRecord? reattemptProj))

                    reattemptProj.BuildProjSynchronizationQueued = true;
                approvedSummon = anticipatedCanon.Snapshot;
                purpose = OnlineMirrorPurpose.CreateSupersessionRecovery;
                anticipatedBuildIntegrationVer =
                    anticipatedCanon.BuildIntegrationVersion;
            }
        }
        catch
        {
            if (_runtime.IsLatestCanon(anticipatedCanon)
                && _runtime.TryFetchProj(
                    anticipatedCanon,
                    out OnlineActorRecord? interruptedProj))

                interruptedProj.BuildProjSynchronizationQueued = true;
            throw;
        }
        finally
        {
            if (!_projectionOperations.Remove(anticipatedCanon, out var removed)
                || !ReferenceEquals(removed, op))
            {
                throw new InvalidOperationException(
                    "Canonical projection construction ownership was corrupted");
            }
        }
    }

    private bool ProjectPreciseOnce(
        SimActorRecord anticipatedCanon,
        RealmSession.MoverSpawn approvedSummon,
        OnlineMirrorPurpose purpose,
        ulong buildIntegrationVer)
    {
        _relationships.OnSpawn(approvedSummon);
        if (!_runtime.IsLatestBuildIntegration(
                anticipatedCanon,
                buildIntegrationVer))
            return false;

        var canonSummon = anticipatedCanon.Snapshot;
        BootstrapOriginAndRecoverFetched(canonSummon);
        if (!_runtime.IsLatestBuildIntegration(
                anticipatedCanon,
                buildIntegrationVer)
            || !_origin.IsKnown)

            return false;

        _runtime.TryFetchProj(
            anticipatedCanon,
            out OnlineActorRecord? anticipatedCapture);

        if (canonSummon.Position is null)
        {
            if (canonSummon.ParentGuid is not null and not 0)
            {
                if (anticipatedCapture is null)
                    return false;

                if (anticipatedCapture.StartingHydrationFinished
                    && !anticipatedCapture.BuildProjSynchronizationQueued)
                {
                    return _runtime.IsLatestBuildIntegration(
                        anticipatedCapture,
                        buildIntegrationVer);
                }

                return anticipatedCapture.WorldEntity is null
                    || anticipatedCapture.ProjSort is not
                        OnlineActorMirrorKind.Attached
                    || !anticipatedCapture.IsSpatiallyProjected
                    || !BroadcastPrimed(
                        anticipatedCapture,
                        buildIntegrationVer)
                    ? false
                    : !anticipatedCapture.BuildProjSynchronizationQueued
                    || _runtime.TryDoneBuildProjSynchronization(
                        anticipatedCapture,
                        buildIntegrationVer);
            }

            if (anticipatedCanon.WholeChamberTag is not 0
                || anticipatedCapture?.IsSpatiallyProjected is true)

                return false;

            if (anticipatedCapture?.WorldEntity is null)
            {
                return anticipatedCapture is null
                    || !anticipatedCapture.BuildProjSynchronizationQueued
                    || _runtime.TryDoneBuildProjSynchronization(
                        anticipatedCapture,
                        buildIntegrationVer);
            }

            return !BroadcastPrimed(
                    anticipatedCapture,
                    buildIntegrationVer)
                ? false
                : !anticipatedCapture.BuildProjSynchronizationQueued
                || _runtime.TryDoneBuildProjSynchronization(
                    anticipatedCapture,
                    buildIntegrationVer);
        }

        bool materialized = _materializer.TryMaterialize(
            anticipatedCanon,
            anticipatedCanon.Snapshot,
            purpose,
            buildIntegrationVer);
        if (!materialized
            || !_runtime.IsLatestBuildIntegration(
                anticipatedCanon,
                buildIntegrationVer)
            || purpose is OnlineMirrorPurpose.AppearanceMutation)

            return materialized;

        if (!_runtime.TryFetchProj(
                anticipatedCanon,
                out anticipatedCapture))

            return false;

        return !BroadcastPrimed(
                anticipatedCapture,
                buildIntegrationVer)
            ? false
            : purpose is not OnlineMirrorPurpose.CreateSupersessionRecovery
            || _runtime.TryDoneBuildProjSynchronization(
                anticipatedCapture,
                buildIntegrationVer);
    }
}
