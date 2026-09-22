using System.Collections.Immutable;
using MacAC.Client.Graphics.Gpu;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal static class RasterizeBundleShaderHoldings
{
    internal static ValidatedRasterizeBundleShaderHoldings Validate(
        RenderPackCard descriptor,
        IRenderPackFiles holdings)
    {
        var outcome = RenderPackValidator.VetChosenHoldings(
            descriptor,
            holdings,
            out ValidatedRasterizeBundleShaderHoldings? validated);
        return !outcome.Success ? throw new InvalidDataException(outcome.Reason) : validated!;
    }

    internal static GpuShaderGroup PullPass(
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        RenderPassSpec pass)
    {
        return new(
        $"{descriptor.Id}:{pass.Id}",
        holdings.Duplicate(pass.VertexShaderAsset),
        holdings.Duplicate(pass.FragmentShaderAsset));
    }

    internal static GpuShaderGroup PullVariant(
        RenderPackCard descriptor,
        ValidatedRasterizeBundleShaderHoldings holdings,
        PipelineVariantSpec variant)
    {
        return new(
        $"{descriptor.Id}:{variant.Id}",
        holdings.Duplicate(variant.VertexShaderAsset),
        holdings.Duplicate(variant.FragmentShaderAsset));
    }
}

internal sealed class ValidatedRasterizeBundleShaderHoldings
{
    private readonly IReadOnlyDictionary<string, ImmutableArray<byte>> _holdings;

    internal ValidatedRasterizeBundleShaderHoldings(
        IReadOnlyDictionary<string, byte[]> holdings)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        var possessed = new Dictionary<string, ImmutableArray<byte>>(
            holdings.Count,
            StringComparer.Ordinal);
        foreach ((string tag, byte[] octets) in holdings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
            ArgumentNullException.ThrowIfNull(octets);
            possessed.Add(tag, [.. octets]);
        }
        _holdings = possessed;
    }

    internal byte[] Duplicate(string tag)
    {
        return !_holdings.TryGetValue(tag, out ImmutableArray<byte> octets)
            ? throw new InvalidDataException($"Validated render-pack shader '{tag}' is absent")
            : [.. octets];
    }
}
