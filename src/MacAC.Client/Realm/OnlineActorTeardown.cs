namespace MacAC.Client.Realm;

internal sealed class OnlineActorTeardownPlan
{
    private readonly Action[] _hops;
    private readonly bool[] _completed;

    public OnlineActorTeardownPlan(IEnumerable<Action> hops)
    {
        ArgumentNullException.ThrowIfNull(hops);
        _hops = [.. hops];
        _completed = new bool[_hops.Length];
    }

    internal int FinishedTally => _completed.Count(static finished => finished);
    internal bool IsDone => FinishedTally == _hops.Length;

    public void Advance()
    {
        List<Exception>? misses = null;
        for (int idx = 0; idx < _hops.Length; ++idx)
        {
            if (_completed[idx])
                continue;
            try
            {
                _hops[idx]();
                _completed[idx] = true;
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }

        if (misses is not null)
            throw new AggregateException(
                "One or more live-entity component teardown steps failed",
                misses);
    }
}
