using System.Text;
using MacAC.Wire.Packets;

namespace MacAC.Wire.Messages;

public static class TurbineComms
{
    public const uint Opcode = 0xF7DEu;
    public const int PreambleSize = 36;

    private const uint TransmitToHallByIdentResponseIdent = 2;
    private const uint TransmitToHallByIdentMethodIdent = 2;

    public enum BlobKind : uint
    {
        Unknown = 0,
        EventBinary = 1,
        EventXmlRpc = 2,
        RequestBinary = 3,
        RequestXmlRpc = 4,
        ResponseBinary = 5,
        ResponseXmlRpc = 6,
    }

    public enum RelayKind : uint
    {
        Unknown = 0,
        SendToRoomByName = 1,
        SendToRoomById = 2,
        CreateRoom = 3,
        InviteClientToRoomById = 4,
        EjectClientFromRoomById = 5,
    }

    public enum CommsKind : uint
    {
        Undef = 0,
        Allegiance = 1,
        General = 2,
        Trade = 3,
        Lfg = 4,
        Roleplay = 5,
        Society = 6,
        SocietyCelHan = 7,
        SocietyEldWeb = 8,
        SocietyRadBlo = 9,
        Olthoi = 10,
    }

    /// <summary>The payload shapes we understand; anything else is kept as raw bytes.</summary>
    public abstract record Cargo
    {
        public sealed record RoomSend(uint RoomId, string SenderName, string Message, uint ExtraDataSize, uint SenderId, int HResult, uint ChatType) : Cargo;

        public sealed record RoomSendAsk(uint ContextId, uint RoomId, string Message, uint ExtraDataSize, uint SenderId, int HResult, uint ChatType) : Cargo;

        public sealed record Reply(uint ContextId, uint ResponseId, uint MethodId, int HResult) : Cargo;

        public sealed record Unresolved(byte[] Bytes) : Cargo;
    }

    public readonly record struct Parsed(BlobKind BlobType, RelayKind DispatchType, uint TargetType, uint TargetId, uint TransportType, uint TransportId, uint Cookie, Cargo Body);

    /// <summary>Body includes the leading opcode word.</summary>
    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        var cursor = new WireCursor(corpus);
        if (!cursor.Has(4 + PreambleSize) || !cursor.Opcode(Opcode))
            return null;
        try
        {
            cursor.Skip(4); // outer size: 40 + payload, not needed
            uint blobRaw = cursor.U32();
            uint relayRaw = cursor.U32();
            uint markKind = cursor.U32();
            uint markIdent = cursor.U32();
            uint conveyanceKind = cursor.U32();
            uint conveyanceIdent = cursor.U32();
            uint cookie = cursor.U32();
            uint interior = cursor.U32(); // 8 + payload length
            if (interior < 8)
                return null;
            int cargoLen = checked((int)(interior - 8));
            if (!cursor.Has(cargoLen))
                return null;
            // Unknown discriminants are rejected outright; the enums cover retail's full set.
            if (blobRaw > 6 || relayRaw > 5)
                return null;
            BlobKind blob = (BlobKind)blobRaw;
            RelayKind relay = (RelayKind)relayRaw;

            int cargoBegin = cursor.At;
            Cargo? cargo = (blob, dispatch: relay) switch
            {
                (BlobKind.EventBinary, RelayKind.SendToRoomByName) => HallTransmit(ref cursor),
                (BlobKind.RequestBinary, RelayKind.SendToRoomById) => HallTransmitAsk(ref cursor),
                (BlobKind.ResponseBinary, _) => Response(ref cursor),
                _ => new Cargo.Unresolved(cursor.Bytes(cargoLen)),
            };
            if (cargo is null)
                return null;

            // Anything the shape did not consume is slack that must still be present.
            int slack = cargoLen - (cursor.At - cargoBegin);
            if (slack > 0)
            {
                if (!cursor.Has(slack))
                    return null;
                cursor.Skip(slack);
            }
            return new Parsed(blob, relay, markKind, markIdent, conveyanceKind, conveyanceIdent, cookie, cargo);
        }
        catch
        {
            return null;
        }
    }

    public static byte[] Build(BlobKind blobKind, RelayKind relayKind, uint markKind, uint markIdent, uint conveyanceKind, uint conveyanceIdent, uint cookie, Cargo cargo)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        List<byte> interior = new List<byte>(64);
        EmitCargo(interior, cargo);
        uint num = (uint)interior.Count;

        DatagramScribe scribe = new DatagramScribe(4 + PreambleSize + interior.Count);
        scribe.EmitUInt32(Opcode);
        scribe.EmitUInt32(40u + num);
        scribe.EmitUInt32((uint)blobKind);
        scribe.EmitUInt32((uint)relayKind);
        scribe.EmitUInt32(markKind);
        scribe.EmitUInt32(markIdent);
        scribe.EmitUInt32(conveyanceKind);
        scribe.EmitUInt32(conveyanceIdent);
        scribe.EmitUInt32(cookie);
        scribe.EmitUInt32(8u + num);
        scribe.EmitOctets(interior.ToArray());
        return scribe.ToArray();
    }

    public static string ScanTurbineString(ReadOnlySpan<byte> blob, ref int spot)
    {
        var cursor = new WireCursor(blob);
        cursor.Skip(spot);
        string phrase = TurbineString(ref cursor);
        spot = cursor.At;
        return phrase;
    }

    public static void EmitTurbineString(List<byte> buffer, string s)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(s);
        int chars = s.Length;
        if (chars >= 0x8000)
            throw new ArgumentException("turbine string exceeds 2-byte length prefix range (max 0x7FFF code units)", nameof(s));
        if (chars < 0x80)
        {
            buffer.Add((byte)chars);
        }
        else
        {
            buffer.Add((byte)(0x80 | ((chars >> 8) & 0x7F)));
            buffer.Add((byte)(chars & 0xFF));
        }
        buffer.AddRange(Encoding.Unicode.GetBytes(s));
    }

    private static Cargo? HallTransmit(ref WireCursor cursor)
    {
        uint hall = cursor.U32();
        string sender = TurbineString(ref cursor);
        string msg = TurbineString(ref cursor);
        if (!cursor.Has(16))
            return null;
        return new Cargo.RoomSend(hall, sender, msg, cursor.U32(), cursor.U32(), (int)cursor.U32(), cursor.U32());
    }

    private static Cargo? HallTransmitAsk(ref WireCursor cursor)
    {
        if (!cursor.Has(16))
            return null;
        uint ctx = cursor.U32();
        uint responseIdent = cursor.U32();
        uint methodIdent = cursor.U32();
        uint hall = cursor.U32();
        if (responseIdent != TransmitToHallByIdentResponseIdent || methodIdent != TransmitToHallByIdentMethodIdent)
            return null;
        string msg = TurbineString(ref cursor);
        if (!cursor.Has(16))
            return null;
        return new Cargo.RoomSendAsk(ctx, hall, msg, cursor.U32(), cursor.U32(), (int)cursor.U32(), cursor.U32());
    }

    private static Cargo? Response(ref WireCursor cursor)
    {
        if (!cursor.Has(16))
            return null;
        return new Cargo.Reply(cursor.U32(), cursor.U32(), cursor.U32(), (int)cursor.U32());
    }

    private static void EmitCargo(List<byte> into, Cargo cargo)
    {
        void Word(uint val)
        {
            Span<byte> four = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(four, val);
            into.AddRange(four);
        }

        switch (cargo)
        {
            case Cargo.RoomSend send:
                Word(send.RoomId);
                EmitTurbineString(into, send.SenderName);
                EmitTurbineString(into, send.Message);
                Word(send.ExtraDataSize);
                Word(send.SenderId);
                Word(unchecked((uint)send.HResult));
                Word(send.ChatType);
                break;
            case Cargo.RoomSendAsk r:
                Word(r.ContextId);
                Word(TransmitToHallByIdentResponseIdent);
                Word(TransmitToHallByIdentMethodIdent);
                Word(r.RoomId);
                EmitTurbineString(into, r.Message);
                Word(r.ExtraDataSize);
                Word(r.SenderId);
                Word(unchecked((uint)r.HResult));
                Word(r.ChatType);
                break;
            case Cargo.Reply response:
                Word(response.ContextId);
                Word(response.ResponseId);
                Word(response.MethodId);
                Word(unchecked((uint)response.HResult));
                break;
            case Cargo.Unresolved unknown:
                into.AddRange(unknown.Bytes);
                break;
        }
    }

    // Char count in one byte, or two big-endian bytes with the top bit set; then UTF-16LE code units
    private static string TurbineString(ref WireCursor cursor)
    {
        if (!cursor.Has(1))
            throw new FormatException("turbine str: truncated len");
        int chars = cursor.U8();
        if ((chars & 0x80) is not 0)
        {
            if (!cursor.Has(1))
                throw new FormatException("turbine str: truncated len2");
            chars = ((chars & 0x7F) << 8) | cursor.U8();
        }
        long octets = chars * 2L;
        if (octets > int.MaxValue || cursor.Left < octets)
            throw new FormatException("turbine str: truncated body");
        string phrase = Encoding.Unicode.GetString(cursor.Rest.Slice(0, (int)octets));
        cursor.Skip((int)octets);
        return phrase;
    }
}
