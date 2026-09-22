using System.Numerics;

namespace MacAC.Wire.Messages;

public static class SelfPosition
{
    public const uint GameActionOpcode = GameActionScribe.Envelope;
    public const uint AutonomousLocusAct = 0xF753u;

    public static byte[] Build(
        uint playActSeries,
        uint chamberIdent,
        Vector3 locus,
        Quaternion spin,
        ushort instSeries,
        ushort srvControlSeries,
        ushort warpSeries,
        ushort forceLocusSeries,
        byte previousLink = 1)
    {
        return new GameActionScribe(playActSeries, AutonomousLocusAct, 64)
            .WorldPosition(chamberIdent, locus, spin)
            .U16(instSeries).U16(srvControlSeries).U16(warpSeries).U16(forceLocusSeries)
            .U8(previousLink)
            .Align4()
            .Bytes();
    }
}

internal static class WorldPositionScribe
{
    // cell, xyz, then the quaternion in retail's W-X-Y-Z order
    public static GameActionScribe WorldPosition(this GameActionScribe scribe, uint chamberIdent, Vector3 locus, Quaternion spin)
    {
        return scribe.U32(chamberIdent)
            .F32(locus.X).F32(locus.Y).F32(locus.Z)
            .F32(spin.W).F32(spin.X).F32(spin.Y).F32(spin.Z);
    }
}
