using System.Numerics;

namespace MacAC.Client.Graphics;

public readonly struct FrustumFacets
{
    public readonly Vector4 Left;
    public readonly Vector4 Right;
    public readonly Vector4 Bottom;
    public readonly Vector4 Top;
    public readonly Vector4 Near;
    public readonly Vector4 Far;

    private FrustumFacets(Vector4 left, Vector4 right, Vector4 bottom, Vector4 top, Vector4 nearby, Vector4 faraway)
    {
        Left = left;
        Right = right;
        Bottom = bottom;
        Top = top;
        Near = nearby;
        Far = faraway;
    }

    public static FrustumFacets FromLensProj(Matrix4x4 vp)
    {
        Vector4 col1 = new Vector4(vp.M11, vp.M21, vp.M31, vp.M41);
        Vector4 col2 = new Vector4(vp.M12, vp.M22, vp.M32, vp.M42);
        Vector4 col3 = new Vector4(vp.M13, vp.M23, vp.M33, vp.M43);
        Vector4 col4 = new Vector4(vp.M14, vp.M24, vp.M34, vp.M44);

        Vector4 left = Standardize(col4 + col1);
        Vector4 right = Standardize(col4 - col1);
        Vector4 bottom = Standardize(col4 + col2);
        Vector4 top = Standardize(col4 - col2);
        Vector4 nearby = Standardize(col3);
        Vector4 faraway = Standardize(col4 - col3);

        return new FrustumFacets(left, right, bottom, top, nearby, faraway);
    }

    private static Vector4 Standardize(Vector4 plane)
    {
        float len = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
        return plane / len;
    }
}

public static class FrustumPruner
{
    public static bool IsAabbShown(FrustumFacets planes, Vector3 lower, Vector3 upper)
    {
        return AssessPlane(planes.Left, lower, upper)
            && AssessPlane(planes.Right, lower, upper)
            && AssessPlane(planes.Bottom, lower, upper)
            && AssessPlane(planes.Top, lower, upper)
            && AssessPlane(planes.Near, lower, upper)
            && AssessPlane(planes.Far, lower, upper);
    }

    private static bool AssessPlane(Vector4 plane, Vector3 lower, Vector3 upper)
    {
        float px = plane.X >= 0 ? upper.X : lower.X;
        float py = plane.Y >= 0 ? upper.Y : lower.Y;
        float pz = plane.Z >= 0 ? upper.Z : lower.Z;

        return plane.X * px + plane.Y * py + plane.Z * pz + plane.W >= 0;
    }
}
