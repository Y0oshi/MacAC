namespace MacAC.Wire.Messages;

/// <summary>GameActions for /say, /tell and channel chat; every string is a CP1252 String16L.</summary>
public static class CommsAsks
{
    public const uint GameActionEnvelope = GameActionScribe.Envelope;
    public const uint TalkOpcode = 0x0015u;
    public const uint TellOpcode = 0x005Du;
    public const uint CommsLaneOpcode = 0x0147u;

    /// <summary>Local speech, heard within roughly twenty metres.</summary>
    public static byte[] AssembleTalk(uint playActSeries, string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        return new GameActionScribe(playActSeries, TalkOpcode).String16L(msg).Bytes();
    }

    /// <summary>Message first, then the recipient's name - retail's order.</summary>
    public static byte[] AssembleTell(uint playActSeries, string markLabel, string msg)
    {
        ArgumentNullException.ThrowIfNull(markLabel);
        ArgumentNullException.ThrowIfNull(msg);
        return new GameActionScribe(playActSeries, TellOpcode).String16L(msg).String16L(markLabel).Bytes();
    }

    public static byte[] AssembleCommsLane(uint playActSeries, uint laneIdent, string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        return new GameActionScribe(playActSeries, CommsLaneOpcode).U32(laneIdent).String16L(msg).Bytes();
    }
}
