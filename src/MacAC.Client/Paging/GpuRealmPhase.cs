using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Realm;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Paging;

internal sealed record GpuLandblockSpatialBulletin(
    uint LandblockId,
    MountedLandblock Landblock,
    IReadOnlyList<ulong> AdditionalRenderIds,
    IReadOnlyList<RealmActor> StaticEntities,
    bool RequiresActivation = true,
    uint RenderTraversalOrder = 0,
    IReadOnlyList<ulong>? AdditionalOrdinaryRenderIds = null,
    bool ReplacesMeshRegistration = false);

public sealed partial class GpuRealmPhase(
    LandblockSpawnBridge? wbSummonBridge = null,
    System.Action<uint>? onLbUnloaded = null,
    ActorProgramActivator? actorProgramActivator = null,
    IRealmEpochAvailability? readiness = null) : IOnlineActorSpatialProbe
{
    private readonly LandblockSpawnBridge? _wbSummonBridge = wbSummonBridge;

    private readonly ActorProgramActivator? _actorProgramActivator = actorProgramActivator;

    private readonly IRealmEpochAvailability _readiness = readiness ?? AlwaysAvailableRealmEpoch.Instance;

    private readonly System.Action<uint>? _onLbUnloaded = onLbUnloaded;
    private readonly Dictionary<uint, MountedLandblock> _fetched = [];

    private readonly List<uint> _rasterizeTraversalLbSockets = [];

    private readonly Stack<int> _releaseRasterizeTraversalLbSockets = [];

    private readonly Dictionary<uint, int> _rasterizeTraversalSocketByLb = [];

    private readonly Dictionary<uint, LandblockFlowTier> _tierByLb = [];

    private readonly Dictionary<uint, (Vector3 Min, Vector3 Max)> _aabbs = [];

    private readonly Dictionary<uint, AnimatedActorIndex> _movingOrdinalByLb = [];

    private readonly List<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
        IReadOnlyList<RealmActor> Entities,
        IReadOnlyDictionary<uint, RealmActor>? AnimatedById)>
        _lbListingsLens = [];

    private readonly List<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
        IReadOnlyList<RealmActor> Entities,
        IReadOnlyDictionary<uint, RealmActor>? AnimatedById)>
        _lbListingsWithoutMovingOrdinalLens = [];

    private readonly List<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax)>
        _lbLimitsLens = [];

    private bool _lbListingsLensStale = true;

    private bool _lbListingsWithoutMovingOrdinalLensStale = true;

    private bool _lbLimitsLensStale = true;

    private sealed record AnimatedActorIndex(
        MountedLandblock Source,
        IReadOnlyDictionary<uint, RealmActor> EntitiesById);

    private readonly Dictionary<uint, List<RealmActor>> _queuedByLb = [];

    private readonly Dictionary<uint, HashSet<ulong>> _queuedRasterizeIdentsByLb = [];

    private readonly HashSet<uint> _queuedNearbyTierLbs = [];

    private readonly HashSet<uint> _persistentOids = [];

    private readonly List<RealmActor> _planarActors = [];

    private readonly Dictionary<RealmActor, int> _planarActorOrdinals =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<RealmActor, MirrorLocation> _projLocales =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<uint, HashSet<RealmActor>> _fetchedOnlineByLb = [];

    private readonly Dictionary<SimActorKey, RealmActor> _liveProjectionByKey = [];

    private readonly Dictionary<SimActorKey, int> _visibleLiveProjectionCounts = [];

    private readonly Dictionary<SimActorKey, bool> _visibilityBeforeMutation = [];

    private readonly Queue<(SimActorKey Key, bool Visible)> _visChangeovers = new();

    private readonly List<RealmActor> _oidDeletionTemp = [];

    private bool _dispatchingVisChangeovers;

    private int _alterationZDepth;
    private bool _planarMembershipStale;

    public event Action<SimActorKey, bool>? LiveProjectionVisibilityChanged;

    private IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                           IReadOnlyList<RealmActor> Entities,
                           IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> FetchLbListingsLens(
        bool includeMovingOrdinal)
    {
        if (!_readiness.IsRealmOnHand)
        {
            return Array.Empty<(uint, Vector3, Vector3,
                IReadOnlyList<RealmActor>,
                IReadOnlyDictionary<uint, RealmActor>?)>();
        }

        List<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
            IReadOnlyList<RealmActor> Entities,
            IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> lens =
            includeMovingOrdinal
                ? _lbListingsLens
                : _lbListingsWithoutMovingOrdinalLens;
        bool stale = includeMovingOrdinal
            ? _lbListingsLensStale
            : _lbListingsWithoutMovingOrdinalLensStale;
        if (!stale)
            return lens;

        lens.Clear();

        for (int socket = 0; socket < _rasterizeTraversalLbSockets.Count; ++socket)
        {
            uint lbIdent = _rasterizeTraversalLbSockets[socket];
            if (lbIdent is 0)
                continue;
            var lb = _fetched[lbIdent];
            IReadOnlyDictionary<uint, RealmActor>? byIdent = null;
            if (includeMovingOrdinal)
            {
                if (!_movingOrdinalByLb.TryGetValue(lbIdent, out var stashed)
                    || !ReferenceEquals(stashed.Source, lb))
                {
                    var rebuilt = new Dictionary<uint, RealmActor>(lb.Entities.Count);
                    foreach (var actor in lb.Entities)
                        rebuilt[actor.Id] = actor;

                    stashed = new AnimatedActorIndex(lb, rebuilt);
                    _movingOrdinalByLb[lbIdent] = stashed;
                }

                byIdent = stashed.EntitiesById;
            }

            if (_aabbs.TryGetValue(lbIdent, out var aabb))
                lens.Add(
                    (lbIdent, aabb.Min, aabb.Max, lb.Entities, byIdent));
            else
                lens.Add(
                    (lbIdent, Vector3.Zero, Vector3.Zero, lb.Entities, byIdent));
        }

        if (includeMovingOrdinal)
            _lbListingsLensStale = false;
        else
            _lbListingsWithoutMovingOrdinalLensStale = false;
        return lens;
    }

    public readonly ref struct AlterationLot(GpuRealmPhase holder)
    {
        private readonly GpuRealmPhase _holder = holder;

        public void Dispose() => _holder.DisposeRest();
    }

    private readonly record struct MirrorLocation(
        uint LandblockId,
        bool IsLoaded,
        int BucketIndex,
        SimActorKey Key);

    private readonly List<RealmActor> _persistentRescued = [];

    private readonly HashSet<uint> _persistentInPlanarSensor = [];
}
