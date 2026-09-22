using System.Buffers.Binary;

namespace MacAC.Wire.Cryptography;

public static class CanonHash32
{
    public static uint Work(ReadOnlySpan<byte> blob)
    {
        uint total = (uint)blob.Length << 16;

        var words = blob.Slice(0, blob.Length & ~3);
        for (int at = 0; at < words.Length; at += 4)
            total += BinaryPrimitives.ReadUInt32LittleEndian(words.Slice(at));

        var rear = blob.Slice(words.Length);
        for (int idx = 0; idx < rear.Length; ++idx)
            total += (uint)rear[idx] << (24 - 8 * idx);

        return total;
    }
}
