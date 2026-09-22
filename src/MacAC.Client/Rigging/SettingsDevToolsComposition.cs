using MacAC.Client.Extensions;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Packs;
using MacAC.Client.Preferences;
using MacAC.Cockpit.Panels.Settings;
using Silk.NET.Input;

namespace MacAC.Client.Rigging;

internal sealed record PreferencesDevToolsResult(
    MacAC.Cockpit.Settings.QualityKnobs ResolvedQuality)
{
    internal RenderPackRegistrySource? RenderPacks { get; init; }

    internal RenderPackPick RasterizeBundlePick { get; init; } =
        RenderPackPick.Retail;
}

internal sealed record PreferencesDevToolsDependencies(
    EnginePreferencesDriver Settings,
    IEnginePreferencesStartupTarget StartupTarget)
{
    internal BufferedRasterizeBundleRegistry? RenderPacks { get; init; }

    internal IClientGpuDevice? GpuDev { get; init; }
}

internal sealed class PreferencesDevToolsAssemblyPhase(PreferencesDevToolsDependencies dependencies) :
    IPreferencesDevToolsAssemblyPhase<
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext>,
        HubFeedCameraOutcome,
        SubstanceFxListSoundOutcome,
        PreferencesDevToolsResult>
{
    private readonly PreferencesDevToolsDependencies _deps = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public PreferencesDevToolsResult Compose(
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform,
        HubFeedCameraOutcome hub,
        SubstanceFxListSoundOutcome substance)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(substance);

        _deps.Settings.ImposeStartup(_deps.StartupTarget);
        RenderPackRegistrySource? rasterizeBundles = null;
        if (_deps.RenderPacks is { } registry
            && _deps.GpuDev is { } gpu)
        {
            rasterizeBundles = new RenderPackRegistrySource(
                registry,
                RenderPackCapabilityPicker.Resolve(gpu.Capabilities));
        }

        return new PreferencesDevToolsResult(_deps.Settings.SettledFidelity)
        {
            RenderPacks = rasterizeBundles,
            RasterizeBundlePick = _deps.Settings.Readout.RenderPack,
        };
    }
}
