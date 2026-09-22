using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Kinetics;

// Pushes an entity's cell-local pose into the proxy registry, expressed relative to the live
// landblock centre
internal static class ProxyPositionSynchronizer
{
    private const float LbSpan = 192f;

    public static void Sync(ProxyRegistry registry, uint actorIdent, Vector3 locus, Quaternion facing, uint chamberIdent, int onlineMiddleX, int onlineMiddleY)
    {
        if (chamberIdent is 0)
            return;

        int bx = (int)((chamberIdent >> 24) & 0xFFu);
        int by = (int)((chamberIdent >> 16) & 0xFFu);
        registry.RefreshLocus(
            actorIdent,
            locus,
            facing,
            (bx - onlineMiddleX) * LbSpan,
            (by - onlineMiddleY) * LbSpan,
            chamberIdent,
            seedChamberIdent: chamberIdent);
    }
}
