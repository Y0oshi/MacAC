using System.Numerics;

namespace MacAC.Wire.Messages;

public static class JumpMove
{
    public const uint PlayActOpcode = GameActionScribe.Envelope;
    public const uint LeapOpcode = 0xF61Bu;

    public static byte[] Build(
        uint playActSeries,
        float reach,
        Vector3 vel,
        uint chamberIdent,
        Vector3 locus,
        Quaternion spin,
        ushort instSeries,
        ushort srvControlSeries,
        ushort warpSeries,
        ushort forceLocusSeries)
    {
        return new GameActionScribe(playActSeries, LeapOpcode, 80)
            .F32(reach)
            .F32(vel.X).F32(vel.Y).F32(vel.Z)
            .WorldPosition(chamberIdent, locus, spin)
            .U16(instSeries).U16(srvControlSeries).U16(warpSeries).U16(forceLocusSeries)
            .Align4()
            .Bytes();
    }
}
