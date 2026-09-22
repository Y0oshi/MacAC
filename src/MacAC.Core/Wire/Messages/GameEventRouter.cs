namespace MacAC.Wire.Messages;

public sealed class GameEventRouter
{
    public delegate void EventHandler(GameEventParcel envelope);

    private sealed class Stratum(GameEventKind sort, EventHandler handler, Stratum? beneath)
    {
        public GameEventKind Kind { get; } = sort;
        public EventHandler Handler { get; } = handler;
        public Stratum? Beneath { get; } = beneath;
        public bool Retired { get; set; }
    }

    private sealed class Ownership(GameEventRouter router, Stratum stratum) : IDisposable
    {
        private Action? _free = () => router.Retire(stratum);

        public void Dispose() => Interlocked.Exchange(ref _free, null)?.Invoke();
    }

    private readonly Dictionary<GameEventKind, Stratum> _top = new();
    private readonly Dictionary<GameEventKind, int> _unhandled = new();

    /// <summary>Installs <paramref name="handler"/> as the sole handler for <paramref name="kind"/>.</summary>
    public void Register(GameEventKind kind, EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _top[kind] = new Stratum(kind, handler, beneath: null);
    }

    public IDisposable ListPossessed(GameEventKind kind, EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _top.TryGetValue(kind, out Stratum? beneath);
        Stratum stratum = new Stratum(kind, handler, beneath);
        _top[kind] = stratum;
        return new Ownership(this, stratum);
    }

    public void Unregister(GameEventKind kind)
    {
        if (_top.Remove(kind, out Stratum? latest))
            latest.Retired = true;
    }

    public void Relay(GameEventParcel envelope)
    {
        if (!_top.TryGetValue(envelope.EventType, out Stratum? stratum))
        {
            _unhandled[envelope.EventType] = FetchUnhandledTally(envelope.EventType) + 1;
            return;
        }

        try
        {
            stratum.Handler(envelope);
        }
        catch (Exception exc)
        {
            Console.Error.WriteLine($"[GameEvent] handler for 0x{(uint)envelope.EventType:X4} threw: {exc.Message}");
        }
    }

    /// <summary>How many events of this kind arrived with nobody listening.</summary>
    public int FetchUnhandledTally(GameEventKind kind) => _unhandled.TryGetValue(kind, out int num) ? num : 0;

    public IReadOnlyDictionary<GameEventKind, int> UnhandledCounts => _unhandled;

    public void RestartUnhandledCounts() => _unhandled.Clear();

    public int RegisteredHandlerTally => _top.Count;

    private void Retire(Stratum stratum)
    {
        stratum.Retired = true;
        if (!_top.TryGetValue(stratum.Kind, out Stratum? latest) || !ReferenceEquals(latest, stratum))
            return;

        Stratum? survivor = stratum.Beneath;
        while (survivor?.Retired == true)
            survivor = survivor.Beneath;

        if (survivor is null)
            _top.Remove(stratum.Kind);
        else
            _top[stratum.Kind] = survivor;
    }
}
