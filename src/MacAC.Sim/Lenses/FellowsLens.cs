namespace MacAC.Sim;

public readonly record struct SimFellowMemberCapture(
    uint Guid,
    string Name,
    uint Level,
    uint MaxHealth,
    uint MaxStamina,
    uint MaxMana,
    uint CurrentHealth,
    uint CurrentStamina,
    uint CurrentMana,
    bool ShareLoot);

public readonly record struct SimFellowsCapture(
    long Revision,
    bool IsInFellowship,
    string Name,
    uint LeaderGuid,
    bool ShareXp,
    bool EvenXpSplit,
    bool IsOpen,
    bool Locked,
    int MemberCount)
{
    public SimFellowsCapture()
        : this(0, false, string.Empty, 0u, false, false, false, false, 0)
    {
    }
}

public interface ISimFellowsLens
{
    SimFellowsCapture Snapshot { get; }

    bool TryFetchMember(uint oid, out SimFellowMemberCapture participant);

    IEnumerable<SimFellowMemberCapture> FetchParticipants();
}
