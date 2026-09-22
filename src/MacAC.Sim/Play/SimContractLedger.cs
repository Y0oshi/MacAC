using MacAC.Wire.Messages;

namespace MacAC.Sim.Play;

public readonly record struct SimContractHoldingCapture(bool IsDisposed, int ContractCount, uint DisplayContractId)
{
    public bool IsConverged => IsDisposed && ContractCount is 0 && DisplayContractId is 0u;
}

public readonly record struct SimContractsCapture(long Revision, int ContractCount, uint DisplayContractId);

public interface ISimContractLens
{
    SimContractsCapture Snapshot { get; }

    bool TryFetchContract(uint contractIdent, out QuestTracker tracker);

    IReadOnlyList<QuestTracker> FetchContracts();
}

/// <summary>The quest tracker: contracts by id plus the one the panel is showing.</summary>
public sealed class SimContractLedger : IDisposable
{
    private readonly object _latch = new();
    private readonly Dictionary<uint, QuestTracker> _byIdent = [];
    private uint _shownIdent;
    private long _rev;
    private bool _destroyed;

    public SimContractLedger() => View = new Lens(this);

    public ISimContractLens View { get; }

    public void ImposeChart(IReadOnlyDictionary<uint, QuestTracker> chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        lock (_latch)
        {
            if (_destroyed)
                return;
            _byIdent.Clear();
            foreach ((uint ident, QuestTracker tracker) in chart)
                _byIdent[ident] = tracker;
            if (_shownIdent is not 0u && !_byIdent.ContainsKey(_shownIdent))
                _shownIdent = 0u;
            ++_rev;
        }
    }

    public void EnactRefresh(QuestTrackerUpdate refresh)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;

            uint ident = refresh.Tracker.ContractId;
            if (refresh.Delete)
            {
                bool removed = _byIdent.Remove(ident);
                if (_shownIdent == ident)
                    _shownIdent = 0u;
                if (removed)
                    ++_rev;
                return;
            }

            _byIdent[ident] = refresh.Tracker;
            if (refresh.SetAsDisplay)
                _shownIdent = ident;
            ++_rev;
        }
    }

    public SimContractHoldingCapture CaptureOwnership()
    {
        lock (_latch)
            return new SimContractHoldingCapture(_destroyed, _byIdent.Count, _shownIdent);
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

    private void Wipe()
    {
        bool altered = _byIdent.Count is not 0 || _shownIdent is not 0u;
        _byIdent.Clear();
        _shownIdent = 0u;
        if (altered)
            ++_rev;
    }

    private sealed class Lens(SimContractLedger holder) : ISimContractLens
    {
        public SimContractsCapture Snapshot
        {
            get
            {
                lock (holder._latch)
                    return new SimContractsCapture(holder._rev, holder._byIdent.Count, holder._shownIdent);
            }
        }

        public bool TryFetchContract(uint contractIdent, out QuestTracker tracker)
        {
            lock (holder._latch)
                return holder._byIdent.TryGetValue(contractIdent, out tracker);
        }

        // All contracts ordered by id
        public IReadOnlyList<QuestTracker> FetchContracts()
        {
            lock (holder._latch)
            {
                QuestTracker[] all = [.. holder._byIdent.Values];
                Array.Sort(all, static (tracker, b) => tracker.ContractId.CompareTo(b.ContractId));
                return all;
            }
        }
    }
}
