namespace MacAC.Wire.Messages;

public static class ToonRoster
{
    public const uint Opcode = 0xF658u;

    public readonly record struct Toon(uint Id, string Name, uint SecondsGreyedOut);

    public readonly record struct WireSelection(int ActiveIndex, Toon Character);

    public sealed record ParsedUnit(
        uint Status,
        IReadOnlyList<Toon> Characters,
        IReadOnlyList<Toon> DeletedCharacters,
        int SlotCount,
        string AccountName,
        bool UseTurbineChat,
        bool HasThroneOfDestiny);

    public static ParsedUnit Parse(ReadOnlySpan<byte> corpus)
    {
        var cursor = new WireCursor(corpus);
        uint opcode = cursor.Word();
        if (opcode != Opcode)
            throw new FormatException($"wanted ToonRoster opcode 0x{Opcode:X4}, got 0x{opcode:X8}");

        uint condition = cursor.Word();
        Toon[] engaged = Lineup(ref cursor, "active");
        Toon[] deleted = Lineup(ref cursor, "deleted");
        int sockets = unchecked((int)cursor.Word());
        string acct = cursor.String16L(demandPadding: true);
        bool turbineComms = cursor.Word() is not 0;
        bool throne = cursor.Word() is not 0;
        return new ParsedUnit(condition, engaged, deleted, sockets, acct, turbineComms, throne);
    }

    public static bool TryPickLeadOnHand(ParsedUnit decoded, out WireSelection pick)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        for (int idx = 0; idx < decoded.Characters.Count; ++idx)
        {
            if (IsOnHandEngagedPersona(decoded.Characters[idx]))
            {
                pick = new WireSelection(idx, decoded.Characters[idx]);
                return true;
            }
        }
        pick = default;
        return false;
    }

    public static bool IsOnHandEngagedPersona(Toon toon) => toon.Id is not 0 && toon.SecondsGreyedOut is 0;

    // count, then (id, String16L name, greyed-out seconds) each - at least twelve bytes per row
    private static Toon[] Lineup(ref WireCursor cursor, string which)
    {
        uint tally = cursor.Word();
        if (tally > (uint)(cursor.Left / 12))
            throw new FormatException($"{which} character count {tally} exceeds remaining payload");

        Toon[] ranks = new Toon[checked((int)tally)];
        for (int idx = 0; idx < ranks.Length; ++idx)
        {
            uint ident = cursor.Word();
            string label = cursor.String16L(demandPadding: true);
            ranks[idx] = new Toon(ident, label, cursor.Word());
        }
        return ranks;
    }
}
