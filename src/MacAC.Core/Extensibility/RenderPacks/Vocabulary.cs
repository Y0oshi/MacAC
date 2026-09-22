namespace MacAC.Extensibility.RenderPacks;

/// <summary>Prerequisite tier a pack reaches.</summary>
public enum PackTier
{
    Tier1 = 1,
    Tier2 = 2,
    Tier2Plus = 3,
}

/// <summary>Renderer features a pack may require or take advantage of.</summary>
public enum RenderFeature
{
    MainWorldColorIntermediate,
    FullscreenPasses,
    SceneDepthSampling,
    SceneNormalSampling,
    AuthoredSunDirection,
    AuthoredSunScreenPosition,
    AuthoredWeather,
    DirectionalShadowMaps,
    OutdoorDirectionalShadowCasterReplay,
    AnimatedCasterTransforms,
    AlphaCutoutShadowCasters,
    GpuTimestampQueries,
    MultiviewDirectionalShadowCascades,
    AuthoredCelestialDirectionalLight,
}

/// <summary>Fixed points in the frame where a declared pass may run.</summary>
public enum RenderPassAnchor
{
    ShadowDepthBeforeWorld,
    AtmosphereBeforeToneMap,
    ToneMap,
    AfterToneMapBeforePrivateViewports,
}

/// <summary>Immutable frame facts the renderer can bind for a pack.</summary>
public enum SemanticInput
{
    WorldColor,
    SceneDepth,
    SceneNormals,
    SunDirection,
    SunScreenPosition,
    ActiveDayGroup,
    Weather,
    CameraMatrices,
    ShadowCasterTransforms,
    DirectionalShadowMaps,
    FrameTime,
    SelectedCelestialDirectionalLight,
}

/// <summary>Renderer-owned replays of retained geometry a pack may request.</summary>
public enum SceneReplayRole
{
    OutdoorDirectionalShadowCasters,
}

[Flags]
public enum ShadowCasterKind
{
    None = 0,
    Terrain = 1 << 0,
    OpaqueWorld = 1 << 1,
    AlphaCutoutWorld = 1 << 2,
    AnimatedOpaque = 1 << 3,
    AnimatedAlphaCutout = 1 << 4,
}

/// <summary>Base renderer pipeline a variant specializes.</summary>
public enum PipelineBaseRole
{
    Terrain,
    WorldMesh,
    EnvCell,
}

[Flags]
public enum MaterialKind
{
    None = 0,
    Opaque = 1 << 0,
    AlphaCutout = 1 << 1,
    AnimatedOpaque = 1 << 2,
    AnimatedAlphaCutout = 1 << 3,
}

public enum GpuResourceKind
{
    Image2D,
    Image2DArray,
    Buffer,
}

/// <summary>Portable format families the renderer resolves to real formats.</summary>
public enum PixelFormatKind
{
    LdrColor,
    HdrColor,
    SingleChannel,
    DirectionalDepth,
    StructuredData,
}

/// <summary>How a declared image extent is read.</summary>
public enum ExtentRule
{
    AbsolutePixels,
    RelativeToMainWorld,
    RelativeToOutput,
}

[Flags]
public enum GpuResourceUsage
{
    None = 0,
    Sampled = 1 << 0,
    ColorAttachment = 1 << 1,
    DepthAttachment = 1 << 2,
    Storage = 1 << 3,
    TransferSource = 1 << 4,
    TransferDestination = 1 << 5,
}

public enum GpuResourceLifetime
{
    TransientPass,
    FrameFlight,
    ActivePack,
}

public enum RenderResourceRole
{
    Custom,
    MainWorldHdr,
    BloomPing,
    BloomPong,
    SunOcclusionMask,
    SunRays,
    DirectionalShadowDepth,
    VolumetricShafts,
}

public enum RenderPassRole
{
    CustomFullscreen,
    DirectionalShadowDepth,
    BloomDownsample,
    BloomBlurHorizontal,
    BloomBlurVertical,
    SunOcclusion,
    SunRays,
    VolumetricShafts,
    FilmicComposite,
}

public enum PipelineVariantRole
{
    Custom,
    TerrainDirectionalShadowCaster,
    WorldOpaqueDirectionalShadowCaster,
    WorldAlphaCutoutDirectionalShadowCaster,
    TerrainDirectionalShadowReceiver,
    WorldDirectionalShadowReceiver,
    TerrainMultiviewDirectionalShadowCaster,
    WorldOpaqueMultiviewDirectionalShadowCaster,
    WorldAlphaCutoutMultiviewDirectionalShadowCaster,
}

[Flags]
public enum QualityExecutionHints
{
    None = 0,

    FusedAtmosphericPostProcess = 1 << 0,

    MultiviewDirectionalShadowCascades = 1 << 1,
}

public enum RenderQualityRole
{
    Custom,
    Low,
    Medium,
    High,
    Automatic,
}

/// <summary>Storage and presentation kind of a pack-defined setting.</summary>
public enum SettingValueKind
{
    Boolean,
    Integer,
    Float,
    Choice,
}

public enum RenderSettingRole
{
    Custom,
    BloomStrength,
    FilmicStrength,
    Exposure,
    GradeSaturation,
    GradeContrast,
    VignetteStrength,
    SunRayStrength,
    DirectionalShadowStrength,
    DirectionalShadowReachMetres,
    DirectionalShadowPcfTaps,
    VolumetricStrength,
    VolumetricRayMarchSteps,
    AutomaticQuality,
    WindEnabled,
    WindStrength,
    WindDirectionDegrees,
    WindLeanMetres,
    WindBranchMetres,
    WindFlutterMetres,
    WindCanopyHeightMetres,
}
