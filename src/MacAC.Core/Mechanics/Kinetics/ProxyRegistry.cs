using System.Numerics;
using System.Runtime.CompilerServices;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class ProxyRegistry
{
    private ContactWorldStateSlot _impactRealm;

    private Dictionary<uint, List<ProxyEntry>> _ranksByChamber =>
        _impactRealm.Current.ShadeChambers;

    private Dictionary<uint, List<uint>> _chambersByHolder =>
        _impactRealm.Current.ShadeActorChambers;

    private HashSet<uint> _dormantHolders =>
        _impactRealm.Current.SuspendedShadeActors;

    private Dictionary<uint, List<uint>> _dormantHolderChambers =>
        _impactRealm.Current.SuspendedShadeActorChambers;

    private Dictionary<uint, HashSet<uint>> _withdrawnByHolder =>
        _impactRealm.Current.WithdrawnStemsByHolder;

    private Dictionary<uint, IReadOnlyList<ProxyShape>> _formsByHolder =>
        _impactRealm.Current.ShadeActorForms;

    private Dictionary<uint, IReadOnlyList<ProxyShape>> _canonPiecesByHolder =>
        _impactRealm.Current.ShadeActorCanonPieceArrs;

    private Dictionary<uint, List<uint>> _canonChambersByHolder =>
        _impactRealm.Current.ShadeActorCanonChamberArrs;

    private Dictionary<uint, CanonCellSetRoute> _canonCoursesByHolder =>
        _impactRealm.Current.ShadeActorCanonChamberArrCourses;

    private Dictionary<uint, List<CanonPartRow>> _canonRanksByChamber =>
        _impactRealm.Current.CanonPieceListingsByChamber;

    private Dictionary<uint, uint> _ancestorOf =>
        _impactRealm.Current.ShadeDescendantAncestor;

    private Dictionary<uint, List<uint>> _descendantsOf =>
        _impactRealm.Current.ShadeAncestorDescendants;

    private Dictionary<uint, IReadOnlyList<ProxyShape>> _descendantForms =>
        _impactRealm.Current.ShadeDescendantPieceArrs;

    private Dictionary<uint, EnrolmentRecord> _enrolments =>
        _impactRealm.Current.ShadeActorRegistrations;

    private Dictionary<uint, ulong> _versions =>
        _impactRealm.Current.ShadeHolderVersions;

    private Dictionary<uint, HashSet<uint>> _stemsByHolder =>
        _impactRealm.Current.ShadeHolderStems;

    private Dictionary<uint, List<uint>> _holdersByStem =>
        _impactRealm.Current.ShadeStemHolderSockets;

    private Dictionary<uint, Dictionary<uint, int>> _holderOrdinalByStem =>
        _impactRealm.Current.ShadeStemHolderOrdinals;

    private Dictionary<uint, Stack<int>> _releaseHolderSocketsByStem =>
        _impactRealm.Current.ShadeStemSpareSockets;

    private List<uint> _holderLineup =>
        _impactRealm.Current.ShadeHolderSockets;

    private Dictionary<uint, int> _lineupOrdinal =>
        _impactRealm.Current.ShadeHolderOrdinals;

    private Stack<int> _lineupSpareSockets =>
        _impactRealm.Current.ShadeHolderSpareSockets;

    private readonly HashSet<uint> _stemTemp = new();

    private readonly List<uint> _removedStemTemp = new();

    private readonly ConditionalWeakTable<List<CanonPartRow>, ChamberRasterizeStamp> _chamberRasterizeStamps = new();

    private ulong _upcomingChamberRasterizeRev;

    private sealed class ChamberRasterizeStamp
    {
        internal ulong Rev;
    }

    public ulong FetchChamberRasterizeRev(uint chamberIdent)
    {
        if (!_canonRanksByChamber.TryGetValue(chamberIdent, out var listings) || listings.Count is 0)
            return 0;
        if (!_chamberRasterizeStamps.TryGetValue(listings, out var stamp))
        {
            stamp = new ChamberRasterizeStamp { Rev = checked(++_upcomingChamberRasterizeRev) };
            _chamberRasterizeStamps.Add(listings, stamp);
        }
        return stamp.Rev;
    }

    public IReadOnlyList<ProxyEntry> FetchObjectsInChamber(uint chamberIdent)
    {
        if (_ranksByChamber.TryGetValue(chamberIdent, out var roster))
            return roster;
        return System.Array.Empty<ProxyEntry>();
    }

    public IReadOnlyList<uint> FetchHolderChambers(uint actorIdent)
    {
        if (_chambersByHolder.TryGetValue(actorIdent, out List<uint>? chambers))
            return chambers;
        return System.Array.Empty<uint>();
    }

    private ulong _alterationRev;

    private ulong _upcomingReadiedSetLocusSealIdent;

    private ulong _previousImposedSetLocusSealIdent;

    private readonly HashSet<ulong> _queuedSetLocusDispatches = [];

    private long _setLocusRelayMissTally;

    internal event Action<uint, ulong>? OwnerMutated;

    internal event Action<uint, uint>? OwnerPrefixMembershipChanged;

    public ProxyRegistry()
        : this(new ContactWorldStateSlot())
    {
    }

    internal ProxyRegistry(ContactWorldStateSlot collisionWorld)
    {
        _impactRealm = collisionWorld
            ?? throw new ArgumentNullException(nameof(collisionWorld));
    }

    public bool HasHolderRanksInLb(uint holderIdent, uint lbIdent)
    {
        return _chambersByHolder.TryGetValue(holderIdent, out List<uint>? chambers)
        && chambers.Exists(chamber =>
            (chamber & 0xFFFF0000u) == (lbIdent & 0xFFFF0000u));
    }

    internal sealed record EnrolmentRecord(
        uint SeedCellId,
        Vector3 EntityWorldPos,
        Quaternion EntityWorldRot,
        uint State,
        ActorImpactFlagSet Flags,
        bool IsStatic,
        bool IsMultiPart,
        uint GfxObjId,
        float Radius,
        ProxyContactType CollisionType,
        float CylHeight,
        float Scale);

    public bool HasLogicalHolder(uint actorIdent) =>
        _enrolments.ContainsKey(actorIdent);

    internal ulong AlterationRev => _alterationRev;

    public int StemHolderSocketCapForTelemetry(uint lbIdent)
    {
        return _holdersByStem.TryGetValue(
            lbIdent & 0xFFFF0000u,
            out List<uint>? sockets)
                ? sockets.Count
                : 0;
    }

    public void Clear()
    {
        bool mutated = _ranksByChamber.Count is not 0
            || _chambersByHolder.Count is not 0
            || _enrolments.Count is not 0
            || _dormantHolders.Count is not 0
            || _dormantHolderChambers.Count is not 0
            || _upcomingReadiedSetLocusSealIdent
                != _previousImposedSetLocusSealIdent;
        if (mutated)
            ProgressAlterationRev();
        _ranksByChamber.Clear();
        _chambersByHolder.Clear();
        _dormantHolders.Clear();
        _dormantHolderChambers.Clear();
        _withdrawnByHolder.Clear();
        _formsByHolder.Clear();
        _enrolments.Clear();
        _versions.Clear();
        _stemsByHolder.Clear();
        _holdersByStem.Clear();
        _holderOrdinalByStem.Clear();
        _releaseHolderSocketsByStem.Clear();
        _holderLineup.Clear();
        _lineupOrdinal.Clear();
        _lineupSpareSockets.Clear();
        _stemTemp.Clear();
        _removedStemTemp.Clear();
        _queuedSetLocusDispatches.Clear();
        _canonPiecesByHolder.Clear();
        _canonChambersByHolder.Clear();
        _canonCoursesByHolder.Clear();
        _canonRanksByChamber.Clear();
        _chamberRasterizeStamps.Clear();
        _ancestorOf.Clear();
        _descendantsOf.Clear();
        _descendantForms.Clear();
        _backup = null;
    }

    public KineticAssetCache? DataCache { get; set; }

    private KineticAssetCache _tempStash => _backup ??= new KineticAssetCache();

    private KineticAssetCache? _backup;

    private KineticAssetCache StashForFlood => DataCache ?? _tempStash;

    public IEnumerable<ProxyEntry> AllListingsForDiag()
    {
        HashSet<uint> observedActors = new HashSet<uint>();
        foreach (var kvp in _chambersByHolder)
        {
            uint actorIdent = kvp.Key;
            if (!observedActors.Add(actorIdent)) continue;

            foreach (uint chamberIdent in kvp.Value)
            {
                if (!_ranksByChamber.TryGetValue(chamberIdent, out var roster)) continue;
                bool anyLocated = false;
                foreach (var listing in roster)
                {
                    if (listing.EntityId == actorIdent)
                    {
                        yield return listing;
                        anyLocated = true;
                    }
                }
                if (anyLocated) break;
            }
        }
    }

    internal void FastenImpactRealm(ContactWorldStateSlot impactRealm)
    {
        ArgumentNullException.ThrowIfNull(impactRealm);
        if (_ranksByChamber.Count is not 0 || _enrolments.Count is not 0)
        {
            throw new InvalidOperationException(
                "A populated shadow registry can't change collision roots");
        }
        _impactRealm = impactRealm;
        ProgressAlterationRev();
    }

    public int SumRegistered => _chambersByHolder.Count;

    public int KeptEnrollmentTally => _enrolments.Count;

    public int WithdrawnStemMarkerTally
    {
        get
        {
            int tally = 0;
            foreach (var stems in _withdrawnByHolder.Values)
                tally += stems.Count;
            return tally;
        }
    }

    public int SuspendedEnrollmentTally => _dormantHolders.Count;

    internal ulong FetchHolderVer(uint actorIdent)
    {
        return _versions.TryGetValue(actorIdent, out ulong ver)
            ? ver
            : 0UL;
    }

    internal bool HolderTouchesLb(uint actorIdent, uint lbIdent)
    {
        uint stem = lbIdent & 0xFFFF0000u;
        if (!_enrolments.TryGetValue(actorIdent, out EnrolmentRecord? capture))
            return false;
        if ((capture.SeedCellId & 0xFFFF0000u) == stem)
            return true;
        if (_chambersByHolder.TryGetValue(actorIdent, out List<uint>? chambers)
            && chambers.Exists(chamber => (chamber & 0xFFFF0000u) == stem))

            return true;
        return _withdrawnByHolder.TryGetValue(
                actorIdent,
                out HashSet<uint>? withdrawn)
            && withdrawn.Contains(stem);
    }

    internal bool IsStaticHolderRootedIn(uint actorIdent, uint lbIdent)
    {
        return _enrolments.TryGetValue(actorIdent, out EnrolmentRecord? enrollment)
        && enrollment.IsStatic
        && (enrollment.SeedCellId & 0xFFFF0000u)
            == (lbIdent & 0xFFFF0000u);
    }

    internal bool TryFetchStaticHolderTrunkStem(
        uint actorIdent,
        out uint lbStem)
    {
        if (_enrolments.TryGetValue(
                actorIdent,
                out EnrolmentRecord? enrollment)
            && enrollment.IsStatic)
        {
            lbStem = enrollment.SeedCellId & 0xFFFF0000u;
            return true;
        }
        lbStem = 0u;
        return false;
    }

    internal bool TryFetchImpactHolder(
        uint actorIdent,
        out uint kineticsPhase,
        out bool isStatic)
    {
        if (_enrolments.TryGetValue(
                actorIdent,
                out EnrolmentRecord? enrollment))
        {
            kineticsPhase = enrollment.State;
            isStatic = enrollment.IsStatic;
            return true;
        }

        kineticsPhase = 0u;
        isStatic = false;
        return false;
    }

    private void StampChamber(List<CanonPartRow> listings)
    {
        if (_chamberRasterizeStamps.TryGetValue(listings, out var stamp))
            stamp.Rev = checked(++_upcomingChamberRasterizeRev);
    }

    private void StampHolder(uint actorIdent)
    {
        if (!_canonChambersByHolder.TryGetValue(actorIdent, out var chambers))
            return;
        foreach (uint chamberIdent in chambers)
        {
            if (_canonRanksByChamber.TryGetValue(chamberIdent, out var listings))
                StampChamber(listings);
        }
    }

    public int HolderVerTallyForTelemetry => _versions.Count;

    public int StemHolderVesselTallyForTelemetry =>
        _holdersByStem.Count;

    private void ProgressAlterationRev() =>
        _alterationRev = checked(_alterationRev + 1UL);

    private void BumpHolderVer(uint actorIdent)
    {
        ProgressAlterationRev();
        ulong ver = checked(FetchHolderVer(actorIdent) + 1UL);
        _versions[actorIdent] = ver;
        ReindexHolderStems(actorIdent);
        StampHolder(actorIdent);
        OwnerMutated?.Invoke(actorIdent, ver);
    }
}
