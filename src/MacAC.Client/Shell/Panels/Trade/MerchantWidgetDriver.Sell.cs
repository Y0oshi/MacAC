using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class MerchantWidgetDriver
{

    public void OnPullLift(WidgetGearRoster srcRoster, WidgetGearSlot srcChamber, GearDragPayload cargo)
    {
        if (!ReferenceEquals(srcRoster, _sellingRoster) || cargo.ObjId is 0u)
            return;

        _pick.Select(cargo.ObjId, PickChangeSource.Vendor);
        DropSellingListing(cargo.ObjId, dossierDeletion: false);
        if (_objects.Get(cargo.ObjId) is not { } gear)
            return;

        uint wholePile = (uint)Math.Max(1, gear.StackSize);
        uint chosen = _divideQty.FetchObjectDivideDims(
            cargo.ObjId,
            _pick.ChosenObjectTag ?? 0u,
            wholePile);
        if (chosen < wholePile)
        {
            _gearDealing.AnnounceClientOwn(
                "You cannot split items from this panel");
            _divideQty.Reset(wholePile);
        }
    }

    public GearDragAcceptance OnPullOver(
        WidgetGearRoster markRoster,
        WidgetGearSlot markChamber,
        GearDragPayload cargo)
    {
        return !ReferenceEquals(markRoster, _sellingRoster) || cargo.ObjId is 0u
            ? GearDragAcceptance.Reject
            : EvaluateVendAcceptability(cargo.ObjId, out _) == VendorSellRefusal.None
            ? GearDragAcceptance.Accept
            : GearDragAcceptance.Reject;
    }

    public void ProcessDiscardFree(
        WidgetGearRoster markRoster,
        WidgetGearSlot markChamber,
        GearDragPayload cargo)
    {
        if (!ReferenceEquals(markRoster, _sellingRoster) || cargo.ObjId is 0u)
            return;

        var rejection = EvaluateVendAcceptability(cargo.ObjId, out int qty);
        if (rejection != VendorSellRefusal.None)
        {
            if (VendorSellVerdict.MsgFor(rejection) is { } msg)
                _sysMsg?.Invoke(msg);
            return;
        }

        RevealTab(MerchantPaneTab.Selling);
        _pick.Select(cargo.ObjId, PickChangeSource.Vendor);

        var gear = _objects.Get(cargo.ObjId)!;
        var insides = _objects.FetchInsides(cargo.ObjId);
        if (insides.Count > 0)
        {
            JunctureVesselInsides(gear, insides);
            return;
        }

        int wholePile = Math.Max(1, gear.StackSize);
        if (qty < wholePile)
        {
            _queuedMerchantDivide = new PendingMerchantSplit(
                cargo.ObjId,
                gear.WeenieClassIdent,
                qty);
            if (!_gearDealing.TryDivideToVessel(
                    cargo.ObjId,
                    gear.VesselTag,
                    0u,
                    (uint)qty))
            {
                _queuedMerchantDivide = null;
                _sysMsg?.Invoke("Cannot split the stack to sell it");
                return;
            }

            string label = string.IsNullOrWhiteSpace(gear.Name) ? "item" : gear.Name;
            _sysMsg?.Invoke($"Splitting the {label} before selling them");
        }
        _vendLoading.Stage(cargo.ObjId, qty);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _merchant.Changed -= OnMerchantAltered;
        _pick.Changed -= OnPickChangeover;
        _objects.ObjectAdded -= OnObjectAdded;
        _objects.ObjectRemoved -= OnObjectRemoved;
        _objects.ObjectUpdated -= OnObjectMoneyAltered;
        _objects.StackSizeUpdated -= OnPileDimsUpdated;
        _objects.ObjectMoved -= OnObjectMoved;
        _gearDealing.CoreTransactions.Inventory.RequestFailed -= OnSatchelReqFailed;
        _gearDealing.StateChanged -= OnDealingPhaseAltered;
        _divideQty.Changed -= OnDivideQtyAltered;
        _purchaseLoading.Changed -= ReassembleBuyingRoster;
        _purchaseLoading.Changed -= RenewGearListTabReadiness;
        _purchaseLoading.Changed -= RefreshPurchaseTransactionPhrase;
        _vendLoading.Changed -= ReassembleSellingRoster;
        _vendLoading.Changed -= RefreshVendTransactionPhrase;
        DismissShutAckIfOpen();
        _pullOverDrain.Ancestor?.DropDescendant(_pullOverDrain);
        CanonTabWiring.AssignPress(_gearListTab, null);
        CanonTabWiring.AssignPress(_buyingTab, null);
        CanonTabWiring.AssignPress(_sellingTab, null);
        _kindMenu.OnSelect = null;
        _kindMenu.BtnCaptionSupplier = null;
        _gearRoster.ExamineGearAsked = null;
        _gearRoster.PrimaryGearPressed = null;
        if (_buyingRoster is not null)
        {
            _buyingRoster.ExamineGearAsked = null;
            _buyingRoster.PrimaryGearPressed = null;
        }
        if (_sellingRoster is not null)
        {
            _sellingRoster.ExamineGearAsked = null;
            _sellingRoster.PrimaryGearPressed = null;
        }
        _shut?.OnClick = null;
        _purchaseBtn?.OnClick = null;
        _appendBtn?.OnClick = null;
        _purchaseGearBtn?.OnClick = null;
        _purchaseAllBtn?.OnClick = null;
        _purchaseWipeGearBtn?.OnClick = null;
        _purchaseWipeRosterBtn?.OnClick = null;
        _vendGearBtn?.OnClick = null;
        _vendAllBtn?.OnClick = null;
        _vendWipeGearBtn?.OnClick = null;
        _vendWipeRosterBtn?.OnClick = null;
    }
    private void VendAllBtnPressed()
    {
        if (_vendLoading.IsEmpty)
            return;

        var gearList = new List<(int Amount, uint ItemGuid)>(_vendLoading.Listings.Count);
        foreach (VendorTrayRow listing in _vendLoading.Listings)
            gearList.Add((listing.Quantity, listing.ItemGuid));

        if (_gearDealing.TryVend(_merchant.MerchantIdent, gearList))
            _vendLoading.Clear();
    }

    private void VendWipeGearBtnPressed()
    {
        if (_pick.ChosenObjectTag is not { } oid)
            return;
        _vendLoading.Remove(oid, -1);
    }

    private void ReassembleBuyingRoster()
    {
        if (_buyingRoster is not { } roster)
            return;

        uint? chosenOid = _pick.ChosenObjectTag;
        using (roster.DeferArrangement())
        {
            roster.Flush();
            foreach (VendorTrayRow listing in _purchaseLoading.Listings)
            {
                if (!TrySeekShopGear(listing.ItemGuid, out VendorWare shopGear))
                    continue;

                uint glyph = _locateGlyph(
                    (GearKind)(shopGear.ItemType ?? 0u),
                    shopGear.IconId,
                    shopGear.IconUnderlayId,
                    shopGear.IconOverlayId,
                    shopGear.Effects);
                WidgetGearSlot chamber = new WidgetGearSlot
                {
                    SpriteResolve = roster.SpriteResolve,
                    SocketIdx = roster.FetchCountWIDGETGearList(),
                    AllowPullSrc = false,
                    HintPhraseLocate = g => _objects.Get(g)?.FetchHintReadoutLabel(),
                };
                chamber.AssignGear(shopGear.ItemGuid, glyph);
                chamber.Selected = shopGear.ItemGuid == chosenOid;
                VendorWare grabbed = shopGear;
                chamber.Clicked = () =>
                    _pick.Select(grabbed.ItemGuid, PickChangeSource.Vendor);
                chamber.DoubleClicked = () => DropOneBuyingUnit(grabbed.ItemGuid);
                roster.AddItem(chamber);
            }
        }
    }

    private void ReassembleSellingRoster()
    {
        if (_sellingRoster is not { } roster)
            return;

        uint? chosenOid = _pick.ChosenObjectTag;
        using (roster.DeferArrangement())
        {
            roster.Flush();
            foreach (VendorTrayRow listing in _vendLoading.Listings)
            {
                if (_objects.Get(listing.ItemGuid) is not { } gear)
                    continue;

                uint glyph = _locateGlyph(
                    gear.Type, gear.IconId, gear.GlyphUnderlayIdent, gear.GlyphTopLayerIdent, gear.Effects);
                WidgetGearSlot chamber = new WidgetGearSlot
                {
                    SpriteResolve = roster.SpriteResolve,
                    SocketIdx = roster.FetchCountWIDGETGearList(),
                    AllowPullSrc = true,
                    SrcSort = GearDragSource.Inventory,
                    HintPhraseLocate = g => _objects.Get(g)?.FetchHintReadoutLabel(),
                };
                chamber.AssignGear(gear.ObjectId, glyph);
                chamber.Selected = gear.ObjectId == chosenOid;
                uint grabbed = gear.ObjectId;
                chamber.Clicked = () =>
                    _pick.Select(grabbed, PickChangeSource.Vendor);
                chamber.DoubleClicked = () => DropSellingListing(grabbed);
                roster.AddItem(chamber);
            }
        }
    }

    private void DropOneBuyingUnit(uint gearOid)
    {
        if (!_purchaseLoading.TryGet(gearOid, out _))
            return;
        _pick.Select(gearOid, PickChangeSource.Vendor);
        AnnounceShoppingRosterDeletion(gearOid);
        _purchaseLoading.Remove(gearOid, 1);
    }

    private void DropSellingListing(uint gearOid, bool dossierDeletion = true)
    {
        if (!_vendLoading.TryGet(gearOid, out _))
            return;
        _pick.Select(gearOid, PickChangeSource.Vendor);
        if (dossierDeletion)
            AnnounceShoppingRosterDeletion(gearOid);
        _vendLoading.Remove(gearOid, -1);
    }

    private void AnnounceShoppingRosterDeletion(uint gearOid)
    {
        string? label = _objects.Get(gearOid)?.FetchAppropriateLabel();
        if (string.IsNullOrWhiteSpace(label))
            label = _merchant.Items.FirstOrDefault(gear => gear.ItemGuid == gearOid).Name;
        if (string.IsNullOrWhiteSpace(label))
            label = "that item";
        _gearDealing.AnnounceClientOwn(
            $"Removing {label} from shopping list");
    }

    private void SamplePullOver()
    {
        if (_sellingSheet.Visible) return;
        if (!_window.IsVisible) return;

        WidgetTrunk? trunk = _gearListSheet.SeekTrunk();
        if (trunk?.PullSrc is null) return;

        var spot = _window.OuterCycle.MonitorLocus;
        float x0 = spot.X, y0 = spot.Y;
        float x1 = x0 + _window.OuterCycle.Width, y1 = y0 + _window.OuterCycle.Height;
        if (trunk.PointerX > x0 && trunk.PointerX < x1 && trunk.PointerY > y0 && trunk.PointerY < y1)
            RevealTab(MerchantPaneTab.Selling);
    }

    private void JunctureVesselInsides(ClientThing bundle, IReadOnlyList<uint> insides)
    {
        string label = string.IsNullOrWhiteSpace(bundle.Name) ? "item" : bundle.Name;
        _sysMsg?.Invoke($"Selling contents of {label}");

        uint[] descendants = [.. insides];
        foreach (uint descendant in descendants)
        {
            if (_objects.FetchInsides(descendant).Count > 0)
                continue;
            if (EvaluateVendAcceptability(descendant, out int descendantQty) != VendorSellRefusal.None)
                continue;
            _vendLoading.Stage(descendant, descendantQty);
        }
    }

    private VendorSellRefusal EvaluateVendAcceptability(uint gearOid, out int qty)
    {
        qty = 1;
        if (_objects.Get(gearOid) is not { } gear)
            return VendorSellRefusal.WrongType;

        bool possessedByAvatar = _gearDealing.IsPossessedByAvatar(gearOid);
        int containedGearTally = _objects.FetchInsides(gearOid).Count;
        int perUnitVal = VendorPriceRules.PerUnitVal(gear.Value, gear.StackSize);

        var rejection = VendorSellVerdict.Evaluate(
            possessedByAvatar,
            containedGearTally,
            (uint)gear.Type,
            perUnitVal,
            _merchant.Profile.MerchandiseItemTypes,
            _merchant.Profile.MerchandiseMinValue,
            _merchant.Profile.MerchandiseMaxValue,
            gear.PublicWeenieBitfield ?? 0u);

        if (rejection == VendorSellRefusal.None)
        {
            uint wholePile = (uint)Math.Max(1, gear.StackSize);
            qty = (int)Math.Min(
                _divideQty.FetchObjectDivideDims(
                    gearOid,
                    _pick.ChosenObjectTag ?? 0u,
                    wholePile),
                wholePile);
        }
        return rejection;
    }

    private void TryLocateQueuedMerchantDivide(ClientThing gear)
    {
        if (_queuedMerchantDivide is not { } queued
            || gear.ObjectId == queued.SourceGuid
            || gear.WeenieClassIdent != queued.WeenieClassId
            || gear.StackSize != queued.Quantity
            || !_objects.IsPossessedByObject(gear.ObjectId, _avatarOid()))

            return;

        if (_vendLoading.Replace(queued.SourceGuid, gear.ObjectId))
            _queuedMerchantDivide = null;
    }

    private void OnSatchelReqFailed(PackRequestInFlight req, uint _)
    {
        if (_queuedMerchantDivide is not { } queued
            || req.Kind != PackRequestKind.SplitToContainer
            || req.ItemId != queued.SourceGuid)

            return;

        _vendLoading.Remove(queued.SourceGuid, -1);
        _queuedMerchantDivide = null;
    }

    private void ShutBtnPressed()
    {
        if (_purchaseLoading.IsEmpty && _vendLoading.IsEmpty)
        {
            _window.Hide();
            return;
        }

        if (_popups is null || _shutConfirmCtx is not 0u)
            return;

        _shutConfirmCtx = _popups.MakeDialog(
            CanonPromptData.Confirmation(ShutAckMsg),
            outcome =>
            {
                _shutConfirmCtx = 0u;
                if (outcome.FetchBoolean(CanonPromptProperty.AckOutcome))
                    _window.Hide();
            });
    }

    private void DismissShutAckIfOpen()
    {
        if (_shutConfirmCtx is 0u)
            return;
        _popups?.ShutPopup(_shutConfirmCtx);
        _shutConfirmCtx = 0u;
    }

    private void WipeSubstance()
    {
        _presentBuckets.Clear();
        _chosenBucketOrdinal = -1;
        _kindMenu.Items = Array.Empty<WidgetMenu.MenuGear>();
        _kindMenu.Selected = null;
        _gearRoster.Flush();
        WipePickReadout();
        RefreshPurchaseTransactionPhrase();
        RefreshVendTransactionPhrase();
    }

    private static void ConfigureVacantStrip(WidgetGearRoster? roster, uint vacantSocketSprite)
    {
        if (roster is null) return;

        roster.Flush();
        roster.Columns = 1;
        roster.SingleRank = true;
        roster.HorizontalRoll = true;
        roster.ChamberWidth = 32f;
        roster.ChamberHeight = 32f;
        roster.PopulateShownVacantSockets = true;
        if (vacantSocketSprite is not 0u)
            roster.ChamberVacantSprite = vacantSocketSprite;
        roster.VacantSocketMaker = () => new WidgetGearSlot
        {
            SpriteResolve = roster.SpriteResolve,
            AllowPullSrc = false,
        };
    }

    private static void AssignPlainPhrase(WidgetPhrase phrase, string val)
    {
        IReadOnlyList<WidgetPhrase.Line> strokes = string.IsNullOrEmpty(val)
            ? []
            : new[] { new WidgetPhrase.Line(val, phrase.DefaultTint) };
        phrase.StrokesSupplier = () => strokes;
    }
}
