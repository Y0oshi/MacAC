using System.Security;
using MacAC.Extensibility.Hosting;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Mechanics.PluginHosting;

public enum PluginRosterStatusKind
{
    Loaded,
    Failed,
}

public readonly record struct PluginRosterStatus(
    string Plugin,
    PluginRosterStatusKind Kind,
    string? Error = null);

public sealed class PluginHostFlavorException(string msg) : Exception(msg);

public sealed class PluginRoster : IDisposable
{
    private sealed record Active(MountedPlugin Loaded, PluginHostScope Scope, RenderPackShelfScope? RenderScope);

    // What discovery produced per plugin id, plus the ids in the order they were first seen
    private sealed class Discovery
    {
        public readonly Dictionary<string, List<PluginScoutHit>> Candidates = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<Exception>> Problems = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Order = [];

        public void Note(string ident)
        {
            if (!Order.Contains(ident, StringComparer.OrdinalIgnoreCase))
                Order.Add(ident);
        }

        public void Fail(string ident, Exception problem)
        {
            if (!Problems.TryGetValue(ident, out List<Exception>? roster))
                Problems.Add(ident, roster = []);
            roster.Add(problem);
        }

        public void Offer(string ident, PluginScoutHit strike)
        {
            if (!Candidates.TryGetValue(ident, out List<PluginScoutHit>? roster))
                Candidates.Add(ident, roster = []);
            roster.Add(strike);
        }
    }

    private readonly IExtensionHost _hub;
    private readonly Action<PluginRosterStatus>? _dossier;
    private readonly IRenderPackShelf? _rasterizeBundles;
    private readonly HashSet<PluginFlavor> _supported;
    private readonly List<Active> _engaged = [];
    private readonly List<WeakReference> _unloadedContexts = [];
    private bool _begun;
    private bool _destroyed;

    public PluginRoster(
        IExtensionHost host,
        Action<PluginRosterStatus>? dossier = null,
        IRenderPackShelf? renderPacks = null,
        IEnumerable<PluginFlavor>? supportedKinds = null)
    {
        _hub = host ?? throw new ArgumentNullException(nameof(host));
        _dossier = dossier;
        _rasterizeBundles = renderPacks;
        _supported = new HashSet<PluginFlavor>(
            supportedKinds ?? (renderPacks is null ? [PluginFlavor.Gameplay] : [PluginFlavor.Gameplay, PluginFlavor.RenderPack]));
        if (_supported.Count is 0)
            throw new ArgumentException("At least one supported plugin kind is needed", nameof(supportedKinds));
        if (_supported.Contains(PluginFlavor.RenderPack) && renderPacks is null)
        {
            throw new ArgumentException(
                "A host that supports render-pack plugins must supply a render-pack registry",
                nameof(renderPacks));
        }
    }

    public int LoadedTally => _engaged.Count;

    public IReadOnlyList<string> FetchedExtensionIdents => _engaged.Select(static active => active.Loaded.Manifest.Id).ToArray();

    public void Start(IEnumerable<string> extensionTrunks, IReadOnlyList<string>? allowRoster)
    {
        ArgumentNullException.ThrowIfNull(extensionTrunks);
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (_begun)
            throw new InvalidOperationException("The plugin session has by now started");
        _begun = true;

        string[]? wanted = allowRoster?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (wanted is { Length: 0 })
            return;
        HashSet<string>? wantedSet = wanted is null ? null : new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);

        Discovery located = new Discovery();
        foreach (string trunk in DistinctTrunks(extensionTrunks))
            Sweep(trunk, wantedSet, located);

        foreach (string ident in wanted ?? (IEnumerable<string>)located.Order)
            Mount(ident, located);
    }

    public IReadOnlyList<WeakReference> GrabPullCtxWeakReferences()
    {
        return [.. _unloadedContexts, .. _engaged.Select(static active => new WeakReference(active.Loaded.LoadContext!))];
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        for (int idx = _engaged.Count - 1; idx >= 0; --idx)
        {
            (MountedPlugin fetched, PluginHostScope ambit, RenderPackShelfScope? rasterizeAmbit) = _engaged[idx];
            DeactivateQuietly(fetched, "plugin disable failed");

            // Host-side registrations go before the assembly unloads, so no
            // UI binding or event delegate can keep the plugin reachable.
            ambit.Dispose();
            FreeRasterizeAmbit(rasterizeAmbit, fetched.Manifest.Id);
            try
            {
                fetched.LoadContext!.Unload();
            }
            catch (Exception problem)
            {
                TraceProblem($"plugin unload failed: {fetched.Manifest.Id}", problem);
            }
        }
        _engaged.Clear();
    }

    private void Sweep(string trunk, HashSet<string>? wanted, Discovery located)
    {
        IReadOnlyList<PluginScoutHit> strikes;
        try
        {
            strikes = PluginScout.Scan(trunk);
        }
        catch (Exception problem) when (IsDiscoveryMiss(problem))
        {
            TraceProblem($"plugin discovery failed for root '{trunk}'", problem);
            return;
        }

        foreach (PluginScoutHit strike in strikes)
        {
            if (!strike.Success)
            {
                string folderIdent = Path.GetFileName(Path.TrimEndingDirectorySeparator(strike.PluginDirectory));
                if (string.IsNullOrWhiteSpace(folderIdent) || (wanted is not null && !wanted.Contains(folderIdent)))
                    continue;
                located.Note(folderIdent);
                located.Fail(folderIdent, strike.Error ?? new InvalidOperationException("plugin discovery failed"));
                continue;
            }

            PluginCard card = strike.Manifest!;
            string ident = card.Id;
            if (wanted is not null && !wanted.Contains(ident))
                continue;

            if (!card.Kinds.Any(_supported.Contains))
            {
                // Only an explicitly requested plugin is worth reporting as unsupported.
                if (wanted is not null)
                {
                    located.Note(ident);
                    located.Fail(ident, new PluginHostFlavorException(
                        $"plugin '{ident}' declares only {string.Join(", ", card.Kinds)} entry points, "
                        + "which this host doesn't support"));
                }
                continue;
            }

            located.Note(ident);
            located.Offer(ident, strike);
        }
    }

    // Tries each candidate directory for an id until one loads and enables
    private void Mount(string ident, Discovery located)
    {
        if (located.Candidates.TryGetValue(ident, out List<PluginScoutHit>? contenders))
        {
            foreach (PluginScoutHit contender in contenders)
            {
                PluginCard card = contender.Manifest!;
                PluginHostScope ambit = new PluginHostScope(_hub, card.Id, card.DisplayName);
                RenderPackShelfScope? rasterizeAmbit =
                    card.Declares(PluginFlavor.RenderPack) && _rasterizeBundles is not null ? new RenderPackShelfScope(_rasterizeBundles) : null;

                var fetched = PluginMounter.Load(contender.PluginDirectory, card, ambit, rasterizeAmbit);
                if (!fetched.Success)
                {
                    ambit.Dispose();
                    FreeRasterizeAmbit(rasterizeAmbit, card.Id);
                    Toss(fetched, "plugin cleanup after initialize failure failed", "plugin unload after load failure failed");
                    located.Fail(ident, fetched.Error ?? new InvalidOperationException("plugin load failed"));
                    continue;
                }

                try
                {
                    fetched.Plugin?.Enable();
                    _engaged.Add(new Active(fetched, ambit, rasterizeAmbit));
                    TraceDetails($"plugin loaded: {fetched.Manifest.Id} ({fetched.Manifest.DisplayName})");
                    Report(new PluginRosterStatus(fetched.Manifest.Id, PluginRosterStatusKind.Loaded));
                    return;
                }
                catch (Exception problem)
                {
                    located.Fail(ident, problem);
                    ambit.Dispose();
                    // Enable threw: undo everything, but only after Disable had its chance.
                    DeactivateQuietly(fetched, "plugin cleanup after enable failure failed");
                    FreeRasterizeAmbit(rasterizeAmbit, fetched.Manifest.Id);
                    Unload(fetched.LoadContext!, fetched.Manifest.Id, "plugin unload after enable failure failed");
                }
            }
        }

        if (!located.Problems.TryGetValue(ident, out List<Exception>? misses) || misses.Count is 0)
            misses = [new FileNotFoundException($"plugin '{ident}' wasn't found in the configured plugin roots")];

        string summary = string.Join(" | ", misses.Select(Depict));
        Report(new PluginRosterStatus(ident, PluginRosterStatusKind.Failed, summary));
        TraceWarn($"plugin failed: {ident}: {summary}");
    }

    private void DeactivateQuietly(MountedPlugin fetched, string missNote)
    {
        if (fetched.Plugin is null)
            return;
        try
        {
            fetched.Plugin.Disable();
        }
        catch (Exception problem)
        {
            TraceProblem($"{missNote}: {fetched.Manifest.Id}", problem);
        }
    }

    private void Toss(MountedPlugin fetched, string deactivateNote, string unloadNote)
    {
        DeactivateQuietly(fetched, deactivateNote);
        if (fetched.LoadContext is { } ctx)
            Unload(ctx, fetched.Manifest.Id, unloadNote);
    }

    private void Unload(System.Runtime.Loader.AssemblyLoadContext ctx, string extensionIdent, string missNote)
    {
        _unloadedContexts.Add(new WeakReference(ctx));
        try
        {
            ctx.Unload();
        }
        catch (Exception problem)
        {
            TraceProblem($"{missNote}: {extensionIdent}", problem);
        }
    }

    private void FreeRasterizeAmbit(RenderPackShelfScope? ambit, string extensionIdent)
    {
        if (ambit is null)
            return;
        try
        {
            ambit.Dispose();
        }
        catch (Exception problem)
        {
            TraceProblem($"render-pack registration cleanup failed: {extensionIdent}", problem);
        }
    }

    private void Report(PluginRosterStatus condition)
    {
        if (_dossier is null)
            return;
        try
        {
            _dossier(condition);
        }
        catch (Exception problem)
        {
            TraceProblem($"plugin status observer failed for {condition.Plugin}", problem);
        }
    }

    private void TraceDetails(string msg)
    {
        try { _hub.Trace.Info(msg); } catch { }
    }

    private void TraceWarn(string msg)
    {
        try { _hub.Trace.Warn(msg); } catch { }
    }

    private void TraceProblem(string msg, Exception problem)
    {
        try { _hub.Trace.Error(msg, problem); } catch { }
    }

    private static string[] DistinctTrunks(IEnumerable<string> trunks)
    {
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return trunks
            .Where(static trunk => !string.IsNullOrWhiteSpace(trunk))
            .Select(Path.GetFullPath)
            .Distinct(comparer)
            .ToArray();
    }

    private static string Depict(Exception problem)
    {
        Exception trunk = problem.GetBaseException();
        return string.IsNullOrWhiteSpace(trunk.Message) ? trunk.GetType().Name : trunk.Message;
    }

    private static bool IsDiscoveryMiss(Exception problem)
    {
        return problem is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;
    }
}
