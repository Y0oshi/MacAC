using System.Numerics;

namespace MacAC.Client.Graphics;

internal readonly record struct DirectionalShadeCascadeFitInput(
    Matrix4x4 CameraView,
    Matrix4x4 CameraProjection,
    Vector3 SurfaceToLightDirection,
    DirectionalShadeQuality Quality,
    float CameraNearMeters = 0.1f,
    float PracticalSplitLambda = 0.65f,
    float CasterDepthPaddingMeters = 48f,
    float ResidentMaximumReachMeters = float.PositiveInfinity);

internal readonly record struct DirectionalShadeCascade(
    int Index,
    float SplitNearMeters,
    float SplitFarMeters,
    Matrix4x4 LightView,
    Matrix4x4 LightProjection,
    Matrix4x4 WorldToShadowClip,
    Vector2 StabilizedLightSpaceCenter,
    float HalfExtentMeters,
    float TexelWorldSize,
    float CasterDepthPaddingMeters,
    DirectionalShadeRealmBias Bias);

internal static class DirectionalShadeCascadeFitter
{
    private const float RadiusQuantizationMeters = 1f / 16f;

    public static int Fit(
        in DirectionalShadeCascadeFitInput input,
        Span<DirectionalShadeCascade> dest)
    {
        Validate(in input, dest.Length);
        if (!Matrix4x4.Invert(input.CameraView, out Matrix4x4 invLens))
            throw new ArgumentException("Camera view matrix isn't invertible", nameof(input));
        if (!Matrix4x4.Invert(input.CameraProjection, out Matrix4x4 invProj))
            throw new ArgumentException("Camera projection matrix isn't invertible", nameof(input));

        Vector3 lampDir = Vector3.Normalize(input.SurfaceToLightDirection);
        float ceilingReach = MathF.Min(
            input.Quality.MaximumReachMeters,
            input.ResidentMaximumReachMeters);
        if (ceilingReach <= input.CameraNearMeters)
            return 0;
        float divideNearby = input.CameraNearMeters;
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int cascadeOrdinal = 0;
             cascadeOrdinal < input.Quality.CascadeCount;
             ++cascadeOrdinal)
        {
            float divideFaraway = PracticalDivide(
                input.CameraNearMeters,
                ceilingReach,
                cascadeOrdinal + 1,
                input.Quality.CascadeCount,
                input.PracticalSplitLambda);
            AssembleFrustumSliceCorners(
                invLens,
                invProj,
                divideNearby,
                divideFaraway,
                corners);
            dest[cascadeOrdinal] = FitCascade(
                cascadeOrdinal,
                divideNearby,
                divideFaraway,
                corners,
                lampDir,
                input.Quality.MapResolution,
                input.CasterDepthPaddingMeters,
                input.Quality.BiasPolicy);
            divideNearby = divideFaraway;
        }

        return input.Quality.CascadeCount;
    }

    internal static float PracticalDivide(
        float nearbyMeters,
        float farMeters,
        int splitIndex,
        int divideTally,
        float lambda)
    {
        if (!float.IsFinite(nearbyMeters)
            || !float.IsFinite(farMeters)
            || nearbyMeters <= 0f
            || farMeters <= nearbyMeters)

            throw new ArgumentOutOfRangeException(nameof(farMeters));
        if (divideTally <= 0 || splitIndex <= 0 || splitIndex > divideTally)
            throw new ArgumentOutOfRangeException(nameof(splitIndex));
        if (!float.IsFinite(lambda) || lambda < 0f || lambda > 1f)
            throw new ArgumentOutOfRangeException(nameof(lambda));

        float ratio = (float)splitIndex / divideTally;
        float logarithmic = nearbyMeters * MathF.Pow(farMeters / nearbyMeters, ratio);
        float uniform = nearbyMeters + (farMeters - nearbyMeters) * ratio;
        return lambda * logarithmic + (1f - lambda) * uniform;
    }

    internal static Vector3 StableLampUp(Vector3 canvasToLamp)
    {
        canvasToLamp = Vector3.Normalize(canvasToLamp);
        float sign = MathF.CopySign(1f, canvasToLamp.Z);
        float a = -1f / (sign + canvasToLamp.Z);
        float b = canvasToLamp.X * canvasToLamp.Y * a;
        return Vector3.Normalize(new Vector3(
            b,
            sign + canvasToLamp.Y * canvasToLamp.Y * a,
            -canvasToLamp.Y));
    }

    private static DirectionalShadeCascade FitCascade(
        int ordinal,
        float divideNearby,
        float divideFaraway,
        ReadOnlySpan<Vector3> corners,
        Vector3 canvasToLamp,
        int lookupResolution,
        float zDepthPadding,
        in DirectionalShadeBiasRule biasRule)
    {
        Vector3 middle = Vector3.Zero;
        for (int idx = 0; idx < corners.Length; ++idx)
            middle += corners[idx];
        middle /= corners.Length;

        float radius = 0f;
        for (int idx = 0; idx < corners.Length; ++idx)
            radius = MathF.Max(radius, Vector3.Distance(middle, corners[idx]));
        radius = MathF.Ceiling(radius / RadiusQuantizationMeters)
            * RadiusQuantizationMeters;
        radius = MathF.Max(radius, RadiusQuantizationMeters);

        Vector3 up = StableLampUp(canvasToLamp);
        Matrix4x4 lampSpin = Matrix4x4.CreateLookAt(
            Vector3.Zero,
            -canvasToLamp,
            up);

        Vector3 lampMiddle = Vector3.Transform(middle, lampSpin);
        float texelRealmDims = (2f * radius) / lookupResolution;
        float snappedX = SnapToTexel(lampMiddle.X, texelRealmDims);
        float snappedY = SnapToTexel(lampMiddle.Y, texelRealmDims);

        float lowerZ = float.PositiveInfinity;
        float upperZ = float.NegativeInfinity;
        for (int idx = 0; idx < corners.Length; ++idx)
        {
            float z = Vector3.Transform(corners[idx], lampSpin).Z;
            lowerZ = MathF.Min(lowerZ, z);
            upperZ = MathF.Max(upperZ, z);
        }

        float eyePtAxis = upperZ + zDepthPadding;
        Vector3 eyePt = canvasToLamp * eyePtAxis;
        Matrix4x4 lampLens = Matrix4x4.CreateLookAt(
            eyePt,
            eyePt - canvasToLamp,
            up);
        float nearbyPlane = 0.1f;
        float farawayPlane = MathF.Max(
            nearbyPlane + 0.1f,
            (upperZ - lowerZ) + 2f * zDepthPadding);
        Matrix4x4 lampProj = Matrix4x4.CreateOrthographicOffCenter(
            snappedX - radius,
            snappedX + radius,
            snappedY - radius,
            snappedY + radius,
            nearbyPlane,
            farawayPlane);

        return new DirectionalShadeCascade(
            ordinal,
            divideNearby,
            divideFaraway,
            lampLens,
            lampProj,
            lampLens * lampProj,
            new Vector2(snappedX, snappedY),
            radius,
            texelRealmDims,
            zDepthPadding,
            biasRule.Resolve(texelRealmDims));
    }

    private static void AssembleFrustumSliceCorners(
        Matrix4x4 invLens,
        Matrix4x4 invProj,
        float nearbyMeters,
        float farawayMeters,
        Span<Vector3> dest)
    {
        int cur = 0;
        for (int zDepthOrdinal = 0; zDepthOrdinal < 2; ++zDepthOrdinal)
        {
            float gap = zDepthOrdinal is 0 ? nearbyMeters : farawayMeters;
            for (int yOrdinal = 0; yOrdinal < 2; ++yOrdinal)
            {
                float y = yOrdinal is 0 ? -1f : 1f;
                for (int xOrdinal = 0; xOrdinal < 2; ++xOrdinal)
                {
                    float x = xOrdinal is 0 ? -1f : 1f;
                    Vector4 lensCorner = Vector4.Transform(
                        new Vector4(x, y, 1f, 1f),
                        invProj);
                    if (MathF.Abs(lensCorner.W) <= 1e-6f)
                        throw new ArgumentException("Camera projection produced a corner at infinity");
                    Vector3 lens = new(
                        lensCorner.X / lensCorner.W,
                        lensCorner.Y / lensCorner.W,
                        lensCorner.Z / lensCorner.W);
                    float lensZDepth = MathF.Abs(lens.Z);
                    if (lensZDepth <= 1e-6f)
                        throw new ArgumentException("Camera projection produced zero view depth");
                    lens *= gap / lensZDepth;
                    dest[cur++] = Vector3.Transform(lens, invLens);
                }
            }
        }
    }

    private static float SnapToTexel(float val, float texelRealmDims)
    {
        return MathF.Round(val / texelRealmDims, MidpointRounding.AwayFromZero)
        * texelRealmDims;
    }

    private static void Validate(
        in DirectionalShadeCascadeFitInput input,
        int destLen)
    {
        var fidelity = input.Quality;
        if (fidelity.CascadeCount is <= 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(input), "Cascade count has to be in [1,4]");
        if (destLen < fidelity.CascadeCount)
            throw new ArgumentException("Destination can't hold every configured cascade");
        if (fidelity.MapResolution <= 0
            || !float.IsFinite(fidelity.MaximumReachMeters)
            || fidelity.MaximumReachMeters <= input.CameraNearMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Shadow quality dimensions are not valid");
        }
        if (!float.IsFinite(input.CameraNearMeters) || input.CameraNearMeters <= 0f)
            throw new ArgumentOutOfRangeException(nameof(input), "Camera near distance has to be positive");
        if (float.IsNaN(input.ResidentMaximumReachMeters)
            || input.ResidentMaximumReachMeters < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Resident shadow reach has to be nonnegative or positive infinity");
        }
        if (!float.IsFinite(input.PracticalSplitLambda)
            || input.PracticalSplitLambda < 0f
            || input.PracticalSplitLambda > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Split lambda has to be in [0,1]");
        }
        if (!float.IsFinite(input.CasterDepthPaddingMeters)
            || input.CasterDepthPaddingMeters <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Caster depth padding has to be positive");
        }
        float lampLen = input.SurfaceToLightDirection.Length();
        if (!float.IsFinite(lampLen) || lampLen <= 1e-6f)
            throw new ArgumentOutOfRangeException(nameof(input), "Light direction has to be finite and nonzero");
    }
}
