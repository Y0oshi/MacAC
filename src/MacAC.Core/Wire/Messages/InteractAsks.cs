namespace MacAC.Wire.Messages;

/// <summary>GameActions for using things, lifestone recall, and putting an item into a container.</summary>
public static class InteractAsks
{
    public const uint GameActionEnvelope = GameActionScribe.Envelope;
    public const uint UseOpcode = 0x0036u;
    public const uint UseWithMarkOpcode = 0x0035u;
    public const uint TeleToLifestoneOpcode = 0x0063u;
    public const uint PutGearInVesselOpcode = 0x0019u;

    public static byte[] AssembleUse(uint playActSeries, uint markOid)
    {
        return new GameActionScribe(playActSeries, UseOpcode, 16).U32(markOid).Bytes();
    }

    public static byte[] AssembleUseWithMark(uint playActSeries, uint srcOid, uint markOid)
    {
        return new GameActionScribe(playActSeries, UseWithMarkOpcode, 20).U32(srcOid).U32(markOid).Bytes();
    }

    public static byte[] AssembleTeleToLifestone(uint playActSeries) =>
        new GameActionScribe(playActSeries, TeleToLifestoneOpcode, 12).Bytes();

    public static byte[] AssembleChooseUp(uint playActSeries, uint gearOid, uint vesselOid, int stance)
    {
        return new GameActionScribe(playActSeries, PutGearInVesselOpcode, 24).U32(gearOid).U32(vesselOid).I32(stance).Bytes();
    }
}
