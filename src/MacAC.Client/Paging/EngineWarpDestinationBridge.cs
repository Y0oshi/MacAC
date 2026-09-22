using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim;
using MacAC.Wire;

namespace MacAC.Client.Paging;

internal static class EngineWarpDestinationBridge
{
    public static SimWarpDestination FromApprovedLocus(
        in RealmSession.MoverPositionUpdate refresh)
    {
        var locus = refresh.Position;
        return new SimWarpDestination(
            refresh.Guid,
            refresh.InstanceSequence,
            refresh.PositionSequence,
            refresh.TeleportSequence,
            refresh.ForcePositionSequence,
            new Locus(
                locus.LandblockId,
                new Vector3(
                    locus.PositionX,
                    locus.PositionY,
                    locus.PositionZ),
                new Quaternion(
                    locus.RotationX,
                    locus.RotationY,
                    locus.RotationZ,
                    locus.RotationW)));
    }
}
