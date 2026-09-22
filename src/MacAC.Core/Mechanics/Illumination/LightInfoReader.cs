using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Illumination;

/// <summary>Instantiates the lights a Setup declares, placed in world space.</summary>
public static class LightInfoReader
{
    private const float DynamicReach = 1.5f;
    private const float StaticReach = 1.3f;

    public static IReadOnlyList<LightEmitter> Load(
        RigSpec rig,
        uint holderIdent,
        Vector3 actorLocus,
        Quaternion actorSpin,
        bool isDynamic = false,
        uint chamberIdent = 0,
        bool tracksHolderPosture = false)
    {
        List<LightEmitter> lamps = new List<LightEmitter>();
        if (rig?.Lamps is not { Count: > 0 } declared)
            return lamps;

        Matrix4x4 trunkRealm =
            Matrix4x4.CreateFromQuaternion(actorSpin) * Matrix4x4.CreateTranslation(actorLocus);

        foreach (LampSpec details in declared.Values)
        {
            if (details is null)
                continue;

            Matrix4x4 ownPosture = Matrix4x4.Identity;
            if (details.ViewSpaceLocation is { } cycle)
            {
                Quaternion spin = new Quaternion(
                    cycle.Orientation.X, cycle.Orientation.Y, cycle.Orientation.Z, cycle.Orientation.W);
                Vector3 shift = new Vector3(cycle.Origin.X, cycle.Origin.Y, cycle.Origin.Z);
                ownPosture = Matrix4x4.CreateFromQuaternion(spin) * Matrix4x4.CreateTranslation(shift);
            }

            Matrix4x4 lampRealm = ownPosture * trunkRealm;
            Vector3 ahead = Vector3.TransformNormal(Vector3.UnitY, lampRealm);
            if (ahead.LengthSquared() > 1e-8f)
                ahead = Vector3.Normalize(ahead);

            lamps.Add(new LightEmitter
            {
                Kind = details.ConeAngle > 0f ? LampFlavor.Spot : LampFlavor.Point,
                RealmPosition = lampRealm.Translation,
                RankingOrigin = actorLocus,
                RealmAhead = ahead,
                TintLinear = new Vector3(
                    (details.Color?.Red ?? 255) / 255f,
                    (details.Color?.Green ?? 255) / 255f,
                    (details.Color?.Blue ?? 255) / 255f),
                Intensity = details.Intensity,
                Range = details.Falloff * (isDynamic ? DynamicReach : StaticReach),
                ConeAngle = details.ConeAngle,
                HolderTag = holderIdent,
                CellId = chamberIdent,
                IsLit = true,
                IsDynamic = isDynamic,
                TracksHolderPosture = tracksHolderPosture,
                OwnPosture = ownPosture,
            });
        }

        return lamps;
    }
}
