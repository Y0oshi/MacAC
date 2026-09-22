using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

internal sealed partial class AutoWieldDriver
{
    public bool IsOccupied => _queuedSwitch is not null;

    public void AlertExplicitCombatModeRequest()
    {
        _queuedFightingSettlement = null;
        _fightingChangeoverObservedDuringSwitch = false;
        if (_queuedSwitch is { } queued)
            _queuedSwitch = queued with { CombatModeAfterWield = null };
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _queuedSwitch = null;
        _queuedFightingSettlement = null;
        _fightingChangeoverObservedDuringSwitch = false;
        _objects.ObjectMoved -= OnObjectMoved;
        _objects.ObjectRemoved -= OnObjectRemoved;
        _objects.MoveRequestFailed -= OnRelocateReqFailed;
        _objects.WieldConfirmed -= OnWieldConfirmed;
        _objects.Cleared -= OnObjectsCleared;
        _fightingPhase?.CombatModeChanged -= OnFightingMannerAltered;
    }

    internal static bool ChunksUseOfShield(ClientThing gear)
    {
        byte fightingUse = gear.CombatUse ?? 0;
        return fightingUse == FightingUseTwoHanded
            || (fightingUse == FightingUseMissile && gear.AmmoType is > 0)
            || gear.Type.HasFlag(GearKind.Caster);
    }

    private bool CommenceWeaponSubstitute(
        uint askedGearIdent,
        ClientThing blockingGear,
        WieldBitmask askedBitmask,
        FightingManner? fightingMannerFollowingWield)
    {
        if (_transmitPutGearInVessel is null)
            return false;

        uint avatar = _avatarOid();
        if (avatar is 0)
            return false;

        _queuedSwitch = new QueuedSwitch(
            askedGearIdent,
            blockingGear.ObjectId,
            askedBitmask,
            fightingMannerFollowingWield);

        _sysMsg?.Invoke(
            $"Moving {blockingGear.FetchAppropriateLabel()} to your backpack");
        bool dispatched = RelaySatchelReq(
            PackRequestKind.PutInContainer,
            blockingGear.ObjectId,
            () =>
            {
                _transmitPutGearInVessel(blockingGear.ObjectId, avatar, 0);
                return true;
            });
        if (!dispatched)
            _queuedSwitch = null;
        return dispatched;
    }

    private bool TransmitWield(
        ClientThing gear,
        WieldBitmask bitmask,
        FightingManner? fightingMannerFollowingWield)
    {
        if (_transmitWield is null)
            return false;
        _queuedSwitch = new QueuedSwitch(
            gear.ObjectId,
            BlockingItemId: 0,
            RequestedMask: bitmask,
            CombatModeAfterWield: fightingMannerFollowingWield);
        bool dispatched = RelaySatchelReq(
            PackRequestKind.Wield,
            gear.ObjectId,
            () =>
            {
                _transmitWield(gear.ObjectId, (uint)bitmask);
                return true;
            });
        if (!dispatched)
            _queuedSwitch = null;
        return dispatched;
    }

    private bool RelaySatchelReq(
        PackRequestKind sort,
        uint gearIdent,
        Func<bool> relay)
        => _transactions?.TryRelay(sort, gearIdent, relay) ?? relay();

    private void OnObjectMoved(ObjectRelocation relocate)
    {
        if (_queuedSwitch is not { } queued
            || queued.BlockingItemId is 0
            || relocate.ItemId != queued.BlockingItemId
            || relocate.Current.ContainerId != _avatarOid()
            || relocate.Current.EquipLocation != WieldBitmask.None)
            return;

        _queuedSwitch = null;
        if (_objects.Get(queued.RequestedItemId) is { } asked)
            TryWield(
                asked,
                queued.RequestedMask,
                queued.CombatModeAfterWield);
    }

    private void OnWieldConfirmed(uint gearIdent)
    {
        if (_queuedSwitch is { BlockingItemId: 0 } queued
            && gearIdent == queued.RequestedItemId)
        {
            _queuedSwitch = null;
            if (queued.CombatModeAfterWield is { } primedManner)
            {
                _queuedFightingSettlement = new(
                    gearIdent,
                    primedManner,
                    FightingSettlementPhase.AwaitingPostWieldMode,
                    _fightingChangeoverObservedDuringSwitch);
            }
            _fightingChangeoverObservedDuringSwitch = false;
        }
    }

    private void OnFightingMannerAltered(FightingManner manner)
    {
        if (_queuedSwitch is { CombatModeAfterWield: { } switchPrimedManner }
            && manner != switchPrimedManner)

            _fightingChangeoverObservedDuringSwitch = true;

        if (_queuedFightingSettlement is not { } settlement)
            return;
        if (manner == settlement.ReadyMode)
        {
            _queuedFightingSettlement = settlement.Phase == FightingSettlementPhase.SawTransitionalPeace
                || settlement.ExpectTrailingPeace
                ? (settlement with
                {
                    Phase = FightingSettlementPhase.SawReadyAfterPeace,
                })
                : null;
            return;
        }

        if (manner != FightingManner.NonCombat)
            return;

        if (settlement.Phase == FightingSettlementPhase.SawReadyAfterPeace)
        {
            _queuedFightingSettlement = null;
            _transmitEditFightingManner?.Invoke(settlement.ReadyMode);
        }
        else
        {
            _queuedFightingSettlement = settlement with
            {
                Phase = FightingSettlementPhase.SawTransitionalPeace,
            };
        }
    }

    private void OnRelocateReqFailed(RelocationRefusal miss)
    {
        if (_queuedSwitch is { } queued
            && (miss.ItemId == queued.BlockingItemId
                || miss.ItemId == queued.RequestedItemId))
        {
            _queuedSwitch = null;
            _fightingChangeoverObservedDuringSwitch = false;
        }
    }

    private void OnObjectRemoved(ClientThing gear)
    {
        if (_queuedSwitch is { } queued
            && (gear.ObjectId == queued.BlockingItemId
                || gear.ObjectId == queued.RequestedItemId))
        {
            _queuedSwitch = null;
            _fightingChangeoverObservedDuringSwitch = false;
        }
        if (_queuedFightingSettlement is { } settlement
            && gear.ObjectId == settlement.ItemId)
            _queuedFightingSettlement = null;
    }

    private void OnObjectsCleared()
    {
        _queuedSwitch = null;
        _queuedFightingSettlement = null;
        _fightingChangeoverObservedDuringSwitch = false;
    }

    private WieldBitmask FinestOnHandWieldBitmask(ClientThing gear)
    {
        foreach (WieldBitmask bitmask in AutoWieldOrdering)
        {
            if ((gear.ValidLocations & bitmask) == WieldBitmask.None)
                continue;
            if (!WieldBitmaskOccupied(bitmask, gear.ObjectId))
                return bitmask;
        }
        return WieldBitmask.None;
    }

    private static WieldBitmask LeadCompatibleWieldBitmask(ClientThing gear)
    {
        foreach (WieldBitmask bitmask in AutoWieldOrdering)
            if ((gear.ValidLocations & bitmask) != WieldBitmask.None)
                return bitmask;
        return WieldBitmask.None;
    }

    private bool AutoDonIsLegal(
        ClientThing gear,
        out ClientThing? blocker)
    {
        uint precedenceBitmask = EquippedAutoDonPrecedenceBitmask(gear.ObjectId);
        if ((gear.Priority & precedenceBitmask) is 0)
        {
            blocker = null;
            return true;
        }

        WieldBitmask occupiedLocales = EquippedAutoDonLocaleBitmask(gear.ObjectId)
            & gear.ValidLocations;
        blocker = FetchEquippedObjectAtLocale(
            occupiedLocales, gear.Priority, gear.ObjectId);
        return false;
    }

    private bool WieldBitmaskOccupied(WieldBitmask bitmask, uint exceptOid)
    {
        return FetchEquippedObjectAtLocale(bitmask, precedence: 0, exceptOid) is not null;
    }

    private uint EquippedAutoDonPrecedenceBitmask(uint exceptOid)
    {
        uint bitmask = 0;
        foreach (ClientThing gear in _objects.FetchEquippedBy(_avatarOid()))
        {
            if (gear.ObjectId == exceptOid
                || (gear.CurrentlyEquippedLocale & GearEquipRules.AutoDonBitmask) == WieldBitmask.None)
                continue;
            bitmask |= gear.Priority;
        }
        return bitmask;
    }

    private WieldBitmask EquippedAutoDonLocaleBitmask(uint exceptOid)
    {
        WieldBitmask bitmask = WieldBitmask.None;
        foreach (ClientThing gear in _objects.FetchEquippedBy(_avatarOid()))
        {
            if (gear.ObjectId == exceptOid
                || (gear.CurrentlyEquippedLocale & GearEquipRules.AutoDonBitmask) == WieldBitmask.None)
                continue;
            bitmask |= gear.CurrentlyEquippedLocale;
        }
        return bitmask;
    }

    private ClientThing? FetchEquippedObjectAtLocale(
        WieldBitmask localeBitmask,
        uint precedence,
        uint exceptOid)
    {
        if (localeBitmask == WieldBitmask.None)
            return null;

        foreach (ClientThing gear in _objects.FetchEquippedBy(_avatarOid()))
        {
            if (gear.ObjectId == exceptOid
                || (gear.CurrentlyEquippedLocale & localeBitmask) == WieldBitmask.None)
                continue;
            if ((gear.Priority & precedence) is not 0 || precedence is 0)
                return gear;
        }
        return null;
    }

    private static FightingManner? FightingMannerForWeaponLocale(WieldBitmask locale)
    {
        if ((locale & WieldBitmask.MissileWeapon) != 0)
            return FightingManner.Missile;
        if ((locale & WieldBitmask.Held) != 0)
            return FightingManner.Magic;
        return (locale & (WieldBitmask.MeleeWeapon | WieldBitmask.TwoHanded)) != 0 ? FightingManner.Melee : null;
    }
}
