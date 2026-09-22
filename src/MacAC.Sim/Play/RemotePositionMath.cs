using System.Numerics;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Play;

// Landblock-relative wire positions to continuous world coordinates
internal static class RemotePositionMath
{
    private const float LbSpan = 192f;

    public static Vector3 ToRealm(in ObjectCreation.RemotePosition position)
    {
        int bx = (int)((position.LandblockId >> 24) & 0xFFu);
        int by = (int)((position.LandblockId >> 16) & 0xFFu);
        return new Vector3(position.PositionX + bx * LbSpan, position.PositionY + by * LbSpan, position.PositionZ);
    }

    public static Quaternion Rotation(in ObjectCreation.RemotePosition position)
    {
        return new(position.RotationX, position.RotationY, position.RotationZ, position.RotationW);
    }
}
