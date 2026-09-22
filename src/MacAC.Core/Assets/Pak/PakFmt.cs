using System.Buffers.Binary;

namespace MacAC.Assets.Pak;

/// <summary>Version stamps written into every pak header.</summary>
public static class PakFmt
{
    public const uint LatestFmtVer = 2;

    public const uint LatestBakeToolVer = 10;
}

public struct PakPreamble
{
    public const int Size = 64;
    public const uint MagicVal = 0x4B504341u; // 'ACPK' little-endian

    private const int ReservedBegin = 40;

    public uint Magic { get; private set; } = MagicVal;

    public uint FmtVer;
    public uint PortalIteration;
    public uint CellIteration;
    public uint HighResIteration;
    public uint LanguageIteration;
    public ulong TocShift;
    public uint TocTally;
    public uint BakeToolVer;

    public PakPreamble() { }

    public void WriteTo(Span<byte> dest)
    {
        if (dest.Length < Size)
            throw new ArgumentException($"destination has to be no fewer than {Size} bytes", nameof(dest));

        BinaryPrimitives.WriteUInt32LittleEndian(dest[0..4], MagicVal);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[4..8], FmtVer);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[8..12], PortalIteration);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[12..16], CellIteration);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[16..20], HighResIteration);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[20..24], LanguageIteration);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[24..32], TocShift);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[32..36], TocTally);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[36..40], BakeToolVer);
        dest[ReservedBegin..Size].Clear(); // reserved, zero
    }

    public void WriteTo(Stream flow)
    {
        Span<byte> buf = stackalloc byte[Size];
        WriteTo(buf);
        flow.Write(buf);
    }

    public static PakPreamble ReadFrom(ReadOnlySpan<byte> src)
    {
        if (src.Length < Size)
            throw new ArgumentException($"source has to be no fewer than {Size} bytes", nameof(src));

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(src[0..4]);
        if (magic != MagicVal)
            throw new InvalidDataException($"pak header magic mismatch: wanted 0x{MagicVal:X8}, got 0x{magic:X8}");

        return new PakPreamble
        {
            FmtVer = BinaryPrimitives.ReadUInt32LittleEndian(src[4..8]),
            PortalIteration = BinaryPrimitives.ReadUInt32LittleEndian(src[8..12]),
            CellIteration = BinaryPrimitives.ReadUInt32LittleEndian(src[12..16]),
            HighResIteration = BinaryPrimitives.ReadUInt32LittleEndian(src[16..20]),
            LanguageIteration = BinaryPrimitives.ReadUInt32LittleEndian(src[20..24]),
            TocShift = BinaryPrimitives.ReadUInt64LittleEndian(src[24..32]),
            TocTally = BinaryPrimitives.ReadUInt32LittleEndian(src[32..36]),
            BakeToolVer = BinaryPrimitives.ReadUInt32LittleEndian(src[36..40]),
        };
    }

    public static PakPreamble ReadFrom(Stream flow)
    {
        Span<byte> buf = stackalloc byte[Size];
        flow.ReadExactly(buf);
        return ReadFrom((ReadOnlySpan<byte>)buf);
    }
}

public struct PakTocListing
{
    public const int Size = 24;
    public const uint CompressionBit = 0x8000_0000u;
    public const uint StoredLenBitmask = 0x7FFF_FFFFu;

    public ulong Key;
    public ulong Offset;
    public uint Length;
    public uint Crc32;

    public readonly bool IsCompressed => (Length & CompressionBit) is not 0;

    public readonly uint StoredLen => Length & StoredLenBitmask;

    public void WriteTo(Span<byte> dest)
    {
        if (dest.Length < Size)
            throw new ArgumentException($"destination has to be no fewer than {Size} bytes", nameof(dest));

        BinaryPrimitives.WriteUInt64LittleEndian(dest[0..8], Key);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[8..16], Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[16..20], Length);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[20..24], Crc32);
    }

    public void WriteTo(Stream flow)
    {
        Span<byte> buf = stackalloc byte[Size];
        WriteTo(buf);
        flow.Write(buf);
    }

    public static PakTocListing ScanFrom(ReadOnlySpan<byte> src)
    {
        if (src.Length < Size)
            throw new ArgumentException($"source has to be no fewer than {Size} bytes", nameof(src));

        return new PakTocListing
        {
            Key = BinaryPrimitives.ReadUInt64LittleEndian(src[0..8]),
            Offset = BinaryPrimitives.ReadUInt64LittleEndian(src[8..16]),
            Length = BinaryPrimitives.ReadUInt32LittleEndian(src[16..20]),
            Crc32 = BinaryPrimitives.ReadUInt32LittleEndian(src[20..24]),
        };
    }
}
