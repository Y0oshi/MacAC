using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ExternalContainerDriver
{
    public static ExternalContainerDriver Bind(
        ImportedArrangement arrangement,
        OpenContainerState phase,
        ClientThingChart objects,
        PickPhase pick,
        GearDealingDriver gearDealing,
        StackSplitGauge pileDivideQty,
        Func<GearKind, uint, uint, uint, uint, uint> locateGlyph,
        Func<GearKind, uint, uint, uint, uint, uint> locatePullGlyph,
        Action<uint> transmitUse,
        Action<uint, uint, int> transmitPutGearInVessel,
        Action<uint, uint, uint, uint> transmitDivideToVessel,
        Func<uint, bool> isWithinUseSpan,
        CanonWindowHandle pane,
        uint insidesVacantSprite = 0u,
        uint vesselVacantSprite = 0u)
    {
        return new(
                arrangement,
                phase,
                objects,
                pick,
                gearDealing,
                pileDivideQty,
                locateGlyph,
                locatePullGlyph,
                transmitUse,
                transmitPutGearInVessel,
                transmitDivideToVessel,
                isWithinUseSpan,
                pane,
                insidesVacantSprite,
                vesselVacantSprite);
    }

    public void Tick()
    {
        uint trunk = _phase.LatestVesselIdent;
        if (trunk is not 0u && _window.IsVisible && !_shutAsked && !_isWithinUseSpan(trunk))
            ReqShut();
    }

    public void ReqShut()
    {
        uint trunk = _phase.LatestVesselIdent;
        if (trunk is 0u || _shutAsked)
            return;

        _shutAsked = true;
        _window.Hide();
        WipeRosters();
        _transmitUse(trunk);
    }

    public void OnPullLift(WidgetGearRoster srcRoster, WidgetGearSlot srcChamber, GearDragPayload cargo)
    {
        if (cargo.ObjId is not 0u)
            _pick.Select(cargo.ObjId, PickChangeSource.ExternalContainer);
    }

    public GearDragAcceptance OnPullOver(
        WidgetGearRoster markRoster,
        WidgetGearSlot markChamber,
        GearDragPayload cargo)
    {
        if (cargo.SourceKind == GearDragSource.ShortcutBar)
            return GearDragAcceptance.None;
        return !ReferenceEquals(markRoster, _insidesRoster)
            || _openVessel is 0u
            ? GearDragAcceptance.Reject
            : EvaluateDiscard(cargo.ObjId) == PackPlacementRefusal.None
            ? GearDragAcceptance.Accept
            : GearDragAcceptance.Reject;
    }

    public void ProcessDiscardFree(
        WidgetGearRoster markRoster,
        WidgetGearSlot markChamber,
        GearDragPayload cargo)
    {
        var legality = EvaluateDiscard(cargo.ObjId);
        if (legality != PackPlacementRefusal.None)
        {
            if (PackPlacementPolicy.ConstructClientOwn(
                    legality,
                    _objects.Get(cargo.ObjId),
                    _objects.Get(_openVessel),
                    avatarIdent: 0u) is { } refusal)

                _gearDealing.AnnounceClientOwn(refusal);
            return;
        }
        if (!_gearDealing.SecureSatchelReqPrimed())
            return;
        if (_objects.Get(cargo.ObjId) is not { } gear)
            return;

        uint wholePile = (uint)Math.Max(1, gear.StackSize);
        uint quantity = _pileDivideQty.FetchObjectDivideDims(
            gear.ObjectId,
            _pick.ChosenObjectTag ?? 0u,
            wholePile);
        int stance = markChamber.GearIdent is not 0u
            ? Math.Max(0, markChamber.SocketIdx)
            : _objects.FetchInsides(_openVessel).Count;

        PackRequestKind sort = quantity < wholePile
            ? PackRequestKind.SplitToContainer
            : PackRequestKind.PutInContainer;
        if (quantity < wholePile)
        {
            _gearDealing.TryRelaySatchelReq(
                sort,
                gear.ObjectId,
                () =>
                {
                    _transmitDivideToVessel(gear.ObjectId, _openVessel, (uint)stance, quantity);
                    return true;
                });
        }
        else
        {
            _gearDealing.TryRelayQueuedBackpackStance(
                gear.ObjectId,
                _openVessel,
                stance,
                sort,
                () =>
                {
                    _transmitPutGearInVessel(gear.ObjectId, _openVessel, stance);
                    return true;
                });
        }
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _phase.Changed -= OnExternalVesselAltered;
        _objects.ObjectAdded -= OnObjectAltered;
        _objects.ObjectUpdated -= OnObjectAltered;
        _objects.ObjectMoved -= OnObjectMoved;
        _objects.ObjectRemoved -= OnObjectRemoved;
        _objects.ContainerContentsReplaced -= OnInsidesReplaced;
        _objects.Cleared -= OnObjectsCleared;
        _pick.Changed -= OnPickAltered;
        _gearDealing.StateChanged -= OnDealingPhaseAltered;
        _gearDealing.PendingBackpackPlacementRequested -= OnQueuedStanceAsked;
        _gearDealing.PendingBackpackPlacementCancelled -= OnQueuedStanceCancelled;
        _gearDealing.PendingBackpackPlacementResolved -= OnQueuedStanceSettled;
        _topVessel.PrimaryGearPressed = null;
        _vesselRoster.PrimaryGearPressed = null;
        _insidesRoster.PrimaryGearPressed = null;
        _topVessel.ExamineGearAsked = null;
        _vesselRoster.ExamineGearAsked = null;
        _insidesRoster.ExamineGearAsked = null;
    }

    internal static CanonPaneCycle.Options BuildPaneKnobs(WidgetElem trunk)
    {
        return new()
        {
            PaneMoniker = PaneLabels.ExternalVessel,
            Chrome = CanonWindowChrome.NineSlice,
            Left = trunk.Left,
            Top = trunk.Top,
            SubstanceWidth = Math.Min(trunk.Width, DefaultSubstanceWidth),
            SubstanceHeight = trunk.Height,
            MinWidth = FloorSubstanceWidth + 2f * CanonChromeSprites.Border,
            Visible = false,
            Draggable = true,
            Resizable = true,
            RescaleX = true,
            RescaleY = false,
            ResizableRims = RescaleRims.Left | RescaleRims.Right,
            ConstrainPullToAncestor = true,
            ConstrainRescaleToAncestor = true,
            PaintChromeMiddle = false,
        };
    }

    private static void AttachShut(ImportedArrangement arrangement, Action shut)
    {
        switch (arrangement.SeekElem(ShutBtnIdent))
        {
            case WidgetBtn btn:
                btn.OnClick = shut;
                break;
            case WidgetDatElement elem:
                elem.ClickThrough = false;
                elem.OnClick = shut;
                break;
            default:
                throw new InvalidOperationException(
                    "External-container LayoutDesc is absent its close button");
        }
    }

    private WidgetGearSlot BuildChamber(WidgetGearRoster holder, uint oid, GearDragSource src)
    {
        var gear = _objects.Get(oid);
        uint glyph = gear is null ? 0u : _locateGlyph(
            gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
        uint pullGlyph = gear is null ? 0u : _locatePullGlyph(
            gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
        WidgetGearSlot chamber = new WidgetGearSlot
        {
            SpriteResolve = holder.SpriteResolve,
            SocketIdx = holder.FetchCountWIDGETGearList(),
            SrcSort = src,
            HintPhraseLocate = g => _objects.Get(g)?.FetchHintReadoutLabel(),
        };
        chamber.AssignGear(oid, glyph, pullGlyphTexture: pullGlyph);
        return chamber;
    }

    private void OnExternalVesselAltered(OpenContainerShift changeover)
    {
        if (changeover.Kind == OpenContainerShiftKind.ReplacementRequested)
        {
            _openVessel = 0u;
            _shutAsked = false;
            _window.Hide();
            WipeRosters();
            return;
        }

        if (changeover.ContainerId is 0u)
        {
            _openVessel = 0u;
            _shutAsked = false;
            _window.Hide();
            WipeRosters();
            return;
        }

        _openVessel = changeover.ContainerId;
        _shutAsked = false;
        RollToHome();
        Fill();
        _window.Display();
    }

    private void OnObjectAltered(ClientThing gear)
    {
        if (_window.IsVisible && Concerns(gear))
            Fill();
    }

    private void OnObjectMoved(ObjectRelocation relocate)
    {
        if (!_window.IsVisible) return;
        uint trunk = _phase.LatestVesselIdent;
        if ((relocate.Item is { } gear && Concerns(gear))
            || relocate.Previous.ContainerId == trunk
            || relocate.Current.ContainerId == trunk
            || relocate.Previous.ContainerId == _openVessel
            || relocate.Current.ContainerId == _openVessel)

            Fill();
    }

    private void OnInsidesReplaced(uint vesselIdent)
    {
        if (_window.IsVisible
            && (vesselIdent == _phase.LatestVesselIdent
                || vesselIdent == _openVessel
                || ProjContains(_phase.LatestVesselIdent, vesselIdent)))
            Fill();
    }

    private void OnObjectRemoved(ClientThing gear)
    {
        if (_pick.ChosenObjectTag == gear.ObjectId)
        {
            _pick.Clear(
                PickChangeSource.System,
                PickChangeReason.SelectedObjectRemoved);
        }

        if (gear.ObjectId == _phase.LatestVesselIdent)
            _phase.ImposeShut(gear.ObjectId);
        else if (_window.IsVisible && Concerns(gear))
            Fill();
    }

    private void OnObjectsCleared()
    {
        _openVessel = 0u;
        _shutAsked = false;
        _window.Hide();
        WipeRosters();
    }

    private void OnPickAltered(PickShift _) => ImposeIndicators();

    // A transaction only changes which cells are waiting on it
    private void OnDealingPhaseAltered() => ImposeTransactionPhases();

    private void OnQueuedStanceAsked(QueuedBackpackStance queued)
    {
        if (queued.ContainerId != _openVessel)
            return;
        _queuedStance = queued;
        if (_window.IsVisible)
            Fill();
    }

    private void OnQueuedStanceCancelled(QueuedBackpackStance queued)
        => LocateQueuedStance(queued);

    private void OnQueuedStanceSettled(QueuedBackpackStance queued)
        => LocateQueuedStance(queued);

    private void Fill()
    {
        uint trunk = _phase.LatestVesselIdent;
        if (trunk is 0u)
        {
            WipeRosters();
            return;
        }
        if (_openVessel is 0u)
            _openVessel = trunk;

        using IDisposable topArrangement = _topVessel.DeferArrangement();
        using IDisposable vesselArrangement = _vesselRoster.DeferArrangement();
        using IDisposable insidesArrangement = _insidesRoster.DeferArrangement();

        List<uint> shownVessels = new List<uint>();
        foreach (uint oid in _objects.FetchInsides(trunk))
        {
            if (IsVessel(_objects.Get(oid)))
                shownVessels.Add(oid);
        }

        List<uint> shownInsides = new List<uint>();
        foreach (uint oid in _objects.FetchInsides(_openVessel))
        {
            if (!IsVessel(_objects.Get(oid)))
                shownInsides.Add(oid);
        }
        if (_queuedStance is { } queued
            && queued.ContainerId == _openVessel
            && _objects.Get(queued.ItemId) is { } queuedGear
            && !IsVessel(queuedGear))
        {
            shownInsides.Remove(queued.ItemId);
            shownInsides.Insert(
                Math.Clamp(queued.Placement, 0, shownInsides.Count),
                queued.ItemId);
        }
        if (TryRenewChambersInPlace(trunk, shownVessels, shownInsides))
        {
            ImposeIndicators();
            return;
        }

        _topVessel.Flush();
        _vesselRoster.Flush();
        _insidesRoster.Flush();

        AppendTrunkChamber(trunk);
        foreach (uint oid in shownVessels)
            AppendVesselChamber(oid);

        foreach (uint oid in shownInsides)
            AppendInsidesChamber(oid, IsWaiting(oid));
        ImposeIndicators();
    }

    private bool IsWaiting(uint oid)
    {
        return _gearDealing.IsQueuedSatchelSrc(oid)
                || (_queuedStance is { } proj
                    && proj.ContainerId == _openVessel
                    && proj.ItemId == oid);
    }

    private static bool IsVessel(ClientThing? gear)
    {
        return gear is not null
                && (gear.VesselKindHint is not 0u
                    || gear.Type.HasFlag(GearKind.Container)
                    || gear.ItemsCapacity > 0);
    }

    // False when anything structural moved, which needs the rebuild
    private bool TryRenewChambersInPlace(
        uint trunk,
        IReadOnlyList<uint> shownVessels,
        IReadOnlyList<uint> shownInsides)
    {
        if (_topVessel.FetchCountWIDGETGearList() is not 1
            || _topVessel.GetItem(0)?.GearIdent != trunk
            || !Fits(_vesselRoster, shownVessels)
            || !Fits(_insidesRoster, shownInsides))

            return false;

        RenewGlyphs(_topVessel, [trunk]);
        AssignCap(_topVessel.GetItem(0)!, trunk);
        RenewGlyphs(_vesselRoster, shownVessels);
        for (int idx = 0; idx < shownVessels.Count; ++idx)
            AssignCap(_vesselRoster.GetItem(idx)!, shownVessels[idx]);
        RenewGlyphs(_insidesRoster, shownInsides);
        for (int idx = 0; idx < shownInsides.Count; ++idx)
            _insidesRoster.GetItem(idx)!.AssignWaitingPhase(IsWaiting(shownInsides[idx]));
        return true;
    }

    // The lists pad themselves with empty cells (FillVisibleEmptySlots)
    private static bool Fits(WidgetGearRoster roster, IReadOnlyList<uint> oids)
    {
        if (roster.FetchCountWIDGETGearList() < oids.Count) return false;
        for (int idx = 0; idx < roster.FetchCountWIDGETGearList(); ++idx)
        {
            uint anticipated = idx < oids.Count ? oids[idx] : 0u;
            if (roster.GetItem(idx) is not { } chamber || chamber.GearIdent != anticipated) return false;
        }
        return true;
    }

    private void RenewGlyphs(WidgetGearRoster roster, IReadOnlyList<uint> oids)
    {
        for (int idx = 0; idx < oids.Count; ++idx)
        {
            if (roster.GetItem(idx) is not { } chamber) continue;
            uint oid = oids[idx];
            var gear = _objects.Get(oid);
            uint glyph = gear is null ? 0u : _locateGlyph(
                gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
            uint pullGlyph = gear is null ? 0u : _locatePullGlyph(
                gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
            chamber.AssignGear(oid, glyph, pullGlyphTexture: pullGlyph);
        }
    }

    private void AppendTrunkChamber(uint oid)
    {
        var chamber = BuildChamber(_topVessel, oid, GearDragSource.Ground);
        chamber.DoubleClicked = ReqShut;
        chamber.IsOpenVessel = _openVessel == oid;
        AssignCap(chamber, oid);
        _topVessel.AddItem(chamber);
    }

    private void AppendVesselChamber(uint oid)
    {
        var chamber = BuildChamber(_vesselRoster, oid, GearDragSource.Ground);
        AssignCap(chamber, oid);
        _vesselRoster.AddItem(chamber);
    }

    private void AppendInsidesChamber(uint oid, bool waiting = false)
    {
        var chamber = BuildChamber(_insidesRoster, oid, GearDragSource.Ground);
        chamber.DoubleClicked = () => _gearDealing.EngageGear(oid);
        chamber.AssignWaitingPhase(waiting);
        chamber.PullAdmitSprite = 0x060011F9u;
        chamber.PullRejectSprite = 0x060011F8u;
        _insidesRoster.AddItem(chamber);
    }

    private void OpenNestedVessel(uint oid)
    {
        Select(oid);
        _openVessel = oid;
        _insidesRoster.Scroll.AssignRollY(0);
        Fill();
    }

    private void Select(uint oid)
        => _pick.Select(oid, PickChangeSource.ExternalContainer);

    private void StudyGear(uint oid)
    {
        Select(oid);
        _gearDealing.StudyChosenOrJoinManner(oid);
    }

    private bool PressGear(uint oid)
    {
        if (_gearDealing.OfferPrimaryPress(oid) != GearPrimaryClickResult.NotActive)
            return true;
        if (IsVessel(_objects.Get(oid)) && oid != _phase.LatestVesselIdent)
            OpenNestedVessel(oid);
        else
            Select(oid);
        return false;
    }

    private void ImposeIndicators()
    {
        ImposeIndicators(_topVessel);
        ImposeIndicators(_vesselRoster);
        ImposeIndicators(_insidesRoster);
    }

    private void ImposeIndicators(WidgetGearRoster roster)
    {
        for (int idx = 0; idx < roster.FetchCountWIDGETGearList(); ++idx)
        {
            if (roster.GetItem(idx) is not { } chamber) continue;
            bool queuedSrc = _gearDealing.IsQueuedSrc(chamber.GearIdent);
            chamber.Selected = chamber.GearIdent is not 0u
                && chamber.GearIdent == _pick.ChosenObjectTag
                && !queuedSrc
                && !_gearDealing.IsQueuedSatchelSrc(chamber.GearIdent);
            chamber.IsOpenVessel = chamber.GearIdent is not 0u && chamber.GearIdent == _openVessel;
        }
    }

    private void ImposeTransactionPhases()
    {
        for (int idx = 0; idx < _insidesRoster.FetchCountWIDGETGearList(); ++idx)
        {
            if (_insidesRoster.GetItem(idx) is not { GearIdent: not 0u } chamber) continue;
            chamber.AssignWaitingPhase(
                _gearDealing.IsQueuedSatchelSrc(chamber.GearIdent)
                || (_queuedStance is { } proj
                    && proj.ContainerId == _openVessel
                    && proj.ItemId == chamber.GearIdent));
        }

        ImposeIndicators();
    }

    private void AssignCap(WidgetGearSlot chamber, uint vesselIdent)
    {
        int cap = _objects.Get(vesselIdent)?.ItemsCapacity ?? 0;
        chamber.CapPopulate = cap <= 0
            ? -1f
            : Math.Clamp(_objects.FetchInsides(vesselIdent).Count / (float)cap, 0f, 1f);
    }

    private void WipeRosters()
    {
        _topVessel.Flush();
        _vesselRoster.Flush();
        _insidesRoster.Flush();
        RollToHome();
    }

    private void RollToHome()
    {
        _topVessel.Scroll.AssignRollY(0);
        _vesselRoster.Scroll.AssignRollY(0);
        _insidesRoster.Scroll.AssignRollY(0);
    }

    private void LocateQueuedStance(QueuedBackpackStance queued)
    {
        if (_queuedStance is not { } latest || latest.Token != queued.Token)
            return;
        _queuedStance = null;
        if (_window.IsVisible)
            Fill();
    }

    private PackPlacementRefusal EvaluateDiscard(uint gearIdent)
    {
        return _objects.Get(gearIdent) is { } src && IsVessel(src)
            ? PackPlacementRefusal.ContainerCapacityFull
            : PackPlacementPolicy.Evaluate(
            _objects,
            gearIdent,
            _openVessel,
            avatarIdent: 0u);
    }

    private bool Concerns(ClientThing gear)
    {
        uint trunk = _phase.LatestVesselIdent;
        return gear.ObjectId == trunk
            || gear.ObjectId == _openVessel
            || gear.VesselTag == trunk
            || gear.VesselTag == _openVessel
            || ProjContains(trunk, gear.ObjectId)
            || ProjContains(_openVessel, gear.ObjectId);
    }

    private bool ProjContains(uint vesselIdent, uint gearIdent)
    {
        if (vesselIdent is 0u || gearIdent is 0u) return false;
        foreach (uint contender in _objects.FetchInsides(vesselIdent))
        {
            if (contender == gearIdent) return true;
        }
        return false;
    }

    private static void ConfigureRoster(
        WidgetGearRoster roster,
        float chamberDims,
        bool horizontalRoll,
        uint vacantSprite)
    {
        roster.Columns = 1;
        roster.ChamberWidth = chamberDims;
        roster.ChamberHeight = chamberDims;
        roster.SingleRank = true;
        roster.HorizontalRoll = horizontalRoll;
        roster.PopulateShownVacantSockets = true;
        if (vacantSprite is not 0u)
            roster.ChamberVacantSprite = vacantSprite;
        roster.VacantSocketMaker = () => new WidgetGearSlot
        {
            SpriteResolve = roster.SpriteResolve,
            SrcSort = GearDragSource.Ground,
            PullAdmitSprite = 0x060011F9u,
            PullRejectSprite = 0x060011F8u,
        };
    }

    private static WidgetGearRoster NeededRoster(ImportedArrangement arrangement, uint ident)
    {
        return arrangement.SeekElem(ident) as WidgetGearRoster
                ?? throw new InvalidOperationException(
                    $"External-container LayoutDesc is absent ItemList 0x{ident:X8}.");
    }
}
