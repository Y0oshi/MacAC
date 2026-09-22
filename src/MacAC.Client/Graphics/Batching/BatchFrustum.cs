
using System.Numerics;

namespace MacAC.Client.Graphics.Batching;

public struct BatchBoundingBox(Vector3 lower, Vector3 upper)
{
    public Vector3 Min = lower;
    public Vector3 Max = upper;

    public static BatchBoundingBox Union(BatchBoundingBox a, BatchBoundingBox b)
    {
        return new(
            Vector3.Min(a.Min, b.Min),
            Vector3.Max(a.Max, b.Max));
    }
}

public enum FrustumTestOutcome
{
    Outside,
    Inside,
    Intersecting
}

public sealed class BatchFrustum
{
    private struct Facet
    {
        public Vector3 Normal;
        public float D;

        public Facet(float a, float b, float c, float d)
        {
            Normal = new Vector3(a, b, c);
            float len = Normal.Length();
            Normal /= len;
            D = d / len;
        }

        public float Dot(Vector3 pt) => Vector3.Dot(Normal, pt) + D;
    }

    private readonly Facet[] _planes = new Facet[6];
    private readonly object _mutex = new();

    public void Update(Matrix4x4 matrix)
    {
        lock (_mutex)
        {
            _planes[0] = new Facet(matrix.M14 + matrix.M11, matrix.M24 + matrix.M21, matrix.M34 + matrix.M31, matrix.M44 + matrix.M41);
            _planes[1] = new Facet(matrix.M14 - matrix.M11, matrix.M24 - matrix.M21, matrix.M34 - matrix.M31, matrix.M44 - matrix.M41);
            _planes[2] = new Facet(matrix.M14 + matrix.M12, matrix.M24 + matrix.M22, matrix.M34 + matrix.M32, matrix.M44 + matrix.M42);
            // Top plane
            _planes[3] = new Facet(matrix.M14 - matrix.M12, matrix.M24 - matrix.M22, matrix.M34 - matrix.M32, matrix.M44 - matrix.M42);
            _planes[4] = new Facet(matrix.M14 + matrix.M13, matrix.M24 + matrix.M23, matrix.M34 + matrix.M33, matrix.M44 + matrix.M43);
            // Far plane
            _planes[5] = new Facet(matrix.M14 - matrix.M13, matrix.M24 - matrix.M23, matrix.M34 - matrix.M33, matrix.M44 - matrix.M43);
        }
    }

    public bool Intersects(BatchBoundingBox bbox, bool ignoreNearbyPlane = false)
    {
        lock (_mutex)
        {
            for (int idx = 0; idx < 6; ++idx)
            {
                if (ignoreNearbyPlane && idx is 4) continue;

                Vector3 positive = bbox.Min;
                if (_planes[idx].Normal.X >= 0) positive.X = bbox.Max.X;
                if (_planes[idx].Normal.Y >= 0) positive.Y = bbox.Max.Y;
                if (_planes[idx].Normal.Z >= 0) positive.Z = bbox.Max.Z;

                if (_planes[idx].Dot(positive) < 0)

                    return false;
            }
        }
        return true;
    }

    public FrustumTestOutcome AssessBbox(BatchBoundingBox bbox, bool ignoreNearbyPlane = false)
    {
        var outcome = FrustumTestOutcome.Inside;
        lock (_mutex)
        {
            for (int idx = 0; idx < 6; ++idx)
            {
                if (ignoreNearbyPlane && idx is 4) continue;

                Vector3 positive = bbox.Min;
                Vector3 negative = bbox.Max;
                if (_planes[idx].Normal.X >= 0)
                {
                    positive.X = bbox.Max.X;
                    negative.X = bbox.Min.X;
                }
                if (_planes[idx].Normal.Y >= 0)
                {
                    positive.Y = bbox.Max.Y;
                    negative.Y = bbox.Min.Y;
                }
                if (_planes[idx].Normal.Z >= 0)
                {
                    positive.Z = bbox.Max.Z;
                    negative.Z = bbox.Min.Z;
                }

                if (_planes[idx].Dot(positive) < 0)

                    return FrustumTestOutcome.Outside;
                if (_planes[idx].Dot(negative) < 0)

                    outcome = FrustumTestOutcome.Intersecting;
            }
        }
        return outcome;
    }
}
