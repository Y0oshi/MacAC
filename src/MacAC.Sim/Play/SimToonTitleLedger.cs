namespace MacAC.Sim.Play;

public readonly record struct SimToonTitleCapture(uint DisplayTitleId, int TitleCount, long Revision);

/// <summary>Earned titles and the one on display, with events for the title panel.</summary>
public sealed class SimToonTitleLedger
{
    private readonly object _latch = new();
    private readonly HashSet<uint> _earned = new();
    private uint _shown;
    private long _rev;

    public event Action? TableReplaced;

    public event Action<uint>? TitleAdded;

    public event Action<uint>? DisplayTitleChanged;

    public uint ReadoutBannerIdent => Volatile.Read(ref _shown);
    public long Revision => Interlocked.Read(ref _rev);

    public IReadOnlyCollection<uint> EarnedBannerIdents
    {
        get { lock (_latch) return [.. _earned]; }
    }

    public int Count
    {
        get { lock (_latch) return _earned.Count; }
    }

    public bool HasEarnedBanner(uint bannerIdent)
    {
        lock (_latch) return _earned.Contains(bannerIdent);
    }

    public SimToonTitleCapture Snapshot
    {
        get
        {
            int tally;
            lock (_latch) tally = _earned.Count;
            return new SimToonTitleCapture(ReadoutBannerIdent, tally, Revision);
        }
    }

    public void ReplaceChart(uint readoutBannerIdent, IReadOnlyList<uint> bannerIdents)
    {
        ArgumentNullException.ThrowIfNull(bannerIdents);
        bool setAltered;
        bool shownAltered;
        lock (_latch)
        {
            setAltered = !_earned.SetEquals(bannerIdents);
            _earned.Clear();
            _earned.UnionWith(bannerIdents);
            shownAltered = _shown != readoutBannerIdent;
            if (shownAltered)
                Volatile.Write(ref _shown, readoutBannerIdent);
        }
        if (setAltered || shownAltered)
            Interlocked.Increment(ref _rev);
        TableReplaced?.Invoke();
        if (shownAltered)
            DisplayTitleChanged?.Invoke(readoutBannerIdent);
    }

    public void ImposeRefreshBanner(uint bannerIdent, bool setAsReadout)
    {
        bool added;
        bool shownAltered;
        lock (_latch)
        {
            added = _earned.Add(bannerIdent);
            shownAltered = setAsReadout && _shown != bannerIdent;
            if (shownAltered)
                Volatile.Write(ref _shown, bannerIdent);
        }
        if (added)
            Interlocked.Increment(ref _rev);
        if (shownAltered)
            Interlocked.Increment(ref _rev);
        if (added)
            TitleAdded?.Invoke(bannerIdent);
        if (shownAltered)
            DisplayTitleChanged?.Invoke(bannerIdent);
    }

    public void ResetSession()
    {
        uint wasShown;
        lock (_latch)
        {
            wasShown = _shown;
            _earned.Clear();
            Volatile.Write(ref _shown, 0u);
        }
        Interlocked.Increment(ref _rev);
        TableReplaced?.Invoke();
        if (wasShown is not 0u)
            DisplayTitleChanged?.Invoke(0u);
    }
}
