using System.Numerics;

namespace MacAC.Client.Graphics.Batching;

public static class GeoAids
{

    public static bool RayIntersectsBbox(Vector3 rayOrigin, Vector3 rayDir, Vector3 lower, Vector3 upper, out float gap)
    {
        gap = 0;
        float tmin = 0.0f;
        float tmax = float.MaxValue;

        if (Math.Abs(rayDir.X) < 1e-7f)
        {
            if (rayOrigin.X < lower.X || rayOrigin.X > upper.X) return false;
        }
        else
        {
            float invD = 1.0f / rayDir.X;
            float t0 = (lower.X - rayOrigin.X) * invD;
            float t1 = (upper.X - rayOrigin.X) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tmin = Math.Max(tmin, t0);
            tmax = Math.Min(tmax, t1);
            if (tmin > tmax) return false;
        }

        if (Math.Abs(rayDir.Y) < 1e-7f)
        {
            if (rayOrigin.Y < lower.Y || rayOrigin.Y > upper.Y) return false;
        }
        else
        {
            float invD = 1.0f / rayDir.Y;
            float t0 = (lower.Y - rayOrigin.Y) * invD;
            float t1 = (upper.Y - rayOrigin.Y) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tmin = Math.Max(tmin, t0);
            tmax = Math.Min(tmax, t1);
            if (tmin > tmax) return false;
        }

        if (Math.Abs(rayDir.Z) < 1e-7f)
        {
            if (rayOrigin.Z < lower.Z || rayOrigin.Z > upper.Z) return false;
        }
        else
        {
            float invD = 1.0f / rayDir.Z;
            float t0 = (lower.Z - rayOrigin.Z) * invD;
            float t1 = (upper.Z - rayOrigin.Z) * invD;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tmin = Math.Max(tmin, t0);
            tmax = Math.Min(tmax, t1);
            if (tmin > tmax) return false;
        }

        gap = tmin;
        return true;
    }

    public static bool RayIntersectsTriangle(Vector3 origin, Vector3 dir, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        t = 0;
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 h = Vector3.Cross(dir, edge2);
        float a = Vector3.Dot(edge1, h);

        if (a is > -0.00001f and < 0.00001f) return false;

        float f = 1.0f / a;
        Vector3 s = origin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u is < 0.0f or > 1.0f) return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(dir, q);

        if (v < 0.0f || u + v > 1.0f) return false;

        t = f * Vector3.Dot(edge2, q);
        return t > 0.00001f;
    }

    public static bool RayIntersectsOrb(Vector3 rayOrigin, Vector3 rayDir, Vector3 orbOrigin, float orbRadius, out float gap)
    {
        gap = 0;
        Vector3 l = orbOrigin - rayOrigin;
        float tca = Vector3.Dot(l, rayDir);
        if (tca < 0) return false;
        float d2 = Vector3.Dot(l, l) - tca * tca;
        float r2 = orbRadius * orbRadius;
        if (d2 > r2) return false;
        float thc = MathF.Sqrt(r2 - d2);
        gap = tca - thc;
        return true;
    }

    public static ushort BundleTag(int x, int y) => (ushort)((x << 8) | y);

    public static Vector3 QuaternionToEuler(Quaternion q)
    {
        float x = q.X, y = q.Y, z = q.Z, w = q.W;
        float roll, pitch, yaw;

        float sinr_cosp = 2 * (w * x + y * z);
        float cosr_cosp = 1 - 2 * (x * x + y * y);
        roll = (float)Math.Atan2(sinr_cosp, cosr_cosp);

        float sinp = 2 * (w * y - z * x);
        pitch = Math.Abs(sinp) >= 1 ? (float)Math.CopySign(Math.PI / 2, sinp) : (float)Math.Asin(sinp);

        float siny_cosp = 2 * (w * z + x * y);
        float cosy_cosp = 1 - 2 * (y * y + z * z);
        yaw = (float)Math.Atan2(siny_cosp, cosy_cosp);

        return new Vector3(roll, pitch, yaw) * (180.0f / (float)Math.PI);
    }

    public static Quaternion EulerToQuaternion(Vector3 euler)
    {
        Vector3 rads = euler * (MathF.PI / 180.0f);
        float roll = rads.X;
        float pitch = rads.Y;
        float yaw = rads.Z;

        float cr = MathF.Cos(roll * 0.5f);
        float sr = MathF.Sin(roll * 0.5f);
        float cp = MathF.Cos(pitch * 0.5f);
        float sp = MathF.Sin(pitch * 0.5f);
        float cy = MathF.Cos(yaw * 0.5f);
        float sy = MathF.Sin(yaw * 0.5f);

        return new Quaternion(
            sr * cp * cy - cr * sp * sy,
            cr * sp * cy + sr * cp * sy,
            cr * cp * sy - sr * sp * cy,
            cr * cp * cy + sr * sp * sy
        );
    }
}
