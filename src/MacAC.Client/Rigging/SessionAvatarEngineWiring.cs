using MacAC.Client.Graphics;
using MacAC.Client.Realm;

namespace MacAC.Client.Rigging;

internal sealed class SessionAvatarEngineWiring : IDisposable
{
    private readonly List<(string Name, IDisposable Binding)> _bindings = [];
    private bool _deactivationBegun;

    public void Adopt(string label, IDisposable mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(mapping);
        ObjectDisposedException.ThrowIf(_deactivationBegun, this);
        _bindings.Add((label, mapping));
    }

    public void AttachActorPrimed(
        EquippedChildRenderDriver src,
        Action<OnlineActorReadyCandidate> handler)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(handler);
        src.EntityReady += handler;
        Adopt(
            "equipped-child entity ready",
            new DelegateWiring(() => src.EntityReady -= handler));
    }

    public void AttachLooksImposed(
        OnlineActorFillingDriver src,
        Action<uint> handler)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(handler);
        src.AppearanceApplied += handler;
        Adopt(
            "live appearance applied",
            new DelegateWiring(() => src.AppearanceApplied -= handler));
    }

    public void Dispose()
    {
        if (_deactivationBegun && _bindings.Count is 0)
            return;
        _deactivationBegun = true;

        List<Exception>? misses = null;
        for (int idx = _bindings.Count - 1; idx >= 0; --idx)
        {
            (string label, IDisposable mapping) = _bindings[idx];
            try
            {
                mapping.Dispose();
                _bindings.RemoveAt(idx);
            }
            catch (Exception miss)
            {
                (misses ??= []).Add(new InvalidOperationException(
                    $"Session/player binding '{label}' didn't detach",
                    miss));
            }
        }

        if (misses is not null)
        {
            throw new AggregateException(
                "Session/player binding cleanup remains incomplete",
                misses);
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
