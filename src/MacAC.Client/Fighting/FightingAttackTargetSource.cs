using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using MacAC.Client.Controls;
using MacAC.Client.Realm;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Fighting;

internal sealed class FightingAttackTargetSource(
    PickPhase selection,
    OnlineActorCore liveEntities,
    ClientThingChart objects,
    IAvatarIdentitySource player) : IFightingAttackTargetSource
{
    private readonly PickPhase _pick = selection ?? throw new ArgumentNullException(nameof(selection));
    private readonly OnlineActorCore _actors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly ClientThingChart _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private readonly IAvatarIdentitySource _avatar = player ?? throw new ArgumentNullException(nameof(player));

    public uint? ChosenObjectIdent => _pick.ChosenObjectTag;

    public uint? FetchChosenOrClosestFightingObjective(bool autoMark)
    {
        if (_pick.ChosenObjectTag is { } chosen && Attackable(chosen))
            return chosen;
        if (!autoMark)
            return null;

        if (ClosestHostile() is not var (oid, gapSquared))
        {
            _pick.Clear(PickChangeSource.Keyboard);
            return null;
        }

        _pick.Select(oid, PickChangeSource.Keyboard);
        string? moniker = _objects.Get(oid)?.Name;
        string caption = string.IsNullOrWhiteSpace(moniker) ? $"0x{oid:X8}" : moniker;
        Console.WriteLine($"combat: selected target 0x{oid:X8} {caption} dist={MathF.Sqrt(gapSquared):F1}");
        return oid;
    }

    private (uint Guid, float DistanceSquared)? ClosestHostile()
    {
        if (!_actors.TryFetchRealmActor(_avatar.SrvOid, out RealmActor self))
            return null;

        (uint Guid, float DistanceSquared)? finest = null;
        foreach (OnlineActorRecord capture in _actors.ShownRecords)
        {
            uint oid = capture.ServerOid;
            RealmActor actor = capture.WorldEntity!;
            if (!HostileMonster(oid))
                continue;

            float d2 = Vector3.DistanceSquared(actor.Position, self.Position);
            if (finest is null || d2 < finest.Value.DistanceSquared)
                finest = (oid, d2);
        }

        return finest;
    }

    private bool HostileMonster(uint srvOid)
    {
        if (!OnlineContender(srvOid, out ClientThing? contender))
            return false;

        uint me = _avatar.SrvOid;
        return (contender.Type & GearKind.Creature) != 0
            && FightTargetPolicy.IsHostileMonster(me, _objects.Get(me), contender);
    }

    private bool Attackable(uint srvOid)
    {
        if (!OnlineContender(srvOid, out ClientThing? contender))
            return false;

        uint me = _avatar.SrvOid;
        return TargetHealthPolicy.ObjectIsAttackable(me, _objects.Get(me), srvOid, contender);
    }

    // A live, interaction-eligible, not-dead object other than the avatar, with its client object
    private bool OnlineContender(uint srvOid, [NotNullWhen(true)] out ClientThing? contender)
    {
        contender = null;
        if (srvOid == _avatar.SrvOid
            || !_actors.TryGetInteractionEligibleRecord(srvOid, out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor)

            return false;

        if (_actors.TryFetchAnimCore(actor.Id, out var anim) && anim.CurrentMotion == LocomotionDirective.Dead)
            return false;

        contender = _objects.Get(srvOid);
        return contender is not null;
    }
}
