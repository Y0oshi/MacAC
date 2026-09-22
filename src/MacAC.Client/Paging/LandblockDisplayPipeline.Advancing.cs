using MacAC.Dat;
namespace MacAC.Client.Paging;

public sealed partial class LandblockDisplayPipeline
{
    public void AdvanceRetirements() => _retirements.Advance();

    public void AdvanceRetirements(PagingWorkMeter gauge) => _retirements.Advance(gauge);

    internal void ProgressPrecedenceSunset(
        uint lbIdent,
        PagingWorkMeter gauge) =>
        _retirements.ProgressPrecedence(lbIdent, gauge);

    private LandblockBulletinProceed Advance(
        LandblockFlowOutcome outcome,
        BulletinTransaction transaction,
        PagingWorkMeter? gauge,
        bool secureHeadway)
    {
        bool progressed = false;
        void ExecuteTimed(string juncture, Action op)
        {
            if (transaction.Timing is not { } timing)
            {
                op();
                return;
            }

            long begin = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                op();
            }
            finally
            {
                timing.Add(
                    juncture,
                    System.Diagnostics.Stopwatch.GetTimestamp() - begin);
            }
        }
        bool TryExec(
            PagingWorkCost price,
            string juncture,
            Action op)
        {
            if (gauge is null)
            {
                ExecuteTimed(juncture, op);
                progressed = true;
                return true;
            }

            var admission = gauge.TryAllocate(
                price,
                juncture,
                secureHeadway && !progressed);
            if (admission == PagingWorkAdmission.Yielded)
                return false;
            try
            {
                ExecuteTimed(juncture, op);
                gauge.Complete();
                progressed = true;
                return true;
            }
            catch
            {
                gauge.Fail();
                throw;
            }
        }

        if (!transaction.ExhibitSealed)
        {
            if (_rasterizePublisher is not null
                && _kineticsPublisher is not null
                && _staticPublisher is not null)
            {
                while (transaction.RasterizeBulletin is null)
                {
                    if (!TryExec(
                            new PagingWorkCost(EntityOperations: 1),
                            "publication-prepare-render",
                            () => transaction.RasterizeBulletin =
                                _rasterizePublisher.ReadyPublication(
                                    transaction.Build,
                                    transaction.TriMeshBlob)))

                        return new LandblockBulletinProceed(false, progressed);
                }
                while (transaction.KineticsBulletin is null)
                {
                    if (!TryExec(
                            default,
                            "publication-prepare-physics",
                            () => transaction.KineticsBulletin =
                                _kineticsPublisher.BuildBulletin(
                                    transaction.RasterizeBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }
                while (!transaction.KineticsBulletin.PrepSealed)
                {
                    int actorOps =
                        transaction.KineticsBulletin.PrepCur
                        < transaction.Build.Landblock.Entities.Count
                            ? 1
                            : 0;
                    if (!TryExec(
                            new PagingWorkCost(
                                EntityOperations: actorOps),
                            "publication-index-physics",
                            () => _kineticsPublisher.ProgressPrepOne(
                                transaction.KineticsBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }
                while (transaction.StaticBulletin is null)
                {
                    if (!TryExec(
                            default,
                            "publication-prepare-statics",
                            () => transaction.StaticBulletin =
                                _staticPublisher.BuildBulletin(
                                    transaction.KineticsBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }
                while (!transaction.StaticBulletin.PrepSealed)
                {
                    int actorOps =
                        transaction.StaticBulletin.PrepCur
                        < transaction.Build.Landblock.Entities.Count
                            ? 1
                            : 0;
                    if (!TryExec(
                            new PagingWorkCost(
                                EntityOperations: actorOps),
                            "publication-validate-statics",
                            () => _staticPublisher.ProgressPrepOne(
                                transaction.StaticBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }

                while (!transaction.RasterizeBulletin.CommenceSealed)
                {
                    PagingWorkCost cost =
                        !transaction.RasterizeBulletin.LandSealed
                            ? new PagingWorkCost(
                                GpuUploadBytes:
                                    transaction.Cost.TerrainPayloadBytes)
                            : !transaction.RasterizeBulletin.VisSealed
                                ? new PagingWorkCost(
                                    EntityOperations:
                                        transaction.Cost.VisibilityCells)
                                : default;
                    if (!TryExec(
                            cost,
                            "publication-render-prefix",
                            () => _rasterizePublisher.AdvanceBeginOne(
                                transaction.RasterizeBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }
                while (!transaction.KineticsBulletin.CommenceSealed)
                {
                    int actorOps =
                        transaction.KineticsBulletin.LandCanvas is not null
                        && transaction.KineticsBulletin.Build.Landblock.PhysicsDats?
                            .Info is { } details
                        && transaction.KineticsBulletin.ChamberCur < details.CellCount
                            ? 1
                            : transaction.KineticsBulletin.StructureCur
                                < transaction.KineticsBulletin.Buildings.Length
                                ? 1
                                : 0;
                    if (!TryExec(
                            new PagingWorkCost(
                                EntityOperations: actorOps),
                            "publication-physics-prefix",
                            () => _kineticsPublisher.AdvanceBeginOne(
                                transaction.KineticsBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }

                while (!transaction.RasterizeBulletin.WrapUpSealed)
                {
                    int actorOps =
                        transaction.RasterizeBulletin.StructureBulletin is
                        { PrepSealed: false } structureBulletin
                        && structureBulletin.StructureCur
                            < structureBulletin.Buildings.Length
                            ? 1
                            : transaction.RasterizeBulletin.EnvironChamberBulletin is
                            { PrepSealed: false } environChamberBulletin
                            && environChamberBulletin.ShellCur
                                < environChamberBulletin.Build.Shells.Length
                                ? 1
                            : !transaction.RasterizeBulletin.EnvironChambersSealed
                                && transaction.RasterizeBulletin
                                    .EnvironChamberBulletin is null
                                    ? transaction.Cost.EnvCellShells
                                : 0;
                    if (!TryExec(
                            new PagingWorkCost(
                                EntityOperations: actorOps),
                            "publication-render-suffix",
                            () => _rasterizePublisher.ProgressDoneOne(
                                transaction.RasterizeBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }

                while (!transaction.StaticBulletin.CommenceSealed)
                {
                    int actorOps =
                        transaction.StaticBulletin.PrecedingTidyCur
                        < transaction.StaticBulletin
                            .SequencedPreviouslyEngagedIdents.Count
                            ? 1
                            : 0;
                    if (!TryExec(
                            new PagingWorkCost(
                                EntityOperations: actorOps),
                            "publication-static-cleanup",
                            () => _staticPublisher.AdvanceBeginOne(
                                transaction.StaticBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }
                while (!transaction.KineticsBulletin.WrapUpSealed)
                {
                    if (transaction.KineticsBulletin.EngineAlterationSealed
                        && transaction.KineticsBulletin.CoreAlterationQueued
                        && !transaction.SpatialExhibitSealed
                        && !TryExec(
                            default,
                            "publication-spatial-commit",
                            () => SealSpatialExhibit(transaction)))

                        return new LandblockBulletinProceed(false, progressed);
                    int actorOps =
                        transaction.KineticsBulletin.GfxCur
                            < transaction.KineticsBulletin.GfxObjectIdents.Length
                            ? 1
                            : transaction.KineticsBulletin.PrecedingStaticCur
                                < transaction.KineticsBulletin
                                    .PrecedingStaticHolderIdents.Length
                                ? 1
                                : transaction.KineticsBulletin.StaticCur
                                    < transaction.Build.Landblock.Entities.Count
                                    ? 1
                                    : transaction.KineticsBulletin
                                            .RefloodHolderIdents is null
                                    || transaction.KineticsBulletin
                                        .RefloodCur
                                        < transaction.KineticsBulletin
                                            .RefloodHolderIdents.Count
                                    || !transaction.KineticsBulletin
                                        .SealSealed
                                        ? 1
                                        : 0;
                    if (!TryExec(
                            new PagingWorkCost(
                                EntityOperations: actorOps),
                            "publication-physics-statics",
                            () => _kineticsPublisher.ProgressDoneOne(
                                transaction.KineticsBulletin,
                                actor =>
                                    _staticPublisher
                                        .ReadyActorPriorImpact(
                                            transaction.StaticBulletin,
                                            actor))))

                        return new LandblockBulletinProceed(false, progressed);
                    if (transaction.KineticsBulletin.CoreAlterationQueued)
                    {
                        if (transaction.KineticsBulletin.EngineAlterationSealed
                            && !transaction.SpatialExhibitSealed
                            && !TryExec(
                                default,
                                "publication-spatial-commit",
                                () => SealSpatialExhibit(transaction)))

                            return new LandblockBulletinProceed(false, progressed);
                        if (!_kineticsPublisher
                                .CanContinueAlterationSynchronously())
                        {
                            return new LandblockBulletinProceed(
                                false,
                                progressed);
                        }
                    }
                }
                while (!transaction.StaticBulletin.WrapUpSealed)
                {
                    int actorOps =
                        transaction.StaticBulletin.ExtensionCur
                        < transaction.StaticBulletin.SequencedCaptures.Count
                            ? 1
                            : 0;
                    if (!TryExec(
                            new PagingWorkCost(
                                EntityOperations: actorOps),
                            "publication-plugin-projection",
                            () => _staticPublisher.ProgressDoneOne(
                                transaction.StaticBulletin)))

                        return new LandblockBulletinProceed(false, progressed);
                }
            }
            else
            {
                PagingWorkCost legacyPrice = new(
                    EntityOperations: transaction.Cost.Entities,
                    GpuUploadBytes: transaction.Cost.TerrainPayloadBytes);
                bool ran = TryExec(
                    legacyPrice,
                    "publication-legacy-presentation",
                    () =>
                    {
                        try
                        {
                            (_broadcastPriorSpatialSeal
                                ?? throw new InvalidOperationException(
                                    "The presentation pipeline has no publication owners"))(
                                transaction.Build,
                                transaction.TriMeshBlob);
                        }
                        catch (Exception problem)
                        {
                            if (problem is PagingMutationException
                                {
                                    MutationSealed: true,
                                })

                                _publications.Remove(outcome);
                            throw;
                        }
                    });
                if (!ran)

                    return new LandblockBulletinProceed(false, progressed);
            }
            transaction.ExhibitSealed = true;
        }

        if (!transaction.SpatialExhibitSealed && !TryExec(
                    default,
                    "publication-spatial-commit",
                    () => SealSpatialExhibit(transaction)))

            return new LandblockBulletinProceed(false, progressed);

        if (!transaction.EnvironChamberRerunSealed && !TryExec(
                    new PagingWorkCost(
                        EntityOperations: transaction.Cost.EnvCellShells),
                    "publication-envcell-replay",
                    () =>
                    {
                        if (transaction.Kind != BulletinFlavor.Far
                            && transaction.Build.EnvCells is
                            {
                                Shells.Length: > 0,
                            } environChambers)

                            _secureEnvironChamberTriMeshes?.Invoke(environChambers);
                        transaction.EnvironChamberRerunSealed = true;
                    }))

            return new LandblockBulletinProceed(false, progressed);

        if (!transaction.OnlineRecoverySealed && !TryExec(
                    default,
                    "publication-live-recovery",
                    () =>
                    {
                        _onLbFetched?.Invoke(transaction.LandblockId);
                        transaction.OnlineRecoverySealed = true;
                    }))

            return new LandblockBulletinProceed(false, progressed);

        _publications.Remove(outcome);
        if (transaction.Timing is { } finishedTiming)
        {
            BulletinTimingSensor.WriteBulletin(
                transaction.LandblockId,
                transaction.Kind.ToString(),
                finishedTiming);
        }
        return new LandblockBulletinProceed(true, progressed);
    }
}
