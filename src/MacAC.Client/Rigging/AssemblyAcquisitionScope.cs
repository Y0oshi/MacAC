using System.Runtime.ExceptionServices;
using MacAC.Client.Graphics;

namespace MacAC.Client.Rigging;

internal sealed class AssemblyAcquisitionScope : IRetryableAssetTidy
{
    internal enum EntryPhase
    {
        Owned,
        Transferred,
        Released,
    }

    internal sealed class Entry(string label, object asset, Action free)
    {
        public string Name { get; } = label;
        public object Resource { get; } = asset;
        public Action Release { get; } = free;
        public EntryPhase State { get; set; } = EntryPhase.Owned;
    }

    private readonly List<Entry> _listings = [];
    private bool _tidyEngaged;
    private bool _closed;

    public bool IsTidyDone =>
        _listings.All(static listing => listing.State is not EntryPhase.Owned);

    public AssemblyAcquisitionLease<T> Acquire<T>(
        string label,
        Func<T> maker,
        Action<T> free)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(maker);
        ArgumentNullException.ThrowIfNull(free);
        SecureAcceptingOwnership();

        T asset = maker()
            ?? throw new InvalidOperationException(
                $"Composition factory '{label}' returned null");
        return Own(label, asset, free);
    }

    public AssemblyAcquisitionElectiveLease<T> ObtainOptional<T>(
        string label,
        Func<T?> maker,
        Action<T> free)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(maker);
        ArgumentNullException.ThrowIfNull(free);
        SecureAcceptingOwnership();

        T? asset = maker();
        return asset is null
            ? new AssemblyAcquisitionElectiveLease<T>(null)
            : new AssemblyAcquisitionElectiveLease<T>(
                Own(label, asset, free));
    }

    public AssemblyAcquisitionLease<T> Own<T>(
        string label,
        T asset,
        Action<T> free)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(free);
        SecureAcceptingOwnership();

        Entry listing = new Entry(label, asset, () => free(asset));
        _listings.Add(listing);
        return new AssemblyAcquisitionLease<T>(
            this,
            listing,
            asset);
    }

    public void Complete()
    {
        if (_tidyEngaged)
            throw new InvalidOperationException(
                "Composition cleanup is currently active");
        if (_closed)
            return;
        if (!IsTidyDone)
        {
            string queued = string.Join(
                ", ",
                _listings
                    .Where(static listing => listing.State == EntryPhase.Owned)
                    .Select(static listing => listing.Name));
            throw new InvalidOperationException(
                $"Composition phase completed with unpublished resources: {queued}.");
        }

        _closed = true;
    }

    public void RevertAndThrow(Exception constructionMiss)
    {
        ArgumentNullException.ThrowIfNull(constructionMiss);
        _closed = true;

        try
        {
            ReattemptTidy();
        }
        catch (AggregateException tidyMiss)
        {
            List<Exception> misses = new List<Exception> { constructionMiss };
            misses.AddRange(tidyMiss.InnerExceptions);
            throw new AssemblyAcquisitionException(
                "Startup composition failed and rollback remains incomplete",
                this,
                misses);
        }

        ExceptionDispatchInfo.Capture(constructionMiss).Throw();
    }

    public void ReattemptTidy()
    {
        if (_tidyEngaged || IsTidyDone)
            return;

        _closed = true;
        _tidyEngaged = true;
        List<Exception>? misses = null;
        try
        {
            for (int idx = _listings.Count - 1; idx >= 0; --idx)
            {
                Entry listing = _listings[idx];
                if (listing.State != EntryPhase.Owned)
                    continue;

                try
                {
                    listing.Release();
                    listing.State = EntryPhase.Released;
                }
                catch (Exception miss)
                {
                    (misses ??= []).Add(new InvalidOperationException(
                        $"Composition cleanup operation '{listing.Name}' failed",
                        miss));
                }
            }
        }
        finally
        {
            _tidyEngaged = false;
        }

        if (misses is not null)
        {
            throw new AggregateException(
                "Startup composition rollback remains incomplete",
                misses);
        }
    }

    private void SecureAcceptingOwnership()
    {
        if (_closed || _tidyEngaged)
            throw new InvalidOperationException(
                "The composition acquisition scope is no longer accepting ownership");
    }

    private void Transfer<T>(Entry listing, T anticipated)
        where T : class
    {
        if (_closed || _tidyEngaged)
            throw new InvalidOperationException(
                "The composition acquisition scope is no longer transferable");
        if (!ReferenceEquals(listing.Resource, anticipated))
            throw new InvalidOperationException(
                "The acquisition lease doesn't own the wanted resource");
        if (listing.State != EntryPhase.Owned)
            throw new InvalidOperationException(
                $"Composition resource '{listing.Name}' has by now been transferred");

        listing.State = EntryPhase.Transferred;
    }

    internal sealed class AssemblyAcquisitionLease<T>
        where T : class
    {
        private readonly AssemblyAcquisitionScope _ambit;
        private readonly Entry _listing;

        internal AssemblyAcquisitionLease(
            AssemblyAcquisitionScope ambit,
            Entry listing,
            T asset)
        {
            _ambit = ambit;
            _listing = listing;
            Resource = asset;
        }

        public T Resource { get; }

        public T Transfer()
        {
            _ambit.Transfer(_listing, Resource);
            return Resource;
        }

        public T Publish(Action<T> broadcast)
        {
            ArgumentNullException.ThrowIfNull(broadcast);
            broadcast(Resource);
            return Transfer();
        }
    }

    // A lease over a resource the active backend may not own at all
    internal sealed class AssemblyAcquisitionElectiveLease<T>(
        AssemblyAcquisitionLease<T>? interior)
        where T : class
    {
        public T? Resource => interior?.Resource;

        public T? Transfer() => interior?.Transfer();

        public T? Publish(Action<T?> broadcast)
        {
            ArgumentNullException.ThrowIfNull(broadcast);
            if (interior is null)
            {
                broadcast(null);
                return null;
            }

            broadcast(interior.Resource);
            return interior.Transfer();
        }
    }
}

internal sealed class AssemblyAcquisitionException(
    string msg,
    IRetryableAssetTidy cleanup,
    IEnumerable<Exception> misses) : AggregateException(msg, misses),
    IRetryableAssetTidy
{
    private readonly IRetryableAssetTidy _tidy = cleanup ?? throw new ArgumentNullException(nameof(cleanup));

    public bool IsTidyDone => _tidy.IsTidyDone;

    public void ReattemptTidy() => _tidy.ReattemptTidy();
}
