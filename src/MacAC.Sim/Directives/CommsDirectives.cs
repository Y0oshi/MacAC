namespace MacAC.Sim;

public enum SimCommsChannel
{
    Say,
    Tell,
    Fellowship,
    Allegiance,
    Vassals,
    Patron,
    Monarch,
    CoVassals,
    General,
    Trade,
    LookingForGroup,
    Roleplay,
    Society,
    Olthoi,

    AllegianceBroadcast,
}

public readonly record struct SimCommsDirective(SimCommsChannel Channel, string Text, string? TargetName = null);

public interface ISimCommsDirectives
{
    SimDirectiveResult Execute(SimEpochTicket anticipatedGen, in SimCommsDirective directive);
}

public enum SimBuddyDirectiveKind
{
    Add,
    Remove,
    Clear,
    RequestLegacyList,
}

public readonly record struct SimBuddyDirective(SimBuddyDirectiveKind Kind, uint CharacterId = 0u, string? Name = null);

public enum SimMuteScope
{
    Character,
    Account,
    Global,
}

public readonly record struct SimMuteDirective(
    SimMuteScope Scope,
    bool Add,
    uint CharacterId = 0u,
    string? Name = null,
    uint MessageType = 0u);

public interface ISimSocialDirectives
{
    SimDirectiveResult Execute(SimEpochTicket anticipatedGen, in SimBuddyDirective directive);

    SimDirectiveResult Execute(SimEpochTicket anticipatedGen, in SimMuteDirective directive);
}
