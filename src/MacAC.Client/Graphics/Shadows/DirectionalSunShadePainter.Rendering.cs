using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

internal sealed partial class DirectionalSunShadePainter
{
    internal DirectionalSunShadeTelemetry Render(
        IGpuCycle cycle,
        in DirectionalSunShadeRenderInput feed,
        RealmPaintRouter realm,
        LandModernPainter land)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(cycle);
        LatestCycleMapping = DirectionalShadeFrameWiring.Disabled;
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(land);
        var gated = EvaluateLatchAndBroadcastDisabledMapping(
            cycle,
            in feed,
            out DirectionalShadeEnvironmentLedger surroundings,
            out long surroundingsLatchBeats);
        if (gated is not null)
            return gated.Value;

        long cpuJunctureBegun = feed.MeasureCpuStages ? Stopwatch.GetTimestamp() : 0L;
        var realmDraws =
            realm.ReadyDirectedShadeDraws(
                feed.Casters,
                feed.AllowTopologyRebuild);
        var precedingSceneryVis =
            feed.PriorLandscapeVisibility;
        var landDraws =
            land.ReadyDirectedShadeDraws(
                in precedingSceneryVis);
        DirectionalShadeMeshGeometry? realmGeo =
            realmDraws.EngagedDirectives.IsEmpty
                ? null
                : realm.FetchDirectedShadeGeo();
        DirectionalShadeLandscapeGeometry? landGeo =
            landDraws.Commands.IsEmpty ? null : land.FetchDirectedShadeGeo();
        uint xformMappingByteSize =
            realm.LocateDirectedShadeXformMappingDims(
                realmDraws.Xforms.Length,
                realmDraws.Stats.SourceBatches);
        var keptXforms = _xformBufs.Publish(
            cycle,
            realmDraws.AssembleSeries,
            realmDraws.Xforms,
            realmDraws.DynamicXformSockets,
            realmDraws.AllDynamicXformSockets,
            realmDraws.PreviousDynamicXformRenewWasDense,
            xformMappingByteSize);
        var xforms =
            realm.CommenceDirectedShadeXformCycle(
                cycle,
                in keptXforms);
        var invokerStats = feed.Casters.Stats;
        var invokerClasses =
            ConcludeInvokerClassTelemetry(
                in invokerStats,
                landDraws.HousedSpans.Length);
        var broadcastStats =
            _xformBufs.PreviousStats;
        var xformChurn = new DirectionalShadeTransformChurnTelemetry(
            invokerStats.CopiedTransformChanges,
            invokerStats.UpdateTransformChanges,
            invokerStats.UpdateAppearanceChanges,
            invokerStats.DynamicSynchronizationChanges,
            invokerStats.ActiveAnimatedStaticChanges,
            invokerStats.LiveDynamicRootChanges,
            invokerStats.EquippedChildChanges,
            invokerStats.DedupedChangedCasterSlots,
            invokerStats.TransformJournalFullRefresh,
            invokerStats.DensityBulkRefresh,
            invokerStats.BatchedProjectionCopyCalls,
            realmDraws.PreviousDynamicXformRenewTally,
            broadcastStats.CurrentChangedMatrices,
            broadcastStats.PendingReplayMatrices,
            broadcastStats.DynamicMatricesUpdated,
            broadcastStats.DynamicRangesUpdated,
            broadcastStats.BytesWritten,
            broadcastStats.UsedFullDynamicFallback,
            broadcastStats.DenseDirectUpload,
            broadcastStats.DenseFlightReplay,
            invokerClasses,
            invokerStats.ActiveSelected);
        long readiedDrawsAndXformsBeats = feed.MeasureCpuStages
            ? Stopwatch.GetTimestamp() - cpuJunctureBegun
            : 0L;
        try
        {
            return PaintReadied(
                cycle,
                surroundings,
                feed.CameraView,
                feed.CameraProjection,
                feed.CameraNearMeters,
                feed.CasterDepthPaddingMeters,
                realmDraws,
                landDraws,
                realmGeo,
                landGeo,
                xforms,
                feed.ResidentMaximumReachMeters,
                feed.MeasureGpuTimers,
                feed.MeasureCpuStages,
                new DirectionalSunShadeCpuStageTicks(
                    surroundingsLatchBeats,
                    readiedDrawsAndXformsBeats,
                    0L,
                    0L,
                    0L),
                xformChurn,
                feed.AtmosphericFrame);
        }
        catch
        {
            realm.AbortDirectedShadeXformCycle(cycle);
            throw;
        }
    }

    internal DirectionalSunShadeTelemetry PaintReadied(
        IGpuCycle cycle,
        in DirectionalShadeEnvironmentLedger surroundings,
        Matrix4x4 camLens,
        Matrix4x4 camProj,
        float camNearbyMeters,
        float invokerZDepthPaddingMeters,
        DirectionalShadePreparedDraws realmDraws,
        DirectionalShadeLandscapePreparedDraws landDraws,
        DirectionalShadeMeshGeometry? worldGeometry,
        DirectionalShadeLandscapeGeometry? terrainGeometry,
        RealmTransformFrameSlice worldTransforms,
        float housedCeilingReachMeters = float.PositiveInfinity,
        bool gaugeGpuTickers = true,
        bool gaugeCpuJunctures = false,
        DirectionalSunShadeCpuStageTicks cpuJunctures = default,
        DirectionalShadeTransformChurnTelemetry xformChurn = default,
        AtmosphericFrameBufferWiring atmosphericCycle = default)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(cycle);
        LatestCycleMapping = DirectionalShadeFrameWiring.Disabled;
        ArgumentNullException.ThrowIfNull(realmDraws);
        ArgumentNullException.ThrowIfNull(landDraws);
        if (!surroundings.ShouldRasterize)
        {
            BroadcastDisabledRecipientMapping(cycle, atmosphericCycle);
            return Disabled(in surroundings, cpuJunctures);
        }
        if (!realmDraws.EngagedDirectives.IsEmpty && worldGeometry is null)
            throw new ArgumentNullException(nameof(worldGeometry));
        if (!landDraws.Commands.IsEmpty && terrainGeometry is null)
            throw new ArgumentNullException(nameof(terrainGeometry));
        if (!worldTransforms.IsValidFor(cycle))
            throw new ArgumentException(
                "Shadow transforms must use this frame's shared N.5 allocation",
                nameof(worldTransforms));

        long begun = Stopwatch.GetTimestamp();
        float netInvokerZDepthPaddingMeters =
            LocateInvokerZDepthPaddingMeters(
                invokerZDepthPaddingMeters,
                _fidelity.MaximumReachMeters,
                housedCeilingReachMeters);
        var fit = new DirectionalShadeCascadeFitInput(
            camLens,
            camProj,
            surroundings.SurfaceToLightDirection,
            _fidelity,
            camNearbyMeters,
            PracticalSplitLambda: 0.65f,
            netInvokerZDepthPaddingMeters,
            housedCeilingReachMeters);
        int cascadeTally = DirectionalShadeCascadeFitter.Fit(
            fit,
            _cascades);
        if (cascadeTally is 0)
        {
            var unavailable = surroundings with
            {
                Reason = DirectionalShadeTurnstileReason.ResidentWindowUnavailable,
            };
            BroadcastDisabledRecipientMapping(cycle, atmosphericCycle);
            return Disabled(in unavailable, cpuJunctures);
        }

        ReadOnlySpan<DirectionalShadeCascade> cascades =
            _cascades.AsSpan(0, cascadeTally);
        var uniforms = DirectionalShadeUniforms.Create(
            cascades,
            surroundings,
            _fidelity,
            _textureSocket);
        var uniformAlloc = cycle.ReserveLoop(
            DirectionalShadeUniforms.SizeInBytes,
            GpuLoopPurpose.Uniform);
        MemoryMarshal.Write(uniformAlloc.Data, in uniforms);

        long fitAndUniformFinished = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
        var uploads = ReadyGpuBlob(
            worldTransforms,
            realmDraws,
            landDraws);
        if (_multiviewCascades)
        {
            using var coder = cycle.BeginPass(
                GpuPassSpec.DirectedZDepthMultiview(
                    "directional-shadow-multiview",
                    _mark,
                    LoMultiviewBitmask));
            using IDisposable? ticker = gaugeGpuTickers
                ? coder.CommenceTickerAmbit(MultiviewTickerLabel)
                : null;
            coder.AttachUniformBuf(
                GpuBindingModel.UniformDirectedShade,
                uniformAlloc.Buffer,
                uniformAlloc.ShiftOctets,
                DirectionalShadeUniforms.SizeInBytes);
            if (atmosphericCycle.IsTied)
            {
                coder.AttachUniformBuf(
                    GpuBindingModel.UniformAtmosphericCycle,
                    atmosphericCycle.Buffer!,
                    atmosphericCycle.OffsetBytes,
                    atmosphericCycle.SizeBytes);
            }
            PaintLand(coder, uploads, landDraws, terrainGeometry, 0,
                _landMultiviewPipe);
            PaintRealm(coder, uploads, realmDraws, worldGeometry, 0,
                _realmSolidMultiviewPipe, _realmCutoutMultiviewPipe);
        }
        else for (int cascadeOrdinal = 0; cascadeOrdinal < cascadeTally; ++cascadeOrdinal)
        {
            using var coder = cycle.BeginPass(
                GpuPassSpec.DirectedZDepth(
                    $"directional-shadow-{cascadeOrdinal}",
                    _mark,
                    cascadeOrdinal));
            using IDisposable? ticker = gaugeGpuTickers
            ? coder.CommenceTickerAmbit(TickerLabel(cascadeOrdinal))
            : null;
            coder.AttachUniformBuf(
                GpuBindingModel.UniformDirectedShade,
                uniformAlloc.Buffer,
                uniformAlloc.ShiftOctets,
                DirectionalShadeUniforms.SizeInBytes);
            if (atmosphericCycle.IsTied)
            {
                coder.AttachUniformBuf(
                    GpuBindingModel.UniformAtmosphericCycle,
                    atmosphericCycle.Buffer!,
                    atmosphericCycle.OffsetBytes,
                    atmosphericCycle.SizeBytes);
            }

            PaintLand(coder, uploads, landDraws, terrainGeometry, cascadeOrdinal);
            PaintRealm(coder, uploads, realmDraws, worldGeometry, cascadeOrdinal);
        }

        long passRecordingFinished = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;

        LatestCycleMapping = new DirectionalShadeFrameWiring(
            cycle.SerialNo,
            Enabled: true,
            uniformAlloc.Buffer,
            uniformAlloc.ShiftOctets,
            DirectionalShadeUniforms.SizeInBytes,
            _textureSocket,
            cascadeTally,
            AtmosphericFrame: atmosphericCycle);

        (bool hasGpu, double gpuMillis) = LocateGpu(cascadeTally);
        int drawsPerCascade = landDraws.Commands.IsEmpty ? 0 : 1;
        drawsPerCascade = checked(
            drawsPerCascade
            + (realmDraws.EngagedDirectives.IsEmpty
                ? 0
                : realmDraws.EngagedSolidExecutions.Length
                    + realmDraws.EngagedAlphaCutoutExecutions.Length));
        long finished = Stopwatch.GetTimestamp();
        cpuJunctures = cpuJunctures with
        {
            FitAndUniformTicks = gaugeCpuJunctures
                ? fitAndUniformFinished - begun
                : 0L,
            LayeredPassRecordingTicks = gaugeCpuJunctures
                ? passRecordingFinished - fitAndUniformFinished
                : 0L,
            BookkeepingTicks = gaugeCpuJunctures
                ? finished - passRecordingFinished
                : 0L,
        };
        return new DirectionalSunShadeTelemetry(
            DirectionalShadeTurnstileReason.Enabled,
            surroundings.Strength,
            cascadeTally,
            checked((_multiviewCascades ? 1 : cascadeTally) * drawsPerCascade),
            realmDraws.EngagedSolidDirectiveTally,
            realmDraws.EngagedAlphaCutoutDirectiveTally,
            landDraws.Commands.Length,
            realmDraws.AssembleSeries,
            landDraws.BuildSeries,
            (finished - begun) * 1000d / Stopwatch.Frequency,
            gpuMillis,
            hasGpu,
            _fidelity.ApproximateDepthMapBytes,
            cpuJunctures,
            xformChurn,
            surroundings.SourceKind,
            surroundings.SourceObjectIndex,
            surroundings.SourceGfxObjId,
            surroundings.SurfaceToLightDirection,
            surroundings.LightElevationSin,
            ResidentWorldCasters: feedInvokerClassesTally(xformChurn),
            ActiveWorldCasters: xformChurn.ActiveSelectedCasters,
            ResidentWorldInstances: realmDraws.Stats.PreparedInstances,
            ActiveWorldInstances: realmDraws.Stats.ActiveInstances,
            ResidentWorldCommands: realmDraws.Commands.Length,
            ActiveWorldCommands: realmDraws.EngagedDirectives.Length,
            ResidentTerrainCommands: landDraws.HousedSpans.Length,
            ActiveTerrainCommands: landDraws.Commands.Length);

        static int feedInvokerClassesTally(
            DirectionalShadeTransformChurnTelemetry churn)
        {
            var classes = churn.CasterClasses;
            return checked(
                classes.OutdoorStatics
                + classes.Buildings
                + classes.AnimatedStatics
                + classes.LocalPlayers
                + classes.RemotePlayers
                + classes.NonPlayerCreatures
                + classes.OtherLiveDynamics
                + classes.EquippedChildren);
        }
    }
}
