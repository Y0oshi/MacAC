using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class SimActorIndex
{
    public const uint LeadOwnActorIdent = 1_000_000u;

    public const uint PreviousOwnActorIdent = 0x3FFF_FFFFu;

    private readonly InboundKineticsStateDriver _incoming = new();

    private readonly object _onlineLatch = new();

    private readonly Dictionary<uint, SimActorRecord> _onlineByOid = new();
    private readonly Dictionary<(uint Guid, ushort Incarnation), SimActorRecord> _sunsettingByIncarnation = new();

    private readonly Dictionary<uint, SimActorRecord> _onlineByOwnIdent = new();

    private readonly Dictionary<uint, ulong> _alterationsByOid = new();

    private uint _ownIdentCur;

    public SimActorIndex(uint firstLocalEntityId = LeadOwnActorIdent)
    {
        if (firstLocalEntityId is < LeadOwnActorIdent or > PreviousOwnActorIdent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstLocalEntityId),
                $"Live entity ids must stay in 0x{LeadOwnActorIdent:X8}..0x{PreviousOwnActorIdent:X8}.");
        }

        _ownIdentCur = firstLocalEntityId;
    }

    public int Count
    {
        get
        {
            lock (_onlineLatch)
                return _onlineByOid.Count;
        }
    }

    public int PendingTeardownCount => _sunsettingByIncarnation.Count;

    public int ClaimedOwnIdentTally
    {
        get
        {
            lock (_onlineLatch)
                return _onlineByOwnIdent.Count;
        }
    }

    public ulong SessionLifetimeVersion { get; private set; }

    public IReadOnlyCollection<SimActorRecord> ActiveRecords => _onlineByOid.Values;

    public IReadOnlyCollection<SimActorRecord> TeardownRecords =>
        _sunsettingByIncarnation.Values;

    public IReadOnlyDictionary<uint, RealmSession.MoverSpawn> Snapshots => _incoming.Snapshots;

    public AnchorAttachmentLedger AncestorAttachments { get; } = new();

    public bool TryGetSnapshot(uint oid, out RealmSession.MoverSpawn summon) =>
        _incoming.TryFetchCapture(oid, out summon);

    public bool TryFetchEngaged(uint oid, out SimActorRecord capture) =>
        _onlineByOid.TryGetValue(oid, out capture!);

    public bool IsCurrent(SimActorRecord capture)
    {
        return _onlineByOid.TryGetValue(capture.ServerGuid, out SimActorRecord? latest)
        && ReferenceEquals(latest, capture);
    }

    public bool TryFetchByOwnTag(uint ownActorIdent, out SimActorRecord capture) =>
        _onlineByOwnIdent.TryGetValue(ownActorIdent, out capture!);

    public bool TryGetTeardown(
        uint oid,
        ushort incarnation,
        out SimActorRecord capture) =>
        _sunsettingByIncarnation.TryGetValue((oid, incarnation), out capture!);

    public SimActorRecord AppendEngaged(RealmSession.MoverSpawn capture)
    {
        lock (_onlineLatch)
        {
            if (_onlineByOid.ContainsKey(capture.Guid))
            {
                throw new InvalidOperationException(
                    $"Live entity 0x{capture.Guid:X8} by now has an active incarnation");
            }

            SimActorRecord record = new SimActorRecord(capture);
            _onlineByOid.Add(capture.Guid, record);
            try
            {
                ClaimOwnIdent(record);
                return record;
            }
            catch
            {
                _onlineByOid.Remove(capture.Guid);
                throw;
            }
        }
    }

    public bool RemoveActive(uint oid, out SimActorRecord? capture)
    {
        lock (_onlineLatch)
            return _onlineByOid.Remove(oid, out capture);
    }

    public bool RemoveActive(SimActorRecord anticipated)
    {
        lock (_onlineLatch)
        {
            if (!_onlineByOid.TryGetValue(
                    anticipated.ServerGuid,
                    out SimActorRecord? latest)
                || !ReferenceEquals(latest, anticipated))

                return false;
            return _onlineByOid.Remove(anticipated.ServerGuid);
        }
    }

    public void HoldTeardown(SimActorRecord capture)
    {
        var tag = (capture.ServerGuid, capture.Incarnation);
        if (_sunsettingByIncarnation.TryGetValue(
                tag,
                out SimActorRecord? kept)
            && !ReferenceEquals(kept, capture))
        {
            throw new InvalidOperationException(
                $"Live entity teardown tombstone collision for 0x{capture.ServerGuid:X8} generation {capture.Incarnation}.");
        }

        _sunsettingByIncarnation[tag] = capture;
    }

    public void RelinquishTeardown(SimActorRecord capture)
    {
        var tag = (capture.ServerGuid, capture.Incarnation);
        if (_sunsettingByIncarnation.TryGetValue(
                tag,
                out SimActorRecord? kept)
            && ReferenceEquals(kept, capture))

            _sunsettingByIncarnation.Remove(tag);
    }

    public bool HasQueuedTeardown(uint srvOid)
    {
        foreach ((uint Guid, ushort Incarnation) tag in _sunsettingByIncarnation.Keys)
        {
            if (tag.Guid == srvOid)
                return true;
        }

        return false;
    }

    public uint ClaimOwnIdent(SimActorRecord capture)
    {
        lock (_onlineLatch)
        {
            if (!Tracks(capture))
            {
                throw new InvalidOperationException(
                    "A local id can only be claimed for an active or retained incarnation");
            }

            if (capture.OwnActorTag is { } extant)
                return extant;

            uint begin = _ownIdentCur;
            do
            {
                uint contender = _ownIdentCur;
                _ownIdentCur = contender == PreviousOwnActorIdent
                    ? LeadOwnActorIdent
                    : contender + 1u;
                if (_onlineByOwnIdent.ContainsKey(contender))
                    continue;

                _onlineByOwnIdent.Add(contender, capture);
                capture.OwnActorTag = contender;
                return contender;
            }
            while (_ownIdentCur != begin);

            throw new InvalidOperationException("The live entity id namespace is exhausted");
        }
    }

    public bool FreeOwnIdent(SimActorRecord capture)
    {
        lock (_onlineLatch)
        {
            if (capture.OwnActorTag is not { } ownIdent)
                return false;
            if (_onlineByOwnIdent.TryGetValue(ownIdent, out SimActorRecord? kept)
                && ReferenceEquals(kept, capture))

                _onlineByOwnIdent.Remove(ownIdent);

            capture.OwnActorTag = null;
            return true;
        }
    }

    public ulong ProgressLifespanAlteration(uint srvOid)
    {
        ulong upcoming = _alterationsByOid.GetValueOrDefault(srvOid) + 1UL;
        _alterationsByOid[srvOid] = upcoming;
        return upcoming;
    }

    public ulong LatestLifespanAlteration(uint srvOid) =>
        _alterationsByOid.GetValueOrDefault(srvOid);

    public void CommenceSessWipe()
    {
        ++SessionLifetimeVersion;
        _alterationsByOid.Clear();
        AncestorAttachments.Clear();
        _incoming.Clear();
    }

    public bool ConcludeSessWipeIfConverged()
    {
        lock (_onlineLatch)
        {
            if (_onlineByOid.Count is not 0 || _sunsettingByIncarnation.Count is not 0)
                return false;

            _onlineByOwnIdent.Clear();
        }
        AncestorAttachments.Clear();
        _incoming.Clear();
        return true;
    }

    internal bool TryFetchApprovedTimestamps(
        uint oid,
        out GrantedKineticsTimestamps timestamps) =>
        _incoming.TryFetchApprovedTimestamps(oid, out timestamps);

    internal EngagedScanTenancy ObtainEngagedScan() => new(_onlineLatch);

    internal readonly struct EngagedScanTenancy : IDisposable
    {
        private readonly object _latch;

        internal EngagedScanTenancy(object latch)
        {
            _latch = latch;
            Monitor.Enter(latch);
        }

        public void Dispose() => Monitor.Exit(_latch);
    }

    private bool Tracks(SimActorRecord capture)
    {
        if (IsCurrent(capture))
            return true;
        return _sunsettingByIncarnation.TryGetValue(
                (capture.ServerGuid, capture.Incarnation),
                out SimActorRecord? kept)
            && ReferenceEquals(kept, capture);
    }

    private void DemandFollowed(SimActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!Tracks(capture))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} is no longer owned by the directory");
        }
    }
}
