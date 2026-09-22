using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Targeting;
using MacAC.Mechanics.Arcana;
using MacAC.Sim.Actors;
using MacAC.Sim.Play;

namespace MacAC.Sim.Presence;

public sealed partial class DirectSimCoreDirectiveBridge
    : ISimCoreDirectives,
      ISimSessionDirectives,
      ISimSelectionDirectives,
      ISimFightingDirectives,
      ISimArcanaDirectives,
      ISimLocomotionDirectives,
      ISimCommsDirectives,
      ISimPortalDirectives,
      ISimStashStateDirectives,
      ISimArcanabookDirectives,
      ISimToonDirectives,
      ISimSocialDirectives,
      ISimFellowsDirectives,
      ISimAllegianceDirectives,
      ISimDealingTransport
{
    private sealed class Route(
        DirectSimCoreDirectiveBridge holder,
        RealmSession sess,
        SimEpochTicket gen)
        : IOnlineSessionDirectiveRouting
    {
        private bool _engaged;

        public void Arm()
        {
            if (_engaged)
                return;
            holder.Engage(sess, gen, this);
            _engaged = true;
        }

        public void Dispose()
        {
            holder.Disengage(this);
            _engaged = false;
        }
    }

    private readonly object _latch = new();
    private readonly SimCore _sim;
    private readonly ISimSessionDirectives _sessDirectives;
    private RealmSession? _session;
    private Route? _course;
    private SimEpochTicket _courseEpoch;

    public DirectSimCoreDirectiveBridge(
        SimCore runtime,
        ISimSessionDirectives sessionCommands)
    {
        _sim = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sessDirectives = sessionCommands
            ?? throw new ArgumentNullException(nameof(sessionCommands));
    }

    public ISimSessionDirectives Session => this;
    public ISimToonPickDirectives CharacterSelection =>
        _sim.Session;
    public ISimToonGenesisDirectives CharacterCreation =>
        _sim.Session;
    public ISimSelectionDirectives Selection => this;
    public ISimFightingDirectives Combat => this;
    public ISimArcanaDirectives Magic => this;
    public ISimLocomotionDirectives Movement => this;
    public ISimCommsDirectives Chat => this;
    public ISimPortalDirectives Portal => this;
    public ISimStashStateDirectives InventoryState => this;
    public ISimArcanabookDirectives Spellbook => this;
    public ISimToonDirectives Character => this;
    public ISimSocialDirectives Social => this;
    public ISimFellowsDirectives Fellowship => this;
    public ISimAllegianceDirectives Allegiance => this;

    public IOnlineSessionDirectiveRouting BuildCourse(RealmSession sess)
    {
        ArgumentNullException.ThrowIfNull(sess);
        return new Route(this, sess, _sim.Generation);
    }

    public SimSessionStartResult Start(
        SimEpochTicket anticipatedGen)
    {
        var earlier = _sim.Lifecycle.State;
        var outcome =
            _sessDirectives.Start(anticipatedGen);
        _sim.SignalDrain.WriteLifecycle(
            earlier,
            _sim.Lifecycle.State);
        return outcome;
    }

    public SimSessionStartResult Reconnect(
        SimEpochTicket anticipatedGen)
    {
        var earlier = _sim.Lifecycle.State;
        var outcome =
            _sessDirectives.Reconnect(anticipatedGen);
        _sim.SignalDrain.WriteLifecycle(
            earlier,
            _sim.Lifecycle.State);
        return outcome;
    }

    public SimTeardownAck Stop(
        SimEpochTicket anticipatedGen)
    {
        var earlier = _sim.Lifecycle.State;
        var outcome =
            _sessDirectives.Stop(anticipatedGen);
        _sim.SignalDrain.WriteLifecycle(
            earlier,
            _sim.Lifecycle.State);
        return outcome;
    }

    bool ISimDealingTransport.IsInRealm
    {
        get
        {
            lock (_latch)
            {
                return _course is not null
                    && _session is not null
                    && _courseEpoch == _sim.Generation
                    && _sim.Session.IsInWorld;
            }
        }
    }

    bool ISimDealingTransport.TryTransmitUse(
        uint srvOid,
        out uint series)
    {
        lock (_latch)
        {
            if (_course is null
                || _session is null
                || _courseEpoch != _sim.Generation
                || !_sim.Session.IsInWorld)
            {
                series = 0u;
                return false;
            }

            series = _session.UpcomingPlayActSeries();
            _session.TransmitPlayAct(
                InteractAsks.AssembleUse(series, srvOid));
            return true;
        }
    }

    bool ISimDealingTransport.TryTransmitLift(
        uint gearOid,
        uint destVesselIdent,
        int stance,
        out uint series)
    {
        lock (_latch)
        {
            if (_course is null
                || _session is null
                || _courseEpoch != _sim.Generation
                || !_sim.Session.IsInWorld)
            {
                series = 0u;
                return false;
            }

            series = _session.UpcomingPlayActSeries();
            _session.TransmitPlayAct(
                InteractAsks.AssembleChooseUp(
                    series,
                    gearOid,
                    destVesselIdent,
                    stance));
            return true;
        }
    }

    private SimDirectiveResult Unsupported(
        SimDirectiveDomain domain,
        int op,
        SimDirectiveStatus condition = SimDirectiveStatus.Unsupported,
        uint primaryObjectIdent = 0u)
    {
        _sim.SignalDrain.WriteDirective(
            domain,
            op,
            condition,
            primaryObjectIdent);
        return Result(condition, primaryObjectIdent);
    }

    private SimDirectiveResult Emit(
        SimDirectiveDomain domain,
        int op,
        SimDirectiveStatus condition,
        uint primaryObjectIdent = 0u,
        string? phrase = null)
    {
        _sim.SignalDrain.WriteDirective(
            domain,
            op,
            condition,
            primaryObjectIdent,
            phrase);
        return Result(condition, primaryObjectIdent);
    }

    private SimDirectiveStatus Latch(
        SimEpochTicket anticipatedGen,
        out RealmSession? sess)
    {
        lock (_latch)
        {
            if (anticipatedGen != _sim.Generation
                || (_course is not null
                    && _courseEpoch != anticipatedGen))
            {
                sess = null;
                return SimDirectiveStatus.StaleGeneration;
            }

            sess = _session;
            return _course is not null
                && sess is not null
                && _sim.Session.IsInWorld
                    ? SimDirectiveStatus.Accepted
                    : SimDirectiveStatus.Inactive;
        }
    }

    private SimDirectiveResult Result(
        SimDirectiveStatus condition,
        uint objectIdent = 0u) =>
        new(condition, _sim.Generation, objectIdent);

    private void Engage(
        RealmSession sess,
        SimEpochTicket gen,
        Route course)
    {
        lock (_latch)
        {
            if (_course is not null)
            {
                throw new InvalidOperationException(
                    "A direct Runtime command route is by now active");
            }
            _session = sess;
            _courseEpoch = gen;
            _course = course;
        }
    }

    private void Disengage(Route course)
    {
        lock (_latch)
        {
            if (!ReferenceEquals(_course, course))
                return;
            _course = null;
            _session = null;
            _courseEpoch = default;
        }
    }
}
