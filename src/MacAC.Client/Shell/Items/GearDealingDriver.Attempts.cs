using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public sealed partial class GearDealingDriver
{
    public bool TryPurchase(uint merchantOid, uint gearOid, int quantity, uint alternateCurrencyIdent)
    {
        if (merchantOid is 0u || gearOid is 0u || quantity <= 0 || _transmitPurchase is null)
            return false;
        if (!SecureSatchelReqPrimed())
            return false;

        ItemUseHold reservation = CommenceUseReqReservation();
        bool dispatched;
        try
        {
            dispatched = _transmitPurchase(merchantOid, gearOid, quantity, alternateCurrencyIdent);
        }
        catch
        {
            reservation.AbortPriorRelay();
            throw;
        }

        if (!dispatched)
        {
            reservation.AbortPriorRelay();
            return false;
        }

        reservation.FlagDispatched();
        return true;
    }

    public bool TryPurchaseAll(
        uint merchantOid,
        IReadOnlyList<(int Amount, uint ItemGuid)> gearList,
        uint alternateCurrencyIdent)
    {
        if (merchantOid is 0u || gearList is null || gearList.Count is 0 || _transmitPurchaseAll is null)
            return false;
        if (!SecureSatchelReqPrimed())
            return false;

        ItemUseHold reservation = CommenceUseReqReservation();
        bool dispatched;
        try
        {
            dispatched = _transmitPurchaseAll(merchantOid, gearList, alternateCurrencyIdent);
        }
        catch
        {
            reservation.AbortPriorRelay();
            throw;
        }

        if (!dispatched)
        {
            reservation.AbortPriorRelay();
            return false;
        }

        reservation.FlagDispatched();
        return true;
    }

    public bool TryVend(
        uint merchantOid,
        IReadOnlyList<(int Amount, uint ItemGuid)> gearList)
    {
        if (merchantOid is 0u || gearList is null || gearList.Count is 0 || _transmitVend is null)
            return false;
        if (!SecureSatchelReqPrimed())
            return false;

        ItemUseHold reservation = CommenceUseReqReservation();
        bool dispatched;
        try
        {
            dispatched = _transmitVend(merchantOid, gearList);
        }
        catch
        {
            reservation.AbortPriorRelay();
            throw;
        }

        if (!dispatched)
        {
            reservation.AbortPriorRelay();
            return false;
        }

        reservation.FlagDispatched();
        return true;
    }

    public bool TryRelaySatchelReq(
        PackRequestKind sort,
        uint gearIdent,
        Func<bool> relay,
        ulong reservationTicket = 0u)
    {
        ArgumentNullException.ThrowIfNull(relay);
        if (gearIdent is 0u)
            return false;

        if (reservationTicket is not 0u)
        {
            try
            {
                return _transactions.TryRelay(
                    sort,
                    gearIdent,
                    relay,
                    reservationTicket);
            }
            catch
            {
                AbortQueuedBackpackStance(gearIdent, reservationTicket);
                throw;
            }
        }

        return !SecureSatchelReqPrimed() ? false : _transactions.TryRelay(sort, gearIdent, relay);
    }

    public bool TryFetchQueuedSatchelReq(out PackRequestInFlight queued)
        => _transactions.TryFetchQueued(out queued);

    public bool TryDivideToVessel(
        uint gearIdent,
        uint vesselIdent,
        uint stance,
        uint quantity)
    {
        if (gearIdent is 0u
            || vesselIdent is 0u
            || _transmitDivideToVessel is null
            || _objects.Get(gearIdent) is not { } gear)

            return false;

        uint wholePile = (uint)Math.Max(1, gear.StackSize);
        return quantity is 0u || quantity >= wholePile
            ? false
            : TryRelaySatchelReq(
            PackRequestKind.SplitToContainer,
            gearIdent,
            () =>
            {
                _transmitDivideToVessel(gearIdent, vesselIdent, stance, quantity);
                return true;
            });
    }

    public bool TryRelocateGearForAutomation(
        uint gearIdent,
        uint vesselIdent,
        uint quantity = 0u,
        int stance = 0)
    {
        if (gearIdent is 0u
            || vesselIdent is 0u
            || _transmitPutGearInVessel is null
            || _objects.Get(gearIdent) is not { } gear
            || !IsPossessedByAvatar(gearIdent)
            || (vesselIdent != _avatarOid() && !IsPossessedByAvatar(vesselIdent)))

            return false;

        uint wholePile = (uint)Math.Max(1, gear.StackSize);
        uint asked = quantity is 0u ? wholePile : quantity;
        if (asked is 0u || asked > wholePile)
            return false;
        return asked < wholePile
            ? TryDivideToVessel(
                gearIdent,
                vesselIdent,
                (uint)Math.Max(0, stance),
                asked)
            : TryRelaySatchelReq(
            PackRequestKind.PutInContainer,
            gearIdent,
            () =>
            {
                _transmitPutGearInVessel(gearIdent, vesselIdent, stance);
                return true;
            });
    }

    public bool TryCombineGearListForAutomation(
        uint srcGearIdent,
        uint markGearIdent,
        uint quantity = 0u)
    {
        if (_transmitStackableCombine is null
            || !IsPossessedByAvatar(srcGearIdent)
            || !IsPossessedByAvatar(markGearIdent)
            || _objects.Get(srcGearIdent) is not { } src
            || _objects.Get(markGearIdent) is not { } mark)

            return false;

        int asked = quantity > int.MaxValue ? int.MaxValue : (int)quantity;
        var plan = StackMergeRouter.Plan(
            ToPileCombineGear(src),
            ToPileCombineGear(mark),
            CanCraftSatchelReq,
            asked);
        return plan is not { } combine
            ? false
            : TryRelaySatchelReq(
            PackRequestKind.Merge,
            srcGearIdent,
            () =>
            {
                _transmitStackableCombine(
                    combine.SourceObjectId,
                    combine.TargetObjectId,
                    combine.Amount);
                MergeAttempted?.Invoke(
                    combine.SourceObjectId,
                    combine.TargetObjectId);
                return true;
            });
    }

    public bool TryDiscardGearForAutomation(uint gearIdent, uint quantity = 0u)
    {
        if (!IsPossessedByAvatar(gearIdent) || _objects.Get(gearIdent) is not { } gear)
            return false;

        uint wholePile = (uint)Math.Max(1, gear.StackSize);
        uint asked = quantity is 0u ? wholePile : quantity;
        if (asked is 0u || asked > wholePile)
            return false;
        PackRequestKind sort = asked < wholePile
            ? PackRequestKind.SplitToWorld
            : PackRequestKind.DropToWorld;
        return TryRelaySatchelReq(
            sort,
            gearIdent,
            () =>
            {
                if (asked < wholePile)
                {
                    if (_transmitDivideToRealm is null)
                        return false;
                    _transmitDivideToRealm(gearIdent, asked);
                }
                else
                {
                    if (_transmitDiscard is null)
                        return false;
                    _transmitDiscard(gearIdent);
                }
                return true;
            });
    }

    public bool TryHandGearForAutomation(
        uint gearIdent,
        uint markIdent,
        uint quantity = 0u)
    {
        if (_transmitHand is null
            || markIdent is 0u
            || markIdent == _avatarOid()
            || _objects.Get(markIdent) is null
            || !IsPossessedByAvatar(gearIdent)
            || _objects.Get(gearIdent) is not { } gear)

            return false;

        uint wholePile = (uint)Math.Max(1, gear.StackSize);
        uint asked = quantity is 0u ? wholePile : quantity;
        return asked is 0u || asked > wholePile
            ? false
            : TryRelaySatchelReq(
            PackRequestKind.Give,
            gearIdent,
            () =>
            {
                _transmitHand(markIdent, gearIdent, asked);
                return true;
            });
    }

    public bool TrySalvageGearListForAutomation(
        uint toolIdent,
        IReadOnlyList<uint> gearIdents)
        => TrySalvageGearList(toolIdent, gearIdents);

    public bool TrySalvageGearList(uint toolIdent, IReadOnlyList<uint> gearIdents)
    {
        if (_transmitSalvage is null
            || toolIdent is 0u
            || gearIdents is null
            || gearIdents.Count is 0
            || !CanCraftSatchelReq
            || !IsPossessedByAvatar(toolIdent)
            || _objects.Get(toolIdent) is not { } tool
            || (tool.Type & GearKind.TinkeringTool) == 0)

            return false;

        HashSet<uint> distinct = new HashSet<uint>();
        foreach (uint gearIdent in gearIdents)
        {
            if (gearIdent is 0u
                || gearIdent == toolIdent
                || !distinct.Add(gearIdent)
                || !IsPossessedByAvatar(gearIdent)
                || _objects.Get(gearIdent) is not { } gear
                || !SalvageRules.IsSuitable(gear))

                return false;
        }
        return _transmitSalvage(toolIdent, gearIdents);
    }

    public bool TryEvaluateForAutomation(uint objectIdent)
    {
        return objectIdent is 0u
            || _transmitExamine is null
            || _objects.Get(objectIdent) is null
            ? false
            : _runtimeTransactions.TryReqAppraisal(objectIdent, _transmitExamine);
    }

    public bool TryUseGearForAutomation(uint gearOid)
    {
        if (gearOid is 0u || _objects.Get(gearOid) is not { } gear)
            return false;
        if (GearUseability.IsTargeted(gear.Useability ?? GearUseability.Undef))
            return false;
        if (!AbsorbUseThrottle())
            return false;
        if (!SecureSatchelReqPrimed())
            return false;

        ItemUseQuery feed = new ItemUseQuery(
            Freeze(gear),
            _avatarOid(),
            _terrainObjectIdent(),
            CanCraftSatchelReq,
            _engagedMerchantIdent(),
            BypassClassification: true,
            UseCurrentSelection: false,
            SelectedTarget: null,
            ConfirmVolatileRareUses: true,
            InNonCombatMode: _inNonFightingManner());
        var decision = ItemInteractRules.DecideUse(feed);
        bool sends = decision.Actions.Any(static act =>
            act.Kind == ItemRulingActionKind.SendUse);
        return sends && PerformUseActs(decision.Actions);
    }

    public bool TryEnactGear(uint gearOid, uint markOid)
    {
        if (gearOid is 0u || markOid is 0u)
            return false;
        if (_objects.Get(gearOid) is not { } gear
            || _objects.Get(markOid) is not { } mark)

            return false;
        if (!AbsorbUseThrottle())
            return true;
        if (!SecureSatchelReqPrimed())
            return false;

        ItemUseQuery feed = new ItemUseQuery(
            Freeze(gear),
            _avatarOid(),
            _terrainObjectIdent(),
            CanCraftSatchelReq,
            _engagedMerchantIdent(),
            BypassClassification: true,
            UseCurrentSelection: true,
            SelectedTarget: Freeze(mark),
            ConfirmVolatileRareUses: true,
            InNonCombatMode: _inNonFightingManner());
        var decision = ItemInteractRules.DecideUse(feed);
        bool sends = decision.Actions.Any(static act =>
            act.Kind == ItemRulingActionKind.SendUseWithTarget);
        return sends && PerformUseActs(decision.Actions);
    }

    public bool TryCommencePendingBackpackPlacement(
        uint gearOid,
        uint vesselIdent,
        int stance,
        out QueuedBackpackStance queued)
    {
        return TryCommenceQueuedBackpackStance(
                gearOid,
                vesselIdent,
                stance,
                PackRequestKind.Pickup,
                out queued);
    }

    public bool TryRelayQueuedBackpackStance(
        uint gearOid,
        uint vesselIdent,
        int stance,
        PackRequestKind sort,
        Func<bool> relay)
    {
        if (!SecureSatchelReqPrimed())
            return false;
        if (!TryCommenceQueuedBackpackStance(
                gearOid,
                vesselIdent,
                stance,
                sort,
                out QueuedBackpackStance queued))

            return false;

        if (_queuedBackpackStance != queued
            || !_transactions.TryFetchQueued(out PackRequestInFlight reserved)
            || reserved.Token != queued.Token
            || reserved.ItemId != queued.ItemId
            || reserved.Kind != sort
            || reserved.Dispatched)

            return false;

        if (TryRelaySatchelReq(
                sort,
                gearOid,
                relay,
                queued.Token))
            return true;

        AbortQueuedBackpackStance(gearOid, queued.Token);
        return false;
    }

    public bool TryFetchQueuedBackpackStance(
        uint gearOid,
        out QueuedBackpackStance queued)
    {
        if (_queuedBackpackStance is { } latest
            && latest.ItemId == gearOid)
        {
            queued = latest;
            return true;
        }

        queued = default;
        return false;
    }

    public bool TryWieldGear(uint gearOid, WieldBitmask askedBitmask = WieldBitmask.None)
    {
        if (gearOid is 0u || _objects.Get(gearOid) is not { } gear)
            return false;
        return !SecureSatchelReqPrimed()
            ? false
            : askedBitmask == WieldBitmask.None
            ? _autoWield.TryWield(gear)
            : _autoWield.TryWield(gear, askedBitmask);
    }

    public bool TryOpenSecureBarterWithAvatar(uint markOid)
    {
        if (markOid is 0u || markOid == _avatarOid())
            return false;
        var mark = _objects.Get(markOid);
        if (mark is null
            || ((PublicWeenieBits)(mark.PublicWeenieBitfield ?? 0u)
                & PublicWeenieBits.Player) == 0)
            return false;

        if (_inNonFightingManner())
            SecureTradeRequested?.Invoke(markOid, 0u);
        return true;
    }

    internal bool TryGrabObjectPersona(uint objectIdent, out ClientThing gear)
    {
        gear = _objects.Get(objectIdent)!;
        return gear is not null;
    }

    private StackMergeRoute? TryPlanAutoCombine(uint srcIdent)
    {
        if (_transmitStackableCombine is null
            || _objects.Get(srcIdent) is not { } src
            || src.PileDimsUpper <= 1)

            return null;

        uint asked = _pileDivideQty?.FetchObjectDivideDims(
                srcIdent,
                _chosenObjectIdent(),
                (uint)Math.Max(1, src.StackSize))
            ?? (uint)Math.Max(1, src.StackSize);
        int askedQuantity = (int)Math.Min(asked, int.MaxValue);
        StackMergeCandidate srcCombine = ToPileCombineGear(src);
        uint avatar = _avatarOid();
        if (avatar is 0u)
            return null;

        HashSet<uint> visitedVessels = new HashSet<uint>();
        foreach (uint markIdent in ExhaustiveInsides(avatar, visitedVessels))
        {
            if (_objects.Get(markIdent) is not { } mark)
                continue;
            var plan = StackMergeRouter.Plan(
                srcCombine,
                ToPileCombineGear(mark),
                CanCraftSatchelReq,
                askedQuantity);
            if (plan is { } done && done.Amount == asked)
                return done;
        }
        return null;
    }

    private bool TryCommenceQueuedBackpackStance(
        uint gearOid,
        uint vesselIdent,
        int stance,
        PackRequestKind sort,
        out QueuedBackpackStance queued)
    {
        if (gearOid is 0u || vesselIdent is 0u)
        {
            queued = default;
            return false;
        }

        if (_queuedBackpackStance is { } extant)
        {
            AnnounceQueuedBackpackStanceConflict();
            queued = extant;
            return false;
        }
        if (!SecureSatchelReqPrimed())
        {
            queued = default;
            return false;
        }

        QueuedBackpackStance contender = default;
        bool reserved;
        PackRequestInFlight published;
        try
        {
            reserved = _transactions.TryReserve(
                sort,
                gearOid,
                out published,
                req =>
                {
                    contender = new QueuedBackpackStance(
                        req.Token,
                        gearOid,
                        vesselIdent,
                        stance,
                        req.ItemIdentity);
                    _queuedBackpackStance = contender;
                    List<Exception> misses = [];
                    RelayAll(
                        PendingBackpackPlacementRequested,
                        contender,
                        misses);
                    if (misses.Count is not 0)
                    {
                        throw new AggregateException(
                            "One or more pending-placement observers failed",
                            misses);
                    }
                });
        }
        catch (Exception miss)
        {
            if (contender.Token is 0u)
                throw;

            _queuedBackpackStance = null;
            _transactions.RevokePriorRelay(contender.Token);
            List<Exception> misses = [miss];
            RelayAll(
                PendingBackpackPlacementCancelled,
                contender,
                misses);
            throw new AggregateException(
                "Pending backpack placement publication failed",
                misses);
        }
        queued = contender;
        return reserved
            && _queuedBackpackStance == contender
            && published.Token == contender.Token
            && published.ItemId == gearOid
            && published.Kind == sort
            && !published.Dispatched;
    }
}
