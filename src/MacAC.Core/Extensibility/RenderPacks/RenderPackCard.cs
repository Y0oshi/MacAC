namespace MacAC.Extensibility.RenderPacks;

/// <summary>Everything a pack declares about itself, fixed for its lifetime.</summary>
public sealed record RenderPackCard(
    string Id,
    string DisplayName,
    Version PackVersion,
    int PackApiVersion,
    PackTier HighestTier,
    IReadOnlyList<RenderFeature> RequiredCapabilities,
    IReadOnlyList<RenderFeature> OptionalCapabilities,
    IReadOnlyList<RenderResourceSpec> Resources,
    IReadOnlyList<RenderPassSpec> Passes,
    IReadOnlyList<SceneReplaySpec> SceneReplays,
    IReadOnlyList<PipelineVariantSpec> PipelineVariants,
    IReadOnlyList<QualityLadderStep> QualityPresets,
    IReadOnlyList<RenderSettingSpec> Settings,
    AtmospherePolicySpec? AtmospherePolicy)
{
    public string FeatureSummary { get; init; } = string.Empty;
}
