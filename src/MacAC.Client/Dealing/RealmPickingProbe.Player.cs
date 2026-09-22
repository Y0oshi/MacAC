using System.Numerics;
using MacAC.Client.Realm;
using MacAC.Client.Shell.Panels;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Shell;
using MacAC.Mechanics.Traits;

namespace MacAC.Client.Dealing;

internal sealed partial class RealmPickingProbe
{
    public uint AvatarOid => _avatarOid();

    public uint? SelectAtCur(bool includeSelf)
    {
        Vector2 cur = _cur();
        return ChooseAt(cur.X, cur.Y, includeSelf);
    }

    public uint? ChooseAt(float pointerX, float pointerY, bool includeSelf)
    {
        var cam = _cam();
        var strike = _pickTableau.Pick(
            pointerX,
            pointerY,
            cam.Viewport,
            cam.View,
            cam.Projection,
            includeSelf ? 0u : _avatarOid());
        return strike is { } located
            && _onlineActors.TryGetPickEligibleRecord(
                located.ServerGuid,
                located.LocalEntityId,
                out _)
            ? located.ServerGuid
            : null;
    }

    public void CommenceIlluminationPulse(uint srvOid)
    {
        if (TryFetchDealingMark(srvOid, out RealmDealingTarget mark))
            _pickTableau.OpenIlluminationPulse(mark.ServerGuid, mark.LocalEntityId);
    }

    public bool TryGrabPersona(uint srvOid, out uint ownActorIdent)
    {
        if (TryFetchDealingMark(srvOid, out RealmDealingTarget mark))
        {
            ownActorIdent = mark.LocalEntityId;
            return true;
        }

        ownActorIdent = 0u;
        return false;
    }

    public bool TryFetchDealingMark(
        uint srvOid,
        out RealmDealingTarget mark)
    {
        if (_onlineActors.TryGetPickEligibleRecord(
                srvOid,
                out OnlineActorRecord capture)
            && capture.WorldEntity is { } actor)
        {
            mark = new RealmDealingTarget(srvOid, actor.Id, actor);
            return true;
        }

        mark = default;
        return false;
    }

    public bool TryFetchPickOrb(
        uint srvOid,
        out Vector3 realmMiddle,
        out float realmRadius)
    {
        realmMiddle = default;
        realmRadius = 0f;
        if (!_onlineActors.TryFetchRealmActor(srvOid, out RealmActor actor))
            return false;

        bool affixed =
            _onlineActors.TryFetchAffixedProjectedCapture(srvOid, out _);
        Matrix4x4 descendantTrunk = Matrix4x4.Identity;
        if (affixed)
        {
            if (_descendantTrunkPosture(actor.Id) is not { } published)
                return false;
            descendantTrunk = published;
        }

        realmMiddle = affixed ? descendantTrunk.Translation : actor.Position;
        realmRadius = 0.1f;
        if (!_onlineActors.TryGetCapture(srvOid, out var summon)
            || summon.SetupTableId is not uint rigIdent
            || _pickOrb(rigIdent) is not { } orb
            || orb.Radius <= 1e-4f)

            return true;

        float scaling = affixed
            ? (summon.ObjScale is { } descendantScaling && descendantScaling > 0f ? descendantScaling : 1f)
            : (actor.Scale > 0f ? actor.Scale : 1f);
        Vector3 ownMiddle = orb.Origin * scaling;
        realmMiddle = affixed
            ? Vector3.Transform(ownMiddle, descendantTrunk)
            : actor.Position + Vector3.Transform(ownMiddle, actor.Rotation);
        realmRadius = orb.Radius * scaling;
        return true;
    }

    public bool TryFetchApproach(
        uint srvOid,
        out DealingApproach approach)
    {
        if (_avatarPosture() is not { } avatar
            || _onlineActors.TryFetchAffixedProjectedCapture(srvOid, out _)
            || !TryFetchDealingMark(srvOid, out RealmDealingTarget mark))
        {
            approach = default;
            return false;
        }

        float useRadius = FetchUseRadius(srvOid);
        float dx = mark.Entity.Position.X - avatar.Position.X;
        float dy = mark.Entity.Position.Y - avatar.Position.Y;
        float gapSquared = dx * dx + dy * dy;
        (float radius, float height) = _rigCylinder(srvOid, mark.Entity);
        approach = new DealingApproach(
            mark,
            avatar,
            useRadius,
            gapSquared <= useRadius * useRadius,
            gapSquared >= AceCanChargeGap * AceCanChargeGap,
            radius,
            height);
        return true;
    }

    public bool IsCurrent(RealmDealingTarget mark)
        => IsCurrent(mark.ServerGuid, mark.LocalEntityId);

    public bool IsCurrent(uint srvOid, uint ownActorIdent)
        => _onlineActors.TryGetPickEligibleRecord(srvOid, ownActorIdent, out _);

    public bool IsBeast(uint srvOid)
    {
        if (srvOid == _avatarOid()
            || !TryFetchDealingMark(srvOid, out RealmDealingTarget mark))

            return false;

        return _onlineActors.TryFetchAnimCore(mark.LocalEntityId, out var anim)
            && anim.CurrentMotion == LocomotionDirective.Dead
            ? false
            : (FetchGearKind(srvOid) & GearKind.Creature) != 0;
    }

    public bool IsHostileMonster(uint srvOid)
    {
        return IsBeast(srvOid)
                && FightTargetPolicy.IsHostileMonster(
                    _avatarOid(),
                    _objects.Get(_avatarOid()),
                    _objects.Get(srvOid));
    }

    public bool IsAttackableMark(uint srvOid)
    {
        return IsBeast(srvOid)
                && TargetHealthPolicy.ObjectIsAttackable(
                    _avatarOid(),
                    _objects.Get(_avatarOid()),
                    srvOid,
                    _objects.Get(srvOid));
    }

    public bool IsUseable(uint srvOid)
    {
        return _onlineActors.TryGetCapture(srvOid, out var summon)
            ? GearUseability.IsUseable(
                summon.Useability ?? GearUseability.Undef)
            : false;
    }

    public bool IsWieldedByAvatar(uint srvOid)
    {
        uint avatarOid = _avatarOid();
        return avatarOid is not 0u
            && _objects.Get(srvOid) is { } gear
            && gear.WielderIdent == avatarOid;
    }

    public bool IsWieldedLocusPhase(uint srvOid)
    {
        return _objects.Get(srvOid) is { } gear
                && gear.VesselTag is 0u
                && gear.CurrentlyEquippedLocale != WieldBitmask.None;
    }

    public bool IsPickupable(uint srvOid)
    {
        return !_onlineActors.TryGetCapture(srvOid, out var summon)
            || ((summon.ObjectDescriptionFlags ?? 0u) & StuckObjectBit) is not 0u
            ? false
            : ((summon.ItemType ?? 0u) & SmallGearBitmask) is not 0u;
    }

    public bool IsWithinExternalVesselUseSpan(uint srvOid)
    {
        if (_avatarPosture() is not { } avatarPosture
            || !_onlineActors.TryFetchRealmActor(_avatarOid(), out RealmActor avatar)
            || !TryFetchDealingMark(srvOid, out RealmDealingTarget mark)
            || !_onlineActors.TryGetCapture(srvOid, out var summon)
            || summon.UseRadius is not > 0f)
        {
            // The server remains authoritative while render projection is absent
            return true;
        }

        var avatarCylinder = _rigCylinder(_avatarOid(), avatar);
        var markCylinder = _rigCylinder(srvOid, mark.Entity);
        return ReachMath.ObjectsInSpan(
            avatarPosture.Position,
            avatarCylinder.Radius,
            avatarCylinder.Height,
            mark.Entity.Position,
            markCylinder.Radius,
            markCylinder.Height,
            summon.UseRadius.Value,
            useRadii: true,
            ignoreZDiff: false);
    }

    public GearKind FetchGearKind(uint srvOid)
        => _objects.Get(srvOid)?.Type ?? GearKind.None;

    public Vector3? GetCombatCameraTargetPoint(uint srvOid)
    {
        return IsAttackableMark(srvOid)
                && TryFetchDealingMark(srvOid, out RealmDealingTarget mark)
                    ? mark.Entity.Position
                        + Vector3.Transform(new Vector3(0f, 0f, 0.5f), mark.Entity.Rotation)
                    : null;
    }

    public string Depict(uint srvOid)
    {
        string? label = _objects.Get(srvOid)?.Name;
        return string.IsNullOrWhiteSpace(label) ? $"0x{srvOid:X8}" : label;
    }

    public bool ShouldUnhideHealth(uint srvOid)
    {
        return TargetHealthPolicy.ShouldAskHealth(
                _avatarOid(),
                _objects.Get(_avatarOid()),
                _objects.Get(srvOid));
    }

    public ClosestFightingTarget? SeekClosestHostileMonster()
    {
        if (!_onlineActors.TryFetchRealmActor(_avatarOid(), out RealmActor avatar))
            return null;

        ClosestFightingTarget? finest = null;
        foreach (OnlineActorRecord capture in _onlineActors.ShownRecords)
        {
            uint oid = capture.ServerOid;
            RealmActor actor = capture.WorldEntity!;
            if (!IsHostileMonster(oid))
                continue;
            float gapSquared = Vector3.DistanceSquared(actor.Position, avatar.Position);
            if (finest is null || gapSquared < finest.Value.DistanceSquared)
                finest = new ClosestFightingTarget(oid, gapSquared);
        }
        return finest;
    }

    public uint? SeekPickMark(
        CanonPickingKind sort,
        CanonPickingDirection dir,
        uint? mooring,
        bool excludePossessedByAvatar = false)
    {
        uint avatarOid = _avatarOid();
        if (!_onlineActors.TryFetchRealmActor(avatarOid, out RealmActor avatar))
            return null;

        float radarRadius = IsExteriorChamber(avatar.VisChamberIdent)
            ? CanonRadar.ExteriorSpanMeters
            : CanonRadar.InsideSpanMeters;
        var contenders = new List<(uint Guid, float Order)>();
        foreach (OnlineActorRecord capture in _onlineActors.ShownRecords)
        {
            uint oid = capture.ServerOid;
            if (oid is 0u
                || oid == avatarOid
                || capture.WorldEntity is not { } actor
                || _objects.Get(oid) is not { } objRef
                || (excludePossessedByAvatar
                    && _objects.IsPossessedByObject(oid, avatarOid)))

                continue;

            float ordering = PickOrdering(avatar, actor);
            if (ordering > radarRadius
                || !FitsPickSort(sort, oid, objRef, capture.FinalKineticsPhase))
                continue;
            contenders.Add((oid, ordering));
        }

        if (contenders.Count is 0)
            return null;
        contenders.Sort(static (left, right) =>
        {
            int gap = left.Order.CompareTo(right.Order);
            return gap is not 0 ? gap : left.Guid.CompareTo(right.Guid);
        });

        if (dir == CanonPickingDirection.Closest)
            return contenders[0].Guid;

        (float Order, uint Guid)? mooringTag = null;
        if (mooring is { } mooringOid
            && _onlineActors.TryFetchRealmActor(mooringOid, out RealmActor mooringActor))
        {
            mooringTag = (PickOrdering(avatar, mooringActor), mooringOid);
        }

        if (mooringTag is null)
        {
            return dir == CanonPickingDirection.Previous
                ? contenders[^1].Guid
                : contenders[0].Guid;
        }

        if (dir == CanonPickingDirection.Next)
        {
            foreach ((uint oid, float ordering) in contenders)
            {
                if (ContrastPickTag(ordering, oid, mooringTag.Value.Order, mooringTag.Value.Guid) > 0)
                    return oid;
            }
            return contenders[0].Guid;
        }

        for (int idx = contenders.Count - 1; idx >= 0; --idx)
        {
            (uint oid, float ordering) = contenders[idx];
            if (ContrastPickTag(ordering, oid, mooringTag.Value.Order, mooringTag.Value.Guid) < 0)
                return oid;
        }
        return contenders[^1].Guid;
    }

    public uint? SeekPreviousAttacker()
    {
        uint avatarOid = _avatarOid();
        if (_objects.Get(avatarOid) is not { } avatarObject
            || !avatarObject.Properties.InstIdents.TryGetValue(
                (uint)TraitInstanceId.CurrentAttacker,
                out var attacker)
            || attacker is 0u
            || !_onlineActors.TryFetchRealmActor(avatarOid, out RealmActor avatar)
            || !_onlineActors.TryFetchRealmActor(attacker, out RealmActor mark))

            return null;

        float radarRadius = IsExteriorChamber(avatar.VisChamberIdent)
            ? CanonRadar.ExteriorSpanMeters
            : CanonRadar.InsideSpanMeters;
        return PickOrdering(avatar, mark) <= radarRadius ? attacker : null;
    }

    public VividMarkDetails? LocateVividMarkDetails(uint srvOid)
    {
        uint avatarOid = _avatarOid();
        var mark = _objects.Get(srvOid);

        if (srvOid == avatarOid
            || mark is null
            || _objects.IsPossessedByObject(srvOid, avatarOid)
            || mark.VesselTag is not 0u
            || !(_onlineActors.TryFetchSpatiallyProjectedCapture(srvOid, out _)
                || _onlineActors.TryFetchAffixedProjectedCapture(srvOid, out _))
            || !TryFetchPickOrb(srvOid, out Vector3 middle, out float radius))

            return null;

        uint pwdBitset = _onlineActors.TryGetCapture(srvOid, out var summon)
            ? summon.ObjectDescriptionFlags ?? 0u
            : 0u;
        return new VividMarkDetails(middle, radius, (uint)FetchGearKind(srvOid), pwdBitset);
    }

    private static bool IsExteriorChamber(uint? chamberIdent)
        => chamberIdent is null || (chamberIdent.Value & 0xFFFFu) < 0x100u;

    private float FetchUseRadius(uint srvOid)
    {
        bool haveSummon = _onlineActors.TryGetCapture(srvOid, out var summon);
        bool fromWire = haveSummon && summon.UseRadius is > 0f;
        float radius = fromWire ? summon.UseRadius!.Value : DefaultUseRadius;
        return radius;
    }

    private bool FitsPickSort(
        CanonPickingKind sort,
        uint oid,
        ClientThing objRef,
        KineticStateFlags kineticsPhase)
    {
        bool showableOnRadar = objRef.RadarBehavior is { } behavior
            && CanonRadar.IsShowable((MechRadarBehavior)behavior, hasKineticsObject: true);
        PublicWeenieBits flagSet = (PublicWeenieBits)(objRef.PublicWeenieBitfield ?? 0u);
        bool isFellow = _isFellow(oid);
        bool isFightingCompass = _fightingManner() is FightingManner.Melee or FightingManner.Missile;
        bool isSpecialCompassObject = (flagSet
            & (PublicWeenieBits.Lifestone
                | PublicWeenieBits.Portal
                | PublicWeenieBits.Bindstone)) != 0;

        return objRef.VesselTag is not 0u
            || (kineticsPhase & KineticStateFlags.Cloaked) != 0
            || (((uint)flagSet & 0x8000_0000u) is not 0)
            ? false
            : sort switch
            {
                CanonPickingKind.Item =>
                    objRef.RadarBehavior is null or 0
                    || isSpecialCompassObject,
                CanonPickingKind.CompassItem =>
                    (isSpecialCompassObject || showableOnRadar)
                    && (!isFightingCompass
                        || (IsAttackableMark(oid)
                            && !isFellow
                            && (flagSet & PublicWeenieBits.Vendor) == 0
                            && (kineticsPhase & KineticStateFlags.ReportAsEnvironment) == 0)),
                CanonPickingKind.Monster =>
                    showableOnRadar
                    && IsAttackableMark(oid)
                    && !isFellow
                    && (flagSet & PublicWeenieBits.Vendor) == 0,
                CanonPickingKind.Player =>
                    showableOnRadar && (flagSet & PublicWeenieBits.Player) != 0,
                CanonPickingKind.UnopenedCorpse =>
                    (flagSet & PublicWeenieBits.Corpse) != 0
                    && !_hasOpenedCorpse(oid),
                _ => false,
            };
    }

    private static float PickOrdering(RealmActor avatar, RealmActor mark)
    {
        Vector3 diff = mark.Position - avatar.Position;
        Vector3 own = Vector3.Transform(diff, Quaternion.Inverse(avatar.Rotation));
        return MathF.Sqrt(own.X * own.X + own.Y * own.Y)
            + MathF.Abs(own.Z) * 1.2f;
    }

    private static int ContrastPickTag(
        float leftOrdering,
        uint leftOid,
        float rightOrdering,
        uint rightOid)
    {
        int ordering = leftOrdering.CompareTo(rightOrdering);
        return ordering is not 0 ? ordering : leftOid.CompareTo(rightOid);
    }
}
