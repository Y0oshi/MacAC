using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Stage;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

// API-v1 executor for declaration-only fullscreen graphs
internal class DeclaredFullscreenRasterizeBundleGraph :
    IAtmosphericRealmGraphEngine,
    IRenderPackEnginePerformanceSource,
    IRenderPackEngineTelemetrySource
{
    private readonly IClientGpuDevice _device;
    private readonly IDisposable _hdrTenancy;
    private readonly IClientGpuSampler _sampler;
    private readonly Joint[] _joints;
    private readonly IReadOnlyDictionary<string, RenderResourceSpec> _assetList;
    private readonly PackPreferencesUniforms _prefs;
    private readonly DirectionalSunShadePainter? _directedShades;
    private readonly DirectionalShadeCasterFrame _shadowCasters = new();
    private readonly RenderPassSpec? _shadePass;
    private readonly float _shadeStrength;
    private ObjectiveGroup? _targets;
    private RenderPackResourceAllowance _assetAllowance;
    private long _assetGen;
    private long _housedGpuAllowanceOctets;
    private AtmosphericCycleFeeds _previousFeeds;
    private DirectionalSunShadeTelemetry _lastShadowDiagnostics;
    private int _previousShadeInvokerTally;
    private int _previousShadeTaxonomyCalls;
    private RealmPaintRouter? _previousShadeRealmTriMeshes;
    private long _previousShadeCycleSerialNo = -1;
    private bool _renderedCycle;
    private bool _destroyed;

    internal DeclaredFullscreenRasterizeBundleGraph(
        IClientGpuDevice dev,
        RenderPackCard descriptor,
        IRenderPackFiles holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions)
        : this(
            dev,
            descriptor,
            RasterizeBundleShaderHoldings.Validate(descriptor, holdings),
            preset,
            userSettingSubstitutions)
    {
    }

    internal DeclaredFullscreenRasterizeBundleGraph(
        IClientGpuDevice device,
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ArgumentNullException.ThrowIfNull(holdings);
        Preset = preset ?? throw new ArgumentNullException(nameof(preset));
        ArgumentNullException.ThrowIfNull(userSettingSubstitutions);
        if (device is not IGpuPipeFmtVariantHub variants)
            throw new NotSupportedException("The active RHI can't build an HDR world intermediate");

        _assetList = descriptor.Resources.ToDictionary(val => val.Id, StringComparer.OrdinalIgnoreCase);
        RenderPassSpec[] passs = [.. descriptor.Passes.OrderBy(val => val.Hook)];
        RenderPassSpec[] fullscreenPasss = [.. passs
            .Where(static val =>
                val.Semantic != RenderPassRole.DirectionalShadowDepth)];
        if (!fullscreenPasss.Any(val => val.Hook == RenderPassAnchor.ToneMap
                && val.ResourceWrites.Count == 0))
        {
            throw new NotSupportedException(
                $"Fullscreen pack '{descriptor.Id}' must declare a ToneMap pass that writes the output surface");
        }

        IDisposable? tenancy = null;
        DirectionalSunShadePainter? directedShades = null;
        var joints = new List<Joint>(fullscreenPasss.Length);
        try
        {
            tenancy = variants.ObtainPipeTintFmt(GpuBitmapFmt.Rgba16FloatRenderTarget);
            _sampler = device.BuildSampler(GpuSamplerSpec.RealmClamp);
            foreach (RenderPassSpec pass in fullscreenPasss)
            {
                if (pass.Hook is not RenderPassAnchor.AtmosphereBeforeToneMap
                    and not RenderPassAnchor.ToneMap)
                    throw new NotSupportedException($"Fullscreen executor doesn't support hook '{pass.Hook}'.");
                SemanticInput? unsupported = pass.SemanticInputs.FirstOrDefault(val =>
                    val is SemanticInput.SceneNormals
                        or SemanticInput.ShadowCasterTransforms
                        or SemanticInput.DirectionalShadowMaps);
                if (unsupported is SemanticInput.SceneNormals
                    or SemanticInput.ShadowCasterTransforms
                    or SemanticInput.DirectionalShadowMaps)
                {
                    throw new NotSupportedException(
                        $"Tier-1 fullscreen pass '{pass.Id}' needs not supported semantic '{unsupported}'.");
                }
                if (pass.ResourceWrites.Count > 1)
                    throw new NotSupportedException($"Pass '{pass.Id}' writes more than one colour target");
                GpuBitmapFmt fmt = pass.ResourceWrites.Count == 0
                    ? GpuBitmapFmt.Rgba8UnormRenderTarget
                    : VetProduct(Resource(pass.ResourceWrites[0]));
                var pipe = device.BuildPipe(new GpuPipeSpec
                {
                    Name = $"render-pack-{descriptor.Id}-{pass.Id}",
                    Shaders = RasterizeBundleShaderHoldings.PullPass(descriptor, holdings, pass),
                    VertArrangement = GpuVertexArrangement.None,
                    Blend = GpuBlendManner.None,
                    Depth = GpuDepthLedger.Disabled,
                    Cull = GpuPruneManner.None,
                    TintFmt = fmt,
                    AllowTintFmtVariants = false,
                    SampleCount = 1,
                    UsesRasterizeBundleShaderAbi = true,
                });
                string tickerLabel = $"render-pack-{descriptor.Id}-{pass.Id}";
                joints.Add(new Joint(
                    pass,
                    pipe,
                    [.. RenderPackTextureWiringPicker.Resolve(pass, _assetList)],
                    tickerLabel));
            }
            _joints = [.. joints];
            _prefs = PackPreferencesUniforms.Create(
                descriptor,
                preset,
                userSettingSubstitutions);
            _shadePass = passs.SingleOrDefault(static val =>
                val.Semantic == RenderPassRole.DirectionalShadowDepth);
            _shadeStrength = _shadePass is null
                ? 0f
                : ScanSemanticSetting(
                    descriptor,
                    preset,
                    userSettingSubstitutions,
                    RenderSettingRole.DirectionalShadowStrength);
            if (_shadePass is not null)
            {
                directedShades = new DirectionalSunShadePainter(
                    device,
                    LocateShadeFidelity(
                        descriptor,
                        preset,
                        userSettingSubstitutions),
                    RenderPackAtmosphereRuleEvaluation.NeutralDirectedShadeElevation,
                    PullDirectedShadeShaders(descriptor, holdings),
                    multiviewCascades: (preset.ExecutionHints
                        & QualityExecutionHints
                            .MultiviewDirectionalShadowCascades) != 0);
            }
            _directedShades = directedShades;
            directedShades = null;
            _hdrTenancy = tenancy;
            tenancy = null;
        }
        catch
        {
            directedShades?.Dispose();
            for (int idx = joints.Count - 1; idx >= 0; idx--)
                joints[idx].Pipeline.Dispose();
            tenancy?.Dispose();
            throw;
        }
    }

    public RenderPackCard Descriptor { get; }

    public QualityLadderStep Preset { get; }

    internal IDirectionalShadeReceiverSource DeclaredDirectedShadeRecipients =>
        _directedShades
        ?? throw new InvalidOperationException(
            $"Pack '{Descriptor.Id}' has no declared directional-shadow executor");

    internal DirectionalSunShadeTelemetry RenderDeclaredDirectionalShadows(
        IGpuCycle cycle,
        in RasterizeCycleFoundation foundation,
        in RealmRenderFrame realm,
        int engagedDayCluster,
        in RenderStageProbe tableau,
        RealmPaintRouter realmTriMeshes,
        LandModernPainter land)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        DirectionalSunShadePainter painter = _directedShades
            ?? throw new InvalidOperationException(
                $"Pack '{Descriptor.Id}' has no declared directional-shadow executor");
        _shadowCasters.Build(in tableau);
        CanonLandscapeVisibilityFrame precedingSceneryVis =
            realm.PriorLandscapeVisibility;
        _shadowCasters.Select(
            in precedingSceneryVis,
            realm.DirectionalShadowCellMembership
                ?? EmptyDirectionalShadeCellMembership.Instance);
        AuthoredCelestialShadeSource src = realm.CelestialShadeSrc;
        float elevationStrength = RenderPackAtmosphereRuleEvaluation
            .DirectedShadeFromSin(
            Descriptor.AtmospherePolicy!.DirectedShadeLampElevationResponse,
            src.ElevationSin,
            backup: 0f);
        var surroundings = new DirectionalShadeEnvironmentInput(
            PackEnabled: true,
            PortalOrLoginCoverVisible: foundation.PortalViewportVisible,
            PlayerInsideCell: realm.Roots.AvatarOrCamInsideEnclosedChamber,
            src,
            foundation.Atmosphere,
            DayGroupWeight: Math.Clamp(
                EvaluateDayClusterRule(engagedDayCluster)
                    * elevationStrength
                    * _shadeStrength,
                0f,
                1f));
        var feed = new DirectionalSunShadeRenderInput(
            surroundings,
            realm.Camera.Camera.View,
            realm.Camera.Projection,
            _shadowCasters,
            ResidentMaximumReachMeters:
                realm.HousedPagingPane.CeilingReachMeters,
            PriorLandscapeVisibility: realm.PriorLandscapeVisibility);
        _previousShadeInvokerTally = _shadowCasters.Stats.ActiveSelected;
        _previousShadeTaxonomyCalls = _shadowCasters.Stats.TopologyRebuilt ? 1 : 0;
        _lastShadowDiagnostics = painter.Render(
            cycle,
            in feed,
            realmTriMeshes,
            land);
        _previousShadeRealmTriMeshes = realmTriMeshes;
        _previousShadeCycleSerialNo = cycle.SerialNo;
        DemandKeptGpuAllowance(painter);
        return _lastShadowDiagnostics;
    }

    public IGpuRasterizeMark PrepareWorldTarget(int width, int height, int specimenTally)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (_targets is { } latest
            && latest.Width == width
            && latest.Height == height
            && latest.SampleCount == specimenTally)
            return latest.World;
        RasterizeBundleHubCapabilities capabilities =
            RenderPackCapabilityPicker.Resolve(_device.Capabilities);
        RenderPackResourceAllowance allowance = RenderPackResourceAllowancePlanner.DemandWithinHub(
            Descriptor,
            Preset,
            width,
            height,
            specimenTally,
            capabilities);
        var contender = ObjectiveGroup.Create(
            _device,
            Descriptor,
            Preset,
            _sampler,
            width,
            height,
            specimenTally);
        ObjectiveGroup? preceding = _targets;
        _targets = contender;
        _assetAllowance = allowance;
        _housedGpuAllowanceOctets = RenderPackResidentAllowance.Net(
            Preset.MaxResidentGpuBytes,
            width,
            height,
            capabilities.MaxPackResidentBytes);
        _assetGen = checked(_assetGen + 1);
        _renderedCycle = false;
        preceding?.Dispose();
        return contender.World;
    }

    public void PaintPostProc(IGpuCycle cycle, in AtmosphericCycleFeeds feeds)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ObjectiveGroup marks = _targets
            ?? throw new InvalidOperationException("PrepareWorldTarget must run prior to the fullscreen graph");
        if (feeds.ViewportWidth != marks.Width || feeds.ViewportHeight != marks.Height)
            throw new InvalidOperationException("Fullscreen graph inputs and targets belong to different frames");

        float elevationRule = EvaluateSunElevationRule(feeds.SunElevationDegrees);
        float dayClusterRule = EvaluateDayClusterRule(feeds.ActiveDayGroup);
        IReadOnlyList<SunElevationKnot> shadeCurve =
            Descriptor.AtmospherePolicy?.DirectedShadeLampElevationResponse ?? [];
        IReadOnlyList<SunElevationKnot> volumetricCurve =
            Descriptor.AtmospherePolicy?.VolumetricShaftSunElevationResponse ?? [];
        float shadeElevationRule = shadeCurve.Count == 0
            ? elevationRule
            : RenderPackAtmosphereRuleEvaluation.DirectionalShadow(
                shadeCurve,
                feeds.SunElevationDegrees,
                elevationRule);
        float volumetricElevationRule = volumetricCurve.Count == 0
            ? 0f
            : RenderPackAtmosphereRuleEvaluation.VolumetricShaft(
                volumetricCurve,
                feeds.SunElevationDegrees,
                0f);
        float sunRule = EvaluateSunRule(
            feeds,
            elevationRule,
            dayClusterRule);
        var cycleVals = new AtmosphericCycleUniforms(
            new Vector4(feeds.SunScreenUv, sunRule, feeds.SunElevationDegrees),
            new Vector4(feeds.SunColor, sunRule),
            new Vector4(marks.Width, marks.Height, 1f / marks.Width, 1f / marks.Height),
            new Vector4((float)feeds.Weather, feeds.WeatherIntensity,
                (float)Math.Clamp(feeds.DeltaSeconds, 0d, 1d), feeds.IsOutdoor ? 1f : 0f),
            new Vector4(feeds.SunDirection, feeds.SunDirectionalBrightness),
            new Vector4(
                feeds.ActiveDayGroup,
                dayClusterRule,
                shadeElevationRule,
                volumetricElevationRule),
            feeds.InverseViewProjection,
            Vector4.Zero,
            Vector4.Zero);
        GpuLoopAlloc cycleChunk = cycle.ReserveLoop(AtmosphericCycleUniforms.SizeInBytes, GpuLoopPurpose.Uniform);
        MemoryMarshal.Write(cycleChunk.Data, in cycleVals);
        GpuLoopAlloc prefsChunk = cycle.ReserveLoop(PackPreferencesUniforms.SizeInBytes, GpuLoopPurpose.Uniform);
        PackPreferencesUniforms prefs = _prefs;
        MemoryMarshal.Write(prefsChunk.Data, in prefs);

        foreach (Joint joint in _joints)
            Draw(cycle, joint, marks, cycleChunk, prefsChunk);
        _previousFeeds = feeds;
        _renderedCycle = true;
    }

    public RenderPackEngineTelemetry GrabTelemetry()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ObjectiveGroup? marks = _targets;
        if (!_renderedCycle || marks is null)
            return RenderPackEngineTelemetry.Empty(Preset.Id);

        int shadePassTally = _shadePass is null ? 0 : 1;
        var passs = new RenderPackPassTelemetry[_joints.Length + shadePassTally];
        int passOrdinal = 0;
        if (_shadePass is not null)
        {
            passs[passOrdinal++] = new RenderPackPassTelemetry(
                _shadePass.Id,
                _lastShadowDiagnostics.LastResolvedGpuMilliseconds,
                _lastShadowDiagnostics.DrawCalls,
                DispatchCalls: 0);
        }
        for (int idx = 0; idx < _joints.Length; idx++)
        {
            Joint joint = _joints[idx];
            _device.Tickers.TryLocate(joint.TimerName, out double millis);
            passs[passOrdinal++] = new RenderPackPassTelemetry(
                joint.Pass.Id,
                millis,
                DrawCalls: 1,
                DispatchCalls: 0);
        }

        return new RenderPackEngineTelemetry(
            Preset.Id,
            checked(
                _assetAllowance.RetainedGpuBytes
                + (_directedShades?.KeptGpuBufOctets ?? 0L)),
            _assetAllowance.MultisampleGpuBytes,
            marks.ImageTally + shadePassTally,
            BufferCount: _directedShades?.KeptGpuBufTally ?? 0,
            DrawCalls: _joints.Length + _lastShadowDiagnostics.DrawCalls,
            DispatchCalls: 0,
            ShadowCasterCount: _previousShadeInvokerTally,
            CascadeDrawCount: _lastShadowDiagnostics.CascadeCount,
            CpuClassificationCalls: _previousShadeTaxonomyCalls,
            _previousFeeds.SunElevationDegrees,
            _previousFeeds.ActiveDayGroup,
            _previousFeeds.Weather.ToString(),
            _previousFeeds.WeatherIntensity,
            _previousFeeds.IsOutdoor,
            DirectionalShadowStrength: _lastShadowDiagnostics.Strength,
            passs)
        {
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
        double gpuMillis = 0d;
        bool settled = _targets is not null;
        for (int idx = 0; idx < _joints.Length; idx++)
        {
            if (!_device.Tickers.TryGrabSettled(
                    _joints[idx].TimerName,
                    out double millis))
            {
                settled = false;
            }
            else
            {
                gpuMillis += millis;
            }
        }
        if (_directedShades is not null)
        {
            if (!_device.Tickers.TryGrabSettled(
                    RasterizeBundlePerformanceAmbitLabels.EnhancedRealmRecipient,
                    out double recipientMillis))
            {
                settled = false;
            }
            else
            {
                gpuMillis += recipientMillis;
            }
            int shadeTickerTally = _directedShades.MultiviewCascadesTurnedOn
                && _lastShadowDiagnostics.CascadeCount > 0
                    ? 1
                    : _lastShadowDiagnostics.CascadeCount;
            for (int idx = 0; idx < shadeTickerTally; idx++)
            {
                if (!_device.Tickers.TryGrabSettled(
                        _directedShades.MultiviewCascadesTurnedOn
                            ? DirectionalSunShadePainter.MultiviewTickerLabel
                            : DirectionalSunShadePainter.TickerLabel(idx),
                        out double millis))
                {
                    settled = false;
                }
                else
                {
                    gpuMillis += millis;
                }
            }
        }
        return new RenderPackEnginePerformanceMetrics(
            _assetGen,
            settled,
            settled ? gpuMillis : 0d,
            checked(
                _assetAllowance.RetainedGpuBytes
                + (_directedShades?.KeptGpuBufOctets ?? 0L)),
            _assetAllowance.MultisampleGpuBytes);
    }

    private void DemandKeptGpuAllowance(
        DirectionalSunShadePainter painter)
    {
        long sum = checked(
            _assetAllowance.RetainedGpuBytes
            + painter.KeptGpuBufOctets);
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
        _directedShades?.Dispose();
        for (int idx = _joints.Length - 1; idx >= 0; idx--)
            _joints[idx].Pipeline.Dispose();
        _hdrTenancy.Dispose();
    }

    private void Draw(
        IGpuCycle cycle,
        Joint joint,
        ObjectiveGroup marks,
        GpuLoopAlloc cycleChunk,
        GpuLoopAlloc prefsChunk)
    {
        IGpuRasterizeMark? product = joint.Pass.ResourceWrites.Count == 0
            ? null
            : marks.Resource(joint.Pass.ResourceWrites[0]).Target;
        using IGpuSweepCoder coder = cycle.BeginPass(new GpuPassSpec
        {
            Name = joint.TimerName,
            Color = new GpuTintAffix(product, GpuPullOp.Clear, GpuVaultOp.Store, Vector4.Zero),
            ZDepth = null,
            SampleCount = 1,
        });
        using IDisposable ticker = coder.CommenceTickerAmbit(joint.TimerName);
        coder.BindPipeline(joint.Pipeline);
        coder.AttachUniformBuf(GpuBindingModel.UniformAtmosphericCycle,
            cycleChunk.Buffer, cycleChunk.ShiftOctets, AtmosphericCycleUniforms.SizeInBytes);
        GpuLoopAlloc passChunk = cycle.ReserveLoop(
            AtmosphericBundleSweepUniforms.SizeInBytes,
            GpuLoopPurpose.Uniform);
        var zero = AtmosphericBundleSweepUniforms.From(Vector4.Zero);
        MemoryMarshal.Write(passChunk.Data, in zero);
        coder.AttachUniformBuf(GpuBindingModel.UniformBundlePass,
            passChunk.Buffer, passChunk.ShiftOctets, AtmosphericBundleSweepUniforms.SizeInBytes);
        coder.AttachUniformBuf(GpuBindingModel.UniformBundlePrefs,
            prefsChunk.Buffer, prefsChunk.ShiftOctets, PackPreferencesUniforms.SizeInBytes);

        Span<GpuTextureSlot> sockets = stackalloc GpuTextureSlot[4];
        sockets.Fill(GpuTextureSlot.Unassigned);
        for (int idx = 0; idx < joint.Inputs.Length; idx++)
            sockets[idx] = Resolve(joint.Inputs[idx], marks);
        GpuShoveConstants push = GpuShoveConstants.Default;
        push.TextureIndexA = sockets[0].Index;
        push.TextureOrdinalB = sockets[1].Index;
        push.ParamA = BitConverter.UInt32BitsToSingle(sockets[2].Index);
        push.ParameterB = BitConverter.UInt32BitsToSingle(sockets[3].Index);
        coder.AssignPushConstants(in push);
        coder.Draw(3, 1, 0, 0);
    }

    private static GpuTextureSlot Resolve(RasterizeBundleBitmapFeed feed, ObjectiveGroup marks)
    {
        return feed.Semantic is { } semantic
            ? semantic switch
            {
                SemanticInput.WorldColor => marks.WorldColor,
                SemanticInput.SceneDepth => marks.RealmZDepth,
                _ => throw new NotSupportedException($"Texture semantic '{semantic}' is not supported by Tier-1"),
            }
            : marks.Resource(feed.ResourceId!).Slot;
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

    private static float EvaluateSunRule(
        in AtmosphericCycleFeeds feeds,
        float elevationRule,
        float dayClusterRule)
    {
        return !feeds.IsOutdoor || !feeds.SunIsOnScreen
            ? 0f
            : Math.Clamp(
            elevationRule
                * dayClusterRule
                * EvaluateWeatherRule(feeds.Weather, feeds.WeatherIntensity),
            0f,
            4f);
    }

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
                .Single(val => val.Semantic == semantic);
            return RasterizeBundleShaderHoldings.PullVariant(descriptor, holdings, variant);
        }
    }

    private static DirectionalShadeQuality LocateShadeFidelity(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions)
    {
        DirectionalShadePreset shadePreset = preset.Semantic switch
        {
            RenderQualityRole.Low => DirectionalShadePreset.Low,
            RenderQualityRole.High => DirectionalShadePreset.High,
            _ => DirectionalShadePreset.Medium,
        };
        var fidelity = DirectionalShadeQuality.For(shadePreset);
        RenderResourceSpec asset = descriptor.Resources.Single(val =>
            val.Semantic == RenderResourceRole.DirectionalShadowDepth);
        RenderExtentSpec extent = preset.ResourceOverrides.FirstOrDefault(val =>
                string.Equals(
                    val.ResourceId,
                    asset.Id,
                    StringComparison.OrdinalIgnoreCase))?.Extent
            ?? asset.Extent
            ?? throw new NotSupportedException(
                "The DirectionalShadowDepth semantic resource has no image extent");
        if (extent.Mode != ExtentRule.AbsolutePixels
            || extent.Width != extent.Height
            || extent.Width != Math.Truncate(extent.Width)
            || extent.Width is < 1 or > 16_384
            || extent.Layers is < 1 or > 4)
        {
            throw new NotSupportedException(
                "The DirectionalShadowDepth semantic resource has to be a square "
                + "absolute 1..16384 image with 1..4 array layers");
        }

        float reach = ScanSemanticSetting(
            descriptor,
            preset,
            userSettingSubstitutions,
            RenderSettingRole.DirectionalShadowReachMetres);
        int taps = ScanShadePcfTaps(descriptor, preset, userSettingSubstitutions);
        int radius = taps switch
        {
            1 => 0,
            9 => 1,
            25 => 2,
            _ => throw new NotSupportedException(
                "DirectionalShadowPcfTaps must resolve to precisely 1, 9, or 25 samples"),
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
        IReadOnlyDictionary<string, string> userSettingSubstitutions)
    {
        RenderSettingSpec setting = descriptor.Settings.Single(val =>
            val.Semantic == RenderSettingRole.DirectionalShadowPcfTaps);
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
                    "DirectionalShadowPcfTaps must resolve to an integer sample count");
    }

    private static float ScanSemanticSetting(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions,
        RenderSettingRole semantic)
    {
        RenderSettingSpec setting = descriptor.Settings.Single(val =>
            val.Semantic == semantic);
        string value = RenderPackPreferenceResolution.Resolve(
            setting,
            preset,
            userSettingSubstitutions);
        return !SettingValueCodec.TryPack(setting, value, out float encoded)
            || !float.IsFinite(encoded)
            ? throw new NotSupportedException(
                $"Setting semantic '{semantic}' didn't resolve to a finite value")
            : encoded;
    }

    private RenderResourceSpec Resource(string ident) =>
        _assetList.TryGetValue(ident, out RenderResourceSpec? val)
            ? val
            : throw new InvalidOperationException($"Unrecognized render-pack resource '{ident}'.");

    private static GpuBitmapFmt ComposeOf(RenderResourceSpec asset) => asset.Format switch
    {
        PixelFormatKind.HdrColor => GpuBitmapFmt.Rgba16FloatRenderTarget,
        PixelFormatKind.LdrColor or PixelFormatKind.SingleChannel =>
            GpuBitmapFmt.Rgba8UnormRenderTarget,
        _ => throw new NotSupportedException(
            $"Fullscreen resource '{asset.Id}' has not supported format '{asset.Format}'."),
    };

    private static GpuBitmapFmt VetProduct(RenderResourceSpec asset)
    {
        return asset.Kind != GpuResourceKind.Image2D
            || (asset.Usage & GpuResourceUsage.ColorAttachment) == 0
            || asset.Extent is null
            ? throw new NotSupportedException(
                $"Fullscreen output '{asset.Id}' has to be an extent-declared colour Image2D")
            : ComposeOf(asset);
    }

    private sealed record Joint(
        RenderPassSpec Pass,
        IGpuPipe Pipeline,
        RasterizeBundleBitmapFeed[] Inputs,
        string TimerName);

    private sealed class ObjectiveGroup : IDisposable
    {
        private readonly IClientGpuDevice _device;
        private readonly Dictionary<string, AssetMark> _assetList;
        private readonly GpuTextureSlot[] _sockets;
        private readonly string? _primaryRealmAssetIdent;

        private ObjectiveGroup(
            IClientGpuDevice dev,
            int width,
            int height,
            int specimenTally,
            IGpuRasterizeMark realm,
            GpuTextureSlot realmTint,
            GpuTextureSlot realmZDepth,
            Dictionary<string, AssetMark> assetList,
            GpuTextureSlot[] sockets,
            string? primaryRealmAssetIdent)
        {
            _device = dev;
            Width = width;
            Height = height;
            SampleCount = specimenTally;
            World = realm;
            WorldColor = realmTint;
            RealmZDepth = realmZDepth;
            _assetList = assetList;
            _sockets = sockets;
            _primaryRealmAssetIdent = primaryRealmAssetIdent;
        }

        internal int Width { get; }
        internal int Height { get; }
        internal int SampleCount { get; }
        internal IGpuRasterizeMark World { get; }
        internal GpuTextureSlot WorldColor { get; }
        internal GpuTextureSlot RealmZDepth { get; }
        internal int ImageTally => checked(
            2
            + _assetList.Count
            + (SampleCount > 1 ? (RealmZDepth.IsAssigned ? 2 : 1) : 0));

        internal AssetMark Resource(string ident) =>
            string.Equals(ident, _primaryRealmAssetIdent, StringComparison.OrdinalIgnoreCase)
                ? new AssetMark(World, WorldColor)
                : _assetList.TryGetValue(ident, out AssetMark? val)
                    ? val
                    : throw new InvalidOperationException($"Resource '{ident}' has no produced image");

        internal static ObjectiveGroup Create(
            IClientGpuDevice dev,
            RenderPackCard descriptor,
            QualityLadderStep preset,
            IClientGpuSampler sampler,
            int width,
            int height,
            int specimens)
        {
            var marks = new List<IGpuRasterizeMark>();
            var sockets = new List<GpuTextureSlot>();
            try
            {
                bool needsZDepth = descriptor.Passes.Any(pass =>
                    pass.SemanticInputs.Contains(SemanticInput.SceneDepth));
                IGpuRasterizeMark realm = dev.BuildRasterizeMark(new GpuRenderTargetSpec(
                    $"render-pack-{descriptor.Id}-world-hdr", width, height,
                    GpuBitmapFmt.Rgba16FloatRenderTarget,
                    GpuBitmapFmt.Depth24Stencil8,
                    specimens,
                    needsZDepth));
                marks.Add(realm);
                GpuTextureSlot realmTint = Register(dev, realm.ColorTexture, sampler, sockets);
                GpuTextureSlot realmZDepth = needsZDepth
                    ? Register(dev, realm.ZDepthTexture!, sampler, sockets)
                    : GpuTextureSlot.Unassigned;
                var assetList = new Dictionary<string, AssetMark>(StringComparer.OrdinalIgnoreCase);
                string? primaryRealmAssetIdent = descriptor.Resources.SingleOrDefault(asset =>
                    asset.Semantic == RenderResourceRole.MainWorldHdr)?.Id;
                var written = descriptor.Passes
                    .SelectMany(pass => pass.ResourceWrites)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (RenderResourceSpec resource in descriptor.Resources)
                {
                    if (!written.Contains(resource.Id)
                        || resource.Semantic is RenderResourceRole.MainWorldHdr
                            or RenderResourceRole.DirectionalShadowDepth)
                        continue;
                    if (resource.Kind != GpuResourceKind.Image2D
                        || (resource.Usage & GpuResourceUsage.ColorAttachment) == 0)
                        throw new NotSupportedException($"Fullscreen resource '{resource.Id}' isn't a colour image");
                    (int assetWidth, int assetHeight) = Reach(resource, preset, width, height);
                    IGpuRasterizeMark mark = dev.BuildRasterizeMark(new GpuRenderTargetSpec(
                        $"render-pack-{descriptor.Id}-{resource.Id}", assetWidth, assetHeight,
                        ComposeOf(resource), null, 1));
                    marks.Add(mark);
                    assetList.Add(resource.Id, new AssetMark(
                        mark,
                        Register(dev, mark.ColorTexture, sampler, sockets)));
                }
                return new ObjectiveGroup(
                    dev, width, height, specimens, realm, realmTint, realmZDepth,
                    assetList, [.. sockets], primaryRealmAssetIdent);
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
            for (int idx = _sockets.Length - 1; idx >= 0; idx--)
                _device.FreeTextureSocket(_sockets[idx]);
            foreach (AssetMark asset in _assetList.Values.Reverse())
                asset.Target.Dispose();
            World.Dispose();
        }

        private static (int Width, int Height) Reach(
            RenderResourceSpec asset,
            QualityLadderStep preset,
            int width,
            int height)
        {
            RenderExtentSpec reach = preset.ResourceOverrides.FirstOrDefault(val =>
                    string.Equals(val.ResourceId, asset.Id, StringComparison.OrdinalIgnoreCase))?.Extent
                ?? asset.Extent
                ?? throw new NotSupportedException($"Image resource '{asset.Id}' has no extent");
            return reach.Mode switch
            {
                ExtentRule.AbsolutePixels =>
                    (checked((int)reach.Width), checked((int)reach.Height)),
                ExtentRule.RelativeToMainWorld or ExtentRule.RelativeToOutput =>
                    (Math.Max(1, (int)Math.Ceiling(width * reach.Width)),
                     Math.Max(1, (int)Math.Ceiling(height * reach.Height))),
                _ => throw new NotSupportedException($"Resource '{asset.Id}' has not supported extent mode"),
            };
        }

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

    internal sealed record AssetMark(IGpuRasterizeMark Target, GpuTextureSlot Slot);
}

internal sealed class DeclaredDirectionalShadeRenderPackGraph :
    DeclaredFullscreenRasterizeBundleGraph,
    IDirectionalShadeRealmGraphEngine
{
    internal DeclaredDirectionalShadeRenderPackGraph(
        IClientGpuDevice dev,
        RenderPackCard descriptor,
        IRenderPackFiles holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions)
        : base(dev, descriptor, holdings, preset, userSettingSubstitutions)
    {
    }

    internal DeclaredDirectionalShadeRenderPackGraph(
        IClientGpuDevice dev,
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string> userSettingSubstitutions)
        : base(dev, descriptor, holdings, preset, userSettingSubstitutions)
    {
    }

    public IDirectionalShadeReceiverSource DirectedShadeRecipients =>
        DeclaredDirectedShadeRecipients;

    public DirectionalSunShadeTelemetry RenderDirectionalShadows(
        IGpuCycle cycle,
        in RasterizeCycleFoundation foundation,
        in RealmRenderFrame realm,
        int engagedDayCluster,
        in RenderStageProbe tableau,
        RealmPaintRouter realmTriMeshes,
        LandModernPainter land) => RenderDeclaredDirectionalShadows(
            cycle,
            in foundation,
            in realm,
            engagedDayCluster,
            in tableau,
            realmTriMeshes,
            land);
}
