using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

public interface ISimPlacementMirrorSink
{
    bool TryApply(in SimPlacementMirrorCapture proj);
}

public sealed class SimPlacementMirrorSubscription : ISimPlacementWatcher, IDisposable
{
    private readonly SimPlacementMirrorChannel _lane;
    private readonly Func<SimEpochTicket> _epoch;
    private readonly ISimPlacementMirrorSink _drain;
    private IDisposable? _subscription;
    private SimPlacementMirrorTicket _imposedUnacked;
    private bool _destroyed;

    public SimPlacementMirrorSubscription(SimCore core, ISimPlacementMirrorSink drain)
        : this(core, drain, reattemptQueuedOnEnlist: true)
    {
    }

    public SimPlacementMirrorSubscription(SimCore runtime, ISimPlacementMirrorSink drain, bool reattemptQueuedOnEnlist)
        : this(runtime?.Placements ?? throw new ArgumentNullException(nameof(runtime)), () => runtime.Generation, drain, reattemptQueuedOnEnlist)
    {
    }

    internal SimPlacementMirrorSubscription(SimPlacementMirrorChannel lane, Func<SimEpochTicket> gen, ISimPlacementMirrorSink drain)
        : this(lane, gen, drain, reattemptQueuedOnEnlist: true)
    {
    }

    internal SimPlacementMirrorSubscription(SimPlacementMirrorChannel channel, Func<SimEpochTicket> generation, ISimPlacementMirrorSink sink, bool reattemptQueuedOnEnlist)
    {
        _lane = channel ?? throw new ArgumentNullException(nameof(channel));
        _epoch = generation ?? throw new ArgumentNullException(nameof(generation));
        _drain = sink ?? throw new ArgumentNullException(nameof(sink));
        _subscription = _lane.Attach(this);
        if (reattemptQueuedOnEnlist)
            _ = RetryPending();
    }

    public bool HasImposedReceiptExpectingAcknowledgement => _imposedUnacked.IsValid;

    public bool HasQueuedReceipts => _lane.PendingTally is not 0;

    public bool RetryPending()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        var epoch = _epoch();
        // The remembered projection only counts while it is still at the head.
        if (_imposedUnacked.IsValid && (!_lane.TryPeek(epoch, out SimPlacementMirrorCapture front) || front.Token != _imposedUnacked))
            _imposedUnacked = default;
        return _lane.RetryQueued(epoch);
    }

    public void OnStance(in SimPlacementDiff diff)
    {
        if (_destroyed || !_lane.TryPeek(diff.Stamp.Generation, out SimPlacementMirrorCapture front) || front != diff.Placement)
            return;

        var ticket = front.Token;
        if (_imposedUnacked != ticket)
        {
            if (!_drain.TryApply(in front))
                return;
            if (_destroyed)
                return;
            _imposedUnacked = ticket;
        }

        if (_lane.Acknowledge(diff.Stamp.Generation, ticket) && _imposedUnacked == ticket)
            _imposedUnacked = default;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        Interlocked.Exchange(ref _subscription, null)?.Dispose();
        _imposedUnacked = default;
    }
}
