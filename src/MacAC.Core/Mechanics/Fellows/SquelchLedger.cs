namespace MacAC.Mechanics.Fellows;

public sealed record SquelchFacts(
    string Name,
    bool AccountWide,
    IReadOnlySet<uint> MessageTypes);

/// <summary>The server's full squelch picture for this character.</summary>
public sealed record SquelchBook(
    IReadOnlyDictionary<string, uint> Accounts,
    IReadOnlyDictionary<uint, SquelchFacts> Characters,
    SquelchFacts Global)
{
    public static SquelchBook Empty { get; } = new(
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<uint, SquelchFacts>(),
        new SquelchFacts(string.Empty, AccountWide: false, new HashSet<uint>()));
}

/// <summary>Holds the current <see cref="SquelchBook"/> and counts replacements.</summary>
public sealed class SquelchLedger
{
    private readonly Lock _synchronize = new();
    private SquelchBook _book = SquelchBook.Empty;
    private long _rev;

    public long Revision => Interlocked.Read(ref _rev);

    public SquelchBook Snapshot()
    {
        lock (_synchronize)
            return _book;
    }

    public void Replace(SquelchBook database)
    {
        ArgumentNullException.ThrowIfNull(database);
        Swap(database);
    }

    public void Clear() => Swap(SquelchBook.Empty);

    private void Swap(SquelchBook upcoming)
    {
        lock (_synchronize)
        {
            _book = upcoming;
            Interlocked.Increment(ref _rev);
        }
    }
}
