using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Realm;

public sealed partial class SimRealmCrossingLedger : ISimPortalLens
{
    public static readonly TimeSpan CanonPauseCueDelay = TimeSpan.FromSeconds(5);

    public const double CanonSignoutGripSecs = 3.0;

    public const double CanonAvatarKillerAdditionalGripSecs = 20.0;

    private readonly Action<string> _trace;
    private readonly Dictionary<ushort, SimWarpDestination> _buffered = [];
    private readonly Dictionary<long, Mirror> _mirrors = [];
    private SimPortalCapture _capture = SimPortalCapture.Idle;
    private long _generations;
    private Warp _teleport;
    private Hold _signout;

    public SimRealmCrossingLedger(Action<string>? trace = null)
    {
        _trace = trace ?? (static _ => { });
    }

    public SimPortalCapture Snapshot => _capture;
    public SimRealmCrossingHoldingCapture Ownership => CaptureOwnership();
    public bool IsRealmSimulationOnHand => _capture.WorldSimulationAvailable;
    public bool IsWarpEngaged => _teleport.Active;
    public ushort EngagedWarpSeries => _teleport.Active ? _teleport.EngagedSeries : (ushort)0;
    public bool HasQueuedWarpBegin => _teleport.Pending;
    public bool HasApprovedWarpDest => _teleport.Held;
    public int BufferedWarpDestTally => _buffered.Count;
    public int ProbeMissTally { get; private set; }
    public Exception? PreviousProbeMiss { get; private set; }

    public SimRealmCrossingHoldingCapture CaptureOwnership()
    {
        int owed = 0;
        foreach (Mirror mirror in _mirrors.Values)
            owed = checked(owed + mirror.Snapshot.QueuedAcknowledgementTally);

        bool revealing = _capture.Generation is not 0
            && !_capture.Completed
            && !_capture.Cancelled;
        return new SimRealmCrossingHoldingCapture(
            _buffered.Count,
            _teleport.Pending ? 1 : 0,
            _teleport.Active ? 1 : 0,
            _teleport.Held ? 1 : 0,
            revealing ? 1 : 0,
            revealing && !_capture.IsReady ? 1 : 0,
            _mirrors.Count,
            owed,
            _signout.Stage != SimLogoutStage.None ? 1 : 0);
    }

    public void ResetSession()
    {
        if (_mirrors.Count is not 0)
        {
            var ownership = CaptureOwnership();
            throw new InvalidOperationException(
                "World transit can't reset while a host projection "
                + "still owns an acknowledgement suffix "
                + $"(hosts={ownership.HostProjectionCount}, "
                + $"pending={ownership.PendingHostAcknowledgementCount}).");
        }

        _capture = SimPortalCapture.Idle;
        _teleport = default;
        _buffered.Clear();
        _signout = default;
    }

    private static bool IsNewer(ushort latest, ushort incoming) =>
        MacAC.Mechanics.Kinetics.KineticStampGate.IsNewer(latest, incoming);

    private static bool IsInside(uint chamberIdent) => (chamberIdent & 0xFFFFu) >= 0x0100u;

    // The F751 teleport lifetime
    private struct Warp
    {
        public bool Started;
        public ushort LastSequence;
        public bool Pending;
        public ushort QueuedSeries;
        public bool Active;
        public ushort EngagedSeries;
        public bool Accepted;
        public bool Held;
        public SimWarpDestination Destination;

        public void Enqueue(ushort series)
        {
            Active = false;
            EngagedSeries = 0;
            Pending = true;
            QueuedSeries = series;
            Started = true;
            LastSequence = series;
            Drop();
        }

        public void Activate()
        {
            Active = true;
            EngagedSeries = QueuedSeries;
            Pending = false;
            QueuedSeries = 0;
            Drop();
        }

        public void Adopt(in SimWarpDestination dest)
        {
            Accepted = true;
            Held = true;
            Destination = dest;
        }

        public void Drop()
        {
            Accepted = false;
            Held = false;
            Destination = default;
        }

        public void End()
        {
            Active = false;
            EngagedSeries = 0;
            Pending = false;
            QueuedSeries = 0;
            Drop();
        }
    }

    // The retail logout hold: stage plus the seconds waited and owed
    private struct Hold
    {
        public SimLogoutStage Stage;
        public double Elapsed;
        public double Required;
    }

    // One presentation host mirroring a reveal generation, with the acknowledgement stages it still
    // owes the ledger
    private sealed class Mirror(SimRealmHarborMirrorTicket ticket)
    {
        public SimRealmHarborMirrorTicket Ticket { get; } = ticket;
        public SimRealmHarborAckStage Owed { get; set; } = SimRealmHarborAckStage.ProjectionRegistered;
        public bool Registered { get; set; }
        public bool SimulationReleased { get; set; }
        public bool ReservationReleased { get; set; }
        public bool Superseding { get; set; }

        public SimRealmHarborMirrorCapture Snapshot
        {
            get
            {
                return new(
            Ticket,
            Owed,
            Registered,
            SimulationReleased,
            ReservationReleased,
            Superseding);
            }
        }

        public bool Done(SimRealmHarborAckStage juncture)
        {
            return juncture switch
            {
                SimRealmHarborAckStage.ProjectionRegistered => Registered,
                SimRealmHarborAckStage.SimulationReleaseProjected => SimulationReleased,
                SimRealmHarborAckStage.DestinationReservationReleased => ReservationReleased,
                _ => false,
            };
        }

        // Adds juncture to the owed set unless already acknowledged
        public void Owe(SimRealmHarborAckStage juncture)
        {
            if (!Done(juncture))
                Owed |= juncture;
        }

        // A terminating host no longer has to finish acquiring a projection; it must instead release
        // everything it did acquire
        public void Cease(bool demandSimulationFree)
        {
            Owed &= ~SimRealmHarborAckStage.ProjectionRegistered;
            if (demandSimulationFree && !SimulationReleased)
                Owed |= SimRealmHarborAckStage.SimulationReleaseProjected;
            if (!ReservationReleased)
                Owed |= SimRealmHarborAckStage.DestinationReservationReleased;
            Owed |= SimRealmHarborAckStage.TerminalProjected;
        }

        public void Supersede()
        {
            Superseding = true;
            Owed &= ~(SimRealmHarborAckStage.ProjectionRegistered
                | SimRealmHarborAckStage.SimulationReleaseProjected);
            if (!ReservationReleased)
                Owed |= SimRealmHarborAckStage.DestinationReservationReleased;
            Owed |= SimRealmHarborAckStage.TerminalProjected;
        }
    }
}
