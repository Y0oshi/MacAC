using MacAC.Mechanics.Landscape;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public sealed partial class LandblockDisplayPipeline
{
    public void AnnounceFetched(LandblockFlowOutcome.Fetched fetched)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        var transaction = FetchOrBuild(
            fetched,
            BulletinFlavor.Loaded,
            fetched.Build,
            fetched.MeshData,
            fetched.LandblockId,
            fetched.Tier,
            estimate: null);
        Advance(fetched, transaction, gauge: null, secureHeadway: false);
    }

    public void AnnouncePromoted(
        LandblockFlowOutcome.Elevated promoted,
        bool combineIntoExtantLb)
    {
        ArgumentNullException.ThrowIfNull(promoted);
        BulletinFlavor sort = combineIntoExtantLb
            ? BulletinFlavor.PromoteExisting
            : BulletinFlavor.PromoteSelfContained;
        var transaction = FetchOrBuild(
            promoted,
            sort,
            promoted.Build,
            promoted.MeshData,
            promoted.LandblockId,
            LandblockFlowTier.Near,
            estimate: null);
        Advance(promoted, transaction, gauge: null, secureHeadway: false);
    }

    public void AnnounceAsFaraway(
        LandblockFlowOutcome approvedOutcome,
        LandblockAssemble finishedAssemble,
        LandblockTessellationData triMeshBlob)
    {
        ArgumentNullException.ThrowIfNull(approvedOutcome);
        ArgumentNullException.ThrowIfNull(finishedAssemble);
        ArgumentNullException.ThrowIfNull(triMeshBlob);

        if (!_publications.TryGetValue(approvedOutcome, out BulletinTransaction? transaction))
        {
            var finished = finishedAssemble.Landblock;
            MountedLandblock farawayLb = new MountedLandblock(
                finished.LandblockId,
                finished.Heightmap,
                Array.Empty<RealmActor>(),
                KineticDatBundle.Empty);
            LandblockAssemble farawayAssemble = new(
                farawayLb,
                Origin: finishedAssemble.Origin,
                TerrainBounds: finishedAssemble.TerrainBounds);
            transaction = new BulletinTransaction
            {
                Kind = BulletinFlavor.Far,
                Build = farawayAssemble,
                TriMeshBlob = triMeshBlob,
                LandblockId = farawayLb.LandblockId,
                Cost = LandblockFlowOutcomePrice.Estimate(
                    farawayAssemble,
                    triMeshBlob),
                Tier = LandblockFlowTier.Far,
                Timing = BulletinTimingSensor.BuildTimings(),
            };
            _publications.Add(approvedOutcome, transaction);
        }
        else if (transaction.Kind != BulletinFlavor.Far)
        {
            throw new InvalidOperationException(
                $"Landblock publication 0x{transaction.LandblockId:X8} changed kind while pending");
        }

        Advance(
            approvedOutcome,
            transaction,
            gauge: null,
            secureHeadway: false);
    }

    internal LandblockBulletinProceed BroadcastFetched(
        LandblockFlowOutcome.Fetched fetched,
        LandblockFlowPriceEstimate estimate,
        PagingWorkMeter gauge,
        bool secureHeadway)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentNullException.ThrowIfNull(gauge);
        var transaction = FetchOrBuild(
            fetched,
            BulletinFlavor.Loaded,
            fetched.Build,
            fetched.MeshData,
            fetched.LandblockId,
            fetched.Tier,
            estimate);
        return Advance(fetched, transaction, gauge, secureHeadway);
    }

    internal LandblockBulletinProceed BroadcastPromoted(
        LandblockFlowOutcome.Elevated promoted,
        bool combineIntoExtantLb,
        LandblockFlowPriceEstimate estimate,
        PagingWorkMeter gauge,
        bool secureHeadway)
    {
        ArgumentNullException.ThrowIfNull(promoted);
        ArgumentNullException.ThrowIfNull(gauge);
        BulletinFlavor sort = combineIntoExtantLb
            ? BulletinFlavor.PromoteExisting
            : BulletinFlavor.PromoteSelfContained;
        var transaction = FetchOrBuild(
            promoted,
            sort,
            promoted.Build,
            promoted.MeshData,
            promoted.LandblockId,
            LandblockFlowTier.Near,
            estimate);
        return Advance(promoted, transaction, gauge, secureHeadway);
    }

    internal LandblockBulletinProceed BroadcastAsFaraway(
        LandblockFlowOutcome approvedOutcome,
        LandblockAssemble finishedAssemble,
        LandblockTessellationData triMeshBlob,
        PagingWorkMeter gauge,
        bool secureHeadway)
    {
        ArgumentNullException.ThrowIfNull(approvedOutcome);
        ArgumentNullException.ThrowIfNull(finishedAssemble);
        ArgumentNullException.ThrowIfNull(triMeshBlob);
        ArgumentNullException.ThrowIfNull(gauge);

        if (!_publications.TryGetValue(
                approvedOutcome,
                out BulletinTransaction? transaction))
        {
            var finished = finishedAssemble.Landblock;
            MountedLandblock farawayLb = new MountedLandblock(
                finished.LandblockId,
                finished.Heightmap,
                Array.Empty<RealmActor>(),
                KineticDatBundle.Empty);
            LandblockAssemble farawayAssemble = new(
                farawayLb,
                Origin: finishedAssemble.Origin,
                TerrainBounds: finishedAssemble.TerrainBounds);
            transaction = new BulletinTransaction
            {
                Kind = BulletinFlavor.Far,
                Build = farawayAssemble,
                TriMeshBlob = triMeshBlob,
                LandblockId = farawayLb.LandblockId,
                Cost = LandblockFlowOutcomePrice.Estimate(farawayAssemble, triMeshBlob),
                Tier = LandblockFlowTier.Far,
                Timing = BulletinTimingSensor.BuildTimings(),
            };
            _publications.Add(approvedOutcome, transaction);
        }
        else if (transaction.Kind != BulletinFlavor.Far)
        {
            throw new InvalidOperationException(
                $"Landblock publication 0x{transaction.LandblockId:X8} changed kind while pending");
        }

        return Advance(approvedOutcome, transaction, gauge, secureHeadway);
    }
}
