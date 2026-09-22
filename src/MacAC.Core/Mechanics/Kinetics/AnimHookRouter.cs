using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public sealed class AnimHookRouter : IAnimHookTap
{
    private readonly Lock _latch = new();
    private IAnimHookTap[] _taps = [];

    public IReadOnlyList<IAnimHookTap> Sinks => _taps;

    /// <summary>Register a sink. Idempotent - adding the same instance twice is a no-op.</summary>
    public void Register(IAnimHookTap drain)
    {
        ArgumentNullException.ThrowIfNull(drain);
        lock (_latch)
        {
            if (OrdinalOf(drain) >= 0)
                return;
            _taps = [.. _taps, drain];
        }
    }

    /// <summary>Unregister a sink. No-op if not registered.</summary>
    public void Unregister(IAnimHookTap drain)
    {
        if (drain is null)
            return;
        lock (_latch)
        {
            int at = OrdinalOf(drain);
            if (at < 0)
                return;
            IAnimHookTap[] kept = new IAnimHookTap[_taps.Length - 1];
            Array.Copy(_taps, 0, kept, 0, at);
            Array.Copy(_taps, at + 1, kept, at, _taps.Length - at - 1);
            _taps = kept;
        }
    }

    public void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue hook)
    {
        // Snapshot - no lock in the hot path (render thread).
        foreach (IAnimHookTap tap in _taps)
        {
            try
            {
                tap.OnTap(actorIdent, actorRealmLocus, hook);
            }
            catch
            {
                // One misbehaving tap must not take down the whole animation
                // tick; subsystems log their own failures.
            }
        }
    }

    private int OrdinalOf(IAnimHookTap drain)
    {
        for (int idx = 0; idx < _taps.Length; ++idx)
        {
            if (ReferenceEquals(_taps[idx], drain))
                return idx;
        }
        return -1;
    }
}
