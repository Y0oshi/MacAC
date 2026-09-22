using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class StashDriver
{

    public static StashDriver Bind(
        ImportedArrangement arrangement,
        ClientThingChart objects,
        Func<uint> avatarOid,
        Func<GearKind, uint, uint, uint, uint, uint> glyphIdents,
        Func<int?> strength,
        PickPhase pick,
        WidgetDatFont? datTypeface,
        Func<string>? holderLabel = null,
        uint insidesVacantSprite = 0u,
        uint flankBagVacantSprite = 0u,
        uint primaryBundleVacantSprite = 0u,
        Action<uint>? transmitUse = null,
        Action<uint, uint, int>? transmitPutGearInVessel = null,
        Action<uint, uint, uint, uint>? transmitStackableDivideToVessel = null,
        Action<uint, uint, uint>? transmitStackableCombine = null,
        Action<uint, uint>? alertCombineAttempt = null,
        GearDealingDriver? gearDealing = null,
        Action? onShut = null,
        StackSplitGauge? pileDivideQty = null,
        Func<GearKind, uint, uint, uint, uint, uint>? pullGlyphIdents = null,
        Grimoire? burdenGrimoire = null)
    {
        return new(arrangement, objects, avatarOid, glyphIdents, pullGlyphIdents, strength, pick,
                                       holderLabel, datTypeface,
                                       insidesVacantSprite, flankBagVacantSprite, primaryBundleVacantSprite,
                                       transmitUse, transmitPutGearInVessel,
                                       transmitStackableDivideToVessel, transmitStackableCombine,
                                       alertCombineAttempt, gearDealing,
                                       onShut, pileDivideQty, burdenGrimoire);
    }

    public void OnPullLift(WidgetGearRoster srcRoster, WidgetGearSlot srcChamber, GearDragPayload cargo)
    {
        if (cargo.ObjId is not 0 && _pick.ChosenObjectTag != cargo.ObjId)
            _pick.Select(cargo.ObjId, PickChangeSource.Inventory);
    }

    public GearDragAcceptance OnPullOver(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
    {
        if (cargo.SourceKind == GearDragSource.ShortcutBar)
            return GearDragAcceptance.None;
        var legality = EvaluateDiscard(
            markRoster, markChamber, cargo.ObjId, out _, out uint dest);
        if (IsCapRejection(legality)
            && LocateFallthroughVessel(cargo.ObjId, dest, out _) is not 0u)

            legality = PackPlacementRefusal.None;
        return legality == PackPlacementRefusal.None
            ? GearDragAcceptance.Accept
            : GearDragAcceptance.Reject;
    }

    public void Populate()
    {
        using var insidesArrangement = _insidesGrid?.DeferArrangement();
        using var vesselsArrangement = _vesselRoster?.DeferArrangement();

        uint p = _avatarOid();
        uint open = NetOpen();

        List<uint> shownBags = new List<uint>();
        foreach (var oid in _objects.FetchInsides(p))
        {
            ClientThing? gear = _objects.Get(oid);
            if (gear is null || gear.CurrentlyEquippedLocale != WieldBitmask.None) continue;
            bool isBag = IsBag(gear);
            if (isBag) shownBags.Add(oid);
        }

        var queued = _queuedRosterStance;
        if (queued is { } bagProj
            && bagProj.ContainerId == p
            && _objects.Get(bagProj.ItemId) is { } queuedBag
            && IsBag(queuedBag))
        {
            shownBags.Remove(bagProj.ItemId);
            int ordinal = Math.Clamp(bagProj.Placement, 0, shownBags.Count);
            shownBags.Insert(ordinal, bagProj.ItemId);
        }

        List<uint> shownInsides = new List<uint>();
        foreach (var oid in _objects.FetchInsides(open))
        {
            ClientThing? gear = _objects.Get(oid);
            if (gear is null || gear.CurrentlyEquippedLocale != WieldBitmask.None) continue;
            bool isBag = IsBag(gear);
            if (!isBag) shownInsides.Add(oid);
        }

        if (queued is { } proj
            && proj.ContainerId == open
            && _objects.Get(proj.ItemId) is { } queuedGear
            && !IsBag(queuedGear))
        {
            shownInsides.Remove(proj.ItemId);
            int ordinal = Math.Clamp(proj.Placement, 0, shownInsides.Count);
            shownInsides.Insert(ordinal, proj.ItemId);
        }

        if (TryRenewChambersInPlace(shownBags, shownInsides, queued, p, open))
        {
            ImposeIndicators();
            RenewBurden();
            return;
        }

        _vesselRoster?.Flush();
        _insidesGrid?.Flush();

        foreach (uint oid in shownBags)
        {
            AppendChamber(
                _vesselRoster,
                oid,
                isVessel: true,
                IsWaiting(oid, p, queued));
        }

        foreach (uint oid in shownInsides)
        {
            AppendChamber(
                _insidesGrid,
                oid,
                isVessel: false,
                IsWaiting(oid, open, queued));
        }

        if (_insidesGrid is not null)
        {
            int cap = _objects.Get(open)?.ItemsCapacity ?? 0;
            int sockets = cap > 0 ? cap : (open == p ? PrimaryBundleSockets : FlankBundleSockets);
            while (_insidesGrid.FetchCountWIDGETGearList() < sockets) AppendVacantChamber(_insidesGrid);
        }

        if (_vesselRoster is not null)
        {
            int capacity = _objects.Get(p)?.ContainersCapacity ?? 0;
            int bags = _vesselRoster.FetchCountWIDGETGearList();
            int sockets = capacity > 0 ? capacity : FlankBagSockets;
            sockets = Math.Max(sockets, bags);
            sockets = Math.Min(sockets, FlankBagSockets);
            while (_vesselRoster.FetchCountWIDGETGearList() < sockets) AppendVacantChamber(_vesselRoster);
        }

        if (_topVessel is not null)
        {
            const uint AvatarBundleBaseGlyph = 0x0600127Eu;
            _topVessel.Flush();
            WidgetGearSlot primary = new WidgetGearSlot
            {
                SpriteResolve = _topVessel.SpriteResolve,
                HintPhraseLocate = g => _objects.Get(g)?.FetchHintReadoutLabel(),
            };
            primary.AssignGear(
                p,
                _glyphIdents(GearKind.Container, AvatarBundleBaseGlyph, 0u, 0u, 0u),
                pullGlyphTexture: _pullGlyphIdents?.Invoke(
                    GearKind.Container, AvatarBundleBaseGlyph, 0u, 0u, 0u) ?? 0u);
            primary.PullAdmitSprite = 0x060011F7u; primary.PullRejectSprite = 0x060011F8u;
            primary.AssignWaitingPhase(IsWaitingSrc(p));
            primary.Clicked = () => OpenVessel(p);
            AssignCapBar(primary, p);               // main-pack fullness (items / ItemsCapacity)
            _topVessel.AddItem(primary);
        }

        ImposeIndicators();
        RenewBurden();
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _objects.ObjectAdded -= OnObjectAltered;
        _objects.ObjectMoved -= OnObjectMoved;
        _objects.MoveRequestFailed -= OnRelocateReqFailed;
        _objects.ContainerContentsReplaced -= OnVesselInsidesReplaced;
        _objects.ObjectRemoved -= OnObjectRemoved;
        _objects.ObjectUpdated -= OnObjectAltered;
        _objects.Cleared -= OnObjectsCleared;
        _pick.Changed -= OnPickAltered;
        _burdenGrimoire?.EnchantmentsChanged -= RenewBurden;
        if (_insidesGrid is not null)
        {
            _insidesGrid.PrimaryGearPressed = null;
            _insidesGrid.ExamineGearAsked = null;
        }
        if (_vesselRoster is not null)
        {
            _vesselRoster.PrimaryGearPressed = null;
            _vesselRoster.ExamineGearAsked = null;
        }
        if (_topVessel is not null)
        {
            _topVessel.PrimaryGearPressed = null;
            _topVessel.ExamineGearAsked = null;
        }
        if (_gearDealing is not null)
        {
            _gearDealing.MergeAttempted -= OnCombineAttempted;
            _gearDealing.StateChanged -= OnDealingPhaseAltered;
            _gearDealing.PendingBackpackPlacementRequested -= OnQueuedBackpackStanceAsked;
            _gearDealing.PendingBackpackPlacementCancelled -= OnQueuedBackpackStanceCancelled;
            _gearDealing.PendingBackpackPlacementResolved -= OnQueuedBackpackStanceSettled;
        }
    }
    internal static void ConfigureRescaleArrangement(ImportedArrangement arrangement)
    {
        static void Assign(ImportedArrangement imported, uint ident, MooringRims moorings)
        {
            if (imported.SeekElem(ident) is not { } elem) return;
            elem.Moorings = moorings;
            elem.GrabLatestMooringBaseline();
        }

        MooringRims stretch = MooringRims.Left | MooringRims.Top | MooringRims.Bottom;
        Assign(arrangement, BackdropIdent, stretch);
        Assign(arrangement, InsidesPaneIdent, stretch);
        Assign(arrangement, InsidesGridIdent, stretch);
        Assign(arrangement, InsidesScrollerTag, stretch);
        Assign(arrangement, PaperdollPaneIdent, MooringRims.Left | MooringRims.Top);
        Assign(arrangement, BackpackPaneIdent, MooringRims.Left | MooringRims.Top);
    }

    private void ConfigureDiscardFeedback(WidgetGearRoster roster, WidgetGearSlot chamber)
    {
        chamber.PullAdmitSprite = ReferenceEquals(roster, _insidesGrid)
            ? 0x060011F9u
            : 0x060011F7u;
        chamber.PullRejectSprite = 0x060011F8u;
    }

    private void OnObjectAltered(ClientThing o)
    {
        if (Concerns(o) || _queuedRosterStance?.ItemId == o.ObjectId)
            Populate();
    }

    private void OnObjectRemoved(ClientThing o)
    {
        bool backupSettled = _gearDealing is null
            && _queuedRosterStance?.ItemId == o.ObjectId;
        if (backupSettled)
            _queuedRosterStance = null;
        if (_pick.ChosenObjectTag == o.ObjectId)
        {
            _pick.Clear(
                PickChangeSource.System,
                PickChangeReason.SelectedObjectRemoved);
        }
        if (backupSettled || Concerns(o)) Populate();
    }

    private void OnObjectMoved(ObjectRelocation relocate)
    {
        bool backupSettled = _gearDealing is null
            && _queuedRosterStance?.ItemId == relocate.ItemId;
        if (backupSettled)
            _queuedRosterStance = null;
        uint avatar = _avatarOid();
        if (backupSettled
            || (relocate.Item is { } gear && Concerns(gear))
            || relocate.Previous.ContainerId == avatar
            || relocate.Current.ContainerId == avatar
            || relocate.Previous.WielderId == avatar
            || relocate.Current.WielderId == avatar)
            Populate();
    }

    private void OnVesselInsidesReplaced(uint vesselIdent)
    {
        if (vesselIdent == NetOpen() || vesselIdent == _avatarOid())
            Populate();
    }

    // A transaction only changes which cells are waiting on it
    private void OnDealingPhaseAltered() => ImposeTransactionPhases();

    private void OnQueuedBackpackStanceAsked(QueuedBackpackStance queued)
    {
        if (_queuedRosterStance is not null
            || queued.ItemId is 0u
            || queued.ContainerId is 0u
            || _objects.Get(queued.ItemId) is null)

            return;

        _queuedRosterStance = new QueuedRosterStance(
            queued.Token,
            queued.ItemId,
            queued.ContainerId,
            queued.Placement);
        Populate();
    }

    private void OnQueuedBackpackStanceCancelled(QueuedBackpackStance queued)
    {
        if (_queuedRosterStance is not { } proj
            || proj.Token != queued.Token)
            return;
        _queuedRosterStance = null;
        Populate();
    }

    private void OnQueuedBackpackStanceSettled(QueuedBackpackStance queued)
    {
        if (_queuedRosterStance is not { } proj
            || proj.Token != queued.Token)
            return;
        _queuedRosterStance = null;
        Populate();
    }

    private void OnPickAltered(PickShift _) => ImposeIndicators();

    private void OnRelocateReqFailed(RelocationRefusal miss)
    {
        if (_gearDealing is not null
            || _queuedRosterStance?.ItemId != miss.ItemId)
            return;
        _queuedRosterStance = null;
        Populate();
    }

    private void OnObjectsCleared()
    {
        _queuedRosterStance = null;
        Populate();
    }

    private void OnCombineAttempted(uint srcIdent, uint markIdent)
    {
        _alertCombineAttempt?.Invoke(srcIdent, markIdent);
        _pick.Select(markIdent, PickChangeSource.Inventory);
    }

    private void ImposeTransactionPhases()
    {
        var queued = _queuedRosterStance;
        ImposeWaitingPhases(_vesselRoster, _avatarOid(), queued);
        ImposeWaitingPhases(_insidesGrid, NetOpen(), queued);
        if (_topVessel?.GetItem(0) is { GearIdent: not 0u } primary)
            primary.AssignWaitingPhase(IsWaitingSrc(primary.GearIdent));
        ImposeIndicators();
    }

    private void ImposeWaitingPhases(
        WidgetGearRoster? roster,
        uint vesselIdent,
        QueuedRosterStance? queued)
    {
        if (roster is null) return;
        for (int idx = 0; idx < roster.FetchCountWIDGETGearList(); ++idx)
        {
            if (roster.GetItem(idx) is not { GearIdent: not 0u } chamber) continue;
            chamber.AssignWaitingPhase(
                IsWaitingSrc(chamber.GearIdent)
                || (queued is { } proj
                    && proj.ContainerId == vesselIdent
                    && proj.ItemId == chamber.GearIdent));
        }
    }

    private void ImposeIndicators()
    {
        AssignIndicators(_insidesGrid);
        AssignIndicators(_vesselRoster);
        AssignIndicators(_topVessel);
    }

    // True if the object is in (or wielded by) the player - i.e. a rebuild is warranted.
    private bool Concerns(ClientThing o)
    {
        uint p = _avatarOid();
        return o.ObjectId == p                          // the player object IS the burden source
            || o.VesselTag == p || o.WielderIdent == p
            || o.VesselTag == NetOpen()
            || (o.VesselTag is not 0 && _objects.Get(o.VesselTag)?.VesselTag == p);  // 2-deep
    }

    private int TallyBags(uint vesselIdent)
    {
        int tally = 0;
        foreach (uint oid in _objects.FetchInsides(vesselIdent))
        {
            if (_objects.Get(oid) is { } gear && IsBag(gear))
                ++tally;
        }
        return tally;
    }

    private int TallyLooseInsides(uint vesselIdent)
    {
        int tally = 0;
        foreach (uint oid in _objects.FetchInsides(vesselIdent))
        {
            if (_objects.Get(oid) is { } gear
                && gear.CurrentlyEquippedLocale == WieldBitmask.None
                && !IsBag(gear))

                ++tally;
        }
        return tally;
    }

    public uint LatestOpenVesselIdent => NetOpen();

    private uint NetOpen() => _openVessel is not 0 ? _openVessel : _avatarOid();

    private static bool FitsChambers(WidgetGearRoster? roster, IReadOnlyList<uint> oids, int socketMark)
    {
        if (roster is null) return oids.Count is 0;
        if (roster.FetchCountWIDGETGearList() != socketMark) return false;
        for (int idx = 0; idx < socketMark; ++idx)
        {
            uint anticipated = idx < oids.Count ? oids[idx] : 0u;
            if (roster.GetItem(idx) is not { } chamber || chamber.GearIdent != anticipated) return false;
        }
        return true;
    }

    private int VesselSocketMark(uint avatar, int bagTally)
    {
        if (_vesselRoster is null) return 0;
        int cap = _objects.Get(avatar)?.ContainersCapacity ?? 0;
        int sockets = cap > 0 ? cap : FlankBagSockets;
        sockets = Math.Max(sockets, bagTally);
        return Math.Min(sockets, FlankBagSockets);
    }

    private int InsidesSocketMark(uint open, uint avatar)
    {
        if (_insidesGrid is null) return 0;
        int cap = _objects.Get(open)?.ItemsCapacity ?? 0;
        return cap > 0 ? cap : (open == avatar ? PrimaryBundleSockets : FlankBundleSockets);
    }

    private bool PressGear(uint oid)
    {
        if (_gearDealing?.OfferPrimaryPress(oid)
            is not null and not GearPrimaryClickResult.NotActive)
            return true;
        if (_objects.Get(oid) is { } gear && IsBag(gear))
            OpenVessel(oid);
        else
            PickGear(oid);
        return false;
    }

    private bool PressSelfGear(uint oid)
    {
        if (_gearDealing?.OfferSelfPrimaryPress()
            is not null and not GearPrimaryClickResult.NotActive)
            return true;
        OpenVessel(oid);
        return false;
    }

    private bool RelaySatchelReq(
        PackRequestKind sort,
        uint gearIdent,
        Func<bool> relay)
    {
        return _gearDealing?.TryRelaySatchelReq(sort, gearIdent, relay)
                ?? relay();
    }

    private PackPlacementRefusal EvaluateDiscard(
        WidgetGearRoster markRoster,
        WidgetGearSlot markChamber,
        uint gearIdent,
        out bool srcIsBag,
        out uint destIdent)
    {
        srcIsBag = _objects.Get(gearIdent) is { } src && IsBag(src);
        destIdent = 0u;
        if (gearIdent is 0u)
            return PackPlacementRefusal.InvalidItem;

        if (ReferenceEquals(markRoster, _insidesGrid))
        {
            destIdent = NetOpen();
            if (srcIsBag)
                return PackPlacementRefusal.ContainerCapacityFull;
        }
        else if (ReferenceEquals(markRoster, _vesselRoster)
            || ReferenceEquals(markRoster, _topVessel))
        {
            destIdent = srcIsBag ? _avatarOid() : markChamber.GearIdent;
            if (!srcIsBag && (markChamber.GearIdent is 0u || markChamber.GearIdent == gearIdent))
                return PackPlacementRefusal.InvalidDestination;
        }
        else
        {
            return PackPlacementRefusal.InvalidDestination;
        }

        return PackPlacementPolicy.Evaluate(
            _objects,
            gearIdent,
            destIdent,
            _avatarOid());
    }

    private void StudyGear(uint oid)
    {
        PickGear(oid);
        _gearDealing?.StudyChosenOrJoinManner(oid);
    }

    private string HolderLabel()
    {
        string? label = _holderLabel?.Invoke();
        return string.IsNullOrWhiteSpace(label) ? "Player" : label;
    }

    private static bool IsBag(ClientThing gear)
    {
        return gear.VesselKindHint is not 0u
        || gear.Type.HasFlag(GearKind.Container)
        || gear.ItemsCapacity > 0;
    }

    private bool IsWaiting(uint oid, uint vesselIdent, QueuedRosterStance? queued)
    {
        return IsWaitingSrc(oid)
                || (queued is { } proj
                    && proj.ContainerId == vesselIdent
                    && proj.ItemId == oid);
    }

    private static bool IsCapRejection(PackPlacementRefusal rejection)
    {
        return rejection is PackPlacementRefusal.ItemCapacityFull
            or PackPlacementRefusal.ContainerCapacityFull;
    }

    private bool IsVesselWhole(uint vessel)
    {
        int cap = _objects.Get(vessel)?.ItemsCapacity ?? 0;
        return cap <= 0 ? false : TallyLooseInsides(vessel) >= cap;
    }

    private bool IsWaitingSrc(uint gearOid)
        => _gearDealing?.IsQueuedSatchelSrc(gearOid) == true;

    // False when anything structural moved, which needs the rebuild
    private bool TryRenewChambersInPlace(
        IReadOnlyList<uint> shownBags,
        IReadOnlyList<uint> shownInsides,
        QueuedRosterStance? queued,
        uint avatar,
        uint open)
    {
        if (!FitsChambers(_vesselRoster, shownBags, VesselSocketMark(avatar, shownBags.Count))
            || !FitsChambers(_insidesGrid, shownInsides, InsidesSocketMark(open, avatar)))

            return false;

        RenewChambers(_vesselRoster, shownBags, isVessel: true, avatar, queued);
        RenewChambers(_insidesGrid, shownInsides, isVessel: false, open, queued);
        RenewTopVessel(avatar);
        return true;
    }

    private bool TryCombinePiles(uint srcIdent, uint markIdent)
    {
        if (_transmitStackableCombine is null
            || _objects.Get(srcIdent) is not { } src
            || _objects.Get(markIdent) is not { } mark)
            return false;

        int askedQuantity = _pick.ChosenObjectTag == srcIdent
            && _pileDivideQty is not null
                ? (int)Math.Min(_pileDivideQty.Value, int.MaxValue)
                : Math.Max(1, src.StackSize);
        var plan = StackMergeRouter.Plan(
            new StackMergeCandidate(
                src.ObjectId,
                src.WeenieClassIdent,
                src.StackSize,
                src.PileDimsUpper,
                src.BarterPhase),
            new StackMergeCandidate(
                mark.ObjectId,
                mark.WeenieClassIdent,
                mark.StackSize,
                mark.PileDimsUpper,
                mark.BarterPhase),
            _gearDealing?.CanCraftSatchelReq ?? true,
            askedQuantity);
        return plan is not { } combine
            ? false
            : RelaySatchelReq(
            PackRequestKind.Merge,
            combine.SourceObjectId,
            () =>
            {
                _transmitStackableCombine(
                    combine.SourceObjectId,
                    combine.TargetObjectId,
                    combine.Amount);
                _alertCombineAttempt?.Invoke(combine.SourceObjectId, combine.TargetObjectId);
                _pick.Select(combine.TargetObjectId, PickChangeSource.Inventory);
                return true;
            });
    }

    private void RenewChambers(
        WidgetGearRoster? roster,
        IReadOnlyList<uint> oids,
        bool isVessel,
        uint vesselIdent,
        QueuedRosterStance? queued)
    {
        if (roster is null) return;
        for (int idx = 0; idx < oids.Count; ++idx)
        {
            if (roster.GetItem(idx) is not { } chamber) continue;
            uint oid = oids[idx];
            var gear = _objects.Get(oid);
            uint bmp = gear is null
                ? 0u
                : _glyphIdents(gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
            uint pullBmp = gear is null
                ? 0u
                : _pullGlyphIdents?.Invoke(
                    gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects) ?? 0u;
            chamber.AssignGear(oid, bmp, pullGlyphTexture: pullBmp);
            AssignStructureBar(chamber, gear);
            chamber.AssignWaitingPhase(IsWaiting(oid, vesselIdent, queued));
            if (isVessel)
                AssignCapBar(chamber, oid);
        }
    }

    private void RenewTopVessel(uint avatar)
    {
        if (_topVessel?.GetItem(0) is not { } primary) return;
        primary.AssignWaitingPhase(IsWaitingSrc(avatar));
        AssignCapBar(primary, avatar);
    }

    private void RenewBurden()
    {
        uint p = _avatarOid();
        int text = _strength() ?? 10;                       // InqAttribute default 0xa
        int aug = _objects.Get(p)?.Properties.FetchInt(EncumbranceAugProp) ?? 0;
        int cap = BurdenRules.EncumbranceCapacity(text, aug);

        int? wire = _objects.Get(p)?.Properties.Ints.TryGetValue(EncumbranceValueProp, out var ev) == true
            ? ev : (int?)null;
        int burden = wire ?? _objects.TotalCarriedBurden(p);

        float pull = BurdenRules.PullRatio(cap, burden);
        _burdenPopulate = BurdenRules.PullToPopulate(pull);
        _burdenPct = BurdenRules.PullToPct(pull);
    }

    private void AppendChamber(WidgetGearRoster? roster, uint oid, bool isVessel, bool waiting = false)
    {
        if (roster is null) return;
        ClientThing? gear = _objects.Get(oid);
        uint bmp = gear is null ? 0u
            : _glyphIdents(gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
        uint pullBmp = gear is null ? 0u
            : _pullGlyphIdents?.Invoke(
                gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects) ?? 0u;
        WidgetGearSlot chamber = new WidgetGearSlot
        {
            SpriteResolve = roster.SpriteResolve,
            HintPhraseLocate = g => _objects.Get(g)?.FetchHintReadoutLabel(),
        };
        chamber.AssignGear(oid, bmp, pullGlyphTexture: pullBmp);
        AssignStructureBar(chamber, gear);
        chamber.AssignWaitingPhase(waiting);
        chamber.SocketIdx = roster.FetchCountWIDGETGearList();                 // index it will occupy (== its slot in a packed list)
        ConfigureDiscardFeedback(roster, chamber);
        if (isVessel)
        {
            chamber.Clicked = () => OpenVessel(oid);
            AssignCapBar(chamber, oid);
        }
        else
        {
            chamber.DoubleClicked = () => _gearDealing?.EngageGear(oid);
        }
        roster.AddItem(chamber);
    }

    private void AppendVacantChamber(WidgetGearRoster roster)
    {
        WidgetGearSlot chamber = new WidgetGearSlot { SpriteResolve = roster.SpriteResolve, SocketIdx = roster.FetchCountWIDGETGearList() };
        ConfigureDiscardFeedback(roster, chamber);
        roster.AddItem(chamber);
    }

    private void AssignCapBar(WidgetGearSlot chamber, uint vesselOid)
    {
        int cap = _objects.Get(vesselOid)?.ItemsCapacity ?? 0;
        if (cap <= 0) { chamber.CapPopulate = -1f; return; }
        int num = TallyLooseInsides(vesselOid);
        chamber.CapPopulate = Math.Clamp(num / (float)cap, 0f, 1f);
    }

    private static void AssignStructureBar(WidgetGearSlot chamber, ClientThing? gear) => chamber.AssignStructure(gear?.Structure ?? 0, gear?.MaxStructure ?? 0);

    private void AssignIndicators(WidgetGearRoster? roster)
    {
        if (roster is null) return;
        uint open = NetOpen();
        for (int idx = 0; idx < roster.FetchCountWIDGETGearList(); ++idx)
        {
            WidgetGearSlot? chamber = roster.GetItem(idx);
            if (chamber is null) continue;
            bool queuedMarkSrc = _gearDealing?.IsQueuedSrc(chamber.GearIdent) == true
                || IsWaitingSrc(chamber.GearIdent);
            chamber.Selected = chamber.GearIdent is not 0
                && chamber.GearIdent == _pick.ChosenObjectTag
                && !queuedMarkSrc;
            chamber.IsOpenVessel = chamber.GearIdent is not 0 && chamber.GearIdent == open;
        }
    }

    private uint LocateFallthroughVessel(
        uint gearIdent,
        uint namedVessel,
        out PackPlacementRefusal refusal)
    {
        uint trunk = _avatarOid();
        return PackSlotSearch.SelectVessel(
            _objects, gearIdent, trunk, namedVessel, trunk, out refusal);
    }

    private void PickGear(uint oid)
    {
        if (oid is 0) return;
        _pick.Select(oid, PickChangeSource.Inventory);
    }

    private void OpenVessel(uint oid)
    {
        if (oid is 0) return;
        _pick.Select(oid, PickChangeSource.Inventory);
        uint open = NetOpen();
        if (oid == open) { ImposeIndicators(); return; }   // already open - just move the square

        uint p = _avatarOid();
        _openVessel = oid;
        if (oid != p)
        {
            if (_gearDealing is not null)
                _gearDealing.EngageGear(oid);
            else
                _transmitUse?.Invoke(oid);             // open the side bag (ViewContents will land)
        }
        Populate();
    }

    private string OpenVesselLabel()
    {
        uint open = NetOpen();
        return open == _avatarOid() ? "Backpack" : (_objects.Get(open)?.Name ?? "Backpack");
    }

    private void FastenLegend(WidgetElem? hub, Func<string> phrase, WidgetDatFont? datTypeface)
    {
        if (hub is null) return;

        if (hub is WidgetPhrase t)
        {
            t.Centered = true;
            t.OneLine = true;
            t.DatFont = datTypeface;
            t.ClickThrough = true;
            t.AcceptsFocus = false;
            t.IsEditControl = false;
            t.CapturesPointerDrag = false;
            t.StrokesSupplier = () =>
            {
                string s = phrase();
                return string.IsNullOrEmpty(s)
                    ? []
                    : new[] { new WidgetPhrase.Line(s, LegendTint) };
            };
            return;
        }

        WidgetPhrase caption = new WidgetPhrase
        {
            Left = 0f,
            Top = 0f,
            Width = hub.Width,
            Height = hub.Height,
            Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Right,
            Centered = true,
            OneLine = true,
            DatFont = datTypeface,
            ClickThrough = true,
            AcceptsFocus = false,
            IsEditControl = false,
            CapturesPointerDrag = false,
            StrokesSupplier = () =>
            {
                string s = phrase();
                return string.IsNullOrEmpty(s)
                    ? []
                    : new[] { new WidgetPhrase.Line(s, LegendTint) };
            },
        };
        hub.AddChild(caption);
    }
}
