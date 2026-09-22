using MacAC.Client.Extensions;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Packs;
using MacAC.Client.Machine;
using MacAC.Host;
using MacAC.Mechanics.PluginHosting;
using Serilog;

namespace MacAC.Client;

internal sealed class ClientRig : IDisposable
{
    private readonly Stack<IDisposable> _possessed = new();

    internal ClientRig(EngineKnobs knobs, GraphicalHubPlatformServices machine)
    {
        UserStateLayout trails = machine.Paths;
        var realmPhase = new RealmPlayPhase();
        var realmSignals = new RealmSignals();
        var widgets = new BufferedWidgetRegistry();

        var rasterizeBundles = Own(new BufferedRasterizeBundleRegistry());
        Own(rasterizeBundles.Register(
            BuiltInAtmosphericRasterizeBundle.Descriptor,
            BuiltInAtmosphericRasterizeBundle.BuildHoldings(
                Path.Combine(AppContext.BaseDirectory, "Graphics", "Shaders", "spv"))));

        var automation = Own(new AppAutopilotSurface(
            realmSignals,
            new OwnExtensionCounterpartRegistry(Path.Combine(trails.Data, "plugin-peers")),
            knobs.ExtensionTags));

        var pane = Own(new PlayPane(
            knobs,
            realmPhase,
            realmSignals,
            widgets,
            machine,
            automation,
            rasterizeBundles));
        Window = pane;

        Host = new AppExtensionHub(
            new SerilogBridge(Log.Logger),
            realmPhase,
            realmSignals,
            pane.Selection,
            widgets,
            automation,
            new FilePluginVault(Path.Combine(trails.Config, "plugins")),
            automation.ExtensionDirectives,
            new PluginLootRuleRegistry(),
            new FilePluginVault(knobs.VtankProfileFolderOverride ?? ClientVtankProfilesDefault.Resolve(trails.Data)));
        RenderPacks = rasterizeBundles;
        Trails = trails;
        Knobs = knobs;
    }

    internal PlayPane Window { get; }
    internal AppExtensionHub Host { get; }
    internal BufferedRasterizeBundleRegistry RenderPacks { get; }
    internal UserStateLayout Trails { get; }
    internal EngineKnobs Knobs { get; }
    private T Own<T>(T disposable) where T : IDisposable
    {
        _possessed.Push(disposable);
        return disposable;
    }

    public void Dispose()
    {
        while (_possessed.Count > 0)
            _possessed.Pop().Dispose();
    }
}
