using MacAC.Wire.Packets;

namespace MacAC.Wire.Messages;

public static class GameFragment
{
    public const uint OutgoingFragmentIdent = 0x80000000u;

    public static WireFragment AssembleSingleFragment(uint fragmentSeries, GameQueueGroup fifo, ReadOnlySpan<byte> playMsgOctets)
    {
        return new(PreambleFor(fragmentSeries, fifo, playMsgOctets.Length), playMsgOctets.ToArray());
    }

    public static byte[] Serialize(in WireFragment fragment)
    {
        byte[] octets = new byte[WireFragmentHeader.Size + fragment.Payload.Length];
        fragment.Header.Pack(octets);
        fragment.Payload.CopyTo(octets.AsSpan(WireFragmentHeader.Size));
        return octets;
    }

    // Writes header + body into destination and returns the bytes used
    internal static int EmitSingleFragment(Span<byte> destination, uint fragmentSeries, GameQueueGroup fifo, ReadOnlySpan<byte> playMsgOctets)
    {
        var preamble = PreambleFor(fragmentSeries, fifo, playMsgOctets.Length);
        int wireDims = preamble.SumDims;
        if (destination.Length < wireDims)
            throw new ArgumentException($"destination has to be no fewer than {wireDims} bytes", nameof(destination));

        preamble.Pack(destination);
        playMsgOctets.CopyTo(destination.Slice(WireFragmentHeader.Size));
        return wireDims;
    }

    private static WireFragmentHeader PreambleFor(uint fragmentSeries, GameQueueGroup fifo, int corpusLen)
    {
        if (corpusLen > WireFragmentHeader.UpperFragmentBlobDims)
        {
            throw new ArgumentException(
                $"game message body ({corpusLen} bytes) exceeds single-fragment capacity "
                + $"({WireFragmentHeader.UpperFragmentBlobDims} bytes). Multi-fragment split TBD",
                "gameMessageBytes");
        }

        return new WireFragmentHeader
        {
            Sequence = fragmentSeries,
            Id = OutgoingFragmentIdent,
            Count = 1,
            SumDims = checked((ushort)(WireFragmentHeader.Size + corpusLen)),
            Index = 0,
            Queue = (ushort)fifo,
        };
    }
}

public enum GameQueueGroup : ushort
{
    InvalidQueue = 0x00,
    EventQueue = 0x01,
    ControlQueue = 0x02,
    WeenieQueue = 0x03,
    LoginQueue = 0x04,
    DatabaseQueue = 0x05,
    SecureControlQueue = 0x06,
    SecureWeenieQueue = 0x07,
    SecureLoginQueue = 0x08,
    UIQueue = 0x09,
    SmartboxQueue = 0x0A,
    ObserverQueue = 0x0B,
}
