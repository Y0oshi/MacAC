using System.Collections.Immutable;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal sealed partial class SimContactNoticesLedger : IDisposable
{
    private readonly SimActorIndex _actors;

    private readonly ProxyRegistry _proxies;

    private readonly Dictionary<SimActorKey, HolderLedger> _holders = new();

    private readonly Dictionary<SimActorKey, List<SimActorKey>> _holdersTouching = new();

    private readonly Queue<Queued> _fifo = new();

    private readonly HashSet<SimActorKey> _leaving = [];

    private readonly HashSet<SimActorKey> _intakeBlocked = [];

    private ISimContactNoticeWatcher[] _watchers = [];

    private ulong _series;

    private ulong _deliveryEpoch = 1UL;

    private long _deliveryMisses;

    private ulong _rev;

    private bool _delivering;

    private bool _destroyed;

    internal SimContactNoticesLedger(
        SimActorIndex entities,
        ProxyRegistry shadows)
    {
        _actors = entities ?? throw new ArgumentNullException(nameof(entities));
        _proxies = shadows ?? throw new ArgumentNullException(nameof(shadows));
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _holders.Clear();
        _holdersTouching.Clear();
        _fifo.Clear();
        _leaving.Clear();
        _intakeBlocked.Clear();
        _lotsInFlight.Clear();
        _watchers = [];
        _deliveryEpoch = checked(_deliveryEpoch + 1UL);
        _destroyed = true;
    }

    internal SimContactNoticesHoldingCapture GrabOwnership()
    {
        int followed = 0;
        foreach ((_, HolderLedger holder) in _holders)
            followed += holder.Records.Count;
        return new SimContactNoticesHoldingCapture(
            _holders.Count,
            followed,
            _holdersTouching.Count,
            _watchers.Length,
            _fifo.Count,
            _leaving.Count,
            _intakeBlocked.Count,
            _lotsInFlight.Count,
            _delivering,
            _deliveryMisses,
            _destroyed);
    }

    internal IDisposable Enlist(ISimContactNoticeWatcher watcher)
    {
        Live();
        ArgumentNullException.ThrowIfNull(watcher);
        if (Array.IndexOf(_watchers, watcher) >= 0)
        {
            throw new InvalidOperationException(
                "A collision-report observer can't be subscribed twice");
        }

        var substitute = new ISimContactNoticeWatcher[
            _watchers.Length + 1];
        Array.Copy(_watchers, substitute, _watchers.Length);
        substitute[^1] = watcher;
        _watchers = substitute;
        return new SubscriptionDef(this, watcher);
    }

    internal void ExitRealm(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag)
            return;
        _rev = checked(_rev + 1UL);
        if (!_intakeBlocked.Add(tag))
            return;
        try
        {
            ForceFinish(capture, tag);
        }
        finally
        {
            _intakeBlocked.Remove(tag);
        }
    }

    internal void ExitRealmLot(
        IReadOnlyList<SimActorRecord> records)
    {
        Live();
        ArgumentNullException.ThrowIfNull(records);
        _rev = checked(_rev + 1UL);
        var blocked = new List<(SimActorRecord Record, SimActorKey Key)>(
            records.Count);
        for (int ordinal = 0; ordinal < records.Count; ++ordinal)
        {
            if (records[ordinal].Key is { } tag
                && _intakeBlocked.Add(tag))

                blocked.Add((records[ordinal], tag));
        }
        try
        {
            for (int ordinal = 0; ordinal < blocked.Count; ++ordinal)
                ForceFinish(blocked[ordinal].Record, blocked[ordinal].Key);
        }
        finally
        {
            for (int ordinal = 0; ordinal < blocked.Count; ++ordinal)
                _intakeBlocked.Remove(blocked[ordinal].Key);
        }
    }

    internal void Drop(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag)
            return;
        _rev = checked(_rev + 1UL);
        if (_intakeBlocked.Contains(tag))
        {
            if (!_leaving.Contains(tag))
                ForceFinish(capture, tag);
        }
        else
        {
            ExitRealm(capture);
        }
        _holders.Remove(tag);
    }

    internal void RestartSess()
    {
        Live();
        _rev = checked(_rev + 1UL);
        _holders.Clear();
        _holdersTouching.Clear();
        _fifo.Clear();
        _leaving.Clear();
        _intakeBlocked.Clear();
        _lotsInFlight.Clear();
        _deliveryEpoch = checked(_deliveryEpoch + 1UL);
    }

    private HolderLedger HolderFor(
        SimActorKey tag,
        ulong setLocusLotIdent = 0UL)
    {
        if (!_holders.TryGetValue(tag, out HolderLedger? holder))
        {
            holder = new HolderLedger();
            _holders.Add(tag, holder);
        }
        holder.SetLocusLotIdent = setLocusLotIdent;
        return holder;
    }

    private HolderLedger? TryHolder(SimActorKey tag) =>
        _holders.TryGetValue(tag, out HolderLedger? holder) ? holder : null;

    private void PruneHolder(SimActorKey tag)
    {
        if (_holders.TryGetValue(tag, out HolderLedger? holder)
            && holder.Records.Count is 0
            && !holder.CollidingWithEnvironment)

            _holders.Remove(tag);
    }

    private void ConnectTouching(
        SimActorKey counterpart,
        SimActorKey holder)
    {
        if (!_holdersTouching.TryGetValue(
                counterpart,
                out List<SimActorKey>? holders))
        {
            holders = [];
            _holdersTouching.Add(counterpart, holders);
        }
        if (!holders.Contains(holder))
            holders.Add(holder);
    }

    private void UnlinkTouching(
        SimActorKey counterpart,
        SimActorKey holder)
    {
        if (!_holdersTouching.TryGetValue(
                counterpart,
                out List<SimActorKey>? holders))

            return;
        holders.Remove(holder);
        if (holders.Count is 0)
            _holdersTouching.Remove(counterpart);
    }

    private bool TryOnlineParty(
        SimActorRecord capture,
        KineticBody corpus,
        out SimActorKey tag)
    {
        tag = capture.Key ?? default;
        return tag != default
            && PartyIsOnline(capture, corpus, tag);
    }

    private bool TryOnlineParty(
        uint ownActorIdent,
        out SimActorRecord capture,
        out KineticBody corpus,
        out SimActorKey tag)
    {
        if (_actors.TryFetchByOwnTag(ownActorIdent, out capture!)
            && capture.KineticBody is { } kept
            && capture.Key is { } keptTag
            && keptTag.LocalEntityId == ownActorIdent
            && !_leaving.Contains(keptTag)
            && !_intakeBlocked.Contains(keptTag)
            && kept.InWorld
            && (kept.State & KineticStateFlags.Hidden) == 0
            && _actors.IsCurrent(capture))
        {
            corpus = kept;
            tag = keptTag;
            return true;
        }
        corpus = null!;
        tag = default;
        return false;
    }

    private bool TryParty(
        SimActorKey tag,
        out SimActorRecord capture,
        out KineticBody corpus)
    {
        if (_actors.TryFetchByOwnTag(tag.LocalEntityId, out capture!)
            && capture.Key == tag
            && !_leaving.Contains(tag)
            && _actors.IsCurrent(capture)
            && capture.KineticBody is { } kept)
        {
            corpus = kept;
            return true;
        }
        corpus = null!;
        return false;
    }

    private bool PartyIsOnline(
        SimActorRecord capture,
        KineticBody corpus,
        SimActorKey tag)
    {
        return _actors.IsCurrent(capture)
        && !_leaving.Contains(tag)
        && !_intakeBlocked.Contains(tag)
        && corpus.InWorld
        && (corpus.State & KineticStateFlags.Hidden) == 0
        && PartyIsRecognized(capture, corpus, tag);
    }

    private void ForceFinish(
        SimActorRecord capture,
        SimActorKey tag)
    {
        if (!_leaving.Add(tag))
            return;
        try
        {
            ShutExpiredLinks(
                capture,
                capture.KineticBody,
                tag,
                kineticsMoment: 0d,
                force: true);
        }
        finally
        {
            _leaving.Remove(tag);
        }
    }

    private bool PartyIsRecognized(
        SimActorRecord capture,
        KineticBody corpus,
        SimActorKey tag)
    {
        return capture.Key == tag
        && ReferenceEquals(capture.KineticBody, corpus)
        && _actors.TryFetchByOwnTag(
            tag.LocalEntityId,
            out SimActorRecord kept)
        && ReferenceEquals(kept, capture);
    }

    private ulong UpcomingSeq() => checked(++_series);

    private void Publish(in SimContactNotice dossier)
    {
        _fifo.Enqueue(new Queued(_deliveryEpoch, dossier));
        if (_delivering)
            return;

        _delivering = true;
        try
        {
            while (!_destroyed && _fifo.TryDequeue(out Queued queued))
            {
                if (queued.Epoch != _deliveryEpoch)
                    continue;
                var watchers = _watchers;
                for (int ordinal = 0; ordinal < watchers.Length; ++ordinal)
                {
                    try
                    {
                        watchers[ordinal].OnImpactDossier(queued.Report);
                    }
                    catch (Exception problem)
                    {
                        ++_deliveryMisses;
                        System.Diagnostics.Trace.TraceError(
                            "Runtime collision-report observer failed: {0}",
                            problem);
                    }
                    if (_destroyed || queued.Epoch != _deliveryEpoch)
                        break;
                }
            }
        }
        finally
        {
            _delivering = false;
            if (_destroyed)
                _fifo.Clear();
        }
    }

    private void Delist(ISimContactNoticeWatcher watcher)
    {
        int ordinal = Array.IndexOf(_watchers, watcher);
        if (ordinal < 0)
            return;
        if (_watchers.Length is 1)
        {
            _watchers = [];
            return;
        }
        var substitute = new ISimContactNoticeWatcher[
            _watchers.Length - 1];
        if (ordinal > 0)
            Array.Copy(_watchers, 0, substitute, 0, ordinal);
        if (ordinal < _watchers.Length - 1)
        {
            Array.Copy(
                _watchers,
                ordinal + 1,
                substitute,
                ordinal,
                _watchers.Length - ordinal - 1);
        }
        _watchers = substitute;
    }

    private void Live() =>
        ObjectDisposedException.ThrowIf(_destroyed, this);

    internal sealed class HolderLedger
    {
        internal Dictionary<SimActorKey, ContactRecord> Records { get; }
            = new();
        internal List<SimActorKey> Order { get; } = [];
        internal bool CollidingWithEnvironment { get; set; }
        internal ulong SetLocusLotIdent { get; set; }
    }

    internal readonly record struct ContactRecord(
        double TouchedTime,
        bool Ethereal,
        uint ServerGuid);

    private readonly record struct Closed(
        SimActorKey Key,
        uint ServerGuid);

    private readonly record struct Queued(
        ulong Epoch,
        SimContactNotice Report);

    private sealed class SubscriptionDef : IDisposable
    {
        private SimContactNoticesLedger? _holder;
        private readonly ISimContactNoticeWatcher _watcher;

        internal SubscriptionDef(
            SimContactNoticesLedger holder,
            ISimContactNoticeWatcher watcher)
        {
            _holder = holder;
            _watcher = watcher;
        }

        public void Dispose()
        {
            var holder =
                Interlocked.Exchange(ref _holder, null);
            holder?.Delist(_watcher);
        }
    }
}
