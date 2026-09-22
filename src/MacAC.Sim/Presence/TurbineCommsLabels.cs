using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

// Display names and log-text kinds for the Turbine chat channels
internal static class TurbineCommsLabels
{
    private static readonly (TurbineComms.CommsKind Type, string Name, uint LogTextType)[] Ranks =
    [
        (TurbineComms.CommsKind.Allegiance, "Allegiance", 0x12u),
        (TurbineComms.CommsKind.General, "General", 0x1Bu),
        (TurbineComms.CommsKind.Trade, "Trade", 0x1Cu),
        (TurbineComms.CommsKind.Lfg, "LFG", 0x1Du),
        (TurbineComms.CommsKind.Roleplay, "Roleplay", 0x1Eu),
        (TurbineComms.CommsKind.Society, "Society", 0x20u),
        (TurbineComms.CommsKind.SocietyCelHan, "Celestial Hand", 0x20u),
        (TurbineComms.CommsKind.SocietyEldWeb, "Eldrytch Web", 0x20u),
        (TurbineComms.CommsKind.SocietyRadBlo, "Radiant Blood", 0x20u),
        (TurbineComms.CommsKind.Olthoi, "Olthoi", 0x12u),
    ];

    public static string Resolve(uint hallIdent, uint commsKind) => Row(commsKind)?.Name ?? $"Room 0x{hallIdent:X8}";

    public static uint TraceWordingKind(uint commsKind) => Row(commsKind)?.LogTextType ?? 0x00u;

    private static (TurbineComms.CommsKind Type, string Name, uint LogTextType)? Row(uint commsKind)
    {
        TurbineComms.CommsKind kind = (TurbineComms.CommsKind)commsKind;
        foreach (var rank in Ranks)
        {
            if (rank.Type == kind)
                return rank;
        }
        return null;
    }
}
