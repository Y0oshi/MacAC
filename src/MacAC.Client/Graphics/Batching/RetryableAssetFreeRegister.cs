namespace MacAC.Client.Graphics.Batching;

internal sealed class RetryableAssetFreeRegister
{
    private readonly ClientEntry[] _listings;
    private bool _advancing;

    public RetryableAssetFreeRegister(IEnumerable<(string Name, Action Release)> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        _listings = [.. listings.Select(listing => new ClientEntry(listing.Name, listing.Release))];
        LeftoverTally = _listings.Length;
    }

    public bool IsComplete => LeftoverTally is 0;
    public int LeftoverTally { get; private set; }

    public AssetFreeAttempt Advance()
    {
        if (_advancing)
            return new AssetFreeAttempt(0, 0, []);

        _advancing = true;
        List<AssetFreeMiss>? misses = null;
        int attempted = 0;
        int finished = 0;
        try
        {
            for (int idx = 0; idx < _listings.Length; ++idx)
            {
                ClientEntry listing = _listings[idx];
                if (listing.Completed || listing.Running)
                    continue;

                ++attempted;
                listing.Running = true;
                try
                {
                    listing.Release();
                    listing.Completed = true;
                    --LeftoverTally;
                    ++finished;
                }
                catch (Exception problem)
                {
                    bool committed = problem is TriMeshRefAlterationFault
                    {
                        AlterationSealed: true,
                    };
                    if (committed)
                    {
                        listing.Completed = true;
                        --LeftoverTally;
                        ++finished;
                    }

                    (misses ??= []).Add(
                        new AssetFreeMiss(listing.Name, problem, committed));
                }
                finally
                {
                    listing.Running = false;
                }
            }
        }
        finally
        {
            _advancing = false;
        }

        return new AssetFreeAttempt(
            attempted,
            finished,
            misses ?? []);
    }

    private sealed class ClientEntry
    {
        public ClientEntry(string name, Action free)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A resource-release stage needs a name", nameof(name));
            ArgumentNullException.ThrowIfNull(free);
            Name = name;
            Release = free;
        }

        public string Name { get; }
        public Action Release { get; }
        public bool Completed { get; set; }
        public bool Running { get; set; }
    }
}

internal readonly record struct AssetFreeMiss(
    string Stage,
    Exception Error,
    bool MutationCommitted);

internal readonly record struct AssetFreeAttempt(
    int AttemptedCount,
    int CompletedCount,
    IReadOnlyList<AssetFreeMiss> Failures)
{
    public bool HasMisses => Failures.Count is not 0;

    public AggregateException ToException(string msg)
    {
        return new(
            msg,
            Failures.Select(miss =>
                new InvalidOperationException(
                    $"Resource-release stage '{miss.Stage}' failed "
                    + (miss.MutationCommitted
                        ? "after committing its mutation"
                        : "prior to committing its mutation"),
                    miss.Error)));
    }
}
