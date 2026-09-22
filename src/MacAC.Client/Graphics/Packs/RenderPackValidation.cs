using MacAC.Extensibility.RenderPacks;
using MacAC.Extensibility.RenderPacks.Spirv;

namespace MacAC.Client.Graphics.Packs;

internal sealed record RasterizeBundleHubCapabilities(
    IReadOnlySet<RenderFeature> Available,
    int MaxImageDimension2D,
    int MaxImageArrayLayers,
    long MaxPackResidentBytes,
    long MaxPackTransientBytes = 512L * 1024 * 1024,
    string MemoryPolicyDescription = "API-v1 conformance ceiling")
{
    internal static RasterizeBundleHubCapabilities Conformance { get; } = new(
        Enum.GetValues<RenderFeature>().ToHashSet(),
        MaxImageDimension2D: 16_384,
        MaxImageArrayLayers: 256,
        MaxPackResidentBytes: 256L * 1024 * 1024,
        MaxPackTransientBytes: 512L * 1024 * 1024);
}

internal readonly record struct RenderPackAuditResult(
    bool Success,
    string? Reason)
{
    internal static RenderPackAuditResult Valid() => new(true, null);

    internal static RenderPackAuditResult Invalid(string cause) =>
        new(false, cause);
}

internal static partial class RenderPackValidator
{
    private const long AbsoluteBundleByteCeiling = 256L * 1024 * 1024;

    private const int AbsoluteImageDimension2DCeiling = 16_384;

    private const int AbsoluteImageArrStratumCeiling = 256;

    private const int CeilingShaderOctets = ShaderAbi.CeilingShaderAssetOctets;

    private const uint SpirvMagic = 0x0723_0203u;

    private sealed record ClientShaderValidationRequest(
        string Key,
        ShaderStage Stage,
        RenderPassSpec? Pass,
        PipelineVariantSpec? Variant);

    private static RenderPackAuditResult UniqueNonCustomSemantics<T, TSemantic>(
        RenderPackCard descriptor,
        IEnumerable<T?> vals,
        Func<T, TSemantic> pick,
        TSemantic custom,
        string sort)
        where T : class
        where TSemantic : struct, Enum
    {
        HashSet<TSemantic> observed = new HashSet<TSemantic>();
        foreach (T? val in vals)
        {
            if (val is null)
                continue;
            TSemantic semantic = pick(val);
            if (!Enum.IsDefined(semantic))
                return Invalid($"Pack '{descriptor.Id}' declares an unknown {sort} semantic.");
            if (!EqualityComparer<TSemantic>.Default.Equals(semantic, custom)
                && !observed.Add(semantic))
            {
                return Invalid(
                    $"Pack '{descriptor.Id}' declares duplicate {sort} semantic '{semantic}'.");
            }
        }
        return RenderPackAuditResult.Valid();
    }

    private static string? LeadNullRoster(RenderPackCard val)
    {
        if (val.RequiredCapabilities is null) return "required-capability";
        if (val.OptionalCapabilities is null) return "optional-capability";
        if (val.Resources is null) return "resource";
        if (val.Passes is null) return "pass";
        if (val.SceneReplays is null) return "scene-replay";
        if (val.PipelineVariants is null) return "pipeline-variant";
        if (val.QualityPresets is null) return "quality-preset";
        return val.Settings is null ? "setting" : null;
    }

    private static RenderPackAuditResult Invalid(string cause) =>
        RenderPackAuditResult.Invalid(cause);

    private static bool IsStableIdent(string? val)
    {
        if (string.IsNullOrWhiteSpace(val) || val.Length > 128)
            return false;
        if (val[0] is < 'a' or > 'z')
            return false;
        foreach (char toon in val)
        {
            if (toon is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '.' or '-' or '_')
                continue;
            return false;
        }
        return true;
    }

    private static bool IsSafeAssetTag(string? val)
    {
        if (string.IsNullOrWhiteSpace(val) || val.Length > 512)
            return false;
        if (Path.IsPathRooted(val) || val.Contains('\\'))
            return false;
        string[] segments = val.Split('/');
        return segments.All(static segment =>
            segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsFinitePositive(double val) =>
        double.IsFinite(val) && val > 0;

    private static bool IsFiniteNonNegative(double val) =>
        double.IsFinite(val) && val >= 0;

    private static bool TryAppend(ref long sum, long val)
    {
        if (val > long.MaxValue - sum)
            return false;
        sum += val;
        return true;
    }
}
