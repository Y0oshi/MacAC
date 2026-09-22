using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Stage;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal readonly record struct AtmosphericPostProcessPreferences(
    float BloomStrength,
    float FilmicStrength,
    float Exposure,
    float Saturation,
    float Contrast,
    float VignetteStrength,
    float SunRayStrength)
{
    internal static AtmosphericPostProcessPreferences Neutral { get; } = new(
        BloomStrength: 0f,
        FilmicStrength: 0f,
        Exposure: 1f,
        Saturation: 1f,
        Contrast: 1f,
        VignetteStrength: 0f,
        SunRayStrength: 0f);

    internal static AtmosphericPostProcessPreferences FromDescriptor(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(preset);
        return new AtmosphericPostProcessPreferences(
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.BloomStrength, 0.65f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.FilmicStrength, 1f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.Exposure, 1f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.GradeSaturation, 1f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.GradeContrast, 1f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.VignetteStrength, AtmosphericPostProcGraph.DefaultVignetteStrengthBackup),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.SunRayStrength, 0.55f));
    }

    private static float Read(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions,
        RenderSettingRole semantic,
        float backup)
    {
        RenderSettingSpec? setting = descriptor.Settings.FirstOrDefault(val =>
            val.Semantic == semantic);
        if (setting is null)
            return backup;
        string value = RenderPackPreferenceResolution.Resolve(
            setting,
            preset,
            userSettingSubstitutions);
        return SettingValueCodec.TryPack(setting, value, out float encoded)
            ? encoded
            : backup;
    }
}

internal readonly record struct FoliageWindPreferences(
    bool Enabled,
    float Strength,
    float DirectionDegrees,
    float LeanMetres,
    float BranchMetres,
    float FlutterMetres,
    float CanopyHeightMetres)
{
    internal static FoliageWindPreferences FromDescriptor(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(preset);
        return new FoliageWindPreferences(
            ScanBool(descriptor, preset, userSettingSubstitutions, RenderSettingRole.WindEnabled, true),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.WindStrength, 1f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.WindDirectionDegrees, 225f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.WindLeanMetres, 0.25f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.WindBranchMetres, 0.15f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.WindFlutterMetres, 0.05f),
            Read(descriptor, preset, userSettingSubstitutions, RenderSettingRole.WindCanopyHeightMetres, 8f));
    }

    private static float Read(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions,
        RenderSettingRole semantic,
        float backup)
    {
        RenderSettingSpec? setting = descriptor.Settings.FirstOrDefault(val =>
            val.Semantic == semantic);
        if (setting is null)
            return backup;
        string value = RenderPackPreferenceResolution.Resolve(setting, preset, userSettingSubstitutions);
        return SettingValueCodec.TryPack(setting, value, out float encoded)
            ? encoded
            : backup;
    }

    private static bool ScanBool(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions,
        RenderSettingRole semantic,
        bool backup) =>
        Read(descriptor, preset, userSettingSubstitutions, semantic, backup ? 1f : 0f) > 0.5f;
}

internal interface IAtmosphericRealmGraphEngine : IRenderPackEngine
{
    IGpuRasterizeMark PrepareWorldTarget(int width, int height, int specimenTally);

    void PaintPostProc(
        IGpuCycle cycle,
        in AtmosphericCycleFeeds feeds);
}

internal interface IDirectionalShadeRealmGraphEngine :
    IAtmosphericRealmGraphEngine
{
    IDirectionalShadeReceiverSource DirectedShadeRecipients { get; }

    DirectionalSunShadeTelemetry RenderDirectionalShadows(
        IGpuCycle cycle,
        in RasterizeCycleFoundation foundation,
        in RealmRenderFrame realm,
        int engagedDayCluster,
        in RenderStageProbe tableau,
        RealmPaintRouter realmTriMeshes,
        LandModernPainter land);
}

internal sealed class AtmosphericPostProcGraph :
    IDirectionalShadeRealmGraphEngine,
    IRenderPackEngineTelemetrySource,
    IRenderPackEnginePerformanceSource,
    IAtmosphericCpuStageProfileEngine
{
    private readonly IClientGpuDevice _device;
    private readonly IDisposable _hdrPipeTenancy;
    private readonly IClientGpuSampler _linearSampler;
    private readonly IClientGpuSampler _closestSampler;
    private readonly IGpuPipe _sunOcclusion;
    private readonly IGpuPipe _sunRays;
    private readonly IGpuPipe _bloomDownsample;
    private readonly IGpuPipe _bloomBlur;
    private readonly IGpuPipe _filmic;
    private readonly float _shadeStrength;
    private readonly PackPreferencesUniforms _bundlePrefs;
    private readonly FoliageWindPreferences _foliageWind;
    private readonly IReadOnlySet<uint> _foliageWindExclusions;
    private float? _windTimerSecsOverride;
    private readonly System.Diagnostics.Stopwatch _windTimer =
        System.Diagnostics.Stopwatch.StartNew();
    private long _windCycleSerialNo = -1;
    private float _windTimerSecs;
    private float _windPreviousProceedTimerSecs;
    private float _windMean;
    private float _windGust;
    private readonly DirectionalSunShadePainter _directedShades;
    private readonly VolumetricShaftPainter? _volumetric;
    private readonly bool _fuseLoPostProc;
    private readonly AtmosphericCpuJunctureProfiler? _cpuJunctureProfiler;
    private readonly DirectionalShadeCasterFrame _shadowCasters = new();
    private ulong _previousObservedTableauShadeRev;
    private long _previousObservedReadinessVer;
    private int _shadeReassembleDeferrals;
    private MarkGroup? _targets;
    private AtmosphericCycleFeeds _previousFeeds;
    private DirectionalSunShadeTelemetry _lastShadowDiagnostics;
    private int _previousShadeInvokerTally;
    private int _previousShadeTaxonomyCalls;
    private long _previousShadeCycleSerialNo = -1;
    private RealmPaintRouter? _previousShadeRealmTriMeshes;
    private AtmosphericCpuJunctureCycle _cpuJunctureCycle;
    private long _housedGpuAllowanceOctets;
    private bool _renderedCycle;
    private bool _destroyed;

    internal const float BloomThresholdLinear = 1f;
    internal const float DefaultVignetteStrengthBackup = 0.245f;

    internal const float BloomKneeLinear = 0.73f;

    internal AtmosphericPostProcGraph(
        IClientGpuDevice dev,
        RenderPackCard descriptor,
        IRenderPackFiles holdings,
        QualityLadderStep preset,
        AtmosphericPostProcessPreferences? prefs = null,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions = null,
        float? windTimerSecsOverride = null)
        : this(
            dev,
            descriptor,
            RasterizeBundleShaderHoldings.Validate(descriptor, holdings),
            preset,
            prefs,
            userSettingSubstitutions,
            windTimerSecsOverride)
    {
    }

    internal AtmosphericPostProcGraph(
        IClientGpuDevice device,
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        QualityLadderStep preset,
        AtmosphericPostProcessPreferences? prefs = null,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions = null,
        float? windTimerSecsOverride = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ArgumentNullException.ThrowIfNull(holdings);
        Preset = preset ?? throw new ArgumentNullException(nameof(preset));
        if (device is not IGpuPipeFmtVariantHub variants)
        {
            throw new NotSupportedException(
                "The active RHI cannot prebuild HDR variants of the normal world pipelines.");
        }

        IDisposable? tenancy = null;
        DirectionalSunShadePainter? directedShades = null;
        VolumetricShaftPainter? volumetric = null;
        var built = new List<IGpuPipe>(capacity: 5);
        try
        {
            tenancy = variants.ObtainPipeTintFmt(
                GpuBitmapFmt.Rgba16FloatRenderTarget);
            _linearSampler = device.BuildSampler(GpuSamplerSpec.RealmClamp);
            _closestSampler = device.BuildSampler(GpuSamplerSpec.WidgetClosest);
            _sunOcclusion = BuildPipe(
                device,
                "atmospheric-sun-occlusion",
                ShaderSet(descriptor, holdings, RenderPassRole.SunOcclusion),
                GpuBitmapFmt.Rgba8UnormRenderTarget);
            built.Add(_sunOcclusion);
            _sunRays = BuildPipe(
                device,
                "atmospheric-sun-rays",
                ShaderSet(descriptor, holdings, RenderPassRole.SunRays),
                GpuBitmapFmt.Rgba16FloatRenderTarget);
            built.Add(_sunRays);
            _bloomDownsample = BuildPipe(
                device,
                "atmospheric-bloom-downsample",
                ShaderSet(descriptor, holdings, RenderPassRole.BloomDownsample),
                GpuBitmapFmt.Rgba16FloatRenderTarget);
            built.Add(_bloomDownsample);
            _bloomBlur = BuildPipe(
                device,
                "atmospheric-bloom-blur",
                ShaderSet(descriptor, holdings, RenderPassRole.BloomBlurHorizontal),
                GpuBitmapFmt.Rgba16FloatRenderTarget);
            built.Add(_bloomBlur);
            _filmic = BuildPipe(
                device,
                "atmospheric-filmic",
                ShaderSet(descriptor, holdings, RenderPassRole.FilmicComposite),
                GpuBitmapFmt.Rgba8UnormRenderTarget);
            built.Add(_filmic);
            _prefs = prefs
                ?? AtmosphericPostProcessPreferences.FromDescriptor(
                    descriptor,
                    preset,
                    userSettingSubstitutions);
            _foliageWind = FoliageWindPreferences.FromDescriptor(
                descriptor,
                preset,
                userSettingSubstitutions);
            _foliageWindExclusions =
                (descriptor.AtmospherePolicy?.FoliageExclusions
                    ?? (IReadOnlyList<uint>)[]).ToFrozenSet();
            _windTimerSecsOverride = windTimerSecsOverride;
            _bundlePrefs = PackPreferencesUniforms.Create(
                descriptor,
                preset,
                userSettingSubstitutions);
            _fuseLoPostProc = (preset.ExecutionHints
                & QualityExecutionHints.FusedAtmosphericPostProcess) != 0;
            _cpuJunctureProfiler = preset.Semantic is RenderQualityRole.Low
                ? new AtmosphericCpuJunctureProfiler()
                : null;
            _shadeStrength = ScanSemanticSetting(
                descriptor,
                preset,
                userSettingSubstitutions,
                RenderSettingRole.DirectionalShadowStrength,
                0.72f);
            directedShades = new DirectionalSunShadePainter(
                device,
                LocateShadeFidelity(
                    descriptor,
                    preset,
                    userSettingSubstitutions),
                atmosphereRule:
                    RenderPackAtmosphereRuleEvaluation.NeutralDirectedShadeElevation,
                pipeShaders: PullDirectedShadeShaders(descriptor, holdings),
                multiviewCascades: (preset.ExecutionHints
                    & QualityExecutionHints
                        .MultiviewDirectionalShadowCascades) != 0);
            if (HasPass(descriptor, RenderPassRole.VolumetricShafts))
            {
                volumetric = new VolumetricShaftPainter(
                    device,
                    descriptor,
                    holdings,
                    preset,
                    userSettingSubstitutions);
            }
            _directedShades = directedShades;
            directedShades = null;
            _volumetric = volumetric;
            volumetric = null;
            _hdrPipeTenancy = tenancy;
            tenancy = null;
        }
        catch
        {
            volumetric?.Dispose();
            directedShades?.Dispose();
            for (int idx = built.Count - 1; idx >= 0; idx--)
                built[idx].Dispose();
            tenancy?.Dispose();
            throw;
        }
    }

    public RenderPackCard Descriptor { get; }

    public QualityLadderStep Preset { get; }

    internal int AssetGen { get; private set; }

    private readonly AtmosphericPostProcessPreferences _prefs;

    internal AtmosphericPostProcessPreferences Prefs => _prefs;
    internal VolumetricShaftFidelity? VolumetricFidelity => _volumetric?.Quality;

    public IDirectionalShadeReceiverSource DirectedShadeRecipients =>
        _directedShades;

    public DirectionalSunShadeTelemetry RenderDirectionalShadows(
        IGpuCycle cycle,
        in RasterizeCycleFoundation foundation,
        in RealmRenderFrame realm,
        int engagedDayCluster,
        in RenderStageProbe tableau,
        RealmPaintRouter realmTriMeshes,
        LandModernPainter land)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        bool gaugeCpuJunctures = _cpuJunctureProfiler is not null
            && AtmosphericCpuJunctureProfiler.ShouldMeasure(cycle.SerialNo);
        long junctureBegun = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
        ulong tableauShadeRev = tableau.DirectedShadeWiringRev;
        long readinessVer = realmTriMeshes.DirectedShadeReadinessVer;
        bool shadeFeedsAltered =
            tableauShadeRev != _previousObservedTableauShadeRev
            || readinessVer != _previousObservedReadinessVer;
        _previousObservedTableauShadeRev = tableauShadeRev;
        _previousObservedReadinessVer = readinessVer;
        bool allowWiringReassemble =
            !shadeFeedsAltered || _shadeReassembleDeferrals >= 2;
        ulong invokerSeriesPrior = _shadowCasters.AssembleSequence;
        _shadowCasters.Build(in tableau, allowWiringReassemble);
        CanonLandscapeVisibilityFrame precedingSceneryVis =
            realm.PriorLandscapeVisibility;
        _shadowCasters.Select(
            in precedingSceneryVis,
            realm.DirectionalShadowCellMembership
                ?? EmptyDirectionalShadeCellMembership.Instance);
        if (_shadowCasters.AssembleSequence != invokerSeriesPrior)
            allowWiringReassemble = true;
        _shadeReassembleDeferrals = allowWiringReassemble
            ? 0
            : _shadeReassembleDeferrals + 1;
        long invokerAssembleFinished = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
        AuthoredCelestialShadeSource src = realm.CelestialShadeSrc;
        var surroundings = new DirectionalShadeEnvironmentInput(
            PackEnabled: true,
            PortalOrLoginCoverVisible: foundation.PortalViewportVisible,
            PlayerInsideCell: realm.Roots.AvatarOrCamInsideEnclosedChamber,
            src,
            foundation.Atmosphere,
            DayGroupWeight: Math.Clamp(
                EvaluateDayClusterRule(engagedDayCluster)
                    * RenderPackAtmosphereRuleEvaluation.DirectedShadeFromSin(
                        Descriptor.AtmospherePolicy!
                            .DirectedShadeLampElevationResponse,
                        src.ElevationSin)
                    * _shadeStrength,
                0f,
                1f));
        realmTriMeshes.FoliageWindExclusions = _foliageWindExclusions;
        bool isExterior = realm.Roots.IsAtmosphericallyExterior;
        AtmosphericFrameBufferWiring shadeAtmosphericCycle =
            AssembleShadeAtmosphericCycleMapping(cycle, foundation.Atmosphere.Kind, isExterior);
        var feed = new DirectionalSunShadeRenderInput(
            surroundings,
            realm.Camera.Camera.View,
            realm.Camera.Projection,
            _shadowCasters,
            ResidentMaximumReachMeters:
                realm.HousedPagingPane.CeilingReachMeters,
            MeasureGpuTimers: AtmosphericGpuTickerSampling.ShouldMeasure(
                Preset.Semantic,
                cycle.SerialNo),
            MeasureCpuStages: gaugeCpuJunctures,
            AtmosphericFrame: shadeAtmosphericCycle,
            PriorLandscapeVisibility: realm.PriorLandscapeVisibility,
            AllowTopologyRebuild: allowWiringReassemble);
        long surroundingsFinished = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
        _previousShadeInvokerTally = _shadowCasters.Stats.ActiveSelected;
        _previousShadeTaxonomyCalls = _shadowCasters.Stats.TopologyRebuilt ? 1 : 0;
        _lastShadowDiagnostics = _directedShades.Render(
            cycle,
            in feed,
            realmTriMeshes,
            land);
        _previousShadeRealmTriMeshes = realmTriMeshes;
        if (gaugeCpuJunctures)
        {
            DirectionalSunShadeCpuStageTicks shade = _lastShadowDiagnostics.CpuStages;
            _cpuJunctureCycle = new AtmosphericCpuJunctureCycle(
                cycle.SerialNo,
                invokerAssembleFinished - junctureBegun,
                checked(surroundingsFinished - invokerAssembleFinished
                    + shade.EnvironmentGateTicks),
                shade.PreparedDrawsAndTransformsTicks,
                shade.FitAndUniformTicks,
                shade.LayeredPassRecordingTicks,
                shade.BookkeepingTicks,
                0L,
                0L,
                0L);
        }
        else
        {
            _cpuJunctureCycle = default;
        }
        DemandKeptGpuAllowance();
        _previousShadeCycleSerialNo = cycle.SerialNo;
        return _lastShadowDiagnostics;
    }

    private AtmosphericFrameBufferWiring AssembleShadeAtmosphericCycleMapping(
        IGpuCycle cycle,
        MacAC.Mechanics.Realm.WeatherKind weather,
        bool isExterior)
    {
        (Vector4 timerWind, Vector4 windAmplitude) = LocateFoliageWind(
            cycle.SerialNo,
            weather,
            isExterior);
        GpuLoopAlloc alloc = cycle.ReserveLoop(
            AtmosphericCycleUniforms.SizeInBytes,
            GpuLoopPurpose.Uniform);
        var uniforms = new AtmosphericCycleUniforms(
            Vector4.Zero,
            Vector4.Zero,
            Vector4.Zero,
            Vector4.Zero,
            Vector4.Zero,
            Vector4.Zero,
            Matrix4x4.Identity,
            timerWind,
            windAmplitude);
        MemoryMarshal.Write(alloc.Data, in uniforms);
        return new AtmosphericFrameBufferWiring(
            alloc.Buffer,
            alloc.ShiftOctets,
            (uint)AtmosphericCycleUniforms.SizeInBytes);
    }

    private (Vector4 ClockWind, Vector4 WindAmplitude) LocateFoliageWind(
        long cycleSerialNo,
        MacAC.Mechanics.Realm.WeatherKind weather,
        bool isExterior)
    {
        float timerSecs = _windTimerSecsOverride
            ?? (float)_windTimer.Elapsed.TotalSeconds;
        if (_windCycleSerialNo != cycleSerialNo)
        {
            (float markMean, float markGust) = RenderPackAtmosphereRuleEvaluation
                .FoliageWind(
                    Descriptor.AtmospherePolicy?.FoliageWindByWeather,
                    weather);
            markMean *= _foliageWind.Strength;
            markGust *= _foliageWind.Strength;

            if (_windCycleSerialNo == -1)
            {
                _windMean = markMean;
                _windGust = markGust;
            }
            else
            {
                float diffSecs = Math.Clamp(
                    timerSecs - _windPreviousProceedTimerSecs,
                    0f,
                    1f);
                _windMean = RenderPackAtmosphereRuleEvaluation.EaseTowardMark(
                    _windMean,
                    markMean,
                    diffSecs,
                    MacAC.Mechanics.Realm.WeatherEngine.ChangeoverSecs);
                _windGust = RenderPackAtmosphereRuleEvaluation.EaseTowardMark(
                    _windGust,
                    markGust,
                    diffSecs,
                    MacAC.Mechanics.Realm.WeatherEngine.ChangeoverSecs);
            }

            _windTimerSecs = timerSecs;
            _windPreviousProceedTimerSecs = timerSecs;
            _windCycleSerialNo = cycleSerialNo;
        }

        float latch = _foliageWind.Enabled && isExterior ? 1f : 0f;
        float dirRadians = _foliageWind.DirectionDegrees * (MathF.PI / 180f);
        var timerWind = new Vector4(
            _windTimerSecs,
            _windMean * latch,
            _windGust * latch,
            dirRadians);
        var windAmplitude = new Vector4(
            _foliageWind.LeanMetres,
            _foliageWind.BranchMetres,
            _foliageWind.FlutterMetres,
            _foliageWind.CanopyHeightMetres);
        return (timerWind, windAmplitude);
    }

    internal void AssignWindTimerSecsOverrideForTesting(float secs) =>
        _windTimerSecsOverride = secs;

    public IGpuRasterizeMark PrepareWorldTarget(
        int width,
        int height,
        int specimenTally)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(specimenTally);
        if (_targets is { } latest
            && latest.Width == width
            && latest.Height == height
            && latest.SampleCount == specimenTally)
            return latest.World;

        RasterizeBundleHubCapabilities capabilities =
            RenderPackCapabilityPicker.Resolve(_device.Capabilities);
        RenderPackResourceAllowancePlanner.DemandWithinHub(
            Descriptor,
            Preset,
            width,
            height,
            specimenTally,
            capabilities);
        var contender = MarkGroup.Create(
            _device,
            width,
            height,
            specimenTally,
            PostScaling(Descriptor, Preset),
            RayScaling(Descriptor, Preset),
            reserveBloomIntermediates: !_fuseLoPostProc,
            _linearSampler,
            _closestSampler);
        try
        {
            _volumetric?.ReadyMark(width, height);
        }
        catch
        {
            contender.Dispose();
            throw;
        }
        MarkGroup? earlier = _targets;
        _targets = contender;
        _housedGpuAllowanceOctets = RenderPackResidentAllowance.Net(
            Preset.MaxResidentGpuBytes,
            width,
            height,
            capabilities.MaxPackResidentBytes);
        AssetGen = checked(AssetGen + 1);
        _cpuJunctureProfiler?.Reset();
        earlier?.Dispose();
        return contender.World;
    }

    /// <summary>
    /// What the atmosphere descriptor works out to for one frame. These are read off the sun's
    /// position, the time of day and the weather, and then only ever fed into the frame's uniform
    /// block — so they are computed together rather than as six loose locals partway down a method
    /// that goes on for another three hundred lines.
    /// </summary>
    private readonly record struct FrameRules(
        float Sun,
        float Elevation,
        float DayCluster,
        float ShadeElevation,
        float VolumetricElevation,
        float RayStrength);

    /// <summary>
    /// The uniform blocks one frame writes, wherever they ended up in the ring. A ref struct because
    /// a ring allocation is one: these point into the frame's buffer and must not outlive it.
    /// </summary>
    private readonly ref struct UniformChunks(
        GpuLoopAlloc frame,
        GpuLoopAlloc prefs,
        GpuLoopAlloc fusedSunPass,
        GpuLoopAlloc fusedFilmicPass)
    {
        public GpuLoopAlloc Frame { get; } = frame;
        public GpuLoopAlloc Prefs { get; } = prefs;
        public GpuLoopAlloc FusedSunPass { get; } = fusedSunPass;
        public GpuLoopAlloc FusedFilmicPass { get; } = fusedFilmicPass;
    }

    private FrameRules RulesFor(in AtmosphericCycleFeeds feeds)
    {
        float sun = EvaluateSunRule(feeds);
        float elevation = EvaluateSunElevationRule(feeds.SunElevationDegrees);
        return new FrameRules(
            sun,
            elevation,
            EvaluateDayClusterRule(feeds.ActiveDayGroup),
            RenderPackAtmosphereRuleEvaluation.DirectionalShadow(
                Descriptor.AtmospherePolicy?.DirectedShadeLampElevationResponse,
                feeds.SunElevationDegrees,
                elevation),
            RenderPackAtmosphereRuleEvaluation.VolumetricShaft(
                Descriptor.AtmospherePolicy?.VolumetricShaftSunElevationResponse,
                feeds.SunElevationDegrees),
            // Rays are drawn from the sun itself, so there are none when it is off screen or when
            // the player is inside.
            feeds.SunIsOnScreen && feeds.IsOutdoor
                ? Math.Clamp(_prefs.SunRayStrength * sun, 0f, 4f)
                : 0f);
    }

    /// <summary>Packs one frame's atmosphere into the block every pass reads.</summary>
    private static AtmosphericCycleUniforms FrameUniformsFor(
        in AtmosphericCycleFeeds feeds,
        in FrameRules rules,
        MarkGroup marks,
        Vector4 timerWind,
        Vector4 windAmplitude) => new(
        new Vector4(feeds.SunScreenUv, rules.RayStrength, feeds.SunElevationDegrees),
        new Vector4(feeds.SunColor, rules.Sun),
        new Vector4(marks.Width, marks.Height, 1f / marks.Width, 1f / marks.Height),
        new Vector4(
            (float)feeds.Weather,
            feeds.WeatherIntensity,
            (float)Math.Clamp(feeds.DeltaSeconds, 0d, 1d),
            feeds.IsOutdoor ? 1f : 0f),
        new Vector4(feeds.SunDirection, feeds.SunDirectionalBrightness),
        new Vector4(
            feeds.ActiveDayGroup,
            rules.DayCluster,
            rules.ShadeElevation,
            rules.VolumetricElevation),
        feeds.InverseViewProjection,
        timerWind,
        windAmplitude);

    /// <summary>
    /// Reserves the frame's uniform blocks.
    ///
    /// When the low passes are fused they all run inside one pass, so their blocks have to sit in a
    /// single reservation at offsets the device can bind to; otherwise each is reserved on its own
    /// and the two fused-pass blocks stay empty.
    /// </summary>
    private UniformChunks ReserveUniforms(IGpuCycle cycle)
    {
        if (!_fuseLoPostProc)
        {
            return new UniformChunks(
                cycle.ReserveLoop(AtmosphericCycleUniforms.SizeInBytes, GpuLoopPurpose.Uniform),
                cycle.ReserveLoop(PackPreferencesUniforms.SizeInBytes, GpuLoopPurpose.Uniform),
                default,
                default);
        }

        int alignment = checked((int)Math.Max(
            1u, _device.Capabilities.LowerUniformBufShiftAlignment));
        int prefsShift = LineUp(AtmosphericCycleUniforms.SizeInBytes, alignment);
        int sunPassShift = LineUp(
            checked(prefsShift + PackPreferencesUniforms.SizeInBytes), alignment);
        int filmicPassShift = LineUp(
            checked(sunPassShift + AtmosphericBundleSweepUniforms.SizeInBytes), alignment);

        GpuLoopAlloc uniforms = cycle.ReserveLoop(
            checked(filmicPassShift + AtmosphericBundleSweepUniforms.SizeInBytes),
            GpuLoopPurpose.Uniform);
        return new UniformChunks(
            Slice(uniforms, shiftOctets: 0, AtmosphericCycleUniforms.SizeInBytes),
            Slice(uniforms, prefsShift, PackPreferencesUniforms.SizeInBytes),
            Slice(uniforms, sunPassShift, AtmosphericBundleSweepUniforms.SizeInBytes),
            Slice(uniforms, filmicPassShift, AtmosphericBundleSweepUniforms.SizeInBytes));
    }

    public void PaintPostProc(
        IGpuCycle cycle,
        in AtmosphericCycleFeeds feeds)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(cycle);
        MarkGroup marks = _targets
            ?? throw new InvalidOperationException(
                "PrepareWorldTarget must succeed before post-processing begins.");
        if (feeds.ViewportWidth != marks.Width
            || feeds.ViewportHeight != marks.Height)
        {
            throw new InvalidOperationException(
                "Atmospheric inputs and target extent belong to different frames.");
        }
        if (_previousShadeCycleSerialNo != cycle.SerialNo)
        {
            _lastShadowDiagnostics = default;
            _previousShadeInvokerTally = 0;
            _previousShadeTaxonomyCalls = 0;
        }

        bool gaugeCpuJunctures = _cpuJunctureProfiler is not null
            && _cpuJunctureCycle.FrameSerial == cycle.SerialNo;
        long postBegun = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;

        FrameRules rules = RulesFor(feeds);
        (Vector4 timerWind, Vector4 windAmplitude) = LocateFoliageWind(
            cycle.SerialNo,
            feeds.Weather,
            feeds.IsOutdoor);
        AtmosphericCycleUniforms cycleUniforms =
            FrameUniformsFor(feeds, rules, marks, timerWind, windAmplitude);

        UniformChunks chunks = ReserveUniforms(cycle);
        GpuLoopAlloc cycleChunk = chunks.Frame;
        GpuLoopAlloc prefsChunk = chunks.Prefs;
        GpuLoopAlloc fusedSunPassChunk = chunks.FusedSunPass;
        GpuLoopAlloc fusedFilmicPassChunk = chunks.FusedFilmicPass;
        MemoryMarshal.Write(cycleChunk.Data, in cycleUniforms);
        PackPreferencesUniforms bundlePrefs = _bundlePrefs;
        MemoryMarshal.Write(prefsChunk.Data, in bundlePrefs);
        bool gaugeGpuTickers = AtmosphericGpuTickerSampling.ShouldMeasure(
            Preset.Semantic,
            cycle.SerialNo);

        long sunRaysBegun = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
        if (!_fuseLoPostProc)
        {
            PaintFullscreen(
                cycle,
                "atmospheric-sun-occlusion",
                marks.SunBitmask,
                _sunOcclusion,
                marks.RealmZDepthSocket,
                GpuTextureSlot.Unassigned,
                AtmosphericBundleSweepUniforms.From(Vector4.Zero),
                cycleChunk,
                prefsChunk,
                GpuTextureSlot.Unassigned,
                GpuTextureSlot.Unassigned,
                gaugeGpuTickers);
        }
        var sunPassUniforms = new AtmosphericBundleSweepUniforms(
            new Vector4(0.965f, 0.24f, 0.82f, 48f),
            _fuseLoPostProc
                ? new Vector4(
                    1f,
                    marks.SunRays.Description.Width,
                    marks.SunRays.Description.Height,
                    0f)
                : Vector4.Zero,
            Vector4.Zero,
            Vector4.Zero);
        if (_fuseLoPostProc)
        {
            PaintFullscreenReadied(
                cycle,
                "atmospheric-sun-rays",
                marks.SunRays,
                _sunRays,
                marks.RealmZDepthSocket,
                GpuTextureSlot.Unassigned,
                in sunPassUniforms,
                cycleChunk,
                prefsChunk,
                fusedSunPassChunk,
                GpuTextureSlot.Unassigned,
                GpuTextureSlot.Unassigned,
                gaugeGpuTickers);
        }
        else
        {
            PaintFullscreen(
                cycle,
                "atmospheric-sun-rays",
                marks.SunRays,
                _sunRays,
                marks.SunBitmaskSocket,
                GpuTextureSlot.Unassigned,
                in sunPassUniforms,
                cycleChunk,
                prefsChunk,
                GpuTextureSlot.Unassigned,
                GpuTextureSlot.Unassigned,
                gaugeGpuTickers);
        }
        long sunRaysFinished = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
        DirectionalShadeFrameWiring shadeMapping =
            _directedShades.TryFetchLatestCycleMapping(cycle, out var latestShade)
                ? latestShade
                : DirectionalShadeFrameWiring.Disabled;
        VolumetricShaftProduct volumetric = _volumetric is null
            ? default
            : _volumetric.Render(
                cycle,
                in feeds,
                in shadeMapping,
                marks.RealmZDepthSocket);
        if (!_fuseLoPostProc)
        {
            PaintFullscreen(
                cycle,
                "atmospheric-bloom-downsample",
                marks.BloomA,
                _bloomDownsample,
                marks.RealmTintSocket,
                marks.SunRaysSocket,
                AtmosphericBundleSweepUniforms.From(new Vector4(
                    _prefs.BloomStrength,
                    BloomThresholdLinear,
                    BloomKneeLinear,
                    volumetric.HasTexture ? 1f : 0f)),
                cycleChunk,
                prefsChunk,
                volumetric.TextureSlot,
                GpuTextureSlot.Unassigned,
                gaugeGpuTickers);
            PaintFullscreen(
                cycle,
                "atmospheric-bloom-blur-horizontal",
                marks.BloomB,
                _bloomBlur,
                marks.BloomASocket,
                GpuTextureSlot.Unassigned,
                AtmosphericBundleSweepUniforms.From(new Vector4(
                    1f / marks.BloomA.Description.Width,
                    0f,
                    0f,
                    0f)),
                cycleChunk,
                prefsChunk,
                GpuTextureSlot.Unassigned,
                GpuTextureSlot.Unassigned,
                gaugeGpuTickers);
            PaintFullscreen(
                cycle,
                "atmospheric-bloom-blur-vertical",
                marks.BloomA,
                _bloomBlur,
                marks.BloomBSocket,
                GpuTextureSlot.Unassigned,
                AtmosphericBundleSweepUniforms.From(new Vector4(
                    0f,
                    1f / marks.BloomA.Description.Height,
                    0f,
                    0f)),
                cycleChunk,
                prefsChunk,
                GpuTextureSlot.Unassigned,
                GpuTextureSlot.Unassigned,
                gaugeGpuTickers);
        }
        var filmicPassUniforms = new AtmosphericBundleSweepUniforms(
                new Vector4(
                    _prefs.Exposure,
                    _prefs.Saturation,
                    _prefs.Contrast,
                    _prefs.VignetteStrength),
                new Vector4(
                    _prefs.FilmicStrength,
                    volumetric.HasTexture ? 1f : 0f,
                    _fuseLoPostProc ? 1f : 0f,
                    0f),
                _fuseLoPostProc
                    ? new Vector4(
                        _prefs.BloomStrength,
                        BloomThresholdLinear,
                        BloomKneeLinear,
                        volumetric.HasTexture ? 1f : 0f)
                    : Vector4.Zero,
                _fuseLoPostProc
                    ? new Vector4(
                        1f / marks.PostWidth,
                        1f / marks.PostHeight,
                        0f,
                        0f)
                    : Vector4.Zero);
        long filmicBegun = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
        if (_fuseLoPostProc)
        {
            PaintFullscreenReadied(
                cycle,
                "atmospheric-filmic",
                mark: null,
                _filmic,
                marks.RealmTintSocket,
                marks.SunRaysSocket,
                in filmicPassUniforms,
                cycleChunk,
                prefsChunk,
                fusedFilmicPassChunk,
                volumetric.TextureSlot,
                GpuTextureSlot.Unassigned,
                gaugeGpuTickers);
        }
        else
        {
            PaintFullscreen(
                cycle,
                "atmospheric-filmic",
                mark: null,
                _filmic,
                marks.RealmTintSocket,
                marks.BloomASocket,
                in filmicPassUniforms,
                cycleChunk,
                prefsChunk,
                marks.SunRaysSocket,
                volumetric.TextureSlot,
                gaugeGpuTickers);
        }
        long filmicFinished = gaugeCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
        if (gaugeCpuJunctures)
        {
            _cpuJunctureCycle = _cpuJunctureCycle with
            {
                PostSetupAndOtherTicks = checked(
                    sunRaysBegun - postBegun
                    + filmicBegun - sunRaysFinished),
                PostSunRaysTicks = sunRaysFinished - sunRaysBegun,
                PostFilmicTicks = filmicFinished - filmicBegun,
            };
        }
        _previousFeeds = feeds;
        _renderedCycle = true;
    }

    bool IAtmosphericCpuStageProfileEngine.ShouldProfileCpuCycle(long cycleSerialNo) =>
        _cpuJunctureProfiler is not null
        && AtmosphericCpuJunctureProfiler.ShouldMeasure(cycleSerialNo);

    void IAtmosphericCpuStageProfileEngine.ConcludeCpuProfile(
        long cycleSerialNo,
        long markPrepBeats,
        long measuredBundleSumBeats,
        long watchBookkeepingBeats,
        bool stableCycleBoundary)
    {
        if (!stableCycleBoundary
            || _cpuJunctureProfiler is null
            || _cpuJunctureCycle.FrameSerial != cycleSerialNo)
        {
            return;
        }

        _cpuJunctureProfiler.Observe(
            in _cpuJunctureCycle,
            markPrepBeats,
            measuredBundleSumBeats,
            watchBookkeepingBeats);
    }

    public RenderPackEngineTelemetry GrabTelemetry()
    {
        MarkGroup? marks = _targets;
        if (!_renderedCycle || marks is null)
            return RenderPackEngineTelemetry.Empty(Preset.Id);
        VolumetricShaftTelemetry volumetric = _volumetric?.PreviousTelemetry ?? default;
        (string Name, int DrawCalls)[] postPasss =
            (_fuseLowPostProcess: _fuseLoPostProc, _volumetric is null) switch
            {
                (true, true) =>
                [
                    ("atmospheric-sun-rays", 1),
                    ("atmospheric-filmic", 1),
                ],
                (true, false) =>
                [
                    ("atmospheric-sun-rays", 1),
                    (VolumetricShaftPainter.TickerLabel, volumetric.DrawCalls),
                    ("atmospheric-filmic", 1),
                ],
                (false, true) =>
                [
                    ("atmospheric-sun-occlusion", 1),
                    ("atmospheric-sun-rays", 1),
                    ("atmospheric-bloom-downsample", 1),
                    ("atmospheric-bloom-blur-horizontal", 1),
                    ("atmospheric-bloom-blur-vertical", 1),
                    ("atmospheric-filmic", 1),
                ],
                _ =>
                [
                    ("atmospheric-sun-occlusion", 1),
                    ("atmospheric-sun-rays", 1),
                    (VolumetricShaftPainter.TickerLabel, volumetric.DrawCalls),
                    ("atmospheric-bloom-downsample", 1),
                    ("atmospheric-bloom-blur-horizontal", 1),
                    ("atmospheric-bloom-blur-vertical", 1),
                    ("atmospheric-filmic", 1),
                ],
            };
        int shadePassTally = _directedShades.MultiviewCascadesTurnedOn
            && _lastShadowDiagnostics.CascadeCount > 0
                ? 1
                : _lastShadowDiagnostics.CascadeCount;
        const int recipientPassTally = 1;
        var passs = new RenderPackPassTelemetry[
            recipientPassTally + postPasss.Length + shadePassTally];
        _device.Tickers.TryLocate(
            RasterizeBundlePerformanceAmbitLabels.EnhancedRealmRecipient,
            out double recipientMillis);
        passs[0] = new RenderPackPassTelemetry(
            RasterizeBundlePerformanceAmbitLabels.EnhancedRealmRecipient,
            recipientMillis,
            DrawCalls: 0,
            DispatchCalls: 0);
        for (int idx = 0; idx < shadePassTally; idx++)
        {
            string label = _directedShades.MultiviewCascadesTurnedOn
                ? DirectionalSunShadePainter.MultiviewTickerLabel
                : DirectionalSunShadePainter.TickerLabel(idx);
            _device.Tickers.TryLocate(label, out double millis);
            passs[recipientPassTally + idx] = new RenderPackPassTelemetry(
                label,
                millis,
                DrawCalls: shadePassTally == 0
                    ? 0
                    : _lastShadowDiagnostics.DrawCalls / shadePassTally,
                DispatchCalls: 0);
        }
        for (int idx = 0; idx < postPasss.Length; idx++)
        {
            (string label, int paintCalls) = postPasss[idx];
            _device.Tickers.TryLocate(label, out double millis);
            passs[recipientPassTally + shadePassTally + idx] = new RenderPackPassTelemetry(
                label,
                millis,
                paintCalls,
                DispatchCalls: 0);
        }
        return new RenderPackEngineTelemetry(
            Preset.Id,
            checked(
                marks.KeptOctets
                + _directedShades.Quality.ApproximateDepthMapBytes
                + volumetric.RetainedGpuBytes
                + _directedShades.KeptGpuBufOctets),
            marks.TransientOctets,
            marks.ImageTally + 1 + (volumetric.RetainedGpuBytes > 0 ? 1 : 0),
            BufferCount: _directedShades.KeptGpuBufTally,
            DrawCalls: postPasss.Sum(pass => pass.DrawCalls)
                + _lastShadowDiagnostics.DrawCalls,
            DispatchCalls: 0,
            ShadowCasterCount: _previousShadeInvokerTally,
            CascadeDrawCount: _lastShadowDiagnostics.CascadeCount,
            CpuClassificationCalls: _previousShadeTaxonomyCalls,
            _previousFeeds.SunElevationDegrees,
            ActiveDayGroup: _previousFeeds.ActiveDayGroup,
            _previousFeeds.Weather.ToString(),
            _previousFeeds.WeatherIntensity,
            _previousFeeds.IsOutdoor,
            DirectionalShadowStrength: _lastShadowDiagnostics.Strength,
            passs)
        {
            CpuJunctures = _cpuJunctureProfiler?.Freeze() ?? [],
            DirectedShadeSrcSort = _lastShadowDiagnostics.SourceKind,
            DirectedShadeSrcObjectOrdinal =
                _lastShadowDiagnostics.SourceObjectIndex,
            DirectedShadeSrcGfxObjRefIdent =
                _lastShadowDiagnostics.SourceGfxObjId,
            DirectedShadeCanvasToLampDir =
                _lastShadowDiagnostics.SurfaceToLightDirection,
            DirectedShadeLampElevationSin =
                _lastShadowDiagnostics.LightElevationSin,
            ShadeXformChurn = _lastShadowDiagnostics.TransformChurn,
            SharedWorldTransformUsedInstances =
                _previousShadeRealmTriMeshes is not null
                && _previousShadeRealmTriMeshes.HasDirectedShadeXformCycle(
                    _previousShadeCycleSerialNo)
                    ? _previousShadeRealmTriMeshes
                        .DirectedShadeXformCycleConsumedInsts
                    : 0u,
        };
    }

    public RenderPackEnginePerformanceMetrics GrabPerformanceMetrics()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        MarkGroup? marks = _targets;
        if (marks is null)
        {
            return new RenderPackEnginePerformanceMetrics(
                AssetGen,
                HasResolvedGpuMeasurement: false,
                InclusiveResolvedGpuMilliseconds: 0d,
                RetainedGpuBytes: _directedShades.Quality.ApproximateDepthMapBytes,
                TransientGpuBytes: 0L);
        }

        double gpuMillis = 0d;
        bool settled = true;
        settled &= TryAppendSettledTicker(
            RasterizeBundlePerformanceAmbitLabels.EnhancedRealmRecipient,
            ref gpuMillis);
        if (!_fuseLoPostProc)
        {
            settled &= TryAppendSettledTicker(
                "atmospheric-sun-occlusion",
                ref gpuMillis);
        }
        settled &= TryAppendSettledTicker("atmospheric-sun-rays", ref gpuMillis);
        if (!_fuseLoPostProc)
        {
            settled &= TryAppendSettledTicker(
                "atmospheric-bloom-downsample",
                ref gpuMillis);
            settled &= TryAppendSettledTicker(
                "atmospheric-bloom-blur-horizontal",
                ref gpuMillis);
            settled &= TryAppendSettledTicker(
                "atmospheric-bloom-blur-vertical",
                ref gpuMillis);
        }
        settled &= TryAppendSettledTicker("atmospheric-filmic", ref gpuMillis);
        int shadeTickerTally = _directedShades.MultiviewCascadesTurnedOn
            && _lastShadowDiagnostics.CascadeCount > 0
                ? 1
                : _lastShadowDiagnostics.CascadeCount;
        for (int idx = 0; idx < shadeTickerTally; idx++)
        {
            settled &= TryAppendSettledTicker(
                _directedShades.MultiviewCascadesTurnedOn
                    ? DirectionalSunShadePainter.MultiviewTickerLabel
                    : DirectionalSunShadePainter.TickerLabel(idx),
                ref gpuMillis);
        }

        VolumetricShaftTelemetry volumetric = _volumetric?.PreviousTelemetry ?? default;
        if (volumetric.DrawCalls > 0)
        {
            settled &= TryAppendSettledTicker(
                VolumetricShaftPainter.TickerLabel,
                ref gpuMillis);
        }

        return new RenderPackEnginePerformanceMetrics(
            AssetGen,
            settled,
            settled ? gpuMillis : 0d,
            checked(
                marks.KeptOctets
                + _directedShades.Quality.ApproximateDepthMapBytes
                + volumetric.RetainedGpuBytes
                + _directedShades.KeptGpuBufOctets),
            marks.TransientOctets);
    }

    private void DemandKeptGpuAllowance()
    {
        MarkGroup? marks = _targets;
        if (marks is null)
            return;
        long sum = checked(
            marks.KeptOctets
            + _directedShades.Quality.ApproximateDepthMapBytes
            + (_volumetric?.PreviousTelemetry.RetainedGpuBytes ?? 0L)
            + _directedShades.KeptGpuBufOctets);
        if (sum <= _housedGpuAllowanceOctets)
            return;
        throw new NotSupportedException(
            $"Render pack preset '{Preset.Id}' needs {sum} resident GPU bytes "
            + "after materializing its scene-dependent shadow command buffers; "
            + $"the active pack budget is {_housedGpuAllowanceOctets} bytes.");
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _targets?.Dispose();
        _targets = null;
        _volumetric?.Dispose();
        _directedShades.Dispose();
        _filmic.Dispose();
        _bloomBlur.Dispose();
        _bloomDownsample.Dispose();
        _sunRays.Dispose();
        _sunOcclusion.Dispose();
        _hdrPipeTenancy.Dispose();
    }

    internal float EvaluateSunRule(in AtmosphericCycleFeeds feeds)
    {
        return !feeds.IsOutdoor || !feeds.SunIsOnScreen
            ? 0f
            : Math.Clamp(
            EvaluateSunElevationRule(feeds.SunElevationDegrees)
                * EvaluateDayClusterRule(feeds.ActiveDayGroup)
                * EvaluateWeatherRule(feeds.Weather, feeds.WeatherIntensity),
            0f,
            4f);
    }

    internal float EvaluateDirectedShadeStrength(
        float sunElevationDeg,
        int engagedDayCluster) => Math.Clamp(
        RenderPackAtmosphereRuleEvaluation.DirectionalShadow(
            Descriptor.AtmospherePolicy?.DirectedShadeLampElevationResponse,
            sunElevationDeg)
            * EvaluateDayClusterRule(engagedDayCluster)
            * _shadeStrength,
        0f,
        1f);

    private static float EvaluateWeatherRule(
        MacAC.Mechanics.Realm.WeatherKind weather,
        float intensity)
    {
        float weatherMark = weather switch
        {
            MacAC.Mechanics.Realm.WeatherKind.Clear => 1f,
            MacAC.Mechanics.Realm.WeatherKind.Overcast => 0.18f,
            MacAC.Mechanics.Realm.WeatherKind.Rain => 0.10f,
            MacAC.Mechanics.Realm.WeatherKind.Snow => 0.16f,
            MacAC.Mechanics.Realm.WeatherKind.Storm => 0.06f,
            _ => 0f,
        };
        return 1f + ((weatherMark - 1f) * Math.Clamp(intensity, 0f, 1f));
    }

    private bool TryAppendSettledTicker(string label, ref double sum)
    {
        if (!_device.Tickers.TryGrabSettled(label, out double millis))
            return false;
        sum += millis;
        return true;
    }

    private float EvaluateSunElevationRule(float elevation)
    {
        IReadOnlyList<SunElevationKnot>? pts =
            Descriptor.AtmospherePolicy?.SunElevationResponse;
        return RenderPackAtmosphereRuleEvaluation.Ray(pts, elevation);
    }

    private float EvaluateDayClusterRule(int engagedDayCluster)
    {
        DayGroupWeight? val = Descriptor.AtmospherePolicy?
            .ActiveDayGroupMultipliers
            .FirstOrDefault(listing => listing.ActiveDayGroup == engagedDayCluster);
        return val is null ? 1f : (float)val.Multiplier;
    }

    private static IGpuPipe BuildPipe(
        IClientGpuDevice dev,
        string label,
        GpuShaderGroup shaders,
        GpuBitmapFmt tintFmt) =>
        dev.BuildPipe(new GpuPipeSpec
        {
            Name = label,
            Shaders = shaders,
            VertArrangement = GpuVertexArrangement.None,
            Blend = GpuBlendManner.None,
            Depth = GpuDepthLedger.Disabled,
            Cull = GpuPruneManner.None,
            TintFmt = tintFmt,
            AllowTintFmtVariants = false,
            SampleCount = 1,
            UsesRasterizeBundleShaderAbi = true,
        });

    private static bool HasPass(
        RenderPackCard descriptor,
        RenderPassRole semantic) =>
        descriptor.Passes.Any(pass => pass.Semantic == semantic);

    private static GpuShaderGroup ShaderSet(
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        RenderPassRole semantic)
    {
        RenderPassSpec pass = descriptor.Passes.FirstOrDefault(val =>
            val.Semantic == semantic)
            ?? throw new InvalidOperationException(
                $"Atmospheric graph requires declared pass semantic '{semantic}'.");
        return RasterizeBundleShaderHoldings.PullPass(descriptor, holdings, pass);
    }

    private static DirectionalShadePipelineShaders PullDirectedShadeShaders(
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings)
    {
        DirectionalShadePipelineShaders shaders = new(
            Variant(PipelineVariantRole.TerrainDirectionalShadowCaster),
            Variant(PipelineVariantRole.WorldOpaqueDirectionalShadowCaster),
            Variant(PipelineVariantRole.WorldAlphaCutoutDirectionalShadowCaster),
            Variant(PipelineVariantRole.TerrainDirectionalShadowReceiver),
            Variant(PipelineVariantRole.WorldDirectionalShadowReceiver));
        if (descriptor.PipelineVariants.Any(val =>
                val.Semantic == PipelineVariantRole.TerrainMultiviewDirectionalShadowCaster))
        {
            shaders = shaders with
            {
                MultiviewCasters = new DirectionalShadeMultiviewPipelineShaders(
                    Variant(PipelineVariantRole.TerrainMultiviewDirectionalShadowCaster),
                    Variant(PipelineVariantRole.WorldOpaqueMultiviewDirectionalShadowCaster),
                    Variant(PipelineVariantRole.WorldAlphaCutoutMultiviewDirectionalShadowCaster)),
            };
        }
        return shaders;

        GpuShaderGroup Variant(PipelineVariantRole semantic)
        {
            PipelineVariantSpec variant = descriptor.PipelineVariants
                .FirstOrDefault(val => val.Semantic == semantic)
                ?? throw new InvalidOperationException(
                    $"Atmospheric graph requires declared pipeline-variant semantic '{semantic}'.");
            return RasterizeBundleShaderHoldings.PullVariant(descriptor, holdings, variant);
        }
    }

    private static DirectionalShadePreset ShadePreset(
        QualityLadderStep preset) => preset.Semantic switch
        {
            RenderQualityRole.Low => DirectionalShadePreset.Low,
            RenderQualityRole.High => DirectionalShadePreset.High,
            _ => DirectionalShadePreset.Medium,
        };

    private static DirectionalShadeQuality LocateShadeFidelity(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions)
    {
        var fidelity = DirectionalShadeQuality.For(
            ShadePreset(preset));
        RenderResourceSpec asset = descriptor.Resources.Single(val =>
            val.Semantic == RenderResourceRole.DirectionalShadowDepth);
        QualityResourceTweak? assetOverride = preset.ResourceOverrides
            .FirstOrDefault(val => string.Equals(
                val.ResourceId,
                asset.Id,
                StringComparison.OrdinalIgnoreCase));
        RenderExtentSpec extent = assetOverride?.Extent
            ?? asset.Extent
            ?? throw new NotSupportedException(
                "The DirectionalShadowDepth semantic resource has no image extent.");
        if (extent.Mode != ExtentRule.AbsolutePixels
            || extent.Width != extent.Height
            || extent.Width != Math.Truncate(extent.Width)
            || extent.Width is < 1 or > 16_384
            || extent.Layers is < 1 or > 4)
        {
            throw new NotSupportedException(
                "The DirectionalShadowDepth semantic resource must be a square "
                + "absolute 1..16384 image with 1..4 array layers.");
        }

        float reach = ScanSemanticSetting(
            descriptor,
            preset,
            userSettingSubstitutions,
            RenderSettingRole.DirectionalShadowReachMetres,
            fidelity.MaximumReachMeters);
        int taps = ScanShadePcfTaps(
            descriptor,
            preset,
            userSettingSubstitutions,
            fidelity.PcfRadiusTexels switch
            {
                0 => 1,
                1 => 9,
                _ => 25,
            });
        int radius = taps switch
        {
            1 => 0,
            9 => 1,
            25 => 2,
            _ => throw new NotSupportedException(
                "DirectionalShadowPcfTaps must resolve to exactly 1, 9, or 25 samples."),
        };
        int resolution = checked((int)extent.Width);
        int cascades = extent.Layers;
        return fidelity with
        {
            CascadeCount = cascades,
            MapResolution = resolution,
            MaximumReachMeters = Math.Clamp(reach, 1f, 10_000f),
            PcfRadiusTexels = radius,
            ApproximateDepthMapBytes = checked(
                (long)cascades * resolution * resolution * sizeof(float)),
            IncrementalGpuP50BudgetMilliseconds = preset.MaxIncrementalGpuMillisecondsP50,
            IncrementalGpuP99BudgetMilliseconds = preset.MaxIncrementalGpuMillisecondsP99,
            IncrementalCpuP50BudgetMilliseconds = preset.MaxIncrementalCpuMillisecondsP50,
            IncrementalCpuP99BudgetMilliseconds = preset.MaxIncrementalCpuMillisecondsP99,
            PackResidentGpuByteBudget = preset.MaxResidentGpuBytes,
        };
    }

    private static int ScanShadePcfTaps(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions,
        int backup)
    {
        RenderSettingSpec? setting = descriptor.Settings.FirstOrDefault(val =>
            val.Semantic == RenderSettingRole.DirectionalShadowPcfTaps);
        if (setting is null)
            return backup;

        string value = RenderPackPreferenceResolution.Resolve(
            setting,
            preset,
            userSettingSubstitutions);
        return int.TryParse(
            value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int taps)
                ? taps
                : throw new NotSupportedException(
                    "DirectionalShadowPcfTaps must resolve to an integer sample count.");
    }

    private static float ScanSemanticSetting(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions,
        RenderSettingRole semantic,
        float backup)
    {
        RenderSettingSpec? setting = descriptor.Settings.FirstOrDefault(val =>
            val.Semantic == semantic);
        if (setting is null)
            return backup;
        string value = RenderPackPreferenceResolution.Resolve(
            setting,
            preset,
            userSettingSubstitutions);
        return SettingValueCodec.TryPack(setting, value, out float encoded)
            && float.IsFinite(encoded)
                ? encoded
                : backup;
    }

    private static void PaintFullscreen(
        IGpuCycle cycle,
        string label,
        IGpuRasterizeMark? mark,
        IGpuPipe pipe,
        GpuTextureSlot textureA,
        GpuTextureSlot textureB,
        in AtmosphericBundleSweepUniforms passUniforms,
        GpuLoopAlloc cycleChunk,
        GpuLoopAlloc prefsChunk,
        GpuTextureSlot textureC,
        GpuTextureSlot textureD,
        bool gaugeGpuTickers)
    {
        GpuLoopAlloc passChunk = cycle.ReserveLoop(
            AtmosphericBundleSweepUniforms.SizeInBytes,
            GpuLoopPurpose.Uniform);
        PaintFullscreenReadied(
            cycle,
            label,
            mark,
            pipe,
            textureA,
            textureB,
            in passUniforms,
            cycleChunk,
            prefsChunk,
            passChunk,
            textureC,
            textureD,
            gaugeGpuTickers);
    }

    private static void PaintFullscreenReadied(
        IGpuCycle cycle,
        string label,
        IGpuRasterizeMark? mark,
        IGpuPipe pipe,
        GpuTextureSlot textureA,
        GpuTextureSlot textureB,
        in AtmosphericBundleSweepUniforms passUniforms,
        GpuLoopAlloc cycleChunk,
        GpuLoopAlloc prefsChunk,
        GpuLoopAlloc passChunk,
        GpuTextureSlot textureC,
        GpuTextureSlot textureD,
        bool gaugeGpuTickers)
    {
        using IGpuSweepCoder coder = cycle.BeginPass(new GpuPassSpec
        {
            Name = label,
            Color = new GpuTintAffix(
                mark,
                GpuPullOp.Clear,
                GpuVaultOp.Store,
                Vector4.Zero),
            ZDepth = null,
            SampleCount = 1,
        });
        using IDisposable? ticker = gaugeGpuTickers
            ? coder.CommenceTickerAmbit(label)
            : null;
        coder.BindPipeline(pipe);
        coder.AttachUniformBuf(
            GpuBindingModel.UniformAtmosphericCycle,
            cycleChunk.Buffer,
            cycleChunk.ShiftOctets,
            AtmosphericCycleUniforms.SizeInBytes);
        MemoryMarshal.Write(passChunk.Data, in passUniforms);
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
        GpuShoveConstants constants = GpuShoveConstants.Default;
        constants.TextureIndexA = textureA.IsAssigned
            ? textureA.Index
            : GpuTextureSlot.Unassigned.Index;
        constants.TextureOrdinalB = textureB.IsAssigned
            ? textureB.Index
            : GpuTextureSlot.Unassigned.Index;
        constants.ParamA = BitConverter.UInt32BitsToSingle(
            textureC.IsAssigned ? textureC.Index : GpuTextureSlot.Unassigned.Index);
        constants.ParameterB = BitConverter.UInt32BitsToSingle(
            textureD.IsAssigned ? textureD.Index : GpuTextureSlot.Unassigned.Index);
        coder.AssignPushConstants(in constants);
        coder.Draw(3, 1, 0, 0);
    }

    private static GpuLoopAlloc Slice(
        GpuLoopAlloc alloc,
        int shiftOctets,
        int byteSize) => new(
        alloc.Buffer,
        checked(alloc.ShiftOctets + (uint)shiftOctets),
        alloc.Data.Slice(shiftOctets, byteSize));

    private static int LineUp(int val, int alignment)
    {
        int remainder = val % alignment;
        return remainder == 0
            ? val
            : checked(val + alignment - remainder);
    }

    private static float PostScaling(
        RenderPackCard descriptor,
        QualityLadderStep preset)
    {
        QualityResourceTweak? val = preset.ResourceOverrides
            .FirstOrDefault(overrideVal =>
                AssetSemantic(
                    descriptor,
                    overrideVal,
                    RenderResourceRole.BloomPing));
        return val?.Extent is { } reach
            && reach.Mode == ExtentRule.RelativeToMainWorld
            ? (float)Math.Clamp(reach.Width, 0.125, 1.0)
            : preset.Semantic == RenderQualityRole.Low
            ? 0.25f
            : 0.5f;
    }

    private static float RayScaling(
        RenderPackCard descriptor,
        QualityLadderStep preset)
    {
        QualityResourceTweak? val = preset.ResourceOverrides
            .FirstOrDefault(overrideVal =>
                AssetSemantic(
                    descriptor,
                    overrideVal,
                    RenderResourceRole.SunRays));
        return val?.Extent is { } reach
            && reach.Mode == ExtentRule.RelativeToMainWorld
            ? (float)Math.Clamp(reach.Width, 0.125, 1.0)
            : preset.Semantic == RenderQualityRole.Low
            ? 0.25f
            : 0.5f;
    }

    private static bool AssetSemantic(
        RenderPackCard descriptor,
        QualityResourceTweak val,
        RenderResourceRole semantic) =>
        descriptor.Resources.FirstOrDefault(asset => string.Equals(
            asset.Id,
            val.ResourceId,
            StringComparison.OrdinalIgnoreCase))?.Semantic == semantic;

    private sealed class MarkGroup : IDisposable
    {
        private readonly IClientGpuDevice _device;
        private readonly GpuTextureSlot[] _sockets;
        private bool _destroyed;

        private MarkGroup(
            IClientGpuDevice dev,
            int width,
            int height,
            int specimenTally,
            IGpuRasterizeMark realm,
            IGpuRasterizeMark? bloomA,
            IGpuRasterizeMark? bloomB,
            IGpuRasterizeMark sunBitmask,
            IGpuRasterizeMark sunRays,
            int postWidth,
            int postHeight,
            GpuTextureSlot realmTintSocket,
            GpuTextureSlot realmZDepthSocket,
            GpuTextureSlot bloomASocket,
            GpuTextureSlot bloomBSocket,
            GpuTextureSlot sunBitmaskSocket,
            GpuTextureSlot sunRaysSocket)
        {
            _device = dev;
            Width = width;
            Height = height;
            SampleCount = specimenTally;
            World = realm;
            BloomAOrNull = bloomA;
            BloomBOrNull = bloomB;
            SunBitmask = sunBitmask;
            SunRays = sunRays;
            PostWidth = postWidth;
            PostHeight = postHeight;
            RealmTintSocket = realmTintSocket;
            RealmZDepthSocket = realmZDepthSocket;
            BloomASocket = bloomASocket;
            BloomBSocket = bloomBSocket;
            SunBitmaskSocket = sunBitmaskSocket;
            SunRaysSocket = sunRaysSocket;
            _sockets = bloomA is null
                ? [realmTintSocket, realmZDepthSocket, sunBitmaskSocket, sunRaysSocket]
                : [realmTintSocket, realmZDepthSocket, bloomASocket, bloomBSocket,
                    sunBitmaskSocket, sunRaysSocket];
        }

        internal int Width { get; }
        internal int Height { get; }
        internal int SampleCount { get; }
        internal IGpuRasterizeMark World { get; }
        private IGpuRasterizeMark? BloomAOrNull { get; }
        private IGpuRasterizeMark? BloomBOrNull { get; }
        internal IGpuRasterizeMark BloomA => BloomAOrNull
            ?? throw new InvalidOperationException(
                "The fused Low graph has no bloom ping intermediate.");
        internal IGpuRasterizeMark BloomB => BloomBOrNull
            ?? throw new InvalidOperationException(
                "The fused Low graph has no bloom pong intermediate.");
        internal IGpuRasterizeMark SunBitmask { get; }
        internal IGpuRasterizeMark SunRays { get; }
        internal int PostWidth { get; }
        internal int PostHeight { get; }
        internal GpuTextureSlot RealmTintSocket { get; }
        internal GpuTextureSlot RealmZDepthSocket { get; }
        internal GpuTextureSlot BloomASocket { get; }
        internal GpuTextureSlot BloomBSocket { get; }
        internal GpuTextureSlot SunBitmaskSocket { get; }
        internal GpuTextureSlot SunRaysSocket { get; }
        internal long KeptOctets =>
            checked(
                (long)Width * Height * 12L
                + (BloomAOrNull is null
                    ? 0L
                    : (long)PostWidth * PostHeight * 16L)
                + ((long)SunBitmask.Description.Width * SunBitmask.Description.Height * 4L)
                + ((long)SunRays.Description.Width * SunRays.Description.Height * 8L));
        internal long TransientOctets => SampleCount > 1
            ? checked((long)Width * Height * 12L * SampleCount)
            : 0L;
        internal int ImageTally => (BloomAOrNull is null ? 4 : 6)
            + (SampleCount > 1 ? 2 : 0);

        internal static MarkGroup Create(
            IClientGpuDevice dev,
            int width,
            int height,
            int specimenTally,
            float postScaling,
            float rayScaling,
            bool reserveBloomIntermediates,
            IClientGpuSampler linear,
            IClientGpuSampler closest)
        {
            var marks = new List<IGpuRasterizeMark>(capacity: 5);
            var sockets = new List<GpuTextureSlot>(capacity: 6);
            try
            {
                IGpuRasterizeMark realm = BuildMark(
                    dev,
                    "atmospheric-world-hdr",
                    width,
                    height,
                    GpuBitmapFmt.Rgba16FloatRenderTarget,
                    GpuBitmapFmt.Depth24Stencil8,
                    specimenTally,
                    sampleableZDepth: true);
                marks.Add(realm);
                int postWidth = Math.Max(1, (int)MathF.Ceiling(width * postScaling));
                int postHeight = Math.Max(1, (int)MathF.Ceiling(height * postScaling));
                int rayWidth = Math.Max(1, (int)MathF.Ceiling(width * rayScaling));
                int rayHeight = Math.Max(1, (int)MathF.Ceiling(height * rayScaling));
                IGpuRasterizeMark? bloomA = null;
                IGpuRasterizeMark? bloomB = null;
                if (reserveBloomIntermediates)
                {
                    bloomA = BuildMark(
                        dev, "atmospheric-bloom-a", postWidth, postHeight,
                        GpuBitmapFmt.Rgba16FloatRenderTarget, null, 1, false);
                    marks.Add(bloomA);
                    bloomB = BuildMark(
                        dev, "atmospheric-bloom-b", postWidth, postHeight,
                        GpuBitmapFmt.Rgba16FloatRenderTarget, null, 1, false);
                    marks.Add(bloomB);
                }
                IGpuRasterizeMark sunBitmask = BuildMark(
                    dev, "atmospheric-sun-mask", rayWidth, rayHeight,
                    GpuBitmapFmt.Rgba8UnormRenderTarget, null, 1, false);
                marks.Add(sunBitmask);
                IGpuRasterizeMark sunRays = BuildMark(
                    dev, "atmospheric-sun-rays", rayWidth, rayHeight,
                    GpuBitmapFmt.Rgba16FloatRenderTarget, null, 1, false);
                marks.Add(sunRays);

                GpuTextureSlot realmTint = Register(dev, realm.ColorTexture, linear, sockets);
                GpuTextureSlot realmZDepth = Register(
                    dev,
                    realm.ZDepthTexture
                        ?? throw new InvalidOperationException("The HDR world target exposed no sampled depth."),
                    closest,
                    sockets);
                GpuTextureSlot bloomASocket = bloomA is null
                    ? GpuTextureSlot.Unassigned
                    : Register(dev, bloomA.ColorTexture, linear, sockets);
                GpuTextureSlot bloomBSocket = bloomB is null
                    ? GpuTextureSlot.Unassigned
                    : Register(dev, bloomB.ColorTexture, linear, sockets);
                GpuTextureSlot sunBitmaskSocket = Register(dev, sunBitmask.ColorTexture, linear, sockets);
                GpuTextureSlot sunRaysSocket = Register(dev, sunRays.ColorTexture, linear, sockets);
                return new MarkGroup(
                    dev,
                    width,
                    height,
                    specimenTally,
                    realm,
                    bloomA,
                    bloomB,
                    sunBitmask,
                    sunRays,
                    postWidth,
                    postHeight,
                    realmTint,
                    realmZDepth,
                    bloomASocket,
                    bloomBSocket,
                    sunBitmaskSocket,
                    sunRaysSocket);
            }
            catch
            {
                for (int idx = sockets.Count - 1; idx >= 0; idx--)
                    dev.FreeTextureSocket(sockets[idx]);
                for (int idx = marks.Count - 1; idx >= 0; idx--)
                    marks[idx].Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_destroyed)
                return;
            _destroyed = true;
            for (int idx = _sockets.Length - 1; idx >= 0; idx--)
                _device.FreeTextureSocket(_sockets[idx]);
            SunRays.Dispose();
            SunBitmask.Dispose();
            BloomBOrNull?.Dispose();
            BloomAOrNull?.Dispose();
            World.Dispose();
        }

        private static IGpuRasterizeMark BuildMark(
            IClientGpuDevice dev,
            string label,
            int width,
            int height,
            GpuBitmapFmt tint,
            GpuBitmapFmt? zDepth,
            int specimens,
            bool sampleableZDepth) =>
            dev.BuildRasterizeMark(new GpuRenderTargetSpec(
                label,
                width,
                height,
                tint,
                zDepth,
                specimens,
                sampleableZDepth));

        private static GpuTextureSlot Register(
            IClientGpuDevice dev,
            IGpuBitmap texture,
            IClientGpuSampler sampler,
            List<GpuTextureSlot> sockets)
        {
            GpuTextureSlot socket = dev.EnrollTexture(texture, sampler);
            sockets.Add(socket);
            return socket;
        }
    }
}

internal sealed class AtmosphericRenderPackEngineMint(
    IClientGpuDevice device,
    float? heavensStageSecsOverride = null) :
    IRenderPackEngineMint
{
    private readonly IClientGpuDevice _device = device
        ?? throw new ArgumentNullException(nameof(device));

    private readonly float? _heavensStageSecsOverride = heavensStageSecsOverride;

    public IRenderPackEngine Build(
        RenderPackCard descriptor,
        IRenderPackFiles holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions) =>
        Build(
            descriptor,
            RasterizeBundleShaderHoldings.Validate(descriptor, holdings),
            preset,
            userSettingSubstitutions);

    public IRenderPackEngine Build(
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(userSettingSubstitutions);
        if (descriptor.Passes.Count == 0)
        {
            return descriptor.SceneReplays.Count != 0 || descriptor.PipelineVariants.Count != 0
                ? throw new NotSupportedException(
                    $"Pack '{descriptor.Id}' declares scene replay or pipeline variants without an executable pass.")
                : (IRenderPackEngine)new NoOpRenderPackEngine(descriptor, preset);
        }

        RenderPassRole[] atmosphericSemanticPasss =
        [
            RenderPassRole.DirectionalShadowDepth,
            RenderPassRole.SunOcclusion,
            RenderPassRole.SunRays,
            RenderPassRole.VolumetricShafts,
            RenderPassRole.BloomDownsample,
            RenderPassRole.BloomBlurHorizontal,
            RenderPassRole.BloomBlurVertical,
            RenderPassRole.FilmicComposite,
        ];
        bool standardAtmosphericGraph = atmosphericSemanticPasss.All(needed =>
            descriptor.Passes.Count(pass => pass.Semantic == needed) == 1);
        if (standardAtmosphericGraph)
        {
            return new AtmosphericPostProcGraph(
                _device,
                descriptor,
                holdings,
                preset,
                userSettingSubstitutions: userSettingSubstitutions,
                windTimerSecsOverride: _heavensStageSecsOverride);
        }

        bool declaredDirectedShadeGraph = descriptor.Passes.Count(pass =>
                pass.Semantic == RenderPassRole.DirectionalShadowDepth) == 1
            && descriptor.Passes.All(pass =>
                pass.Semantic is RenderPassRole.CustomFullscreen
                    or RenderPassRole.DirectionalShadowDepth);
        if (declaredDirectedShadeGraph)
        {
            return new DeclaredDirectionalShadeRenderPackGraph(
                _device,
                descriptor,
                holdings,
                preset,
                userSettingSubstitutions);
        }

        if (descriptor.SceneReplays.Count != 0 || descriptor.PipelineVariants.Count != 0)
        {
            throw new NotSupportedException(
                $"Pack '{descriptor.Id}' uses scene replay or renderer-pipeline variants "
                + "without a host semantic executor.");
        }
        return descriptor.Passes.Any(pass => pass.Hook is
            RenderPassAnchor.ShadowDepthBeforeWorld or
            RenderPassAnchor.AfterToneMapBeforePrivateViewports)
            ? throw new NotSupportedException(
                $"Pack '{descriptor.Id}' uses a pass hook outside the API-v1 Tier-1 fullscreen executor.")
            : (IRenderPackEngine)new DeclaredFullscreenRasterizeBundleGraph(
            _device,
            descriptor,
            holdings,
            preset,
            userSettingSubstitutions);
    }
}

internal sealed class NoOpRenderPackEngine(
    RenderPackCard descriptor,
    QualityLadderStep preset) : IDefaultRealmPathRenderPackEngine
{
    public RenderPackCard Descriptor { get; } = descriptor
        ?? throw new ArgumentNullException(nameof(descriptor));

    public QualityLadderStep Preset { get; } = preset
        ?? throw new ArgumentNullException(nameof(preset));

    public void Dispose()
    {
    }
}
