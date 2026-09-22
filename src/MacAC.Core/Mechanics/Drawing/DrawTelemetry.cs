using System.Globalization;

namespace MacAC.Mechanics.Drawing;

public static class DrawTelemetry
{
    private const uint LeadEnvironChamberOrdinal = 0x0100u;

    public static int LampDiagManner { get; set; } = ScanInt("MACAC_LIGHT_DEBUG");

    /// <summary>The permanent frame-profiler toggle.</summary>
    public static bool CycleProfTurnedOn { get; set; } = Environment.GetEnvironmentVariable("MACAC_FRAME_PROF") == "1";

    public static string? CycleHistoryTrail { get; } = Environment.GetEnvironmentVariable("MACAC_FRAME_HISTORY");

    public static bool DumpWalkTranscriptEnabled { get; set; }

    /// <summary>True for indoor EnvCell ids (low word at or above 0x0100).</summary>
    public static bool IsEnvironChamberIdent(ulong ident) => (ident & 0xFFFFu) >= LeadEnvironChamberOrdinal;

    public static bool ShouldRasterizeInside(uint avatarChamberIdent, bool rasterizeTrunkSettled) =>
        rasterizeTrunkSettled && IsEnvironChamberIdent(avatarChamberIdent);

    private static int ScanInt(string variable) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int num)
            ? num
            : 0;
}
