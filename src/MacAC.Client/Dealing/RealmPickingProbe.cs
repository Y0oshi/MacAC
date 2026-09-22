using System.Numerics;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Realm;
using MacAC.Client.Shell.Panels;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Dealing;

internal readonly record struct PickingCameraCapture(
    Matrix4x4 View,
    Matrix4x4 Projection,
    Vector2 Viewport);

internal readonly record struct AvatarDealingPose(uint CellId, Vector3 Position);

internal readonly record struct RealmDealingTarget(
    uint ServerGuid,
    uint LocalEntityId,
    RealmActor Entity);

internal readonly record struct ClosestFightingTarget(uint ServerGuid, float DistanceSquared);

internal enum CanonPickingKind
{
    Item,
    CompassItem,
    Monster,
    Player,
    UnopenedCorpse,
}

internal enum CanonPickingDirection
{
    Closest,
    Previous,
    Next,
}

internal readonly record struct DealingApproach(
    RealmDealingTarget Target,
    AvatarDealingPose Player,
    float UseRadius,
    bool IsCloseRange,
    bool CanCharge,
    float TargetRadius,
    float TargetHeight);

internal interface IRealmPickingProbe
{
    uint AvatarOid => 0u;
    uint? SelectAtCur(bool includeSelf);
    uint? ChooseAt(float pointerX, float pointerY, bool includeSelf);
    void CommenceIlluminationPulse(uint srvOid);
    bool TryGrabPersona(uint srvOid, out uint ownActorIdent);
    bool IsCurrent(uint srvOid, uint ownActorIdent);
    string Depict(uint srvOid);
    bool IsBeast(uint srvOid);
    bool IsHostileMonster(uint srvOid);
    bool IsAttackableMark(uint srvOid);
    ClosestFightingTarget? SeekClosestHostileMonster();
    uint? SeekPickMark(
        CanonPickingKind sort,
        CanonPickingDirection dir,
        uint? mooring,
        bool excludePossessedByAvatar = false)
    {
        return sort == CanonPickingKind.Monster
            && dir == CanonPickingDirection.Closest
                ? SeekClosestHostileMonster()?.ServerGuid
                : null;
    }

    uint? SeekPreviousAttacker() => null;
    bool IsUseable(uint srvOid);
    bool IsPickupable(uint srvOid);
    bool IsWieldedByAvatar(uint srvOid);
    bool IsWieldedLocusPhase(uint srvOid);
    bool TryFetchApproach(uint srvOid, out DealingApproach approach);
    Vector3? GetCombatCameraTargetPoint(uint srvOid);
}

internal interface IRetainedWidgetPickingProbe
{
    bool ShouldUnhideHealth(uint srvOid);
    VividMarkDetails? LocateVividMarkDetails(uint srvOid);
    bool IsWithinExternalVesselUseSpan(uint srvOid);
}

internal interface IPickingViewPlaneSource
{
    MacAC.Client.Graphics.IClientCamera ApplyViewPlane(
        MacAC.Client.Graphics.IClientCamera cam);
}

internal sealed partial class RealmPickingProbe(
    OnlineActorCore liveEntities,
    ClientThingChart objects,
    CanonPickingStage selectionScene,
    Func<uint> playerGuid,
    Func<PickingCameraCapture> camera,
    Func<Vector2> cursor,
    Func<AvatarDealingPose?> playerPose,
    Func<uint, RealmActor, (float Radius, float Height)> setupCylinder,
    Func<uint, (Vector3 Origin, float Radius)?> selectionSphere,
    Func<uint, Matrix4x4?> childRootPose,
    Func<uint, bool>? hasOpenedCorpse = null,
    Func<FightingManner>? fightingManner = null,
    Func<uint, bool>? isFellow = null)
        : IRealmPickingProbe,
      IRetainedWidgetPickingProbe
{
    private const uint StuckObjectBit = 0x0004u;

    private const float DefaultUseRadius = 0.6f;

    private const float AceCanChargeGap = 7.5f;

    private const uint SmallGearBitmask =
        (uint)(GearKind.MeleeWeapon
             | GearKind.Armor
             | GearKind.Clothing
             | GearKind.Jewelry
             | GearKind.Food
             | GearKind.Money
             | GearKind.Misc
             | GearKind.MissileWeapon
             | GearKind.Container
             | GearKind.Gem
             | GearKind.SpellComponents
             | GearKind.Writable
             | GearKind.Key
             | GearKind.Caster);

    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));

    private readonly ClientThingChart _objects = objects ?? throw new ArgumentNullException(nameof(objects));

    private readonly CanonPickingStage _pickTableau = selectionScene ?? throw new ArgumentNullException(nameof(selectionScene));

    private readonly Func<uint> _avatarOid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));

    private readonly Func<PickingCameraCapture> _cam = camera ?? throw new ArgumentNullException(nameof(camera));

    private readonly Func<Vector2> _cur = cursor ?? throw new ArgumentNullException(nameof(cursor));

    private readonly Func<AvatarDealingPose?> _avatarPosture = playerPose ?? throw new ArgumentNullException(nameof(playerPose));

    private readonly Func<uint, RealmActor, (float Radius, float Height)> _rigCylinder = setupCylinder ?? throw new ArgumentNullException(nameof(setupCylinder));

    private readonly Func<uint, (Vector3 Origin, float Radius)?> _pickOrb = selectionSphere ?? throw new ArgumentNullException(nameof(selectionSphere));

    private readonly Func<uint, Matrix4x4?> _descendantTrunkPosture = childRootPose ?? throw new ArgumentNullException(nameof(childRootPose));

    private readonly Func<uint, bool> _hasOpenedCorpse = hasOpenedCorpse ?? (_ => false);

    private readonly Func<FightingManner> _fightingManner = fightingManner ?? (() => FightingManner.NonCombat);

    private readonly Func<uint, bool> _isFellow = isFellow ?? (_ => false);
}
