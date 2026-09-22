namespace MacAC.Extensibility.RenderPacks;

/// <summary>Shape of one declared image.</summary>
/// <param name="Mode">Absolute pixels, or a scale of a renderer surface.</param>
/// <param name="Width">Pixel width when absolute; horizontal scale otherwise.</param>
/// <param name="Height">Pixel height when absolute; vertical scale otherwise.</param>
/// <param name="Layers">Array layers; one for a plain 2-D image.</param>
public sealed record RenderExtentSpec(
    ExtentRule Mode,
    double Width,
    double Height,
    int Layers = 1);

public sealed record RenderResourceSpec(
    string Id,
    GpuResourceKind Kind,
    PixelFormatKind Format,
    RenderExtentSpec? Extent,
    long SizeBytes,
    GpuResourceUsage Usage,
    GpuResourceLifetime Lifetime,
    long EstimatedResidentBytes)
{
    public RenderResourceRole Semantic { get; init; } = RenderResourceRole.Custom;
}

public sealed record RenderPassSpec(
    string Id,
    RenderPassAnchor Hook,
    string VertexShaderAsset,
    string FragmentShaderAsset,
    IReadOnlyList<SemanticInput> SemanticInputs,
    IReadOnlyList<string> ResourceReads,
    IReadOnlyList<string> ResourceWrites)
{
    public RenderPassRole Semantic { get; init; } = RenderPassRole.CustomFullscreen;
}

public sealed record SceneReplaySpec(
    string Id,
    SceneReplayRole Semantic,
    ShadowCasterKind CasterClasses,
    int ViewCount);

public sealed record PipelineVariantSpec(
    string Id,
    PipelineBaseRole BaseSemantic,
    string VertexShaderAsset,
    string FragmentShaderAsset,
    MaterialKind CompatibleMaterials,
    IReadOnlyList<SemanticInput> SemanticInputs)
{
    public PipelineVariantRole Semantic { get; init; } = PipelineVariantRole.Custom;
}

/// <summary>Per-step replacement for one resource's size.</summary>
public sealed record QualityResourceTweak(
    string ResourceId,
    RenderExtentSpec? Extent,
    long SizeBytes,
    long EstimatedResidentBytes);

/// <summary>Per-step value for a declared user setting.</summary>
public sealed record QualitySettingTweak(
    string SettingId,
    string Value);

/// <summary>One rung of a pack's quality ladder.</summary>
public sealed record QualityLadderStep(
    string Id,
    string DisplayName,
    IReadOnlyList<RenderFeature> RequiredCapabilities,
    IReadOnlyList<QualityResourceTweak> ResourceOverrides,
    IReadOnlyList<QualitySettingTweak> SettingOverrides,
    long MaxResidentGpuBytes,
    double MaxIncrementalGpuMillisecondsP50,
    double MaxIncrementalGpuMillisecondsP99,
    double MaxIncrementalCpuMillisecondsP50,
    double MaxIncrementalCpuMillisecondsP99,
    bool AutoEligible = true)
{
    public RenderQualityRole Semantic { get; init; } = RenderQualityRole.Custom;

    public QualityExecutionHints ExecutionHints { get; init; } = QualityExecutionHints.None;
}

public sealed record RenderSettingSpec(
    string Id,
    string DisplayName,
    SettingValueKind Kind,
    string DefaultValue,
    double? Minimum,
    double? Maximum,
    double? Step,
    IReadOnlyList<string> Choices)
{
    public RenderSettingRole Semantic { get; init; } = RenderSettingRole.Custom;
}

public sealed record SunElevationKnot(
    double ElevationDegrees,
    double Multiplier);

/// <summary>Maps an authored AC day group to an effect multiplier.</summary>
public sealed record DayGroupWeight(
    int ActiveDayGroup,
    double Multiplier);

public sealed record WindWeatherKnot(
    string WeatherKind,
    double Mean,
    double Gust);

/// <summary>The pack's own reading of the authored atmosphere.</summary>
public sealed record AtmospherePolicySpec(
    IReadOnlyList<SunElevationKnot> SunElevationResponse,
    IReadOnlyList<DayGroupWeight> ActiveDayGroupMultipliers)
{
    public IReadOnlyList<SunElevationKnot> DirectedShadeLampElevationResponse { get; init; } = [];

    public IReadOnlyList<SunElevationKnot> VolumetricShaftSunElevationResponse { get; init; } = [];

    public IReadOnlyList<WindWeatherKnot> FoliageWindByWeather { get; init; } = [];

    public IReadOnlyList<uint> FoliageExclusions { get; init; } = [];
}
