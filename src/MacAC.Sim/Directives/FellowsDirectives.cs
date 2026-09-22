namespace MacAC.Sim;

public interface ISimFellowsDirectives
{
    SimDirectiveResult Create(SimEpochTicket anticipatedGen, string fellowshipLabel, bool portionXp);

    SimDirectiveResult Recruit(SimEpochTicket anticipatedGen, uint markOid);

    SimDirectiveResult Dismiss(SimEpochTicket anticipatedGen, uint markOid);

    SimDirectiveResult Quit(SimEpochTicket anticipatedGen, bool disband);

    SimDirectiveResult AssignLeader(SimEpochTicket anticipatedGen, uint newLeaderOid);

    SimDirectiveResult SetOpen(SimEpochTicket anticipatedGen, bool isOpen);

    SimDirectiveResult ApplyBoardOpen(SimEpochTicket anticipatedGen, bool boardOpen);
}

public interface ISimAllegianceDirectives
{
    SimDirectiveResult Swear(SimEpochTicket anticipatedGen, uint patronOid);

    SimDirectiveResult Break(SimEpochTicket anticipatedGen, uint markOid);

    SimDirectiveResult Kick(SimEpochTicket anticipatedGen, uint vassalOid);

    /// <summary><c>0x027B</c> - empty name queries self.</summary>
    SimDirectiveResult ReqDetails(SimEpochTicket anticipatedGen, string avatarLabel);

    /// <summary><c>0x001F</c> - the allegiance panel's subscribe/unsubscribe toggle.</summary>
    SimDirectiveResult AssignRefreshSubscription(SimEpochTicket anticipatedGen, bool on);
}
