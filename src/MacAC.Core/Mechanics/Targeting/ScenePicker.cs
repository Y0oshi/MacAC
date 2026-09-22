using System.Numerics;

namespace MacAC.Mechanics.Targeting;

/// <summary>Unprojects a mouse position into a world-space ray.</summary>
public static class ScenePicker
{
    private static readonly (Vector3, Vector3) NoRay = (Vector3.Zero, Vector3.Zero);

    public static (Vector3 Origin, Vector3 Direction) AssembleRay(
        float pointerX,
        float pointerY,
        float viewRectW,
        float viewRectH,
        Matrix4x4 lens,
        Matrix4x4 proj)
    {
        float ndcX = 2f * pointerX / viewRectW - 1f;
        float ndcY = 1f - 2f * pointerY / viewRectH;

        if (!Matrix4x4.Invert(lens * proj, out Matrix4x4 unproject)
            || !Matrix4x4.Invert(lens, out Matrix4x4 toRealm))
            return NoRay;

        if (!Unproject(new Vector4(ndcX, ndcY, -1f, 1f), unproject, out Vector3 nearby)
            || !Unproject(new Vector4(ndcX, ndcY, 1f, 1f), unproject, out Vector3 faraway))
            return NoRay;

        Vector3 span = faraway - nearby;
        if (span.LengthSquared() < 1e-10f)
            return NoRay;

        return (Vector3.Transform(Vector3.Zero, toRealm), Vector3.Normalize(span));
    }

    private static bool Unproject(Vector4 clip, in Matrix4x4 unproject, out Vector3 realm)
    {
        Vector4 h = Vector4.Transform(clip, unproject);
        if (h.W == 0f)
        {
            realm = default;
            return false;
        }
        realm = new Vector3(h.X, h.Y, h.Z) / h.W;
        return true;
    }
}
