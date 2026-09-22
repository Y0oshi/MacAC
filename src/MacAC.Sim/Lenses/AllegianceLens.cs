namespace MacAC.Sim;

public readonly record struct SimAllegianceMemberCapture(
    uint CharacterId,
    uint ParentGuid,
    bool IsLoggedIn,
    string Name,
    ushort Rank,
    uint Level,
    ushort Loyalty,
    ushort Leadership,
    uint CpCached,
    uint CpTithed,
    byte Gender,
    byte HeritageGroup,
    bool MayPassupExperience);

public readonly record struct SimAllegianceCapture(
    long Revision,
    bool HasServerSeed,
    bool HasProfile,
    uint Rank,
    uint TotalMembers,
    uint TotalVassals,
    string AllegianceName,
    uint MonarchGuid,
    int RecordCount)
{
    public bool HasMonarch => MonarchGuid is not 0u;

    public SimAllegianceCapture()
        : this(0, false, false, 0u, 0u, 0u, string.Empty, 0u, 0)
    {
    }
}

public interface ISimAllegianceLens
{
    SimAllegianceCapture Snapshot { get; }

    bool TryFetchMonarch(out SimAllegianceMemberCapture monarch);

    bool TryFetchParticipant(uint oid, out SimAllegianceMemberCapture participant);

    bool TryGetPatron(uint oid, out SimAllegianceMemberCapture patron);

    IEnumerable<SimAllegianceMemberCapture> GetVassals(uint oid);
}
