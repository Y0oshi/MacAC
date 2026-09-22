using System.Numerics;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Play;

public static class SimAllyTargetProbe
{
    private const KineticStateFlags Invisible = KineticStateFlags.Hidden | KineticStateFlags.NoDraw;

    public static uint? SeekClosestAnotherAvatar(SimCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return Nearest(core, static _ => true);
    }

    public static uint? SeekAvatarByLabel(SimCore core, string label)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentException.ThrowIfNullOrEmpty(label);
        return Nearest(core, capture => string.Equals(capture.Snapshot.Name, label, StringComparison.Ordinal));
    }

    public static string? TryFetchLabel(SimCore core, uint oid)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.EntityObjects.Entities.TryFetchEngaged(oid, out SimActorRecord capture) ? capture.Snapshot.Name : null;
    }

    /// <summary>Horizontal live-world distance from the local player.</summary>
    public static bool TryFetchGap(SimCore core, uint oid, out float gap)
    {
        ArgumentNullException.ThrowIfNull(core);
        var actors = core.EntityObjects.Entities;
        if (Self(core) is not { } self
            || !actors.TryFetchEngaged(oid, out SimActorRecord mark)
            || mark.Snapshot.Position is not { } markLocus
            || (mark.FinalKineticsCondition & Invisible) != 0)
        {
            gap = float.PositiveInfinity;
            return false;
        }

        Vector3 to = RemotePositionMath.ToRealm(markLocus);
        gap = Vector2.Distance(new Vector2(self.World.X, self.World.Y), new Vector2(to.X, to.Y));
        return true;
    }

    // The closest visible player other than the local one that passes admit
    private static uint? Nearest(SimCore core, Func<SimActorRecord, bool> admit)
    {
        if (Self(core) is not { } self)
            return null;

        uint? finest = null;
        float finestGapSq = float.PositiveInfinity;
        foreach (SimActorRecord capture in core.EntityObjects.Entities.ActiveRecords)
        {
            if (capture.ServerGuid == self.Guid
                || capture.Snapshot.Position is not { } locus
                || (capture.FinalKineticsCondition & Invisible) != 0
                || !IsPlayer(capture)
                || !admit(capture))

                continue;

            float gapSq = Vector3.DistanceSquared(self.World, RemotePositionMath.ToRealm(locus));
            if (gapSq < finestGapSq)
            {
                finestGapSq = gapSq;
                finest = capture.ServerGuid;
            }
        }
        return finest;
    }

    // The local player's guid and world position, or null before the player is placed
    private static (uint Guid, Vector3 World)? Self(SimCore core)
    {
        uint oid = core.AvatarIdentity.ServerGuid;
        if (oid is 0u
            || !core.EntityObjects.Entities.TryFetchEngaged(oid, out SimActorRecord capture)
            || capture.Snapshot.Position is not { } locus)

            return null;
        return (oid, RemotePositionMath.ToRealm(locus));
    }

    private static bool IsPlayer(SimActorRecord capture)
    {
        return (EntityContactFlagsExt.FromPwdBitfield(capture.Snapshot.ObjectDescriptionFlags ?? 0u) & ActorImpactFlagSet.IsPlayer) != 0;
    }
}
