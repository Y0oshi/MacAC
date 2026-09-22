using System.Buffers.Binary;
using System.Text;

namespace MacAC.Wire.Packets;

/// <summary>The account/password login body carried in the LoginRequest optional header.</summary>
public static class SigninRequest
{
    public enum WireAuthKind : uint
    {
        Undefined = 0x00000000,
        Account = 0x00000001,
        AccountPassword = 0x00000002,
        GlsTicket = 0x40000002,
    }

    public const string CanonClientVer = "1802";

    public readonly record struct Parsed(
        string ClientVersion,
        uint BodyLength,
        WireAuthKind NetAuth,
        uint AuthFlags,
        uint Timestamp,
        string Account,
        string LoginAs,
        string? Password,
        string? GlsTicket);

    public static byte[] Build(string acct, string password, uint stamp, string clientVer = CanonClientVer)
    {
        ArgumentNullException.ThrowIfNull(acct);
        ArgumentNullException.ThrowIfNull(password);

        DatagramScribe scribe = new DatagramScribe(128);
        scribe.EmitString16L(clientVer);

        int lenSocket = scribe.Position;
        scribe.EmitUInt32(0);
        int corpusBegin = scribe.Position;
        scribe.EmitUInt32((uint)WireAuthKind.AccountPassword);
        scribe.EmitUInt32(0); // auth flags
        scribe.EmitUInt32(stamp);
        scribe.EmitString16L(acct);
        scribe.EmitString16L(string.Empty); // LoginAs: only admins fill this
        scribe.EmitString32L(password);

        // The length prefix counts everything after itself
        byte[] octets = scribe.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(octets.AsSpan(lenSocket), (uint)(scribe.Position - corpusBegin));
        return octets;
    }

    public static Parsed Parse(ReadOnlySpan<byte> octets)
    {
        int spot = 0;
        string clientVer = String16L(octets, ref spot);
        uint corpusLen = U32(octets, ref spot, "truncated before bodyLength");
        WireAuthKind auth = (WireAuthKind)U32(octets, ref spot, "truncated before WireAuthKind");
        uint authFlagSet = BinaryPrimitives.ReadUInt32LittleEndian(octets.Slice(spot));
        spot += 4;
        uint stamp = BinaryPrimitives.ReadUInt32LittleEndian(octets.Slice(spot));
        spot += 4;
        string acct = String16L(octets, ref spot);
        string signinAs = String16L(octets, ref spot);

        string? password = auth == WireAuthKind.AccountPassword ? String32L(octets, ref spot) : null;
        string? ticket = auth == WireAuthKind.GlsTicket ? String32L(octets, ref spot) : null;
        return new Parsed(clientVer, corpusLen, auth, authFlagSet, stamp, acct, signinAs, password, ticket);
    }

    private static uint U32(ReadOnlySpan<byte> src, ref int spot, string truncated)
    {
        if (src.Length - spot < 4)
            throw new FormatException(truncated);
        uint val = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(spot));
        spot += 4;
        return val;
    }

    private static int PaddingFor(int captureDims) => (4 - (captureDims & 3)) & 3;

    private static string String16L(ReadOnlySpan<byte> src, ref int spot)
    {
        if (src.Length - spot < 2)
            throw new FormatException("truncated String16L length");
        ushort len = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(spot));
        spot += 2;
        if (src.Length - spot < len)
            throw new FormatException("truncated String16L body");
        string phrase = Encoding.ASCII.GetString(src.Slice(spot, len));
        spot += len + PaddingFor(2 + len);
        return phrase;
    }

    private static string String32L(ReadOnlySpan<byte> src, ref int spot)
    {
        if (src.Length - spot < 4)
            throw new FormatException("truncated String32L length");
        uint outer = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(spot));
        spot += 4;
        if (outer is 0)
            return string.Empty;

        if (src.Length - spot < 1)
            throw new FormatException("truncated String32L marker");
        spot += 1;
        uint len = outer - 1;
        if (len > 255)
        {
            // Long strings carry a second marker byte.
            spot += 1;
            len -= 1;
        }

        if (src.Length - spot < len)
            throw new FormatException("truncated String32L body");
        string phrase = Encoding.ASCII.GetString(src.Slice(spot, (int)len));
        spot += (int)len + PaddingFor(4 + (int)outer);
        return phrase;
    }
}
