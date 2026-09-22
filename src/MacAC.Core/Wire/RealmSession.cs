using System.Diagnostics;
using System.Net;
using MacAC.Wire.Messages;
using MacAC.Wire.Packets;
using MacAC.Wire.Transport;

namespace MacAC.Wire;

/// <summary>Thrown when the server refuses the character the client tried to enter the world with.</summary>
public sealed class CharacterPickRefusedException(CharacterFault.ParsedDef problem)
    : InvalidOperationException($"The server rejected character entry with error 0x{problem.RawErrorCode:X8}.")
{
    public CharacterFault.ParsedDef Error { get; } = problem;
}

// The datagram carrier a session talks through; swapped for fakes in tests and wrapped by the loss
// shim
internal interface IRealmCarrier : IDisposable
{
    void Send(ReadOnlySpan<byte> datagram);
    void Send(IPEndPoint distant, ReadOnlySpan<byte> datagram);
    int Take(Span<byte> dest, TimeSpan timeout, out IPEndPoint? from);
    ValueTask<InboundVerdict> TakeAsync(Memory<byte> dest, CancellationToken abortTicket);
}

internal sealed class WireClientCarrier(IPEndPoint distant) : IRealmCarrier
{
    private readonly WireClient _slot = new(distant);

    public void Send(ReadOnlySpan<byte> datagram) => _slot.Send(datagram);
    public void Send(IPEndPoint endpoint, ReadOnlySpan<byte> datagram) => _slot.Send(endpoint, datagram);
    public int Take(Span<byte> dest, TimeSpan timeout, out IPEndPoint? from) => _slot.Receive(dest, timeout, out from);
    public ValueTask<InboundVerdict> TakeAsync(Memory<byte> dest, CancellationToken abortTicket) => _slot.AcceptAsync(dest, abortTicket);
    public void Dispose() => _slot.Dispose();
}

public sealed partial class RealmSession : IDisposable
{
    public enum State
    {
        Disconnected,
        Handshaking,
        InCharacterSelect,
        EnteringWorld,
        InWorld,
        Failed,
    }

    private enum GenesisAwaiting
    {
        None,
        Restore,
        Create,
    }

    internal const double LinkResponseReattemptSecs = 0.333333333;

    /// <summary>
    /// How long to wait for the server to answer a SigninRequest before sending it again. Slower
    /// than the ConnectResponse retry because this one goes to the login port, where a burst would
    /// look like a sign-in flood rather than a retransmission.
    /// </summary>
    internal const double SigninReattemptSecs = 1.0;

    /// <summary>
    /// The gap between SigninRequest sends. Settable so a test can exercise the retry without
    /// waiting on the wall clock.
    /// </summary>
    internal TimeSpan SigninReattemptDelay { get; set; } = TimeSpan.FromSeconds(SigninReattemptSecs);
    internal const int UpperIncomingDatagramOctets = ushort.MaxValue;

    private static readonly bool PrintOpcodesTurnedOn = Environment.GetEnvironmentVariable("MACAC_DUMP_OPCODES") == "1";
    private static readonly bool PrintLooksTurnedOn = Environment.GetEnvironmentVariable("MACAC_DUMP_APPEARANCE") == "1";

    private readonly IRealmCarrier _net;
    private readonly IPEndPoint _signinEndpoint;
    private readonly IPEndPoint _linkEndpoint;
    private readonly FragmentStitcher _assembler = new();
    private readonly HashSet<uint> _unhandledObserved = [];

    private ReliableLink? _conveyance;
    private bool _conveyanceNegotiated;
    private bool _handshakeConfirmed;
    private ushort _sessClientIdent;
    private ushort _sessIteration;

    private bool _blobVerifyDone;
    private bool _blobInterrogationReceived;
    private GenesisAwaiting _genesisAwaiting = GenesisAwaiting.None;
    private bool _warnedStrayGenesisReply;
    private CharacterFault.ParsedDef? _previousToonPickProblem;

    private long _previousIncomingPacketBeats = Stopwatch.GetTimestamp();
    private long _previousPingReqBeats;
    private long _previousPingRoundTripBitset = BitConverter.DoubleToInt64Bits(double.NaN);

    private ushort _instSeries;
    private ushort _srvControlSeries;
    private ushort _warpSeries;
    private ushort _forceLocusSeries;
    private uint _engagedToonIdent;
    private uint _playActSeries;
    private int _toonLogOffConfirmed;
    private int _teardownBegun;
    private bool _setPhaseHexDumped;

    public RealmSession(IPEndPoint srvSignin)
        : this(srvSignin, static endpoint => LossyLinkShim.EncloseIfConfigured(new WireClientCarrier(endpoint)))
    {
    }

    internal RealmSession(IPEndPoint srvSignin, IRealmCarrier conveyance)
        : this(srvSignin, _ => conveyance)
    {
    }

    internal RealmSession(IPEndPoint serverLogin, Func<IPEndPoint, IRealmCarrier> conveyanceMaker)
    {
        ArgumentNullException.ThrowIfNull(serverLogin);
        ArgumentNullException.ThrowIfNull(conveyanceMaker);
        if (serverLogin.Port == ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(serverLogin), "The login endpoint must leave room for the adjacent connect port");

        _signinEndpoint = serverLogin;
        _linkEndpoint = new IPEndPoint(serverLogin.Address, serverLogin.Port + 1);
        _net = conveyanceMaker(serverLogin) ?? throw new InvalidOperationException("The session transport factory returned null");

        PlaySignals.Register(GameEventKind.SetTurbineChatChannels, parcel =>
        {
            if (GroupTurbineCommsLanes.TryParse(parcel.Payload.Span) is { } lanes)
                TurbineChannelsReceived?.Invoke(lanes);
        });
        PlaySignals.Register(GameEventKind.PingResponse, parcel =>
        {
            if (Messages.PlaySignals.DecodePingResponse(parcel.Payload.Span))
                CapturePingResponse(Stopwatch.GetTimestamp());
        });
    }

    public GameEventRouter PlaySignals { get; } = new();

    public State LatestPhase { get; private set; } = State.Disconnected;

    /// <summary>Raised on every state change.</summary>
    public event Action<State>? StateChanged;

    public event Action<ConnectionHeadway>? ConnectionProgressChanged;

    public ConnectionHeadway ConnectionHeadway { get; private set; }

    public DddVersions? BlobVersions { get; set; }

    public ToonRoster.ParsedUnit? Characters { get; private set; }

    public RealmName.Parsed? SrvDetails { get; private set; }

    public double PreviousSrvMomentBeats { get; private set; }

    public event Action<double>? ServerTimeUpdated;

    internal ReliableLink? Conveyance => _conveyance;

    internal (Func<long> GetTimestamp, long Frequency)? ConveyanceTimerSrc { get; set; }

    public ushort InstanceSequence => _instSeries;
    public ushort ServerControlSequence => _srvControlSeries;
    public ushort TeleportSequence => _warpSeries;
    public ushort ForcePositionSequence => _forceLocusSeries;

    /// <summary>The kinetic layer reports the timestamps it accepted so outbound moves quote them.</summary>
    public void BroadcastApprovedOwnKineticsTimestamps(ushort inst, ushort srvControlledRelocate, ushort warp, ushort forceLocus)
    {
        _instSeries = inst;
        _srvControlSeries = srvControlledRelocate;
        _warpSeries = warp;
        _forceLocusSeries = forceLocus;
    }

    public LinkStatusFrame LinkStatus
    {
        get
        {
            return AssembleConnectCondition(
        LatestPhase,
        Volatile.Read(ref _previousIncomingPacketBeats),
        Stopwatch.GetTimestamp(),
        Stopwatch.Frequency,
        PingRoundTripSecs,
        _conveyance?.PacketLossPercentage ?? 0d);
        }
    }

    internal double? PingRoundTripSecs
    {
        get
        {
            double secs = BitConverter.Int64BitsToDouble(Volatile.Read(ref _previousPingRoundTripBitset));
            return double.IsFinite(secs) && secs >= 0d ? secs : null;
        }
    }

    internal static LinkStatusFrame AssembleConnectCondition(State phase, long previousIncomingPacketBeats, long instantBeats, long frequency, double? roundTripSecs = null, double packetLossPercentage = 0d)
    {
        bool online = phase is not (State.Disconnected or State.Failed);
        if (!online || frequency <= 0)
            return LinkStatusFrame.Disconnected;
        long quiet = Math.Max(0, instantBeats - previousIncomingPacketBeats);
        return new LinkStatusFrame(true, quiet / (double)frequency, packetLossPercentage, roundTripSecs);
    }

    private void CapturePingResponse(long instantBeats)
    {
        long asked = Interlocked.Exchange(ref _previousPingReqBeats, 0L);
        if (asked <= 0L || instantBeats < asked || Stopwatch.Frequency <= 0)
            return;
        Volatile.Write(ref _previousPingRoundTripBitset, BitConverter.DoubleToInt64Bits((instantBeats - asked) / (double)Stopwatch.Frequency));
    }

    private void Transition(State upcoming)
    {
        if (LatestPhase == upcoming)
            return;
        LatestPhase = upcoming;
        StateChanged?.Invoke(upcoming);
    }

    // A terminal data-check verdict sticks until a fresh connect attempt
    private void AssignConnectionHeadway(ConnectionHeadway headway)
    {
        if (ConnectionHeadway.Phase is LinkPhase.Unsupported or LinkPhase.Failed && headway.Phase != LinkPhase.Connecting)
            return;
        ConnectionHeadway = headway;
        ConnectionProgressChanged?.Invoke(headway);
    }

    private void HurlIfBlobVerifyFailed()
    {
        switch (ConnectionHeadway.Phase)
        {
            case LinkPhase.Unsupported:
                Transition(State.Failed);
                throw new UnknownDataUpdateException();
            case LinkPhase.Failed:
                Transition(State.Failed);
                throw new InvalidDataException(ConnectionHeadway.Error);
        }
    }
}
