using System.Diagnostics;
using System.Threading.Channels;
using MacAC.Wire.Messages;
using MacAC.Wire.Packets;
using MacAC.Wire.Transport;

namespace MacAC.Wire;

public sealed partial class RealmSession
{
    internal readonly record struct SessionTeardownPlan(bool RequestCharacterLogOff, bool SendTransportDisconnect);

    internal readonly record struct TeardownOutcome(
        bool CharacterLogOffSent,
        bool ConfirmationReceived,
        bool TransportDisconnectSent,
        Exception? CharacterLogOffError,
        Exception? TransportDisconnectError);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _teardownBegun, 1) is not 0)
            return;

        _genesisAwaiting = GenesisAwaiting.None;
        var plan = AssembleShutdownPlan(LatestPhase, _conveyanceNegotiated, _engagedToonIdent);
        Interlocked.Exchange(ref _toonLogOffConfirmed, 0);
        var verdict = PerformShutdownWire(
            plan,
            _engagedToonIdent,
            _sessClientIdent,
            _sessIteration,
            TransmitPlayMsg,
            PauseForToonLogOffAck,
            packet => _net.Send(packet),
            TimeSpan.FromSeconds(35));
        Report(verdict);

        _netAbort.Cancel();
        _netTakeTask?.GetAwaiter().GetResult();
        _incomingFifo.Writer.TryComplete();
        while (_incomingFifo.Reader.TryRead(out PooledInbound queued))
            YieldIncomingDatagram(queued);
        _netAbort.Dispose();

        if (WireTelemetry.SensorNet && _conveyance is { } connect)
            Console.WriteLine(FinalSensorStroke(connect));

        _conveyance?.Dispose();
        _net.Dispose();
        Transition(State.Disconnected);
    }

    internal static SessionTeardownPlan AssembleShutdownPlan(State phase, bool conveyanceNegotiated, uint engagedToonIdent)
    {
        return new(
        RequestCharacterLogOff: conveyanceNegotiated && phase == State.InWorld && engagedToonIdent is not 0,
        SendTransportDisconnect: conveyanceNegotiated);
    }

    // Runs the plan with injected sends so the sequencing is testable without a socket; failures are
    // captured, not thrown
    internal static TeardownOutcome PerformShutdownWire(
        SessionTeardownPlan plan,
        uint engagedToonIdent,
        ushort sessClientIdent,
        ushort sessIteration,
        Action<byte[]> transmitPlayMsg,
        Func<TimeSpan, bool> pauseForAck,
        Action<byte[]> transmitConveyanceDatagram,
        TimeSpan ackTimeout)
    {
        ArgumentNullException.ThrowIfNull(transmitPlayMsg);
        ArgumentNullException.ThrowIfNull(pauseForAck);
        ArgumentNullException.ThrowIfNull(transmitConveyanceDatagram);

        bool traceOffSent = false, confirmed = false, unlinkSent = false;
        Exception? traceOffProblem = null, unlinkProblem = null;

        if (plan.RequestCharacterLogOff)
        {
            try
            {
                transmitPlayMsg(CharacterExit.ConstructReqCorpus(engagedToonIdent));
                traceOffSent = true;
                confirmed = pauseForAck(ackTimeout);
            }
            catch (Exception problem)
            {
                traceOffProblem = problem;
            }
        }

        if (plan.SendTransportDisconnect)
        {
            try
            {
                transmitConveyanceDatagram(LinkDisconnect.Build(sessClientIdent, sessIteration));
                unlinkSent = true;
            }
            catch (Exception problem)
            {
                unlinkProblem = problem;
            }
        }

        return new TeardownOutcome(traceOffSent, confirmed, unlinkSent, traceOffProblem, unlinkProblem);
    }

    // Drains reader until the predicate confirms, the timeout passes, or the channel closes
    internal static bool PauseForToonLogOffAck<T>(
        ChannelReader<T> reader,
        TimeSpan timeout,
        Func<T, bool> procAndVerifyAck,
        Action<T>? free = null,
        Action? periodicJob = null,
        TimeSpan? periodicInterval = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(procAndVerifyAck);
        TimeSpan cadence = periodicInterval ?? TimeSpan.FromMilliseconds(25);
        if (periodicJob is not null && cadence <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(periodicInterval));

        using CancellationTokenSource overall = new CancellationTokenSource(timeout);
        long begun = Stopwatch.GetTimestamp();
        bool bounded = timeout >= TimeSpan.Zero;
        bool Over() => overall.IsCancellationRequested || (bounded && Stopwatch.GetElapsedTime(begun) >= timeout);

        try
        {
            while (!Over())
            {
                while (reader.TryRead(out T? gear))
                {
                    if (Over())
                    {
                        free?.Invoke(gear);
                        return false;
                    }

                    bool confirmed;
                    try
                    {
                        confirmed = procAndVerifyAck(gear);
                    }
                    finally
                    {
                        free?.Invoke(gear);
                    }
                    if (confirmed)
                        return true;
                }

                periodicJob?.Invoke();
                if (Over())
                    return false;

                bool readable;
                if (periodicJob is null)
                {
                    readable = reader.WaitToReadAsync(overall.Token).AsTask().GetAwaiter().GetResult();
                }
                else
                {
                    TimeSpan slice = cadence;
                    if (bounded)
                    {
                        TimeSpan left = timeout - Stopwatch.GetElapsedTime(begun);
                        if (left <= TimeSpan.Zero)
                            return false;
                        if (left < slice)
                            slice = left;
                    }

                    using CancellationTokenSource sliceSrc = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                    sliceSrc.CancelAfter(slice);
                    try
                    {
                        readable = reader.WaitToReadAsync(sliceSrc.Token).AsTask().GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (!Over())
                    {
                        continue; // only the slice expired; go round for periodic work
                    }
                }
                if (!readable)
                    return false;
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
        return false;
    }

    private void Report(in TeardownOutcome verdict)
    {
        if (verdict.CharacterLogOffSent)
        {
            Console.WriteLine($"[session] graceful logout requested character=0x{_engagedToonIdent:X8}");
            if (verdict.ConfirmationReceived)
                Console.WriteLine("[session] graceful logout confirmed");
            else if (verdict.CharacterLogOffError is null)
                Console.Error.WriteLine("[session] graceful logout confirmation timed out; disconnecting transport");
        }
        if (verdict.CharacterLogOffError is { } traceOffProblem)
            Console.Error.WriteLine($"[session] graceful logout failed: {traceOffProblem.Message}");
        if (verdict.TransportDisconnectError is { } unlinkProblem)
            Console.Error.WriteLine($"[session] transport disconnect failed: {unlinkProblem.Message}");
    }

    private static string FinalSensorStroke(ReliableLink connect)
    {
        LinkStats stats = connect.Stats;
        return $"[net-final] resends={stats.ResendsSent}"
            + $" nak-in={stats.NakReqsReceived}"
            + $" nak-out={stats.NaksSent}"
            + $" rej-in={stats.RejectsReceived}"
            + $" acks-out={stats.AcksSent}"
            + $" acks-in={stats.AcksConsumed}"
            + $" dup-drop={stats.IncomingDupsDropped}"
            + $" sanity-drop={stats.IncomingSanityDrops}"
            + $" cksum-fail={stats.ChecksumMisses}"
            + $" parked={stats.TagsShelved}"
            + $" reclaimed={stats.RejectWordsReclaimed}"
            + $" uncached-nak={stats.UncachedNakIdents}"
            + $" cache={stats.StashZDepth}"
            + $" nakset={connect.Inbound.NakTally}";
    }

    private bool PauseForToonLogOffAck(TimeSpan timeout)
    {
        return PauseForToonLogOffAck(
            _incomingFifo.Reader,
            timeout,
            datagram =>
            {
                Absorb(datagram.Memory, relayRealmSignals: false);
                SweepConveyance();
                return Volatile.Read(ref _toonLogOffConfirmed) is not 0;
            },
            YieldIncomingDatagram);
    }
}
