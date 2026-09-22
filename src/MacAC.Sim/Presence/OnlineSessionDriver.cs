using System.Net;
using System.Net.Sockets;
using MacAC.Mechanics.Genesis;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class OnlineSessionDriver : IDisposable, ISimOnlineSessionFramePhase, ISimToonPickDirectives, ISimToonGenesisDirectives
{
    private sealed class PickWireBinding : IDisposable
    {
        private RealmSession? _session;
        private readonly Action<ToonRoster.ParsedUnit> _lineup;
        private readonly Action _erase;
        private readonly Action<CharacterRevive.Parsed> _revert;
        private readonly Action<CharacterFault.ParsedDef> _problem;
        private readonly Action<RealmName.Parsed> _realmLabel;
        private readonly Action<GenesisVerdict.Parsed> _built;

        public PickWireBinding(
            RealmSession sess,
            Action<ToonRoster.ParsedUnit> lineup,
            Action erase,
            Action<CharacterRevive.Parsed> revert,
            Action<CharacterFault.ParsedDef> problem,
            Action<RealmName.Parsed> realmLabel,
            Action<GenesisVerdict.Parsed> built)
        {
            _session = sess;
            _lineup = lineup;
            _erase = erase;
            _revert = revert;
            _problem = problem;
            _realmLabel = realmLabel;
            _built = built;
            sess.CharacterListReceived += lineup;
            sess.CharacterDeleteAcknowledged += erase;
            sess.CharacterRestoreReceived += revert;
            sess.CharacterErrorReceived += problem;
            sess.ServerNameReceived += realmLabel;
            sess.CharacterCreateResponseReceived += built;
        }

        public bool IsDisposed => _session is null;

        public void Dispose()
        {
            var sess = Interlocked.Exchange(ref _session, null);
            if (sess is null)
                return;
            sess.CharacterListReceived -= _lineup;
            sess.CharacterDeleteAcknowledged -= _erase;
            sess.CharacterRestoreReceived -= _revert;
            sess.CharacterErrorReceived -= _problem;
            sess.ServerNameReceived -= _realmLabel;
            sess.CharacterCreateResponseReceived -= _built;
        }
    }

    private sealed class Scope(
        RealmSession sess,
        IOnlineSessionLifespanHarbor hub,
        SimEpochTicket gen)
    {
        private int _teardownJuncture;

        public RealmSession Session { get; } = sess;
        public IOnlineSessionLifespanHarbor Host { get; } = hub;
        public SimEpochTicket Generation { get; } = gen;
        public OnlineSessionBinding? Mapping { get; set; }
        public OnlineSessionConnectOptions? ConnectingKnobs { get; set; }
        public PickWireBinding? ToonPickMapping
        {
            get;
            set;
        }
        public bool HubAffixed { get; set; }
        public SimTeardownStage FinishedStages { get; private set; }

        public void EmptyTeardown(IOnlineSessionOps ops)
        {
            if (_teardownJuncture is 0)
            {
                try
                {
                    Mapping?.Dispose();
                    ToonPickMapping?.Dispose();
                }
                finally
                {
                    if (Mapping is null || Mapping.DirectivesDeactivated)
                        FinishedStages |= SimTeardownStage.CommandsInert;
                    if ((Mapping is null || Mapping.SignalsDetached)
                        && (ToonPickMapping is null
                            || ToonPickMapping.IsDisposed))

                        FinishedStages |= SimTeardownStage.InboundDetached;
                }
                _teardownJuncture = 1;
            }
            if (_teardownJuncture is 1)
            {
                ops.TeardownSess(Session);
                FinishedStages |= SimTeardownStage.TransportDisposed;
                _teardownJuncture = 2;
            }
            if (_teardownJuncture is 2)
            {
                if (HubAffixed)
                    Host.UnfastenSess(Session);
                _teardownJuncture = 3;
            }
            if (_teardownJuncture is 3)
            {
                Host.RewindSessPhase(Generation);
                FinishedStages |= SimTeardownStage.HostReset;
                _teardownJuncture = 4;
            }
        }

        public bool IsTeardownDone => _teardownJuncture is 4;
    }

    private enum QueuedKind
    {
        Stop,
        Reconnect,
        Dispose,
    }

    private sealed record QueuedOp(
        QueuedKind Kind,
        OnlineSessionConnectOptions? Options = null,
        IOnlineSessionLifespanHarbor? Host = null);

    private sealed record HarborReset(
        IOnlineSessionLifespanHarbor Host,
        SimEpochTicket Generation);

    private readonly object _latch = new();

    private readonly IOnlineSessionOps _ops;

    private readonly SimLinkLedger _connect = new();

    private Scope? _ambit;

    private Scope? _sunsetting;

    private HarborReset? _queuedHarborRestart;

    private QueuedOp? _queued;

    private int _depth;

    private bool _inRealm;

    private bool _teardownAsked;

    private bool _destroyed;

    private ulong _epoch;

    private SimTeardownStage _teardownJunctures;

    private int _createsSinceLineup;

    private OnlineSessionToonPick? _choose;

    private uint _upcomingSigninIdent;

    private Action<RealmSession>? _autoPersistTap;

    private Action<RealmSession>? _logoffDrainTap;

    public OnlineSessionDriver()
        : this(ProductionOnlineSessionOps.Instance, null)
    {
    }

    public OnlineSessionDriver(
        IOnlineSessionOps operations,
        TimeProvider? momentSupplier = null,
        GenesisOptions? chargenKnobs = null,
        Random? random = null)
    {
        _ops = operations ?? throw new ArgumentNullException(nameof(operations));
        ToonPickPhase = new SimToonPickLedger(
            momentSupplier);
        ToonCreationPhase = new SimToonGenesisLedger(
            chargenKnobs ?? GenesisOptions.Empty, random);
    }

    public SimToonPickLedger ToonPickPhase { get; }

    public ISimLinkLens Connection => _connect;

    public ISimToonPickLens ToonSelection =>
        ToonPickPhase.View;

    public SimToonGenesisLedger ToonCreationPhase { get; }

    public ISimToonGenesisLens CharacterCreation =>
        ToonCreationPhase.View;

    public RealmSession? LatestSession
    {
        get { lock (_latch) return _ambit?.Session; }
    }

    public bool IsInWorld
    {
        get { lock (_latch) return _inRealm; }
    }

    public ulong SessGen
    {
        get { lock (_latch) return _epoch; }
    }

    public SimEpochTicket Generation
    {
        get { lock (_latch) return new SimEpochTicket(_epoch); }
    }

    public uint UpcomingSigninToonIdent
    {
        get { lock (_latch) return _upcomingSigninIdent; }
    }

    public bool TrySetUpcomingSignin(uint toonIdent)
    {
        lock (_latch)
        {
            if (_destroyed
                || _teardownAsked
                || _ambit is null
                || toonIdent is 0u
                || !ToonPickPhase.View.TryGet(
                    toonIdent,
                    out SimToonPickEntry toon)
                || !toon.CanJoin)

                return false;
            _upcomingSigninIdent = toonIdent;
            return true;
        }
    }

    public bool WipeUpcomingSignin()
    {
        lock (_latch)
        {
            if (_destroyed)
                return false;
            _upcomingSigninIdent = 0u;
            return true;
        }
    }

    public OnlineSessionHoldingCapture CaptureOwnership()
    {
        lock (_latch)
        {
            return new OnlineSessionHoldingCapture(
                _destroyed,
                _teardownAsked,
                _inRealm,
                _ambit is not null,
                _sunsetting is not null,
                _queuedHarborRestart is not null,
                _queued is not null,
                _depth,
                _epoch,
                _teardownJunctures);
        }
    }

    public void Tick()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            if (_ambit is null || _depth is not 0)
                return;

            AtTopTier(() =>
            {
                Scope ambit = _ambit;
                ulong gen = _epoch;
                if (ambit.ConnectingKnobs is { } connectingKnobs)
                {
                    try
                    {
                        if (!_ops.SampleLink(ambit.Session))
                            return;
                        ambit.Session.ConnectionProgressChanged -= _connect.Apply;
                        ambit.ConnectingKnobs = null;
                        if (AmbitHolds(ambit, gen))
                            ConcludeBegin(ambit, connectingKnobs);
                    }
                    catch (Exception problem)
                    {
                        Exception reportedProblem = HaltFollowing(problem);
                        _connect.Fail(problem);
                        Console.Error.WriteLine($"live: connection failed: {reportedProblem}");
                    }
                    return;
                }
                try
                {
                    _ops.Tick(ambit.Session);
                    if (!AmbitHolds(ambit, gen))
                        return;
                    ToonPickPhase.SweepRevertCorrelation();
                }
                catch (Exception beatProblem)
                {
                    Exception problem = HaltFollowing(beatProblem);
                    if (ReferenceEquals(problem, beatProblem))
                        throw;
                    throw problem;
                }

                if (_inRealm)
                    ExecuteAutoPersistTap(ambit.Session);
            });
        }
    }

    public bool IsDisposalComplete
    {
        get { lock (_latch) return _destroyed; }
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _teardownAsked = true;
            if (_depth is not 0)
            {
                Queue(new QueuedOp(QueuedKind.Dispose));
                return;
            }

            AtTopTier(TeardownInstant);
        }
    }

    internal void ConfigureAutoPersistBeat(Action<RealmSession> hook)
    {
        _autoPersistTap = hook ?? throw new ArgumentNullException(nameof(hook));
    }

    internal void ConfigurePreLogoffDrain(Action<RealmSession> hook)
    {
        _logoffDrainTap = hook ?? throw new ArgumentNullException(nameof(hook));
    }

    private void ExecuteAutoPersistTap(RealmSession sess)
    {
        if (_autoPersistTap is not { } tap)
            return;
        try
        {
            tap(sess);
        }
        catch (Exception problem)
        {
            Console.Error.WriteLine(
                $"live: auto-save character-options tick failed: {problem.Message}");
        }
    }

    private void EmptySunsetting()
    {
        if (_sunsetting is not { } retired)
            return;
        retired.EmptyTeardown(_ops);
        if (retired.IsTeardownDone)
        {
            _teardownJunctures = retired.FinishedStages;
            _sunsetting = null;
        }
    }

    private void EmptyHarborRestart()
    {
        if (_queuedHarborRestart is not { } queued)
            return;
        queued.Host.RewindSessPhase(queued.Generation);
        _queuedHarborRestart = null;
    }

    private void Queue(QueuedOp op)
    {
        if (_queued?.Kind == QueuedKind.Dispose)
            return;
        _queued = op;
        ++_epoch;
        _inRealm = false;
        _ambit?.Mapping?.Dispose();
    }

    private T AtTopTier<T>(Func<T> op)
    {
        ++_depth;
        try
        {
            return op();
        }
        finally
        {
            --_depth;
            if (_depth is 0)
                EmptyFifo();
        }
    }

    private void AtTopTier(Action op)
    {
        AtTopTier(() =>
        {
            op();
            return true;
        });
    }

    private void EmptyFifo()
    {
        while (_queued is { } queued)
        {
            _queued = null;
            ++_depth;
            try
            {
                switch (queued.Kind)
                {
                    case QueuedKind.Stop:
                        HaltInstant();
                        break;
                    case QueuedKind.Reconnect:
                        _ = ReconnectInstant(queued.Options!, queued.Host!);
                        break;
                    case QueuedKind.Dispose:
                        TeardownInstant();
                        break;
                }
            }
            finally
            {
                --_depth;
            }
        }
    }

    private void TeardownInstant()
    {
        HaltInstant();
        ToonPickPhase.Dispose();
        ToonCreationPhase.Dispose();
        _destroyed = true;
    }

    private bool AmbitHolds(Scope ambit, ulong gen) =>
        ReferenceEquals(_ambit, ambit) && _epoch == gen;

    private void Live()
    {
        if (_teardownAsked || _destroyed)
            throw new ObjectDisposedException(nameof(OnlineSessionDriver));
    }
}
