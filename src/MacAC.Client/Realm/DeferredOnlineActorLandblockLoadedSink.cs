namespace MacAC.Client.Realm;

internal interface IOnlineActorLandblockLoadedSink
{
    void OnLbLoaded(uint lbIdent);
}

internal sealed class DeferredOnlineActorLandblockLoadedSink
    : IOnlineActorLandblockLoadedSink
{
    private IOnlineActorLandblockLoadedSink? _mark;
    private bool _deactivated;

    public void OnLbLoaded(uint lbIdent)
    {
        if (!_deactivated)
            _mark?.OnLbLoaded(lbIdent);
    }

    public IDisposable Bind(IOnlineActorLandblockLoadedSink mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_mark is not null)
        {
            throw new InvalidOperationException(
                "Live-entity landblock hydration is by now bound");
        }

        _mark = mark;
        return new Binding(this, mark);
    }

    public void Deactivate()
    {
        _deactivated = true;
        _mark = null;
    }

    private void Loosen(IOnlineActorLandblockLoadedSink anticipated)
    {
        if (ReferenceEquals(_mark, anticipated))
            _mark = null;
    }

    private sealed class Binding(
        DeferredOnlineActorLandblockLoadedSink holder,
        IOnlineActorLandblockLoadedSink anticipated) : IDisposable
    {
        private DeferredOnlineActorLandblockLoadedSink? _holder = holder;
        private readonly IOnlineActorLandblockLoadedSink _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(_anticipated);
    }
}
