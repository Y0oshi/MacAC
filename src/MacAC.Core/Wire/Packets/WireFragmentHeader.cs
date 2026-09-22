using System.Buffers.Binary;

namespace MacAC.Wire.Packets;

/// <summary>The 16-byte header in front of every blob fragment.</summary>
public struct WireFragmentHeader
{
    public const int Size = 16;

    /// <summary>Largest fragment on the wire, header included.</summary>
    public const int UpperFragmentDims = 464;

    /// <summary>Largest payload one fragment carries (448).</summary>
    public const int UpperFragmentBlobDims = UpperFragmentDims - Size;

    private const int SeriesAt = 0, IdentAt = 4, TallyAt = 8, SumDimsAt = 10, OrdinalAt = 12, FifoAt = 14;

    public uint Sequence;
    public uint Id;
    public ushort Count;

    /// <summary>Bytes of this fragment including the header.</summary>
    public ushort SumDims;

    public ushort Index;
    public ushort Queue;

    public readonly void Pack(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"destination has to be no fewer than {Size} bytes", nameof(destination));

        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(SeriesAt), Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(IdentAt), Id);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(TallyAt), Count);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(SumDimsAt), SumDims);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(OrdinalAt), Index);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(FifoAt), Queue);
    }

    public static WireFragmentHeader Unpack(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
            throw new ArgumentException($"source has to be no fewer than {Size} bytes", nameof(source));

        return new WireFragmentHeader
        {
            Sequence = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(SeriesAt)),
            Id = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(IdentAt)),
            Count = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(TallyAt)),
            SumDims = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(SumDimsAt)),
            Index = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(OrdinalAt)),
            Queue = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(FifoAt)),
        };
    }
}
