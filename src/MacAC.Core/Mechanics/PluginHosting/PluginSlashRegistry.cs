using MacAC.Extensibility.Hosting;

namespace MacAC.Mechanics.PluginHosting;

public sealed class PluginSlashRegistry(Action<string, Exception>? onMiss = null) : ISlashCommandRegistry
{
    private const int UpperVerbLen = 32;
    private static readonly char[] Whitespace = [' ', '\t'];

    private readonly Lock _synchronize = new();
    private readonly Dictionary<string, MechBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);

    public IDisposable Register(string verb, Action<SlashCommand> handler)
    {
        string canon = CanonVerb(verb);
        ArgumentNullException.ThrowIfNull(handler);
        MechBinding mapping = new MechBinding(this, canon, handler);
        lock (_synchronize)
        {
            if (_bindings.ContainsKey(canon))
                throw new InvalidOperationException($"Plugin command '{canon}' is by now registered");
            _bindings.Add(canon, mapping);
        }
        return mapping;
    }

    public bool TryHnd(string rawPhrase)
    {
        if (string.IsNullOrWhiteSpace(rawPhrase))
            return false;
        string stroke = rawPhrase.Trim();
        if (stroke.Length < 2 || stroke[0] is not ('/' or '@'))
            return false;

        int gap = stroke.IndexOfAny(Whitespace, 1);
        string verb = gap < 0 ? stroke[1..] : stroke[1..gap];
        if (verb.Length is 0)
            return false;

        MechBinding? mapping;
        lock (_synchronize)
            _bindings.TryGetValue(verb, out mapping);
        if (mapping is null)
            return false;

        string arguments = gap < 0 ? string.Empty : stroke[(gap + 1)..].Trim();
        try
        {
            mapping.Invoke(new SlashCommand(mapping.Verb, arguments, stroke));
        }
        catch (Exception problem)
        {
            try
            {
                onMiss?.Invoke(mapping.Verb, problem);
            }
            catch
            {
            }
        }
        return true;
    }

    private static string CanonVerb(string verb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        string bare = verb.Trim().TrimStart('/', '@');
        if (bare.Length is < 1 or > UpperVerbLen || !bare.All(char.IsLetterOrDigit))
            throw new ArgumentException("Plugin command verbs must contain 1-32 letters or digits", nameof(verb));
        return bare;
    }

    private void Loosen(MechBinding anticipated)
    {
        lock (_synchronize)
        {
            if (_bindings.TryGetValue(anticipated.Verb, out MechBinding? pinned) && ReferenceEquals(pinned, anticipated))
                _bindings.Remove(anticipated.Verb);
        }
    }

    private sealed class MechBinding(PluginSlashRegistry holder, string verb, Action<SlashCommand> handler) : IDisposable
    {
        private readonly Lock _synchronize = new();
        private PluginSlashRegistry? _holder = holder;
        private Action<SlashCommand>? _handler = handler;

        internal string Verb { get; } = verb;

        internal void Invoke(SlashCommand directive)
        {
            Action<SlashCommand>? hnd;
            lock (_synchronize)
                hnd = _handler;
            hnd?.Invoke(directive);
        }

        public void Dispose()
        {
            PluginSlashRegistry? holder;
            lock (_synchronize)
            {
                holder = _holder;
                _holder = null;
                _handler = null;
            }
            holder?.Loosen(this);
        }
    }
}
