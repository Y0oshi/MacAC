using MacAC.Client.Controls;
using MacAC.Sim.Actors;

namespace MacAC.Client.Realm;

internal readonly record struct OnlineActorOnlinenessSample(
    SimActorKey Key,
    uint ServerGuid,
    bool IsConservativelyVisible,
    bool HasNonWorldRetention);

internal readonly record struct OnlineActorPruneCandidate(
    SimActorKey Key,
    uint ServerGuid,
    ushort Generation)
{
    public OnlineActorPruneCandidate(SimActorKey tag, uint srvOid)
        : this(tag, srvOid, tag.Incarnation)
    {
    }
}

internal sealed class OnlineActorOnlinenessLedger
{
    internal const double DestructionTimeoutSecs = 25.0;

    private readonly Dictionary<SimActorKey, double> _deadlines = [];
    private readonly HashSet<SimActorKey> _present = [];
    private readonly List<SimActorKey> _stale = [];
    private readonly List<OnlineActorPruneCandidate> _due = [];

    internal int DeadlineTally => _deadlines.Count;

    internal IReadOnlyList<OnlineActorPruneCandidate> Tick(
        double instant,
        IReadOnlyList<OnlineActorOnlinenessSample> specimens)
    {
        _present.Clear();
        _due.Clear();
        for (int idx = 0; idx < specimens.Count; ++idx)
        {
            var specimen = specimens[idx];
            _present.Add(specimen.Key);
            if (specimen.IsConservativelyVisible || specimen.HasNonWorldRetention)
            {
                _deadlines.Remove(specimen.Key);
                continue;
            }

            if (!_deadlines.TryGetValue(specimen.Key, out double expiresAt))
            {
                _deadlines[specimen.Key] = instant + DestructionTimeoutSecs;
                continue;
            }

            if (expiresAt > instant)
                continue;

            _due.Add(new OnlineActorPruneCandidate(specimen.Key, specimen.ServerGuid));
            _deadlines.Remove(specimen.Key);
        }

        _stale.Clear();
        foreach (SimActorKey tag in _deadlines.Keys)
        {
            if (!_present.Contains(tag))
                _stale.Add(tag);
        }
        for (int idx = 0; idx < _stale.Count; ++idx)
            _deadlines.Remove(_stale[idx]);

        return _due;
    }

    internal void Clear() => _deadlines.Clear();
}

// Owns the 25-second destruction deadline for world objects that left visibility
internal sealed class OnlineActorOnlinenessDriver(
    OnlineActorCore runtime,
    IAvatarIdentitySource identity,
    IOnlineActorPruneSink prune)
{
    // Landblock Chebyshev radius of the visible neighbourhood
    internal const int ShownLbRadius = 1;
    private const double MaintenanceIntervalSecs = 1.0;

    private readonly OnlineActorCore _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly IOnlineActorPruneSink _prune = prune ?? throw new ArgumentNullException(nameof(prune));
    private readonly OnlineActorOnlinenessLedger _tracker = new();
    private readonly List<OnlineActorOnlinenessSample> _specimens = [];
    private double _upcomingMaintenanceAt;

    public void Tick(double instant)
    {
        if (instant < _upcomingMaintenanceAt)
            return;
        _upcomingMaintenanceAt = instant + MaintenanceIntervalSecs;

        uint avatarOid = _identity.SrvOid;
        if (avatarOid is 0
            || !_runtime.TryFetchRecord(avatarOid, out OnlineActorRecord avatar))

            return;
        uint avatarChamber = ChamberOf(avatar);
        if (avatarChamber is 0u)
            return;

        _specimens.Clear();
        foreach (OnlineActorRecord capture in _runtime.Records)
        {
            if (capture.ServerOid == avatarOid)
                continue;
            uint chamber = ChamberOf(capture);
            if (chamber is 0u)
                continue;

            bool kept = capture.ProjSort is OnlineActorMirrorKind.Attached
                || NonZero(capture.Snapshot.ContainerId)
                || NonZero(capture.Snapshot.WielderId)
                || NonZero(capture.Snapshot.ParentGuid);
            _specimens.Add(new OnlineActorOnlinenessSample(
                capture.ProjTag
                    ?? throw new InvalidOperationException(
                        $"Materialized liveness owner 0x{capture.ServerOid:X8}/" +
                        $"{capture.Generation} has no exact projection key"),
                capture.ServerOid,
                IsWithinShownLbs(avatarChamber, chamber),
                kept));
        }

        var due = _tracker.Tick(instant, _specimens);
        for (int idx = 0; idx < due.Count; ++idx)
        {
            var contender = due[idx];
            if (_runtime.TryFetchCapture(contender.Key, out OnlineActorRecord latest)
                && latest.ServerOid == contender.ServerGuid)

                _prune.Prune(contender);
        }
    }

    public void Clear()
    {
        _tracker.Clear();
        _specimens.Clear();
        _upcomingMaintenanceAt = 0;
    }

    // True when actorChamber's landblock is within one landblock (Chebyshev) of avatarChamber's
    internal static bool IsWithinShownLbs(uint avatarChamber, uint actorChamber)
    {
        int dx = Math.Abs((int)((avatarChamber >> 24) & 0xFFu) - (int)((actorChamber >> 24) & 0xFFu));
        int dy = Math.Abs((int)((avatarChamber >> 16) & 0xFFu) - (int)((actorChamber >> 16) & 0xFFu));
        return Math.Max(dx, dy) <= ShownLbRadius;
    }

    // The record's committed cell, else its accepted spawn cell, else 0
    private static uint ChamberOf(OnlineActorRecord capture)
    {
        return capture.WholeChamberIdent is not 0u ? capture.WholeChamberIdent : capture.Snapshot.Position?.LandblockId ?? 0u;
    }

    private static bool NonZero(uint? val) => val.GetValueOrDefault() is not 0u;
}
