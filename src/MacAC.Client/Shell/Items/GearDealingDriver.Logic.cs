using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public sealed partial class GearDealingDriver
{
    public uint PlayerOid => _avatarOid();

    private readonly DealingLedger _dealingPhase;

    public DealingLedger DealingLedger => _dealingPhase;
    private readonly SimDealingTransactionLedger _runtimeTransactions;

    public SimDealingTransactionLedger CoreTransactions => _runtimeTransactions;
    public int OccupiedTally => _transactions.OccupiedCount;

    public uint LatestAppraisalIdent =>
        _runtimeTransactions.LatestAppraisalTag;

    private bool BaseCanCraftSatchelReq
    {
        get
        {
            return _primedForSatchelReq()
        && _transactions.OccupiedCount is 0
        && !_autoWield.IsOccupied;
        }
    }

    public void IncrementOccupiedTally()
        => _runtimeTransactions.IncrementBusyTally();

    public uint QueuedSrcGear
    {
        get
        {
            return _dealingPhase.Current is { Kind: DealingModeKind.UseItemOnTarget } manner
                ? manner.SourceObjectId
                : 0u;
        }
    }

    public GearPrimaryClickResult OfferPrimaryPress(uint markOid)
    {
        var manner = _dealingPhase.Current.Kind;
        if (manner == DealingModeKind.None)
        {
            long instant = _instantMsec();
            if (markOid is not 0
                && markOid == _consumedPrimaryPressMark
                && instant - _consumedPrimaryPressMsec <= CanonDoublePressMsec)
                return GearPrimaryClickResult.ConsumedRejected;
            _consumedPrimaryPressMark = 0;
            return GearPrimaryClickResult.NotActive;
        }

        bool approved;
        switch (manner)
        {
            case DealingModeKind.Use:
                WipeMarkManner();
                approved = EngageGear(markOid);
                break;
            case DealingModeKind.Examine:
                WipeMarkManner();
                approved = markOid is not 0 && _transmitExamine is not null;
                if (approved)
                    ReqAppraisal(markOid);
                break;
            case DealingModeKind.UseItemOnTarget:
                approved = ObtainMark(markOid);
                break;
            default:
                throw new InvalidOperationException($"Unrecognized interaction mode {manner}.");
        }
        _consumedPrimaryPressMark = markOid;
        _consumedPrimaryPressMsec = _instantMsec();
        return approved
            ? GearPrimaryClickResult.ConsumedSuccess
            : GearPrimaryClickResult.ConsumedRejected;
    }

    public GearPrimaryClickResult OfferSelfPrimaryPress()
        => OfferPrimaryPress(_avatarOid());

    public bool EmployChosenOrJoinManner(uint chosenObjectIdent)
    {
        return chosenObjectIdent is not 0 ? EngageGear(chosenObjectIdent) : _dealingPhase.JoinUse();
    }

    public bool StudyChosenOrJoinManner(uint chosenObjectIdent)
    {
        if (chosenObjectIdent is 0)
            return _dealingPhase.JoinExamine();
        if (_transmitExamine is null)
            return false;
        ReqAppraisal(chosenObjectIdent);
        return true;
    }

    public bool WieldFromPaperdoll(uint gearOid, WieldBitmask markBitmask)
    {
        if (gearOid is 0u || markBitmask == WieldBitmask.None)
            return false;
        if (_objects.Get(gearOid) is not { } gear)
            return false;
        return !SecureSatchelReqPrimed() ? false : _autoWield.TryWield(gear, markBitmask);
    }

    public bool ObtainMark(uint markOid)
    {
        if (!IsMarkMannerEngaged || markOid is 0) return false;

        uint srcOid = QueuedSrcGear;
        WipeMarkManner();

        ClientThing? src = _objects.Get(srcOid);
        if (src?.Useability is not { } useability)
            return false;

        bool compatible = MarkCompatible(src, markOid);
        ClientThing? mark = _objects.Get(markOid);
        Console.WriteLine(
            $"[use-target] src=0x{srcOid:X8} use=0x{useability:X8} ttypeMask=0x{src.TargetType ?? 0u:X8}"
            + $" tgt=0x{markOid:X8} tgtKind={(mark is null ? "none" : $"0x{(uint)mark.Type:X8}")}"
            + $" -> {(compatible ? "SEND UseWithTarget" : "refused")}");
        if (!compatible)
            return false;

        if (!SecureSatchelReqPrimed())
            return false;
        _runtimeTransactions.TryRelayTargetedUse(
            srcOid,
            markOid,
            _transmitUseWithMark,
            incrementOccupied: true);
        return true;
    }

    public bool ObtainSelfMark()
        => IsMarkMannerEngaged && ObtainMark(_avatarOid());

    public bool DiscardToRealm(GearDragPayload cargo)
        => SetIn3D(cargo, markOid: 0u);

    public bool SecureSatchelReqPrimed()
    {
        if (CanCraftSatchelReq)
            return true;

        if (_transactions.HasQueuedReq
            || _transactions.OccupiedCount is not 0
            || _autoWield.IsOccupied)
            _sysMsg?.Invoke(SatchelReqOccupiedMsg);
        return false;
    }

    public void AnnounceQueuedBackpackStanceConflict()
    {
        if (_queuedBackpackStance is not { } queued)
            return;
        string? gearLabel = _objects.Get(queued.ItemId)?.Name;
        string label = string.IsNullOrWhiteSpace(gearLabel) ? "that item" : gearLabel;
        _sysMsg?.Invoke($"Already attempting to place {label} here");
    }

    public void AnnounceClientOwn(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg))
            return;
        if (_interfacePhrase is not null)
            _interfacePhrase(msg, CanonLogTextType.ClientLocal);
        else if (_sysMsg is not null)
            _sysMsg(msg);
        else
            _toast?.Invoke(msg);
    }

    public bool IsQueuedSrc(uint gearOid)
        => gearOid is not 0 && gearOid == QueuedSrcGear;

    public bool IsQueuedSatchelSrc(uint gearOid)
    {
        return gearOid is not 0
                && _transactions.TryFetchQueued(out PackRequestInFlight queued)
                && queued.ItemId == gearOid;
    }

    public bool IsPossessedByAvatar(uint gearOid)
        => _objects.IsPossessedByObject(gearOid, _avatarOid());

    public bool IsLatestMarkCompatible(uint markOid)
    {
        if (!IsMarkMannerEngaged || markOid is 0) return false;

        ClientThing? src = _objects.Get(QueuedSrcGear);
        return src?.Useability is not null
            && MarkCompatible(src, markOid);
    }

    public bool IsToolbarUseTurnedOn(uint chosenObjectIdent)
    {
        return chosenObjectIdent is 0
            || _objects.Get(chosenObjectIdent) is not { } gear
            ? false
            : ItemInteractRules.IsToolbarUseEnabled(
            gear.Type,
            gear.CombatUse ?? 0,
            gear.Useability ?? GearUseability.Undef);
    }

    public AssayReplyAcceptance AdmitAppraisalResponse(uint objectIdent)
    {
        bool possessedOccupiedReference = _transactions.OccupiedCount > 0;
        var acceptance =
            _runtimeTransactions.AllowAppraisalResponse(objectIdent);
        if (acceptance.FirstResponse && !possessedOccupiedReference)
            StateChanged?.Invoke();
        return new AssayReplyAcceptance(
            acceptance.Accepted,
            acceptance.FirstResponse);
    }

    public bool CanCraftSatchelReq =>
        BaseCanCraftSatchelReq && !_transactions.HasQueuedReq;

    public bool RenewLatestAppraisal()
    {
        return _transmitExamine is null ? false : _runtimeTransactions.UpdateLatestAppraisal(_transmitExamine);
    }

    public void AbortObjectAppraisalForArcanum()
    {
        if (_transmitExamine is null)
            return;
        bool possessedOccupiedReference = _transactions.OccupiedCount > 0;
        if (_runtimeTransactions.RevokeObjectAppraisalForArcanum(_transmitExamine)
            && !possessedOccupiedReference)

            StateChanged?.Invoke();
    }

    public void AbortQueuedBackpackStance(uint gearOid = 0u, ulong ticket = 0u)
    {
        if (_queuedBackpackStance is not { } queued
            || (gearOid is not 0u && queued.ItemId != gearOid)
            || (ticket is not 0u && queued.Token != ticket))

            return;

        _queuedBackpackStance = null;
        _transactions.RevokePriorRelay(queued.Token);
        List<Exception> misses = [];
        RelayAll(PendingBackpackPlacementCancelled, queued, misses);
        if (misses.Count is not 0)
        {
            throw new AggregateException(
                "One or more pending-placement cancellation observers failed",
                misses);
        }
    }

    public bool IsMarkMannerEngaged
        => _dealingPhase.Current.Kind == DealingModeKind.UseItemOnTarget;

    public bool IsAnyObjectiveMannerEngaged
        => _dealingPhase.Current.Kind != DealingModeKind.None;

    public void AbortObjectiveManner()
    {
        if (!IsAnyObjectiveMannerEngaged) return;
        WipeMarkManner();
    }

    public bool EngageGear(uint gearOid)
    {
        if (gearOid is 0) return false;

        if (IsMarkMannerEngaged)
            return OfferPrimaryPress(gearOid) == GearPrimaryClickResult.ConsumedSuccess;

        ClientThing? gear = _objects.Get(gearOid);
        if (gear is null) return false;

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
            BypassClassification: false,
            UseCurrentSelection: false,
            SelectedTarget: null,
            ConfirmVolatileRareUses: true,
            InNonCombatMode: _inNonFightingManner());
        ItemUseRuling decision = ItemInteractRules.DecideUse(feed);
        return PerformUseActs(decision.Actions);
    }

    public bool PutRealmGearInBackpack(uint gearOid, bool primaryBundle = false)
    {
        if (gearOid is 0u || _placeInBackpack is null)
            return false;

        uint trunk = _avatarOid();
        uint mark = primaryBundle ? trunk : _backpackVesselIdent();
        if (mark is 0u)
            mark = trunk;
        const int stance = 0;

        uint vesselIdent = PackSlotSearch.SelectVessel(
            _objects, gearOid, trunk, mark, trunk,
            out PackPlacementRefusal noHall);
        if (vesselIdent is 0u)
        {
            if (PackPlacementPolicy.ConstructClientOwn(
                    noHall, _objects.Get(gearOid), _objects.Get(trunk), trunk) is { } wholeNotice)

                AnnounceClientOwn(wholeNotice);
            return true;
        }

        if (TryPlanAutoCombine(gearOid) is { } combine)
        {
            return !TryRelayQueuedBackpackStance(
                    gearOid,
                    vesselIdent,
                    stance,
                    PackRequestKind.Merge,
                    () =>
                    {
                        _transmitStackableCombine!(
                            combine.SourceObjectId,
                            combine.TargetObjectId,
                            combine.Amount);
                        MergeAttempted?.Invoke(
                            combine.SourceObjectId,
                            combine.TargetObjectId);
                        return true;
                    })
                ? true
                : true;
        }

        if (!TryCommencePendingBackpackPlacement(
                gearOid,
                vesselIdent,
                stance,
                out _))

            return true;
        _placeInBackpack(gearOid, vesselIdent, stance);
        return true;
    }

    public bool PutChosenIn3D(uint gearOid, uint markOid)
        => PutIn3D(gearOid, GearDragSource.Inventory, markOid);

    public bool SetIn3D(GearDragPayload cargo, uint markOid)
    {
        ArgumentNullException.ThrowIfNull(cargo);

        return PutIn3D(cargo.ObjId, cargo.SourceKind, markOid);
    }

    public void NotifyExplicitFightingModeRequest()
        => _autoWield.AlertExplicitCombatModeRequest();

    public void WipeOccupied()
        => _transactions.PurgeOccupied();

    public bool IsAutoWieldOccupied =>
        _autoWield.IsOccupied || !_transactions.CanCommenceReq;

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _dealingPhase.Changed -= OnDealingMannerAltered;
        _objects.MoveRequestFailed -= OnRelocateReqFailedNotice;
        _transactions.ObjectTableCleared -= OnSatchelObjectsCleared;
        _transactions.RequestFailed -= OnSatchelReqFailed;
        _transactions.RequestCompleted -= OnSatchelReqFinished;
        _transactions.StateChanged -= OnTransactionPhaseAltered;
        WorldDropDispatched = null;
        MergeAttempted = null;
        _autoWield.Dispose();
    }

    public void ConcludeUse(uint problem)
        => _runtimeTransactions.CompleteUse(problem);

    public void ResetSession()
        => RestartSessCore(restartCore: true);

    internal bool IsInLatestTerrainObject(uint objectIdent)
    {
        uint terrainObjectIdent = _terrainObjectIdent();
        return terrainObjectIdent is not 0u
            && _objects.Get(objectIdent) is { } gear
            && gear.VesselTag == terrainObjectIdent;
    }

    internal bool IsLatestObjectPersona(uint objectIdent, ClientThing gear)
        => ReferenceEquals(_objects.Get(objectIdent), gear);

    internal void RestartGenExhibit()
        => RestartSessCore(restartCore: false);

    private void ReqAppraisal(uint objectIdent)
    {
        if (objectIdent is 0 || _transmitExamine is null)
            return;
        _runtimeTransactions.TryReqAppraisal(objectIdent, _transmitExamine);
    }

    private IEnumerable<uint> ExhaustiveInsides(
        uint vesselIdent,
        HashSet<uint> visitedVessels)
    {
        if (!visitedVessels.Add(vesselIdent))
            yield break;

        foreach (uint gearIdent in _objects.FetchInsides(vesselIdent))
        {
            yield return gearIdent;
            if (_objects.FetchInsides(gearIdent).Count is 0)
                continue;
            foreach (uint nested in ExhaustiveInsides(gearIdent, visitedVessels))
                yield return nested;
        }
    }

    private static StackMergeCandidate ToPileCombineGear(ClientThing gear)
    {
        return new(
        gear.ObjectId,
        gear.WeenieClassIdent,
        gear.StackSize,
        gear.PileDimsUpper,
        gear.BarterPhase);
    }

    private void JoinMarkManner(uint srcOid)
    {
        _consumedPrimaryPressMark = 0;
        _dealingPhase.JoinUseGearOnMark(srcOid);
        string? label = _objects.Get(srcOid)?.Name;
        if (!string.IsNullOrWhiteSpace(label))
            AnnounceClientOwn($"Choose a target for the {label}");
    }

    private static string RuleActMsg(ItemRulingAction act)
    {
        return act.Kind switch
        {
            ItemRulingActionKind.ConfirmPlayerKillerSwitch =>
                "Confirm using this Player Killer altar before continuing.",
            ItemRulingActionKind.ConfirmNonPlayerKillerSwitch =>
                "Confirm using this Non-Player Killer altar before continuing.",
            ItemRulingActionKind.ConfirmVolatileRare =>
                "Confirm using this volatile rare before continuing.",
            ItemRulingActionKind.OpenSecureTrade or ItemRulingActionKind.StartSecureTrade =>
                "Secure trade is not open.",
            ItemRulingActionKind.OpenSalvage => "Open the salvage panel to use that item.",
            _ => "That item action is not available here.",
        };
    }

    private static void RelayAll(Action? listeners, List<Exception> misses)
    {
        if (listeners is null)
            return;
        foreach (Action listener in listeners.GetInvocationList())
        {
            try { listener(); }
            catch (Exception problem) { misses.Add(problem); }
        }
    }

    private bool AbsorbUseThrottle()
        => _runtimeTransactions.TryAbsorbUseThrottle(_instantMsec());

    private ItemRulingSubject Freeze(ClientThing gear)
    {
        var flagSet = (PublicWeenieBits)(gear.PublicWeenieBitfield ?? 0u);
        if (gear.ObjectId == _avatarOid())
            flagSet |= PublicWeenieBits.Player;

        bool possessed = gear.ObjectId == _avatarOid()
            || IsCarriedByAvatar(gear)
            || IsEquippedByAvatar(gear);
        int pileDims = Math.Max(1, gear.StackSize);
        return new ItemRulingSubject(
            gear.ObjectId,
            gear.Type,
            flagSet,
            gear.VesselTag,
            gear.WielderIdent,
            gear.ValidLocations,
            gear.CurrentlyEquippedLocale,
            gear.CombatUse ?? 0,
            gear.ItemsCapacity,
            gear.ContainersCapacity,
            gear.Useability ?? 0u,
            gear.TargetType ?? 0u,
            possessed,
            IsVessel(gear),
            gear.IsModulePack || _isModuleBundle(gear.WeenieClassIdent),
            gear.BarterPhase,
            pileDims,
            pileDims,
            IsIn3DView: gear.VesselTag is 0 && gear.WielderIdent is 0
                && gear.ObjectId != _avatarOid(),
            Name: gear.FetchAppropriateLabel());
    }

    private bool MarkCompatible(ClientThing src, uint markOid)
    {
        ClientThing? mark = _objects.Get(markOid);
        return mark is null
            ? false
            : ItemInteractRules.IsObjectiveCompatible(
            Freeze(src), Freeze(mark), _avatarOid());
    }

    private static bool IsVessel(ClientThing gear)
    {
        return gear.VesselKindHint is not 0
            || gear.Type.HasFlag(GearKind.Container)
            || gear.ItemsCapacity > 0;
    }

    private bool IsEquippedByAvatar(ClientThing gear)
    {
        uint avatar = _avatarOid();
        return gear.CurrentlyEquippedLocale != WieldBitmask.None
            && (gear.WielderIdent == avatar || gear.VesselTag == avatar);
    }

    private bool IsCarriedByAvatar(ClientThing gear)
    {
        uint avatar = _avatarOid();
        uint vessel = gear.VesselTag;
        for (int hops = 0; vessel is not 0 && hops < 8; ++hops)
        {
            if (vessel == avatar) return true;
            vessel = _objects.Get(vessel)?.VesselTag ?? 0u;
        }
        return false;
    }

    private bool PutIn3D(
        uint gearOid,
        GearDragSource srcSort,
        uint markOid)
    {
        if (srcSort == GearDragSource.ShortcutBar)
            return false;
        if (gearOid is 0 || _objects.Get(gearOid) is not { } gear)
            return false;
        if (!SecureSatchelReqPrimed())
            return false;

        uint wholePile = (uint)Math.Max(1, gear.StackSize);
        int divideDims = (int)(_pileDivideQty?.FetchObjectDivideDims(
            gear.ObjectId, _chosenObjectIdent(), wholePile) ?? wholePile);
        ClientThing? mark = markOid is 0u ? null : _objects.Get(markOid);
        ItemPlacementRuling decision = ItemInteractRules.DecideStance(new ItemPlacementQuery(
            Freeze(gear),
            _avatarOid(),
            _terrainObjectIdent(),
            CanCraftSatchelReq,
            TargetId: markOid,
            Target: mark is null ? null : Freeze(mark),
            AllowGroundFallback: true,
            MergeAccepted: false,
            DragOnPlayerOpensSecureTrade: _pullOnAvatarOpensSecureBarter(),
            PlayerOnGround: _avatarOnTerrain(),
            SplitSize: divideDims));
        PerformStanceActs(decision.Actions);
        return decision.ReturnValue;
    }

    private ItemUseHold CommenceUseReqReservation()
        => _runtimeTransactions.OpenUseReqReservation();

    private void WipeMarkManner() => _dealingPhase.Clear();

    private void OnDealingMannerAltered(DealingModeShift _)
    {
        List<Exception> misses = [];
        RelayAll(StateChanged, misses);
        if (misses.Count is not 0)
            throw new AggregateException(
                "One or more item-interaction observers failed",
                misses);
    }

    private void OnTransactionPhaseAltered()
    {
        List<Exception> misses = [];
        RelayAll(StateChanged, misses);
        if (misses.Count is not 0)
        {
            throw new AggregateException(
                "One or more item-interaction observers failed",
                misses);
        }
    }

    private void OnSatchelReqFinished(PackRequestInFlight req)
    {
        if (_queuedBackpackStance is { } stance
            && stance.Token == req.Token
            && stance.ItemId == req.ItemId)
        {
            _queuedBackpackStance = null;
            List<Exception> misses = [];
            RelayAll(PendingBackpackPlacementResolved, stance, misses);
            if (misses.Count is not 0)
            {
                throw new AggregateException(
                    "One or more pending-placement resolution observers failed",
                    misses);
            }
        }
    }

    private void OnSatchelObjectsCleared() => _queuedBackpackStance = null;

    private void OnSatchelReqFailed(
        PackRequestInFlight req,
        uint weenieProblem)
    {
        ClientThing? gear = req.ItemIdentity ?? _objects.Get(req.ItemId);
        if (gear is null)
            return;
        bool plural = req.Kind
            is PackRequestKind.Merge
            or PackRequestKind.SplitToContainer
            or PackRequestKind.SplitToWorld;
        string label = plural && !string.IsNullOrEmpty(gear.PluralName)
            ? gear.PluralName
            : gear.FetchAppropriateLabel();
        if (string.IsNullOrEmpty(label))
            return;
        if (InventoryRefusalText.Compose(req.Kind, label, weenieProblem)
            is { } phrase)

            AnnounceClientOwn(phrase);
    }

    private void OnRelocateReqFailedNotice(RelocationRefusal miss)
    {
        if (_interfacePhrase is null
            || miss.WeenieError is 0u
            || InventoryRefusalText.SuppressesGenericMissPhrase(
                miss.WeenieError))

            return;
        var (phrase, kind) = WeenieErrorText.Resolve(miss.WeenieError, null);
        if (phrase is not null)
            _interfacePhrase(phrase, kind);
    }

    private void RestartSessCore(bool restartCore)
    {
        var queuedStance = _queuedBackpackStance;
        _queuedBackpackStance = null;
        if (restartCore)
        {
            _transactions.StateChanged -= OnTransactionPhaseAltered;
            try
            {
                _runtimeTransactions.ResetSession();
            }
            finally
            {
                if (!_destroyed)
                    _transactions.StateChanged += OnTransactionPhaseAltered;
            }
        }
        _consumedPrimaryPressMark = 0u;
        _consumedPrimaryPressMsec = long.MinValue / 2;

        List<Exception> misses = [];
        if (restartCore)
        {
            try { _dealingPhase.ResetSession(); }
            catch (Exception problem) { misses.Add(problem); }
        }

        if (queuedStance is { } queued)
            RelayAll(PendingBackpackPlacementCancelled, queued, misses);

        if (misses.Count is not 0)
            throw new AggregateException(
                "One or more item-interaction reset observers failed",
                misses);
    }
}
