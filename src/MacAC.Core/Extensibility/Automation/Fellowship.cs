namespace MacAC.Extensibility.Automation;

/// <summary>One authoritative roster entry plus its live range from the player.</summary>
public readonly record struct FellowEntry(
    uint ObjectId,
    string Name,
    uint CurrentHealth,
    uint MaxHealth,
    uint CurrentStamina,
    uint MaxStamina,
    uint CurrentMana,
    uint MaxMana,
    float Distance)
{
    public bool PortionLoot { get; init; }
}

public enum FellowshipOutcome
{
    Unavailable = 0,
    Accepted,
    Rejected,
}

public readonly record struct FellowshipVerdict(FellowshipOutcome Status)
{
    public bool Accepted => Status == FellowshipOutcome.Accepted;
}

public interface IFellowshipControls
{
    bool IsInFellowship => false;

    string Name => string.Empty;

    uint LeaderObjectIdent => 0u;

    bool IsOpen => false;

    bool IsBolted => false;

    int MemberCount => 0;

    IReadOnlyList<FellowEntry> GrabParticipants() => Array.Empty<FellowEntry>();

    IReadOnlyList<FellowEntry> CaptureRoster() => GrabParticipants();

    FellowshipVerdict Create(string label, bool portionExperience) =>
        new(FellowshipOutcome.Unavailable);

    FellowshipVerdict Recruit(uint markObjectIdent) => new(FellowshipOutcome.Unavailable);

    FellowshipVerdict Dismiss(uint markObjectIdent) => new(FellowshipOutcome.Unavailable);

    FellowshipVerdict Quit(bool disband) => new(FellowshipOutcome.Unavailable);

    FellowshipVerdict AssignLeader(uint markObjectIdent) => new(FellowshipOutcome.Unavailable);

    FellowshipVerdict AssignOpen(bool isOpen) => new(FellowshipOutcome.Unavailable);
}
