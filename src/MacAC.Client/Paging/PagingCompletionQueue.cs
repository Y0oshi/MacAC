using System.Diagnostics;

namespace MacAC.Client.Paging;

public interface ILandblockWrapUpOrigin
{
    int BacklogTally { get; }
    bool TryPeek(out LandblockFlowOutcome? outcome);
    bool TryRead(out LandblockFlowOutcome? outcome);
}

internal enum PagingCompletionPriority : byte
{
    Destination = 0,
    Control = 1,
    Unload = 2,
    Near = 3,
    Far = 4,
}

internal readonly record struct PagingQueuedCompletion(
    LandblockFlowOutcome Result,
    LandblockFlowPriceEstimate Estimate,
    PagingCompletionPriority Priority,
    long RevealGeneration,
    ulong Generation,
    long Sequence,
    long EnqueuedTimestamp);

internal readonly record struct PagingCompletionQueueCapture(
    int Count,
    long RetainedCpuBytes,
    double OldestAgeMilliseconds,
    int Destination,
    int Control,
    int Unload,
    int Near,
    int Far);

internal sealed class PagingCompletionQueue
{
    private readonly Queue<PagingQueuedCompletion>[] _fifos =
        [.. Enumerable.Range(0, Enum.GetValues<PagingCompletionPriority>().Length).Select(static _ => new Queue<PagingQueuedCompletion>())];

    public int Count { get; private set; }
    public long KeptCpuOctets { get; private set; }

    public bool HasPrecedence(PagingCompletionPriority precedence) =>
        _fifos[(int)precedence].Count is not 0;

    public void Line(PagingQueuedCompletion wrapUp)
    {
        _fifos[(int)wrapUp.Priority].Enqueue(wrapUp);
        ++Count;
        KeptCpuOctets = SaturatingAppend(
            KeptCpuOctets,
            wrapUp.Estimate.Work.AdoptedCpuBytes);
    }

    public bool TryGlimpseUpcoming(
        Func<LandblockFlowOutcome, bool> isBlocked,
        out PagingQueuedCompletion? wrapUp,
        PagingCompletionPriority ceilingPrecedence =
            PagingCompletionPriority.Far)
    {
        ArgumentNullException.ThrowIfNull(isBlocked);
        int previousPrecedence = Math.Min(
            (int)ceilingPrecedence,
            _fifos.Length - 1);
        for (int precedence = 0; precedence <= previousPrecedence; ++precedence)
        {
            var fifo = _fifos[precedence];
            if (fifo.Count is 0)
                continue;
            var front = fifo.Peek();
            if (isBlocked(front.Result))
                continue;

            wrapUp = front;
            return true;
        }

        wrapUp = null;
        return false;
    }

    public void DropFront(PagingQueuedCompletion wrapUp)
    {
        var fifo =
            _fifos[(int)wrapUp.Priority];
        if (fifo.Count is 0)
        {
            throw new InvalidOperationException(
                "Streaming completion priority FIFO is empty");
        }

        var front = fifo.Peek();
        if (front.Sequence != wrapUp.Sequence
            || !ReferenceEquals(front.Result, wrapUp.Result))
        {
            throw new InvalidOperationException(
                "Streaming completion isn't the head of its priority FIFO");
        }

        fifo.Dequeue();
        Release(wrapUp);
    }

    public int DropOutcomes(
        IReadOnlyList<LandblockFlowOutcome> outcomes,
        Func<LandblockFlowOutcome, bool> shouldDrop)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(shouldDrop);
        int removed = 0;
        for (int precedence = 0; precedence < _fifos.Length; ++precedence)
        {
            var fifo = _fifos[precedence];
            int tally = fifo.Count;
            for (int listing = 0; listing < tally; ++listing)
            {
                var latest = fifo.Dequeue();
                bool fits = false;
                for (int idx = 0; idx < outcomes.Count; ++idx)
                {
                    if (ReferenceEquals(latest.Result, outcomes[idx]))
                    {
                        fits = true;
                        break;
                    }
                }

                if (fits && shouldDrop(latest.Result))
                {
                    Release(latest);
                    ++removed;
                }
                else
                {
                    fifo.Enqueue(latest);
                }
            }
        }
        return removed;
    }

    public void Clear()
    {
        for (int idx = 0; idx < _fifos.Length; ++idx)
            _fifos[idx].Clear();
        Count = 0;
        KeptCpuOctets = 0;
    }

    public bool TryDropOne()
    {
        for (int precedence = 0; precedence < _fifos.Length; ++precedence)
        {
            var fifo = _fifos[precedence];
            if (!fifo.TryDequeue(out PagingQueuedCompletion wrapUp))
                continue;

            Release(wrapUp);
            return true;
        }

        return false;
    }

    public PagingCompletionQueueCapture GrabSnapshot()
    {
        long instant = Stopwatch.GetTimestamp();
        long oldest = instant;
        bool located = false;
        for (int idx = 0; idx < _fifos.Length; ++idx)
        {
            foreach (PagingQueuedCompletion wrapUp in _fifos[idx])
            {
                oldest = Math.Min(oldest, wrapUp.EnqueuedTimestamp);
                located = true;
            }
        }

        return new PagingCompletionQueueCapture(
            Count,
            KeptCpuOctets,
            located
                ? Math.Max(0L, instant - oldest) * 1000.0 / Stopwatch.Frequency
                : 0,
            _fifos[(int)PagingCompletionPriority.Destination].Count,
            _fifos[(int)PagingCompletionPriority.Control].Count,
            _fifos[(int)PagingCompletionPriority.Unload].Count,
            _fifos[(int)PagingCompletionPriority.Near].Count,
            _fifos[(int)PagingCompletionPriority.Far].Count);
    }

    private void Release(PagingQueuedCompletion wrapUp)
    {
        --Count;
        KeptCpuOctets = Math.Max(
            0,
            KeptCpuOctets - wrapUp.Estimate.Work.AdoptedCpuBytes);
    }

    private static long SaturatingAppend(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}

internal sealed class DelegateLandblockWrapUpOrigin(
    Func<int, IReadOnlyList<LandblockFlowOutcome>> drain)
    : ILandblockWrapUpOrigin
{
    private readonly Func<int, IReadOnlyList<LandblockFlowOutcome>> _empty =
        drain ?? throw new ArgumentNullException(nameof(drain));
    private LandblockFlowOutcome? _peeked;

    public int BacklogTally => _peeked is null ? 0 : 1;

    public bool TryPeek(out LandblockFlowOutcome? outcome)
    {
        if (_peeked is null)
        {
            var lot = _empty(1);
            if (lot.Count > 1)
            {
                throw new InvalidOperationException(
                    "A single-result completion drain returned more than one result");
            }
            if (lot.Count is 0)
            {
                outcome = null;
                return false;
            }
            _peeked = lot[0];
        }

        outcome = _peeked;
        return true;
    }

    public bool TryRead(out LandblockFlowOutcome? outcome)
    {
        if (_peeked is not null)
        {
            outcome = _peeked;
            _peeked = null;
            return true;
        }

        var lot = _empty(1);
        if (lot.Count > 1)
        {
            throw new InvalidOperationException(
                "A single-result completion drain returned more than one result");
        }
        if (lot.Count is 0)
        {
            outcome = null;
            return false;
        }

        outcome = lot[0];
        return true;
    }
}
