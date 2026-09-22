using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics;

public sealed class KineticsCameraContactProbe(KineticEngine kinetics) : ICameraContactProbe
{
    public const float BeholderOrbRadius = 0.3f;

    private readonly KineticEngine _physics = kinetics;

    public CameraSweepOutcome SweepEyePt(Vector3 pivot, Vector3 wantedEyePt, uint chamberIdent, uint selfActorIdent, Vector3 avatarSpot)
    {
        if (chamberIdent is 0) return new CameraSweepOutcome(avatarSpot, 0u);

        uint beginChamber = chamberIdent;
        if ((chamberIdent & 0xFFFFu) >= 0x0100u)
        {
            var (pivotChamber, located) = _physics.TuneLocus(chamberIdent, pivot);
            if (located) beginChamber = pivotChamber;
        }

        Vector3 commence = ToOrbTrail(pivot, BeholderOrbRadius);
        Vector3 finish = ToOrbTrail(wantedEyePt, BeholderOrbRadius);

        ResolveVerdict verdict = _physics.ResolveWithTransition(
            latestSpot: commence,
            markSpot: finish,
            chamberIdent: beginChamber,
            orbRadius: BeholderOrbRadius,
            orbHeight: 0f,                    // single sphere (no head sphere)
            hopUpHeight: 0f,
            hopDownHeight: 0f,                    // no step-down / ground snap
            isOnTerrain: false,
            corpus: null,
            carrierFlagSet: MoverState.IsViewer | MoverState.PathClipped
                          | MoverState.FreeRotate | MoverState.PerfectClip,
            movingActorIdent: selfActorIdent);         // skip the player's own ProxyEntry

        Vector3 eyePt = FromOrbTrail(verdict.Position, BeholderOrbRadius);

        if (verdict.Ok) return new CameraSweepOutcome(eyePt, verdict.CellId);

        var (eyePtChamber, eyePtLocated) = _physics.TuneLocus(chamberIdent, wantedEyePt);
        if (eyePtLocated) return new CameraSweepOutcome(wantedEyePt, eyePtChamber);

        return new CameraSweepOutcome(avatarSpot, 0u);
    }

    internal static Vector3 ToOrbTrail(Vector3 orbPt, float radius)
        => orbPt - new Vector3(0f, 0f, radius);

    internal static Vector3 FromOrbTrail(Vector3 trailPt, float radius)
        => trailPt + new Vector3(0f, 0f, radius);
}
