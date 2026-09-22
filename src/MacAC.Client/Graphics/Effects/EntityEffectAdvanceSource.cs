namespace MacAC.Client.Graphics.Effects;

internal interface IActorEffectAdvanceSource
{
    bool CanProceedHolder(uint holderOwnIdent);
}

internal sealed class DeferredActorEffectAdvanceSource : IActorEffectAdvanceSource
{
    private readonly object _latch = new();
    private IActorEffectAdvanceSource? _mark;
    private bool _deactivated;

    public void Bind(IActorEffectAdvanceSource mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_deactivated, this);
            if (_mark is not null && !ReferenceEquals(_mark, mark))
            {
                throw new InvalidOperationException(
                    "Entity-effect script advancement is by now bound");
            }

            _mark = mark;
        }
    }

    public IDisposable BindOwned(IActorEffectAdvanceSource mark)
    {
        Bind(mark);
        return new Binding(this, mark);
    }

    public void Unbind(IActorEffectAdvanceSource mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            if (ReferenceEquals(_mark, mark))
                _mark = null;
        }
    }

    public void Deactivate()
    {
        lock (_latch)
        {
            _deactivated = true;
            _mark = null;
        }
    }

    public bool CanProceedHolder(uint holderOwnIdent)
    {
        lock (_latch)
        {
            return _deactivated
                || _mark?.CanProceedHolder(holderOwnIdent) != false;
        }
    }

    private sealed class Binding(
        DeferredActorEffectAdvanceSource holder,
        IActorEffectAdvanceSource mark) : IDisposable
    {
        private DeferredActorEffectAdvanceSource? _holder = holder;
        private readonly IActorEffectAdvanceSource _mark = mark;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Unbind(_mark);
    }
}
