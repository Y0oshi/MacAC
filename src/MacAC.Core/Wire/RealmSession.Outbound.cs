using System.Buffers.Binary;
using MacAC.Wire.Messages;
using MacAC.Wire.Transport;

namespace MacAC.Wire;

public sealed partial class RealmSession
{
    internal Action<byte[]>? PlayActGrab { get; set; }

    internal Action<byte[], GameQueueGroup>? PlayMsgGrab { get; set; }

    public uint UpcomingPlayActSeries() => ++_playActSeries;

    public void TransmitPlayAct(byte[] playActCorpus)
    {
        if (PlayActGrab is { } grab)
            grab(playActCorpus);
        else
            TransmitPlayMsg(playActCorpus);
    }

    public void ReqConnectConditionPing()
    {
        Volatile.Write(ref _previousPingReqBeats, System.Diagnostics.Stopwatch.GetTimestamp());
        Act(SocialMoves.AssemblePingReq);
    }

    // Builds a game action with the next sequence number and sends it
    private void Act(Func<uint, byte[]> assemble) => TransmitPlayAct(assemble(UpcomingPlayActSeries()));

    private void TransmitPlayMsg(byte[] corpus) => TransmitPlayMsg(corpus, GameQueueGroup.UIQueue);

    private void TransmitControlMsg(byte[] corpus) => TransmitPlayMsg(corpus, GameQueueGroup.ControlQueue);

    private void TransmitPlayMsg(byte[] corpus, GameQueueGroup fifo)
    {
        if (PlayMsgGrab is { } grab)
        {
            grab(corpus, fifo);
            return;
        }
        if (WireTelemetry.SensorNet)
            InspectNetTraceOutgoing(corpus, fifo);
        try
        {
            ReliableLink conveyance = _conveyance
                ?? throw new InvalidOperationException("reliable send prior to transport negotiation - Connect() must seed ISAAC first");
            conveyance.Outgoing.TransmitPlayMsg(corpus, fifo);
        }
        catch (Exception exc) when (InspectNetTraceOutgoingFlaw(exc))
        {
            throw;
        }
        if (WireTelemetry.SensorNet)
            Interlocked.Increment(ref _sensorTransmitPane);
    }

    private void InspectNetTraceOutgoing(byte[] corpus, GameQueueGroup fifo)
    {
        uint op = corpus.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(corpus) : 0u;
        string specifics = string.Empty;
        if (op == 0xF7B1 && corpus.Length >= 12)
        {
            uint gseq = BinaryPrimitives.ReadUInt32LittleEndian(corpus.AsSpan(4));
            uint act = BinaryPrimitives.ReadUInt32LittleEndian(corpus.AsSpan(8));
            specifics = $" act=0x{act:X4} gseq={gseq}";
        }
        Console.WriteLine(
            $"[net-out] op=0x{op:X4}{specifics} q={fifo}"
            + $" fseq={_conveyance?.Outgoing.FragmentSeries ?? 0}"
            + $" pseq={_conveyance?.Outgoing.GlimpseUpcomingPacketSeries ?? 0}"
            + $" len={corpus.Length}"
            + $" tid={Environment.CurrentManagedThreadId} st={LatestPhase}");
    }

    // Exception filter: logs under the probe and never handles
    private bool InspectNetTraceOutgoingFlaw(Exception exc)
    {
        if (WireTelemetry.SensorNet)
            Console.WriteLine($"[net-out-EX] {exc.GetType().Name}: {exc.Message} tid={Environment.CurrentManagedThreadId} st={LatestPhase}");
        return false;
    }
}
