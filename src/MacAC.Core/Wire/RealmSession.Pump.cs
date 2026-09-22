using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading.Channels;
using MacAC.Wire.Messages;
using MacAC.Wire.Packets;
using MacAC.Wire.Transport;

namespace MacAC.Wire;

public sealed partial class RealmSession
{
    // A datagram in a pool-rented buffer; whoever finishes with it returns the buffer
    internal readonly record struct PooledInbound(byte[] Buffer, int Length)
    {
        public ReadOnlyMemory<byte> Memory => Buffer.AsMemory(0, Length);
    }

    private static readonly long IncomingAllowanceBeats = Stopwatch.Frequency / 1000 * 4;

    private readonly Channel<PooledInbound> _incomingFifo =
        Channel.CreateUnbounded<PooledInbound>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _netAbort = new();
    private Task? _netTakeTask;

    public int Tick()
    {
        int processed = 0;
        bool allowanceBroke = false;
        long begin = Stopwatch.GetTimestamp();
        while (_incomingFifo.Reader.TryRead(out PooledInbound datagram))
        {
            if (WireTelemetry.SensorNet)
                Interlocked.Decrement(ref _sensorIncomingZDepth);
            try
            {
                Absorb(datagram.Memory);
            }
            finally
            {
                YieldIncomingDatagram(datagram);
            }
            ++processed;
            if (IncomingAllowanceExceeded(LatestPhase, begin, Stopwatch.GetTimestamp(), IncomingAllowanceBeats))
            {
                allowanceBroke = true;
                break;
            }
        }
        if (WireTelemetry.SensorNet)
            InspectNetBeatCadence(begin, processed, allowanceBroke);
        // A deferred inbound tail must never hold back a due resend, so the sweep is unconditional.
        SweepConveyance();
        return processed;
    }

    internal static bool IncomingAllowanceExceeded(State phase, long beginBeats, long instantBeats, long allowanceBeats) =>
        phase == State.InWorld && instantBeats - beginBeats >= allowanceBeats;

    private void SweepConveyance()
    {
        if (_conveyanceNegotiated)
            _conveyance?.Sweep();
    }

    private void SecureNetTakeLoopBegun() => _netTakeTask ??= NetReceiveLoopAsync();

    private async Task NetReceiveLoopAsync()
    {
        byte[] temp = ArrayPool<byte>.Shared.Rent(UpperIncomingDatagramOctets);
        try
        {
            while (!_netAbort.Token.IsCancellationRequested)
            {
                byte[]? duplicate = null;
                bool handedOff = false;
                try
                {
                    var got = await _net.TakeAsync(temp.AsMemory(0, UpperIncomingDatagramOctets), _netAbort.Token).ConfigureAwait(false);
                    duplicate = ArrayPool<byte>.Shared.Rent(got.Length);
                    temp.AsSpan(0, got.Length).CopyTo(duplicate);
                    handedOff = _incomingFifo.Writer.TryWrite(new PooledInbound(duplicate, got.Length));
                    if (handedOff && WireTelemetry.SensorNet)
                        Interlocked.Increment(ref _sensorIncomingZDepth);
                }
                catch (System.Net.Sockets.SocketException exc)
                {
                    // Recoverable (a stale peer's ICMP unreachable, for one): log and keep reading.
                    Console.Error.WriteLine($"[net] receive error (continuing): {exc.SocketErrorCode} {exc.Message}");
                }
                finally
                {
                    if (!handedOff && duplicate is not null)
                        ArrayPool<byte>.Shared.Return(duplicate);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (ObjectDisposedException)
        {
            // socket closed before the loop noticed the cancel
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
            _incomingFifo.Writer.TryComplete();
        }
    }

    // Synchronous receive used before the background loop starts; null on timeout
    private PooledInbound? TakeBlocking(TimeSpan timeout)
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(UpperIncomingDatagramOctets);
        try
        {
            int len = _net.Take(buf.AsSpan(0, UpperIncomingDatagramOctets), timeout, out _);
            if (len >= 0)
                return new PooledInbound(buf, len);
            ArrayPool<byte>.Shared.Return(buf);
            return null;
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buf);
            throw;
        }
    }

    private static void YieldIncomingDatagram(PooledInbound datagram) => ArrayPool<byte>.Shared.Return(datagram.Buffer);

    // One blocking receive-and-process; the opcodes seen come back for handshake polling
    private bool PumpOnce(out List<uint> opcodes)
    {
        opcodes = [];
        if (TakeBlocking(TimeSpan.FromMilliseconds(250)) is not { } datagram)
            return false;
        try
        {
            Absorb(datagram.Memory, opcodes);
            return true;
        }
        finally
        {
            YieldIncomingDatagram(datagram);
        }
    }

    // Gate + bookkeeping + dispatch for one datagram
    private void Absorb(ReadOnlyMemory<byte> octets, List<uint>? opcodes = null, bool relayRealmSignals = true)
    {
        if (!DatagramCodec.TryDecodeBorrowed(octets, out LeasedDatagram packet, out uint preambleDigest, out uint cargoDigest, out _))
            return;

        var preamble = packet.Header;
        bool encrypted = preamble.HasFlag(DatagramHeaderFlags.EncryptedChecksum);

        if (preamble.Sequence is not 0 && _conveyance is { } latch)
        {
            var admission = latch.Inbound.Admit(preamble.Sequence, encrypted);
            if (admission.Drop)
                return;
            if (!DatagramCodec.VerifyChecksum(in preamble, preambleDigest, cargoDigest, admission.VerifyKey))
            {
                latch.Stats.ChecksumMisses++;
                if (encrypted)
                    latch.Inbound.ReparkTag(preamble.Sequence, admission.VerifyKey!.Value, admission.VerifyKeyDrawOrder);
                return;
            }
        }
        else if (encrypted || !DatagramCodec.VerifyChecksum(in preamble, preambleDigest, cargoDigest, isaacTag: null))
        {
            return;
        }

        Volatile.Write(ref _previousIncomingPacketBeats, Stopwatch.GetTimestamp());
        if (_conveyance is { } counted)
            counted.Stats.PacketsReceived++;

        if (!_handshakeConfirmed && _conveyanceNegotiated && !preamble.HasFlag(DatagramHeaderFlags.ConnectRequest))
        {
            _handshakeConfirmed = true;
            AssignConnectionHeadway(new(LinkPhase.CheckingData));
        }

        if (_conveyance is { } conveyance)
            FoldConveyancePreambles(conveyance, in preamble, packet.Optional, encrypted);

        if (preamble.HasFlag(DatagramHeaderFlags.TimeSync) && packet.Optional.TimeSync > 0)
        {
            PreviousSrvMomentBeats = packet.Optional.TimeSync;
            ServerTimeUpdated?.Invoke(packet.Optional.TimeSync);
        }

        foreach (LeasedFragment fragment in packet.Fragments)
        {
            if (!_assembler.TryIngest(fragment, out ReadOnlyMemory<byte> msg, out _) || msg.Length < 4)
                continue;

            var corpus = msg.Span;
            uint opcode = BinaryPrimitives.ReadUInt32LittleEndian(corpus);
            opcodes?.Add(opcode);

            if (CharacterExit.IsAck(corpus))
            {
                Interlocked.Exchange(ref _toonLogOffConfirmed, 1);
                continue;
            }
            if (relayRealmSignals)
                Relay(opcode, corpus, msg);
        }
    }

    // Acks, NAKs and rejects ride in the optional header of any datagram
    private static void FoldConveyancePreambles(ReliableLink conveyance, in DatagramHeader preamble, LeasedHeaderExtras optional, bool encrypted)
    {
        if (preamble.HasFlag(DatagramHeaderFlags.RequestRetransmit) && optional.RetransmitRequestCount > 0)
            conveyance.Outgoing.OnRetransmitReq(optional.RetransmitRequestBytes.Span, optional.RetransmitRequestCount);

        if (preamble.HasFlag(DatagramHeaderFlags.RejectRetransmit))
        {
            conveyance.Stats.RejectsReceived++;
            if (optional.RejectRetransmitCount > 0)
                conveyance.Inbound.OnRejectRetransmit(optional.RejectRetransmitBytes.Span, optional.RejectRetransmitCount);
            if (preamble.Sequence is not 0 && !encrypted)
                conveyance.Inbound.OnCleartextRejectSeries(preamble.Sequence);
        }

        if (preamble.HasFlag(DatagramHeaderFlags.AckSequence))
            conveyance.Outgoing.OnAckSeries(optional.AckSequence);
    }
}
