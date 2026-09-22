using System.Numerics;

namespace MacAC.Mechanics.Effects;

public static class CanonParticleFacing
{
    private const uint FaceBeholder = 2u;
    private const uint SpinAboutX = 3u;
    private const uint SpinAboutY = 4u;
    private const uint SpinAboutZ = 5u;

    public static bool Faces(uint downgradeManner) => downgradeManner is >= FaceBeholder and <= SpinAboutZ;

    public static (Vector3 XDir, Vector3 YDir) OrientQuad(
        uint downgradeManner,
        Quaternion orientation,
        Vector3 ownAxisX,
        Vector3 ownAxisY,
        Vector3 toBeholderUnit,
        Vector3 backupRight,
        Vector3 backupUp)
    {
        if (downgradeManner == FaceBeholder)
            return RollSpareTowards(toBeholderUnit, backupRight, backupUp);

        Vector3 realmX = Vector3.Transform(ownAxisX, orientation);
        Vector3 realmY = Vector3.Transform(ownAxisY, orientation);
        if (downgradeManner is < SpinAboutX or > SpinAboutZ)
            return (realmX, realmY);

        Vector3 axis = Vector3.Transform(
            downgradeManner switch
            {
                SpinAboutX => Vector3.UnitX,
                SpinAboutY => Vector3.UnitY,
                _ => Vector3.UnitZ,
            },
            orientation);

        Vector3 norm = Vector3.Cross(realmX, realmY);
        if (norm.LengthSquared() < 1e-10f)
            return (realmX, realmY);
        norm = Vector3.Normalize(norm);

        // Project both the quad normal and the viewer direction into the plane perpendicular to the axis.
        Vector3 mark = toBeholderUnit - axis * Vector3.Dot(toBeholderUnit, axis);
        Vector3 facing = norm - axis * Vector3.Dot(norm, axis);
        if (mark.LengthSquared() < 1e-8f || facing.LengthSquared() < 1e-8f)
            return (realmX, realmY);

        mark = Vector3.Normalize(mark);
        facing = Vector3.Normalize(facing);
        float cos = Math.Clamp(Vector3.Dot(facing, mark), -1f, 1f);
        float sin = Vector3.Dot(Vector3.Cross(facing, mark), axis);
        Quaternion spin = Quaternion.CreateFromAxisAngle(axis, MathF.Atan2(sin, cos));
        return (Vector3.Transform(realmX, spin), Vector3.Transform(realmY, spin));
    }

    private static (Vector3 XDir, Vector3 YDir) RollSpareTowards(Vector3 toBeholderUnit, Vector3 backupRight, Vector3 backupUp)
    {
        Vector3 right = Vector3.Cross(toBeholderUnit, Vector3.UnitZ);
        if (right.LengthSquared() < 1e-8f)
            return (backupRight, backupUp);
        right = Vector3.Normalize(right);
        return (right, Vector3.Cross(right, toBeholderUnit));
    }
}
