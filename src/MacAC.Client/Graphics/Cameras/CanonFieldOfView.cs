namespace MacAC.Client.Graphics;

public static class CanonFieldOfView
{
    public const float DefaultPlayFovRadians = 1.57079637f;

    public const float AspectBias = 0.100000001f;

    public static readonly float DefaultImposedFovY = CalculateDefault();

    private static float CalculateDefault()
    {
        bool ok = TryImposedVerticalFov(DefaultPlayFovRadians, 16f / 9f, out float fovY);
        System.Diagnostics.Debug.Assert(ok, "the default game FOV/aspect pair must satisfy the SetFOVRad gate");
        return fovY;
    }

    public static bool TryImposedVerticalFov(float playFovRadians, float aspect, out float fovY)
    {
        float divisor = aspect - AspectBias;
        if (divisor <= 0f)
        {
            fovY = float.NaN;
            return false;
        }

        fovY = playFovRadians / divisor;
        return fovY is > 0f and < MathF.PI;
    }
}
