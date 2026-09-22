using MacAC.Sim.Actors;

namespace MacAC.Client.Realm;

// App-only projection storage
internal sealed class OnlineActorMirrorStore
{
    private readonly SimActorIndex _directory;
    private readonly Dictionary<SimActorKey, OnlineActorRecord> _materialized = [];
    private readonly Dictionary<SimActorKey, OnlineActorRecord> _shown = [];
    private readonly Dictionary<SimActorKey, OnlineActorRecord> _teardown = [];
    private readonly Dictionary<SimActorRecord, OnlineActorRecord>
        _unmaterializedTeardown = new(ReferenceEqualityComparer.Instance);
    private readonly List<OnlineActorRecord> _materializedInEnrollmentOrdering = [];
    private readonly TeardownCaptureCollection _teardownRecords;

    public OnlineActorMirrorStore(SimActorIndex directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _teardownRecords = new TeardownCaptureCollection(
            _teardown,
            _unmaterializedTeardown);
    }

    public int EngagedTally => _materializedInEnrollmentOrdering.Count;
    public int MaterializedCount => _materialized.Count + _teardown.Count;
    public int TeardownTally => _teardown.Count + _unmaterializedTeardown.Count;
    public IReadOnlyList<OnlineActorRecord> EngagedRecords =>
        _materializedInEnrollmentOrdering;
    public IReadOnlyList<OnlineActorRecord> Values =>
        _materializedInEnrollmentOrdering;
    public IReadOnlyCollection<OnlineActorRecord> MaterializedRecords =>
        _materialized.Values;
    public IReadOnlyCollection<OnlineActorRecord> VisibleRecords => _shown.Values;
    public IReadOnlyCollection<OnlineActorRecord> TeardownRecords =>
        _teardownRecords;

    public OnlineActorRecord AppendMaterializing(SimActorRecord canon)
    {
        ArgumentNullException.ThrowIfNull(canon);
        if (!_directory.IsCurrent(canon))
        {
            throw new InvalidOperationException(
                "An App projection sidecar can only be added for the current Runtime incarnation");
        }
        SimActorKey tag = canon.Key
            ?? throw new InvalidOperationException(
                "Runtime must claim the local projection ID prior to App creates a sidecar");
        if (_materialized.ContainsKey(tag))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{canon.ServerGuid:X8}/{canon.Incarnation} by now has an App sidecar");
        }

        OnlineActorRecord capture = new OnlineActorRecord(_directory, canon)
        {
            ProjTag = tag,
        };
        _materialized.Add(tag, capture);
        _materializedInEnrollmentOrdering.Add(capture);
        return capture;
    }

    public bool TryFetchLatest(uint srvOid, out OnlineActorRecord capture)
    {
        if (_directory.TryFetchEngaged(srvOid, out SimActorRecord canon))
            return TryGet(canon, out capture);

        capture = null!;
        return false;
    }

    public bool ContainsLatest(uint srvOid) =>
        TryFetchLatest(srvOid, out _);

    public OnlineActorRecord? FetchLatestOrDefault(uint srvOid) =>
        TryFetchLatest(srvOid, out OnlineActorRecord capture) ? capture : null;

    public bool TryGet(SimActorRecord canon, out OnlineActorRecord capture)
    {
        if (canon.Key is { } tag
            && _materialized.TryGetValue(tag, out OnlineActorRecord? contender)
            && ReferenceEquals(contender.Canonical, canon))
        {
            capture = contender;
            return true;
        }

        capture = null!;
        return false;
    }

    public bool TryGet(SimActorKey tag, out OnlineActorRecord capture) =>
        _materialized.TryGetValue(tag, out capture!);

    public bool TryFetchByOwnIdent(uint ownActorIdent, out OnlineActorRecord capture)
    {
        if (_directory.TryFetchByOwnTag(
                ownActorIdent,
                out SimActorRecord canon))

            return TryGet(canon, out capture);

        capture = null!;
        return false;
    }

    public bool IsLatest(OnlineActorRecord capture)
    {
        return _directory.IsCurrent(capture.Canonical)
        && TryGet(capture.Canonical, out OnlineActorRecord latest)
        && ReferenceEquals(latest, capture);
    }

    public void AssignShown(OnlineActorRecord capture, bool shown)
    {
        SimActorKey tag = DemandProjTag(capture);
        if (!_materialized.TryGetValue(tag, out OnlineActorRecord? materialized)
            || !ReferenceEquals(materialized, capture))
        {
            throw new InvalidOperationException(
                "Only a current exact App sidecar can change visibility");
        }

        if (shown)
            _shown[tag] = capture;
        else
            _shown.Remove(tag);
    }

    public bool TryFetchShownLatest(uint srvOid, out OnlineActorRecord capture)
    {
        if (_directory.TryFetchEngaged(srvOid, out SimActorRecord canon)
            && canon.Key is { } tag
            && _shown.TryGetValue(tag, out OnlineActorRecord? shown)
            && ReferenceEquals(shown.Canonical, canon))
        {
            capture = shown;
            return true;
        }

        capture = null!;
        return false;
    }

    public bool RemoveCurrent(
        uint srvOid,
        out OnlineActorRecord? capture)
    {
        if (!_directory.TryFetchEngaged(
                srvOid,
                out SimActorRecord canon)
            || !TryGet(canon, out OnlineActorRecord sidecar)
            || !RemoveActive(sidecar))
        {
            capture = null;
            return false;
        }

        capture = sidecar;
        return true;
    }

    public bool RemoveCurrent(uint srvOid) =>
        RemoveCurrent(srvOid, out _);

    public bool RemoveActive(OnlineActorRecord capture)
    {
        if (capture.ProjTag is not { } tag)
            return false;

        _shown.Remove(tag);
        bool removed = _materialized.Remove(
                tag,
                out OnlineActorRecord? materialized)
            && ReferenceEquals(materialized, capture);
        if (!removed)
            return false;
        return !_materializedInEnrollmentOrdering.Remove(capture)
            ? throw new InvalidOperationException(
                "The exact App sidecar was absent from materialization order")
            : true;
    }

    public void KeepTeardown(OnlineActorRecord capture)
    {
        if (capture.ProjTag is { } tag)
            AppendPrecise(_teardown, tag, capture);
        else
            AppendPrecise(_unmaterializedTeardown, capture.Canonical, capture);
    }

    public bool TryFetchTeardown(
        SimActorRecord canon,
        out OnlineActorRecord capture)
    {
        if (canon.Key is { } tag
            && _teardown.TryGetValue(tag, out capture!))

            return true;
        foreach (OnlineActorRecord kept in _teardown.Values)
        {
            if (ReferenceEquals(kept.Canonical, canon))
            {
                capture = kept;
                return true;
            }
        }
        return _unmaterializedTeardown.TryGetValue(canon, out capture!);
    }

    public void FreeTeardown(OnlineActorRecord capture)
    {
        if (capture.ProjTag is { } tag)
        {
            DropPrecise(_teardown, tag, capture);
            capture.ProjTag = null;
            return;
        }

        DropPrecise(_unmaterializedTeardown, capture.Canonical, capture);
    }

    public bool IsKeptTeardown(OnlineActorRecord capture)
    {
        return TryFetchTeardown(capture.Canonical, out OnlineActorRecord kept)
        && ReferenceEquals(kept, capture);
    }

    public void WipeConverged()
    {
        if (_materializedInEnrollmentOrdering.Count is not 0 || TeardownTally is not 0)
        {
            throw new InvalidOperationException(
                "Projection storage can't clear prior to active and teardown ownership converges");
        }

        _materialized.Clear();
        _shown.Clear();
        _teardown.Clear();
        _unmaterializedTeardown.Clear();
    }

    private static SimActorKey DemandProjTag(OnlineActorRecord capture)
    {
        return capture.ProjTag
        ?? throw new InvalidOperationException(
            $"Runtime entity 0x{capture.ServerOid:X8}/{capture.Generation} has no App projection key");
    }

    private static void AppendPrecise<TKey>(
        Dictionary<TKey, OnlineActorRecord> dest,
        TKey tag,
        OnlineActorRecord capture)
        where TKey : notnull
    {
        if (dest.TryGetValue(tag, out OnlineActorRecord? kept)
            && !ReferenceEquals(kept, capture))
        {
            throw new InvalidOperationException(
                $"App teardown sidecar collision for 0x{capture.ServerOid:X8}/{capture.Generation}.");
        }
        dest[tag] = capture;
    }

    private static void DropPrecise<TKey>(
        Dictionary<TKey, OnlineActorRecord> src,
        TKey tag,
        OnlineActorRecord capture)
        where TKey : notnull
    {
        if (src.TryGetValue(tag, out OnlineActorRecord? kept)
            && ReferenceEquals(kept, capture))

            src.Remove(tag);
    }

    private sealed class TeardownCaptureCollection(
        Dictionary<SimActorKey, OnlineActorRecord> keyed,
        Dictionary<SimActorRecord, OnlineActorRecord> unmaterialized) :
        IReadOnlyCollection<OnlineActorRecord>
    {
        private readonly Dictionary<SimActorKey, OnlineActorRecord> _keyed = keyed;
        private readonly Dictionary<SimActorRecord, OnlineActorRecord> _unmaterialized = unmaterialized;

        public int Count => _keyed.Count + _unmaterialized.Count;

        public IEnumerator<OnlineActorRecord> GetEnumerator()
        {
            foreach (OnlineActorRecord capture in _keyed.Values)
                yield return capture;
            foreach (OnlineActorRecord capture in _unmaterialized.Values)
                yield return capture;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
