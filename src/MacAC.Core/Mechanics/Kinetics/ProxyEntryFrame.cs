using System.Buffers;

namespace MacAC.Mechanics.Kinetics;

internal ref struct ProxyEntryFrame
{
    private ProxyEntry[]? _rented;
    private readonly int _tally;

    private ProxyEntryFrame(ProxyEntry[] rented, int tally)
    {
        _rented = rented;
        _tally = tally;
    }

    public readonly ReadOnlySpan<ProxyEntry> Entries => _rented.AsSpan(0, _tally);

    public static ProxyEntryFrame Capture(IReadOnlyList<ProxyEntry> src)
    {
        int tally = src.Count;
        var rented = ArrayPool<ProxyEntry>.Shared.Rent(tally);
        try
        {
            for (int idx = 0; idx < tally; ++idx)
                rented[idx] = src[idx];
            return new ProxyEntryFrame(rented, tally);
        }
        catch
        {
            ArrayPool<ProxyEntry>.Shared.Return(rented, clearArray: false);
            throw;
        }
    }

    public void Dispose()
    {
        var rented = _rented;
        _rented = null;
        if (rented is not null)
            ArrayPool<ProxyEntry>.Shared.Return(rented, clearArray: false);
    }
}
