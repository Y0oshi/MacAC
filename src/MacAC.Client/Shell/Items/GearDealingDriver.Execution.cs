using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public sealed partial class GearDealingDriver
{
    public bool PerformConfirmedUse(uint objectIdent)
    {
        if (objectIdent is 0u || (_reqUse is null && _transmitUse is null))
            return false;
        if (!SecureSatchelReqPrimed())
            return false;
        if (_reqUse is not null)
        {
            ItemUseHold reservation = CommenceUseReqReservation();
            try
            {
                _reqUse(objectIdent, reservation);
            }
            catch
            {
                reservation.AbortPriorRelay();
                throw;
            }
        }
        else
        {
            _transmitUse!(objectIdent);
            _runtimeTransactions.IncrementBusyTally();
        }
        return true;
    }

    private bool PerformUseActs(System.Collections.Generic.IReadOnlyList<ItemRulingAction> acts)
    {
        uint openedOrConsumed = 0;
        bool acted = false;
        bool occupiedPossessedByUseReservation = false;
        foreach (var act in acts)
        {
            switch (act.Kind)
            {
                case ItemRulingActionKind.PlaceInBackpack:
                    acted |= PutRealmGearInBackpack(act.ObjectId);
                    break;
                case ItemRulingActionKind.WieldRight:
                case ItemRulingActionKind.WieldLeft:
                case ItemRulingActionKind.AutoSort:
                    if (_objects.Get(act.ObjectId) is { } gear)
                        acted |= _autoWield.TryWield(gear);
                    break;
                case ItemRulingActionKind.OpenContainedContainer:
                case ItemRulingActionKind.SendUse:
                    if (openedOrConsumed != act.ObjectId)
                    {
                        if (_reqUse is not null)
                        {
                            ItemUseHold reservation =
                                CommenceUseReqReservation();
                            try
                            {
                                _reqUse(act.ObjectId, reservation);
                            }
                            catch
                            {
                                reservation.AbortPriorRelay();
                                throw;
                            }
                            occupiedPossessedByUseReservation = true;
                        }
                        else
                        {
                            _transmitUse?.Invoke(act.ObjectId);
                        }
                        openedOrConsumed = act.ObjectId;
                        acted |= _reqUse is not null || _transmitUse is not null;
                    }
                    break;
                case ItemRulingActionKind.SendUseWithTarget:
                    acted |= _runtimeTransactions.TryRelayTargetedUse(
                        act.ObjectId,
                        act.TargetId,
                        _transmitUseWithMark,
                        incrementOccupied: false);
                    break;
                case ItemRulingActionKind.SetGroundObject:
                    _reqExternalVessel?.Invoke(act.ObjectId);
                    acted |= _reqExternalVessel is not null;
                    break;
                case ItemRulingActionKind.EnterTargetMode:
                    JoinMarkManner(act.ObjectId);
                    acted = true;
                    break;
                case ItemRulingActionKind.IncrementBusy:
                    if (occupiedPossessedByUseReservation)
                    {
                        occupiedPossessedByUseReservation = false;
                    }
                    else
                    {
                        _runtimeTransactions.IncrementBusyTally();
                    }
                    break;
                case ItemRulingActionKind.Reject:
                    if (!string.IsNullOrWhiteSpace(act.Message))
                        AnnounceClientOwn(act.Message);
                    break;
                case ItemRulingActionKind.OpenSecureTrade:
                    SecureTradeRequested?.Invoke(act.ObjectId, 0u);
                    acted |= SecureTradeRequested is not null;
                    break;
                default:
                    _auxiliaryAct?.Invoke(act);
                    PolicyActionRequested?.Invoke(act);
                    bool handled = _auxiliaryAct is not null || PolicyActionRequested is not null;
                    if (!handled)
                        AnnounceClientOwn(RuleActMsg(act));
                    acted |= handled || _interfacePhrase is not null
                        || _sysMsg is not null || _toast is not null;
                    break;
            }
        }
        return acted;
    }

    private void PerformStanceActs(System.Collections.Generic.IReadOnlyList<ItemRulingAction> acts)
    {
        foreach (var act in acts)
        {
            switch (act.Kind)
            {
                case ItemRulingActionKind.StartSecureTrade:
                    SecureTradeRequested?.Invoke(act.TargetId, act.ObjectId);
                    break;
                case ItemRulingActionKind.DropToWorld:
                    TryRelaySatchelReq(
                        PackRequestKind.DropToWorld,
                        act.ObjectId,
                        () =>
                        {
                            if (_transmitDiscard is null)
                                return false;
                            _transmitDiscard(act.ObjectId);
                            return true;
                        });
                    break;
                case ItemRulingActionKind.SplitToWorld:
                    TryRelaySatchelReq(
                        PackRequestKind.SplitToWorld,
                        act.ObjectId,
                        () =>
                        {
                            if (_transmitDivideToRealm is null)
                                return false;
                            _transmitDivideToRealm(act.ObjectId, (uint)act.Amount);
                            if (_transactions.TryFetchQueued(out PackRequestInFlight queued)
                                && queued.Kind is PackRequestKind.SplitToWorld
                                && queued.ItemId == act.ObjectId)
                            {
                                WorldDropDispatched?.Invoke(new RealmDropDispatch(
                                    queued,
                                    (uint)act.Amount));
                            }
                            return true;
                        });
                    break;
                case ItemRulingActionKind.GiveToTarget:
                    TryRelaySatchelReq(
                        PackRequestKind.Give,
                        act.ObjectId,
                        () =>
                        {
                            if (_transmitHand is null)
                                return false;
                            _transmitHand(
                                act.TargetId,
                                act.ObjectId,
                                (uint)Math.Max(1, act.Amount));
                            return true;
                        });
                    break;
                case ItemRulingActionKind.PlaceInContainer:
                    {
                        uint wholePile = (uint)Math.Max(
                            1,
                            _objects.Get(act.ObjectId)?.StackSize ?? act.Amount);
                        uint quantity = (uint)Math.Max(1, act.Amount);
                        PackRequestKind sort = quantity < wholePile
                            ? PackRequestKind.SplitToContainer
                            : PackRequestKind.PutInContainer;
                        TryRelaySatchelReq(
                            sort,
                            act.ObjectId,
                            () =>
                            {
                                if (quantity < wholePile)
                                {
                                    if (_transmitDivideToVessel is null)
                                        return false;
                                    _transmitDivideToVessel(
                                        act.ObjectId,
                                        act.TargetId,
                                        0u,
                                        quantity);
                                }
                                else
                                {
                                    if (_transmitPutGearInVessel is null)
                                        return false;
                                    _transmitPutGearInVessel(
                                        act.ObjectId,
                                        act.TargetId,
                                        0);
                                }
                                return true;
                            });
                        break;
                    }
                case ItemRulingActionKind.Reject:
                    if (!string.IsNullOrWhiteSpace(act.Message))
                        AnnounceClientOwn(act.Message);
                    break;
                default:
                    _auxiliaryAct?.Invoke(act);
                    PolicyActionRequested?.Invoke(act);
                    if (_auxiliaryAct is null && PolicyActionRequested is null)
                        AnnounceClientOwn(RuleActMsg(act));
                    break;
            }
        }
    }
}
