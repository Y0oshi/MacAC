using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ToolbarDriver
{

    public void OnPullLift(WidgetGearRoster srcRoster, WidgetGearSlot srcChamber, GearDragPayload cargo)
    {
        if (cargo.ObjId is not 0 && _chosenObjectIdent() != cargo.ObjId)
            _pickGear?.Invoke(cargo.ObjId);

        _transmitDropShortcut?.Invoke((uint)cargo.SourceSlot);
        _store.Remove(cargo.SourceSlot);
    }

    public GearDragAcceptance OnPullOver(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
    {
        return cargo.ObjId is not 0 ? GearDragAcceptance.Accept : GearDragAcceptance.None;
    }

    public static ToolbarDriver Bind(
        ImportedArrangement arrangement,
        ClientThingChart repo,
        HotbarStore shortcuts,
        Func<GearKind, uint, uint, uint, uint, uint> glyphIdents,
        Action<uint> useGear,
        FightingPhase? fightingPhase = null,
        uint[]? regularDigits = null,
        uint[]? ghostedDigits = null,
        uint[]? vacantDigits = null,
        GearDealingDriver? gearDealing = null,
        Action<HotbarSlot>? transmitAppendShortcut = null,
        Action<uint>? transmitDropShortcut = null,
        Action? flipFighting = null,
        Action<uint>? pickGear = null,
        Func<uint>? chosenObjectIdent = null,
        PickPhase? pick = null,
        Func<uint>? avatarOid = null,
        Action<uint, uint, int>? transmitPutGearInVessel = null,
        WidgetDatFont? ammoTypeface = null,
        Func<GearKind, uint, uint, uint, uint, uint>? pullGlyphIdents = null)
    {
        ToolbarDriver driver = new ToolbarDriver(arrangement, repo, shortcuts, glyphIdents, useGear, fightingPhase,
                                      regularDigits, ghostedDigits, vacantDigits, gearDealing,
                                      transmitAppendShortcut, transmitDropShortcut, flipFighting, pickGear,
                                      chosenObjectIdent, pick, avatarOid, transmitPutGearInVessel, ammoTypeface,
                                      pullGlyphIdents);
        driver.Populate();
        return driver;
    }

    public void AttachBoardBtns(Func<uint, bool> isOnHand, Action<uint> flipBoard)
    {
        ArgumentNullException.ThrowIfNull(isOnHand);
        ArgumentNullException.ThrowIfNull(flipBoard);

        foreach (var (boardIdent, btn) in _boardBtns)
        {
            bool onHand = isOnHand(boardIdent);
            btn.Enabled = onHand;
            btn.OnClick = !onHand ? null : () =>
            {
                if (ReferenceEquals(btn, _satchelBtn)
                    && _gearDealing?.OfferSelfPrimaryPress()
                        is not null and not GearPrimaryClickResult.NotActive)
                    return;
                flipBoard(boardIdent);
            };
        }
    }

    public void AssignBoardOpen(uint boardIdent, bool open)
    {
        foreach (var listing in _boardBtns)
        {
            if (listing.PanelId != boardIdent || !listing.Button.Enabled) continue;
            listing.Button.TrySetCanonPhase(open
                ? WidgetButtonStateMachine.Highlight
                : WidgetButtonStateMachine.Normal);
            return;
        }
    }

    public void AssignFightingManner(FightingManner manner)
    {
        bool[] unhide =
        [
            manner == FightingManner.NonCombat,
            manner == FightingManner.Melee,
            manner == FightingManner.Missile,
            manner == FightingManner.Magic,
        ];

        for (int idx = 0; idx < _fightingIndicators.Length; ++idx)
        {
            if (_fightingIndicators[idx] is { } element)
                element.Visible = unhide[idx];
        }

        _shortcutsGhosted = manner == FightingManner.Magic;
        RestampShortcutNumbers();
    }

    public void Populate()
    {
        foreach (var roster in _sockets) roster?.Cell.Clear();

        for (int socket = 0; socket < _sockets.Length; ++socket)
        {
            HotbarSlot? listing = _store.FetchListing(socket);
            uint oid = listing?.ObjectId ?? 0u;
            if (oid is 0) continue;
            WidgetGearRoster? roster = _sockets[socket];
            if (roster is null) continue;
            ClientThing? gear = _repo.Get(oid);
            if (gear is null) continue;
            uint bmp = _glyphIdents(gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
            uint pullBmp = _pullGlyphIdents?.Invoke(
                gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects) ?? 0u;
            roster.Cell.AssignGear(oid, bmp, listing, pullBmp);
        }

        RestampShortcutNumbers();
    }

    public void ProcessDiscardFree(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
    {
        HotbarDropSource src = cargo.SourceKind == GearDragSource.ShortcutBar
            ? HotbarDropSource.ShortcutAlias
            : HotbarDropSource.FreshItem;
        HotbarSlot dragged = cargo.Shortcut
            ?? new HotbarSlot(cargo.SourceSlot, cargo.ObjId, 0u);
        var plan = HotbarDropPlanner.PlanDiscard(
            _store.Snapshot(), src, cargo.SourceSlot, markChamber.SocketIdx, dragged);

        ImposeShortcutPlan(plan);
    }

    public bool EmployShortcut(int socket, bool use)
    {
        if ((uint)socket >= _sockets.Length || _sockets[socket]?.Cell is not { } chamber)
            return false;

        uint gearIdent = chamber.GearIdent;
        if (_gearDealing?.IsMarkMannerEngaged == true)
        {
            if (gearIdent is not 0)
                _gearDealing.OfferPrimaryPress(gearIdent);
            else
                _gearDealing.AbortObjectiveManner();
            return true;
        }

        if (gearIdent is 0)
            return false;

        if (use)
        {
            if (_gearDealing is not null)
                _gearDealing.EngageGear(gearIdent);
            else
                _useGear(gearIdent);
        }
        else
        {
            _pickGear?.Invoke(gearIdent);
        }

        return true;
    }

    public bool BuildShortcutToGear(uint gearIdent)
    {
        if (gearIdent is 0 || _repo.Get(gearIdent) is not { } gear)
            return false;

        PublicWeenieBits flagSet = (PublicWeenieBits)(gear.PublicWeenieBitfield ?? 0u);
        bool isAvatar = gearIdent == _gearDealing?.PlayerOid
            || (flagSet & PublicWeenieBits.Player) != 0;
        if ((flagSet & PublicWeenieBits.Stuck) != 0 && !isAvatar)
            return false;
        if ((gear.Type & GearKind.Creature) != 0 && !isAvatar)
            return false;
        if (_gearDealing?.IsPossessedByAvatar(gearIdent) != true)
            return false;

        for (int socket = 0; socket < _sockets.Length; ++socket)
            if (_store.Get(socket) == gearIdent)
                return false;

        int vacant = -1;
        for (int socket = 0; socket < _sockets.Length; ++socket)
        {
            if (_store.IsEmpty(socket))
            {
                vacant = socket;
                break;
            }
        }
        if (vacant < 0)
            return false;

        HotbarSlot listing = new HotbarSlot(vacant, gearIdent, 0u);
        _transmitAppendShortcut?.Invoke(listing);
        _store.Set(listing);
        return true;
    }

    public void ReplaceFullyMergedShortcut(uint formerObjectIdent, uint newObjectIdent)
    {
        var plan = HotbarDropPlanner.PlanWholePileCombine(
            _store.Snapshot(), formerObjectIdent, newObjectIdent);
        ImposeShortcutPlan(plan);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;

        foreach (var (_, btn) in _boardBtns)
            btn.OnClick = null;
        foreach (var indicator in _fightingIndicators)
            if (indicator is WidgetBtn btn)
                btn.OnClick = null;
        _useBtn?.OnClick = null;
        _examineBtn?.OnClick = null;
        if (_satchelBtn is not null)
        {
            _satchelBtn.OnGearPullOver = null;
            _satchelBtn.OnGearDiscard = null;
        }

        _fightingPhase?.CombatModeChanged -= AssignFightingManner;
        _pick?.Changed -= OnPickAltered;
        foreach (WidgetGearRoster? roster in _sockets)
            if (roster is not null)
            {
                roster.PrimaryGearPressed = null;
                roster.ExamineGearAsked = null;
            }
        _repo.ObjectAdded -= OnRepositoryObjectAltered;
        _repo.ObjectUpdated -= OnRepositoryObjectAltered;
        _repo.ObjectRemoved -= OnRepositoryObjectAltered;
        _repo.ObjectMoved -= OnRepositoryObjectMoved;
        _repo.Cleared -= OnRepositoryCleared;
        _store.Changed -= Populate;
    }
    private void OnRepositoryObjectAltered(ClientThing objRef)
    {
        if (IsAmmoRelated(objRef))
            RenewAmmo();
        if (IsShortcutOid(objRef.ObjectId))
            Populate();
        if (objRef.ObjectId == _chosenObjectIdent())
            RenewUseBtn();
    }

    private void OnRepositoryObjectMoved(ObjectRelocation relocate)
    {
        uint avatar = _avatarOid?.Invoke() ?? 0u;
        if (relocate.ItemId == _ammoObjectIdent
            || (avatar is not 0
                && (relocate.Previous.ContainerId == avatar
                    || relocate.Current.ContainerId == avatar
                    || relocate.Previous.WielderId == avatar
                    || relocate.Current.WielderId == avatar)))
            RenewAmmo();
        if (IsShortcutOid(relocate.ItemId))
            Populate();
    }

    private void OnRepositoryCleared()
    {
        RenewAmmo();
        Populate();
        RenewUseBtn();
    }

    private void OnPickAltered(PickShift _)
        => RenewUseBtn();

    private void RenewUseBtn()
    {
        if (_useBtn is null)
            return;

        _useBtn.Enabled =
            _gearDealing?.IsToolbarUseTurnedOn(_chosenObjectIdent()) == true;
    }

    private void RenewAmmo()
    {
        uint avatar = _avatarOid?.Invoke() ?? 0u;
        IReadOnlyList<ClientThing> stances = avatar is not 0
            ? _repo.FetchEquippedBy(avatar)
            : Array.Empty<ClientThing>();

        var outcome = HotbarAmmoRules.Resolve(stances);
        _ammoObjectIdent = outcome.ObjectId;
        _ammoIndicator?.Label = outcome.IsVisible
                ? outcome.DisplayCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
    }

    private bool IsAmmoRelated(ClientThing objRef)
    {
        uint avatar = _avatarOid?.Invoke() ?? 0u;
        return objRef.ObjectId == _ammoObjectIdent
            || (avatar is not 0 && objRef.ObjectId == avatar)
            || (avatar is not 0
                && (objRef.WielderIdent == avatar || objRef.VesselTag == avatar)
                && (objRef.CurrentlyEquippedLocale
                    & (WieldBitmask.MissileWeapon | WieldBitmask.MissileAmmo)) != 0);
    }

    private bool IsShortcutOid(uint oid)
    {
        for (int s = 0; s < HotbarStore.SlotTally; ++s)
            if (_store.Get(s) == oid) return true;
        return false;
    }

    private void RestampShortcutNumbers()
    {
        for (int idx = 0; idx < _sockets.Length; ++idx)
        {
            WidgetGearSlot? chamber = _sockets[idx]?.Cell;
            if (chamber is null) continue;
            chamber.RegularDigits = _regularDigits;
            chamber.GhostedDigits = _ghostedDigits;
            chamber.VacantDigits = _vacantDigits;
            if (idx < 9)
                chamber.AssignShortcutCount(idx, _shortcutsGhosted); // top row: slot labels 1-9 always shown
            else
                chamber.WipeShortcutCount();          // bottom row: no slot labels
        }
    }

    private void WirePress(WidgetGearRoster roster)
    {
        roster.Cell.Clicked = () =>
        {
            if (roster.Cell.GearIdent is not 0)
            {
                if (_gearDealing is not null)
                    _gearDealing.EngageGear(roster.Cell.GearIdent);
                else
                    _useGear(roster.Cell.GearIdent);
            }
        };
    }

    private bool PressGear(uint gearIdent)
    {
        if (_gearDealing?.OfferPrimaryPress(gearIdent)
            is not null and not GearPrimaryClickResult.NotActive)
            return true;

        if (_pickGear is not null)
            _pickGear(gearIdent);
        else
            _pick?.Select(gearIdent, PickChangeSource.Toolbar);
        return false;
    }

    private void StudyGear(uint gearIdent)
    {
        if (_pickGear is not null)
            _pickGear(gearIdent);
        else
            _pick?.Select(gearIdent, PickChangeSource.Toolbar);
        _gearDealing?.StudyChosenOrJoinManner(gearIdent);
    }

    private static GearDragAcceptance SatchelBtnPullOver(GearDragPayload cargo)
    {
        return cargo.ObjId is not 0 && cargo.SourceKind != GearDragSource.ShortcutBar
                ? GearDragAcceptance.Accept
                : GearDragAcceptance.None;
    }

    private void ProcessSatchelBtnDiscard(GearDragPayload cargo)
    {
        if (cargo.ObjId is 0 || cargo.SourceKind == GearDragSource.ShortcutBar)
            return;
        uint avatar = _avatarOid?.Invoke() ?? 0u;
        if (avatar is 0 || _repo.Get(cargo.ObjId) is null)
            return;
        if (_gearDealing is not null)
        {
            bool relay()
            {
                if (_transmitPutGearInVessel is null)
                    return false;
                _transmitPutGearInVessel(cargo.ObjId, avatar, 0);
                return true;
            }
            if (_gearDealing.IsPossessedByAvatar(cargo.ObjId))
                _gearDealing.TryRelayQueuedBackpackStance(
                    cargo.ObjId,
                    avatar,
                    0,
                    PackRequestKind.PutInContainer,
relay);
            else
                _gearDealing.TryRelaySatchelReq(
                    PackRequestKind.Pickup,
                    cargo.ObjId,
relay);
            return;
        }
        _transmitPutGearInVessel?.Invoke(cargo.ObjId, avatar, 0);
    }

    private void ImposeShortcutPlan(IReadOnlyList<HotbarEdit> plan)
    {
        foreach (var alteration in plan)
        {
            if (alteration.Kind == HotbarEditKind.Remove)
            {
                _transmitDropShortcut?.Invoke((uint)alteration.Slot);
                _store.Remove(alteration.Slot);
            }
            else if (alteration.Entry is { } listing)
            {
                _transmitAppendShortcut?.Invoke(listing);
                _store.Set(listing);
            }
        }
    }
}
