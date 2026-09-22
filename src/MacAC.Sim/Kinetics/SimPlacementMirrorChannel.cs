using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

public sealed class SimPlacementMirrorChannel
{
    private readonly SimActorObjectEventFlow _signals;
    private readonly SimSetPositionLedger _stances;
    private readonly SimSpawnFollowupRunner _spawns;
    private Func<SimEpochTicket> _epoch = static () => default;
    private bool _epochTied;

    internal SimPlacementMirrorChannel(SimActorObjectEventFlow events, SimSetPositionLedger setPosition, SimSpawnFollowupRunner initialCreateExecution)
    {
        _signals = events ?? throw new ArgumentNullException(nameof(events));
        _stances = setPosition ?? throw new ArgumentNullException(nameof(setPosition));
        _spawns = initialCreateExecution ?? throw new ArgumentNullException(nameof(initialCreateExecution));
    }

    public int PendingTally => _stances.QueuedProjTally;

    public IDisposable Attach(ISimPlacementWatcher watcher) => _signals.EnlistStance(watcher);

    public bool Acknowledge(SimEpochTicket anticipatedGen, in SimPlacementMirrorTicket ticket) =>
        Live(anticipatedGen) && _stances.AcknowledgeProj(ticket);

    public bool RetryQueued(SimEpochTicket anticipatedGen)
    {
        if (!Live(anticipatedGen))
            return false;
        _stances.ReattemptQueuedProjections();
        return true;
    }

    public bool TryPeek(SimEpochTicket anticipatedGen, out SimPlacementMirrorCapture proj)
    {
        if (Live(anticipatedGen))
            return _stances.TryGlimpseProj(out proj);
        proj = default;
        return false;
    }

    public bool TryFetchStartingBuildWrapUp(SimEpochTicket anticipatedGen, in SimPlacementMirrorTicket ticket, out SimSpawnPlacementFinish wrapUp)
    {
        if (Live(anticipatedGen))
            return _spawns.TryFetchWrapUp(ticket, out wrapUp);
        wrapUp = default;
        return false;
    }

    internal void AttachGen(Func<SimEpochTicket> gen)
    {
        ArgumentNullException.ThrowIfNull(gen);
        if (_epochTied)
            throw new InvalidOperationException("The Runtime placement generation source is by now bound");
        _epoch = gen;
        _epochTied = true;
    }

    private bool Live(SimEpochTicket anticipated) => _epochTied && anticipated.Value is not 0UL && anticipated == _epoch();
}
