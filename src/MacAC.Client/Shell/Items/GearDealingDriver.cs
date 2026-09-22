using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public enum GearPrimaryClickResult
{
    NotActive,
    ConsumedSuccess,
    ConsumedRejected,
}

public readonly record struct QueuedBackpackStance(
    ulong Token,
    uint ItemId,
    uint ContainerId,
    int Placement,
    ClientThing? ItemIdentity);

public sealed partial class GearDealingDriver : IDisposable
{
    internal const string SatchelReqOccupiedMsg =
        "You can only move or use one item at a time";

    private const long CanonDoublePressMsec = 500;

    private readonly ClientThingChart _objects;

    private readonly Func<uint> _avatarOid;

    private readonly Func<long> _instantMsec;

    private readonly Action<uint>? _transmitUse;

    private readonly Action<uint>? _transmitExamine;

    private readonly Action<uint, uint>? _transmitUseWithMark;

    private readonly Action<uint, uint>? _transmitWield;

    private readonly Action<uint>? _transmitDiscard;

    private readonly Action<uint, uint>? _transmitDivideToRealm;

    private readonly Action<uint, uint, int>? _transmitPutGearInVessel;

    private readonly Action<uint, uint, uint, uint>? _transmitDivideToVessel;

    private readonly Action<uint, uint, uint>? _transmitStackableCombine;

    private readonly Action<uint, uint, uint>? _transmitHand;

    private readonly Action<string>? _toast;

    private readonly Func<bool> _primedForSatchelReq;

    private readonly Func<uint> _engagedMerchantIdent;

    private readonly Func<uint> _terrainObjectIdent;

    private readonly Func<bool> _avatarOnTerrain;

    private readonly Func<bool> _inNonFightingManner;

    private readonly Func<uint, bool> _isModuleBundle;

    private readonly Action<uint, uint, int>? _placeInBackpack;

    private readonly Func<uint> _backpackVesselIdent;

    private readonly Action<uint>? _reqExternalVessel;

    private readonly Action<ItemRulingAction>? _auxiliaryAct;
    private readonly Func<uint> _chosenObjectIdent;

    private readonly StackSplitGauge? _pileDivideQty;

    private readonly Func<bool> _pullOnAvatarOpensSecureBarter;

    private readonly Action<string>? _sysMsg;

    private readonly Action<string, CanonLogTextType>? _interfacePhrase;

    private readonly AutoWieldDriver _autoWield;

    private readonly Action<uint, ItemUseHold>? _reqUse;

    private readonly Func<uint, uint, int, uint, bool>? _transmitPurchase;

    private readonly Func<uint, IReadOnlyList<(int Amount, uint ItemGuid)>, uint, bool>? _transmitPurchaseAll;

    private readonly Func<uint, IReadOnlyList<(int Amount, uint ItemGuid)>, bool>? _transmitVend;

    private readonly Func<uint, IReadOnlyList<uint>, bool>? _transmitSalvage;
    private readonly PackTransactionState _transactions;

    private uint _consumedPrimaryPressMark;

    private long _consumedPrimaryPressMsec = long.MinValue / 2;

    private QueuedBackpackStance? _queuedBackpackStance;

    private bool _destroyed;

    public GearDealingDriver(
        ClientThingChart objects,
        SimDealingTransactionLedger runtimeTransactions,
        DealingLedger interactionState,
        Func<uint> playerGuid,
        Action<uint>? transmitUse,
        Action<uint, uint>? transmitUseWithMark,
        Action<uint, uint>? transmitWield,
        Action<uint>? transmitDiscard,
        Action<uint>? transmitExamine = null,
        Func<long>? instantMsec = null,
        Action<string>? toast = null,
        Func<bool>? primedForSatchelReq = null,
        Func<uint>? engagedMerchantIdent = null,
        Func<uint>? terrainObjectIdent = null,
        Func<bool>? avatarOnTerrain = null,
        Func<bool>? inNonFightingManner = null,
        Func<uint, bool>? isModuleBundle = null,
        Action<uint, uint, int>? placeInBackpack = null,
        Func<uint>? backpackVesselIdent = null,
        Action<ItemRulingAction>? auxiliaryAct = null,
        Action<uint, uint>? transmitDivideToRealm = null,
        Func<uint>? chosenObjectIdent = null,
        StackSplitGauge? pileDivideQty = null,
        Action<uint, uint, int>? transmitPutGearInVessel = null,
        Action<uint, uint, uint>? transmitHand = null,
        Func<bool>? pullOnAvatarOpensSecureBarter = null,
        Action<string>? sysMsg = null,
        Action<uint, uint, uint, uint>? transmitDivideToVessel = null,
        Action<uint>? reqExternalVessel = null,
        FightingPhase? fightingPhase = null,
        Action<FightingManner>? transmitEditFightingManner = null,
        Action<uint, ItemUseHold>? reqUse = null,
        Func<uint, uint, int, uint, bool>? transmitPurchase = null,
        Func<uint, IReadOnlyList<(int Amount, uint ItemGuid)>, uint, bool>? transmitPurchaseAll = null,
        Func<uint, IReadOnlyList<(int Amount, uint ItemGuid)>, bool>? transmitVend = null,
        Action<string, CanonLogTextType>? interfacePhrase = null,
        Action<uint, uint, uint>? transmitStackableCombine = null,
        Func<uint, IReadOnlyList<uint>, bool>? transmitSalvage = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _avatarOid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
        _transmitUse = transmitUse;
        _transmitExamine = transmitExamine;
        _transmitUseWithMark = transmitUseWithMark;
        _transmitWield = transmitWield;
        _transmitDiscard = transmitDiscard;
        _transmitDivideToRealm = transmitDivideToRealm;
        _transmitPutGearInVessel = transmitPutGearInVessel;
        _transmitDivideToVessel = transmitDivideToVessel;
        _transmitStackableCombine = transmitStackableCombine;
        _transmitHand = transmitHand;
        _instantMsec = instantMsec ?? (() => Environment.TickCount64);
        _toast = toast;
        _primedForSatchelReq = primedForSatchelReq ?? (() => true);
        _engagedMerchantIdent = engagedMerchantIdent ?? (() => 0u);
        _terrainObjectIdent = terrainObjectIdent ?? (() => 0u);
        _avatarOnTerrain = avatarOnTerrain ?? (() => true);
        _inNonFightingManner = inNonFightingManner ?? (() => false);
        _isModuleBundle = isModuleBundle ?? (_ => false);
        _placeInBackpack = placeInBackpack;
        _backpackVesselIdent = backpackVesselIdent ?? _avatarOid;
        _reqExternalVessel = reqExternalVessel;
        _auxiliaryAct = auxiliaryAct;
        _chosenObjectIdent = chosenObjectIdent ?? (() => 0u);
        _pileDivideQty = pileDivideQty;
        _pullOnAvatarOpensSecureBarter = pullOnAvatarOpensSecureBarter ?? (() => true);
        _sysMsg = sysMsg;
        _interfacePhrase = interfacePhrase;
        _reqUse = reqUse;
        _transmitPurchase = transmitPurchase;
        _transmitPurchaseAll = transmitPurchaseAll;
        _transmitVend = transmitVend;
        _transmitSalvage = transmitSalvage;
        _dealingPhase = interactionState
            ?? throw new ArgumentNullException(nameof(interactionState));
        _runtimeTransactions = runtimeTransactions
            ?? throw new ArgumentNullException(nameof(runtimeTransactions));
        _transactions = _runtimeTransactions.Inventory;
        if (!ReferenceEquals(_transactions.Objects, _objects))
        {
            throw new ArgumentException(
                "The inventory transaction owner must borrow the controller's exact object table",
                nameof(runtimeTransactions));
        }
        _autoWield = new AutoWieldDriver(
            _objects,
            _avatarOid,
            _transmitWield,
            transmitPutGearInVessel,
            _sysMsg,
            fightingPhase,
            transmitEditFightingManner,
            _transactions);
        _dealingPhase.Changed += OnDealingMannerAltered;
        _transactions.StateChanged += OnTransactionPhaseAltered;
        _transactions.RequestCompleted += OnSatchelReqFinished;
        _transactions.RequestFailed += OnSatchelReqFailed;
        _transactions.ObjectTableCleared += OnSatchelObjectsCleared;
        _objects.MoveRequestFailed += OnRelocateReqFailedNotice;
    }

    public event Action? StateChanged;

    public event Action<uint, uint>? MergeAttempted;

    public event Action<uint, uint>? SecureTradeRequested;

    public event Action<QueuedBackpackStance>? PendingBackpackPlacementRequested;

    public event Action<QueuedBackpackStance>? PendingBackpackPlacementCancelled;

    public event Action<QueuedBackpackStance>? PendingBackpackPlacementResolved;

    public event Action<RealmDropDispatch>? WorldDropDispatched;

    public event Action<ItemRulingAction>? PolicyActionRequested;

    public readonly record struct AssayReplyAcceptance(
        bool Accepted,
        bool FirstResponse);

    private static void RelayAll<T>(
        Action<T>? listeners,
        T val,
        List<Exception> misses)
    {
        if (listeners is null)
            return;
        foreach (Action<T> listener in listeners.GetInvocationList())
        {
            try { listener(val); }
            catch (Exception problem) { misses.Add(problem); }
        }
    }
}
