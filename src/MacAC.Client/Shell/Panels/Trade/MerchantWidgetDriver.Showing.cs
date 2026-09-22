using System.Globalization;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;
using MacAC.Mechanics.Traits;

namespace MacAC.Client.Shell.Panels;

public sealed partial class MerchantWidgetDriver
{
    private void RevealTab(MerchantPaneTab tab)
    {
        if (_previousAlternateCurrencyPurchase is not 0)
        {
            _previousAlternateCurrencyPurchase = 0;
            RenewMoneyPhrase();
        }
        _gearListSheet.Visible = tab == MerchantPaneTab.Items;
        _buyingSheet.Visible = tab == MerchantPaneTab.Buying;
        _sellingSheet.Visible = tab == MerchantPaneTab.Selling;
        CanonTabWiring.ApplyOpen(_gearListTab, tab == MerchantPaneTab.Items);
        CanonTabWiring.ApplyOpen(_buyingTab, tab == MerchantPaneTab.Buying);
        CanonTabWiring.ApplyOpen(_sellingTab, tab == MerchantPaneTab.Selling);
    }

    private void OnMerchantAltered(VendorShift changeover)
    {
        switch (changeover.Kind)
        {
            case VendorShiftKind.Opened:
                _purchaseLoading.Clear();
                _vendLoading.Clear();
                _queuedMerchantDivide = null;
                RestartAlternateCurrencyTracking();
                RenewMoneyPhrase();
                _chosenBucketOrdinal = -1;
                RevealTab(MerchantPaneTab.Items);
                ReassembleBuckets();
                _window.Display();
                break;
            case VendorShiftKind.Refreshed:
                RestartAlternateCurrencyTracking();
                RenewMoneyPhrase();
                RevealTab(MerchantPaneTab.Items);
                ReassembleBuckets();
                _window.Display();
                break;
            case VendorShiftKind.Closed:
            case VendorShiftKind.Reset:
                _purchaseLoading.Clear();
                _vendLoading.Clear();
                _queuedMerchantDivide = null;
                RestartAlternateCurrencyTracking();
                WipeSubstance();
                RevealTab(MerchantPaneTab.Items);
                _window.Hide();
                DismissShutAckIfOpen();
                break;
        }
    }

    private void ReassembleBuckets()
    {
        var gearList = _merchant.Items;

        _presentBuckets.Clear();
        foreach ((string caption, GearKind bitmask) in BucketFilters)
        {
            uint bitmaskVal = (uint)bitmask;
            bool present = false;
            for (int idx = 0; idx < gearList.Count; ++idx)
            {
                if (((gearList[idx].ItemType ?? 0u) & bitmaskVal) is not 0u)
                {
                    present = true;
                    break;
                }
            }
            if (present) _presentBuckets.Add((caption, bitmask));
        }

        int chosen = _chosenBucketOrdinal;
        if (chosen >= _presentBuckets.Count - 1)
            chosen = _presentBuckets.Count - 1;
        if (chosen < 0)
            chosen = 0;
        _chosenBucketOrdinal = chosen;

        _kindMenu.Items = _presentBuckets
            .Select(listing => new WidgetMenu.MenuGear(listing.Label, (object)(uint)listing.Mask))
            .ToArray();
        _kindMenu.Selected = _chosenBucketOrdinal >= 0 && _chosenBucketOrdinal < _presentBuckets.Count
            ? (object)(uint)_presentBuckets[_chosenBucketOrdinal].Mask
            : null;

        ReassembleGearRoster();
    }

    private void PickBucket(uint bitmask)
    {
        int ordinal = _presentBuckets.FindIndex(listing => (uint)listing.Mask == bitmask);
        if (ordinal < 0 || ordinal == _chosenBucketOrdinal) return;

        _chosenBucketOrdinal = ordinal;
        _kindMenu.Selected = (object)bitmask;
        ReassembleGearRoster();
    }

    private void ReassembleGearRoster() => ReassembleGearRoster(reselectLead: true);

    private void ReassembleGearRoster(bool reselectLead)
    {
        GearKind engagedBitmask = _chosenBucketOrdinal >= 0 && _chosenBucketOrdinal < _presentBuckets.Count
            ? _presentBuckets[_chosenBucketOrdinal].Mask
            : default;
        uint bitmaskVal = (uint)engagedBitmask;

        var gearList = _merchant.Items;
        uint? chosenOid = _pick.ChosenObjectTag;
        VendorWare? leadGear = null;
        bool chosenStillShown = false;

        using (_gearRoster.DeferArrangement())
        {
            _gearRoster.Flush();
            if (bitmaskVal is not 0u)
            {
                foreach (VendorWare gear in gearList)
                {
                    if (((gear.ItemType ?? 0u) & bitmaskVal) is 0u) continue;
                    if (OnHandShopQty(gear) <= 0) continue;

                    leadGear ??= gear;
                    if (gear.ItemGuid == chosenOid) chosenStillShown = true;

                    uint glyph = _locateGlyph(
                        (GearKind)(gear.ItemType ?? 0u),
                        gear.IconId,
                        gear.IconUnderlayId,
                        gear.IconOverlayId,
                        gear.Effects);
                    WidgetGearSlot chamber = new WidgetGearSlot
                    {
                        SpriteResolve = _gearRoster.SpriteResolve,
                        SocketIdx = _gearRoster.FetchCountWIDGETGearList(),
                        AllowPullSrc = false,
                        HintPhraseLocate = g => _objects.Get(g)?.FetchHintReadoutLabel(),
                    };
                    chamber.AssignGear(gear.ItemGuid, glyph);
                    chamber.Selected = gear.ItemGuid == chosenOid;
                    VendorWare grabbed = gear;
                    chamber.Clicked = () =>
                        _pick.Select(grabbed.ItemGuid, PickChangeSource.Vendor);
                    chamber.DoubleClicked = () =>
                    {
                        _pick.Select(grabbed.ItemGuid, PickChangeSource.Vendor);
                        PurchaseChosenGear();
                    };
                    _gearRoster.AddItem(chamber);
                }
            }
        }

        if (reselectLead)
        {
            if (leadGear is { } lead)
                _pick.Select(lead.ItemGuid, PickChangeSource.Vendor);
            else
                _pick.Clear(PickChangeSource.Vendor);

            _gearRoster.Scroll.AssignRollY(0);
        }
        else if (chosenOid is not null && !chosenStillShown)
        {
            _pick.Clear(
                PickChangeSource.Vendor,
                PickChangeReason.SelectedObjectRemoved);
        }
    }

    private int OnHandShopQty(VendorWare gear)
    {
        if (gear.StackSize < 0)
            return int.MaxValue;
        int lined = _purchaseLoading.TryGet(gear.ItemGuid, out VendorTrayRow listing) ? listing.Quantity : 0;
        return gear.StackSize - lined;
    }

    private void RenewGearListTabReadiness() => ReassembleGearRoster(reselectLead: false);

    private void ImposeGearReadout(VendorWare gear)
    {
        for (int idx = 0; idx < _gearRoster.FetchCountWIDGETGearList(); ++idx)
        {
            if (_gearRoster.GetItem(idx) is { } chamber)
                chamber.Selected = chamber.GearIdent == gear.ItemGuid;
        }

        int qty = (int)LocatePurchaseQty(gear);

        string baseLabel = qty <= 1
            ? gear.Name ?? string.Empty
            : (string.IsNullOrEmpty(gear.PluralName) ? gear.Name : gear.PluralName) ?? string.Empty;
        string labelPhrase = qty > 1 ? $"{qty} {baseLabel}" : baseLabel;
        AssignPlainPhrase(_gearLabelPhrase, labelPhrase);

        var profile = _merchant.Profile;
        int price = CalculateShopGearPrice(gear, qty);
        AssignPlainPhrase(_gearPricePhrase, AssemblePricePhrase(profile, qty, price));

        AssignActBtnsTurnedOn(true);
    }

    private int CalculateShopGearPrice(VendorWare gear, int qty)
    {
        int rawVal = gear.Value ?? 0;
        int perUnit = VendorPriceRules.PerUnitVal(rawVal, gear.DescStackSize);
        return VendorPriceRules.SellPrice(
            perUnit,
            gear.ItemType ?? 0u,
            _merchant.Profile.SellPrice,
            qty);
    }

    private void StudyGear(uint oid)
    {
        _pick.Select(oid, PickChangeSource.Vendor);
        _gearDealing.StudyChosenOrJoinManner(oid);
    }

    private bool PressMerchantGear(uint oid)
    {
        if (oid is not 0u)
            _pick.Select(oid, PickChangeSource.Vendor);
        return false;
    }

    private void OnPickChangeover(PickShift changeover)
    {
        _ = changeover;
        RenewPickReadout();
        RenewLoadingPickHighlight();
    }

    private void RenewLoadingPickHighlight()
    {
        uint? chosen = _pick.ChosenObjectTag;
        AssignHighlight(_buyingRoster, chosen);
        AssignHighlight(_sellingRoster, chosen);

        static void AssignHighlight(WidgetGearRoster? roster, uint? chosenOid)
        {
            if (roster is null) return;
            for (int idx = 0; idx < roster.FetchCountWIDGETGearList(); ++idx)
            {
                if (roster.GetItem(idx) is { } chamber)
                    chamber.Selected = chamber.GearIdent == chosenOid;
            }
        }
    }

    private void OnDivideQtyAltered() => RenewPickReadout();

    private void RenewPickReadout()
    {
        uint? chosen = _pick.ChosenObjectTag;
        if (chosen is { } oid)
        {
            foreach (VendorWare gear in _merchant.Items)
            {
                if (gear.ItemGuid == oid)
                {
                    ImposeGearReadout(gear);
                    return;
                }
            }
        }
        WipePickReadout();
    }

    private void OnObjectRemoved(ClientThing gear)
    {
        if (IsLatestAlternateCurrency(gear))
        {
            _alternateCurrencySatchelObserved = true;
            _previousAlternateCurrencyPurchase = 0;
            RenewMoneyPhrase();
        }
        if (_queuedMerchantDivide is { } divide && divide.SourceGuid == gear.ObjectId)
            _queuedMerchantDivide = null;

        if (_pick.ChosenObjectTag == gear.ObjectId)
        {
            _pick.Clear(
                PickChangeSource.Vendor,
                PickChangeReason.SelectedObjectRemoved);
        }

        _vendLoading.Remove(gear.ObjectId, -1);

        if (_purchaseLoading.Remove(gear.ObjectId, -1))
        {
            string label = string.IsNullOrWhiteSpace(gear.Name) ? "that item" : gear.Name;
            _sysMsg?.Invoke($"Removing {label} from shopping list");
        }
    }

    private uint LocatePurchaseQty(VendorWare gear)
    {
        uint pileDims = (uint)VendorSplitRules.LocateAuthoredPileDims(gear.DescStackSize, gear.MaxStackSize);
        if (pileDims <= 1u)
            return 1u;

        uint chosen = _pick.ChosenObjectTag ?? gear.ItemGuid;
        return _divideQty.FetchObjectDivideDims(gear.ItemGuid, chosen, pileDims);
    }

    private string AssemblePricePhrase(VendorCatalogue profile, int qty, int price)
    {
        if (profile.AlternateCurrencyWcid is not 0u)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "This item costs {0} {1}. You have {2} {1}.",
                price,
                profile.AlternateCurrencyPluralName,
                LocateAlternateCurrencyQuantity(profile));
        }

        int avatarSum = _objects.Get(_avatarOid())?.Properties.FetchInt((uint)TraitInt.CoinValue) ?? 0;
        string verb = qty <= 1 ? "costs" : "cost";
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}p (you have {2}p)",
            verb,
            price.ToString("N0", CultureInfo.InvariantCulture),
            avatarSum.ToString("N0", CultureInfo.InvariantCulture));
    }

    private void WipePickReadout()
    {
        for (int idx = 0; idx < _gearRoster.FetchCountWIDGETGearList(); ++idx)
        {
            if (_gearRoster.GetItem(idx) is { } chamber)
                chamber.Selected = false;
        }
        AssignPlainPhrase(_gearLabelPhrase, string.Empty);
        AssignPlainPhrase(_gearPricePhrase, string.Empty);
        AssignActBtnsTurnedOn(false);
    }

    private void AssignActBtnsTurnedOn(bool turnedOn)
    {
        _purchaseTurnedOnByPick = turnedOn;
        RecomputePurchaseBtnTurnedOn();
    }

    private void RecomputePurchaseBtnTurnedOn()
    {
        _purchaseBtn?.Enabled = _purchaseTurnedOnByPick && _gearDealing.CanCraftSatchelReq;
        _appendBtn?.Enabled = _purchaseTurnedOnByPick;
    }

    private void OnDealingPhaseAltered() => RecomputePurchaseBtnTurnedOn();

    private void PurchaseChosenGear()
    {
        if (_pick.ChosenObjectTag is not { } oid)
            return;

        VendorWare? chosen = null;
        foreach (VendorWare gear in _merchant.Items)
        {
            if (gear.ItemGuid == oid)
            {
                chosen = gear;
                break;
            }
        }
        if (chosen is not { } shopGear)
            return;

        uint qty = LocatePurchaseQty(shopGear);
        var profile = _merchant.Profile;
        if (_gearDealing.TryPurchase(
                _merchant.MerchantIdent,
                shopGear.ItemGuid,
                (int)qty,
                profile.AlternateCurrencyWcid))
        {
            CaptureAlternateCurrencyPurchase(
                profile,
                CalculateShopGearPrice(shopGear, (int)qty));
        }
    }

    private bool TrySeekShopGear(uint oid, out VendorWare shopGear)
    {
        foreach (VendorWare gear in _merchant.Items)
        {
            if (gear.ItemGuid == oid)
            {
                shopGear = gear;
                return true;
            }
        }
        shopGear = default;
        return false;
    }

    private void AppendChosenToPurchaseRoster()
    {
        if (_pick.ChosenObjectTag is not { } oid || !TrySeekShopGear(oid, out VendorWare shopGear))
            return;

        uint qty = LocatePurchaseQty(shopGear);
        if (_purchaseLoading.Add(shopGear.ItemGuid, (int)qty) == VendorTrayAddOutcome.Capped)
            _sysMsg?.Invoke(VendorTray.TooMuchMsg);
    }

    private static int PurchaseLoadingDeletionQuantity(VendorWare gear) =>
        (gear.MaxStackSize ?? 1) > 1 ? -1 : 1;

    private void PurchaseGearBtnPressed()
    {
        if (_pick.ChosenObjectTag is not { } oid || !TrySeekShopGear(oid, out VendorWare shopGear))
            return;

        uint qty = LocatePurchaseQty(shopGear);
        if (_gearDealing.TryPurchase(
                _merchant.MerchantIdent,
                shopGear.ItemGuid,
                (int)qty,
                _merchant.Profile.AlternateCurrencyWcid))
        {
            CaptureAlternateCurrencyPurchase(
                _merchant.Profile,
                CalculateShopGearPrice(shopGear, (int)qty));
            _purchaseLoading.Remove(shopGear.ItemGuid, PurchaseLoadingDeletionQuantity(shopGear));
        }
    }

    private void PurchaseAllBtnPressed()
    {
        if (_purchaseLoading.IsEmpty)
            return;

        var gearList = new List<(int Amount, uint ItemGuid)>(_purchaseLoading.Listings.Count);
        foreach (VendorTrayRow listing in _purchaseLoading.Listings)
            gearList.Add((listing.Quantity, listing.ItemGuid));

        var profile = _merchant.Profile;
        int transactionVal = CalculatePurchaseTransactionVal();

        if (profile.AlternateCurrencyWcid is 0u)
        {
            int avatarSum = _objects.Get(_avatarOid())?.Properties.FetchInt((uint)TraitInt.CoinValue) ?? 0;
            if (transactionVal > avatarSum)
            {
                _sysMsg?.Invoke(NotEnoughMoneyMsg);
                return;
            }
        }
        else if (transactionVal > LocateAlternateCurrencyQuantity(profile))
        {
            _sysMsg?.Invoke(NotEnoughMoneyMsg);
            return;
        }

        (int gearSocketsNeeded, int vesselSocketsNeeded) = CalculatePurchaseSocketsNeeded(gearList);
        var avatar = _objects.Get(_avatarOid());
        (int gearListConsumed, int vesselsConsumed) = TallyAvatarInsides();

        int releaseVesselSockets = (avatar?.ContainersCapacity ?? 0) - vesselsConsumed;
        if (vesselSocketsNeeded > releaseVesselSockets)
        {
            _sysMsg?.Invoke(NotEnoughHallMsg);
            return;
        }
        int releaseGearSockets = (avatar?.ItemsCapacity ?? 0) - gearListConsumed;
        if (gearSocketsNeeded > releaseGearSockets)
        {
            _sysMsg?.Invoke(NotEnoughHallMsg);
            return;
        }

        if (_gearDealing.TryPurchaseAll(_merchant.MerchantIdent, gearList, profile.AlternateCurrencyWcid))
        {
            CaptureAlternateCurrencyPurchase(profile, transactionVal);
            _purchaseLoading.Clear();
        }
    }

    private int CalculatePurchaseTransactionVal()
    {
        var profile = _merchant.Profile;
        int sum = 0;
        foreach (VendorTrayRow listing in _purchaseLoading.Listings)
        {
            if (!TrySeekShopGear(listing.ItemGuid, out VendorWare gear))
                continue;
            int perUnit = VendorPriceRules.PerUnitVal(gear.Value ?? 0, gear.DescStackSize);
            sum += VendorPriceRules.SellPrice(perUnit, gear.ItemType ?? 0u, profile.SellPrice, listing.Quantity);
        }
        return sum;
    }

    private int CalculateVendTransactionVal()
    {
        var profile = _merchant.Profile;
        int sum = 0;
        foreach (VendorTrayRow listing in _vendLoading.Listings)
        {
            if (_objects.Get(listing.ItemGuid) is not { } gear)
                continue;
            int perUnit = VendorPriceRules.PerUnitVal(gear.Value, gear.StackSize);
            sum += VendorPriceRules.BuyPrice(perUnit, (uint)gear.Type, profile.BuyPrice, listing.Quantity);
        }
        return sum;
    }

    private static string AssembleTransactionRosterPhrase(
        string verb, int tally, int sumVal, VendorCatalogue profile)
    {
        string noun = tally is 1 ? "item" : "items";
        return profile.AlternateCurrencyWcid is not 0u
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} {2} worth {3} {4}",
                verb,
                tally,
                noun,
                sumVal,
                profile.AlternateCurrencyPluralName)
            : string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} worth {3}p",
            verb,
            tally,
            noun,
            sumVal.ToString("N0", CultureInfo.InvariantCulture));
    }

    private string AssemblePursePhrase(VendorCatalogue profile)
    {
        if (profile.AlternateCurrencyWcid is not 0u)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "You have {0} {1}.",
                LocateAlternateCurrencyQuantity(profile),
                profile.AlternateCurrencyPluralName);
        }
        int avatarSum = _objects.Get(_avatarOid())?.Properties.FetchInt((uint)TraitInt.CoinValue) ?? 0;
        return string.Format(
            CultureInfo.InvariantCulture,
            "You have {0}p",
            avatarSum.ToString("N0", CultureInfo.InvariantCulture));
    }

    private void RefreshPurchaseTransactionPhrase()
    {
        if (_purchaseRosterPhrase is null && _purchasePursePhrase is null)
            return;

        var profile = _merchant.Profile;
        int tally = _purchaseLoading.Listings.Sum(e => e.Quantity);
        int sumVal = CalculatePurchaseTransactionVal();
        if (_purchaseRosterPhrase is not null)
            AssignPlainPhrase(_purchaseRosterPhrase, AssembleTransactionRosterPhrase("Buying", tally, sumVal, profile));
        if (_purchasePursePhrase is not null)
            AssignPlainPhrase(_purchasePursePhrase, AssemblePursePhrase(profile));
    }

    private void RefreshVendTransactionPhrase()
    {
        if (_vendRosterPhrase is null && _vendPursePhrase is null)
            return;

        var profile = _merchant.Profile;
        int tally = _vendLoading.Listings.Sum(e => e.Quantity);
        int sumVal = CalculateVendTransactionVal();
        if (_vendRosterPhrase is not null)
            AssignPlainPhrase(_vendRosterPhrase, AssembleTransactionRosterPhrase("Selling", tally, sumVal, profile));
        if (_vendPursePhrase is not null)
            AssignPlainPhrase(_vendPursePhrase, AssemblePursePhrase(profile));
    }

    private void OnObjectMoneyAltered(ClientThing updated)
    {
        TryLocateQueuedMerchantDivide(updated);
        if (updated.ObjectId != _avatarOid())
            return;
        RenewMoneyPhrase();
    }

    private void OnObjectAdded(ClientThing gear)
    {
        TryLocateQueuedMerchantDivide(gear);
        if (IsLatestAlternateCurrency(gear)
            && _objects.IsPossessedByObject(gear.ObjectId, _avatarOid()))

            SettleAlternateCurrencySatchel();
    }

    private void OnPileDimsUpdated(ClientThing gear)
    {
        if (IsLatestAlternateCurrency(gear)
            && _objects.IsPossessedByObject(gear.ObjectId, _avatarOid()))

            SettleAlternateCurrencySatchel();
    }

    private void OnObjectMoved(ObjectRelocation relocate)
    {
        if (relocate.Item is not { } gear)
            return;

        TryLocateQueuedMerchantDivide(gear);
        if (!IsLatestAlternateCurrency(gear))
            return;

        SettleAlternateCurrencySatchel();
    }

    private void SettleAlternateCurrencySatchel()
    {
        _alternateCurrencySatchelObserved = true;
        _previousAlternateCurrencyPurchase = 0;
        RenewMoneyPhrase();
    }

    private void RenewMoneyPhrase()
    {
        RefreshPurchaseTransactionPhrase();
        RefreshVendTransactionPhrase();
        RenewPickReadout();
    }

    private bool IsLatestAlternateCurrency(ClientThing gear)
    {
        uint wcid = _merchant.Profile.AlternateCurrencyWcid;
        return wcid is not 0u && gear.WeenieClassIdent == wcid;
    }

    private int LocateAlternateCurrencyQuantity(VendorCatalogue profile)
    {
        if (profile.AlternateCurrencyWcid is 0u)
            return 0;

        long online = 0;
        bool located = false;
        foreach (ClientThing gear in _objects.Objects)
        {
            if (gear.WeenieClassIdent != profile.AlternateCurrencyWcid
                || !_objects.IsPossessedByObject(gear.ObjectId, _avatarOid()))

                continue;
            located = true;
            online += Math.Max(1, gear.StackSize);
        }

        long baseline = located || _alternateCurrencySatchelObserved
            ? online
            : profile.AlternateCurrencyAmount;
        return (int)Math.Clamp(
            baseline - _previousAlternateCurrencyPurchase,
            0L,
            int.MaxValue);
    }

    private void CaptureAlternateCurrencyPurchase(VendorCatalogue profile, int price)
    {
        if (profile.AlternateCurrencyWcid is 0u || price <= 0)
            return;
        _previousAlternateCurrencyPurchase = price;
        RenewMoneyPhrase();
    }

    private void RestartAlternateCurrencyTracking()
    {
        _previousAlternateCurrencyPurchase = 0;
        uint wcid = _merchant.Profile.AlternateCurrencyWcid;
        _alternateCurrencySatchelObserved = wcid is not 0u
            && _objects.Objects.Any(gear =>
                gear.WeenieClassIdent == wcid
                && _objects.IsPossessedByObject(gear.ObjectId, _avatarOid()));
    }

    private (int ItemSlots, int ContainerSlots) CalculatePurchaseSocketsNeeded(
        IReadOnlyList<(int Amount, uint ItemGuid)> gearList)
    {
        int gearSockets = 0, vesselSockets = 0;
        foreach ((int quantity, uint oid) in gearList)
        {
            if (!TrySeekShopGear(oid, out VendorWare gear))
                continue;
            bool isVessel = ((gear.ItemType ?? 0u) & (uint)GearKind.Container) is not 0u;
            bool stackable = (gear.MaxStackSize ?? 1) > 1;
            if (stackable)
            {
                if (isVessel) vesselSockets += 1; else gearSockets += 1;
            }
            else
            {
                if (isVessel) vesselSockets += quantity; else gearSockets += quantity;
            }
        }
        return (gearSockets, vesselSockets);
    }

    private (int Items, int Containers) TallyAvatarInsides()
    {
        int gearList = 0, vessels = 0;
        foreach (uint oid in _objects.FetchInsides(_avatarOid()))
        {
            var objRef = _objects.Get(oid);
            bool isVessel = objRef is not null
                && (objRef.VesselKindHint is not 0u || (objRef.Type & GearKind.Container) != 0);
            if (isVessel) ++vessels; else ++gearList;
        }
        return (gearList, vessels);
    }

    private void PurchaseWipeGearBtnPressed()
    {
        if (_pick.ChosenObjectTag is not { } oid)
            return;

        int quantity = TrySeekShopGear(oid, out VendorWare shopGear)
            ? PurchaseLoadingDeletionQuantity(shopGear)
            : -1;
        _purchaseLoading.Remove(oid, quantity);
    }

    private void VendGearBtnPressed()
    {
        if (_pick.ChosenObjectTag is not { } oid || _objects.Get(oid) is not { } gear)
            return;

        uint wholePile = (uint)Math.Max(1, gear.StackSize);
        if (wholePile > 1)
        {
            uint online = _divideQty.FetchObjectDivideDims(oid, oid, wholePile);
            if (online < wholePile)
            {
                _sysMsg?.Invoke(CannotVendPartialPileMsg);
                return;
            }
        }

        if (_gearDealing.TryVend(_merchant.MerchantIdent, new[] { (1, guid: oid) }))
            _vendLoading.Remove(oid, -1);
    }
}
