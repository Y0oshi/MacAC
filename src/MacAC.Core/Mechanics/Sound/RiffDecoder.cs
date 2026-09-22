using System.Buffers.Binary;

namespace MacAC.Mechanics.Sound;

/// <summary>Reads the WAVEFORMATEX header a DAT Wave carries; only plain PCM is played.</summary>
public static class RiffDecoder
{
    public enum RiffFormatTag : ushort
    {
        Unknown = 0x0000,
        Pcm = 0x0001,
        Adpcm = 0x0002,
        Mp3 = 0x0055,
    }

    private const int FloorPreambleOctets = 14;
    private const int BitsetPerSpecimenShift = 14;
    private const ushort DefaultBitset = 16;

    public static PcmClip? Decipher(byte[] preamble, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(preamble);
        ArgumentNullException.ThrowIfNull(blob);
        if (preamble.Length < FloorPreambleOctets)
            return null;

        ReadOnlySpan<byte> h = preamble;
        RiffFormatTag tag = (RiffFormatTag)BinaryPrimitives.ReadUInt16LittleEndian(h);
        ushort lanes = BinaryPrimitives.ReadUInt16LittleEndian(h[2..]);
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(h[4..]);
        ushort bitset = preamble.Length > BitsetPerSpecimenShift + 1
            ? BinaryPrimitives.ReadUInt16LittleEndian(h[BitsetPerSpecimenShift..])
            : DefaultBitset;

        if (lanes is 0 || rate is 0 || tag != RiffFormatTag.Pcm)
            return null;

        // PCM data needs no framing: the payload is the sample buffer.
        double secs = bitset > 0 ? blob.Length * 8.0 / (double)(rate * lanes * bitset) : 0.0;
        return new PcmClip
        {
            LaneTally = lanes,
            SpecimenRate = (int)rate,
            BitsetPerSpecimen = bitset is 0 ? DefaultBitset : bitset,
            PcmOctets = blob,
            Duration = TimeSpan.FromSeconds(secs),
        };
    }

    public static RiffFormatTag GlimpseFmt(byte[] preamble)
    {
        return preamble is { Length: >= 2 } ? (RiffFormatTag)BinaryPrimitives.ReadUInt16LittleEndian(preamble) : RiffFormatTag.Unknown;
    }
}
