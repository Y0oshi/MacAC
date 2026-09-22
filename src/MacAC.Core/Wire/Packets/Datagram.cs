namespace MacAC.Wire.Packets;

/// <summary>A fully decoded inbound datagram with owned (copied) body and fragments.</summary>
public sealed class Datagram
{
    public DatagramHeader Header;
    public DatagramHeaderExtras Optional { get; } = new();
    public List<WireFragment> Fragments { get; } = [];
    public byte[] CorpusOctets { get; set; } = [];
}
