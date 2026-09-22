using MacAC.Extensibility.Hosting;
using MacAC.Extensibility.RenderPacks;
using MacAC.Host;
using MacAC.Mechanics.PluginHosting;
using MacAC.Sim.Presence;

namespace MacAC.Client.Extensions;

internal sealed class GraphicalExtensionSession : IDisposable
{
    private readonly PluginRoster _extensions;
    private readonly string[] _trunks;
    private readonly IReadOnlyList<string>? _allowRoster;
    private readonly string _sessIdent;
    private readonly SessionStatusScribe _conditionWriter;
    private bool _begun;

    private GraphicalExtensionSession(
        PluginRoster extensions,
        string[] trunks,
        IReadOnlyList<string>? allowRoster,
        string sessIdent,
        SessionStatusScribe conditionWriter)
    {
        _extensions = extensions;
        _trunks = trunks;
        _allowRoster = allowRoster;
        _sessIdent = sessIdent;
        _conditionWriter = conditionWriter;
    }

    internal int FetchedTally => _extensions.LoadedTally;

    public void Dispose() => _extensions.Dispose();

    internal IReadOnlyList<WeakReference> GrabPullCtxWeakReferences() =>
        _extensions.GrabPullCtxWeakReferences();

    internal static GraphicalExtensionSession Create(
        UserStateLayout trails,
        IReadOnlyList<string>? allowRoster,
        string sessIdent,
        IExtensionHost hub,
        SessionStatusScribe conditionWriter,
        IRenderPackShelf? rasterizeBundles = null)
    {
        ArgumentNullException.ThrowIfNull(trails);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessIdent);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(conditionWriter);

        PluginRoster extensions = new PluginRoster(
            hub,
            condition => Report(conditionWriter, sessIdent, condition),
            rasterizeBundles,
            rasterizeBundles is null
                ? [PluginFlavor.Gameplay]
                : [PluginFlavor.Gameplay, PluginFlavor.RenderPack]);
        return new GraphicalExtensionSession(
            extensions,
            [
                Path.Combine(AppContext.BaseDirectory, "plugins"),
                trails.Plugins,
            ],
            allowRoster,
            sessIdent,
            conditionWriter);
    }

    internal void Start()
    {
        if (_begun)
            throw new InvalidOperationException(
                "The graphical plugin session has by now started");
        _begun = true;

        _conditionWriter.Started(_sessIdent);
        _extensions.Start(_trunks, _allowRoster);
    }

    private static void Report(
        SessionStatusScribe writer,
        string sessIdent,
        PluginRosterStatus condition)
    {
        if (condition.Kind == PluginRosterStatusKind.Loaded)
        {
            writer.ExtensionFetched(sessIdent, condition.Plugin);
            return;
        }

        writer.ExtensionFailed(
            sessIdent,
            condition.Plugin,
            condition.Error ?? "plugin failed");
    }
}
