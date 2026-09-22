namespace MacAC.Wire.Messages;

public static class GenesisVerdict
{
    public const uint ResponseOpcode = 0xF643u;

    public enum Opcode : uint
    {
        Undef = 0,
        Ok = 1,
        Pending = 2,
        NameInUse = 3,
        NameBanned = 4,
        Corrupt = 5,
        DatabaseDown = 6,
        AdminPrivilegeDenied = 7,
    }

    public readonly record struct Parsed(uint RawCode, uint? Guid, string? Name, uint? SecondsGreyedOut)
    {
        public Opcode AsCode => (Opcode)RawCode;

        public bool IsOk => RawCode == (uint)Opcode.Ok;
    }

    public static Parsed Parse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        uint opcode = cursor.Word();
        if (opcode != ResponseOpcode)
            throw new FormatException($"wanted CharacterGenerationVerificationResponse opcode 0x{ResponseOpcode:X4}, got 0x{opcode:X8}");

        uint code = cursor.Word();
        if (code != (uint)Opcode.Ok)
            return new Parsed(code, null, null, null);

        uint oid = cursor.Word();
        string label = cursor.String16L();
        return new Parsed(code, oid, label, cursor.Word());
    }
}
