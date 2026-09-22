using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

/// <summary>Game actions: Allegiance, fellowship and house administration.</summary>
public sealed partial class RealmSession
{
    public void TransmitWarpToHouse() =>
        Act(seq => CommandAsks.AssembleHouseRecall(seq));

    public void TransmitHouseAsk() =>
        Act(seq => CommandAsks.AssembleHouseAsk(seq));

    public void TransmitRecallAllegianceHometown() =>
        Act(seq => CommandAsks.AssembleRecallAllegianceHometown(seq));

    public void TransmitAllegianceDetailsReq(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleAllegianceDetailsReq(seq, avatarLabel));

    public void TransmitBreakAllegianceBoot(string avatarLabel, bool acctBoot)
    {
        Act(seq => CommandAsks.AssembleBreakAllegianceBoot(seq, avatarLabel, acctBoot));
    }

    public void TransmitAllegianceCommsBoot(string avatarLabel, string cause)
    {
        Act(seq => CommandAsks.AssembleAllegianceCommsBoot(seq, avatarLabel, cause));
    }

    public void TransmitAllegianceCommsGag(string avatarLabel, bool turnedOn)
    {
        Act(seq => CommandAsks.AssembleAllegianceCommsGag(seq, avatarLabel, turnedOn));
    }

    public void TransmitAppendAllegianceBan(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleAppendAllegianceBan(seq, avatarLabel));

    public void TransmitDropAllegianceBan(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleDropAllegianceBan(seq, avatarLabel));

    public void TransmitRosterAllegianceBans() =>
        Act(seq => CommandAsks.AssembleRosterAllegianceBans(seq));

    public void TransmitSetAllegianceOfficer(string avatarLabel, uint tier)
    {
        Act(seq => CommandAsks.AssembleSetAllegianceOfficer(seq, avatarLabel, tier));
    }

    public void TransmitDropAllegianceOfficer(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleDropAllegianceOfficer(seq, avatarLabel));

    public void TransmitRosterAllegianceOfficers() =>
        Act(seq => CommandAsks.AssembleRosterAllegianceOfficers(seq));

    public void TransmitWipeAllegianceOfficers() =>
        Act(seq => CommandAsks.AssembleWipeAllegianceOfficers(seq));

    public void TransmitSetAllegianceOfficerBanner(uint tier, string banner)
    {
        Act(seq => CommandAsks.AssembleSetAllegianceOfficerBanner(seq, tier, banner));
    }

    public void TransmitRosterAllegianceOfficerBanners() =>
        Act(seq => CommandAsks.AssembleRosterAllegianceOfficerBanners(seq));

    public void TransmitWipeAllegianceOfficerBanners() =>
        Act(seq => CommandAsks.AssembleWipeAllegianceOfficerBanners(seq));

    public void TransmitAskAllegianceLabel() =>
        Act(seq => CommandAsks.AssembleAskAllegianceLabel(seq));

    public void TransmitSetAllegianceLabel(string label) =>
        Act(seq => CommandAsks.AssembleSetAllegianceLabel(seq, label));

    public void TransmitWipeAllegianceLabel() =>
        Act(seq => CommandAsks.AssembleWipeAllegianceLabel(seq));

    public void TransmitAllegianceLockAct(uint act) =>
        Act(seq => CommandAsks.AssembleAllegianceLockAct(seq, act));

    public void TransmitSetAllegianceApprovedVassal(string avatarLabel)
    {
        Act(seq => CommandAsks.AssembleSetAllegianceApprovedVassal(seq, avatarLabel));
    }

    public void TransmitAllegianceHouseAct(uint act) =>
        Act(seq => CommandAsks.AssembleAllegianceHouseAct(seq, act));

    public void TransmitAskMotd() =>
        Act(seq => CommandAsks.AssembleAskMotd(seq));

    public void TransmitSetMotd(string motd) =>
        Act(seq => CommandAsks.AssembleSetMotd(seq, motd));

    public void TransmitWipeMotd() =>
        Act(seq => CommandAsks.AssembleWipeMotd(seq));

    public void TransmitSetOpenHouseCondition(bool isOpen) =>
        Act(seq => CommandAsks.AssembleSetOpenHouseCondition(seq, isOpen));

    public void TransmitAppendPermanentGuest(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleAppendPermanentGuest(seq, avatarLabel));

    public void TransmitDropPermanentGuest(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleDropPermanentGuest(seq, avatarLabel));

    public void TransmitDropAllPermanentGuests() =>
        Act(seq => CommandAsks.AssembleDropAllPermanentGuests(seq));

    public void TransmitEditDepotPermission(string avatarLabel, bool turnedOn)
    {
        Act(seq => CommandAsks.AssembleEditDepotPermission(seq, avatarLabel, turnedOn));
    }

    public void TransmitAppendAllDepotPermission() =>
        Act(seq => CommandAsks.AssembleAppendAllDepotPermission(seq));

    public void TransmitDropAllDepotPermission() =>
        Act(seq => CommandAsks.AssembleDropAllDepotPermission(seq));

    public void TransmitReqWholeGuestRoster() =>
        Act(seq => CommandAsks.AssembleReqWholeGuestRoster(seq));

    public void TransmitBootSpecificHouseGuest(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleBootSpecificHouseGuest(seq, avatarLabel));

    public void TransmitBootEveryone() =>
        Act(seq => CommandAsks.AssembleBootEveryone(seq));

    public void TransmitSetTapsVis(bool shown) =>
        Act(seq => CommandAsks.AssembleSetTapsVis(seq, shown));

    public void TransmitModifyAllegianceGuestPermission(bool turnedOn)
    {
        Act(seq => CommandAsks.AssembleModifyAllegianceGuestPermission(seq, turnedOn));
    }

    public void TransmitModifyAllegianceDepotPermission(bool turnedOn)
    {
        Act(seq => CommandAsks.AssembleModifyAllegianceDepotPermission(seq, turnedOn));
    }

    public void TransmitFellowshipBuild(string fellowshipLabel, bool portionXp)
    {
        Act(seq => SocialMoves.AssembleFellowshipBuild(seq, fellowshipLabel, portionXp));
    }

    public void TransmitFellowshipQuit(bool disband) =>
        Act(seq => SocialMoves.AssembleFellowshipQuit(seq, disband));

    public void TransmitFellowshipDismiss(uint markOid) =>
        Act(seq => SocialMoves.AssembleFellowshipDismiss(seq, markOid));

    public void TransmitFellowshipRecruit(uint markOid) =>
        Act(seq => SocialMoves.AssembleFellowshipRecruit(seq, markOid));

    public void TransmitFellowshipRefreshReq(bool boardOpen) =>
        Act(seq => SocialMoves.AssembleFellowshipRefreshReq(seq, boardOpen));

    public void TransmitFellowshipAssignNewLeader(uint newLeaderOid)
    {
        Act(seq => SocialMoves.AssembleFellowshipAssignNewLeader(seq, newLeaderOid));
    }

    public void TransmitFellowshipEditOpenness(bool isOpen) =>
        Act(seq => SocialMoves.AssembleFellowshipEditOpenness(seq, isOpen));

    public void TransmitAllegianceSwear(uint patronOid) =>
        Act(seq => AllegianceAsks.AssembleSwear(seq, patronOid));

    public void TransmitAllegianceBreak(uint markOid) =>
        Act(seq => AllegianceAsks.AssembleBreak(seq, markOid));

    public void TransmitAllegianceKick(uint vassalOid) =>
        Act(seq => AllegianceAsks.AssembleKick(seq, vassalOid));

    public void TransmitAllegianceRefreshReq(bool on) =>
        Act(seq => AllegianceAsks.AssembleAllegianceRefreshReq(seq, on));

    public void TransmitRosterOnHandHouses(uint houseKind) =>
        Act(seq => CommandAsks.AssembleRosterOnHandHouses(seq, houseKind));

    public void TransmitAppendAvatarPermission(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleAppendAvatarPermission(seq, avatarLabel));

    public void TransmitDropAvatarPermission(string avatarLabel) =>
        Act(seq => CommandAsks.AssembleDropAvatarPermission(seq, avatarLabel));

    public void TransmitAbandonHouse() =>
        Act(seq => CommandAsks.AssembleAbandonHouse(seq));
}
