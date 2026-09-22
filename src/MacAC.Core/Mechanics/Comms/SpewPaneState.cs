namespace MacAC.Mechanics.Comms;

public readonly record struct SpewRow(string Text, double ExpiresAtSeconds);

public sealed class SpewPaneState
{
    public const int MaxConcurrentItems = 4;

    public static readonly TimeSpan DefaultLifespan = TimeSpan.FromSeconds(5);

    private readonly Lock _synchronize = new();
    private readonly Queue<string> _incoming = new();
    private readonly List<SpewRow> _shown = [];
    private long _rev;

    public long Revision => Interlocked.Read(ref _rev);

    public int Count
    {
        get
        {
            lock (_synchronize)
                return _shown.Count;
        }
    }

    public void Enqueue(string phrase)
    {
        lock (_synchronize)
            _incoming.Enqueue(phrase);
    }

    public void Tick(double instantSecs)
    {
        bool altered;
        lock (_synchronize)
        {
            altered = _incoming.Count > 0;
            while (_incoming.TryDequeue(out string? phrase))
            {
                if (_shown.Count > 0 && _shown[0].Text == phrase)
                    _shown.RemoveAt(0);
                _shown.Insert(0, new SpewRow(phrase, instantSecs + DefaultLifespan.TotalSeconds));
                while (_shown.Count > MaxConcurrentItems)
                    _shown.RemoveAt(_shown.Count - 1);
            }
            altered |= _shown.RemoveAll(rank => rank.ExpiresAtSeconds <= instantSecs) > 0;
        }
        if (altered)
            Interlocked.Increment(ref _rev);
    }

    public SpewRow[] Snapshot()
    {
        lock (_synchronize)
            return _shown.ToArray();
    }

    public void Reset()
    {
        lock (_synchronize)
        {
            _incoming.Clear();
            _shown.Clear();
        }
        Interlocked.Increment(ref _rev);
    }
}
