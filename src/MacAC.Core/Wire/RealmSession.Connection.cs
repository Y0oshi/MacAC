using System.Buffers.Binary;
using MacAC.Wire.Cryptography;
using MacAC.Wire.Packets;
using MacAC.Wire.Transport;

namespace MacAC.Wire;

public sealed partial class RealmSession
{
    // State of one Connect() in progress
    private sealed class Handshake(DateTime deadline)
    {
        public DateTime Deadline { get; } = deadline;

        /// <summary>When this attempt started, so a timeout can say how long it actually waited.</summary>
        public DateTime Began { get; } = DateTime.UtcNow;

        /// <summary>
        /// The SigninRequest datagram as sent, kept so it can be sent again. UDP drops it as readily
        /// as any other datagram, and until the server answers there is nothing else to go on.
        /// </summary>
        public byte[]? Signin { get; set; }

        public DateTime SigninAgainAt { get; set; }

        /// <summary>How many times the SigninRequest has gone out, for the timeout to report.</summary>
        public int SigninAttempts { get; set; }

        // The sealed ConnectResponse once the ConnectRequest has been seen
        public byte[]? Response { get; set; }

        public DateTime ResponseNotPrior { get; set; }
        public bool ResponseSent { get; set; }
        public long ResponseSentAt { get; set; }

        /// <summary>Drops the credential-bearing datagram as soon as the handshake is over with.</summary>
        public void ForgetSignin()
        {
            if (Signin is { } sent)
                Array.Clear(sent);
            Signin = null;
        }
    }

    private const int HandshakeDatagramsPerSample = 64;

    private Handshake? _handshake;

    public void Connect(string acct, string password, TimeSpan? timeout = null)
    {
        CommenceLink(acct, password, timeout);
        while (!SampleLink())
            Thread.Sleep(1);
    }

    public void CommenceLink(string acct, string password, TimeSpan? timeout = null)
    {
        if (_handshake is not null)
            throw new InvalidOperationException("A connection attempt is by now active");

        _blobVerifyDone = false;
        _blobInterrogationReceived = false;
        _handshakeConfirmed = false;
        Characters = null;
        _handshake = new Handshake(DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10)));
        AssignConnectionHeadway(new(LinkPhase.Connecting));
        Transition(State.Handshaking);

        uint stamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] signin = SigninRequest.Build(acct, password, stamp);
        byte[] datagram = DatagramCodec.Serialize(
            new DatagramHeader { Flags = DatagramHeaderFlags.LoginRequest }, signin, null);
        _handshake.Signin = datagram;
        _handshake.SigninAttempts = 1;
        _handshake.SigninAgainAt = DateTime.UtcNow + SigninReattemptDelay;
        _net.Send(datagram);
    }

    public bool SampleLink()
    {
        Handshake attempt = _handshake ?? throw new InvalidOperationException("No connection attempt is active");
        try
        {
            // Nothing has answered the SigninRequest yet, and it may simply have been dropped.
            if (attempt.Response is null
                && attempt.Signin is { } signin
                && DateTime.UtcNow >= attempt.SigninAgainAt)
            {
                _net.Send(signin);
                attempt.SigninAttempts++;
                attempt.SigninAgainAt = DateTime.UtcNow + SigninReattemptDelay;
            }

            for (int num = 0; num < HandshakeDatagramsPerSample; ++num)
            {
                if (attempt.Response is { } response && !attempt.ResponseSent)
                {
                    if (DateTime.UtcNow < attempt.ResponseNotPrior)
                        break;
                    _net.Send(_linkEndpoint, response);
                    attempt.ResponseSent = true;
                    attempt.ResponseSentAt = _conveyance!.Clock.GetTimestamp();
                }

                if (TakeBlocking(TimeSpan.Zero) is not { } datagram)
                    break;
                try
                {
                    if (attempt.Response is null)
                        AdmitLinkReq(datagram.Memory, attempt);
                    else
                        Absorb(datagram.Memory);
                }
                finally
                {
                    YieldIncomingDatagram(datagram);
                }
                HurlIfBlobVerifyFailed();
            }

            if (attempt.ResponseSent)
            {
                // Keep re-sending ConnectResponse until the server proves it heard us.
                LinkClock timer = _conveyance!.Clock;
                long reattemptFollowing = (long)Math.Round(LinkResponseReattemptSecs * timer.Frequency);
                if (!_handshakeConfirmed && timer.GetTimestamp() - attempt.ResponseSentAt > reattemptFollowing)
                {
                    _net.Send(_linkEndpoint, attempt.Response!);
                    attempt.ResponseSentAt = timer.GetTimestamp();
                }
                SweepConveyance();
            }

            if (Characters is not null && _blobVerifyDone)
            {
                attempt.ForgetSignin();
                _handshake = null;
                Transition(State.InCharacterSelect);
                AssignConnectionHeadway(new(LinkPhase.Ready));
                return true;
            }

            if (DateTime.UtcNow >= attempt.Deadline)
            {
                // Say which step stalled and how hard it was tried; "not received" on its own gives
                // nobody a way to tell a dropped datagram from a server that never answers.
                string stalled = attempt.Response is null
                    ? $"the server did not answer the SigninRequest ({attempt.SigninAttempts} sent)"
                    : Characters is null
                        ? "the character roster did not arrive"
                        : "the server did not complete the game-data check";
                attempt.ForgetSignin();
                throw new TimeoutException(
                    $"Logging in timed out after {(DateTime.UtcNow - attempt.Began).TotalSeconds:F1}s: {stalled}.");
            }
            return false;
        }
        catch (Exception problem)
        {
            attempt.ForgetSignin();
            _handshake = null;
            AssignConnectionHeadway(new(LinkPhase.Failed, problem.Message));
            Transition(State.Failed);
            throw;
        }
    }

    // Seeds both ISAAC streams from a cleartext ConnectRequest and prepares the cookie echo
    private void AdmitLinkReq(ReadOnlyMemory<byte> octets, Handshake attempt)
    {
        if (!DatagramCodec.TryDecodeBorrowed(octets, out LeasedDatagram decoded, out uint preambleDigest, out uint cargoDigest, out _))
            return;
        var preamble = decoded.Header;
        if (preamble.HasFlag(DatagramHeaderFlags.EncryptedChecksum)
            || !DatagramCodec.VerifyChecksum(in preamble, preambleDigest, cargoDigest, isaacTag: null)
            || !preamble.HasFlag(DatagramHeaderFlags.ConnectRequest))

            return;

        var extras = decoded.Optional;
        Span<byte> srvSeed = stackalloc byte[4];
        Span<byte> clientSeed = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(srvSeed, extras.ConnectRequestServerSeed);
        BinaryPrimitives.WriteUInt32LittleEndian(clientSeed, extras.ConnectRequestClientSeed);

        _sessClientIdent = (ushort)extras.ConnectRequestClientId;
        _sessIteration = preamble.Iteration;
        _conveyance = new ReliableLink(
            new IsaacStream(clientSeed),
            new IsaacStream(srvSeed),
            _sessClientIdent,
            _sessIteration,
            datagram => _net.Send(datagram),
            timer: ConveyanceTimerSrc is { } src ? new LinkClock(src.GetTimestamp, src.Frequency) : null,
            assembler: _assembler);
        _conveyanceNegotiated = true;
        PreviousSrvMomentBeats = extras.ConnectRequestServerTime;
        ServerTimeUpdated?.Invoke(extras.ConnectRequestServerTime);

        byte[] cookie = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(cookie, extras.ConnectRequestCookie);
        attempt.Response = DatagramCodec.Serialize(new DatagramHeader { Sequence = 1, Flags = DatagramHeaderFlags.ConnectResponse, Id = 0 }, cookie, null);
        attempt.ResponseNotPrior = DateTime.UtcNow.AddMilliseconds(200);
    }
}
