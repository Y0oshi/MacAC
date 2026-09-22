using MacAC.Client.Extensions;

namespace MacAC.Client.Graphics.Packs;

internal sealed class RenderPackRegistrySource
{
    private readonly BufferedRasterizeBundleRegistry _registry;
    private readonly RasterizeBundleHubCapabilities _capabilities;

    internal RenderPackRegistrySource(
        BufferedRasterizeBundleRegistry registry,
        RasterizeBundleHubCapabilities capabilities)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _capabilities = capabilities
            ?? throw new ArgumentNullException(nameof(capabilities));
    }

    internal long Rev => _registry.Rev;

    internal event Action<long> Changed
    {
        add => _registry.Changed += value;
        remove => _registry.Changed -= value;
    }

    internal RasterizeBundleRegistry Freeze()
    {
        return RasterizeBundleRegistry.Build(
        _registry.Freeze(),
        _capabilities);
    }
}
