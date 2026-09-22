using MacAC.Sim.Play;
using MacAC.Sim.Presence;
using MacAC.Sim.Realm;

namespace MacAC.Sim;

[Flags]
public enum SimCoreTeardownStage
{
    None = 0,
    HostLeasesReleased = 1 << 0,
    EventsDetached = 1 << 1,
    SessionDisposed = 1 << 2,
    TransitReset = 1 << 3,
    ActionsDisposed = 1 << 4,
    MovementDisposed = 1 << 5,
    CharacterDisposed = 1 << 6,
    InventoryDisposed = 1 << 7,
    CommunicationDisposed = 1 << 8,
    FellowshipDisposed = 1 << 9,
    AllegianceDisposed = 1 << 10,
    TradeDisposed = 1 << 11,
    IdentityDisposed = 1 << 12,
    EntityObjectsDisposed = 1 << 13,
    ContractsDisposed = 1 << 14,
    JournalDisposed = 1 << 15,
    Complete =
        HostLeasesReleased | EventsDetached | SessionDisposed | TransitReset
        | ActionsDisposed | MovementDisposed | CharacterDisposed | InventoryDisposed
        | CommunicationDisposed | FellowshipDisposed | AllegianceDisposed | TradeDisposed
        | ContractsDisposed | JournalDisposed | IdentityDisposed | EntityObjectsDisposed,
}

public readonly record struct SimCoreHoldingCapture(
    bool IsDisposeRequested,
    bool IsDisposeDrainActive,
    bool IsDisposed,
    int HostLeaseCount,
    SimCoreTeardownStage CompletedTeardownStages,
    OnlineSessionHoldingCapture Session,
    SimAvatarIdentityHoldingCapture PlayerIdentity,
    SimSimulationHoldingCapture Simulation,
    SimRealmAmbienceHoldingCapture Environment,
    SimRealmCrossingHoldingCapture Transit,
    SimEpochResetCapture GenerationReset,
    SimCoreEventHoldingCapture Events)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed
        && IsDisposeRequested
        && !IsDisposeDrainActive
        && HostLeaseCount is 0
        && CompletedTeardownStages == SimCoreTeardownStage.Complete
        && Session.IsConverged
        && PlayerIdentity.IsConverged
        && Simulation.IsConverged
        && Transit.IsSessionIdle
        && GenerationReset.IsConverged
        && Events.IsConverged;
        }
    }
}

public sealed partial class SimCore
{
    private const int TeardownStageCount = 16;

    private int _disposeStage;
    private bool _teardownAsked;
    private bool _teardownEmptyEngaged;
    private bool _destroyed;

    // One rung of the ladder: the flag it earns, the work, and the convergence check
    private readonly record struct Rung(SimCoreTeardownStage Flag, Action Retire, Func<bool> Converged);

    private Rung[]? _ladder;

    private Rung[] Ladder
    {
        get
        {
            return _ladder ??=
    [
        new(SimCoreTeardownStage.HostLeasesReleased, DemandNoHubTenancies, () => HubTenancyTally == 0),
        new(SimCoreTeardownStage.EventsDetached, _signals.Dispose, () => _signals.SnapOwnership().IsConverged),
        new(SimCoreTeardownStage.SessionDisposed, HaltSess, () => Session.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.TransitReset, () => { GenRestart.EmptyQueued(); PassageHolder.ResetSession(); }, () => PassageHolder.CaptureOwnership().IsSessionIdle),
        new(SimCoreTeardownStage.ActionsDisposed, ActHolder.Dispose, () => ActHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.MovementDisposed, MovementOwner.Dispose, () => MovementOwner.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.CharacterDisposed, ToonHolder.Dispose, () => ToonHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.InventoryDisposed, SatchelHolder.Dispose, () => SatchelHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.CommunicationDisposed, CommunicationHolder.Dispose, () => CommunicationHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.FellowshipDisposed, FellowshipHolder.Dispose, () => FellowshipHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.AllegianceDisposed, AllegianceHolder.Dispose, () => AllegianceHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.TradeDisposed, BarterHolder.Dispose, () => BarterHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.ContractsDisposed, ContractsHolder.Dispose, () => ContractsHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.JournalDisposed, JournalHolder.Dispose, () => JournalHolder.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.IdentityDisposed, AvatarIdentity.Dispose, () => AvatarIdentity.CaptureOwnership().IsConverged),
        new(SimCoreTeardownStage.EntityObjectsDisposed, EntityObjects.Dispose,
            () => EntityObjects.GrabOwnership().IsConverged && EntityObjects.Physics.CaptureOwnership().IsConverged),
    ];
        }
    }

    // The flags of every rung already climbed
    private SimCoreTeardownStage CompletedTeardownStages
    {
        get
        {
            var done = SimCoreTeardownStage.None;
            Rung[] ladder = Ladder;
            for (int idx = 0; idx < _disposeStage && idx < ladder.Length; ++idx)
                done |= ladder[idx].Flag;
            return done;
        }
    }

    public void HaltSess()
    {
        Session.Dispose();
        if (!Session.IsDisposalComplete)
            throw new InvalidOperationException("The Runtime session shutdown was deferred by a re-entrant callback");
    }

    public void Dispose()
    {
        lock (_lifespanLatch)
        {
            if (_destroyed || _teardownEmptyEngaged)
                return;
            _teardownAsked = true;
            _teardownEmptyEngaged = true;
        }

        List<Exception>? lateMisses = null;
        try
        {
            Rung[] ladder = Ladder;
            while (_disposeStage < TeardownStageCount)
            {
                bool converged = true;
                if (_disposeStage < ladder.Length)
                {
                    Rung rung = ladder[_disposeStage];
                    try
                    {
                        rung.Retire();
                        converged = rung.Converged();
                    }
                    catch (Exception problem)
                    {
                        converged = rung.Converged();
                        if (!converged)
                            throw;
                        (lateMisses ??= []).Add(problem);
                    }
                }

                if (!converged)
                    throw new InvalidOperationException($"SimCore teardown stage {_disposeStage} didn't complete");
                ++_disposeStage;
            }

            lock (_lifespanLatch)
                _destroyed = true;
        }
        finally
        {
            lock (_lifespanLatch)
                _teardownEmptyEngaged = false;
        }

        if (lateMisses is not null)
            throw new AggregateException("SimCore reached terminal ownership with callback failures", lateMisses);
    }

    private void DemandNoHubTenancies()
    {
        lock (_lifespanLatch)
        {
            if (_hubTenancies.Count is not 0)
                throw new InvalidOperationException("SimCore can't retire while host leases remain: " + string.Join(", ", _hubTenancies.Values));
        }
    }
}
