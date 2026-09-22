using System.Numerics;

namespace MacAC.Client.Graphics;

public readonly record struct CameraSweepOutcome(Vector3 Eye, uint ViewerCellId);

public interface ICameraContactProbe
{
    CameraSweepOutcome SweepEyePt(Vector3 pivot, Vector3 wantedEyePt, uint chamberIdent, uint selfActorIdent, Vector3 avatarSpot);
}
