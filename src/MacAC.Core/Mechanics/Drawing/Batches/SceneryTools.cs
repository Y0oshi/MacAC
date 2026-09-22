using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Drawing.Batches;

public static class SceneryTools
{
    private const double UnitScaling = 2.3283064e-10;   // 1 / 2^32, as the client wrote it
    private const float UnitScalingF = 2.3283064e-10f;

    private const uint DisplaceXSalt = 45773u;
    private const uint DisplaceYSalt = 72719u;
    private const uint ScalingSalt = 32593u;
    private const uint SpinSalt = 63127u;

    /// <summary>Displaces a scenery object into its pseudo-random spot, then picks a quadrant.</summary>
    public static Vector3 Displace(SceneryItem objRef, uint ix, uint iy, uint iq)
    {
        Vector3 origin = new(objRef.BaseLoc.Origin.X, objRef.BaseLoc.Origin.Y, objRef.BaseLoc.Origin.Z);

        float x = objRef.DisplaceX <= 0
            ? origin.X
            : (float)(Jitter(ix, iy, iq, DisplaceXSalt) * UnitScaling * objRef.DisplaceX + origin.X);
        float y = objRef.DisplaceY <= 0
            ? origin.Y
            : (float)(Jitter(ix, iy, iq, DisplaceYSalt) * UnitScaling * objRef.DisplaceY + origin.Y);

        float quadrant = unchecked(1813693831u * iy - ix * (1870387557u * iy + 1109124029u) - 402451965u) * UnitScalingF;
        return quadrant switch
        {
            >= 0.75f => new Vector3(y, -x, origin.Z),
            >= 0.5f => new Vector3(-x, -y, origin.Z),
            >= 0.25f => new Vector3(-y, x, origin.Z),
            _ => new Vector3(x, y, origin.Z),
        };
    }

    public static float ResizeObjRef(SceneryItem objRef, uint x, uint y, uint kdx)
    {
        float lo = objRef.MinScale;
        float hi = objRef.MaxScale;
        if (lo == hi)
            return hi;
        return (float)(Math.Pow(hi / lo, Jitter(x, y, kdx, ScalingSalt) * UnitScaling) * lo);
    }

    public static Quaternion SpinObjRef(SceneryItem objRef, uint x, uint y, uint kdx, Vector3 loc)
    {
        Quaternion rest = BaseFacing(objRef);
        if (objRef.MaxRotation <= 0.0f)
            return rest;
        float deg = (float)(Jitter(x, y, kdx, SpinSalt) * UnitScaling * objRef.MaxRotation);
        return AssignBearing(rest, deg);
    }

    /// <summary>Turns the object to face away from a surface normal.</summary>
    public static Quaternion ObjRefAlign(SceneryItem objRef, Vector3 norm, float z, Vector3 loc)
    {
        Vector3 away = -norm;
        float bearing = (450.0f - MathF.Atan2(away.Y, away.X) * 180f / MathF.PI) % 360f;
        return AssignBearing(BaseFacing(objRef), bearing);
    }

    public static Quaternion AssignBearing(Quaternion facing, float deg)
    {
        float radians = deg * MathF.PI / 180f;
        Matrix4x4 basis = Matrix4x4.CreateFromQuaternion(facing);
        Vector3 aim = Vector3.Normalize(new Vector3(MathF.Sin(radians), MathF.Cos(radians), basis.M23 + basis.M13));

        // A degenerate heading leaves the orientation alone
        if (aim.LengthSquared() < 0.0001f)
            return facing;

        float bearingDeg = MathF.Atan2(aim.Y, aim.X) * 180f / MathF.PI;
        float yaw = -((450.0f - bearingDeg) % 360.0f) * MathF.PI / 180f;
        float pitch = MathF.Asin(aim.Z);
        return Quaternion.CreateFromYawPitchRoll(pitch, 0, yaw);
    }

    public static bool VerifySlope(SceneryItem objRef, float zNorm) =>
        zNorm >= objRef.MinSlope && zNorm <= objRef.MaxSlope;

    // The client's cell/index hash, salted per use
    private static uint Jitter(uint x, uint y, uint kdx, uint salt)
    {
        return unchecked(1813693831u * y - (kdx + salt) * (1360117743u * y * x + 1888038839u) - 1109124029u * x);
    }

    private static Quaternion BaseFacing(SceneryItem objRef)
    {
        return new(objRef.BaseLoc.Orientation.X, objRef.BaseLoc.Orientation.Y, objRef.BaseLoc.Orientation.Z, objRef.BaseLoc.Orientation.W);
    }
}
