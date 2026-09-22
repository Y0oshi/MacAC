using MacAC.Extensibility.Loot;

namespace MacAC.Mechanics.PluginHosting;

/// <summary>Process-wide registry of loot classifiers supplied by plugins.</summary>
public sealed class PluginLootRuleRegistry : ILootRuleRegistry
{
    private sealed record MechSlot(LootRuleFacts Info, ILootRule Rule);

    private readonly Lock _synchronize = new();
    private readonly Dictionary<string, MechSlot> _sockets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LootRuleFacts> Available
    {
        get
        {
            lock (_synchronize)
            {
                return _sockets.Values
                    .Select(static slot => slot.Info)
                    .OrderBy(static idx => idx.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static idx => idx.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public IDisposable Register(string classifierIdent, string readoutLabel, ILootRule classifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classifierIdent);
        ArgumentException.ThrowIfNullOrWhiteSpace(readoutLabel);
        ArgumentNullException.ThrowIfNull(classifier);

        string ident = classifierIdent.Trim();
        MechSlot socket = new MechSlot(new LootRuleFacts(ident, readoutLabel.Trim()), classifier);
        lock (_synchronize)
        {
            if (!_sockets.TryAdd(ident, socket))
                throw new InvalidOperationException($"Loot classifier '{ident}' is by now registered");
        }
        return new Lease(this, ident, socket);
    }

    public bool TryClassify(string classifierIdent, in LootJudgementContext ctx, out LootJudgement taxonomy)
    {
        MechSlot? socket;
        lock (_synchronize)
            _sockets.TryGetValue(classifierIdent ?? string.Empty, out socket);
        if (socket is null)
        {
            taxonomy = default;
            return false;
        }
        try
        {
            taxonomy = socket.Rule.Classify(ctx);
            return true;
        }
        catch
        {
            taxonomy = default;
            return false;
        }
    }

    public bool TryAlertLooted(string classifierIdent, in LootedEntry gear)
    {
        if (Find(classifierIdent) is not { } rule)
            return false;
        try
        {
            rule.OnLooted(gear);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryAlertGearRemoved(string classifierIdent, uint objectIdent)
    {
        if (Find(classifierIdent) is not { } rule)
            return false;
        try
        {
            rule.OnGearRemoved(objectIdent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private ILootRule? Find(string classifierIdent)
    {
        if (string.IsNullOrWhiteSpace(classifierIdent))
            return null;
        lock (_synchronize)
            return _sockets.TryGetValue(classifierIdent.Trim(), out MechSlot? socket) ? socket.Rule : null;
    }

    private void Withdraw(string ident, MechSlot anticipated)
    {
        lock (_synchronize)
        {
            if (_sockets.TryGetValue(ident, out MechSlot? pinned) && ReferenceEquals(pinned, anticipated))
                _sockets.Remove(ident);
        }
    }

    private sealed class Lease(PluginLootRuleRegistry holder, string ident, MechSlot socket) : IDisposable
    {
        private PluginLootRuleRegistry? _holder = holder;

        public void Dispose() => Interlocked.Exchange(ref _holder, null)?.Withdraw(ident, socket);
    }
}
