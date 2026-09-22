using System.Diagnostics;

namespace MacAC.Wire.Packets;

public sealed class FragmentStitcher
{
    internal const double PartialTtlSecs = 60.0;
    internal const int FinishedLoopDims = 64;

    // Pieces received so far for one message sequence
    private sealed class Partial(int tally, ushort fifo, double begunAt)
    {
        public readonly byte[]?[] Pieces = new byte[tally][];
        public readonly ushort Fifo = fifo;
        public int Have;
        public double TouchedAt = begunAt;

        public int Want => Pieces.Length;

        public bool Complete => Have >= Want;

        // Stores a piece unless that index already arrived (duplicates are harmless)
        public void Place(int ordinal, byte[] cargo, double instant)
        {
            if (Pieces[ordinal] is not null)
                return;
            Pieces[ordinal] = cargo;
            ++Have;
            TouchedAt = instant;
        }

        public byte[] Merge()
        {
            int sum = 0;
            foreach (byte[]? piece in Pieces)
                sum += piece!.Length;

            byte[] joined = new byte[sum];
            int at = 0;
            foreach (byte[]? piece in Pieces)
            {
                piece!.CopyTo(joined, at);
                at += piece.Length;
            }
            return joined;
        }
    }

    private readonly Dictionary<uint, Partial> _open = new();
    private readonly Func<double> _instant;
    private readonly uint[] _recent = new uint[FinishedLoopDims];
    private int _recentUpcoming;
    private int _recentTally;

    public FragmentStitcher() : this(null)
    {
    }

    internal FragmentStitcher(Func<double>? instantSecs) =>
        _instant = instantSecs ?? (static () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    public int PartialTally => _open.Count;

    /// <summary>Owned-payload path; a single-fragment message is handed straight back.</summary>
    public byte[]? Ingest(in WireFragment fragment, out ushort msgFifo)
    {
        var header = fragment.Header;
        if (header.Count is 1 && header.Index is 0)
        {
            msgFifo = header.Queue;
            return fragment.Payload;
        }

        msgFifo = 0;
        if (Open(header, verifyForm: false) is not { } partial)
            return null;
        partial.Place(header.Index, fragment.Payload, _instant());
        return Close(header.Sequence, partial, out msgFifo);
    }

    public void DiscardAll()
    {
        _open.Clear();
        _recentUpcoming = 0;
        _recentTally = 0;
    }

    // Borrowed-payload path; copies each piece, and rejects a piece whose count or queue disagrees
    // with what is already open
    internal bool TryIngest(in LeasedFragment fragment, out ReadOnlyMemory<byte> msg, out ushort msgFifo)
    {
        var header = fragment.Header;
        if (header.Count is 1 && header.Index is 0)
        {
            msg = fragment.Payload;
            msgFifo = header.Queue;
            return true;
        }

        msg = ReadOnlyMemory<byte>.Empty;
        msgFifo = 0;
        if (Open(header, verifyForm: true) is not { } partial)
            return false;
        partial.Place(header.Index, fragment.Payload.ToArray(), _instant());
        if (Close(header.Sequence, partial, out msgFifo) is not { } joined)
            return false;
        msg = joined;
        return true;
    }

    internal int SweepExpired()
    {
        if (_open.Count is 0)
            return 0;

        double instant = _instant();
        int evicted = 0;
        foreach ((uint series, Partial partial) in _open)
        {
            if (instant - partial.TouchedAt > PartialTtlSecs)
            {
                _open.Remove(series);
                ++evicted;
            }
        }
        return evicted;
    }

    // The open partial for this sequence, opening one unless the sequence was recently completed
    private Partial? Open(in WireFragmentHeader header, bool verifyForm)
    {
        if (_open.TryGetValue(header.Sequence, out Partial? partial))
            return verifyForm && (partial.Want != header.Count || partial.Fifo != header.Queue) ? null : partial;
        if (RecentlyFinished(header.Sequence))
            return null;
        return _open[header.Sequence] = new Partial(header.Count, header.Queue, _instant());
    }

    private byte[]? Close(uint series, Partial partial, out ushort fifo)
    {
        fifo = 0;
        if (!partial.Complete)
            return null;
        _open.Remove(series);
        Remember(series);
        fifo = partial.Fifo;
        return partial.Merge();
    }

    private bool RecentlyFinished(uint series) => Array.IndexOf(_recent, series, 0, _recentTally) >= 0;

    private void Remember(uint series)
    {
        _recent[_recentUpcoming] = series;
        _recentUpcoming = (_recentUpcoming + 1) % FinishedLoopDims;
        if (_recentTally < FinishedLoopDims)
            ++_recentTally;
    }
}
