using MacAC.Sim;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;

namespace MacAC.Client.Link;

internal sealed class GraphicalSessionSignalCourse : IOnlineSessionEventRouting
{
    private readonly IOnlineSessionEventRouting _signals;
    private readonly Func<SimPlacementMirrorSubscription>
        _buildSubscription;
    private readonly Func<SimEpochTicket> _gen;
    private readonly EnginePlacementMirrorRetrySlot _reattempts;
    private readonly SimDebutPilot? _leadListing;
    private readonly SimGrantedPositionPilot? _approvedLocusSteer;
    private readonly SimPeerPlacementPilot? _distantStanceSteer;
    private readonly Action<SimActorRecord>? _localPlayerCompleted;
    private SimPlacementMirrorSubscription? _subscription;
    private IDisposable? _reattemptTenancy;
    private bool _fastenBegun;
    private bool _signalsDestroyed;
    private bool _destroyed;

    internal GraphicalSessionSignalCourse(
        IOnlineSessionEventRouting signals,
        SimCore core,
        ISimPlacementMirrorSink stances,
        EnginePlacementMirrorRetrySlot reattempts,
        SimDebutPilot? leadListing = null,
        Action<SimActorRecord>? ownAvatarFinished = null,
        SimGrantedPositionPilot? approvedLocusSteer = null,
        SimPeerPlacementPilot? distantStanceSteer = null)
        : this(
            signals,
            () => new SimPlacementMirrorSubscription(
                core,
                stances,
                reattemptQueuedOnEnlist: false),
            () => core.Generation,
            reattempts,
            leadListing,
            ownAvatarFinished,
            approvedLocusSteer,
            distantStanceSteer)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(stances);
    }

    internal GraphicalSessionSignalCourse(
        IOnlineSessionEventRouting events,
        Func<SimPlacementMirrorSubscription> createSubscription,
        Func<SimEpochTicket> generation,
        EnginePlacementMirrorRetrySlot retries,
        SimDebutPilot? leadListing = null,
        Action<SimActorRecord>? ownAvatarFinished = null,
        SimGrantedPositionPilot? approvedLocusSteer = null,
        SimPeerPlacementPilot? distantStanceSteer = null)
    {
        _signals = events ?? throw new ArgumentNullException(nameof(events));
        _buildSubscription = createSubscription
            ?? throw new ArgumentNullException(nameof(createSubscription));
        _gen = generation
            ?? throw new ArgumentNullException(nameof(generation));
        _reattempts = retries ?? throw new ArgumentNullException(nameof(retries));
        _leadListing = leadListing;
        _localPlayerCompleted = ownAvatarFinished;
        _approvedLocusSteer = approvedLocusSteer;
        _distantStanceSteer = distantStanceSteer;
    }

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (_fastenBegun)
            return;

        _fastenBegun = true;
        _leadListing?.FastenCourse(this, _localPlayerCompleted);
        _approvedLocusSteer?.FastenCourse(this);
        _distantStanceSteer?.FastenCourse(this);
        _signals.Attach();

        SimPlacementMirrorSubscription? subscription = null;
        IDisposable? reattemptTenancy = null;
        try
        {
            subscription = _buildSubscription();
            var tiedSubscription =
                subscription;
            reattemptTenancy = _reattempts.BindOwned(
                _gen(),
                () =>
                {
                    _leadListing?.DriveAll();
                    _approvedLocusSteer?.Advance();
                    _distantStanceSteer?.Advance();
                    return tiedSubscription.RetryPending();
                });
            _subscription = subscription;
            _reattemptTenancy = reattemptTenancy;
            _ = subscription.RetryPending();
        }
        catch
        {
            reattemptTenancy?.Dispose();
            subscription?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        Interlocked.Exchange(ref _reattemptTenancy, null)?.Dispose();
        Interlocked.Exchange(ref _subscription, null)?.Dispose();
        _leadListing?.UnfastenCourse(this);
        _approvedLocusSteer?.UnfastenCourse(this);
        _distantStanceSteer?.UnfastenCourse(this);
        if (!_signalsDestroyed)
        {
            _signals.Dispose();
            _signalsDestroyed = true;
        }

        _destroyed = true;
    }
}
