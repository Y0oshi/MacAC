using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Realm;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics;

public sealed partial class EquippedChildRenderDriver : IDisposable
{
    private readonly IDatAccess _datFiles;

    private readonly object _datMutex;

    private readonly ClientThingChart _objects;

    private readonly OnlineActorCore _onlineActors;

    private readonly Func<AncestorSignal.Parsed, bool> _admitAncestor;

    private readonly Func<OnlineActorRecord, ulong, ulong, ExactMirrorWithdrawalVerdict>
        _withdrawProj;

    private readonly ActorEffectPoseRegistry _postures;

    private readonly ProxyRegistry _shades;

    private readonly KineticAssetCache _kineticsBlob;

    private readonly Action<uint, PartMesh> _broadcastGfxObjRef;

    // Raised after the attached projection is fully registered
    internal event Action<OnlineActorReadyCandidate>? EntityReady;

    public event Action<uint>? ProjectionPoseReady;

    public event Action<OnlineActorRecord>? ProjectionRemoved;

    private readonly Dictionary<SimActorKey, AffixedDescendant> _attachedByChild = [];

    private readonly Dictionary<SimActorKey, QueuedUnparentChangeover>
        _pendingUnparentByChild = [];

    private readonly Dictionary<SimActorKey, QueuedPlainDeletion>
        _pendingOrdinaryRemovalByRoot = [];

    private readonly Dictionary<SimActorKey, PendingMirrorSubtree>
        _pendingDetachedRemovalByChild = [];

    private readonly Dictionary<SimActorKey, PendingMirrorSubtree>
        _pendingReparentRemovalByChild = [];

    private readonly Dictionary<SimActorKey, PendingMirrorSubtree>
        _pendingPoseLossRemovalByChild = [];

    private readonly Dictionary<SimActorKey, PendingMirrorSubtree>
        _pendingOrphanRemovalByChild = [];

    private readonly List<uint> _queuedProjDescendantsTemp = [];

    private readonly List<KeyValuePair<SimActorKey, QueuedPlainDeletion>>
        _queuedPlainDeletionTemp = [];

    private readonly List<KeyValuePair<SimActorKey, PendingMirrorSubtree>>
        _queuedProjSubtreeTemp = [];

    private readonly List<KeyValuePair<SimActorKey, QueuedUnparentChangeover>>
        _queuedUnparentTemp = [];

    private readonly AttachmentPulseOrder<SimActorKey, AffixedDescendant>
        _refreshOrdering = new();

    private readonly AttachmentPulseOrder<uint, uint> _relationRecoveryOrdering =
        new();

    private readonly Func<AffixedDescendant, SimActorKey?> _ancestorOfAffixed;

    private readonly Func<SimActorKey, bool> _beatAffixed;

    private readonly Func<SimActorKey, bool> _reconcileAffixed;

    private int _engagedPostureCompositionVisits;

    private readonly HashSet<uint> _loggedUnaddressableAncestorRefusals = [];

    public EquippedChildRenderDriver(
        IDatAccess dats,
        object datLock,
        ClientThingChart objects,
        OnlineActorCore liveEntities,
        ActorEffectPoseRegistry poses,
        Func<AncestorSignal.Parsed, bool> acceptParent,
        Func<OnlineActorRecord, ulong, ulong, ExactMirrorWithdrawalVerdict>
            withdrawProjection,
        ProxyRegistry shadows,
        KineticAssetCache physicsData,
        Action<uint, PartMesh> publishGfxObj)
    {
        _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
        _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
        _postures = poses ?? throw new ArgumentNullException(nameof(poses));
        _admitAncestor = acceptParent ?? throw new ArgumentNullException(nameof(acceptParent));
        _withdrawProj = withdrawProjection
            ?? throw new ArgumentNullException(nameof(withdrawProjection));
        _shades = shadows ?? throw new ArgumentNullException(nameof(shadows));
        _kineticsBlob = physicsData ?? throw new ArgumentNullException(nameof(physicsData));
        _broadcastGfxObjRef = publishGfxObj
            ?? throw new ArgumentNullException(nameof(publishGfxObj));
        _ancestorOfAffixed = static descendant => descendant.ParentRecord.ProjTag;
        _beatAffixed = PulseDescendant;
        _reconcileAffixed = SettleDescendant;

        _objects.ObjectMoved += OnObjectMoved;
        _objects.MoveRolledBack += OnRelocateRolledBack;
        _objects.ObjectRemovalClassified += OnObjectDeletionClassified;
    }

    private static void DuplicateListings<T>(
        Dictionary<SimActorKey, T> src,
        List<KeyValuePair<SimActorKey, T>> dest)
    {
        dest.Clear();
        foreach (KeyValuePair<SimActorKey, T> listing in src)
            dest.Add(listing);
    }

    private readonly record struct MirrorPreparationResult(
        bool CanAdvanceWireQueue,
        bool Projected);

    private sealed record AffixedDescendant(
        OnlineActorRecord ParentRecord,
        OnlineActorRecord ChildRecord,
        uint ParentGuid,
        uint ChildGuid,
        AttachSlot ParentLocation,
        PlacementId Placement,
        RigSpec ParentSetup,
        RigSpec ChildSetup,
        IReadOnlyList<TriMeshRef> PartTemplate,
        IReadOnlyList<bool> PartAvailability,
        Matrix4x4[] PartPoseBuffer,
        TriMeshRef[] AttachedPartBuffer,
        float Scale,
        RealmActor Entity)
    {
        public RealmActor? PreviousAncestorActor { get; set; }
        public ulong PreviousAncestorPostureVer { get; set; }
        public bool PreviousAncestorPaintShown { get; set; }
        public bool PreviousParentAncestorDrawVisible { get; set; }
        public uint? PreviousAncestorChamberIdent { get; set; }
    }

    private readonly record struct QueuedUnparentChangeover(
        OnlineActorRecord Record,
        ulong PositionAuthorityVersion,
        PendingMirrorSubtree Subtree,
        Action? Continuation);

    private readonly record struct PendingMirrorSubtree(
        OnlineActorRecord RootRecord,
        ulong RootPositionAuthorityVersion,
        IReadOnlyList<MirrorRemovalCapture> Captures,
        int NextIndex,
        bool RestoreRootRelation,
        bool RestoreDescendantRelations);

    private readonly record struct MirrorRemovalCapture(
        OnlineActorRecord Record,
        ulong PositionAuthorityVersion,
        ulong ProjectionMutationVersion,
        AffixedDescendant? Attached,
        bool IsRoot);

    private readonly record struct AffixedDeletionGrab(
        AffixedDescendant Attached,
        ulong PositionAuthorityVersion,
        ulong ProjectionMutationVersion);

    private readonly record struct QueuedPlainDeletion(
        OnlineActorRecord RootRecord,
        IReadOnlyList<AffixedDeletionGrab> Captures,
        int NextIndex);
}

public enum DescendantUnparentDisposition
{
    NotAttached,
    Completed,
    Pending,
    Superseded,
}

internal enum ParentMirrorAuditDisposition
{
    Ready,
    Waiting,
    Rejected,
}
