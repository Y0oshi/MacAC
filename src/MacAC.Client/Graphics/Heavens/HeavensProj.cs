using System.Numerics;

namespace MacAC.Client.Graphics.Heavens;

internal static class HeavensProj
{
    public static Matrix4x4 WithZDepthSpan(
        in Matrix4x4 activeProjection,
        float nearby,
        float far)
    {
        if (!float.IsFinite(nearby) || !float.IsFinite(far)
            || nearby <= 0f || far <= nearby)
        {
            throw new ArgumentOutOfRangeException(
                nameof(far),
                "Sky depth range has to be finite with 0 < near < far");
        }

        Matrix4x4 outcome = activeProjection;

        if (activeProjection.M34 < 0f)
        {
            outcome.M33 = far / (nearby - far);
            outcome.M43 = nearby * far / (nearby - far);
        }
        else if (activeProjection.M34 > 0f)
        {
            outcome.M33 = far / (far - nearby);
            outcome.M43 = -nearby * far / (far - nearby);
        }
        else
        {
            throw new ArgumentException(
                "Sky projection has to be perspective (M34 can't be zero)",
                nameof(activeProjection));
        }

        return outcome;
    }
}
