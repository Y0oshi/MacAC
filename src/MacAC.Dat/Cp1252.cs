namespace MacAC.Dat;

// Windows code page 1252: Latin-1 with the 0x80-0x9F block filled with typographic characters.
public static class Cp1252
{
    static readonly char[] High =
    [
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021', '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014', '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    ];
    static readonly Dictionary<char, byte> Back = BuildBack();

    static Dictionary<char, byte> BuildBack()
    {
        var d = new Dictionary<char, byte>();
        for (int i = 0; i < High.Length; i++) d[High[i]] = (byte)(0x80 + i);
        return d;
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        return string.Create(bytes.Length, bytes.ToArray(), static (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                byte b = src[i];
                span[i] = b is >= 0x80 and <= 0x9F ? High[b - 0x80] : (char)b;
            }
        });
    }

    public static byte[] Encode(string s)
    {
        var bytes = new byte[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            bytes[i] = ch < 0x80 || (ch >= 0xA0 && ch <= 0xFF) ? (byte)ch : Back.TryGetValue(ch, out var b) ? b : (byte)'?';
        }
        return bytes;
    }
}
