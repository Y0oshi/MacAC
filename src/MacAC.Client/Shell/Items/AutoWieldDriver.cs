using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

internal sealed partial class AutoWieldDriver : IDisposable
{
    internal const WieldBitmask WeaponPrimedBitmask =
        WieldBitmask.MeleeWeapon
        | WieldBitmask.MissileWeapon
        | WieldBitmask.Held
        | WieldBitmask.TwoHanded;

    private const byte FightingUseMissile = 0x02;

    private const byte FightingUseTwoHanded = 0x05;

    private static readonly WieldBitmask[] AutoWieldOrdering =
    [
        WieldBitmask.HeadWear,
        WieldBitmask.ChestWear,
        WieldBitmask.AbdomenWear,
        WieldBitmask.UpperArmWear,
        WieldBitmask.LowerArmWear,
        WieldBitmask.HandWear,
        WieldBitmask.UpperLegWear,
        WieldBitmask.LowerLegWear,
        WieldBitmask.FootWear,
        WieldBitmask.ChestArmor,
        WieldBitmask.AbdomenArmor,
        WieldBitmask.UpperArmArmor,
        WieldBitmask.LowerArmArmor,
        WieldBitmask.UpperLegArmor,
        WieldBitmask.LowerLegArmor,
        WieldBitmask.NeckWear,
        WieldBitmask.WristWearLeft,
        WieldBitmask.WristWearRight,
        WieldBitmask.FingerWearLeft,
        WieldBitmask.FingerWearRight,
        WieldBitmask.Shield,
        WieldBitmask.MissileAmmo,
        WieldBitmask.MeleeWeapon,
        WieldBitmask.MissileWeapon,
        WieldBitmask.Held,
        WieldBitmask.TwoHanded,
        WieldBitmask.TrinketOne,
        WieldBitmask.Cloak,
        WieldBitmask.SigilOne,
        WieldBitmask.SigilTwo,
        WieldBitmask.SigilThree,
    ];

    private readonly ClientThingChart _objects;

    private readonly Func<uint> _avatarOid;

    private readonly Action<uint, uint>? _transmitWield;

    private readonly Action<uint, uint, int>? _transmitPutGearInVessel;

    private readonly Action<string>? _sysMsg;

    private readonly FightingPhase? _fightingPhase;

    private readonly Action<FightingManner>? _transmitEditFightingManner;

    private readonly PackTransactionState? _transactions;

    private QueuedSwitch? _queuedSwitch;

    private PendingFightingSettlement? _queuedFightingSettlement;

    private bool _fightingChangeoverObservedDuringSwitch;

    private bool _destroyed;

    public AutoWieldDriver(
        ClientThingChart objects,
        Func<uint> playerGuid,
        Action<uint, uint>? transmitWield,
        Action<uint, uint, int>? transmitPutGearInVessel,
        Action<string>? sysMsg = null,
        FightingPhase? fightingPhase = null,
        Action<FightingManner>? transmitEditFightingManner = null,
        PackTransactionState? transactions = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _avatarOid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
        _transmitWield = transmitWield;
        _transmitPutGearInVessel = transmitPutGearInVessel;
        _sysMsg = sysMsg;
        _fightingPhase = fightingPhase;
        _transmitEditFightingManner = transmitEditFightingManner;
        _transactions = transactions;

        _objects.ObjectMoved += OnObjectMoved;
        _objects.ObjectRemoved += OnObjectRemoved;
        _objects.MoveRequestFailed += OnRelocateReqFailed;
        _objects.WieldConfirmed += OnWieldConfirmed;
        _objects.Cleared += OnObjectsCleared;
        _fightingPhase?.CombatModeChanged += OnFightingMannerAltered;
    }

    private readonly record struct QueuedSwitch(
        uint RequestedItemId,
        uint BlockingItemId,
        WieldBitmask RequestedMask,
        FightingManner? CombatModeAfterWield);

    private readonly record struct PendingFightingSettlement(
        uint ItemId,
        FightingManner ReadyMode,
        FightingSettlementPhase Phase,
        bool ExpectTrailingPeace);

    private enum FightingSettlementPhase
    {
        AwaitingPostWieldMode,
        SawTransitionalPeace,
        SawReadyAfterPeace,
    }
}
