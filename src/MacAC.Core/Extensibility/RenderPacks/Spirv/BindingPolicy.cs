namespace MacAC.Extensibility.RenderPacks.Spirv;

// Which descriptor slots a shader in a given role may touch
internal sealed record BindingPolicy(
    IReadOnlySet<uint> StorageBindings,
    IReadOnlySet<uint> UniformBindings,
    bool SampledTable,
    bool AtmosphericFrame,
    bool DirectionalShadow,
    bool PackPass,
    bool PackSettings)
{
    private static readonly IReadOnlySet<uint> NoSockets = new HashSet<uint>();

    internal static BindingPolicy ForPass(RenderPassSpec pass)
    {
        bool zDepthInvoker = pass.Semantic == RenderPassRole.DirectionalShadowDepth;
        return new BindingPolicy(
            StorageBindings: zDepthInvoker ? Slots(0, 1) : NoSockets,
            UniformBindings: NoSockets,
            SampledTable: pass.ResourceReads.Count is not 0 || SpecimensTableau(pass.SemanticInputs),
            AtmosphericFrame: zDepthInvoker || ReadsAtmosphericCycle(pass.SemanticInputs),
            DirectionalShadow: zDepthInvoker || ReadsShadeLamp(pass.SemanticInputs),
            PackPass: !zDepthInvoker,
            PackSettings: !zDepthInvoker);
    }

    internal static BindingPolicy ForVariant(PipelineVariantSpec variant)
    {
        return variant.Semantic switch
        {
            PipelineVariantRole.TerrainDirectionalShadowCaster
                or PipelineVariantRole.TerrainMultiviewDirectionalShadowCaster =>
                new(NoSockets, NoSockets, false, false, true, false, false),

            PipelineVariantRole.WorldOpaqueDirectionalShadowCaster
                or PipelineVariantRole.WorldOpaqueMultiviewDirectionalShadowCaster =>
                new(Slots(0, 1), NoSockets, false, true, true, false, false),

            PipelineVariantRole.WorldAlphaCutoutDirectionalShadowCaster
                or PipelineVariantRole.WorldAlphaCutoutMultiviewDirectionalShadowCaster =>
                new(Slots(0, 1), NoSockets, true, true, true, false, false),

            PipelineVariantRole.TerrainDirectionalShadowReceiver =>
                new(NoSockets, Slots(1, 2, 3), true, false, true, false, true),

            PipelineVariantRole.WorldDirectionalShadowReceiver =>
                new(Slots(0, 1, 2, 3, 4, 5, 6, 7, 8, 9), Slots(1), true, true, true, false, true),

            _ => new(
                NoSockets,
                NoSockets,
                SpecimensTableau(variant.SemanticInputs),
                ReadsAtmosphericCycle(variant.SemanticInputs),
                ReadsShadeLamp(variant.SemanticInputs),
                false,
                true),
        };
    }

    internal bool PermitsUniform(uint mapping)
    {
        return mapping switch
        {
            ShaderAbi.AtmosphericCycleMapping => AtmosphericFrame,
            ShaderAbi.DirectedShadeMapping => DirectionalShadow,
            ShaderAbi.BundlePassMapping => PackPass,
            ShaderAbi.BundlePrefsMapping => PackSettings,
            _ => false,
        };
    }

    private static IReadOnlySet<uint> Slots(params uint[] mappings) => new HashSet<uint>(mappings);

    private static bool SpecimensTableau(IReadOnlyList<SemanticInput> feeds)
    {
        return feeds.Any(static feed => feed is
            SemanticInput.WorldColor
            or SemanticInput.SceneDepth
            or SemanticInput.SceneNormals
            or SemanticInput.DirectionalShadowMaps);
    }

    private static bool ReadsAtmosphericCycle(IReadOnlyList<SemanticInput> feeds)
    {
        return feeds.Any(static feed => feed is
            SemanticInput.SunDirection
            or SemanticInput.SunScreenPosition
            or SemanticInput.ActiveDayGroup
            or SemanticInput.Weather
            or SemanticInput.CameraMatrices
            or SemanticInput.FrameTime);
    }

    private static bool ReadsShadeLamp(IReadOnlyList<SemanticInput> feeds)
    {
        return feeds.Contains(SemanticInput.DirectionalShadowMaps)
        || feeds.Contains(SemanticInput.SelectedCelestialDirectionalLight);
    }
}
