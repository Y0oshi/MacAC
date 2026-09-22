using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace MacAC.Dat;

// Forward-only reader over one decoded file. Little-endian throughout; the odd encodings the
// game used (packed dwords, length-prefixed strings padded to 4) live here so record readers stay flat.
public ref struct DatCursor(ReadOnlySpan<byte> bytes, uint fileId)
{
    private readonly ReadOnlySpan<byte> _bytes = bytes;
    private int _at;

    public uint FileId { get; } = fileId;
    public int Offset => _at;
    public int Remaining => _bytes.Length - _at;
    public bool AtEnd => _at >= _bytes.Length;

    // The game wrote single-byte text in the Windows Latin code page.
    public static Encoding TextEncoding { get; set; } = Encoding.Latin1;

    // Set by the vault so nested records can resolve property kinds while decoding.
    public PropertyCatalog? Catalog { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> Take(int n)
    {
        if (_at + n > _bytes.Length) throw new DatFormatException($"read past the end of 0x{FileId:X8} at {_at} (+{n} of {_bytes.Length})");
        var s = _bytes.Slice(_at, n); _at += n; return s;
    }

    public byte U8() => Take(1)[0];
    public sbyte I8() => (sbyte)Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
    public bool Flag32() => U32() != 0;
    public Vector3 Vec3() => new(F32(), F32(), F32());
    public Vector2 Vec2() => new(F32(), F32());
    public Quaternion Quat() { float w = F32(); return new Quaternion(F32(), F32(), F32(), w); }
    public Plane Plane() => new(Vec3(), F32());
    public ReadOnlySpan<byte> Bytes(int n) => Take(n);
    public byte[] Array(int n) => Take(n).ToArray();
    public void Skip(int n) => Take(n);

    // The game packs small dwords: one byte when < 0x80, two bytes with the high bit set when < 0x8000,
    // otherwise four bytes with the top two bits set.
    public uint PackedU32()
    {
        byte b0 = U8();
        if ((b0 & 0x80) == 0) return b0;
        byte b1 = U8();
        if ((b0 & 0x40) == 0) return (uint)(((b0 & 0x7F) << 8) | b1);
        ushort lo = U16();
        return (uint)(((b0 & 0x3F) << 24) | (b1 << 16) | lo);
    }

    // A data id stored relative to its type prefix: one word, or two when the high bit is set.
    public uint PackedId(uint typePrefix)
    {
        ushort hi = U16();
        if ((hi & 0x8000) == 0) return typePrefix + hi;
        ushort lo = U16();
        return typePrefix + (uint)(((hi & 0x3FFF) << 16) | lo);
    }

    // Text with a packed byte-length prefix and no padding.
    public string PStr()
    {
        int len = (int)PackedU32();
        return TextEncoding.GetString(Take(len));
    }

    // Length-prefixed text: ushort length (0xFFFF escapes to a uint), Latin-1 bytes, padded to 4.
    public string Str()
    {
        int len = U16();
        if (len == 0xFFFF) len = (int)U32();
        string s = TextEncoding.GetString(Take(len));
        Align(4);
        return s;
    }

    // Same layout but every byte was obfuscated by swapping its nibbles.
    public string ObfuscatedStr()
    {
        int len = U16();
        if (len == 0xFFFF) len = (int)U32();
        var raw = Take(len);
        Span<byte> tmp = len <= 512 ? stackalloc byte[len] : new byte[len];
        for (int i = 0; i < len; i++) tmp[i] = (byte)((raw[i] >> 4) | (raw[i] << 4));
        Align(4);
        return Cp1252.Decode(tmp);
    }

    // A 16-bit-char string with a packed length prefix (string tables).
    public string WideStr()
    {
        int len = (int)PackedU32();
        var raw = Take(len * 2);
        return Encoding.Unicode.GetString(raw);
    }

    public void Align(int n)
    {
        int rem = _at % n;
        if (rem != 0) Take(n - rem);
    }
}

public class DatFormatException(string message) : Exception(message);
