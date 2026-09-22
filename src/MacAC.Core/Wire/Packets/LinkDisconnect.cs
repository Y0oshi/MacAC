namespace MacAC.Wire.Packets;

/// <summary>The bare Disconnect datagram: header only, cleartext checksum.</summary>
public static class LinkDisconnect
{
    public static byte[] Build(ushort networkIdent, ushort iteration)
    {
        return DatagramCodec.Serialize(
            new DatagramHeader { Flags = DatagramHeaderFlags.Disconnect, Id = networkIdent, Iteration = iteration },
            ReadOnlySpan<byte>.Empty,
            outgoingIsaac: null);
    }
}
