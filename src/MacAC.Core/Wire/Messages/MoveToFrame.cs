using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Wire.Messages;

public static class MoveToFrame
{
    public const uint GameActOpcode = GameActionScribe.Envelope;
    public const uint RelocateToPhaseAct = 0xF61Cu;

    public static byte[] Build(
        uint playActSeries,
        CrudeLocomotionPhase rawLocomotionPhase,
        uint chamberIdent,
        Vector3 locus,
        Quaternion spin,
        ushort instSeries,
        ushort srvControlSeries,
        ushort warpSeries,
        ushort forceLocusSeries,
        bool link = true,
        bool standingLongjump = false)
    {
        byte bitset = (byte)((standingLongjump ? 0x02 : 0) | (link ? 0x01 : 0));
        return new GameActionScribe(playActSeries, RelocateToPhaseAct, 128)
            .LocomotionPhase(rawLocomotionPhase)
            .WorldPosition(chamberIdent, locus, spin)
            .U16(instSeries).U16(srvControlSeries).U16(warpSeries).U16(forceLocusSeries)
            .U8(bitset)
            .Align4()
            .Bytes();
    }
}
