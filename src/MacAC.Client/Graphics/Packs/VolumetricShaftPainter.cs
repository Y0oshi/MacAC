using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Gpu;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal enum VolumetricShaftTurnstileReason : byte
{
    Rendered,
    DisabledByPreset,
    NoCurrentDirectionalShadow,
    NoSceneDepth,
    Indoor,
    SunOffScreen,
    SunBelowHorizon,
    AtmosphereSuppressed,
}

internal readonly record struct VolumetricShaftTelemetry(
    VolumetricShaftTurnstileReason GateReason,
    int Width,
    int Height,
    int RayMarchSteps,
    float Density,
    float Strength,
    long RetainedGpuBytes,
    double LastResolvedGpuMilliseconds,
    bool HasResolvedGpuMeasurement,
    int DrawCalls);

internal readonly record struct VolumetricShaftProduct(
    GpuTextureSlot TextureSlot,
    VolumetricShaftTelemetry Diagnostics)
{
    internal bool HasTexture => TextureSlot.IsAssigned;
}

internal sealed class VolumetricShaftPainter : IDisposable
{
    internal const string TickerLabel = "atmospheric-volumetric-shafts";

    private readonly IClientGpuDevice _device;
    private readonly float _declaredStrength;
    private readonly AtmospherePolicySpec _atmosphereRule;
    private readonly IReadOnlyDictionary<int, float> _dayClusterMultipliers;
    private readonly IClientGpuSampler _sampler;
    private readonly IGpuPipe _pipe;
    private readonly PackPreferencesUniforms _prefs;
    private readonly RasterizeBundlePerformancePane _performance = new();
    private Mark? _mark;
    private bool _destroyed;

    internal VolumetricShaftPainter(
        IClientGpuDevice dev,
        RenderPackCard descriptor,
        IRenderPackFiles holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions = null)
        : this(
            dev,
            descriptor,
            RasterizeBundleShaderHoldings.Validate(descriptor, holdings),
            preset,
            userSettingSubstitutions)
    {
    }

    internal VolumetricShaftPainter(
        IClientGpuDevice device,
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(preset);
        _fidelity = LocateFidelity(
            descriptor,
            preset,
            userSettingSubstitutions);
        _declaredStrength = ScanSetting(
            descriptor,
            preset,
            userSettingSubstitutions,
            RenderSettingRole.VolumetricStrength,
            0.35f);
        _atmosphereRule = descriptor.AtmospherePolicy
            ?? throw new NotSupportedException(
                $"Pack '{descriptor.Id}' declares no atmosphere policy");
        if (_atmosphereRule.VolumetricShaftSunElevationResponse.Count < 2)
        {
            throw new NotSupportedException(
                $"Pack '{descriptor.Id}' declares no volumetric-shaft elevation curve");
        }
        _dayClusterMultipliers = _atmosphereRule.ActiveDayGroupMultipliers
            .ToDictionary(val => val.ActiveDayGroup, val => (float)val.Multiplier);
        _prefs = PackPreferencesUniforms.Create(descriptor, preset, userSettingSubstitutions);
        RenderPassSpec pass = descriptor.Passes.FirstOrDefault(val =>
            val.Semantic == RenderPassRole.VolumetricShafts)
            ?? throw new NotSupportedException(
                $"Pack '{descriptor.Id}' declares no VolumetricShafts pass semantic");

        _sampler = device.BuildSampler(GpuSamplerSpec.RealmClamp);
        _pipe = device.BuildPipe(new GpuPipeSpec
        {
            Name = $"render-pack-{descriptor.Id}-volumetric-shafts",
            Shaders = RasterizeBundleShaderHoldings.PullPass(descriptor, holdings, pass),
            VertArrangement = GpuVertexArrangement.None,
            Blend = GpuBlendManner.None,
            Depth = GpuDepthLedger.Disabled,
            Cull = GpuPruneManner.None,
            TintFmt = GpuBitmapFmt.Rgba16FloatRenderTarget,
            AllowTintFmtVariants = false,
            SampleCount = 1,
            UsesRasterizeBundleShaderAbi = true,
        });
        PreviousTelemetry = Disabled(VolumetricShaftTurnstileReason.DisabledByPreset);
    }

    internal VolumetricShaftTelemetry PreviousTelemetry { get; private set; }

    private readonly VolumetricShaftFidelity _fidelity;

    internal VolumetricShaftFidelity Quality => _fidelity;
    internal RenderPackPerformanceCapture Performance => _performance.Freeze();

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _mark?.Dispose();
        _mark = null;
        _pipe.Dispose();
    }

    internal void ReadyMark(int productWidth, int productHeight)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productHeight);
        if (_declaredStrength > 0f)
            _ = Prepare(productWidth, productHeight);
    }

    internal VolumetricShaftProduct Render(
        IGpuCycle cycle,
        in AtmosphericCycleFeeds feeds,
        in DirectionalShadeFrameWiring shade,
        GpuTextureSlot tableauZDepth)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(cycle);
        var cause = Latch(cycle, feeds, shade, tableauZDepth);
        if (cause != VolumetricShaftTurnstileReason.Rendered)
        {
            PreviousTelemetry = Disabled(cause);
            return new VolumetricShaftProduct(GpuTextureSlot.Unassigned, PreviousTelemetry);
        }

        (float density, float strength) = Params(feeds);
        if (strength <= 1e-4f)
        {
            PreviousTelemetry = Disabled(VolumetricShaftTurnstileReason.AtmosphereSuppressed);
            return new VolumetricShaftProduct(GpuTextureSlot.Unassigned, PreviousTelemetry);
        }

        Mark mark = Prepare(feeds.ViewportWidth, feeds.ViewportHeight);
        long begun = Stopwatch.GetTimestamp();
        var atmospheric = CycleUniforms(feeds, strength);
        var cycleChunk = cycle.ReserveLoop(
            AtmosphericCycleUniforms.SizeInBytes,
            GpuLoopPurpose.Uniform);
        MemoryMarshal.Write(cycleChunk.Data, in atmospheric);
        var passChunk = cycle.ReserveLoop(
            AtmosphericBundleSweepUniforms.SizeInBytes,
            GpuLoopPurpose.Uniform);
        var passVals = new AtmosphericBundleSweepUniforms(
            new Vector4(density, strength, _fidelity.RayMarchSteps, 1f),
            Vector4.Zero,
            Vector4.Zero,
            Vector4.Zero);
        MemoryMarshal.Write(passChunk.Data, in passVals);
        var prefsChunk = cycle.ReserveLoop(
            PackPreferencesUniforms.SizeInBytes,
            GpuLoopPurpose.Uniform);
        var prefs = _prefs;
        MemoryMarshal.Write(prefsChunk.Data, in prefs);

        using (IGpuSweepCoder coder = cycle.BeginPass(new GpuPassSpec
        {
            Name = TickerLabel,
            Color = new GpuTintAffix(
                mark.RasterizeMark,
                GpuPullOp.Clear,
                GpuVaultOp.Store,
                Vector4.Zero),
            ZDepth = null,
            SampleCount = 1,
        }))
        using (coder.CommenceTickerAmbit(TickerLabel))
        {
            coder.BindPipeline(_pipe);
            coder.AttachUniformBuf(
                GpuBindingModel.UniformAtmosphericCycle,
                cycleChunk.Buffer,
                cycleChunk.ShiftOctets,
                AtmosphericCycleUniforms.SizeInBytes);
            coder.AttachUniformBuf(
                GpuBindingModel.UniformDirectedShade,
                shade.Buffer!,
                shade.OffsetBytes,
                shade.SizeBytes);
            coder.AttachUniformBuf(
                GpuBindingModel.UniformBundlePass,
                passChunk.Buffer,
                passChunk.ShiftOctets,
                AtmosphericBundleSweepUniforms.SizeInBytes);
            coder.AttachUniformBuf(
                GpuBindingModel.UniformBundlePrefs,
                prefsChunk.Buffer,
                prefsChunk.ShiftOctets,
                PackPreferencesUniforms.SizeInBytes);
            var push = GpuShoveConstants.Default;
            push.TextureIndexA = tableauZDepth.Index;
            push.TextureOrdinalB = GpuTextureSlot.Unassigned.Index;
            push.ParamA = BitConverter.UInt32BitsToSingle(GpuTextureSlot.Unassigned.Index);
            push.ParameterB = BitConverter.UInt32BitsToSingle(GpuTextureSlot.Unassigned.Index);
            coder.AssignPushConstants(in push);
            coder.Draw(3, 1, 0, 0);
        }

        bool hasGpu = _device.Tickers.TryLocate(TickerLabel, out double millis);
        PreviousTelemetry = new VolumetricShaftTelemetry(
            VolumetricShaftTurnstileReason.Rendered,
            mark.RasterizeMark.Description.Width,
            mark.RasterizeMark.Description.Height,
            _fidelity.RayMarchSteps,
            density,
            strength,
            mark.KeptOctets,
            millis,
            hasGpu,
            DrawCalls: 1);
        _performance.Observe(
            Stopwatch.GetElapsedTime(begun).TotalMilliseconds,
            absoluteReceiverCpuMilliseconds: 0d,
            hasGpu,
            millis,
            mark.KeptOctets,
            transientGpuBytes: 0);
        return new VolumetricShaftProduct(mark.TextureSlot, PreviousTelemetry);
    }

    private Mark Prepare(int productWidth, int productHeight)
    {
        int width = Math.Max(1, (int)MathF.Ceiling(productWidth * _fidelity.ResolutionScale));
        int height = Math.Max(1, (int)MathF.Ceiling(productHeight * _fidelity.ResolutionScale));
        if (_mark is { } latest
            && latest.RasterizeMark.Description.Width == width
            && latest.RasterizeMark.Description.Height == height)
            return latest;

        IGpuRasterizeMark? rasterizeMark = null;
        var socket = GpuTextureSlot.Unassigned;
        try
        {
            rasterizeMark = _device.BuildRasterizeMark(new GpuRenderTargetSpec(
                "atmospheric-volumetric",
                width,
                height,
                GpuBitmapFmt.Rgba16FloatRenderTarget,
                DepthFormat: null,
                SampleCount: 1));
            socket = _device.EnrollTexture(rasterizeMark.ColorTexture, _sampler);
            Mark contender = new Mark(_device, rasterizeMark, socket);
            rasterizeMark = null;
            socket = GpuTextureSlot.Unassigned;
            Mark? preceding = _mark;
            _mark = contender;
            preceding?.Dispose();
            _performance.Reset();
            return contender;
        }
        catch
        {
            if (socket.IsAssigned)
                _device.FreeTextureSocket(socket);
            rasterizeMark?.Dispose();
            throw;
        }
    }

    private VolumetricShaftTurnstileReason Latch(
        IGpuCycle cycle,
        in AtmosphericCycleFeeds feeds,
        in DirectionalShadeFrameWiring shade,
        GpuTextureSlot tableauZDepth)
    {
        if (_declaredStrength <= 0f)
            return VolumetricShaftTurnstileReason.DisabledByPreset;
        if (!shade.IsValidFor(cycle))
            return VolumetricShaftTurnstileReason.NoCurrentDirectionalShadow;
        if (!tableauZDepth.IsAssigned)
            return VolumetricShaftTurnstileReason.NoSceneDepth;
        if (!feeds.IsOutdoor)
            return VolumetricShaftTurnstileReason.Indoor;
        return !feeds.SunIsOnScreen ? VolumetricShaftTurnstileReason.SunOffScreen : VolumetricShaftTurnstileReason.Rendered;
    }

    private (float Density, float Strength) Params(in AtmosphericCycleFeeds feeds)
    {
        float weatherMark = feeds.Weather switch
        {
            MacAC.Mechanics.Realm.WeatherKind.Clear => 1f,
            MacAC.Mechanics.Realm.WeatherKind.Overcast => 0.18f,
            MacAC.Mechanics.Realm.WeatherKind.Rain => 0.10f,
            MacAC.Mechanics.Realm.WeatherKind.Snow => 0.16f,
            MacAC.Mechanics.Realm.WeatherKind.Storm => 0.06f,
            _ => 0f,
        };
        float weatherBlend = Math.Clamp(feeds.WeatherIntensity, 0f, 1f);
        float weather = 1f + ((weatherMark - 1f) * weatherBlend);
        float elevation = RenderPackAtmosphereRuleEvaluation.VolumetricShaft(
            _atmosphereRule.VolumetricShaftSunElevationResponse,
            feeds.SunElevationDegrees);
        float authoredEnergy = Math.Clamp(feeds.SunDirectionalBrightness, 0f, 4f);
        float dayCluster = _dayClusterMultipliers.TryGetValue(
            feeds.ActiveDayGroup,
            out float declaredDayCluster)
                ? Math.Clamp(declaredDayCluster, 0f, 4f)
                : 1f;
        float strength = Math.Clamp(
            _declaredStrength * weather * elevation * authoredEnergy * dayCluster,
            0f,
            1f);
        return (0.035f * strength, strength);
    }

    private AtmosphericCycleUniforms CycleUniforms(
        in AtmosphericCycleFeeds feeds,
        float strength)
    {
        return new(
        new Vector4(feeds.SunScreenUv, strength, feeds.SunElevationDegrees),
        new Vector4(feeds.SunColor, strength),
        new Vector4(feeds.ViewportWidth, feeds.ViewportHeight,
            1f / feeds.ViewportWidth, 1f / feeds.ViewportHeight),
        new Vector4((float)feeds.Weather, feeds.WeatherIntensity,
            (float)Math.Clamp(feeds.DeltaSeconds, 0d, 1d), feeds.IsOutdoor ? 1f : 0f),
        new Vector4(feeds.SunDirection, feeds.SunDirectionalBrightness),
        new Vector4(
            feeds.ActiveDayGroup,
            _dayClusterMultipliers.TryGetValue(feeds.ActiveDayGroup, out float dayCluster)
                ? dayCluster
                : 1f,
            RenderPackAtmosphereRuleEvaluation.DirectionalShadow(
                _atmosphereRule.DirectedShadeLampElevationResponse,
                feeds.SunElevationDegrees),
            RenderPackAtmosphereRuleEvaluation.VolumetricShaft(
                _atmosphereRule.VolumetricShaftSunElevationResponse,
                feeds.SunElevationDegrees)),
        feeds.InverseViewProjection,
        Vector4.Zero,
        Vector4.Zero);
    }

    private VolumetricShaftTelemetry Disabled(VolumetricShaftTurnstileReason cause)
    {
        return new(
        cause,
        0,
        0,
        _fidelity.RayMarchSteps,
        0f,
        0f,
        _mark?.KeptOctets ?? 0L,
        0d,
        false,
        0);
    }

    private static DirectionalShadePreset PresetOf(QualityLadderStep preset)
    {
        return preset.Semantic switch
        {
            RenderQualityRole.Low => DirectionalShadePreset.Low,
            RenderQualityRole.High => DirectionalShadePreset.High,
            _ => DirectionalShadePreset.Medium,
        };
    }

    private static VolumetricShaftFidelity LocateFidelity(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions)
    {
        VolumetricShaftFidelity fidelity = VolumetricShaftFidelity.For(PresetOf(preset));
        var asset = descriptor.Resources.Single(val =>
            val.Semantic == RenderResourceRole.VolumetricShafts);
        var assetOverride = preset.ResourceOverrides
            .FirstOrDefault(val => string.Equals(
                val.ResourceId,
                asset.Id,
                StringComparison.OrdinalIgnoreCase));
        RenderExtentSpec reach = assetOverride?.Extent
            ?? asset.Extent
            ?? throw new NotSupportedException(
                "The VolumetricShafts semantic resource has no image extent");
        if (reach.Mode is not ExtentRule.RelativeToMainWorld
            and not ExtentRule.RelativeToOutput)
        {
            throw new NotSupportedException(
                "The VolumetricShafts semantic resource must use a relative extent");
        }
        int hops = checked((int)MathF.Round(ScanSetting(
            descriptor,
            preset,
            userSettingSubstitutions,
            RenderSettingRole.VolumetricRayMarchSteps,
            fidelity.RayMarchSteps)));
        return fidelity with
        {
            ResolutionScale = (float)Math.Clamp(reach.Width, 0.0625, 1.0),
            RayMarchSteps = Math.Clamp(hops, 8, 64),
        };
    }

    private static float ScanSetting(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions,
        RenderSettingRole semantic,
        float backup)
    {
        var setting = descriptor.Settings.FirstOrDefault(contender =>
            contender.Semantic == semantic);
        if (setting is null)
            return backup;
        string val = RenderPackPreferenceResolution.Resolve(
            setting,
            preset,
            userSettingSubstitutions);
        return SettingValueCodec.TryPack(setting, val, out float encoded)
            ? Math.Max(0f, encoded)
            : backup;
    }

    private sealed class Mark(
        IClientGpuDevice dev,
        IGpuRasterizeMark rasterizeMark,
        GpuTextureSlot textureSocket) : IDisposable
    {
        internal IGpuRasterizeMark RasterizeMark { get; } = rasterizeMark;
        internal GpuTextureSlot TextureSlot { get; } = textureSocket;
        internal long KeptOctets
        {
            get
            {
                return checked(
            (long)RasterizeMark.Description.Width * RasterizeMark.Description.Height * 8L);
            }
        }

        public void Dispose()
        {
            dev.FreeTextureSocket(TextureSlot);
            RasterizeMark.Dispose();
        }
    }
}
