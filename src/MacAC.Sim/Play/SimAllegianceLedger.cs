using MacAC.Wire.Messages;

namespace MacAC.Sim.Play;

public readonly record struct SimAllegianceHoldingCapture(bool IsDisposed, bool HasProfile, int RecordCount)
{
    public bool IsConverged => IsDisposed && !HasProfile && RecordCount is 0;
}

public sealed class SimAllegianceLedger : IDisposable
{
    private readonly object _latch = new();
    private Profile _profile = Profile.Empty;
    private bool _hasSrvSeed;
    private long _rev;
    private bool _destroyed;

    // One server update's worth of allegiance data
    private sealed record Profile(
        CommandReplies.AllegianceMemberRow? Monarch,
        IReadOnlyList<CommandReplies.AllegianceMemberRow> Records,
        string Name,
        uint TotalMembers,
        uint TotalVassals,
        uint Rank,
        bool Present)
    {
        public static readonly Profile Empty = new(null, [], string.Empty, 0u, 0u, 0u, Present: false);

        public bool IsBlank
        {
            get
            {
                return Monarch is null && Records.Count is 0 && Name.Length is 0 && TotalMembers is 0u && TotalVassals is 0u && Rank is 0u && !Present;
            }
        }
    }

    public SimAllegianceLedger() => View = new Lens(this);

    public ISimAllegianceLens View { get; }

    public bool IsDisposed
    {
        get { lock (_latch) return _destroyed; }
    }

    public bool HasSrvSeed
    {
        get { lock (_latch) return _hasSrvSeed; }
    }

    public void ImposeRefresh(CommandReplies.AllegianceRefresh refresh)
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            _profile = new Profile(refresh.Monarch, refresh.Records, refresh.AllegianceName, refresh.TotalMembers, refresh.TotalVassals, refresh.Rank, Present: true);
            _hasSrvSeed = true;
            ++_rev;
        }
    }

    // Login notices and update done/abort carry no profile data; they only tick the revision.

    public void ImposeSigninNotification(uint toonOid, bool isLoggedIn) => Tick();

    public void ImposeRefreshDone(uint weenieProblem) => Tick();

    public void ImposeRefreshAborted(uint weenieProblem) => Tick();

    public SimAllegianceHoldingCapture CaptureOwnership()
    {
        lock (_latch)
            return new SimAllegianceHoldingCapture(_destroyed, _profile.Present, _profile.Records.Count);
    }

    public void ResetSession()
    {
        lock (_latch)
            Wipe();
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            Wipe();
            _destroyed = true;
        }
    }

    private void Tick()
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            ++_rev;
        }
    }

    private void Wipe()
    {
        bool altered = !_profile.IsBlank || _hasSrvSeed;
        _profile = Profile.Empty;
        _hasSrvSeed = false;
        if (altered)
            ++_rev;
    }

    private sealed class Lens(SimAllegianceLedger holder) : ISimAllegianceLens
    {
        public SimAllegianceCapture Snapshot
        {
            get
            {
                lock (holder._latch)
                {
                    Profile profile = holder._profile;
                    return new SimAllegianceCapture(
                        holder._rev, holder._hasSrvSeed, profile.Present, profile.Rank, profile.TotalMembers, profile.TotalVassals, profile.Name,
                        profile.Monarch?.CharacterId ?? 0u, profile.Records.Count);
                }
            }
        }

        public bool TryFetchMonarch(out SimAllegianceMemberCapture monarch)
        {
            lock (holder._latch)
                return Pick(holder._profile.Monarch, out monarch);
        }

        public bool TryFetchParticipant(uint oid, out SimAllegianceMemberCapture participant)
        {
            lock (holder._latch)
            {
                Profile profile = holder._profile;
                return Pick(CommandReplies.AllegianceProfileIndex.SeekBlob(profile.Monarch, profile.Records, oid), out participant);
            }
        }

        public bool TryGetPatron(uint oid, out SimAllegianceMemberCapture patron)
        {
            lock (holder._latch)
            {
                Profile profile = holder._profile;
                return Pick(CommandReplies.AllegianceProfileIndex.SeekPatron(profile.Monarch, profile.Records, oid), out patron);
            }
        }

        public IEnumerable<SimAllegianceMemberCapture> GetVassals(uint oid)
        {
            lock (holder._latch)
            {
                var vassals = new List<SimAllegianceMemberCapture>();
                foreach (CommandReplies.AllegianceMemberRow rank in CommandReplies.AllegianceProfileIndex.SeekVassals(holder._profile.Records, oid))
                    vassals.Add(Capture(rank));
                return vassals;
            }
        }

        private static bool Pick(CommandReplies.AllegianceMemberRow? rank, out SimAllegianceMemberCapture grab)
        {
            grab = rank is { } located ? Capture(located) : default;
            return rank is not null;
        }

        private static SimAllegianceMemberCapture Capture(CommandReplies.AllegianceMemberRow r)
        {
            return new(
            r.CharacterId, r.ParentGuid, r.IsLoggedIn, r.Name, r.Rank, r.Level, r.Loyalty, r.Leadership,
            r.CpCached, r.CpTithed, r.Gender, r.HeritageGroup, r.MayPassupExperience);
        }
    }
}
