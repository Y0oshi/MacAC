using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Packs;
using MacAC.Client.Graphics.Stage;

namespace MacAC.Client.Graphics;

internal readonly record struct AtmosphericFrameBufferWiring(
    IClientGpuBuffer? Buffer,
    uint OffsetBytes,
    uint SizeBytes)
{
    internal bool IsTied => Buffer is not null;
}

internal readonly record struct DirectionalSunShadeRenderInput(
    DirectionalShadeEnvironmentInput Environment,
    Matrix4x4 CameraView,
    Matrix4x4 CameraProjection,
    DirectionalShadeCasterFrame Casters,
    float CameraNearMeters = 0.1f,
    float CasterDepthPaddingMeters = 48f,
    float ResidentMaximumReachMeters = float.PositiveInfinity,
    bool MeasureGpuTimers = true,
    bool MeasureCpuStages = false,
    AtmosphericFrameBufferWiring AtmosphericFrame = default,
    CanonLandscapeVisibilityFrame PriorLandscapeVisibility = default,
    bool AllowTopologyRebuild = true);

internal readonly record struct DirectionalSunShadeCpuStageTicks(
    long EnvironmentGateTicks,
    long PreparedDrawsAndTransformsTicks,
    long FitAndUniformTicks,
    long LayeredPassRecordingTicks,
    long BookkeepingTicks);

internal readonly record struct DirectionalShadeTransformChurnTelemetry(
    int CopiedSceneChanges,
    int UpdateTransformChanges,
    int UpdateAppearanceChanges,
    int DynamicSynchronizationChanges,
    int ActiveAnimatedStaticChanges,
    int LiveDynamicRootChanges,
    int EquippedChildChanges,
    int DedupedCasterSlots,
    bool SceneJournalFullRefresh,
    bool DensityBulkRefresh,
    int BatchedProjectionCopyCalls,
    int ChangedMatrixSlots,
    int FlightCurrentChangedMatrices,
    int FlightPendingReplayMatrices,
    int FlightUploadedMatrices,
    int FlightUploadRanges,
    long FlightBytesWritten,
    bool FlightFullDynamicFallback,
    bool DenseDirectUpload,
    bool DenseFlightReplay,
    DirectionalShadeCasterClassTelemetry CasterClasses = default,
    int ActiveSelectedCasters = 0);

internal readonly record struct DirectionalSunShadeTelemetry(
    DirectionalShadeTurnstileReason GateReason,
    float Strength,
    int CascadeCount,
    int DrawCalls,
    int WorldOpaqueCommands,
    int WorldAlphaCutoutCommands,
    int TerrainCommands,
    ulong WorldPreparationSequence,
    ulong TerrainPreparationSequence,
    double CpuMilliseconds,
    double LastResolvedGpuMilliseconds,
    bool HasResolvedGpuMeasurement,
    long ResidentDepthBytes,
    DirectionalSunShadeCpuStageTicks CpuStages = default,
    DirectionalShadeTransformChurnTelemetry TransformChurn = default,
    AuthoredCelestialShadeSourceKind SourceKind =
        AuthoredCelestialShadeSourceKind.None,
    int SourceObjectIndex = -1,
    uint SourceGfxObjId = 0u,
    Vector3 SurfaceToLightDirection = default,
    float LightElevationSin = 0f,
    int ResidentWorldCasters = 0,
    int ActiveWorldCasters = 0,
    int ResidentWorldInstances = 0,
    int ActiveWorldInstances = 0,
    int ResidentWorldCommands = 0,
    int ActiveWorldCommands = 0,
    int ResidentTerrainCommands = 0,
    int ActiveTerrainCommands = 0);

internal static class DirectionalShadeBatchFlags
{
    internal const uint AlphaCutout = 1u << 0;
    internal static uint Pack(DirectionalShadeCasterMaterial matl)
    {
        return matl is DirectionalShadeCasterMaterial.AlphaCutout
            ? AlphaCutout
            : 0u;
    }
}

internal sealed partial class DirectionalSunShadePainter : IDirectionalShadeReceiverSource, IDisposable
{
    internal const string TickerStem = "directional-shadow-cascade-";

    internal const string MultiviewTickerLabel = "directional-shadow-multiview";

    internal const uint LoMultiviewBitmask = 0b11;

    private const int PaintDirectiveStride = 20;

    private readonly IClientGpuDevice _device;
    private readonly DirectionalShadeAtmosphereRule _atmosphereRule;
    private readonly IGpuDirectedZDepthMark _mark;

    private readonly IClientGpuSampler _sampler;
    private readonly IGpuPipe _landPipe;

    private readonly IGpuPipe _realmSolidPipe;

    private readonly IGpuPipe _realmCutoutPipe;

    private readonly IGpuPipe? _landMultiviewPipe;

    private readonly IGpuPipe? _realmSolidMultiviewPipe;

    private readonly IGpuPipe? _realmCutoutMultiviewPipe;

    private readonly DirectionalShadeTransformBufferSet _xformBufs;

    private readonly DirectionalShadeCascade[] _cascades = new DirectionalShadeCascade[4];

    private DirectionalShadeBatchGpuData[] _lotTemp = [];

    private IClientGpuBuffer? _realmLotBuf;

    private IClientGpuBuffer? _realmDirectiveBuf;

    private IClientGpuBuffer? _landDirectiveBuf;

    private ulong _realmGpuAssembleSeries;

    private ulong _landGpuAssembleSeries;
    private bool _destroyed;

    internal DirectionalSunShadePainter(
        IClientGpuDevice dev,
        DirectionalShadePreset preset,
        DirectionalShadeAtmosphereRule? atmosphereRule = null,
        DirectionalShadePipelineShaders? pipeShaders = null,
        bool multiviewCascades = false)
        : this(
            dev,
            DirectionalShadeQuality.For(preset),
            atmosphereRule,
            pipeShaders,
            multiviewCascades)
    {
    }

    internal DirectionalSunShadePainter(
        IClientGpuDevice device,
        DirectionalShadeQuality quality,
        DirectionalShadeAtmosphereRule? atmosphereRule = null,
        DirectionalShadePipelineShaders? pipeShaders = null,
        bool multiviewCascades = false)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        if (quality.CascadeCount is < 1 or > 4
            || quality.MapResolution <= 0
            || !float.IsFinite(quality.MaximumReachMeters)
            || quality.MaximumReachMeters <= 0f
            || quality.PcfRadiusTexels is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quality),
                "Directional-shadow quality must declare 1..4 cascades, a positive "
                + "resolution/reach, and a 0..2 PCF radius");
        }
        _fidelity = quality;
        _atmosphereRule = atmosphereRule ?? DirectionalShadeAtmosphereRule.BuiltIn;
        _pipeShaders = pipeShaders ?? DirectionalShadePipelineShaders.Local;
        _multiviewCascades = multiviewCascades;
        if (multiviewCascades && quality.CascadeCount is not 2)
            throw new NotSupportedException("The multiview shadow hint needs precisely two Low cascades");
        if (multiviewCascades && !device.Capabilities.SupportsMultiview)
            throw new NotSupportedException("The selected device doesn't support multiview shadow cascades");
        if (multiviewCascades && _pipeShaders.MultiviewCasters is null)
            throw new NotSupportedException("The pack didn't declare all multiview shadow caster variants");

        IGpuDirectedZDepthMark? mark = null;
        IClientGpuSampler? sampler = null;
        var textureSocket = GpuTextureSlot.Unassigned;
        IGpuPipe? land = null;
        IGpuPipe? solid = null;
        IGpuPipe? cutout = null;
        IGpuPipe? landMultiview = null;
        IGpuPipe? solidMultiview = null;
        IGpuPipe? cutoutMultiview = null;
        DirectionalShadeTransformBufferSet? xformBufs = null;
        try
        {
            mark = device.BuildDirectedZDepthMark(
                new GpuDirectionalDepthTargetSpec(
                    $"directional-shadow-{quality.Preset.ToString().ToLowerInvariant()}",
                    _fidelity.MapResolution,
                    _fidelity.CascadeCount));
            sampler = device.BuildSampler(GpuSamplerSpec.ShadeClosestClamp);
            textureSocket = device.EnrollTexture(mark.ZDepthTexture, sampler);
            land = BuildPipe(
                device,
                "directional-shadow-terrain",
                _pipeShaders.TerrainCaster,
                LandModernPainter.LandVertArrangement,
                GpuFrontFacet.CounterClockwise);
            solid = BuildPipe(
                device,
                "directional-shadow-world-opaque",
                _pipeShaders.WorldOpaqueCaster,
                GpuVertexArrangement.RealmTriMesh,
                GpuFrontFacet.Clockwise);
            cutout = BuildPipe(
                device,
                "directional-shadow-world-cutout",
                _pipeShaders.WorldAlphaCutoutCaster,
                GpuVertexArrangement.RealmTriMesh,
                GpuFrontFacet.Clockwise);
            if (multiviewCascades)
            {
                var shaders =
                    _pipeShaders.MultiviewCasters!.Value;
                landMultiview = BuildPipe(device, "directional-shadow-terrain-multiview",
                    shaders.TerrainCaster, LandModernPainter.LandVertArrangement,
                    GpuFrontFacet.CounterClockwise, LoMultiviewBitmask);
                solidMultiview = BuildPipe(device, "directional-shadow-world-opaque-multiview",
                    shaders.WorldOpaqueCaster, GpuVertexArrangement.RealmTriMesh,
                    GpuFrontFacet.Clockwise, LoMultiviewBitmask);
                cutoutMultiview = BuildPipe(device, "directional-shadow-world-cutout-multiview",
                    shaders.WorldAlphaCutoutCaster, GpuVertexArrangement.RealmTriMesh,
                    GpuFrontFacet.Clockwise, LoMultiviewBitmask);
            }
            xformBufs = new DirectionalShadeTransformBufferSet(device);
        }
        catch
        {
            xformBufs?.Dispose();
            cutoutMultiview?.Dispose();
            solidMultiview?.Dispose();
            landMultiview?.Dispose();
            cutout?.Dispose();
            solid?.Dispose();
            land?.Dispose();
            if (textureSocket.IsAssigned)
                device.FreeTextureSocket(textureSocket);
            sampler?.Dispose();
            mark?.Dispose();
            throw;
        }

        _mark = mark;
        _sampler = sampler;
        _textureSocket = textureSocket;
        _landPipe = land;
        _realmSolidPipe = solid;
        _realmCutoutPipe = cutout;
        _landMultiviewPipe = landMultiview;
        _realmSolidMultiviewPipe = solidMultiview;
        _realmCutoutMultiviewPipe = cutoutMultiview;
        _xformBufs = xformBufs;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct DirectionalShadeBatchGpuData(
        uint TextureIndex,
        uint Reserved,
        uint TextureLayer,
        uint Flags);

    private readonly record struct KeptGpuBufferSlice(
        IClientGpuBuffer? Buffer,
        uint OffsetBytes,
        uint SizeBytes)
    {
        internal IClientGpuBuffer DemandBuf()
        {
            return Buffer
            ?? throw new InvalidOperationException(
                "A non-empty directional-shadow draw has no retained GPU buffer");
        }
    }

    private readonly record struct ReadiedGpuUploads(
        RealmTransformFrameSlice transforms,
        KeptGpuBufferSlice batches,
        KeptGpuBufferSlice worldCommands,
        KeptGpuBufferSlice terrainCommands)
    {
        internal RealmTransformFrameSlice Xforms { get; } = transforms;
        internal KeptGpuBufferSlice Batches { get; } = batches;
        internal KeptGpuBufferSlice RealmDirectives { get; } = worldCommands;
        internal KeptGpuBufferSlice LandDirectives { get; } = terrainCommands;
    }
}
