using MacAC.Extensibility.RenderPacks;

namespace MacAC.Mechanics.PluginHosting;

// A plugin's view of the render-pack shelf: every pack it registers is withdrawn together when the
// plugin unloads
internal sealed class RenderPackShelfScope : IRenderPackShelf, IDisposable
{
    private readonly IRenderPackShelf _shelf;
    private readonly Lock _synchronize = new();
    private readonly List<Handle> _hnds = [];
    private bool _destroyed;

    internal RenderPackShelfScope(IRenderPackShelf inner) =>
        _shelf = inner ?? throw new ArgumentNullException(nameof(inner));

    public IDisposable Register(RenderPackCard descriptor, IRenderPackFiles holdings)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(holdings);
        lock (_synchronize)
            ObjectDisposedException.ThrowIf(_destroyed, this);

        IDisposable underlying = _shelf.Register(descriptor, holdings)
            ?? throw new InvalidOperationException("The render-pack registry returned a null registration handle");
        Handle hnd = new Handle(this, underlying);
        lock (_synchronize)
        {
            if (!_destroyed)
            {
                _hnds.Add(hnd);
                return hnd;
            }
        }
        hnd.Dispose();
        throw new ObjectDisposedException(nameof(RenderPackShelfScope));
    }

    public void Dispose()
    {
        Handle[] hnds;
        lock (_synchronize)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            hnds = [.. _hnds];
            _hnds.Clear();
        }

        List<Exception>? misses = null;
        for (int idx = hnds.Length - 1; idx >= 0; --idx)
        {
            try
            {
                hnds[idx].Dispose();
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }
        if (misses is not null)
            throw new AggregateException("One or more render-pack registrations could not be withdrawn", misses);
    }

    private void Withdraw(Handle hnd)
    {
        lock (_synchronize)
            _hnds.Remove(hnd);
        hnd.FreeUnderlying();
    }

    private sealed class Handle(RenderPackShelfScope holder, IDisposable underlying) : IDisposable
    {
        private RenderPackShelfScope? _holder = holder;
        private IDisposable? _underlying = underlying;

        public void Dispose() => Interlocked.Exchange(ref _holder, null)?.Withdraw(this);

        internal void FreeUnderlying() => Interlocked.Exchange(ref _underlying, null)?.Dispose();
    }
}
