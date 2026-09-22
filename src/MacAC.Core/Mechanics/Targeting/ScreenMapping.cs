using System.Numerics;

namespace MacAC.Mechanics.Targeting;

public static class ScreenMapping
{
    private const float ClosestUsableW = 0.001f;

    public static bool TryProjectOrbToMonitorRect(
        Vector3 realmMiddle,
        float realmRadius,
        Matrix4x4 lens,
        Matrix4x4 proj,
        Vector2 viewRect,
        out Vector2 rectLower,
        out Vector2 rectUpper,
        out float zDepth,
        float lowerFlankPx = 12f)
    {
        rectLower = default;
        rectUpper = default;
        zDepth = 0f;

        Vector4 clip = Vector4.Transform(new Vector4(realmMiddle, 1f), lens * proj);
        if (clip.W <= ClosestUsableW)
            return false;
        zDepth = clip.W;

        float focal = proj.M22;
        if (focal <= 0f)
            return false;

        float x = (clip.X / clip.W * 0.5f + 0.5f) * viewRect.X;
        float y = (1f - (clip.Y / clip.W * 0.5f + 0.5f)) * viewRect.Y;
        float radius = realmRadius * focal * viewRect.Y / (2f * clip.W);

        bool offscreen =
            x + radius < -viewRect.X || x - radius > 2f * viewRect.X
            || y + radius < -viewRect.Y || y - radius > 2f * viewRect.Y;
        if (offscreen)
            return false;

        // Very distant spheres still get a clickable square
        radius = MathF.Max(radius, lowerFlankPx * 0.5f);

        rectLower = new Vector2(x - radius, y - radius);
        rectUpper = new Vector2(x + radius, y + radius);
        return true;
    }
}
