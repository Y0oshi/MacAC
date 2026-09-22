using MacAC.Wire.Messages;

namespace MacAC.Sim.Play;

public readonly record struct SimFellowsHoldingCapture(bool IsDisposed, bool IsInFellowship, int MemberCount)
{
    public bool IsConverged => IsDisposed && !IsInFellowship && MemberCount is 0;
}

public sealed class SimFellowsLedger : IDisposable
{
    private const int DepartedGraceSecs = 900;
    private const uint EvenDivideTierCap = 50u;
    private const uint EvenDivideTierSpread = 5u;

    private readonly object _latch = new();
    private readonly TimeProvider _clock;
    private readonly Dictionary<uint, PlaySignals.FellowRow> _lineup = [];
    private readonly Dictionary<uint, int> _departedAt = [];
    private string _label = string.Empty;
    private uint _leader;
    private bool _portionXp;
    private bool _evenDivide;
    private bool _open;
    private bool _bolted;
    private bool _participant;
    private long _rev;
    private bool _destroyed;

    public SimFellowsLedger(TimeProvider? momentSupplier = null)
    {
        _clock = momentSupplier ?? TimeProvider.System;
        View = new Lens(this);
    }

    public ISimFellowsLens View { get; }

    public bool IsDisposed
    {
        get { lock (_latch) return _destroyed; }
    }

    public uint LeaderGuid
    {
        get { lock (_latch) return _participant ? _leader : 0u; }
    }

    public bool IsInFellowship
    {
        get { lock (_latch) return _participant; }
    }

    public void ImposeWholeRefresh(PlaySignals.FellowshipWholeRefresh refresh)
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            _lineup.Clear();
            foreach (PlaySignals.FellowRow rank in refresh.Members)
                _lineup[rank.Guid] = rank;
            _departedAt.Clear();
            foreach (PlaySignals.FellowsDeparted gone in refresh.Departed)
                _departedAt[gone.Guid] = gone.DepartedTimestamp;
            _label = refresh.Name;
            _leader = refresh.LeaderGuid;
            _portionXp = refresh.ShareXp;
            _evenDivide = refresh.EvenXpSplit;
            _open = refresh.OpenFellow;
            _bolted = refresh.Locked;
            _participant = true;
            ++_rev;
        }
    }

    public void ImposeRefreshFellow(PlaySignals.FellowshipRefreshFellow refresh)
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (!_participant)
                return;
            bool newcomer = !_lineup.ContainsKey(refresh.MemberGuid);
            if (newcomer && _bolted && !RecentlyDeparted(refresh.MemberGuid))
                return;
            _lineup[refresh.MemberGuid] = refresh.Member;
            RederiveEvenDivide();
            ++_rev;
        }
    }

    public void ImposeQuit(uint quitterOid, uint selfOid) => Depart(quitterOid, selfOid);

    public void ImposeDismiss(uint dismissedOid, uint selfOid) => Depart(dismissedOid, selfOid);

    public void ImposeDisband()
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            Wipe();
        }
    }

    /// <summary>A leader quitting without disbanding must first hand leadership to any other member.</summary>
    public bool RequiresLeaderHandoffPriorQuit(uint selfOid, bool disband, out uint newLeaderOid)
    {
        lock (_latch)
        {
            newLeaderOid = 0u;
            if (disband || !_participant || _leader != selfOid)
                return false;
            foreach (uint oid in _lineup.Keys)
            {
                if (oid != selfOid)
                {
                    newLeaderOid = oid;
                    return true;
                }
            }
            return false;
        }
    }

    public SimFellowsHoldingCapture CaptureOwnership()
    {
        lock (_latch)
            return new SimFellowsHoldingCapture(_destroyed, _participant, _lineup.Count);
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

    private bool RecentlyDeparted(uint oid)
    {
        return _departedAt.TryGetValue(oid, out int departed)
        && _clock.GetUtcNow().ToUnixTimeSeconds() - departed <= DepartedGraceSecs;
    }

    // Retail's rule: with a member under 50, the split is uneven when the level range strays more than
    // five from the leader
    private void RederiveEvenDivide()
    {
        if (!_portionXp)
            return;

        uint lowest = uint.MaxValue;
        uint highest = 0u;
        foreach (PlaySignals.FellowRow rank in _lineup.Values)
        {
            lowest = Math.Min(lowest, rank.Level);
            highest = Math.Max(highest, rank.Level);
        }

        _evenDivide = true;
        if (_lineup.TryGetValue(_leader, out PlaySignals.FellowRow leader) && lowest < EvenDivideTierCap)
        {
            if (highest > leader.Level + EvenDivideTierSpread || lowest + EvenDivideTierSpread < leader.Level)
                _evenDivide = false;
        }
    }

    // Someone left or was removed: if it was us the fellowship is gone, otherwise the roster shrinks
    private void Depart(uint oid, uint selfOid)
    {
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (!_participant)
                return;
            if (oid == selfOid)
            {
                Wipe();
                return;
            }
            if (_lineup.Remove(oid))
            {
                RederiveEvenDivide();
                ++_rev;
            }
        }
    }

    private void Wipe()
    {
        bool altered = _lineup.Count is not 0 || _participant || _label.Length is not 0 || _leader is not 0u || _portionXp || _evenDivide || _open || _bolted;
        _lineup.Clear();
        _departedAt.Clear();
        _label = string.Empty;
        _leader = 0u;
        _portionXp = false;
        _evenDivide = false;
        _open = false;
        _bolted = false;
        _participant = false;
        if (altered)
            ++_rev;
    }

    private sealed class Lens(SimFellowsLedger holder) : ISimFellowsLens
    {
        public SimFellowsCapture Snapshot
        {
            get
            {
                lock (holder._latch)
                {
                    return new SimFellowsCapture(
                        holder._rev, holder._participant, holder._label, holder._leader, holder._portionXp,
                        holder._evenDivide, holder._open, holder._bolted, holder._lineup.Count);
                }
            }
        }

        public bool TryFetchMember(uint oid, out SimFellowMemberCapture participant)
        {
            lock (holder._latch)
            {
                bool located = holder._lineup.TryGetValue(oid, out PlaySignals.FellowRow rank);
                participant = located ? Capture(rank) : default;
                return located;
            }
        }

        public IEnumerable<SimFellowMemberCapture> FetchParticipants()
        {
            lock (holder._latch)
                return [.. holder._lineup.Values.Select(Capture)];
        }

        private static SimFellowMemberCapture Capture(PlaySignals.FellowRow r)
        {
            return new(
            r.Guid, r.Name, r.Level, r.MaxHealth, r.MaxStamina, r.MaxMana,
            r.CurrentHealth, r.CurrentStamina, r.CurrentMana, r.ShareLoot is not 0u);
        }
    }
}
