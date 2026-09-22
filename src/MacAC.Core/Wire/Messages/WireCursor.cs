using MacAC.Wire.Packets;
using System.Buffers.Binary;

namespace MacAC.Wire.Messages;

// Little-endian read position over a message body
internal ref struct WireCursor(ReadOnlySpan<byte> corpus)
{
    private readonly ReadOnlySpan<byte> _corpus = corpus;

    public int At { get; private set; }

    public readonly int Length => _corpus.Length;

    public readonly int Left => _corpus.Length - At;

    public readonly bool Has(int num) => Left >= num;

    // Everything not yet consumed
    public readonly ReadOnlySpan<byte> Rest => _corpus.Slice(At);

    // True (and consumes four bytes) when the next word is anticipated
    public bool Opcode(uint anticipated)
    {
        if (!Has(4) || BinaryPrimitives.ReadUInt32LittleEndian(_corpus.Slice(At)) != anticipated)
            return false;
        At += 4;
        return true;
    }

    public void Skip(int num) => At += num;

    public byte U8() => _corpus[At++];

    public ushort U16()
    {
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_corpus.Slice(At));
        At += 2;
        return v;
    }

    public short Idx16()
    {
        short v = BinaryPrimitives.ReadInt16LittleEndian(_corpus.Slice(At));
        At += 2;
        return v;
    }

    public uint U32()
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_corpus.Slice(At));
        At += 4;
        return v;
    }

    public int I32()
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(_corpus.Slice(At));
        At += 4;
        return v;
    }

    public ulong U64()
    {
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_corpus.Slice(At));
        At += 8;
        return v;
    }

    public long Idx64()
    {
        long v = BinaryPrimitives.ReadInt64LittleEndian(_corpus.Slice(At));
        At += 8;
        return v;
    }

    public float F32()
    {
        float v = BinaryPrimitives.ReadSingleLittleEndian(_corpus.Slice(At));
        At += 4;
        return v;
    }

    public double F64()
    {
        double v = BinaryPrimitives.ReadDoubleLittleEndian(_corpus.Slice(At));
        At += 8;
        return v;
    }

    public bool Bool32() => U32() is not 0;

    public uint PackedDword()
    {
        if (!Has(2))
            throw new FormatException("truncated PackedDword");
        ushort lead = U16();
        if ((lead & 0x8000) is 0)
            return lead;
        if (!Has(2))
            throw new FormatException("truncated PackedDword ext");
        return ((uint)(lead & 0x7FFF) << 16) | U16();
    }

    // A PackedDword whose DAT type prefix was stripped on the wire; zero stays zero ("none")
    public uint PackedDword(uint recognizedKind)
    {
        uint dense = PackedDword();
        return dense is 0 ? 0 : dense | recognizedKind;
    }

    // Copies num bytes out
    public byte[] Bytes(int num)
    {
        byte[] duplicate = _corpus.Slice(At, num).ToArray();
        At += num;
        return duplicate;
    }

    // Skips to the next multiple of four from the start of the body
    public void Align4() => At += (4 - (At & 3)) & 3;

    // A u32 that must be present; FormatException otherwise
    public uint Word() => Has(4) ? U32() : throw new FormatException("truncated u32");

    // A u16 that must be present
    public ushort Half() => Has(2) ? U16() : throw new FormatException("truncated u16");

    // A byte that must be present
    public byte Octet() => Has(1) ? U8() : throw new FormatException("truncated byte");

    // Throws FormatException ("truncated {what}") unless num bytes remain
    public readonly void Demand(int num, string what)
    {
        if (!Has(num))
            throw new FormatException("truncated " + what);
    }

    // u16 length, CP1252 bytes, then padding to a four-byte boundary measured from the length prefix
    public string String16L(bool demandPadding = false, int upperLen = int.MaxValue)
    {
        if (!Has(2))
            throw new FormatException("truncated String16L length");
        ushort len = U16();
        if (len > upperLen)
            throw new FormatException($"String16L length {len} exceeds sanity limit");
        if (!Has(len))
            throw new FormatException("truncated String16L body");
        string phrase = WireEncodings.Windows1252.GetString(_corpus.Slice(At, len));
        At += len;
        int pad = (4 - ((2 + len) & 3)) & 3;
        if (demandPadding && !Has(pad))
            throw new FormatException("truncated String16L padding");
        At += pad;
        return phrase;
    }
}

// Builds the 0xF7B1 GameAction envelope: opcode, sequence, action type, then the action's own
// fields
internal sealed class GameActionScribe
{
    public const uint Envelope = 0xF7B1u;

    private readonly DatagramScribe _out;

    public GameActionScribe(uint series, uint actKind, int cap = 32)
    {
        _out = new DatagramScribe(cap);
        _out.EmitUInt32(Envelope);
        _out.EmitUInt32(series);
        _out.EmitUInt32(actKind);
    }

    public GameActionScribe U32(uint val)
    {
        _out.EmitUInt32(val);
        return this;
    }

    public GameActionScribe I32(int val) => U32(unchecked((uint)val));

    public GameActionScribe U16(ushort val)
    {
        _out.EmitUInt16(val);
        return this;
    }

    public GameActionScribe U8(byte val)
    {
        _out.EmitByte(val);
        return this;
    }

    public GameActionScribe F32(float val)
    {
        _out.EmitFloat(val);
        return this;
    }

    public GameActionScribe F64(double val)
    {
        _out.EmitDouble(val);
        return this;
    }

    public GameActionScribe Bool32(bool val) => U32(val ? 1u : 0u);

    // CP1252 String16L: u16 byte length, bytes, padded to four from the length prefix
    public GameActionScribe String16L(string value, string? tooLong = null, string? parameterLabel = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] blob = WireEncodings.Windows1252.GetBytes(value);
        if (blob.Length > ushort.MaxValue)
            throw new ArgumentException(tooLong ?? "String too long for String16L", parameterLabel ?? nameof(value));
        _out.EmitUInt16((ushort)blob.Length);
        _out.EmitOctets(blob);
        _out.Pad((4 - ((2 + blob.Length) & 3)) & 3);
        return this;
    }

    public GameActionScribe Bytes(ReadOnlySpan<byte> octets)
    {
        _out.EmitOctets(octets);
        return this;
    }

    public GameActionScribe Align4()
    {
        _out.LineTo4();
        return this;
    }

    public byte[] Bytes() => _out.ToArray();
}
