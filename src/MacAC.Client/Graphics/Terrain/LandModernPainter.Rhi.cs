using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Landscape;

namespace MacAC.Client.Graphics;

public sealed unsafe partial class LandModernPainter
{
    internal static readonly GpuVertexArrangement LandVertArrangement = GpuVertexArrangement.Interleaved(
        strideOctets: VertDims,
        ImmutableArray.Create(
            new GpuVertexAttribute(0, GpuVertFmt.Float3, 0),
            new GpuVertexAttribute(1, GpuVertFmt.Float3, 12),
            new GpuVertexAttribute(2, GpuVertFmt.UByte4UInt, 24),
            new GpuVertexAttribute(3, GpuVertFmt.UByte4UInt, 28),
            new GpuVertexAttribute(4, GpuVertFmt.UByte4UInt, 32),
            new GpuVertexAttribute(5, GpuVertFmt.UByte4UInt, 36)));

    private readonly IClientGpuDevice? _device;
    private readonly ILatestGpuCycleOrigin? _cycles;
    private readonly IRealmPassScope? _ambit;
    private IGpuPipe? _pipe;
    private DirectionalShadeReceiverPipelineLedger? _directedShadeRecipient;
    private IClientGpuBuffer? _vertVault;
    private IClientGpuBuffer? _ordinalVault;
    private IClientGpuBuffer? _tilingBuf;

    internal LandModernPainter(
        IClientGpuDevice device,
        ILatestGpuCycleOrigin frames,
        IRealmPassScope scope,
        LandTileset atlas,
        IGpuAssetSunsetFifo assetSunset,
        int startingSocketCap = 64)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
        _ambit = scope ?? throw new ArgumentNullException(nameof(scope));
        _tileset = atlas ?? throw new ArgumentNullException(nameof(atlas));
        ArgumentNullException.ThrowIfNull(assetSunset);
        _sunsetRegister = new GpuSunsetRegister(assetSunset);
        _alloc = new GpuRetiredLandscapeSlotAllotter(startingSocketCap, assetSunset);
        _sockets = new SocketBlob?[startingSocketCap];

        _pipe = device.BuildPipe(new GpuPipeSpec
        {
            Name = "terrain",
            Shaders = new GpuShaderGroup("land_lit"),
            VertArrangement = LandVertArrangement,
            Wiring = GpuPrimitiveWiring.TriangleList,
            Blend = GpuBlendManner.None,
            Depth = new GpuDepthLedger(Test: true, Write: true, RealmDepthContract.RealmContrast),
            Cull = GpuPruneManner.Back,
            FrontFace = GpuFrontFacet.CounterClockwise,
            AlphaToCoverage = false,
            TintEmit = true,
            SampleCount = scope.SampleCount,
        });
        ReserveRhiBufs(startingSocketCap);
    }

    private void ReserveRhiBufs(int capSockets)
    {
        long vertOctets = checked((long)capSockets * VertsPerLb * VertDims);
        long ordinalOctets = checked((long)capSockets * OrdinalsPerLb * OrdinalDims);
        IClientGpuDevice dev = DemandDev();
        _vertVault = dev.BuildBuf(new GpuBufferSpec(
            "terrain-vertices",
            vertOctets,
            GpuBufferPurpose.Vertex
                | GpuBufferPurpose.TransferSource
                | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        _globalVboCapOctets = vertOctets;
        _ordinalVault = dev.BuildBuf(new GpuBufferSpec(
            "terrain-indices",
            ordinalOctets,
            GpuBufferPurpose.Index
                | GpuBufferPurpose.TransferSource
                | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        _globalEboCapOctets = ordinalOctets;
    }

    private void SecureRhiCap(int newCapSockets)
    {
        if (newCapSockets <= _alloc.Capacity)
            return;

        long vertOctets = checked((long)newCapSockets * VertsPerLb * VertDims);
        long ordinalOctets = checked((long)newCapSockets * OrdinalsPerLb * OrdinalDims);
        IClientGpuDevice dev = DemandDev();
        IClientGpuBuffer formerVerts = DemandVertVault();
        IClientGpuBuffer formerOrdinals = DemandOrdinalVault();

        IClientGpuBuffer newVerts = dev.BuildBuf(new GpuBufferSpec(
            "terrain-vertices",
            vertOctets,
            GpuBufferPurpose.Vertex
                | GpuBufferPurpose.TransferSource
                | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal));
        IClientGpuBuffer newOrdinals;
        try
        {
            newOrdinals = dev.BuildBuf(new GpuBufferSpec(
                "terrain-indices",
                ordinalOctets,
                GpuBufferPurpose.Index
                    | GpuBufferPurpose.TransferSource
                    | GpuBufferPurpose.TransferDestination,
                GpuMemoryTenancy.DeviceLocal));
        }
        catch
        {
            newVerts.Dispose();
            throw;
        }

        formerVerts.ReplicateTo(newVerts, 0, 0, _globalVboCapOctets);
        formerOrdinals.ReplicateTo(newOrdinals, 0, 0, _globalEboCapOctets);

        _vertVault = newVerts;
        _ordinalVault = newOrdinals;
        _globalVboCapOctets = vertOctets;
        _globalEboCapOctets = ordinalOctets;

        formerVerts.Dispose();
        formerOrdinals.Dispose();

        SocketBlob?[] grownSockets = new SocketBlob?[newCapSockets];
        Array.Copy(_sockets, grownSockets, _sockets.Length);
        _sockets = grownSockets;
        _alloc.ExpandTo(newCapSockets);
    }

    private void PushRhiLb(
        int socket,
        TerrainVert[] bakedVerts,
        uint[] bakedOrdinals)
    {
        DemandVertVault().Upload(
            (long)socket * VertsPerLb * VertDims,
            MemoryMarshal.AsBytes<TerrainVert>(bakedVerts));
        DemandOrdinalVault().Upload(
            (long)socket * OrdinalsPerLb * OrdinalDims,
            MemoryMarshal.AsBytes<uint>(bakedOrdinals));
    }

    private void PaintRhi(Matrix4x4 lensProj, int paintTally)
    {
        var ambit = _ambit!;
        var coder = ambit.DemandCoder();
        IGpuCycle cycle = _cycles!.LatestCycle
            ?? throw new InvalidOperationException(
                "LandModernPainter needs an open IGpuCycle (see GpuDeviceCycleLifespan)");

        (GpuTextureSlot landSocket, GpuTextureSlot alphaSocket) = _tileset.TextureSockets;

        var pushConstants = new GpuShoveConstants
        {
            LensMirror = lensProj,
            PaintIdentShift = 0,
            IlluminationManner = 0,
            RasterizePass = 0,
            LampDiag = 0,
            TextureIndexA = landSocket.Index,
            TextureOrdinalB = alphaSocket.Index,
            ParamA = 0f,
            ParameterB = 0f,
        };

        IGpuPipe pipe = _pipe!;
        DirectionalShadeFrameWiring shadeMapping = default;
        var recipient =
            _directedShadeRecipient;
        var recipientSrc = recipient?.Source;
        bool mappingValid = recipientSrc is not null
            && recipientSrc.TryFetchLatestCycleMapping(cycle, out shadeMapping);
        if (DirectionalShadeReceiverRule.ShouldPickRecipientPipe(
                coder.Pass.Name,
                recipientSrc is not null,
                mappingValid))

            pipe = recipient!.Pipeline;

        coder.BindPipeline(pipe);
        coder.AssignPushConstants(in pushConstants);
        coder.AttachVertBuf(0, DemandVertVault(), 0);
        coder.AttachOrdinalBuf(DemandOrdinalVault(), 0, GpuOrdinalKind.UInt32);
        AttachTilingChart(coder);
        RealmFrameSectionWiring.AttachTableauIllumination(coder, ambit.Sections, cycle);
        if (shadeMapping.Buffer is not null)
        {
            coder.AttachUniformBuf(
                GpuBindingModel.UniformDirectedShade,
                shadeMapping.Buffer,
                shadeMapping.OffsetBytes,
                shadeMapping.SizeBytes);
        }

        var directives = cycle.ReserveLoop(
            paintTally * sizeof(DrawElementsIndirectDirective),
            GpuLoopPurpose.Indirect);
        MemoryMarshal.AsBytes(_deicTemp.AsSpan(0, paintTally))
            .CopyTo(directives.Data);
        coder.MultiPaintIndexedIndirect(
            directives.Buffer,
            directives.ShiftOctets,
            (uint)paintTally,
            (uint)sizeof(DrawElementsIndirectDirective));
    }

    // Binds the immutable 36-entry tiling table
    private void AttachTilingChart(IGpuSweepCoder coder)
    {
        if (_tilingBuf is null)
        {
            if (_tileset.TilingByStratum.Count != LandBitmapTilingChart.StratumCap)
            {
                throw new InvalidOperationException(
                    $"Terrain tiling table has {_tileset.TilingByStratum.Count} entries; " +
                    $"wanted {LandBitmapTilingChart.StratumCap}.");
            }

            Span<byte> chunk = stackalloc byte[LandBitmapTilingChart.UniformBufOctets];
            chunk.Clear();
            for (int idx = 0; idx < LandBitmapTilingChart.StratumCap; ++idx)
            {
                BitConverter.TryWriteBytes(
                    chunk[(idx * LandBitmapTilingChart.UniformElemStrideOctets)..],
                    _tileset.TilingByStratum[idx]);
            }

            IClientGpuBuffer buf = DemandDev().BuildBuf(new GpuBufferSpec(
                "terrain-tiling",
                LandBitmapTilingChart.UniformBufOctets,
                GpuBufferPurpose.Uniform | GpuBufferPurpose.TransferDestination,
                GpuMemoryTenancy.DeviceLocal));
            try
            {
                buf.Upload(0, chunk);
            }
            catch
            {
                buf.Dispose();
                throw;
            }
            _tilingBuf = buf;
        }

        coder.AttachUniformBuf(
            GpuBindingModel.UniformLandTiling,
            _tilingBuf,
            0,
            LandBitmapTilingChart.UniformBufOctets);
    }

    private IClientGpuDevice DemandDev()
    {
        return _device ?? throw new InvalidOperationException(
            "LandModernPainter's RHI arm was reached without an IGpuDevice");
    }

    private IClientGpuBuffer DemandVertVault()
    {
        return _vertVault ?? throw new InvalidOperationException(
            "The terrain vertex arena hasn't been created");
    }

    private IClientGpuBuffer DemandOrdinalVault()
    {
        return _ordinalVault ?? throw new InvalidOperationException(
            "The terrain index arena hasn't been created");
    }

    private void TeardownRhi()
    {
        _directedShadeRecipient?.Dispose();
        _directedShadeRecipient = null;
        _pipe?.Dispose();
        _pipe = null;
        _tilingBuf?.Dispose();
        _tilingBuf = null;
        _vertVault?.Dispose();
        _vertVault = null;
        _ordinalVault?.Dispose();
        _ordinalVault = null;
        _globalVboCapOctets = 0;
        _globalEboCapOctets = 0;
        _dynamicCycleBegun = false;
        _destroyed = true;
    }
}
