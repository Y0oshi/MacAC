namespace MacAC.Client.Graphics;

using System.Runtime.ExceptionServices;

internal interface IRetryableAssetTidy
{
    bool IsTidyDone { get; }
    void ReattemptTidy();
}

internal sealed class AssetConstructionFault(
    string msg,
    IRetryableAssetTidy cleanup,
    IEnumerable<Exception> misses) : AggregateException(msg, misses),
    IRetryableAssetTidy
{
    private readonly IRetryableAssetTidy _tidy = cleanup ?? throw new ArgumentNullException(nameof(cleanup));

    public bool IsTidyDone => _tidy.IsTidyDone;

    public void ReattemptTidy() => _tidy.ReattemptTidy();
}

internal sealed class AssetTidyCluster : IRetryableAssetTidy
{
    private sealed record Entry(string Name, Action Release)
    {
        public bool Complete { get; set; }
    }

    private readonly List<Entry> _listings = [];
    private bool _running;

    public bool IsTidyDone => _listings.All(static listing => listing.Complete);

    public void Add(string label, Action free)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(free);
        if (_running || IsTidyDone && _listings.Count is not 0)
            throw new InvalidOperationException("The resource cleanup group is no longer accepting ownership");
        _listings.Add(new Entry(label, free));
    }

    public void TransferAll()
    {
        if (_running)
            throw new InvalidOperationException(
                "The resource cleanup group is currently releasing resources");
        foreach (Entry listing in _listings)
            listing.Complete = true;
    }

    public void ReattemptTidy()
    {
        if (_running || IsTidyDone)
            return;

        _running = true;
        List<Exception>? misses = null;
        try
        {
            for (int idx = _listings.Count - 1; idx >= 0; --idx)
            {
                Entry listing = _listings[idx];
                if (listing.Complete)
                    continue;
                try
                {
                    listing.Release();
                    listing.Complete = true;
                }
                catch (Exception miss)
                {
                    (misses ??= []).Add(new InvalidOperationException(
                        $"Resource cleanup operation '{listing.Name}' failed",
                        miss));
                }
            }
        }
        finally
        {
            _running = false;
        }

        if (misses is not null)
            throw new AggregateException("Composite resource cleanup remains incomplete", misses);
    }

    public void RevertConstructionAndThrow(
        string msg,
        Exception constructionMiss)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msg);
        ArgumentNullException.ThrowIfNull(constructionMiss);
        try
        {
            ReattemptTidy();
        }
        catch (Exception tidyMiss)
        {
            throw new AssetConstructionFault(
                msg,
                this,
                [constructionMiss, tidyMiss]);
        }

        ExceptionDispatchInfo.Capture(constructionMiss).Throw();
        throw new InvalidOperationException("Unreachable construction rollback path");
    }
}

internal sealed class AssetConstructionTidyRegister : IDisposable
{
    private readonly List<IRetryableAssetTidy> _queued = [];
    private bool _disposing;

    public bool IsComplete => _queued.Count is 0;

    public bool KeepFrom(Exception miss)
    {
        ArgumentNullException.ThrowIfNull(miss);
        bool kept = false;
        Tour(miss);
        return kept;

        void Tour(Exception latest)
        {
            if (latest is IRetryableAssetTidy tidy)
            {
                if (!tidy.IsTidyDone && !_queued.Contains(tidy))
                    _queued.Add(tidy);
                kept = true;
            }

            if (latest is AggregateException aggregate)
            {
                foreach (Exception interior in aggregate.InnerExceptions)
                    Tour(interior);
            }
            else if (latest.InnerException is { } interior)
            {
                Tour(interior);
            }
        }
    }

    public void Dispose()
    {
        if (_disposing || _queued.Count is 0)
            return;

        _disposing = true;
        List<Exception>? misses = null;
        try
        {
            for (int idx = _queued.Count - 1; idx >= 0; --idx)
            {
                var tidy = _queued[idx];
                try
                {
                    tidy.ReattemptTidy();
                    if (tidy.IsTidyDone)
                        _queued.RemoveAt(idx);
                }
                catch (Exception miss)
                {
                    (misses ??= []).Add(miss);
                }
            }
        }
        finally
        {
            _disposing = false;
        }

        if (misses is not null)
        {
            throw new AggregateException(
                "One or more failed resource construction transactions remain pending",
                misses);
        }
    }
}
