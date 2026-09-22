using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public static class KineticObjUpdate
{
    public static bool IsPassableLink(bool inLink, Vector3 linkNorm) =>
        inLink && linkNorm.Z >= KineticConstants.FloorZ;

    public static void ImposeSetLocusLink(KineticBody corpus, bool inLink, bool onPassable)
    {
        Set(corpus, TransientPhaseFlagSet.Contact, inLink);
        Set(corpus, TransientPhaseFlagSet.OnWalkable, inLink && onPassable);
        Set(corpus, TransientPhaseFlagSet.WaterContact, corpus.ContactPlaneIsWater);
        corpus.calc_acceleration();
    }

    public static bool SealSetLocusChangeover(
        KineticBody corpus,
        bool inLink,
        bool onPassable,
        bool impactNormValid,
        Vector3 impactNorm,
        bool earlierLink,
        bool earlierOnPassable,
        Action? strikeTerrain = null,
        Action? departTerrain = null,
        Func<bool>? isLatest = null,
        Func<bool>? isVelLatest = null)
    {
        if (!SealSetLocusLinkChangeover(corpus, inLink, onPassable, earlierOnPassable, strikeTerrain, departTerrain, isLatest))
            return false;

        if (isVelLatest?.Invoke() == false)
            return isLatest?.Invoke() ?? true;

        HandleAllCollisions(corpus, impactNormValid, impactNorm, earlierLink, earlierOnPassable, corpus.OnWalkable);
        return isLatest?.Invoke() ?? true;
    }

    public static bool SealSetLocusLinkChangeover(
        KineticBody corpus,
        bool inLink,
        bool onPassable,
        bool earlierOnPassable,
        Action? strikeTerrain = null,
        Action? departTerrain = null,
        Func<bool>? isLatest = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        bool instantOnPassable = SealSetLocusLinkStem(corpus, inLink, onPassable, earlierOnPassable);

        Action? terrainSignal = (previousOnWalkable: earlierOnPassable, nowOnWalkable: instantOnPassable) switch
        {
            (false, true) => strikeTerrain,
            (true, false) => departTerrain,
            _ => null,
        };
        if (earlierOnPassable != instantOnPassable)
        {
            terrainSignal?.Invoke();
            if (isLatest?.Invoke() == false)
                return false;
        }

        SealSetLocusPostTerrain(corpus);
        return isLatest?.Invoke() ?? true;
    }

    public static bool SealSetLocusLinkStem(KineticBody corpus, bool inLink, bool onPassable, bool earlierOnPassable)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        Set(corpus, TransientPhaseFlagSet.OnWalkable, earlierOnPassable);
        Set(corpus, TransientPhaseFlagSet.Contact, inLink);
        corpus.calc_acceleration();

        bool instantOnPassable = inLink && onPassable;
        Set(corpus, TransientPhaseFlagSet.OnWalkable, instantOnPassable);
        Set(corpus, TransientPhaseFlagSet.WaterContact, corpus.ContactPlaneIsWater);
        return instantOnPassable;
    }

    public static void SealSetLocusPostTerrain(KineticBody corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        corpus.calc_acceleration();
    }

    public static void HandleAllCollisions(
        KineticBody corpus,
        bool impactNormValid, Vector3 impactNorm,
        bool earlierLink, bool earlierOnPassable, bool instantOnPassable)
    {
        _ = earlierLink;

        if (corpus.FramesStationaryFall > 1)
        {
            corpus.Velocity = Vector3.Zero;   // fsf>1 → THE BLEED (pc:282729)
            return;
        }

        bool sledding = (corpus.State & KineticStateFlags.Sledding) != 0;
        bool stayedOnFloor = earlierOnPassable && instantOnPassable && !sledding;
        if (stayedOnFloor || !impactNormValid)
            return;

        if ((corpus.State & KineticStateFlags.Inelastic) != 0)
        {
            corpus.Velocity = Vector3.Zero;   // pc:282720-282722
            return;
        }

        float into = Vector3.Dot(corpus.Velocity, impactNorm);
        if (into < 0f)   // moving INTO the surface
        {
            float kdx = -(into * (corpus.Elasticity + 1f));   // pc:282712
            corpus.Velocity += impactNorm * kdx;
        }
    }

    private static void Set(KineticBody corpus, TransientPhaseFlagSet bit, bool on)
    {
        if (on)
            corpus.TransientState |= bit;
        else
            corpus.TransientState &= ~bit;
    }
}
