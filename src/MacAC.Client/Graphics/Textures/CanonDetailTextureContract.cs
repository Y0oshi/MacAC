using System.Numerics;

namespace MacAC.Client.Graphics;

internal static class CanonDetailTextureContract
{
    internal const int NoFogRasterizePassBit = 0x200;

    internal static bool ShouldRasterize(
        bool settingTurnedOn,
        LandTileset.CanonDetailTextureWiring mapping) =>
        settingTurnedOn && mapping.IsAvailable;

    internal static Vector4 Combine(
        Vector4 baseTexel,
        Vector3 diffuse,
        Vector4 specifics,
        float authoredDensity,
        float onlineDensity)
    {
        float alpha = authoredDensity * onlineDensity;
        float weight = alpha * specifics.W;
        Vector3 colour = new(specifics.X, specifics.Y, specifics.Z);
        colour *= weight;
        colour += new Vector3(baseTexel.X, baseTexel.Y, baseTexel.Z)
            * diffuse * (1f - weight);
        float productAlpha = alpha * specifics.W * specifics.W;
        return new Vector4(colour, productAlpha);
    }

    internal static Vector4 ApplyFog(Vector4 matl, Vector3 fog, float fogFactor)
    {
        return new(Vector3.Lerp(new Vector3(matl.X, matl.Y, matl.Z), fog, fogFactor), matl.W);
    }

    // Mirrors D3D's GREATER_EQUAL alpha test: equality survives
    internal static bool SurvivesClip(float productAlpha, float reference) =>
        productAlpha >= reference;

    internal enum FramebufferClan
    {
        Opaque,
        Alpha,
        AlphaAdditive,
        Additive,
        InverseAlpha,
        InverseAlphaAdditive,
        Clip,
    }

    internal static Vector4 Composite(
        Vector4 src,
        Vector4 dest,
        FramebufferClan family)
    {
        float x = src.W;
        return family switch
        {
            FramebufferClan.Opaque => src,
            FramebufferClan.Alpha => src * x + dest * (1f - x),
            FramebufferClan.AlphaAdditive => src * x + dest,
            FramebufferClan.Additive => src + dest,
            FramebufferClan.InverseAlpha => src * (1f - x) + dest * x,
            FramebufferClan.InverseAlphaAdditive => src * (1f - x) + dest,
            FramebufferClan.Clip => src + dest * new Vector4(1f - x),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }
}
