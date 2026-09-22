using MacAC.Extensibility.Automation;
using MacAC.Extensibility.Hosting;
using MacAC.Extensibility.Loot;
using MacAC.Extensibility.Panels;
using MacAC.Extensibility.World;

namespace MacAC.Mechanics.PluginHosting;

// The host as one plugin sees it
internal sealed class PluginHostScope : IExtensionHost, IDisposable
{
    private readonly IExtensionHost _hub;
    private readonly string _extensionIdent;
    private readonly PulseScope _pulse;
    private readonly TargetPickerScope _picker;
    private readonly PanelScope _boards;
    private readonly VaultScope _vault;
    private readonly SlashScope _slash;
    private readonly LootRuleScope _lootRules;
    private bool _destroyed;

    internal PluginHostScope(IExtensionHost inner, string extensionIdent, string extensionReadoutLabel)
    {
        _hub = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionIdent);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionReadoutLabel);
        _extensionIdent = extensionIdent;
        _pulse = new PulseScope(inner.Signals);
        _picker = new TargetPickerScope(inner.Selection);
        _boards = new PanelScope(inner.Widget, new PanelOwner(extensionIdent, extensionReadoutLabel));
        _vault = new VaultScope(inner.Depot, extensionIdent);
        _slash = new SlashScope(inner.Commands);
        _lootRules = new LootRuleScope(inner.LootClassifiers, extensionIdent, extensionReadoutLabel);
    }

    public bool HasWidget => _hub.HasWidget;

    public IExtensionLog Trace => _hub.Trace;

    public IWorldLedger State => _hub.State;

    public IWorldPulse Signals => _pulse;

    public ITargetPicker Selection => _picker;

    public IPanelRegistry Widget => _boards;

    public IExtensionVault Depot => _vault;

    public IExtensionVault VtankProfiles => _hub.VtankProfiles;

    public ISlashCommandRegistry Commands => _slash;

    public ILootRuleRegistry LootClassifiers => _lootRules;

    public IAutomationCockpit Automation => _hub.Automation;

    public IReadOnlyDictionary<string, string> SessPrefs
    {
        get
        {
            return _hub is IExtensionSessionSettings perExtension ? perExtension.SessPrefsFor(_extensionIdent) : _hub.SessPrefs;
        }
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _pulse.Dispose();
        _picker.Dispose();
        _boards.Dispose();
        _slash.Dispose();
        _lootRules.Dispose();
    }

    // Prefixes every key with the plugin id and forbids escaping that folder
    private sealed class VaultScope(IExtensionVault interior, string extensionIdent) : IExtensionVault
    {
        public bool IsAvailable => interior.IsAvailable;

        public string? ScanPhrase(string tag) => interior.ScanPhrase(Scoped(tag));

        public IReadOnlyList<string> List(string stem)
        {
            string holderStem = extensionIdent + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            List<string> tags = new List<string>();
            foreach (string raw in interior.List(Scoped(stem)))
            {
                string tag = raw.Replace('/', Path.DirectorySeparatorChar);
                if (tag.StartsWith(holderStem, comparison))
                    tags.Add(tag[holderStem.Length..].Replace(Path.DirectorySeparatorChar, '/'));
            }
            return tags.ToArray();
        }

        public void EmitPhrase(string tag, string substance) => interior.EmitPhrase(Scoped(tag), substance);

        public bool Delete(string tag) => interior.Delete(Scoped(tag));

        private string Scoped(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (Path.IsPathRooted(key) || key.Contains("..", StringComparison.Ordinal) || key.Contains('\\'))
                throw new ArgumentException("Not valid plugin storage key", nameof(key));
            return Path.Combine(extensionIdent, key.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    private sealed class LootRuleScope(ILootRuleRegistry interior, string extensionIdent, string extensionReadoutLabel)
        : ILootRuleRegistry, IDisposable
    {
        private readonly LeaseLedger<IDisposable> _tenancies = new(nameof(LootRuleScope));

        public IReadOnlyList<LootRuleFacts> Available => interior.Available;

        public IDisposable Register(string classifierId, string readoutLabel, ILootRule classifier)
        {
            _tenancies.HurlIfClosed();
            ArgumentException.ThrowIfNullOrWhiteSpace(classifierId);
            string own = classifierId.Trim();
            if (own.Contains('/') || own.Contains('\\'))
                throw new ArgumentException("A classifier id can't contain a path separator", nameof(classifierId));

            string shown = string.IsNullOrWhiteSpace(readoutLabel) ? extensionReadoutLabel : readoutLabel.Trim();
            IDisposable tenancy = interior.Register($"{extensionIdent}/{own}", shown, classifier);
            _tenancies.Keep(tenancy, static disposable => disposable.Dispose());
            return tenancy;
        }

        public bool TryClassify(string classifierIdent, in LootJudgementContext ctx, out LootJudgement taxonomy) =>
            interior.TryClassify(classifierIdent, ctx, out taxonomy);

        public bool TryAlertLooted(string classifierIdent, in LootedEntry gear) => interior.TryAlertLooted(classifierIdent, gear);

        public bool TryAlertGearRemoved(string classifierIdent, uint objectIdent) => interior.TryAlertGearRemoved(classifierIdent, objectIdent);

        public void Dispose() => _tenancies.Close(static disposable => disposable.Dispose());
    }

    private sealed class SlashScope(ISlashCommandRegistry interior) : ISlashCommandRegistry, IDisposable
    {
        private readonly LeaseLedger<IDisposable> _tenancies = new(nameof(SlashScope));

        public IDisposable Register(string verb, Action<SlashCommand> handler)
        {
            _tenancies.HurlIfClosed();
            IDisposable tenancy = interior.Register(verb, handler);
            _tenancies.Keep(tenancy, static disposable => disposable.Dispose());
            return tenancy;
        }

        public void Dispose() => _tenancies.Close(static disposable => disposable.Dispose());
    }

    private sealed class TargetPickerScope(ITargetPicker interior) : ITargetPicker, IDisposable
    {
        private readonly LeaseLedger<Action<TargetChange>> _handlers = new(nameof(TargetPickerScope));
        private readonly Lock _synchronize = new();

        public uint? ChosenObjectTag => interior.ChosenObjectTag;

        public uint? EarlierObjectIdent => interior.EarlierObjectIdent;

        public event Action<TargetChange> Changed
        {
            add => Enlist(_handlers, value, h => interior.Changed += h, h => interior.Changed -= h);
            remove
            {
                if (value is null)
                    return;
                interior.Changed -= value;
                _handlers.DropPrevious(value);
            }
        }

        public bool Select(uint objectIdent)
        {
            lock (_synchronize)
            {
                _handlers.HurlIfClosed();
                return interior.Select(objectIdent);
            }
        }

        public bool Clear()
        {
            lock (_synchronize)
            {
                _handlers.HurlIfClosed();
                return interior.Clear();
            }
        }

        public void Dispose()
        {
            _handlers.Close(h => LeaseLedger<Action<TargetChange>>.FreeQuietly(() => interior.Changed -= h));
        }
    }

    private sealed class PulseScope(IWorldPulse interior) : IWorldPulse, IDisposable
    {
        private readonly LeaseLedger<Action<EntityFrame>> _summonHandlers = new(nameof(PulseScope));
        private readonly LeaseLedger<Action<double>> _beatHandlers = new(nameof(PulseScope));

        public event Action<double> Tick
        {
            add => Enlist(_beatHandlers, value, h => interior.Tick += h, h => interior.Tick -= h);
            remove
            {
                if (value is null)
                    return;
                interior.Tick -= value;
                _beatHandlers.DropPrevious(value);
            }
        }

        public event Action<EntityFrame> EntitySpawned
        {
            add => Enlist(_summonHandlers, value, h => interior.EntitySpawned += h, h => interior.EntitySpawned -= h);
            remove
            {
                if (value is null)
                    return;
                interior.EntitySpawned -= value;
                _summonHandlers.DropPrevious(value);
            }
        }

        public void Dispose()
        {
            // Both ledgers close before either releases, so a late subscriber
            // on either event is refused once teardown has begun.
            var spawns = _summonHandlers.Close();
            var beats = _beatHandlers.Close();
            LeaseLedger<Action<EntityFrame>>.FreeAll(spawns, h => LeaseLedger<Action<EntityFrame>>.FreeQuietly(() => interior.EntitySpawned -= h));
            LeaseLedger<Action<double>>.FreeAll(beats, h => LeaseLedger<Action<double>>.FreeQuietly(() => interior.Tick -= h));
        }
    }

    // Attaches a handler to the host event, then records it
    private static void Enlist<T>(LeaseLedger<T> register, T handler, Action<T> fasten, Action<T> unfasten) where T : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        try
        {
            fasten(handler);
        }
        catch
        {
            LeaseLedger<T>.FreeQuietly(() => unfasten(handler));
            throw;
        }
        register.Keep(handler, h => LeaseLedger<T>.FreeQuietly(() => unfasten(h)));
    }

    private sealed class PanelScope : IPanelRegistry, IDisposable
    {
        private readonly IOwnedPanelRegistry _interior;
        private readonly PanelOwner _holder;
        private readonly LeaseLedger<IDisposable> _tenancies = new(nameof(PanelScope));

        internal PanelScope(IPanelRegistry interior, PanelOwner holder)
        {
            _interior = interior as IOwnedPanelRegistry
                ?? throw new InvalidOperationException(
                    "Plugin hosts must expose an IOwnedPanelRegistry so UI registrations can be rolled back");
            _holder = holder;
        }

        public void AppendMarkupBoard(string markupTrail, object mapping)
        {
            Keep(_interior.RegisterPanel(
                _holder,
                new PanelBlueprint(Path.GetFileNameWithoutExtension(markupTrail), _holder.DisplayName),
                markupTrail,
                mapping));
        }

        public void AppendBoard(PanelBlueprint descriptor, string markupTrail, object mapping)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            Keep(_interior.RegisterPanel(_holder, descriptor, markupTrail, mapping));
        }

        public IDisposable RegisterPanel(PanelBlueprint descriptor, string markupTrail, object mapping)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return Follow(_interior.RegisterPanel(_holder, descriptor, markupTrail, mapping));
        }

        public IDisposable RegisterPanelContent(PanelBlueprint descriptor, string markupSubstance, object mapping)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return Follow(_interior.RegisterPanelContent(_holder, descriptor, markupSubstance, mapping));
        }

        public bool ViewExists(string lensLabel) => _interior.ViewExists(_holder, lensLabel);

        public bool IsViewVisible(string lensLabel) => _interior.IsViewVisible(_holder, lensLabel);

        public bool ControlExists(string lensLabel, string controlLabel) => _interior.ControlExists(_holder, lensLabel, controlLabel);

        public bool SetControlLabel(string lensLabel, string controlLabel, string caption) =>
            _interior.SetControlLabel(_holder, lensLabel, controlLabel, caption);

        public bool SetControlVisible(string lensLabel, string controlLabel, bool shown) =>
            _interior.SetControlVisible(_holder, lensLabel, controlLabel, shown);

        public void Dispose()
        {
            _tenancies.Close(static disposable => LeaseLedger<IDisposable>.FreeQuietly(disposable.Dispose));
        }

        private void Keep(IDisposable tenancy) => _tenancies.Keep(tenancy, static disposable => disposable.Dispose());

        private IDisposable Follow(IDisposable tenancy)
        {
            Keep(tenancy);
            return new SingleLease(this, tenancy);
        }

        private void Release(IDisposable tenancy)
        {
            if (_tenancies.Forget(tenancy))
                tenancy.Dispose();
        }

        private sealed class SingleLease(PanelScope holder, IDisposable tenancy) : IDisposable
        {
            private PanelScope? _holder = holder;

            public void Dispose() => Interlocked.Exchange(ref _holder, null)?.Release(tenancy);
        }
    }
}
