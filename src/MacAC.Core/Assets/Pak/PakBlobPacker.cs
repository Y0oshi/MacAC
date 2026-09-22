using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace MacAC.Assets.Pak;

internal static class PakBlobPacker
{
    internal const int CeilingDecodedOctets = 64 * 1024 * 1024;
    private const int StemOctets = sizeof(uint);
    private const int CompressFloorOctets = 512;
    private const int FloorSavingsOctets = 64;
    private const int SavingsDivisor = 16;
    private const int BrotliFidelity = 1;
    private const int BrotliPane = 22;

    internal readonly record struct Packed(byte[] Bytes, bool Compressed);

    // Tries to compress into a pooled buffer
    public static bool TryCompress(ReadOnlySpan<byte> decoded, out byte[]? rented, out int storedLen)
    {
        if (decoded.Length > CeilingDecodedOctets)
            throw new InvalidDataException($"pak payload is {decoded.Length} bytes; maximum is {CeilingDecodedOctets}");

        rented = null;
        storedLen = decoded.Length;
        if (decoded.Length < CompressFloorOctets)
            return false;

        int cap = checked(StemOctets + BrotliEncoder.GetMaxCompressedLength(decoded.Length));
        byte[] contender = ArrayPool<byte>.Shared.Rent(cap);
        bool dense = BrotliEncoder.TryCompress(
            decoded,
            contender.AsSpan(StemOctets, cap - StemOctets),
            out int compressedLen,
            BrotliFidelity,
            BrotliPane);
        if (!dense)
        {
            ArrayPool<byte>.Shared.Return(contender);
            return false;
        }

        storedLen = checked(StemOctets + compressedLen);
        int neededSavings = Math.Max(FloorSavingsOctets, decoded.Length / SavingsDivisor);
        if (decoded.Length - storedLen < neededSavings)
        {
            ArrayPool<byte>.Shared.Return(contender);
            storedLen = decoded.Length;
            return false;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(contender.AsSpan(0, StemOctets), checked((uint)decoded.Length));
        rented = contender;
        return true;
    }

    public static Packed Pack(byte[] decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        if (!TryCompress(decoded, out byte[]? rented, out int storedLen))
            return new Packed(decoded, false);

        byte[] buf = rented ?? throw new InvalidOperationException("compressed buffer wasn't returned");
        try
        {
            return new Packed(buf.AsSpan(0, storedLen).ToArray(), true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    public static byte[] Unpack(ReadOnlySpan<byte> stored)
    {
        if (stored.Length < StemOctets)
            throw new InvalidDataException("compressed pak blob is absent its decoded-length prefix");

        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(stored[..StemOctets]);
        if (declared > CeilingDecodedOctets)
            throw new InvalidDataException($"compressed pak blob declares not supported decoded length {declared}");

        byte[] decoded = new byte[checked((int)declared)];
        bool precise = BrotliDecoder.TryDecompress(stored[StemOctets..], decoded, out int written) && written == decoded.Length;
        if (!precise)
            throw new InvalidDataException($"compressed pak blob didn't decode to its declared {decoded.Length} bytes");
        return decoded;
    }
}
