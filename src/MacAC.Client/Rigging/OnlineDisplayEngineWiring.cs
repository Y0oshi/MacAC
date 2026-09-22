using MacAC.Client.Graphics;
using MacAC.Client.Realm;

namespace MacAC.Client.Rigging;

internal sealed class OnlineDisplayEngineWiring : IDisposable
{
    private sealed record Entry(string Name, IDisposable Binding);

    private readonly List<Entry> _bindings = [];
    private bool _deactivationBegun;

    public void Adopt(string label, IDisposable mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(mapping);
        ObjectDisposedException.ThrowIf(_deactivationBegun, this);
        _bindings.Add(new Entry(label, mapping));
    }

    public IDisposable AdoptPossessed(string label, IDisposable mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(mapping);
        ObjectDisposedException.ThrowIf(_deactivationBegun, this);
        Entry listing = new Entry(label, mapping);
        _bindings.Add(listing);
        return new Uptake(this, listing);
    }

    public void AdoptFree(string label, Action free) =>
        Adopt(label, new DelegateWiring(free));

    public void AttachProjVis(
        OnlineActorCore src,
        Action<OnlineActorRecord, bool> handler,
        string label)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(handler);
        src.ProjectionVisibilityChanged += handler;
        Adopt(label, new DelegateWiring(
            () => src.ProjectionVisibilityChanged -= handler));
    }

    public void AttachProjPosturePrimed(
        EquippedChildRenderDriver src,
        Action<uint> handler)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(handler);
        src.ProjectionPoseReady += handler;
        Adopt("equipped-child projection pose", new DelegateWiring(
            () => src.ProjectionPoseReady -= handler));
    }

    public void AttachProjRemoved(
        EquippedChildRenderDriver src,
        Action<OnlineActorRecord> handler)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(handler);
        src.ProjectionRemoved += handler;
        Adopt("equipped-child projection removal", new DelegateWiring(
            () => src.ProjectionRemoved -= handler));
    }

    public void Dispose()
    {
        if (_deactivationBegun && _bindings.Count is 0)
            return;
        _deactivationBegun = true;

        List<Exception>? misses = null;
        for (int idx = _bindings.Count - 1; idx >= 0; --idx)
        {
            Entry listing = _bindings[idx];
            try
            {
                listing.Binding.Dispose();
                _bindings.RemoveAt(idx);
            }
            catch (Exception miss)
            {
                (misses ??= []).Add(new InvalidOperationException(
                    $"Live-presentation binding '{listing.Name}' didn't detach",
                    miss));
            }
        }

        if (misses is not null)
        {
            throw new AggregateException(
                "Live-presentation binding cleanup remains incomplete",
                misses);
        }
    }

    private void FreeAdoption(Entry anticipated)
    {
        int ordinal = _bindings.IndexOf(anticipated);
        if (ordinal < 0)
            return;
        anticipated.Binding.Dispose();
        _bindings.RemoveAt(ordinal);
    }

    private sealed class Uptake(
        OnlineDisplayEngineWiring holder,
OnlineDisplayEngineWiring.Entry anticipated) : IDisposable
    {
        private OnlineDisplayEngineWiring? _holder = holder;
        private readonly Entry _anticipated = anticipated;

        public void Dispose()
        {
            var holder = _holder;
            if (holder is null)
                return;
            holder.FreeAdoption(_anticipated);
            _holder = null;
        }
    }

    private sealed class DelegateWiring(Action release) : IDisposable
    {
        private Action? _free = release ?? throw new ArgumentNullException(nameof(release));

        public void Dispose()
        {
            Action? free = _free;
            if (free is null)
                return;
            free();
            _free = null;
        }
    }
}
