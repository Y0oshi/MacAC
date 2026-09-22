using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

public static partial class GameEventBindings
{
    private sealed partial class Binder
    {
        public void AttachSatchel()
        {
            On(GameEventKind.WieldObject, PlaySignals.DecodeWieldObject, p =>
                Items.ImposeConfirmedSrvWield(p.ItemGuid, PlayerGuid?.Invoke() ?? 0u, (WieldBitmask)p.EquipLoc));
            On(GameEventKind.InventoryPutObjInContainer, PlaySignals.DecodePutObjRefInVessel, container =>
                Items.ImposeConfirmedSrvRelocate(container.ItemGuid, container.ContainerGuid, newWielderIdent: 0u, newSocket: (int)container.Placement, vesselKindHint: container.ContainerType));
            On(GameEventKind.HouseUpdateRestrictions, PlaySignals.DecodeHouseRefreshRestrictions, container =>
                Items.RefreshHouseRestrictions(container.SenderId, container.Restrictions));
            On(GameEventKind.ApproachVendor, VendorOpen.TryParse, OpenMerchant);
            On(GameEventKind.ViewContents, PlaySignals.DecodeLensInsides, container =>
            {
                Items.ReplaceContents(container.ContainerGuid, Ranks(container.Items, static rank => new ContainerSlotRow(rank.Guid, rank.ContainerType)));
                ExternalContainers?.ImposeLensInsides(container.ContainerGuid);
            });
            On(GameEventKind.InventoryPutObjectIn3D, PlaySignals.DecodePutObjectIn3D, oid =>
                Items.ImposeConfirmedSrvRelocate(oid, newVesselIdent: 0u, newWielderIdent: 0u));
            On(GameEventKind.InventoryServerSaveFailed, PlaySignals.DecodeSatchelSrvPersistFailed, OnRelocateRejected);
            On(GameEventKind.UseDone, PlaySignals.DecodeUseDone, OnUseFinished);
            On(GameEventKind.SalvageOperationsResult, PlaySignals.DecodeSalvageOpsOutcome, OnSalvaged);
            On(GameEventKind.CloseGroundContainer, PlaySignals.DecodeShutTerrainVessel, oid =>
            {
                ExternalContainers?.ImposeShut(oid);
                Items.HaltViewingInsidesTree(oid);
            });
            On(GameEventKind.IdentifyObjectResponse, AppraisalReader.TryParse, OnAppraised);
        }

        private static TRow[] Ranks<TIn, TRow>(IReadOnlyList<TIn> src, Func<TIn, TRow> craft)
        {
            TRow[] ranks = new TRow[src.Count];
            for (int idx = 0; idx < ranks.Length; ++idx)
                ranks[idx] = craft(src[idx]);
            return ranks;
        }

        private void OpenMerchant(VendorOpen.Parsed parsed)
        {
            VendorCatalogue catalogue = new VendorCatalogue(
                parsed.Profile.MerchandiseItemTypes,
                parsed.Profile.MerchandiseMinValue,
                parsed.Profile.MerchandiseMaxValue,
                parsed.Profile.DealMagicalItems,
                parsed.Profile.BuyPrice,
                parsed.Profile.SellPrice,
                parsed.Profile.AlternateCurrencyWcid,
                parsed.Profile.AlternateCurrencyAmount,
                parsed.Profile.AlternateCurrencyPluralName);

            var wares = Ranks(parsed.Items, static gear => new VendorWare(
                gear.ItemGuid,
                gear.StackSize,
                gear.Desc.WeenieClassId,
                gear.Desc.Name,
                gear.Desc.ItemType,
                gear.Desc.IconId,
                gear.Desc.Value,
                gear.Desc.StackSize,
                gear.Desc.StackSizeMax,
                gear.Desc.IconUnderlayId,
                gear.Desc.IconOverlayId,
                gear.Desc.UiEffects,
                gear.Desc.PluralName));

            Vendor?.Apply(parsed.VendorGuid, catalogue, wares);
        }

        // The server rejected an optimistic move: snap the item back and log what we knew about it
        private void OnRelocateRejected(PlaySignals.SatchelSrvPersistBotched failed)
        {
            ClientThing? gear = Items.Get(failed.ItemGuid);
            string recognized = gear is null
                ? "unknown"
                : $"'{gear.Name}' valid=0x{(uint)gear.ValidLocations:X8} equip=0x{(uint)gear.CurrentlyEquippedLocale:X8} priority=0x{gear.Priority:X8} container=0x{gear.VesselTag:X8} wielder=0x{gear.WielderIdent:X8}";
            bool rolledBack = Items.RejectRelocate(failed.ItemGuid, failed.WeenieError);
            Console.WriteLine($"[B-Drag] SatchelSrvPersistBotched guid=0x{failed.ItemGuid:X8} err=0x{failed.WeenieError:X} rolledBack={rolledBack} item={recognized}");
        }

        private void OnUseFinished(uint problem)
        {
            Console.WriteLine($"[use-done] err=0x{problem:X4}");
            OnUseDone?.Invoke(problem);
            if (problem is 0 || WeenieErrorText.IsSilentClientControlCondition(problem))
                return;
            (string? phrase, CanonLogTextType kind) = WeenieErrorText.Resolve(problem, null);
            if (phrase is not null)
                Say(phrase, kind);
        }

        private void OnSalvaged(PlaySignals.SalvageOperationsOutcome outcome)
        {
            if (outcome.Results.Count is not 0)
                Say(ComposeSalvageOutcomes(outcome), CanonLogTextType.Salvaging);

            if (outcome.UnsuitableItemGuids.Count is not 0)
            {
                string labels = string.Join(", ", outcome.UnsuitableItemGuids.Select(ident => Items.Get(ident)?.FetchAppropriateLabel() ?? "item"));
                Say($" The following were not suitable for salvaging: {labels}", CanonLogTextType.Salvaging);
            }

            if (outcome.Results.Count is 0 && outcome.UnsuitableItemGuids.Count is 0)
                Say("Salvaging Failed!", CanonLogTextType.Salvaging);
        }

        private void OnAppraised(AppraisalReader.WireParsed parsed)
        {
            if (parsed.Success && Items.Get(parsed.Guid) is not null)
                Items.RefreshAppraisal(parsed.Guid, parsed.Properties, parsed.SpellBook, ClientMoment());
            if (parsed.CreatureProfile is { HealthMax: > 0u } beast)
                Combat.OnRefreshHealth(parsed.Guid, Math.Clamp((float)beast.Health / beast.HealthMax, 0f, 1f));
            OnAppraisal?.Invoke(parsed);
        }
    }
}
