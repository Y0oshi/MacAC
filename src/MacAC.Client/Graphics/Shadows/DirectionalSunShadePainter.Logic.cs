using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Dat;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Stage;

namespace MacAC.Client.Graphics;

internal sealed partial class DirectionalSunShadePainter
{
    private readonly DirectionalShadeQuality _fidelity;

    internal DirectionalShadeQuality Quality => _fidelity;
    private readonly bool _multiviewCascades;

    internal bool MultiviewCascadesTurnedOn => _multiviewCascades;

    public bool TryFetchLatestCycleMapping(
        IGpuCycle cycle,
        out DirectionalShadeFrameWiring mapping)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        mapping = LatestCycleMapping;
        return !_destroyed && mapping.IsBindableFor(cycle);
    }

    internal IGpuBitmap ZDepthTexture => _mark.ZDepthTexture;

    private readonly GpuTextureSlot _textureSocket;

    internal GpuTextureSlot TextureSlot => _textureSocket;
    private readonly DirectionalShadePipelineShaders _pipeShaders;

    public DirectionalShadePipelineShaders PipeShaders => _pipeShaders;
    internal DirectionalShadeFrameWiring LatestCycleMapping { get; private set; }

    internal long KeptDirectiveBufOctets
    {
        get
        {
            return checked(
        (_realmLotBuf?.SizeBytes ?? 0L)
        + (_realmDirectiveBuf?.SizeBytes ?? 0L)
        + (_landDirectiveBuf?.SizeBytes ?? 0L));
        }
    }

    internal int KeptDirectiveBufTally
    {
        get
        {
            return (_realmLotBuf is null ? 0 : 1)
        + (_realmDirectiveBuf is null ? 0 : 1)
        + (_landDirectiveBuf is null ? 0 : 1);
        }
    }

    internal long KeptGpuBufOctets
    {
        get
        {
            return checked(
        KeptDirectiveBufOctets + _xformBufs.KeptGpuOctets);
        }
    }

    internal int KeptGpuBufTally
    {
        get
        {
            return checked(
        KeptDirectiveBufTally + _xformBufs.BufTally);
        }
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        LatestCycleMapping = DirectionalShadeFrameWiring.Disabled;
        _realmCutoutPipe.Dispose();
        _realmCutoutMultiviewPipe?.Dispose();
        _realmSolidMultiviewPipe?.Dispose();
        _landMultiviewPipe?.Dispose();
        _realmSolidPipe.Dispose();
        _landPipe.Dispose();
        _landDirectiveBuf?.Dispose();
        _realmDirectiveBuf?.Dispose();
        _realmLotBuf?.Dispose();
        _xformBufs.Dispose();
        _device.FreeTextureSocket(_textureSocket);
        _sampler.Dispose();
        _mark.Dispose();
    }
    internal static string TickerLabel(int cascadeIndex)
    {
        return cascadeIndex switch
        {
            0 => "directional-shadow-cascade-0",
            1 => "directional-shadow-cascade-1",
            2 => "directional-shadow-cascade-2",
            3 => "directional-shadow-cascade-3",
            _ => throw new ArgumentOutOfRangeException(nameof(cascadeIndex)),
        };
    }

    internal DirectionalSunShadeTelemetry? EvaluateLatchAndBroadcastDisabledMapping(
        IGpuCycle cycle,
        in DirectionalSunShadeRenderInput feed,
        out DirectionalShadeEnvironmentLedger surroundings,
        out long surroundingsLatchBeats)
    {
        LatestCycleMapping = DirectionalShadeFrameWiring.Disabled;
        long cpuJunctureBegun = feed.MeasureCpuStages ? Stopwatch.GetTimestamp() : 0L;
        surroundings = DirectionalShadeEnvironmentTurnstile.Evaluate(
            feed.Environment,
            _atmosphereRule);
        surroundingsLatchBeats = feed.MeasureCpuStages
            ? Stopwatch.GetTimestamp() - cpuJunctureBegun
            : 0L;
        if (!surroundings.ShouldRasterize)
        {
            BroadcastDisabledRecipientMapping(cycle, feed.AtmosphericFrame);
            return Disabled(
                in surroundings,
                new DirectionalSunShadeCpuStageTicks(
                    surroundingsLatchBeats, 0L, 0L, 0L, 0L));
        }
        if (feed.ResidentMaximumReachMeters <= feed.CameraNearMeters)
        {
            surroundings = surroundings with
            {
                Reason = DirectionalShadeTurnstileReason.ResidentWindowUnavailable,
            };
            BroadcastDisabledRecipientMapping(cycle, feed.AtmosphericFrame);
            return Disabled(
                in surroundings,
                new DirectionalSunShadeCpuStageTicks(
                    surroundingsLatchBeats, 0L, 0L, 0L, 0L));
        }
        return null;
    }

    internal void BroadcastDisabledRecipientMapping(
        IGpuCycle cycle,
        AtmosphericFrameBufferWiring atmosphericCycle)
    {
        if (!atmosphericCycle.IsTied)
            return;

        var disabledUniforms = new DirectionalShadeUniforms(
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Vector4.Zero,
            Vector4.Zero,
            Vector4.Zero,
            new ClientUInt4(0u, 0u, 0u, 0u),
            new Vector4(0f, 0f, 1f, 0f));
        var alloc = cycle.ReserveLoop(
            DirectionalShadeUniforms.SizeInBytes,
            GpuLoopPurpose.Uniform);
        MemoryMarshal.Write(alloc.Data, in disabledUniforms);

        LatestCycleMapping = new DirectionalShadeFrameWiring(
            cycle.SerialNo,
            Enabled: false,
            alloc.Buffer,
            alloc.ShiftOctets,
            DirectionalShadeUniforms.SizeInBytes,
            GpuTextureSlot.Unassigned,
            CascadeCount: 0,
            AtmosphericFrame: atmosphericCycle);
    }

    internal static DirectionalShadeCasterClassTelemetry
        ConcludeInvokerClassTelemetry(
            in DirectionalShadeCasterBuildStats invokerStats,
            int landDirectiveTally)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(landDirectiveTally);
        return invokerStats.InvokerClasses with
        {
            TerrainCommands = landDirectiveTally,
        };
    }

    internal static float LocateInvokerZDepthPaddingMeters(
        float configuredPaddingMeters,
        float qualityReachMeters,
        float residentMaximumReachMeters)
    {
        if (!float.IsFinite(configuredPaddingMeters)
            || configuredPaddingMeters <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuredPaddingMeters));
        }
        if (!float.IsFinite(qualityReachMeters) || qualityReachMeters <= 0f)
            throw new ArgumentOutOfRangeException(nameof(qualityReachMeters));
        if (float.IsNaN(residentMaximumReachMeters)
            || residentMaximumReachMeters <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(residentMaximumReachMeters));
        }

        float netRecipientReach = MathF.Min(
            qualityReachMeters,
            residentMaximumReachMeters);
        return MathF.Max(configuredPaddingMeters, netRecipientReach);
    }

    private static KeptGpuBufferSlice Slice(IClientGpuBuffer? buf) =>
        new(buf, 0u, checked((uint)(buf?.SizeBytes ?? 0L)));

    private (bool HasMeasurement, double Milliseconds) LocateGpu(int cascadeTally)
    {
        if (_multiviewCascades)
            return _device.Tickers.TryLocate(MultiviewTickerLabel, out double measured)
                ? (true, measured)
                : (false, 0d);
        double sum = 0d;
        for (int idx = 0; idx < cascadeTally; ++idx)
        {
            if (!_device.Tickers.TryLocate(TickerLabel(idx), out double millis))
                return (false, 0d);
            sum += millis;
        }
        return (true, sum);
    }

    private DirectionalSunShadeTelemetry Disabled(
        in DirectionalShadeEnvironmentLedger surroundings,
        DirectionalSunShadeCpuStageTicks cpuJunctures = default)
    {
        return new(
            surroundings.Reason,
            0f,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0d,
            0d,
            false,
            _fidelity.ApproximateDepthMapBytes,
            cpuJunctures,
            SourceKind: surroundings.SourceKind,
            SourceObjectIndex: surroundings.SourceObjectIndex,
            SourceGfxObjId: surroundings.SourceGfxObjId,
            SurfaceToLightDirection: surroundings.SurfaceToLightDirection,
            LightElevationSin: surroundings.LightElevationSin);
    }

    private ReadiedGpuUploads ReadyGpuBlob(
        in RealmTransformFrameSlice xforms,
        DirectionalShadePreparedDraws realm,
        DirectionalShadeLandscapePreparedDraws land)
    {
        if (_realmGpuAssembleSeries != realm.EngagedPickSeries)
            ReassembleRealmGpuBlob(realm);
        if (_landGpuAssembleSeries != land.EngagedSelectionSequence)
            ReassembleLandGpuBlob(land);

        return new ReadiedGpuUploads(
            xforms,
            Slice(_realmLotBuf),
            Slice(_realmDirectiveBuf),
            Slice(_landDirectiveBuf));
    }

    private void ReassembleRealmGpuBlob(DirectionalShadePreparedDraws realm)
    {
        IClientGpuBuffer? lots = null;
        IClientGpuBuffer? directives = null;
        try
        {
            if (!realm.EngagedDirectives.IsEmpty)
            {
                SecureLotCap(realm.EngagedLots.Length);
                for (int idx = 0; idx < realm.EngagedLots.Length; ++idx)
                {
                    var lot = realm.EngagedLots[idx];
                    _lotTemp[idx] = new DirectionalShadeBatchGpuData(
                        lot.TextureSlot.Index,
                        0u,
                        lot.TextureLayer,
                        DirectionalShadeBatchFlags.Pack(lot.Material)
                            | lot.FoliageFlags);
                }

                ReadOnlySpan<byte> lotOctets = MemoryMarshal.AsBytes(
                    _lotTemp.AsSpan(0, realm.EngagedLots.Length));
                var directiveOctets = MemoryMarshal.AsBytes(
                    realm.EngagedDirectives);
                lots = BuildKeptBuf(
                    $"directional-shadow-world-batches-{realm.EngagedPickSeries}",
                    lotOctets,
                    GpuBufferPurpose.Storage);
                directives = BuildKeptBuf(
                    $"directional-shadow-world-commands-{realm.EngagedPickSeries}",
                    directiveOctets,
                    GpuBufferPurpose.Indirect);
            }
        }
        catch
        {
            directives?.Dispose();
            lots?.Dispose();
            throw;
        }

        IClientGpuBuffer? earlierLots = _realmLotBuf;
        IClientGpuBuffer? earlierDirectives = _realmDirectiveBuf;
        _realmLotBuf = lots;
        _realmDirectiveBuf = directives;
        _realmGpuAssembleSeries = realm.EngagedPickSeries;
        earlierDirectives?.Dispose();
        earlierLots?.Dispose();
    }

    private void ReassembleLandGpuBlob(DirectionalShadeLandscapePreparedDraws land)
    {
        IClientGpuBuffer? directives = null;
        if (!land.Commands.IsEmpty)
        {
            directives = BuildKeptBuf(
                $"directional-shadow-terrain-commands-{land.EngagedSelectionSequence}",
                MemoryMarshal.AsBytes(land.Commands),
                GpuBufferPurpose.Indirect);
        }

        IClientGpuBuffer? earlier = _landDirectiveBuf;
        _landDirectiveBuf = directives;
        _landGpuAssembleSeries = land.EngagedSelectionSequence;
        earlier?.Dispose();
    }

    private IClientGpuBuffer BuildKeptBuf(
        string label,
        ReadOnlySpan<byte> contents,
        GpuBufferPurpose usage)
    {
        if (contents.IsEmpty)
            throw new ArgumentException("Retained shadow buffers can't be empty", nameof(contents));
        IClientGpuBuffer buf = _device.BuildBuf(new GpuBufferSpec(
            label,
            contents.Length,
            usage | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        try
        {
            buf.Upload(0, contents);
            return buf;
        }
        catch
        {
            buf.Dispose();
            throw;
        }
    }

    private static IGpuPipe BuildPipe(
        IClientGpuDevice dev,
        string label,
        GpuShaderGroup shaders,
        GpuVertexArrangement arrangement,
        GpuFrontFacet frontFace,
        uint lensBitmask = 0)
    {
        return dev.BuildPipe(new GpuPipeSpec
        {
            Name = label,
            Shaders = shaders,
            VertArrangement = arrangement,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = GpuBlendManner.None,
            Depth = new GpuDepthLedger(true, true, RealmDepthContract.RealmContrast),
            Cull = GpuPruneManner.Back,
            FrontFace = frontFace,
            AlphaToCoverage = false,
            TintEmit = false,
            HasTintAttachment = false,
            AllowTintFmtVariants = false,
            SampleCount = 1,
            UsesRasterizeBundleShaderAbi = true,
            LensMask = lensBitmask,
        });
    }

    private void PaintLand(
        IGpuSweepCoder coder,
        in ReadiedGpuUploads uploads,
        DirectionalShadeLandscapePreparedDraws draws,
        DirectionalShadeLandscapeGeometry? geo,
        int cascadeOrdinal,
        IGpuPipe? pipe = null)
    {
        if (draws.Commands.IsEmpty)
            return;
        var actual = geo!.Value;
        coder.BindPipeline(pipe ?? _landPipe);
        coder.AttachVertBuf(0, actual.VertexBuffer, 0);
        coder.AttachOrdinalBuf(actual.IndexBuffer, 0, GpuOrdinalKind.UInt32);
        var push = PushForCascade(cascadeOrdinal, 0);
        coder.AssignPushConstants(in push);
        coder.MultiPaintIndexedIndirect(
            uploads.LandDirectives.DemandBuf(),
            uploads.LandDirectives.OffsetBytes,
            checked((uint)draws.Commands.Length),
            PaintDirectiveStride);
    }

    private void PaintRealm(
        IGpuSweepCoder coder,
        in ReadiedGpuUploads uploads,
        DirectionalShadePreparedDraws draws,
        DirectionalShadeMeshGeometry? geo,
        int cascadeOrdinal,
        IGpuPipe? solidPipe = null,
        IGpuPipe? cutoutPipe = null)
    {
        if (draws.EngagedDirectives.IsEmpty)
            return;
        var actual = geo!.Value;
        coder.AttachDepotBuf(
            GpuBindingModel.DepotInsts,
            uploads.Xforms.Buffer,
            uploads.Xforms.BaseOffsetBytes,
            uploads.Xforms.BindingSizeBytes);
        coder.AttachDepotBuf(
            GpuBindingModel.DepotLots,
            uploads.Batches.DemandBuf(),
            uploads.Batches.OffsetBytes,
            uploads.Batches.SizeBytes);
        PaintRealmSpan(
            coder,
            uploads.RealmDirectives,
            draws.EngagedSolidExecutions,
            cascadeOrdinal,
            solidPipe ?? _realmSolidPipe,
            actual);
        PaintRealmSpan(
            coder,
            uploads.RealmDirectives,
            draws.EngagedAlphaCutoutExecutions,
            cascadeOrdinal,
            cutoutPipe ?? _realmCutoutPipe,
            actual);
    }

    private static void PaintRealmSpan(
        IGpuSweepCoder coder,
        in KeptGpuBufferSlice directives,
        ReadOnlySpan<DirectionalShadePreparedRun> executions,
        int cascadeOrdinal,
        IGpuPipe pipe,
        in DirectionalShadeMeshGeometry geo)
    {
        if (executions.IsEmpty)
            return;
        coder.BindPipeline(pipe);
        coder.AttachVertBuf(0, geo.VertexBuffer, 0);
        coder.AttachOrdinalBuf(geo.IndexBuffer, 0, GpuOrdinalKind.UInt16);

        for (int execOrdinal = 0; execOrdinal < executions.Length; ++execOrdinal)
        {
            var exec = executions[execOrdinal];
            ImposePrune(coder, exec.CullMode);
            var push = PushForCascade(cascadeOrdinal, exec.StartCommand);
            coder.AssignPushConstants(in push);
            coder.MultiPaintIndexedIndirect(
                directives.DemandBuf(),
                directives.OffsetBytes + checked((uint)(exec.StartCommand * PaintDirectiveStride)),
                checked((uint)exec.CommandCount),
                PaintDirectiveStride);
        }
    }

    private static GpuShoveConstants PushForCascade(int cascadeOrdinal, int paintIdentShift)
    {
        var push = GpuShoveConstants.Default;
        push.RasterizePass = cascadeOrdinal;
        push.PaintIdentShift = paintIdentShift;
        return push;
    }

    private static void ImposePrune(IGpuSweepCoder coder, FaceCulling manner)
    {
        coder.AssignFrontFace(GpuFrontFacet.Clockwise);
        coder.AssignPruneManner(manner switch
        {
            FaceCulling.None => GpuPruneManner.None,
            FaceCulling.Clockwise => GpuPruneManner.Front,
            _ => GpuPruneManner.Back,
        });
    }

    private void SecureLotCap(int needed)
    {
        if (_lotTemp.Length >= needed)
            return;
        int cap = _lotTemp.Length is 0 ? 16 : _lotTemp.Length;
        while (cap < needed)
            cap = checked(cap * 2);
        Array.Resize(ref _lotTemp, cap);
    }
}
