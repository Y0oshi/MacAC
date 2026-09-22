using MacAC.Sim;

namespace MacAC.Client.Link;

internal interface IEnginePlacementMirrorRetryPhase
{
    void ReattemptPending();
}

internal sealed class EnginePlacementMirrorRetrySlot
    : IEnginePlacementMirrorRetryPhase
{
    private sealed record Binding(
        long Id,
        SimEpochTicket Generation,
        Func<bool> Retry);

    private readonly Func<SimEpochTicket> _latestGen;
    private Binding? _latest;
    private long _upcomingMappingIdent;

    internal EnginePlacementMirrorRetrySlot(
        Func<SimEpochTicket> currentGeneration)
    {
        _latestGen = currentGeneration
            ?? throw new ArgumentNullException(nameof(currentGeneration));
    }

    internal int MappingTally => _latest is null ? 0 : 1;

    public void ReattemptPending()
    {
        Binding? mapping = _latest;
        if (mapping is null
            || mapping.Generation != _latestGen())

            return;

        _ = mapping.Retry();
    }

    internal IDisposable BindOwned(
        SimEpochTicket generation,
        Func<bool> reattempt)
    {
        if (generation.Value is 0UL)
        {
            throw new ArgumentException(
                "A placement retry route needs a live Runtime generation",
                nameof(generation));
        }
        ArgumentNullException.ThrowIfNull(reattempt);
        if (_latest is not null)
        {
            throw new InvalidOperationException(
                "A graphical placement retry route is by now bound");
        }

        Binding mapping = new Binding(
            checked(++_upcomingMappingIdent),
            generation,
            reattempt);
        _latest = mapping;
        return new ClientDelegateDisposable(() => Loosen(mapping));
    }

    private void Loosen(Binding mapping)
    {
        if (ReferenceEquals(_latest, mapping))
            _latest = null;
    }

    private sealed class ClientDelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _teardown = dispose
            ?? throw new ArgumentNullException(nameof(dispose));

        public void Dispose() => Interlocked.Exchange(ref _teardown, null)?.Invoke();
    }
}
