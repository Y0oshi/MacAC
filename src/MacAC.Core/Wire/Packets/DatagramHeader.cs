using System.Buffers.Binary;
using MacAC.Wire.Cryptography;

namespace MacAC.Wire.Packets;

/// <summary>The fixed 20-byte datagram header, field order as on the wire.</summary>
public struct DatagramHeader
{
    public const int Size = 20;

    /// <summary>What sits in the checksum slot while the header hash is being computed.</summary>
    public const uint ChecksumPlaceholder = 0xBADD70DDu;

    private const int SeriesAt = 0, FlagSetAt = 4, ChecksumAt = 8, IdentAt = 12, MomentAt = 14, BlobDimsAt = 16, IterationAt = 18;

    public uint Sequence;
    public DatagramHeaderFlags Flags;
    public uint Checksum;
    public ushort Id;
    public ushort Time;
    public ushort BlobDims;
    public ushort Iteration;

    /// <summary>True when any bit of <paramref name="flagSet"/> is set.</summary>
    public readonly bool HasFlag(DatagramHeaderFlags flagSet) => (Flags & flagSet) != 0;

    public readonly void Pack(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"destination has to be no fewer than {Size} bytes", nameof(destination));

        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(SeriesAt), Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(FlagSetAt), (uint)Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(ChecksumAt), Checksum);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(IdentAt), Id);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(MomentAt), Time);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(BlobDimsAt), BlobDims);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(IterationAt), Iteration);
    }

    public static DatagramHeader Unpack(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
            throw new ArgumentException($"source has to be no fewer than {Size} bytes", nameof(source));

        return new DatagramHeader
        {
            Sequence = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(SeriesAt)),
            Flags = (DatagramHeaderFlags)BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(FlagSetAt)),
            Checksum = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(ChecksumAt)),
            Id = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(IdentAt)),
            Time = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(MomentAt)),
            BlobDims = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(BlobDimsAt)),
            Iteration = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(IterationAt)),
        };
    }

    /// <summary>Hash of this header with the checksum slot holding the placeholder.</summary>
    public readonly uint DerivePreambleHash32()
    {
        var masked = this;
        masked.Checksum = ChecksumPlaceholder;
        Span<byte> octets = stackalloc byte[Size];
        masked.Pack(octets);
        return CanonHash32.Work(octets);
    }
}
