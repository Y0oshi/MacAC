namespace MacAC.Wire.Messages;

/// <summary>0xF659: a character-management error code.</summary>
public static class CharacterFault
{
    public const uint Opcode = 0xF659u;

    public enum WireCode : uint
    {
        Undefined = 0x00,
        Logon = 0x01,
        LoggedOn = 0x02,
        AccountLogon = 0x03,
        ServerCrash = 0x04,
        Logoff = 0x05,
        Delete = 0x06,
        NoPremade = 0x07,
        AccountInUse = 0x08,
        AccountInvalid = 0x09,
        AccountDoesntExist = 0x0A,
        EnterGameGeneric = 0x0B,
        EnterGameStressAccount = 0x0C,
        EnterGameCharacterInWorld = 0x0D,
        EnterGamePlayerAccountMissing = 0x0E,
        EnterGameCharacterNotOwned = 0x0F,
        EnterGameCharacterInWorldServer = 0x10,
        EnterGameOldCharacter = 0x11,
        EnterGameCorruptCharacter = 0x12,
        EnterGameStartServerDown = 0x13,
        EnterGameCouldntPlaceCharacter = 0x14,
        LogonServerFull = 0x15,
        CharacterIsBooted = 0x16,
        EnterGameCharacterLocked = 0x17,
        SubscriptionExpired = 0x18,
        NumErrors = 0x19,
    }

    public readonly record struct ParsedDef(uint RawErrorCode)
    {
        public WireCode AsCode => (WireCode)RawErrorCode;
    }

    public static ParsedDef Parse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        uint opcode = cursor.Word();
        if (opcode != Opcode)
            throw new FormatException($"wanted CharacterFault opcode 0x{Opcode:X4}, got 0x{opcode:X8}");
        return new ParsedDef(cursor.Word());
    }
}
