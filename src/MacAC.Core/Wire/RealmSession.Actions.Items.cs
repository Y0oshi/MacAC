using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

/// <summary>Game actions: Inventory, trade, vendor and item queries.</summary>
public sealed partial class RealmSession
{
    public void TransmitAppendShortcut(HotbarSlot listing) =>
        Act(seq => InventoryMoves.AssembleAppendShortcut(seq, listing));

    public void TransmitDropShortcut(uint ordinal) =>
        Act(seq => InventoryMoves.AssembleDropShortcut(seq, ordinal));

    public void TransmitDiscardGear(uint gearOid) =>
        Act(seq => InventoryMoves.AssembleDiscardGear(seq, gearOid));

    public void TransmitOpenBarterNegotiations(uint partnerOid) =>
        Act(seq => TradeAsks.AssembleOpenBarterNegotiations(seq, partnerOid));

    public void TransmitShutBarterNegotiations() =>
        Act(seq => TradeAsks.AssembleShutBarterNegotiations(seq));

    public void TransmitAppendToBarter(uint gearOid, uint barterSocket = 0u) =>
        Act(seq => TradeAsks.AssembleAppendToBarter(seq, gearOid, barterSocket));

    public void TransmitAdmitBarter(
        uint partnerOid,
        double barterStamp,
        uint barterCondition,
        uint initiatorOid,
        bool initiatorAccepts,
        bool partnerAccepts)
    {
        Act(seq => TradeAsks.AssembleAdmitBarter(seq, partnerOid, barterStamp, barterCondition, initiatorOid, initiatorAccepts, partnerAccepts));
    }

    public void TransmitDeclineBarter() =>
        Act(seq => TradeAsks.AssembleDeclineBarter(seq));

    public void TransmitRestartBarter() =>
        Act(seq => TradeAsks.AssembleRestartBarter(seq));

    public void TransmitHandObject(uint markOid, uint gearOid, uint quantity)
    {
        Act(seq => InventoryMoves.AssembleHandObjectReq(seq, markOid, gearOid, quantity));
    }

    public void TransmitFetchAndWieldGear(uint gearOid, uint wieldBitmask)
    {
        Act(seq => InventoryMoves.AssembleFetchAndWieldGear(seq, gearOid, wieldBitmask));
    }

    public void TransmitNoLongerViewingInsides(uint vesselOid)
    {
        Act(seq => InventoryMoves.AssembleNoLongerViewingInsides(seq, vesselOid));
    }

    public void SendUse(uint markOid) =>
        Act(seq => InteractAsks.AssembleUse(seq, markOid));

    public void TransmitUseWithMark(uint srcOid, uint markOid) =>
        Act(seq => InteractAsks.AssembleUseWithMark(seq, srcOid, markOid));

    public void SendBuy(uint merchantOid, uint gearOid, int quantity, uint alternateCurrencyIdent)
    {
        Act(seq => VendorAsks.BuildBuy(seq, merchantOid, quantity, gearOid, alternateCurrencyIdent));
    }

    public void SendBuy(uint merchantOid, IReadOnlyList<(int Amount, uint ItemGuid)> gearList, uint alternateCurrencyIdent)
    {
        Act(seq => VendorAsks.BuildBuy(seq, merchantOid, gearList, alternateCurrencyIdent));
    }

    public void TransmitVend(uint merchantOid, IReadOnlyList<(int Amount, uint ItemGuid)> gearList) =>
        Act(seq => VendorAsks.AssembleVend(seq, merchantOid, gearList));

    public void TransmitEvaluate(uint markOid) =>
        Act(seq => AppraiseAsk.Build(seq, markOid));

    public void TransmitSetInscription(uint gearOid, string inscription)
    {
        Act(seq => InventoryMoves.AssembleSetInscription(seq, gearOid, inscription));
    }

    public void TransmitPutGearInVessel(uint gearOid, uint vesselOid, int stance) =>
        Act(seq => InteractAsks.AssembleChooseUp(seq, gearOid, vesselOid, stance));

    public void TransmitStackableCombine(uint srcOid, uint markOid, uint quantity)
    {
        Act(seq => InventoryMoves.AssembleStackableCombine(seq, srcOid, markOid, quantity));
    }

    public void TransmitStackableDivideToVessel(uint pileOid, uint vesselOid, uint stance, uint quantity)
    {
        Act(seq => InventoryMoves.AssembleStackableDivideToVessel(seq, pileOid, vesselOid, stance, quantity));
    }

    public void TransmitStackableDivideTo3D(uint pileOid, uint quantity)
    {
        Act(seq => InventoryMoves.AssembleStackableDivideTo3D(seq, pileOid, quantity));
    }

    public void TransmitSalvage(uint toolOid, IReadOnlyList<uint> gearOids)
    {
        Act(seq => InventoryMoves.AssembleBuildTinkeringTool(seq, toolOid, gearOids));
    }

    public void TransmitAskHealth(uint markOid) =>
        Act(seq => SocialMoves.AssembleAskHealth(seq, markOid));

    public void TransmitAskGearMana(uint gearOid) =>
        Act(seq => SocialMoves.AssembleAskGearMana(seq, gearOid));
}
