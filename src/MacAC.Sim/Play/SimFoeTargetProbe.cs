using System.Numerics;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Traits;
using MacAC.Wire.Messages;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Play;

public readonly record struct SimFoeTargetCapture(
    uint ObjectId,
    string Name,
    uint WeenieClassId,
    float Distance,
    float RelativeAngleDegrees,
    bool IsHealthKnown,
    float HealthFraction)
{
    public int SpeciesTag { get; init; }
    public int MaximumHealth { get; init; }
    public bool HasShield { get; init; }
    public ushort Incarnation { get; init; }
    public long HealthRevision { get; init; }
    public double SecsSinceHealthUpdate { get; init; } = double.PositiveInfinity;
}

/// <summary>Queries over the live entity set for hostile monsters near the local player.</summary>
public static class SimFoeTargetProbe
{
    private const KineticStateFlags Invisible = KineticStateFlags.Hidden | KineticStateFlags.NoDraw;

    public static IReadOnlyList<SimFoeTargetCapture> Capture(SimCore core, float ceilingGap)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (float.IsNaN(ceilingGap) || ceilingGap <= 0f || Self(core) is not { } self)
            return [];

        var selfLocus = self.Record.Snapshot.Position!.Value;
        float avatarBearing = ApproachMath.FetchBearing(RemotePositionMath.Rotation(selfLocus));
        float reachSq = ceilingGap * ceilingGap;
        var objects = core.SatchelHolder.Objects;
        var avatar = objects.Get(self.Guid);
        var foes = new List<SimFoeTargetCapture>();

        foreach (SimActorRecord capture in core.EntityObjects.Entities.ActiveRecords)
        {
            if (capture.ServerGuid == self.Guid || capture.Snapshot.Position is not { } locus)
                continue;
            var contender = objects.Get(capture.ServerGuid);
            if (!IsOnlineFoe(core, self.Guid, avatar, capture, contender))
                continue;

            Vector3 realm = RemotePositionMath.ToRealm(locus);
            Vector2 diff = new Vector2(realm.X - self.World.X, realm.Y - self.World.Y);
            float gapSq = diff.LengthSquared();
            if (gapSq > reachSq)
                continue;

            bool hasHealth = core.ActHolder.Combat.HasHealth(capture.ServerGuid);
            float health = hasHealth ? core.ActHolder.Combat.FetchHealthPct(capture.ServerGuid) : 1f;
            core.ActHolder.TryFetchHealthActivity(capture.ServerGuid, out long healthRev, out double healthAge);
            foes.Add(new SimFoeTargetCapture(
                capture.ServerGuid,
                contender?.Name ?? string.Empty,
                contender?.WeenieClassIdent ?? 0u,
                MathF.Sqrt(gapSq),
                SignedDeg(ApproachMath.PlaceBearing(self.World, realm) - avatarBearing),
                hasHealth,
                health)
            {
                SpeciesTag = contender?.Properties.FetchInt((uint)TraitInt.CreatureType) ?? 0,
                MaximumHealth = 0,
                HasShield = contender is not null && objects.FetchEquippedBy(contender.ObjectId).Any(static gear => (gear.Type & GearKind.Armor) != 0),
                Incarnation = capture.Incarnation,
                HealthRevision = healthRev,
                SecsSinceHealthUpdate = healthAge,
            });
        }

        return foes.Count is 0 ? [] : [.. foes];
    }

    public static uint? SeekClosest(SimCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (Self(core) is not { } self)
            return null;

        var objects = core.SatchelHolder.Objects;
        var avatar = objects.Get(self.Guid);
        uint? closest = null;
        float closestSq = float.PositiveInfinity;
        foreach (SimActorRecord capture in core.EntityObjects.Entities.ActiveRecords)
        {
            if (capture.ServerGuid == self.Guid
                || capture.Snapshot.Position is not { } locus
                || !IsOnlineFoe(core, self.Guid, avatar, capture, objects.Get(capture.ServerGuid)))

                continue;

            float gapSq = Vector3.DistanceSquared(self.World, RemotePositionMath.ToRealm(locus));
            if (gapSq < closestSq)
            {
                closestSq = gapSq;
                closest = capture.ServerGuid;
            }
        }
        return closest;
    }

    public static bool IsHostile(SimCore core, uint objectIdent)
    {
        ArgumentNullException.ThrowIfNull(core);
        uint avatarOid = core.AvatarIdentity.ServerGuid;
        if (objectIdent is 0u || avatarOid is 0u || !core.EntityObjects.Entities.TryFetchEngaged(objectIdent, out SimActorRecord capture))
            return false;

        var objects = core.SatchelHolder.Objects;
        return IsOnlineFoe(core, avatarOid, objects.Get(avatarOid), capture, objects.Get(objectIdent));
    }

    // The local player's record and position, or null before the player is placed
    private static (uint Guid, SimActorRecord Record, Vector3 World)? Self(SimCore core)
    {
        uint oid = core.AvatarIdentity.ServerGuid;
        if (oid is 0u
            || !core.EntityObjects.Entities.TryFetchEngaged(oid, out SimActorRecord capture)
            || capture.Snapshot.Position is not { } locus)

            return null;
        return (oid, capture, RemotePositionMath.ToRealm(locus));
    }

    // Visible, hostile and not known-dead
    private static bool IsOnlineFoe(SimCore core, uint avatarOid, ClientThing? avatar, SimActorRecord capture, ClientThing? contender)
    {
        if ((capture.FinalKineticsCondition & Invisible) != 0 || !FightTargetPolicy.IsHostileMonster(avatarOid, avatar, contender))
            return false;
        FightingPhase fighting = core.ActHolder.Combat;
        return !(fighting.HasHealth(capture.ServerGuid) && fighting.FetchHealthPct(capture.ServerGuid) <= 0f);
    }

    private static float SignedDeg(float deg)
    {
        float wrapped = deg % 360f;
        if (wrapped > 180f)
            wrapped -= 360f;
        else if (wrapped < -180f)
            wrapped += 360f;
        return wrapped;
    }
}
