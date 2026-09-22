using System.Buffers.Binary;

namespace MacAC.Wire.Messages;

public static partial class PlaySignals
{

    public static LaneAir? DecodeLaneAir(ReadOnlySpan<byte> cargo)
    {
        if (cargo.Length < 4)
            return null;
        return Guarded<LaneAir>(cargo, static (ref WireCursor cursor) =>
        {
            uint lane = cursor.U32();
            string sender = cursor.String16L();
            return new LaneAir(lane, sender, cursor.String16L());
        });
    }

    public static Whisper? DecodeTell(ReadOnlySpan<byte> cargo)
    {
        return Guarded<Whisper>(cargo, static (ref WireCursor cursor) =>
    {
        string msg = cursor.String16L();
        string sender = cursor.String16L();
        if (!cursor.Has(12))
            return null;
        return new Whisper(msg, sender, cursor.U32(), cursor.U32(), cursor.U32());
    });
    }

    public static string? DecodeTransient(ReadOnlySpan<byte> cargo)
    {
        return GuardedRef<string>(cargo, static (ref WireCursor cursor) => cursor.String16L());
    }

    private delegate T CursorParser<T>(ref WireCursor c);

    public readonly record struct LaneAir(uint ChannelId, string SenderName, string Message);

    /// <summary>0x0004 PopupString: modal dialog text.</summary>
    public static string? DecodePopupString(ReadOnlySpan<byte> cargo)
    {
        return GuardedRef<string>(cargo, static (ref WireCursor cursor) => cursor.String16L());
    }

    /// <summary>0x02BD Tell: message, sender name, sender guid, target guid, chat type.</summary>
    public readonly record struct Whisper(string Message, string SenderName, uint SenderGuid, uint TargetGuid, uint ChatType);

    public static AskAgeReply? DecodeAskAgeResponse(ReadOnlySpan<byte> cargo)
    {
        return Guarded<AskAgeReply>(cargo, static (ref WireCursor cursor) =>
    {
        string label = cursor.String16L();
        return new AskAgeReply(label, cursor.String16L());
    });
    }

    public static uint? DecodeWeenieProblem(ReadOnlySpan<byte> cargo) => LeadWord(cargo);

    public static WeenieProblemWithString? DecodeWeenieProblemWithString(ReadOnlySpan<byte> cargo)
    {
        if (cargo.Length < 4)
            return null;
        return Guarded<WeenieProblemWithString>(cargo, static (ref WireCursor cursor) =>
        {
            uint code = cursor.U32();
            return new WeenieProblemWithString(code, cursor.String16L());
        });
    }

    public readonly record struct AskAgeReply(string Name, string Age);

    /// <summary>0x01EA PingResponse carries nothing; arriving is the acknowledgement.</summary>
    public static bool DecodePingResponse(ReadOnlySpan<byte> cargo) => cargo.IsEmpty;
    // The leading word of a payload that is at least one word long
    private static uint? LeadWord(ReadOnlySpan<byte> cargo)
    {
        return cargo.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(cargo) : null;
    }

    /// <summary>0x028B WeenieErrorWithString: code plus the string interpolated into its text.</summary>
    public readonly record struct WeenieProblemWithString(uint ErrorCode, string Interpolation);

    // Runs a cursor-based parse, mapping any throw to "malformed"
    private static T? Guarded<T>(ReadOnlySpan<byte> cargo, CursorParser<T?> decode) where T : struct
    {
        try
        {
            var cursor = new WireCursor(cargo);
            return decode(ref cursor);
        }
        catch
        {
            return null;
        }
    }

    private static T? GuardedRef<T>(ReadOnlySpan<byte> cargo, CursorParser<T?> decode) where T : class
    {
        try
        {
            var cursor = new WireCursor(cargo);
            return decode(ref cursor);
        }
        catch
        {
            return null;
        }
    }
}
