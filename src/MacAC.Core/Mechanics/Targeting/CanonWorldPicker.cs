using System.Numerics;

namespace MacAC.Mechanics.Targeting;

public static class CanonWorldPicker
{
    private const double RayEpsilon = 0.0002;
    private const float DegenerateDir = 1e-10f;

    public static CanonPickHit? Pick(
        Vector3 realmOrigin,
        Vector3 realmDir,
        IEnumerable<CanonPickPart> shownPieces,
        uint skipSrvOid = 0u)
    {
        if (realmDir.LengthSquared() < DegenerateDir)
            return null;

        CanonPickHit? finestOrb = null;
        CanonPickHit? finestPolyg = null;

        foreach (CanonPickPart piece in shownPieces)
        {
            if (piece.ServerGuid is 0u || piece.ServerGuid == skipSrvOid)
                continue;
            if (piece.Mesh.SphereRadius <= 0f)
                continue;
            if (!Matrix4x4.Invert(piece.LocalToWorld, out Matrix4x4 toOwn))
                continue;

            Vector3 origin = Vector3.Transform(realmOrigin, toOwn);
            Vector3 dir = Vector3.TransformNormal(realmDir, toOwn);

            if (!TryIntersectOrb(origin, dir, piece.Mesh.SphereCenter, piece.Mesh.SphereRadius, out double orbAt))
                continue;
            if (finestPolyg is { Distance: var polygAt } && orbAt > polygAt)
                continue;

            if (finestOrb is null || orbAt < finestOrb.Value.Distance)
                finestOrb = Hit(piece, orbAt, polyg: false);

            foreach (CanonPickPolygon polyg in piece.Mesh.Polygons)
            {
                if (!TryIntersectPolyg(origin, dir, polyg, out double faceAt))
                    continue;
                if (finestPolyg is null || faceAt < finestPolyg.Value.Distance)
                    finestPolyg = Hit(piece, faceAt, polyg: true);
                break;
            }
        }

        return finestPolyg ?? finestOrb;
    }

    internal static bool TryIntersectOrb(
        Vector3 origin,
        Vector3 dir,
        Vector3 middle,
        float radius,
        out double gap)
    {
        gap = 0d;
        Vector3 toOrigin = origin - middle;
        double c = Vector3.Dot(toOrigin, toOrigin) - (double)radius * radius;
        if (c <= 0d)
            return false;

        double a = Vector3.Dot(dir, dir);
        if (a < RayEpsilon)
            return false;

        double b = -Vector3.Dot(toOrigin, dir);
        double discriminant = b * b - c * a;
        if (discriminant < 0d)
            return false;

        double trunk = Math.Sqrt(discriminant);
        gap = (b > trunk ? b - trunk : b + trunk) / a;
        return true;
    }

    internal static bool TryIntersectPolyg(
        Vector3 origin,
        Vector3 dir,
        CanonPickPolygon polyg,
        out double gap)
    {
        gap = 0d;
        var corners = polyg.Vertices;
        if (corners.Count < 3 || !TryFitPlane(corners, out Vector3 norm, out float shift))
            return false;

        double slope = Vector3.Dot(dir, norm);
        if (polyg.SingleSided && slope > 0d)
            return false;
        if (Math.Abs(slope) < RayEpsilon)
            return false;

        gap = -(Vector3.Dot(origin, norm) + shift) / slope;
        return gap < 0d ? false : Encloses(corners, norm, origin + dir * (float)gap);
    }

    private static CanonPickHit Hit(in CanonPickPart piece, double at, bool polyg) =>
        new(piece.ServerGuid, piece.LocalEntityId, piece.PartIndex, at, polyg);

    private static bool TryFitPlane(IReadOnlyList<Vector3> corners, out Vector3 norm, out float shift)
    {
        Vector3 mooring = corners[0];
        Vector3 fan = Vector3.Zero;
        for (int idx = 1; idx + 1 < corners.Count; ++idx)
            fan += Vector3.Cross(corners[idx] - mooring, corners[idx + 1] - mooring);

        if (fan.LengthSquared() <= 1e-12f)
        {
            norm = default;
            shift = 0f;
            return false;
        }

        norm = Vector3.Normalize(fan);
        double total = 0d;
        foreach (Vector3 corner in corners)
            total += Vector3.Dot(norm, corner);
        shift = (float)-(total / corners.Count);
        return true;
    }

    private static bool Encloses(IReadOnlyList<Vector3> corners, Vector3 norm, Vector3 pt)
    {
        Vector3 rear = corners[^1];
        foreach (Vector3 front in corners)
        {
            Vector3 inward = Vector3.Cross(norm, front - rear);
            if (Vector3.Dot(pt - rear, inward) < 0f)
                return false;
            rear = front;
        }
        return true;
    }
}
