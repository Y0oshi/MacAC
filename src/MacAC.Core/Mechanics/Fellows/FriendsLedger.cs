namespace MacAC.Mechanics.Fellows;

public sealed record FriendRow(
    uint Id,
    string Name,
    bool Online,
    bool AppearOffline,
    IReadOnlyList<uint> Friends,
    IReadOnlyList<uint> FriendOf);

public enum FriendsDeltaKind : uint
{
    Full = 0,
    Add = 1,
    Remove = 2,
    RemoveSilent = 3,
    OnlineStatus = 4,
}

public sealed record FriendsDelta(
    FriendsDeltaKind Type,
    IReadOnlyList<FriendRow> Entries);

public sealed class FriendsLedger
{
    private readonly Lock _synchronize = new();
    private readonly List<FriendRow> _ranks = [];
    private long _rev;

    public int Count
    {
        get
        {
            lock (_synchronize)
                return _ranks.Count;
        }
    }

    public long Revision => Interlocked.Read(ref _rev);

    public IReadOnlyList<FriendRow> Snapshot()
    {
        lock (_synchronize)
            return _ranks.ToArray();
    }

    public bool TryGet(uint toonIdent, out FriendRow? friend)
    {
        lock (_synchronize)
        {
            friend = _ranks.Find(rank => rank.Id == toonIdent);
            return friend is not null;
        }
    }

    public void Apply(FriendsDelta refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        lock (_synchronize)
        {
            switch (refresh.Type)
            {
                case FriendsDeltaKind.Full:
                    _ranks.Clear();
                    _ranks.AddRange(refresh.Entries);
                    break;

                case FriendsDeltaKind.Add:
                    foreach (FriendRow rank in refresh.Entries)
                        Upsert(rank, slotWhenAbsent: true);
                    break;

                case FriendsDeltaKind.Remove:
                case FriendsDeltaKind.RemoveSilent:
                    foreach (FriendRow rank in refresh.Entries)
                        _ranks.RemoveAll(extant => extant.Id == rank.Id);
                    break;

                case FriendsDeltaKind.OnlineStatus:
                    foreach (FriendRow rank in refresh.Entries)
                        Upsert(rank, slotWhenAbsent: false);
                    break;
            }

            Interlocked.Increment(ref _rev);
        }
    }

    public void Clear()
    {
        lock (_synchronize)
        {
            _ranks.Clear();
            Interlocked.Increment(ref _rev);
        }
    }

    private void Upsert(FriendRow rank, bool slotWhenAbsent)
    {
        int at = _ranks.FindIndex(extant => extant.Id == rank.Id);
        if (at >= 0)
            _ranks[at] = rank;
        else if (slotWhenAbsent)
            _ranks.Add(rank);
    }
}
