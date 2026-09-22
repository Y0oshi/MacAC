using System.Numerics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Packs;

internal enum AuthoredCelestialShadeSourceKind : uint
{
    None = 0,
    Sun = 1,
    DominantMoon = 2,
    SecondaryMoon = 3,
}

internal readonly record struct AuthoredCelestialShadeSource(
    AuthoredCelestialShadeSourceKind Kind,
    int ObjectIndex,
    uint GfxObjId,
    Vector3 SurfaceToLightDirection,
    float ElevationSin,
    float AuthoredEnergy)
{
    internal static AuthoredCelestialShadeSource None(float authoredEnergy = 0f)
    {
        return new(
            AuthoredCelestialShadeSourceKind.None,
            -1,
            0u,
            Vector3.UnitZ,
            0f,
            Math.Clamp(authoredEnergy, 0f, 1f));
    }

    internal bool IsOnHand =>
        Kind is not AuthoredCelestialShadeSourceKind.None;
}

internal static class AuthoredCelestialShadeSourcePicker
{
    internal const uint SunGfxObjRefIdent = 0x01001348u;
    internal const uint DominantMoonGfxObjRefIdent = 0x01001F6Au;
    internal const uint SecondaryMoonGfxObjRefIdent = 0x01001F67u;

    internal static AuthoredCelestialShadeSource Resolve(
        DayGroupRow? dayCluster,
        float dayRatio,
        in SkyKeyframe heavens)
    {
        float energy = Math.Clamp(
            MathF.Max(heavens.SunColor.X, MathF.Max(heavens.SunColor.Y, heavens.SunColor.Z)),
            0f,
            1f);
        if (dayCluster is null || !float.IsFinite(dayRatio))
            return AuthoredCelestialShadeSource.None(energy);

        return TryLocate(
                dayCluster,
                dayRatio,
                SunGfxObjRefIdent,
                AuthoredCelestialShadeSourceKind.Sun,
                energy,
                out var src)
            || TryLocate(
                dayCluster,
                dayRatio,
                DominantMoonGfxObjRefIdent,
                AuthoredCelestialShadeSourceKind.DominantMoon,
                energy,
                out src)
            || TryLocate(
                dayCluster,
                dayRatio,
                SecondaryMoonGfxObjRefIdent,
                AuthoredCelestialShadeSourceKind.SecondaryMoon,
                energy,
                out src)
            ? src
            : AuthoredCelestialShadeSource.None(energy);
    }

    private static bool TryLocate(
        DayGroupRow dayCluster,
        float dayRatio,
        uint roleGfxObjRefIdent,
        AuthoredCelestialShadeSourceKind sort,
        float energy,
        out AuthoredCelestialShadeSource src)
    {
        for (int ordinal = 0; ordinal < dayCluster.SkyObjects.Count; ++ordinal)
        {
            var heavensObject = dayCluster.SkyObjects[ordinal];
            if (heavensObject.GfxObjId != roleGfxObjRefIdent
                || !heavensObject.IsVisible(dayRatio))

                continue;

            var replace = EngagedReplace(
                dayCluster,
                dayRatio,
                checked((uint)ordinal));
            if (replace is not null && replace.Transparent >= 1f - 1e-5f)
                continue;

            uint netGfxObjRefIdent = replace is { GfxObjId: not 0u }
                ? replace.GfxObjId
                : heavensObject.GfxObjId;
            Vector3 mooring = replace is { GfxObjId: not 0u }
                ? replace.AuthoredOrderCenter
                : heavensObject.AuthoredOrderMiddle;
            if (!IsFiniteDir(mooring))
                continue;

            float bearingRadians = (replace?.Rotate ?? 0f) * (MathF.PI / 180f);
            float spinRadians = heavensObject.LatestAngle(dayRatio)
                * (MathF.PI / 180f);
            Matrix4x4 model = Matrix4x4.CreateRotationZ(-bearingRadians)
                * Matrix4x4.CreateRotationY(-spinRadians);
            Vector3 transformed = Vector3.TransformNormal(mooring, model);
            float len = transformed.Length();
            if (!float.IsFinite(len) || len <= 1e-5f)
                continue;

            Vector3 dir = transformed / len;
            if (!IsFiniteDir(dir) || dir.Z <= 0f)
                continue;

            src = new AuthoredCelestialShadeSource(
                sort,
                ordinal,
                netGfxObjRefIdent,
                dir,
                dir.Z,
                energy);
            return true;
        }

        src = default;
        return false;
    }

    private static SkyObjectSwapRow? EngagedReplace(
        DayGroupRow dayCluster,
        float dayRatio,
        uint objectOrdinal)
    {
        if (dayCluster.HeavensTimes.Count is 0)
            return null;

        DatSkyKeyframeRow engaged = dayCluster.HeavensTimes[^1];
        for (int idx = 0; idx < dayCluster.HeavensTimes.Count; ++idx)
        {
            if (dayCluster.HeavensTimes[idx].Keyframe.Begin <= dayRatio)
                engaged = dayCluster.HeavensTimes[idx];
            else
                break;
        }

        SkyObjectSwapRow? outcome = null;
        foreach (SkyObjectSwapRow replace in engaged.Replaces)
        {
            if (replace.ObjectIndex == objectOrdinal)
                outcome = replace;
        }
        return outcome;
    }

    private static bool IsFiniteDir(Vector3 val)
    {
        return float.IsFinite(val.X)
        && float.IsFinite(val.Y)
        && float.IsFinite(val.Z)
        && val.LengthSquared() > 1e-10f;
    }
}
