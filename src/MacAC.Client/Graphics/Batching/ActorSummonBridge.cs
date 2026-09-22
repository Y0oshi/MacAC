using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics.Batching;

public sealed class ActorDisplayRemovalDeferredException(uint srvOid)
    : InvalidOperationException(
        $"Live entity 0x{srvOid:X8} presentation removal is deferred until its active reference transition completes.");

public sealed partial class ActorSummonBridge
{
    private readonly IActorTextureLifetime _textureLifespan;

    private readonly Func<RealmActor, AnimSequencer> _schedulerMaker;

    private readonly IBatchMeshBridge? _triMeshBridge;

    private readonly Dictionary<SimActorKey, Holder> _ownersByKey = [];

    private sealed class Holder(
        SimActorKey tag,
        RealmActor actor,
        AnimatedActorLedger phase,
        HashSet<ulong> triMeshIdents)
    {
        public SimActorKey Key { get; } = tag;
        public RealmActor Entity { get; } = actor;
        public AnimatedActorLedger State { get; } = phase;
        public HashSet<ulong> TriMeshIdents { get; set; } = triMeshIdents;
        public HashSet<ulong> TriMeshReferencesPinned { get; } = [];
        public bool IsExhibitHoused { get; set; }
        public bool TextureFreeNeeded { get; set; }
        public DisplayTransition Transition { get; set; }
        public bool DeletionQueued { get; set; }

        public bool HasExhibitAssetList => TextureFreeNeeded ? true : TriMeshReferencesPinned.Count is not 0;

        public bool IsFullyHoused
        {
            get
            {
                return !IsExhibitHoused || !TextureFreeNeeded ? false : TriMeshReferencesPinned.SetEquals(TriMeshIdents);
            }
        }

        public bool IsFullySuspended =>
            !IsExhibitHoused && !HasExhibitAssetList;
    }

    private enum DisplayTransition
    {
        None,
        Resuming,
        Suspending,
        ChangingAppearance,
    }

    public ActorSummonBridge(
        IActorTextureLifetime textureLifespan,
        Func<RealmActor, AnimSequencer> schedulerMaker,
        IBatchMeshBridge? triMeshBridge = null)
    {
        ArgumentNullException.ThrowIfNull(textureLifespan);
        ArgumentNullException.ThrowIfNull(schedulerMaker);
        _textureLifespan = textureLifespan;
        _schedulerMaker = schedulerMaker;
        _triMeshBridge = triMeshBridge;
    }
}
