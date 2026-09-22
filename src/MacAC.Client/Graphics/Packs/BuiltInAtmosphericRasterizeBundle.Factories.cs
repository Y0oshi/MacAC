using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal static partial class BuiltInAtmosphericRasterizeBundle
{
    internal static IRenderPackFiles BuildHoldings(string shaderFolder) =>
        new FolderRasterizeBundleHoldings(shaderFolder);

    private static IReadOnlyList<RenderResourceSpec> Resources()
    {
        return [
        Image("world-hdr", RenderResourceRole.MainWorldHdr,
            PixelFormatKind.HdrColor, 1.0, 1.0, 32L * 1024 * 1024),
        Image("bloom-a", RenderResourceRole.BloomPing,
            PixelFormatKind.HdrColor, 0.5, 0.5, 8L * 1024 * 1024),
        Image("bloom-b", RenderResourceRole.BloomPong,
            PixelFormatKind.HdrColor, 0.5, 0.5, 8L * 1024 * 1024),
        Image("sun-mask", RenderResourceRole.SunOcclusionMask,
            PixelFormatKind.SingleChannel, 0.25, 0.25, 2L * 1024 * 1024),
        Image("sun-rays", RenderResourceRole.SunRays,
            PixelFormatKind.HdrColor, 0.25, 0.25, 2L * 1024 * 1024),
        new RenderResourceSpec(
            "directional-shadow-depth",
            GpuResourceKind.Image2DArray,
            PixelFormatKind.DirectionalDepth,
            new RenderExtentSpec(ExtentRule.AbsolutePixels, 1024, 1024, Layers: 2),
            SizeBytes: 0,
            GpuResourceUsage.Sampled | GpuResourceUsage.DepthAttachment,
            GpuResourceLifetime.ActivePack,
            EstimatedResidentBytes: 8L * 1024 * 1024)
            with { Semantic = RenderResourceRole.DirectionalShadowDepth },
        Image("volumetric", RenderResourceRole.VolumetricShafts,
            PixelFormatKind.HdrColor, 0.25, 0.25, 2L * 1024 * 1024),
    ];
    }

    private static IReadOnlyList<RenderPassSpec> Passs()
    {
        return [
        Pass(
            "directional-shadow-depth",
            RenderPassRole.DirectionalShadowDepth,
            RenderPassAnchor.ShadowDepthBeforeWorld,
            "sunshade_props_solid.vert.spv",
            "sunshade_props_solid.frag.spv",
            [SemanticInput.CameraMatrices,
                SemanticInput.SelectedCelestialDirectionalLight,
                SemanticInput.ShadowCasterTransforms, SemanticInput.ActiveDayGroup,
                SemanticInput.Weather],
            [],
            ["directional-shadow-depth"]),
        Pass(
            "sun-occlusion",
            RenderPassRole.SunOcclusion,
            RenderPassAnchor.AtmosphereBeforeToneMap,
            "sun_occluder.vert.spv",
            "sun_occluder.frag.spv",
            [SemanticInput.SceneDepth, SemanticInput.SunScreenPosition,
                SemanticInput.ActiveDayGroup, SemanticInput.Weather],
            [],
            ["sun-mask"]),
        Pass(
            "sun-rays",
            RenderPassRole.SunRays,
            RenderPassAnchor.AtmosphereBeforeToneMap,
            "sun_shafts.vert.spv",
            "sun_shafts.frag.spv",
            [SemanticInput.SunScreenPosition, SemanticInput.FrameTime],
            ["sun-mask"],
            ["sun-rays"]),
        Pass(
            "volumetric-shafts",
            RenderPassRole.VolumetricShafts,
            RenderPassAnchor.AtmosphereBeforeToneMap,
            "haze_volumes.vert.spv",
            "haze_volumes.frag.spv",
            [SemanticInput.SceneDepth, SemanticInput.CameraMatrices,
                SemanticInput.SunDirection, SemanticInput.DirectionalShadowMaps,
                SemanticInput.ActiveDayGroup, SemanticInput.Weather],
            ["directional-shadow-depth"],
            ["volumetric"]),
        Pass(
            "bloom-downsample",
            RenderPassRole.BloomDownsample,
            RenderPassAnchor.AtmosphereBeforeToneMap,
            "glow_downsample.vert.spv",
            "glow_downsample.frag.spv",
            [SemanticInput.WorldColor],
            ["sun-rays", "volumetric"],
            ["bloom-a"]),
        Pass(
            "bloom-blur-horizontal",
            RenderPassRole.BloomBlurHorizontal,
            RenderPassAnchor.AtmosphereBeforeToneMap,
            "glow_blur.vert.spv",
            "glow_blur.frag.spv",
            [SemanticInput.FrameTime],
            ["bloom-a"],
            ["bloom-b"]),
        Pass(
            "bloom-blur-vertical",
            RenderPassRole.BloomBlurVertical,
            RenderPassAnchor.AtmosphereBeforeToneMap,
            "glow_blur.vert.spv",
            "glow_blur.frag.spv",
            [SemanticInput.FrameTime],
            ["bloom-b"],
            ["bloom-a"]),
        Pass(
            "filmic-composite",
            RenderPassRole.FilmicComposite,
            RenderPassAnchor.ToneMap,
            "tonemap_filmic.vert.spv",
            "tonemap_filmic.frag.spv",
            [SemanticInput.WorldColor, SemanticInput.FrameTime],
            ["bloom-a", "sun-rays", "volumetric"],
            []),
    ];
    }

    private static IReadOnlyList<SceneReplaySpec> TableauReplays()
    {
        return [
        new SceneReplaySpec(
            "outdoor-directional-shadow-casters",
            SceneReplayRole.OutdoorDirectionalShadowCasters,
            ShadowCasterKind.Terrain
                | ShadowCasterKind.OpaqueWorld
                | ShadowCasterKind.AlphaCutoutWorld
                | ShadowCasterKind.AnimatedOpaque
                | ShadowCasterKind.AnimatedAlphaCutout,
            ViewCount: 4),
    ];
    }

    private static IReadOnlyList<PipelineVariantSpec> PipeVariants()
    {
        return [
        Variant("terrain-shadow-caster", PipelineVariantRole.TerrainDirectionalShadowCaster,
            PipelineBaseRole.Terrain,
            "sunshade_land.vert.spv", "sunshade_land.frag.spv",
            MaterialKind.Opaque,
            [SemanticInput.CameraMatrices]),
        Variant("world-shadow-opaque", PipelineVariantRole.WorldOpaqueDirectionalShadowCaster,
            PipelineBaseRole.WorldMesh,
            "sunshade_props_solid.vert.spv", "sunshade_props_solid.frag.spv",
            MaterialKind.Opaque | MaterialKind.AnimatedOpaque,
            [SemanticInput.CameraMatrices, SemanticInput.ShadowCasterTransforms]),
        Variant("world-shadow-cutout", PipelineVariantRole.WorldAlphaCutoutDirectionalShadowCaster,
            PipelineBaseRole.WorldMesh,
            "sunshade_props_cutout.vert.spv", "sunshade_props_cutout.frag.spv",
            MaterialKind.AlphaCutout | MaterialKind.AnimatedAlphaCutout,
            [SemanticInput.CameraMatrices, SemanticInput.ShadowCasterTransforms]),
        Variant("terrain-shadow-caster-multiview", PipelineVariantRole.TerrainMultiviewDirectionalShadowCaster,
            PipelineBaseRole.Terrain,
            "sunshade_land_mv.vert.spv", "sunshade_land_mv.frag.spv",
            MaterialKind.Opaque,
            [SemanticInput.CameraMatrices]),
        Variant("world-shadow-opaque-multiview", PipelineVariantRole.WorldOpaqueMultiviewDirectionalShadowCaster,
            PipelineBaseRole.WorldMesh,
            "sunshade_props_solid_mv.vert.spv", "sunshade_props_solid_mv.frag.spv",
            MaterialKind.Opaque | MaterialKind.AnimatedOpaque,
            [SemanticInput.CameraMatrices, SemanticInput.ShadowCasterTransforms]),
        Variant("world-shadow-cutout-multiview", PipelineVariantRole.WorldAlphaCutoutMultiviewDirectionalShadowCaster,
            PipelineBaseRole.WorldMesh,
            "sunshade_props_cutout_mv.vert.spv", "sunshade_props_cutout_mv.frag.spv",
            MaterialKind.AlphaCutout | MaterialKind.AnimatedAlphaCutout,
            [SemanticInput.CameraMatrices, SemanticInput.ShadowCasterTransforms]),
        Variant("terrain-shadow-receiver", PipelineVariantRole.TerrainDirectionalShadowReceiver,
            PipelineBaseRole.Terrain,
            "land_haze.vert.spv", "land_haze.frag.spv",
            MaterialKind.Opaque,
            [SemanticInput.DirectionalShadowMaps,
                SemanticInput.SelectedCelestialDirectionalLight]),
        Variant("world-shadow-receiver", PipelineVariantRole.WorldDirectionalShadowReceiver,
            PipelineBaseRole.WorldMesh,
            "props_haze.vert.spv", "props_haze.frag.spv",
            MaterialKind.Opaque | MaterialKind.AlphaCutout
                | MaterialKind.AnimatedOpaque | MaterialKind.AnimatedAlphaCutout,
            [SemanticInput.DirectionalShadowMaps,
                SemanticInput.SelectedCelestialDirectionalLight]),
    ];
    }

    private static IReadOnlyList<QualityLadderStep> FidelityPresets()
    {
        return [
        Preset("low", "Low", RenderQualityRole.Low,
            64, 2.0, 3.0, 0.15, 0.50, 768, 2, 72, 0.25) with
            {
                ExecutionHints =
                    QualityExecutionHints.MultiviewDirectionalShadowCascades,
            },
        Preset("medium", "Medium", RenderQualityRole.Medium,
            128, 3.25, 4.50, 0.25, 0.75, 1536, 3, 144, 0.5),
        Preset("high", "High", RenderQualityRole.High,
            256, 4.50, 6.00, 0.35, 1.00, 2048, 4, 240, 0.5),
        Preset("auto", "Auto", RenderQualityRole.Automatic,
            128, 3.25, 4.50, 0.25, 0.75, 1536, 3, 144, 0.5)
            with
            {
                SettingOverrides =
                [
                    new QualitySettingTweak("automatic-quality", "true"),
                    new QualitySettingTweak("volumetric-strength", "0.35"),
                    new QualitySettingTweak("volumetric-ray-steps", "40"),
                    new QualitySettingTweak("sun-shadow-strength", "0.72"),
                    new QualitySettingTweak("sun-shadow-reach-metres", "144"),
                    new QualitySettingTweak("sun-shadow-pcf-taps", "9"),
                    new QualitySettingTweak("sun-ray-strength", "0.55"),
                ],
                AutoEligible = false,
            },
    ];
    }

    private static IReadOnlyList<RenderSettingSpec> Prefs()
    {
        return [
        Float("bloom-strength", "Bloom strength", RenderSettingRole.BloomStrength,
            0.65, 0, 2, 0.05),
        Float("filmic-strength", "Filmic tonemap strength", RenderSettingRole.FilmicStrength,
            1.0, 0, 1, 0.05),
        Float("exposure", "Exposure", RenderSettingRole.Exposure,
            0.80, 0.25, 4, 0.05),
        Float("grade-saturation", "Colour saturation", RenderSettingRole.GradeSaturation,
            1.0, 0, 2, 0.05),
        Float("grade-contrast", "Colour contrast", RenderSettingRole.GradeContrast,
            1.0, 0.5, 2, 0.05),
        Float("vignette-strength", "Vignette strength", RenderSettingRole.VignetteStrength,
            0.245, 0, 1, 0.005),
        Float("sun-ray-strength", "Sun-ray strength", RenderSettingRole.SunRayStrength,
            0.55, 0, 2, 0.05),
        Float("sun-shadow-strength", "Directional-shadow strength",
            RenderSettingRole.DirectionalShadowStrength, 0.72, 0, 1, 0.02),
        Integer("sun-shadow-reach-metres", "Directional-shadow reach (metres)",
            RenderSettingRole.DirectionalShadowReachMetres, 240, 16, 240, 1),
        Choice("sun-shadow-pcf-taps", "Directional-shadow filter taps",
            RenderSettingRole.DirectionalShadowPcfTaps, "9", ["1", "9", "25"]),
        Float("volumetric-strength", "Volumetric-shaft strength",
            RenderSettingRole.VolumetricStrength, 0.35, 0, 1, 0.01),
        Integer("volumetric-ray-steps", "Volumetric ray-march steps",
            RenderSettingRole.VolumetricRayMarchSteps, 40, 8, 64, 8),
        new RenderSettingSpec(
            "automatic-quality",
            "Automatic quality",
            SettingValueKind.Boolean,
            "false",
            null,
            null,
            null,
            [])
            with { Semantic = RenderSettingRole.AutomaticQuality },
        new RenderSettingSpec(
            "wind-enabled",
            "Foliage wind",
            SettingValueKind.Boolean,
            "true",
            null,
            null,
            null,
            [])
            with { Semantic = RenderSettingRole.WindEnabled },
        Float("wind-strength", "Foliage wind strength", RenderSettingRole.WindStrength,
            1.0, 0, 2, 0.05),
        Float("wind-direction-degrees", "Foliage wind direction (degrees)",
            RenderSettingRole.WindDirectionDegrees, 225, 0, 360, 5),
        Float("wind-lean-metres", "Foliage lean amplitude (metres)",
            RenderSettingRole.WindLeanMetres, 0.25, 0, 1, 0.01),
        Float("wind-branch-metres", "Foliage branch-swing amplitude (metres)",
            RenderSettingRole.WindBranchMetres, 0.15, 0, 1, 0.01),
        Float("wind-flutter-metres", "Foliage flutter amplitude (metres)",
            RenderSettingRole.WindFlutterMetres, 0.05, 0, 0.5, 0.005),
        Float("wind-canopy-height-metres", "Foliage canopy height (metres)",
            RenderSettingRole.WindCanopyHeightMetres, 8, 2, 30, 0.5),
    ];
    }

    private static AtmospherePolicySpec AtmosphereRule()
    {
        return new(
        [
            new SunElevationKnot(-90, 0),
            new SunElevationKnot(-3, 0),
            new SunElevationKnot(4, 1),
            new SunElevationKnot(22, 0.75),
            new SunElevationKnot(55, 0),
            new SunElevationKnot(90, 0),
        ],
        [
            new DayGroupWeight(0, 1.0),
            new DayGroupWeight(1, 0.35),
            new DayGroupWeight(2, 0.20),
        ])
        {
            DirectedShadeLampElevationResponse =
            [
                new SunElevationKnot(-90, 0),
                new SunElevationKnot(1, 0),
                new SunElevationKnot(12, 1),
                new SunElevationKnot(90, 1),
            ],
            VolumetricShaftSunElevationResponse =
            [
                new SunElevationKnot(-90, 0),
                new SunElevationKnot(0, 0),
                new SunElevationKnot(6, 1),
                new SunElevationKnot(18, 1),
                new SunElevationKnot(70, 0),
                new SunElevationKnot(90, 0),
            ],
            FoliageWindByWeather =
            [
                new WindWeatherKnot("Clear", 0.25, 0.15),
                new WindWeatherKnot("Overcast", 0.60, 0.35),
                new WindWeatherKnot("Rain", 0.85, 0.60),
                new WindWeatherKnot("Snow", 0.35, 0.20),
                new WindWeatherKnot("Storm", 1.00, 0.75),
            ],
        };
    }

    private static RenderResourceSpec Image(
        string ident,
        RenderResourceRole semantic,
        PixelFormatKind fmt,
        double widthScaling,
        double heightScaling,
        long estimatedOctets)
    {
        return new(
            ident,
            GpuResourceKind.Image2D,
            fmt,
            new RenderExtentSpec(
                ExtentRule.RelativeToMainWorld,
                widthScaling,
                heightScaling),
            SizeBytes: 0,
            GpuResourceUsage.Sampled | GpuResourceUsage.ColorAttachment,
            GpuResourceLifetime.ActivePack,
            estimatedOctets)
        { Semantic = semantic };
    }

    private static RenderPassSpec Pass(
        string ident,
        RenderPassRole semantic,
        RenderPassAnchor tap,
        string vert,
        string fragment,
        IReadOnlyList<SemanticInput> semantics,
        IReadOnlyList<string> reads,
        IReadOnlyList<string> writes)
    {
        return new(ident, tap, vert, fragment, semantics, reads, writes)
        {
            Semantic = semantic,
        };
    }

    private static PipelineVariantSpec Variant(
        string ident,
        PipelineVariantRole variantSemantic,
        PipelineBaseRole semantic,
        string vert,
        string fragment,
        MaterialKind matls,
        IReadOnlyList<SemanticInput> feeds)
    {
        return new(ident, semantic, vert, fragment, matls, feeds)
        {
            Semantic = variantSemantic,
        };
    }

    private static QualityLadderStep Preset(
        string ident,
        string readoutLabel,
        RenderQualityRole semantic,
        long upperMiB,
        double gpuP50,
        double gpuP99,
        double cpuP50,
        double cpuP99,
        int shadeResolution,
        int cascades,
        int shadeReachMetres,
        double postScaling)
    {
        return new(
            ident,
            readoutLabel,
            semantic == RenderQualityRole.Low
                ? [RenderFeature.DirectionalShadowMaps,
                    RenderFeature.MultiviewDirectionalShadowCascades]
                : [RenderFeature.DirectionalShadowMaps],
            [
                Override("directional-shadow-depth", shadeResolution, shadeResolution, cascades,
                    4L * shadeResolution * shadeResolution * cascades),
                RelativeOverride("bloom-a", postScaling),
                RelativeOverride("bloom-b", postScaling),
                RelativeOverride("sun-mask", ident == "low" ? 0.25 : 0.5),
                RelativeOverride("sun-rays", ident == "low" ? 0.25 : 0.5),
                RelativeOverride("volumetric", ident == "high" ? 0.5 : 0.25),
            ],
            [
                new QualitySettingTweak("automatic-quality", "false"),
                new QualitySettingTweak("volumetric-strength", ident == "low" ? "0" : "0.35"),
                new QualitySettingTweak(
                    "volumetric-ray-steps",
                    semantic switch
                    {
                        RenderQualityRole.Low => "24",
                        RenderQualityRole.High => "56",
                        _ => "40",
                    }),
                new QualitySettingTweak("sun-shadow-strength", "0.72"),
                new QualitySettingTweak("sun-shadow-reach-metres", shadeReachMetres.ToString()),
                new QualitySettingTweak(
                    "sun-shadow-pcf-taps",
                    semantic switch
                    {
                        RenderQualityRole.Low => "1",
                        RenderQualityRole.High => "25",
                        _ => "9",
                    }),
                new QualitySettingTweak("sun-ray-strength", ident == "low" ? "0.4" : "0.55"),
                new QualitySettingTweak("wind-flutter-metres", ident == "low" ? "0" : "0.05"),
            ],
            upperMiB * 1024 * 1024,
            gpuP50,
            gpuP99,
            cpuP50,
            cpuP99)
        { Semantic = semantic };
    }

    private static QualityResourceTweak Override(
        string ident,
        int width,
        int height,
        int strata,
        long octets)
    {
        return new(
            ident,
            new RenderExtentSpec(ExtentRule.AbsolutePixels, width, height, strata),
            SizeBytes: 0,
            EstimatedResidentBytes: octets);
    }

    private static QualityResourceTweak RelativeOverride(string ident, double scaling)
    {
        return new(
            ident,
            new RenderExtentSpec(ExtentRule.RelativeToMainWorld, scaling, scaling),
            SizeBytes: 0,
            EstimatedResidentBytes: 0);
    }

    private static RenderSettingSpec Float(
        string ident,
        string readoutLabel,
        RenderSettingRole semantic,
        double defaultVal,
        double lower,
        double upper,
        double hop)
    {
        return new(
            ident,
            readoutLabel,
            SettingValueKind.Float,
            defaultVal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            lower,
            upper,
            hop,
            [])
        { Semantic = semantic };
    }

    private static RenderSettingSpec Integer(
        string ident,
        string readoutLabel,
        RenderSettingRole semantic,
        int defaultVal,
        int lower,
        int upper,
        int hop)
    {
        return new(
            ident,
            readoutLabel,
            SettingValueKind.Integer,
            defaultVal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            lower,
            upper,
            hop,
            [])
        { Semantic = semantic };
    }

    private static RenderSettingSpec Choice(
        string ident,
        string readoutLabel,
        RenderSettingRole semantic,
        string defaultVal,
        IReadOnlyList<string> choices)
    {
        return new(
            ident,
            readoutLabel,
            SettingValueKind.Choice,
            defaultVal,
            null,
            null,
            null,
            choices)
        { Semantic = semantic };
    }
}
