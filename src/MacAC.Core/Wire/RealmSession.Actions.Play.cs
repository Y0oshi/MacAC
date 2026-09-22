using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

/// <summary>Game actions: Recalls, character options, spellbook, advancement and combat.</summary>
public sealed partial class RealmSession
{
    public void TransmitWarpToLifestone() =>
        Act(seq => InteractAsks.AssembleTeleToLifestone(seq));

    public void TransmitWarpToMarketplace() =>
        Act(seq => CommandAsks.AssembleMarketplace(seq));

    public void TransmitWarpToPkArena() =>
        Act(seq => CommandAsks.AssemblePkArena(seq));

    public void TransmitWarpToPkLiteArena() =>
        Act(seq => CommandAsks.AssemblePkLiteArena(seq));

    public void TransmitJoinPkLite() =>
        Act(seq => CommandAsks.AssembleJoinPkLite(seq));

    public void TransmitWarpToMansion() =>
        Act(seq => CommandAsks.AssembleMansionRecall(seq));

    public void TransmitAbandonContract(uint contractIdent) =>
        Act(seq => CommandAsks.AssembleAbandonContract(seq, contractIdent));

    public void TransmitAskAge() =>
        Act(seq => CommandAsks.AssembleAskAge(seq));

    public void TransmitAskBirth() =>
        Act(seq => CommandAsks.AssembleAskBirth(seq));

    public void TransmitAckResponse(uint ackKind, uint ctxIdent, bool approved)
    {
        Act(seq => CommandAsks.AssembleAckResponse(seq, ackKind, ctxIdent, approved));
    }

    public void TransmitSuicide() =>
        Act(seq => CommandAsks.AssembleSuicide(seq));

    public void TransmitSetSingleToonKnob(uint knobIdent, bool val)
    {
        Act(seq => SocialMoves.AssembleSetSingleToonKnob(seq, knobIdent, val));
    }

    public void TransmitSetToonKnobs(
        uint options1,
        uint options2,
        IReadOnlyList<HotbarSlot> shortcuts,
        IReadOnlyList<IReadOnlyList<uint>> favoriteArcana,
        IReadOnlyDictionary<uint, uint> wantedModules,
        uint grimoireFilters)
    {
        Act(seq => SocialMoves.AssembleSetToonKnobs(seq, options1, options2, shortcuts, favoriteArcana, wantedModules, grimoireFilters));
    }

    public void TransmitSetBanner(uint bannerIdent) =>
        Act(seq => SocialMoves.AssembleBannerSet(seq, bannerIdent));

    public void TransmitWipeWantedModules()
    {
        Act(seq => CommandAsks.AssembleSetWantedModuleTier(seq, moduleIdent: 0u, quantity: uint.MaxValue));
    }

    public void TransmitSetWantedModuleTier(uint moduleIdent, uint quantity)
    {
        Act(seq => CommandAsks.AssembleSetWantedModuleTier(seq, moduleIdent, quantity));
    }

    public void TransmitAppendArcanumFavorite(uint arcanumIdent, int locus, int tabOrdinal)
    {
        Act(seq => CommandAsks.AssembleAppendArcanumFavorite(seq, arcanumIdent, locus, tabOrdinal));
    }

    public void TransmitDropArcanumFavorite(uint arcanumIdent, int tabOrdinal)
    {
        Act(seq => CommandAsks.AssembleDropArcanumFavorite(seq, arcanumIdent, tabOrdinal));
    }

    public void TransmitGrimoireSift(uint filters) =>
        Act(seq => CommandAsks.AssembleGrimoireSift(seq, filters));

    public void TransmitDropArcanum(uint arcanumIdent) =>
        Act(seq => CommandAsks.AssembleDropArcanum(seq, arcanumIdent));

    public void TransmitCastingUntargetedArcanum(uint arcanumIdent) =>
        Act(seq => CastAsk.AssembleUntargeted(seq, arcanumIdent));

    public void TransmitCastingTargetedArcanum(uint markOid, uint arcanumIdent) =>
        Act(seq => CastAsk.AssembleTargeted(seq, markOid, arcanumIdent));

    public void TransmitChangeCombatMode(FightingManner manner)
    {
        Act(seq => ToonActs.AssembleEditFightingManner(seq, (ToonActs.FightingMode)(uint)manner));
    }

    public void TransmitEmitAttr(uint attrIdent, ulong xpSpent)
    {
        Act(seq => ToonActs.AssembleEmitAttr(seq, attrIdent, xpSpent));
    }

    public void TransmitEmitVital(uint vitalIdent, ulong xpSpent) =>
        Act(seq => ToonActs.AssembleEmitVital(seq, vitalIdent, xpSpent));

    public void TransmitEmitAptitude(uint aptitudeIdent, ulong xpSpent)
    {
        Act(seq => ToonActs.AssembleEmitAptitude(seq, aptitudeIdent, xpSpent));
    }

    public void TransmitTrainAptitude(uint aptitudeIdent, uint credits)
    {
        Act(seq => ToonActs.AssembleTrainAptitude(seq, aptitudeIdent, credits));
    }

    public void TransmitMeleeAssault(uint markOid, AssaultElevation assaultHeight, float strengthTier)
    {
        Act(seq => AttackAsk.AssembleMelee(seq, markOid, (uint)assaultHeight, strengthTier));
    }

    public void TransmitMissileAssault(uint markOid, AssaultElevation assaultHeight, float accuracyTier)
    {
        Act(seq => AttackAsk.AssembleMissile(seq, markOid, (uint)assaultHeight, accuracyTier));
    }

    public void DispatchAbortAssault() =>
        Act(seq => AttackAsk.AssembleAbort(seq));
}
