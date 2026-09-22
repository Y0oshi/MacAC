using System.Buffers.Binary;
using System.Text;

namespace MacAC.Wire.Packets;

/// <summary>Little-endian growable writer with the retail string encodings.</summary>
public sealed class DatagramScribe(int startingCap = 256)
{
    private byte[] _octets = new byte[startingCap];

    public int Position { get; private set; }

    public byte[] ToArray() => _octets.AsSpan(0, Position).ToArray();

    /// <summary>A view over the bytes written so far; invalid after the next write.</summary>
    public ReadOnlySpan<byte> AsSpan() => _octets.AsSpan(0, Position);

    public void EmitByte(byte val) => Claim(1)[0] = val;

    public void EmitUInt16(ushort val) => BinaryPrimitives.WriteUInt16LittleEndian(Claim(2), val);

    public void EmitUInt32(uint val) => BinaryPrimitives.WriteUInt32LittleEndian(Claim(4), val);

    public void EmitFloat(float val) => BinaryPrimitives.WriteSingleLittleEndian(Claim(4), val);

    public void EmitDouble(double val) => BinaryPrimitives.WriteDoubleLittleEndian(Claim(8), val);

    public void EmitOctets(ReadOnlySpan<byte> octets) => octets.CopyTo(Claim(octets.Length));

    /// <summary>Zero-pads to the next multiple of four.</summary>
    public void LineTo4() => Pad(PaddingFor(Position));

    public void Pad(int tally)
    {
        if (tally > 0)
            Claim(tally).Clear();
    }

    /// <summary>String16L: u16 length, ASCII bytes, padded to four from the length prefix.</summary>
    public void EmitString16L(string val)
    {
        val ??= string.Empty;
        EmitUInt16((ushort)val.Length);
        Ascii(val);
        Pad(PaddingFor(2 + val.Length));
    }

    public void EmitString32L(string value)
    {
        value ??= string.Empty;
        if (value.Length > 255)
            throw new ArgumentException($"String32L only supports short strings (≤255), got {value.Length}", nameof(value));
        if (value.Length is 0)
        {
            EmitUInt32(0);
            return;
        }

        EmitUInt32((uint)(value.Length + 1));
        EmitByte(0);
        Ascii(value);
        Pad(PaddingFor(4 + 1 + value.Length));
    }

    // Reserves tally bytes and returns the span to fill
    private Span<byte> Claim(int tally)
    {
        int needed = Position + tally;
        if (needed > _octets.Length)
        {
            int grown = _octets.Length;
            while (grown < needed)
                grown *= 2;
            Array.Resize(ref _octets, grown);
        }
        Span<byte> socket = _octets.AsSpan(Position, tally);
        Position += tally;
        return socket;
    }

    private static int PaddingFor(int captureDims) => (4 - (captureDims & 3)) & 3;

    private void Ascii(string phrase) => Encoding.ASCII.GetBytes(phrase, Claim(phrase.Length));
}
