using System.Numerics;
using System.Runtime.ExceptionServices;
using MacAC.Client.Paging;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Realm;

public interface IOnlineActorMotionEngine
{
    RealmActor Entity { get; }
    uint CurrentMotion { get; }
}

public interface IOnlineActorEffectProfile { }

public enum OnlineActorMirrorKind
{
    World,
    Attached,
}

internal enum OnlineActorMaterializationResidence
{
    LegacyImmediate,
    AwaitRuntimePlacement,
}

internal enum EquippedChildDisplayRebucketDisposition
{
    Moved,
    NotAttached,
    NoProjection,
    Displaced,
}

public interface IOnlineActorResourceLifespan
{
    void Register(RealmActor actor);
    void Unregister(RealmActor actor);
}

internal interface IOnlineActorEngineComponentLifespan
{
    void TearDown(OnlineActorRecord capture);
}

internal sealed class NullOnlineActorEngineComponentLifespan :
    IOnlineActorEngineComponentLifespan
{
    public static NullOnlineActorEngineComponentLifespan Instance { get; } = new();
    private NullOnlineActorEngineComponentLifespan() { }
    public void TearDown(OnlineActorRecord capture) { }
}

internal sealed class DelegateOnlineActorEngineComponentLifespan(
    Action<OnlineActorRecord> tearDown) : IOnlineActorEngineComponentLifespan
{
    private readonly Action<OnlineActorRecord> _tearDown =
        tearDown ?? throw new ArgumentNullException(nameof(tearDown));

    public void TearDown(OnlineActorRecord capture) => _tearDown(capture);
}

internal sealed class DeferredOnlineActorEngineComponentLifespan :
    IOnlineActorEngineComponentLifespan
{
    private IOnlineActorEngineComponentLifespan? _interior;

    public bool IsTied => Volatile.Read(ref _interior) is not null;

    public void Bind(IOnlineActorEngineComponentLifespan lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        if (ReferenceEquals(lifecycle, this))
            throw new ArgumentException("A lifecycle bridge can't bind to itself", nameof(lifecycle));
        if (Interlocked.CompareExchange(ref _interior, lifecycle, null) is not null)
            throw new InvalidOperationException("The live-entity runtime component lifecycle is by now bound");
    }

    public IDisposable BindOwned(IOnlineActorEngineComponentLifespan lifecycle)
    {
        Bind(lifecycle);
        return new Binding(this, lifecycle);
    }

    private void Loosen(IOnlineActorEngineComponentLifespan anticipated) => _ = Interlocked.CompareExchange(ref _interior, null, anticipated);

    public void TearDown(OnlineActorRecord capture)
    {
        IOnlineActorEngineComponentLifespan lifecycle = Volatile.Read(ref _interior)
            ?? throw new InvalidOperationException(
                "The live-entity runtime component lifecycle hasn't been bound");
        lifecycle.TearDown(capture);
    }

    private sealed class Binding(
        DeferredOnlineActorEngineComponentLifespan holder,
        IOnlineActorEngineComponentLifespan anticipated) : IDisposable
    {
        private DeferredOnlineActorEngineComponentLifespan? _holder = holder;
        private readonly IOnlineActorEngineComponentLifespan _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(_anticipated);
    }
}

public sealed class DelegateOnlineActorResourceLifespan(
    Action<RealmActor> register,
    Action<RealmActor> unregister) : IOnlineActorResourceLifespan
{
    private readonly Action<RealmActor> _enroll = register ?? throw new ArgumentNullException(nameof(register));
    private readonly Action<RealmActor> _withdraw = unregister ?? throw new ArgumentNullException(nameof(unregister));

    public void Register(RealmActor actor) => _enroll(actor);
    public void Unregister(RealmActor actor) => _withdraw(actor);
}

public sealed class OnlineActorRecord
{
    private readonly SimActorIndex _directory;

    internal OnlineActorRecord(
        SimActorIndex directory,
        SimActorRecord canonical)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        Canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
    }

    internal SimActorRecord Canonical { get; }
    internal SimActorKey? ProjTag { get; set; }
    public uint ServerOid => Canonical.ServerGuid;
    public ushort Generation => Canonical.Incarnation;
    public RealmSession.MoverSpawn Snapshot
    {
        get => Canonical.Snapshot;
        internal set => _directory.RenewCapture(Canonical, value);
    }
    public RealmActor? WorldEntity { get; internal set; }
    public uint? OwnActorIdent => Canonical.OwnActorTag ?? WorldEntity?.Id;
    public uint WholeChamberIdent
    {
        get => Canonical.WholeChamberTag;
        internal set => _directory.AssignWholeChamber(
            Canonical,
            value,
            Canonical.CanonLbTag);
    }
    public uint CanonLbIdent
    {
        get => Canonical.CanonLbTag;
        internal set => _directory.AssignWholeChamber(
            Canonical,
            Canonical.WholeChamberTag,
            value);
    }
    public uint RawKineticsPhase => Canonical.RawKineticsCondition;
    public KineticStateFlags FinalKineticsPhase
    {
        get => Canonical.FinalKineticsCondition;
        internal set => _directory.AssignFinalKineticsPhase(Canonical, value);
    }
    public CanonQuantumClock ObjectTimer => Canonical.ObjectClock;
    internal ulong ObjectTimerEpoch => Canonical.ObjectTimerEpoch;
    public bool HasPieceArr
    {
        get => Canonical.HasPieceArray;
        internal set => _directory.AssignHasPieceArr(Canonical, value);
    }
    public KineticBody? KineticBody => Canonical.KineticBody;
    internal bool KineticsCorpusAcquisitionInHeadway => Canonical.KineticsCorpusAcquisitionInHeadway;
    public IOnlineActorMotionEngine? AnimationRuntime { get; internal set; }
    public ISimPeerMotion? RemoteMotionRuntime => Canonical.PeerMotion;
    internal bool DistantLocomotionMappingInHeadway => Canonical.DistantLocomotionMappingInHeadway;
    public MacAC.Mechanics.Kinetics.Gait.IKineticObjHost? PhysicsHost => Canonical.PhysicsHost;
    internal bool RequiresDistantStanceCore => Canonical.RequiresDistantStanceCore;
    public ISimMissile? ProjectileRuntime => Canonical.Projectile;
    public IOnlineActorEffectProfile? EffectProfile { get; internal set; }
    public bool ResourcesRegistered { get; internal set; }
    internal OnlineActorMaterializationResidence? MaterializationResidence
    { get; set; }
    public bool IsSpatiallyProjected { get; internal set; }
    public bool IsSpatiallyVisible { get; internal set; }
    internal ulong ProjAlterationVer { get; set; }
    internal ulong PositionAuthorityVersion => Canonical.PositionAuthorityVersion;
    internal ulong PhaseArbiterVer => Canonical.PhaseArbiterVer;
    internal ulong VectorArbiterVer => Canonical.VectorArbiterVer;
    internal ulong VelArbiterVer => Canonical.VelArbiterVer;
    internal ulong TravelArbiterVer => Canonical.TravelArbiterVer;
    internal ulong ObjRefDscArbiterVer => Canonical.ObjRefDscArbiterVer;
    internal ulong BuildIntegrationVer => Canonical.BuildIntegrationVersion;
    public bool RealmSummonPublished { get; internal set; }
    public bool StartingHydrationFinished { get; internal set; }
    internal bool BuildProjSynchronizationQueued { get; set; }
    internal bool ProjHydrationInHeadway { get; set; }
    internal ulong ProjHydrationBuildVer { get; set; }
    internal bool ProjHydrationReattemptAsked { get; set; }
    // A renderer mesh transaction can be re-entered by a newer accepted ObjDesc
    internal bool LooksProjSynchronizationQueued { get; set; }
    internal bool LooksHydrationInHeadway { get; set; }
    internal ulong LooksHydrationVer { get; set; }
    internal bool LooksHydrationReattemptAsked { get; set; }
    public OnlineActorMirrorKind ProjSort { get; internal set; }
    internal bool CoreModulesTeardownFinished { get; set; }
    internal OnlineActorTeardownPlan? CoreModuleTeardownPlan { get; set; }
    internal bool SpatialProjTeardownFinished { get; set; }
    internal bool TeardownInHeadway { get; set; }
    internal bool EraseApprovedForTeardown
    {
        get => Canonical.EraseApprovedForTeardown;
        set => _directory.AssignEraseApprovedForTeardown(Canonical, value);
    }

    internal bool TryDequeuePhaseChangeover(out CanonKineticShift changeover) =>
        _directory.TryDequeuePhaseChangeover(Canonical, out changeover);

    internal void SuspendObjectTimer() => _directory.SuspendObjectTimer(Canonical);

    internal void RestartObjectTimerForJoinRealm(bool isStatic) =>
        _directory.RestartObjectTimerForJoinRealm(Canonical, isStatic);

    internal CanonKineticShift ImposeRawKineticsPhase(uint rawPhase) =>
        _directory.ImposeRawKineticsPhase(Canonical, rawPhase);

    internal void NoteObjRefDscProjSynchronization()
    {
        LooksProjSynchronizationQueued = true;
        if (LooksHydrationInHeadway)
            LooksHydrationReattemptAsked = true;
    }

    public uint ProjChamberIdent => WorldEntity is not null
        ? WholeChamberIdent
        : Snapshot.Position?.LandblockId ?? WholeChamberIdent;

    internal void RenewDerivedPhase(bool renewLocus = true) =>
        _directory.RenewCapture(Canonical, Snapshot, renewLocus);
}

public readonly record struct OnlineActorRegistrationResult(
    IncomingBuildOutcome Inbound,
    SimActorRecord? Canonical,
    OnlineActorRecord? Projection,
    bool LogicalRegistrationCreated,
    bool ReplacedExistingGeneration,
    Exception? PriorGenerationCleanupFailure = null);

public sealed class OnlineActorCore : IOnlineActorRadarSource
{
    public const uint LeadOnlineActorIdent = SimActorIndex.LeadOwnActorIdent;
    public const uint PreviousOnlineActorIdent = SimActorIndex.PreviousOwnActorIdent;

    private readonly GpuRealmPhase _spatial;
    private readonly IOnlineActorResourceLifespan _assetList;
    private readonly IOnlineActorEngineComponentLifespan _coreModuleLifecycle;
    private readonly SimActorObjectLifetime _entityObjects;
    private readonly SimActorIndex _directory;
    private readonly OnlineActorMirrorStore _projections;
    private readonly Dictionary<SimActorKey, IOnlineActorMotionEngine> _spatialAnims = [];
    private readonly List<SimActorRecord> _spatialTrunkCanonTemp = [];
    private readonly List<SimActorRecord> _spatialDistantCanonTemp = [];
    private readonly Dictionary<SimActorKey, int>
        _exhibitSoleSpatialAlterationZDepth = [];
    private bool _isClearing;
    private bool _sessWipeQueuedFinalization;
    private bool _isRegisteringAssetList;
    private int _logicalTeardownZDepth;
    private uint _rebucketingOid;

    public OnlineActorCore(
        GpuRealmPhase spatial,
        IOnlineActorResourceLifespan assetList,
        SimActorObjectLifetime actorObjects)
        : this(
            spatial,
            assetList,
            NullOnlineActorEngineComponentLifespan.Instance,
            actorObjects)
    {
    }

    public OnlineActorCore(
        GpuRealmPhase spatial,
        IOnlineActorResourceLifespan assetList,
        Action<OnlineActorRecord> tearDownCoreModules,
        SimActorObjectLifetime actorObjects)
        : this(
            spatial,
            assetList,
            new DelegateOnlineActorEngineComponentLifespan(tearDownCoreModules),
            actorObjects)
    {
    }

    internal OnlineActorCore(
        GpuRealmPhase spatial,
        IOnlineActorResourceLifespan resources,
        IOnlineActorEngineComponentLifespan? runtimeComponentLifecycle,
        SimActorObjectLifetime entityObjects)
    {
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _assetList = resources ?? throw new ArgumentNullException(nameof(resources));
        _coreModuleLifecycle = runtimeComponentLifecycle
            ?? throw new ArgumentNullException(nameof(runtimeComponentLifecycle));
        _entityObjects = entityObjects
            ?? throw new ArgumentNullException(nameof(entityObjects));
        _directory = _entityObjects.Entities;
        _physics = _entityObjects.Physics;
        _projections = new OnlineActorMirrorStore(_directory);
        _spatial.LiveProjectionVisibilityChanged += OnSpatialVisAltered;
        _physics.CellCommitted += OnCoreKineticsChamberSealed;
    }

    public int Count => _directory.Count;
    public int PendingTeardownCount => _directory.PendingTeardownCount;
    public int MaterializedTally => _projections.MaterializedCount;
    public IReadOnlyCollection<OnlineActorRecord> Records => _projections.EngagedRecords;
    public IReadOnlyCollection<OnlineActorRecord> MaterializedRecords =>
        _projections.MaterializedRecords;
    public IReadOnlyCollection<OnlineActorRecord> ShownRecords =>
        _projections.VisibleRecords;
    internal IReadOnlyCollection<SimActorRecord> CanonRecords =>
        _directory.ActiveRecords;
    internal ulong SessionLifetimeVersion => _directory.SessionLifetimeVersion;
    private readonly SimKineticsLedger _physics;

    internal SimKineticsLedger Physics => _physics;
    public IReadOnlyDictionary<uint, RealmSession.MoverSpawn> Snapshots => _directory.Snapshots;
    internal int AnimCoreTally
    {
        get
        {
            int tally = 0;
            IReadOnlyList<OnlineActorRecord> records = _projections.EngagedRecords;
            for (int idx = 0; idx < records.Count; idx++)
            {
                if (records[idx].AnimationRuntime is not null)
                    tally++;
            }
            return tally;
        }
    }
    internal int SpatialAnimCoreTally => _spatialAnims.Count;
    internal int SpatialDistantLocomotionCoreTally => _physics.SpatialDistantTally;
    internal int SpatialMissileCoreTally => _physics.SpatialMissileTally;
    internal int SpatialTrunkObjectTally => _physics.SpatialTrunkTally;
    public AnchorAttachmentLedger AncestorAffixes => _directory.AncestorAttachments;

    internal bool TryFetchCanon(
        uint srvOid,
        out SimActorRecord canon) =>
        _directory.TryFetchEngaged(srvOid, out canon);

    internal bool TryFetchProj(
        SimActorRecord canon,
        out OnlineActorRecord capture) =>
        _projections.TryGet(canon, out capture);

    internal bool IsLatestCanon(SimActorRecord canon) =>
        _directory.IsCurrent(canon);

    internal bool IsLatestBuildIntegration(
        SimActorRecord canon,
        ulong anticipatedBuildIntegrationVer) =>
        _directory.IsCurrent(canon)
        && canon.BuildIntegrationVersion == anticipatedBuildIntegrationVer;

    internal void AssignHasPieceArr(
        SimActorRecord canon,
        bool val) =>
        _directory.AssignHasPieceArr(canon, val);

    public event Action<OnlineActorRecord, bool>? ProjectionVisibilityChanged;

    public OnlineActorRegistrationResult EnrollOnlineActor(
        RealmSession.MoverSpawn incoming,
        bool isOwnAvatar = false)
    {
        if (_isClearing || _sessWipeQueuedFinalization || _isRegisteringAssetList)
        {
            throw new InvalidOperationException(
                _isClearing || _sessWipeQueuedFinalization
                    ? "A live entity can't register while the session lifetime is clearing"
                    : "A live entity can't register from inside atomic resource registration");
        }

        SimActorRegistrationResult enrollment =
            _entityObjects.EnrollActorWithStartingResidence(
                incoming,
                isOwnAvatar,
                RetirePrecedingProj);
        SimActorRecord? canon = enrollment.Canonical;
        OnlineActorRecord? proj = canon is null
            ? null
            : _projections.FetchLatestOrDefault(canon.ServerGuid);
        if (proj is not null
            && ReferenceEquals(proj.Canonical, canon))
        {
            RenewSpatialCoreIndexes(proj);
        }
        return new OnlineActorRegistrationResult(
            enrollment.Inbound,
            canon,
            proj,
            enrollment.LogicalRegistrationCreated,
            enrollment.ReplacedExistingGeneration,
            enrollment.PriorGenerationCleanupFailure);
    }

    public RealmActor? MaterializeOnlineEntity(
        uint srvOid,
        uint wholeChamberIdent,
        Func<uint, RealmActor> maker,
        OnlineActorMirrorKind projSort = OnlineActorMirrorKind.World)
    {
        return !_directory.TryFetchEngaged(
                srvOid,
                out SimActorRecord canon)
            ? null
            : MaterializeOnlineActor(
            canon,
            wholeChamberIdent,
            maker,
            projSort,
            bootstrapProj: null,
            out _);
    }

    internal RealmActor? MaterializeOnlineActor(
        SimActorRecord anticipatedCanon,
        uint wholeChamberIdent,
        Func<uint, RealmActor> maker,
        OnlineActorMirrorKind projectionKind,
        Action<OnlineActorRecord>? bootstrapProj,
        out OnlineActorRecord? capture,
        OnlineActorMaterializationResidence residence =
            OnlineActorMaterializationResidence.LegacyImmediate)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCanon);
        ArgumentNullException.ThrowIfNull(maker);
        if (residence is OnlineActorMaterializationResidence
                .AwaitRuntimePlacement
            && projectionKind is not OnlineActorMirrorKind.World)
        {
            throw new ArgumentException(
                "Only a top-level world projection can await canonical Runtime placement",
                nameof(projectionKind));
        }
        capture = null;
        if (_isClearing
            || _sessWipeQueuedFinalization
            || _logicalTeardownZDepth != 0
            || _isRegisteringAssetList)
        {
            throw new InvalidOperationException(
                "A live entity can't materialize inside an active logical-lifetime transition");
        }
        if (!_directory.IsCurrent(anticipatedCanon))
            return null;

        uint srvOid = anticipatedCanon.ServerGuid;
        bool builtSidecar = false;
        if (!_projections.TryGet(anticipatedCanon, out capture))
        {
            _ = anticipatedCanon.OwnActorTag
                ?? throw new InvalidOperationException(
                    "Runtime must issue entity identity prior to App projection");
            try
            {
                capture = _projections.AppendMaterializing(anticipatedCanon);
                builtSidecar = true;
                capture.MaterializationResidence = residence;
                bootstrapProj?.Invoke(capture);
            }
            catch
            {
                if (capture is not null)
                {
                    _projections.RemoveActive(capture);
                    capture.ProjTag = null;
                    capture = null;
                }
                throw;
            }
        }
        else if (capture.MaterializationResidence != residence)
        {
            throw new InvalidOperationException(
                $"Live entity 0x{srvOid:X8}/{anticipatedCanon.Incarnation} "
                + $"can't change its materialization residence from "
                + $"{capture.MaterializationResidence} to {residence}.");
        }

        if (capture.WorldEntity is null)
        {
            uint ownIdent = anticipatedCanon.OwnActorTag
                ?? throw new InvalidOperationException(
                    "A materializing App sidecar lost its Runtime local ID");
            RealmActor actor;
            try
            {
                actor = maker(ownIdent)
                    ?? throw new InvalidOperationException(
                        "A live-entity projection factory returned null");
                if (actor.Id != ownIdent)
                {
                    throw new InvalidOperationException(
                        $"Live projection id 0x{actor.Id:X8} didn't match reserved id 0x{ownIdent:X8}.");
                }
                if (actor.ServerGuid != srvOid)
                {
                    throw new InvalidOperationException(
                        $"Live projection guid 0x{actor.ServerGuid:X8} didn't match record 0x{srvOid:X8}.");
                }
            }
            catch
            {
                if (builtSidecar)
                {
                    _projections.RemoveActive(capture);
                    capture.ProjTag = null;
                    capture = null;
                }
                throw;
            }

            capture.WorldEntity = actor;
            RenewExhibit(capture);
            _isRegisteringAssetList = true;
            try
            {
                // Registration may span several App owners.
                capture.ResourcesRegistered = true;
                try
                {
                    _assetList.Register(actor);
                }
                catch (Exception enrollmentProblem)
                {
                    Exception? undoProblem = null;
                    try
                    {
                        _assetList.Unregister(actor);
                        capture.ResourcesRegistered = false;
                    }
                    catch (Exception problem)
                    {
                        undoProblem = problem;
                    }

                    if (undoProblem is null)
                    {
                        _projections.RemoveActive(capture);
                        capture.ProjTag = null;
                        capture.WorldEntity = null;
                        capture = null;
                        throw;
                    }

                    _entityObjects.RetireFollowingProjAcquisitionMiss(
                        anticipatedCanon);
                    if (_projections.RemoveActive(capture))
                    {
                        KeepTeardownCapture(capture);
                    }
                    throw new AggregateException(
                        "Live entity resource registration and rollback both failed; " +
                        "the partial owner was retained for teardown retry",
                        enrollmentProblem,
                        undoProblem);
                }
            }
            finally
            {
                _isRegisteringAssetList = false;
            }
        }

        if (!_directory.IsCurrent(anticipatedCanon)
            || !_projections.TryGet(anticipatedCanon, out OnlineActorRecord latestCapture)
            || !ReferenceEquals(latestCapture, capture))
        {
            capture = null;
            return null;
        }

        capture.ProjSort = projectionKind;
        if (projectionKind is OnlineActorMirrorKind.World)
        {
            capture.WorldEntity!.IsAncestorPaintShown = true;
        }
        RenewExhibit(capture);
        RealmActor materialized = capture.WorldEntity!;
        if (residence is OnlineActorMaterializationResidence
                .AwaitRuntimePlacement)
        {
            capture.IsSpatiallyProjected = false;
            capture.IsSpatiallyVisible = false;
            RenewSpatialExhibitIndexes(capture);
            RenewExhibit(capture);
            return materialized;
        }
        if (!RebucketLiveEntity(srvOid, wholeChamberIdent)
            || !_projections.TryGet(anticipatedCanon, out OnlineActorRecord? latest)
            || !ReferenceEquals(latest, capture)
            || !ReferenceEquals(latest.WorldEntity, materialized))
        {
            capture = null;
            return null;
        }
        return materialized;
    }

    public bool RebucketLiveEntity(uint srvOid, uint spatialChamberOrLbIdent)
    {
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.WorldEntity is not { } actor)
            return false;
        if (capture.MaterializationResidence is
                OnlineActorMaterializationResidence.AwaitRuntimePlacement
            && HasEngagedStartingBuildResidence(capture.Canonical))
        {
            return false;
        }

        SimActorKey tag = DemandProjTag(capture);
        bool wasProjected = capture.IsSpatiallyProjected;
        bool wasShown = capture.IsSpatiallyVisible;
        bool wasPlainTrunk = _physics.IsSpatialTrunk(capture.Canonical);
        ulong projOp = ++capture.ProjAlterationVer;
        capture.IsSpatiallyProjected = true;
        bool hasPreciseDestChamber = spatialChamberOrLbIdent != 0u
            && (spatialChamberOrLbIdent & 0xFFFFu) != 0xFFFFu;
        if (hasPreciseDestChamber)
        {
            actor.ParentCellId = spatialChamberOrLbIdent;
        }
        Exception? spatialNotificationMiss = null;
        uint precedingRebucketingOid = _rebucketingOid;
        _rebucketingOid = srvOid;
        try
        {
            try
            {
                _spatial.RebucketLiveEntity(
                    tag,
                    actor,
                    spatialChamberOrLbIdent);
            }
            catch (AggregateException problem)
            {
                spatialNotificationMiss = problem;
            }
        }
        finally
        {
            _rebucketingOid = precedingRebucketingOid;
        }
        if (!IsLatestProjOp(srvOid, capture, projOp))
        {
            HurlFollowingSealedProjEdit(
                srvOid,
                spatialNotificationMiss,
                coreNotificationMiss: null);
            return false;
        }
        bool shown = _spatial.IsOnlineActorProjHoused(tag);
        capture.IsSpatiallyVisible = shown;
        RenewExhibit(capture);
        if (!_entityObjects.SealWireChamberRebucket(
                capture.Canonical,
                spatialChamberOrLbIdent))
        {
            HurlFollowingSealedProjEdit(
                srvOid,
                spatialNotificationMiss,
                coreNotificationMiss: null);
            return false;
        }
        bool isPlainTrunk = capture.ProjSort is OnlineActorMirrorKind.World
            && capture.IsSpatiallyProjected
            && capture.IsSpatiallyVisible
            && capture.WholeChamberIdent != 0;
        if (!wasProjected && !isPlainTrunk)
        {
            capture.SuspendObjectTimer();
            SynchronizeKineticsCorpusEngagedPhase(capture);
        }
        else if (wasPlainTrunk != isPlainTrunk)
        {
            if (isPlainTrunk)
            {
                if (!wasProjected)
                    capture.ObjectTimer.Deactivate();
                capture.RestartObjectTimerForJoinRealm(
                    (capture.FinalKineticsPhase & KineticStateFlags.Static) != 0);
                SynchronizeKineticsCorpusEngagedPhase(capture);
            }
            else
            {
                capture.SuspendObjectTimer();
                SynchronizeKineticsCorpusEngagedPhase(capture);
            }
        }
        RenewSpatialCoreIndexes(capture);
        Exception? coreNotificationMiss = null;
        if (!wasProjected || wasShown != shown)
        {
            try
            {
                BroadcastProjVisAltered(capture, shown);
            }
            catch (Exception problem)
            {
                coreNotificationMiss = problem;
            }
        }
        if (!IsLatestProjOp(srvOid, capture, projOp))
        {
            HurlFollowingSealedProjEdit(
                srvOid,
                spatialNotificationMiss,
                coreNotificationMiss);
            return false;
        }
        HurlFollowingSealedProjEdit(
            srvOid,
            spatialNotificationMiss,
            coreNotificationMiss);
        return true;
    }

    private bool RebucketOnlineActorExhibitSole(
        uint srvOid,
        OnlineActorRecord capture,
        RealmActor actor,
        uint spatialChamberOrLbIdent)
    {
        SimActorKey tag = DemandProjTag(capture);
        bool wasProjected = capture.IsSpatiallyProjected;
        bool wasShown = capture.IsSpatiallyVisible;
        ulong projOp = ++capture.ProjAlterationVer;
        capture.IsSpatiallyProjected = true;
        Exception? spatialNotificationMiss = null;
        uint precedingRebucketingOid = _rebucketingOid;
        _rebucketingOid = srvOid;
        CommenceExhibitSoleSpatialAlteration(tag);
        try
        {
            try
            {
                _spatial.RebucketLiveEntity(
                    tag,
                    actor,
                    spatialChamberOrLbIdent);
            }
            catch (AggregateException problem)
            {
                spatialNotificationMiss = problem;
            }
        }
        finally
        {
            FinishExhibitSoleSpatialAlteration(tag);
            _rebucketingOid = precedingRebucketingOid;
        }
        if (!IsLatestProjOp(srvOid, capture, projOp))
        {
            HurlFollowingSealedProjEdit(
                srvOid,
                spatialNotificationMiss,
                coreNotificationMiss: null);
            return false;
        }
        bool shown = _spatial.IsOnlineActorProjHoused(tag);
        capture.IsSpatiallyVisible = shown;
        RenewSpatialExhibitIndexes(capture);
        RenewExhibit(capture);
        RenewSpatialCoreIndexes(capture);
        Exception? coreNotificationMiss = null;
        if (!wasProjected || wasShown != shown)
        {
            try
            {
                BroadcastProjVisAltered(capture, shown);
            }
            catch (Exception problem)
            {
                coreNotificationMiss = problem;
            }
        }
        if (!IsLatestProjOp(srvOid, capture, projOp))
        {
            HurlFollowingSealedProjEdit(
                srvOid,
                spatialNotificationMiss,
                coreNotificationMiss);
            return false;
        }
        HurlFollowingSealedProjEdit(
            srvOid,
            spatialNotificationMiss,
            coreNotificationMiss);
        return true;
    }

    internal EquippedChildDisplayRebucketDisposition RebucketEquippedDescendantExhibit(
        uint srvOid,
        uint ancestorChamberIdent)
    {
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.WorldEntity is not { } actor)
        {
            return EquippedChildDisplayRebucketDisposition.NoProjection;
        }
        if (!_directory.AncestorAttachments.HasSealedAncestor(srvOid))
            return EquippedChildDisplayRebucketDisposition.NotAttached;
        return capture.MaterializationResidence is
                OnlineActorMaterializationResidence.AwaitRuntimePlacement
            && HasEngagedStartingBuildResidence(capture.Canonical)
            ? EquippedChildDisplayRebucketDisposition.NotAttached
            : RebucketOnlineActorExhibitSole(
                srvOid,
                capture,
                actor,
                ancestorChamberIdent)
            ? EquippedChildDisplayRebucketDisposition.Moved
            : EquippedChildDisplayRebucketDisposition.Displaced;
    }

    internal bool TryEnactStartingBuildWrapUpExhibit(
        in SimPlacementMirrorCapture proj)
    {
        SimPlacementMirrorTicket ticket = proj.Token;
        if (!ticket.IsValid
            || ticket.SessionLifetimeVersion != _directory.SessionLifetimeVersion
            || !_projections.TryGet(ticket.Entity, out OnlineActorRecord? capture)
            || !_directory.IsCurrent(capture.Canonical)
            || capture.Canonical.Key != ticket.Entity
            || capture.WorldEntity is not { } actor)
        {
            return true;
        }
        if (capture.WholeChamberIdent != ticket.ExactCellId
            || capture.Canonical.PlacementCommitVersion
                != ticket.PlacementCommitVersion
            || capture.Canonical.KineticBody is not { } corpus
            || corpus.Position != proj.WorldPosition
            || corpus.Orientation != proj.Orientation)
        {
            return true;
        }

        actor.SetPosition(proj.WorldPosition);
        actor.Rotation = proj.Orientation;
        actor.ParentCellId = ticket.ExactCellId;
        return RebucketOnlineActorExhibitSole(
            capture.ServerOid,
            capture,
            actor,
            ticket.ExactCellId);
    }

    internal bool HasEngagedStartingBuildResidence(SimActorKey tag) =>
        _directory.TryFetchByOwnTag(
            tag.LocalEntityId,
            out SimActorRecord canon)
        && _directory.IsCurrent(canon)
        && canon.Key == tag
        && _entityObjects.TryFetchStartingBuildResidence(canon, out _);

    internal bool HasEngagedStartingBuildResidence(
        SimActorRecord canon) =>
        _entityObjects.TryFetchStartingBuildResidence(canon, out _);

    internal void TranslateMaterializationResidenceToLegacyImmediate(
        OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.MaterializationResidence is not
            OnlineActorMaterializationResidence.AwaitRuntimePlacement)
        {
            return;
        }
        if (HasEngagedStartingBuildResidence(capture.Canonical))
        {
            throw new InvalidOperationException(
                $"Live entity 0x{capture.ServerOid:X8}/"
                + $"{capture.Canonical.Incarnation} can't convert to "
                + "LegacyImmediate residence while its initial-create "
                + "residence lease is still active");
        }
        capture.MaterializationResidence =
            OnlineActorMaterializationResidence.LegacyImmediate;
    }

    internal bool TryEnactCoreStanceProj(
        in SimPlacementMirrorCapture proj)
    {
        if (proj.Kind is SimPlacementMirrorKind.Discard)
        {
            return true;
        }

        if (proj.Kind is SimPlacementMirrorKind.ExecutorCompleted)
        {
            return true;
        }

        SimPlacementMirrorTicket ticket = proj.Token;
        if (!TryFetchCoreStanceProjCapture(
                ticket,
                demandStanceVersions:
                    proj.Kind is SimPlacementMirrorKind.Place,
                out OnlineActorRecord? capture)
            || capture.WorldEntity is not { } actor)
        {
            return false;
        }
        return proj.Kind is SimPlacementMirrorKind.Place
            && !_spatial.IsLoaded(
                (ticket.ExactCellId & 0xFFFF0000u) | 0xFFFFu)
            ? false
            : proj.Kind switch
            {
                SimPlacementMirrorKind.Place =>
                    TryEnactCoreStancePlace(in proj, capture, actor),
                SimPlacementMirrorKind.Withdraw =>
                    TryEnactCoreStanceWithdrawal(ticket, capture, actor),
                SimPlacementMirrorKind.WithdrawalRestored =>
                    TryEnactCoreStancePlace(
                        in proj,
                        capture,
                        actor,
                        sealPosture: false),
                _ => false,
            };
    }

    private bool TryEnactCoreStancePlace(
        in SimPlacementMirrorCapture proj,
        OnlineActorRecord capture,
        RealmActor actor,
        bool sealPosture = true)
    {
        SimPlacementMirrorTicket ticket = proj.Token;
        SimActorKey tag = ticket.Entity;
        ulong projOp = ++capture.ProjAlterationVer;

        if (sealPosture)
        {
            actor.SetPosition(proj.WorldPosition);
            actor.Rotation = proj.Orientation;
        }
        actor.ParentCellId = ticket.ExactCellId;
        capture.IsSpatiallyProjected = true;

        Exception? spatialNotificationMiss = null;
        uint precedingRebucketingOid = _rebucketingOid;
        _rebucketingOid = capture.ServerOid;
        CommenceExhibitSoleSpatialAlteration(tag);
        try
        {
            try
            {
                _spatial.RebucketLiveEntity(tag, actor, ticket.ExactCellId);
            }
            catch (AggregateException problem)
            {
                spatialNotificationMiss = problem;
            }
        }
        finally
        {
            FinishExhibitSoleSpatialAlteration(tag);
            _rebucketingOid = precedingRebucketingOid;
        }

        if (!IsLatestProjOp(
                capture.ServerOid,
                capture,
                projOp)
            || !TryFetchCoreStanceProjCapture(
                ticket,
                demandStanceVersions: true,
                out OnlineActorRecord? latest)
            || !ReferenceEquals(latest, capture)
            || !ReferenceEquals(latest.WorldEntity, actor))
        {
            HurlFollowingSealedProjEdit(
                capture.ServerOid,
                spatialNotificationMiss,
                coreNotificationMiss: null);
            return false;
        }

        bool shown = _spatial.IsOnlineActorProjHoused(tag);
        capture.IsSpatiallyVisible = shown;
        RenewSpatialExhibitIndexes(capture);
        RenewExhibit(capture);

        if (!IsLatestProjOp(
                capture.ServerOid,
                capture,
                projOp))
        {
            HurlFollowingSealedProjEdit(
                capture.ServerOid,
                spatialNotificationMiss,
                coreNotificationMiss: null);
            return false;
        }

        HurlFollowingSealedProjEdit(
            capture.ServerOid,
            spatialNotificationMiss,
            coreNotificationMiss: null);
        return true;
    }

    private bool TryEnactCoreStanceWithdrawal(
        in SimPlacementMirrorTicket ticket,
        OnlineActorRecord capture,
        RealmActor actor)
    {
        SimActorKey tag = ticket.Entity;
        ulong projOp = ++capture.ProjAlterationVer;
        capture.IsSpatiallyProjected = false;

        Exception? spatialNotificationMiss = null;
        uint precedingRebucketingOid = _rebucketingOid;
        _rebucketingOid = capture.ServerOid;
        CommenceExhibitSoleSpatialAlteration(tag);
        try
        {
            try
            {
                _spatial.RemoveLiveEntityProjection(actor);
            }
            catch (AggregateException problem)
            {
                spatialNotificationMiss = problem;
            }
        }
        finally
        {
            FinishExhibitSoleSpatialAlteration(tag);
            _rebucketingOid = precedingRebucketingOid;
        }

        if (!IsLatestProjOp(
                capture.ServerOid,
                capture,
                projOp)
            || !TryFetchCoreStanceProjCapture(
                ticket,
                demandStanceVersions: false,
                out OnlineActorRecord? latest)
            || !ReferenceEquals(latest, capture)
            || !ReferenceEquals(latest.WorldEntity, actor))
        {
            HurlFollowingSealedProjEdit(
                capture.ServerOid,
                spatialNotificationMiss,
                coreNotificationMiss: null);
            return false;
        }

        capture.IsSpatiallyVisible = false;
        RenewSpatialExhibitIndexes(capture);
        RenewExhibit(capture);

        if (!IsLatestProjOp(
                capture.ServerOid,
                capture,
                projOp))
        {
            HurlFollowingSealedProjEdit(
                capture.ServerOid,
                spatialNotificationMiss,
                coreNotificationMiss: null);
            return false;
        }

        HurlFollowingSealedProjEdit(
            capture.ServerOid,
            spatialNotificationMiss,
            coreNotificationMiss: null);
        return true;
    }

    private bool TryFetchCoreStanceProjCapture(
        in SimPlacementMirrorTicket ticket,
        bool demandStanceVersions,
        out OnlineActorRecord capture)
    {
        if (!ticket.IsValid
            || ticket.SessionLifetimeVersion != _directory.SessionLifetimeVersion
            || !_projections.TryGet(ticket.Entity, out capture!)
            || !_directory.IsCurrent(capture.Canonical)
            || capture.Canonical.Key != ticket.Entity
            || DemandProjTag(capture) != ticket.Entity
            || !IsValidGatewayStanceArbiter(ticket))
        {
            capture = null!;
            return false;
        }

        if (demandStanceVersions
            && (capture.Canonical.PositionAuthorityVersion
                    != ticket.PositionAuthorityVersion
                || capture.Canonical.SpatialAuthorityVersion
                    != ticket.SpatialAuthorityVersion
                || capture.Canonical.PlacementCommitVersion
                    != ticket.PlacementCommitVersion
                || capture.Canonical.WholeChamberTag != ticket.ExactCellId))
        {
            capture = null!;
            return false;
        }

        return true;
    }

    private static bool IsValidGatewayStanceArbiter(
        in SimPlacementMirrorTicket ticket)
    {
        SimPortalPlacementAuthority gateway = ticket.Portal;
        return !gateway.Present
            ? gateway.IsVacant
            : gateway.IsValid
            && gateway.Projection.DestinationCell == ticket.ExactCellId;
    }

    private void CommenceExhibitSoleSpatialAlteration(SimActorKey tag)
    {
        _exhibitSoleSpatialAlterationZDepth.TryGetValue(tag, out int zDepth);
        _exhibitSoleSpatialAlterationZDepth[tag] = checked(zDepth + 1);
    }

    private void FinishExhibitSoleSpatialAlteration(SimActorKey tag)
    {
        if (!_exhibitSoleSpatialAlterationZDepth.TryGetValue(
                tag,
                out int zDepth)
            || zDepth <= 0)
        {
            throw new InvalidOperationException(
                "Presentation-only spatial mutation depth wasn't balanced");
        }

        if (zDepth == 1)
            _exhibitSoleSpatialAlterationZDepth.Remove(tag);
        else
            _exhibitSoleSpatialAlterationZDepth[tag] = zDepth - 1;
    }

    public bool WithdrawLiveEntityProjection(uint srvOid)
    {
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.WorldEntity is null)
            return false;

        SimActorKey tag = DemandProjTag(capture);
        bool wasPlainTrunk = _physics.IsSpatialTrunk(capture.Canonical);
        ulong objectTimerEpoch = capture.ObjectTimerEpoch;
        ulong projOp = ++capture.ProjAlterationVer;
        Exception? spatialNotificationMiss = null;
        try
        {
            _spatial.RemoveLiveEntityProjection(capture.WorldEntity);
        }
        catch (AggregateException problem)
        {
            spatialNotificationMiss = problem;
        }
        if (!IsLatestProjOp(srvOid, capture, projOp))
        {
            HurlFollowingSealedProjEdit(
                srvOid,
                spatialNotificationMiss,
                coreNotificationMiss: null);
            return false;
        }
        bool spatialRimWasPostponed = capture.IsSpatiallyVisible;
        _projections.AssignShown(capture, false);
        capture.IsSpatiallyProjected = false;
        capture.IsSpatiallyVisible = false;
        if (wasPlainTrunk && capture.ObjectTimerEpoch == objectTimerEpoch)
            capture.SuspendObjectTimer();
        SynchronizeKineticsCorpusEngagedPhase(capture);
        RenewSpatialCoreIndexes(capture);
        Exception? coreNotificationMiss = null;
        if (spatialRimWasPostponed)
        {
            try
            {
                BroadcastProjVisAltered(capture, false);
            }
            catch (Exception problem)
            {
                coreNotificationMiss = problem;
            }
        }
        if (!IsLatestProjOp(srvOid, capture, projOp))
        {
            HurlFollowingSealedProjEdit(
                srvOid,
                spatialNotificationMiss,
                coreNotificationMiss);
            return false;
        }
        HurlFollowingSealedProjEdit(
            srvOid,
            spatialNotificationMiss,
            coreNotificationMiss);
        return true;
    }

    public bool WithdrawLiveEntityProjection(OnlineActorRecord anticipatedCapture)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        return WithdrawOnlineActorProj(
            anticipatedCapture,
            anticipatedCapture.PositionAuthorityVersion,
            anticipatedCapture.ProjAlterationVer);
    }

    internal bool WithdrawOnlineActorProj(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedLocusArbiterVer,
        ulong anticipatedProjAlterationVer)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        return IsLatestCapture(anticipatedCapture)
            && anticipatedCapture.PositionAuthorityVersion == anticipatedLocusArbiterVer
            && anticipatedCapture.ProjAlterationVer == anticipatedProjAlterationVer
            && WithdrawLiveEntityProjection(anticipatedCapture.ServerOid);
    }

    internal bool WithdrawOnlineActorProjToCellless(uint srvOid)
    {
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.WorldEntity is null)
        {
            return false;
        }

        if (!_entityObjects.SealWithdrawal(
                capture.Canonical,
                AcknowledgeCelllessCanonSeal))
        {
            return false;
        }
        capture.WorldEntity.ParentCellId = 0u;
        return WithdrawLiveEntityProjection(srvOid);
    }

    public bool WithdrawOnlineActor(
        ObjectDeletion.Parsed erase,
        bool isOwnAvatar,
        bool dropKeptObject = false,
        Action? priorTeardown = null)
    {
        if (_isRegisteringAssetList)
        {
            throw new InvalidOperationException(
                "A live entity can't unregister from inside atomic resource registration");
        }
        var teardownTag = (erase.Guid, erase.InstanceSequence);
        OnlineActorRecord? capture = null;
        SimActorRecord? canonSole = null;
        if (_directory.TryGetTeardown(
                teardownTag.Guid,
                teardownTag.InstanceSequence,
                out SimActorRecord keptCanon))
        {
            _projections.TryFetchTeardown(keptCanon, out capture);
        }
        bool retryingApprovedErase = capture is not null
            && (capture.EraseApprovedForTeardown
                || _isClearing
                || _sessWipeQueuedFinalization);
        SimActorDeleteAcceptance? approvedErase = null;
        if (!retryingApprovedErase)
        {
            if (!_entityObjects.TryAdmitErase(
                    erase,
                    isOwnAvatar,
                    dropKeptObject,
                    out approvedErase))
                return false;

            OnlineActorRecord? keptEnrollmentMiss = capture;
            if (approvedErase.RetiredCanon is { } engagedCanon)
            {
                if (_projections.TryGet(
                        engagedCanon,
                        out OnlineActorRecord? engagedCapture))
                {
                    if (!_projections.RemoveActive(engagedCapture))
                    {
                        throw new InvalidOperationException(
                            $"Exact App projection for 0x{erase.Guid:X8}/{erase.InstanceSequence} could not be retired");
                    }
                    capture = engagedCapture;
                    KeepTeardownCapture(capture);
                }
                else
                {
                    canonSole = engagedCanon;
                }
            }
            else
            {
                capture = keptEnrollmentMiss;
            }
            capture?.EraseApprovedForTeardown = true;
        }

        List<Exception>? misses = null;
        _logicalTeardownZDepth++;
        try
        {
            if (!retryingApprovedErase)
            {
                try
                {
                    _entityObjects.ConcludeApprovedErase(approvedErase!);
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }

                try
                {
                    priorTeardown?.Invoke();
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }

            if (capture is not null)
            {
                try
                {
                    TearDownCapture(capture);
                    FreeTeardownCapture(capture);
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }
            else if (canonSole is not null)
            {
                TearDownCanonSole(canonSole);
            }
        }
        finally
        {
            _logicalTeardownZDepth--;
        }

        try
        {
            if (!_directory.TryFetchEngaged(erase.Guid, out _)
                && !HasQueuedTeardown(erase.Guid))
            {
                _spatial.DropOnlineActor(erase.Guid);
            }
        }
        catch (Exception problem)
        {
            (misses ??= []).Add(problem);
        }

        return misses is not null
            ? throw new AggregateException(
                $"Live entity 0x{erase.Guid:X8} deletion cleanup failed",
                misses)
            : true;
    }

    public bool TryFetchRecord(uint srvOid, out OnlineActorRecord capture) =>
        _projections.TryFetchLatest(srvOid, out capture!);

    public bool TryFetchRealmActor(uint srvOid, out RealmActor actor)
    {
        if (_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            && capture.WorldEntity is { } located)
        {
            actor = located;
            return true;
        }

        actor = null!;
        return false;
    }

    public bool TryFetchSpatiallyProjectedCapture(
        uint srvOid,
        out OnlineActorRecord capture)
    {
        if (_projections.TryFetchLatest(srvOid, out OnlineActorRecord located)
            && located.WorldEntity is not null
            && located.ProjSort is OnlineActorMirrorKind.World
            && located.IsSpatiallyProjected)
        {
            capture = located;
            return true;
        }

        capture = null!;
        return false;
    }

    public bool TryFetchAffixedProjectedCapture(
        uint srvOid,
        out OnlineActorRecord capture)
    {
        if (_projections.TryFetchLatest(srvOid, out OnlineActorRecord located)
            && located.WorldEntity is not null
            && located.ProjSort is OnlineActorMirrorKind.Attached
            && located.IsSpatiallyProjected)
        {
            capture = located;
            return true;
        }

        capture = null!;
        return false;
    }

    public bool TryGetPickEligibleRecord(
        uint srvOid,
        out OnlineActorRecord capture)
        => TryGetInteractionEligibleRecord(srvOid, out capture)
            || TryFetchAffixedProjectedCapture(srvOid, out capture);

    public bool TryGetPickEligibleRecord(
        uint srvOid,
        uint ownActorIdent,
        out OnlineActorRecord capture)
    {
        if (srvOid != 0u
            && ownActorIdent != 0u
            && TryGetPickEligibleRecord(srvOid, out OnlineActorRecord located)
            && located.WorldEntity!.Id == ownActorIdent)
        {
            capture = located;
            return true;
        }

        capture = null!;
        return false;
    }

    public bool TryFetchDealingEligibleActor(
        uint srvOid,
        out RealmActor actor)
    {
        if (_projections.TryFetchShownLatest(
                srvOid,
                out OnlineActorRecord capture)
            && capture.WorldEntity is { } shown)
        {
            actor = shown;
            return true;
        }

        actor = null!;
        return false;
    }

    bool IOnlineActorRadarSource.TryFetchMaterialized(
        uint srvOid,
        out RealmActor actor) =>
        TryFetchRealmActor(srvOid, out actor);

    bool IOnlineActorRadarSource.TryFetchShown(
        uint srvOid,
        out RealmActor actor) =>
        TryFetchDealingEligibleActor(srvOid, out actor);

    void IOnlineActorRadarSource.DuplicateShownTo(
        List<KeyValuePair<uint, RealmActor>> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach (OnlineActorRecord capture in _projections.VisibleRecords)
        {
            dest.Add(new KeyValuePair<uint, RealmActor>(
                capture.ServerOid,
                capture.WorldEntity!));
        }
    }

    public bool TryGetInteractionEligibleRecord(
        uint srvOid,
        out OnlineActorRecord capture)
    {
        if (_projections.TryFetchShownLatest(
                srvOid,
                out OnlineActorRecord located)
            && located.WorldEntity is not null)
        {
            capture = located;
            return true;
        }

        capture = null!;
        return false;
    }

    public bool TryGetInteractionEligibleRecord(
        uint srvOid,
        uint ownActorIdent,
        out OnlineActorRecord capture)
    {
        if (srvOid != 0u
            && ownActorIdent != 0u
            && TryGetInteractionEligibleRecord(srvOid, out OnlineActorRecord located)
            && located.WorldEntity!.Id == ownActorIdent)
        {
            capture = located;
            return true;
        }

        capture = null!;
        return false;
    }

    public bool ContainsRealmActor(uint srvOid) =>
        _projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
        && capture.WorldEntity is not null;

    public bool TryFetchSrvOid(uint ownActorIdent, out uint srvOid)
    {
        if (_directory.TryFetchByOwnTag(
                ownActorIdent,
                out SimActorRecord canon))
        {
            srvOid = canon.ServerGuid;
            return true;
        }

        srvOid = 0u;
        return false;
    }

    internal bool TryFetchCaptureByOwnActorIdent(
        uint ownActorIdent,
        out OnlineActorRecord capture) =>
        _projections.TryFetchByOwnIdent(ownActorIdent, out capture);

    internal bool TryFetchCapture(
        SimActorKey tag,
        out OnlineActorRecord capture) =>
        _projections.TryGet(tag, out capture);

    public bool TryFetchOwnActorIdent(uint srvOid, out uint ownActorIdent)
    {
        if (_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            && capture.OwnActorIdent is { } located)
        {
            ownActorIdent = located;
            return true;
        }

        ownActorIdent = 0;
        return false;
    }

    public void AssignAnimCore(uint srvOid, IOnlineActorMotionEngine core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.WorldEntity is not { } actor)
            throw new InvalidOperationException($"Can't bind animation prior to live entity 0x{srvOid:X8} is materialized");
        if (!ReferenceEquals(core.Entity, actor))
            throw new InvalidOperationException("Animation runtime belongs to a different WorldEntity");

        _ = DemandProjTag(capture);
        capture.AnimationRuntime = core;
        RenewSpatialCoreIndexes(capture);
    }

    public bool TryFetchAnimCore(
        uint ownActorIdent,
        out IOnlineActorMotionEngine core)
    {
        if (_projections.TryFetchByOwnIdent(
                ownActorIdent,
                out OnlineActorRecord capture)
            && capture.AnimationRuntime is { } located)
        {
            core = located;
            return true;
        }

        core = null!;
        return false;
    }

    internal bool TryFetchProjTag(
        uint srvOid,
        out SimActorKey tag)
    {
        if (_projections.TryFetchLatest(
                srvOid,
                out OnlineActorRecord? capture)
            && capture.ProjTag is { } located)
        {
            tag = located;
            return true;
        }

        tag = default;
        return false;
    }

    public bool PurgeAnimCore(uint srvOid)
    {
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.AnimationRuntime is null)
            return false;
        capture.AnimationRuntime = null;
        RenewSpatialCoreIndexes(capture);
        return true;
    }

    internal bool WipeAnimCore(OnlineActorRecord anticipatedCapture)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (anticipatedCapture.AnimationRuntime is null
            || anticipatedCapture.ProjTag is not { } tag
            || !_projections.TryGet(tag, out OnlineActorRecord? capture)
            || !ReferenceEquals(capture, anticipatedCapture))
        {
            return false;
        }

        capture.AnimationRuntime = null;
        RenewSpatialCoreIndexes(capture);
        return true;
    }

    internal KineticBody GetOrCreatePhysicsBody(
        uint srvOid,
        Func<OnlineActorRecord, KineticBody> maker)
    {
        ArgumentNullException.ThrowIfNull(maker);
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.WorldEntity is null)
        {
            throw new InvalidOperationException(
                $"Can't acquire physics body prior to live entity 0x{srvOid:X8} is materialized");
        }
        bool ProjIsLatest() =>
            _projections.TryFetchLatest(
                srvOid,
                out OnlineActorRecord? latest)
            && ReferenceEquals(latest, capture)
            && latest.WorldEntity is not null;
        return _physics.GetOrCreatePhysicsBody(
            capture.Canonical,
            _ => maker(capture),
            ProjIsLatest);
    }

    internal void AssignDistantLocomotionCore(uint srvOid, ISimPeerMotion core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture))
            throw new InvalidOperationException($"Can't bind remote motion prior to live entity 0x{srvOid:X8} exists");
        ulong lifespanVer = LatestLifespanAlteration(srvOid);
        _physics.ApplyDistantLocomotion(
            capture.Canonical,
            core,
            () => LatestLifespanAlteration(srvOid) == lifespanVer
                && _projections.TryFetchLatest(
                    srvOid,
                    out OnlineActorRecord? latest)
                && ReferenceEquals(latest, capture));
        SynchronizeKineticsCorpusEngagedPhase(capture);
        RenewSpatialCoreIndexes(capture);
    }

    public void SetupKineticsHub(
        OnlineActorRecord anticipatedCapture,
        MacAC.Mechanics.Kinetics.Gait.IKineticObjHost hub)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        ArgumentNullException.ThrowIfNull(hub);
        uint srvOid = anticipatedCapture.ServerOid;
        bool ProjIsLatest() =>
            _projections.TryFetchLatest(
                srvOid,
                out OnlineActorRecord? latest)
            && ReferenceEquals(latest, anticipatedCapture);
        _physics.PlaceKineticsHub(
            anticipatedCapture.Canonical,
            hub,
            ProjIsLatest);
    }

    public bool TryGetPhysicsHost(
        uint srvOid,
        out MacAC.Mechanics.Kinetics.Gait.IKineticObjHost hub)
    {
        if (_projections.TryFetchLatest(srvOid, out _)
            && _physics.TryGetPhysicsHost(srvOid, out var extant))
        {
            hub = extant;
            return true;
        }

        hub = null!;
        return false;
    }

    internal void ForceFinishImpactReporting(SimActorRecord canon) =>
        _physics.ImpactDossiers.ExitRealm(canon);

    public bool TryFetchDistantLocomotionCore(
        uint srvOid,
        out ISimPeerMotion core)
    {
        if (_projections.TryFetchLatest(
                srvOid,
                out OnlineActorRecord? capture)
            && capture.RemoteMotionRuntime is { } located)
        {
            core = located;
            return true;
        }

        core = null!;
        return false;
    }

    internal bool WipeDistantLocomotionCore(uint srvOid)
    {
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.RemoteMotionRuntime is null)
            return false;
        if (!_physics.WipeDistantLocomotion(capture.Canonical))
            return false;
        RenewSpatialCoreIndexes(capture);
        return true;
    }

    internal PeerMotion FetchOrBuildDistantLocomotionCore(uint srvOid)
    {
        if (!_projections.TryFetchLatest(
                srvOid,
                out OnlineActorRecord? capture))
        {
            throw new InvalidOperationException(
                $"Can't acquire remote motion prior to live entity 0x{srvOid:X8} exists");
        }

        bool ProjIsLatest() =>
            _projections.TryFetchLatest(
                srvOid,
                out OnlineActorRecord? latest)
            && ReferenceEquals(latest, capture);
        return _physics.GetOrCreateRemoteMotion(
            capture.Canonical,
            ProjIsLatest);
    }

    internal ISimMissile AttachMissileCore(
        uint srvOid,
        KineticBody corpus,
        MissileContactSphere impactOrb,
        Func<bool>? externalHolderValid = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.WorldEntity is null)
        {
            throw new InvalidOperationException(
                $"Can't bind projectile physics prior to live entity 0x{srvOid:X8} is materialized");
        }
        if (capture.KineticsCorpusAcquisitionInHeadway && capture.KineticBody is null)
            throw new InvalidOperationException(
                $"Live entity 0x{srvOid:X8} can't bind projectile physics during physics-body acquisition");
        if (capture.KineticBody is { } canonCorpus
            && !ReferenceEquals(canonCorpus, corpus))
        {
            throw new InvalidOperationException(
                $"Live entity 0x{srvOid:X8} can't replace its canonical physics body within one incarnation");
        }

        ISimMissile core = _physics.AttachMissile(
            capture.Canonical,
            corpus,
            impactOrb,
            () => IsLatestCapture(capture)
                && capture.WorldEntity is { } actor
                && capture.OwnActorIdent == actor.Id
                && (externalHolderValid?.Invoke() ?? true));
        RenewSpatialCoreIndexes(capture);
        return core;
    }

    public bool TryFetchMissileCore(
        uint srvOid,
        out ISimMissile core)
    {
        if (_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            && capture.ProjectileRuntime is { } located)
        {
            core = located;
            return true;
        }

        core = null!;
        return false;
    }

    public bool WipeMissileCore(uint srvOid)
    {
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.ProjectileRuntime is null)
            return false;

        _physics.WipeMissile(capture.Canonical);
        RenewSpatialCoreIndexes(capture);
        return true;
    }

    public void AssignFxProfile(uint srvOid, IOnlineActorEffectProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture))
            throw new InvalidOperationException(
                $"Can't bind an effect profile prior to live entity 0x{srvOid:X8} exists");
        capture.EffectProfile = profile;
    }

    public bool TryFetchFxProfile(
        uint srvOid,
        out IOnlineActorEffectProfile profile)
    {
        if (_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            && capture.EffectProfile is { } located)
        {
            profile = located;
            return true;
        }

        profile = null!;
        return false;
    }

    public bool TryGetCapture(uint oid, out RealmSession.MoverSpawn summon) =>
        _directory.TryGetSnapshot(oid, out summon);

    public bool TryEnactObjRefDsc(ObjDescNotice.Parsed refresh, out RealmSession.MoverSpawn approved)
        => _entityObjects.TryApplyObjDsc(
            refresh,
            canon =>
            {
                if (_projections.TryGet(
                        canon,
                        out OnlineActorRecord? capture))
                {
                    capture.NoteObjRefDscProjSynchronization();
                }
            },
            out approved);

    public bool TryEnactLift(PickupNotice.Parsed refresh, out RealmSession.MoverSpawn approved)
        => _entityObjects.TryApplyPickup(
            refresh,
            AcknowledgeCelllessCanonSeal,
            out approved);

    public bool TryEnactBuildAncestor(CreateAnchorUpdate refresh, out RealmSession.MoverSpawn approved)
        => _entityObjects.TryApplyCreateAncestor(
            refresh,
            acknowledgeProj: null,
            out approved);

    public bool TryEnactAncestor(AncestorSignal.Parsed refresh, out RealmSession.MoverSpawn approved)
        => _entityObjects.TryApplyParent(
            refresh,
            acknowledgeProj: null,
            out approved);

    internal bool SealLinedAncestor(
        AnchorAttachmentRelation relation,
        out RealmSession.MoverSpawn approved)
        => _entityObjects.TryCommitAncestor(
            relation,
            acknowledgeProj: null,
            out approved);

    internal bool SealApprovedAncestorCellless(
        OnlineActorRecord capture,
        ulong locusArbiterVer)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return _entityObjects.SealApprovedAncestorCellless(
            capture.Canonical,
            locusArbiterVer,
            AcknowledgeCelllessCanonSeal);
    }

    internal bool SealApprovedAncestorCellless(
        SimActorRecord canon,
        ulong locusArbiterVer)
    {
        return _entityObjects.SealApprovedAncestorCellless(
            canon,
            locusArbiterVer,
            AcknowledgeCelllessCanonSeal);
    }

    public bool TryEnactLocomotion(
        RealmSession.MoverMotionUpdate refresh,
        bool retainCargo,
        out RealmSession.MoverSpawn approved,
        out GrantedKineticsTimestamps timestamps)
        => _entityObjects.TryApplyMotion(
            refresh,
            retainCargo,
            acknowledgeProj: null,
            out approved,
            out timestamps);

    public bool TryEnactVector(VelocityUpdate.Parsed refresh, out RealmSession.MoverSpawn approved)
        => _entityObjects.TryApplyVector(
            refresh,
            acknowledgeProj: null,
            out approved);

    public bool TryApplyState(GroupPhase.Parsed refresh, out RealmSession.MoverSpawn approved)
        => TryApplyState(refresh, out approved, out _);

    public bool TryApplyState(
        GroupPhase.Parsed refresh,
        out RealmSession.MoverSpawn approved,
        out CanonKineticShift changeover)
        => _entityObjects.TryEnactState(
            refresh,
            (canon, _) =>
            {
                if (_projections.TryGet(
                        canon,
                        out OnlineActorRecord? capture))
                {
                    RenewExhibit(capture);
                }
            },
            out approved,
            out changeover);

    public bool AssignAffixedDescendantNoPaint(uint descendantSrvOid, bool noPaint)
    {
        return !_projections.TryFetchLatest(descendantSrvOid, out OnlineActorRecord? capture)
            ? false
            : _entityObjects.SealDescendantNoPaint(
            capture.Canonical,
            noPaint,
            _ => RenewExhibit(capture));
    }

    public bool TryEnactLocus(
        RealmSession.MoverPositionUpdate refresh,
        bool isOwnAvatar,
        System.Numerics.Quaternion? forceLocusSpin,
        System.Numerics.Vector3? latestOwnVel,
        out PoseStampVerdict disposition,
        out RealmSession.MoverSpawn approved,
        out GrantedKineticsTimestamps timestamps) =>
        _entityObjects.TryApplyLocus(
            refresh,
            isOwnAvatar,
            forceLocusSpin,
            latestOwnVel,
            acknowledgeProj: null,
            out disposition,
            out approved,
            out timestamps);

    internal SimSovereignPositionRoute? ClassifyDistantApprovedLocus(
        SimActorRecord canon,
        in MacAC.Wire.RealmSession.MoverPositionUpdate refresh,
        MacAC.Mechanics.Kinetics.PoseStampVerdict disposition,
        in GrantedKineticsTimestamps timestamps,
        float? avatarGap) =>
        _entityObjects.ClassifyDistantApprovedLocus(
            canon,
            refresh,
            disposition,
            timestamps,
            avatarGap);

    public bool IsFreshWarpBegin(uint ownAvatarOid, ushort warpSeries) =>
        _directory.IsFreshTeleportStart(ownAvatarOid, warpSeries);

    public CanonClockVerdict FetchTrunkObjectTimerDisposition(
        uint srvOid)
    {
        return !_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.ProjSort is not OnlineActorMirrorKind.World
            || !capture.IsSpatiallyProjected
            || !capture.IsSpatiallyVisible
            || capture.WholeChamberIdent == 0
            ? CanonClockVerdict.Suspend
            : (capture.FinalKineticsPhase & KineticStateFlags.Frozen) != 0
            ? CanonClockVerdict.Suspend
            : CanonClockVerdict.Advance;
    }

    public bool ShouldProceedTrunkCore(uint srvOid) =>
        FetchTrunkObjectTimerDisposition(srvOid)
            is CanonClockVerdict.Advance;

    internal bool IsLatestLocusArbiter(
        OnlineActorRecord capture,
        ulong arbiterVer) =>
        _projections.TryFetchLatest(capture.ServerOid, out OnlineActorRecord? latest)
        && ReferenceEquals(latest, capture)
        && latest.PositionAuthorityVersion == arbiterVer;

    internal bool IsLatestLocusArbiter(
        SimActorRecord canon,
        ulong arbiterVer) =>
        _directory.IsCurrent(canon)
        && canon.PositionAuthorityVersion == arbiterVer;

    internal bool IsLatestPhaseArbiter(
        OnlineActorRecord capture,
        ulong arbiterVer) =>
        _projections.TryFetchLatest(capture.ServerOid, out OnlineActorRecord? latest)
        && ReferenceEquals(latest, capture)
        && latest.PhaseArbiterVer == arbiterVer;

    internal bool IsLatestVectorArbiter(
        OnlineActorRecord capture,
        ulong arbiterVer) =>
        _projections.TryFetchLatest(capture.ServerOid, out OnlineActorRecord? latest)
        && ReferenceEquals(latest, capture)
        && latest.VectorArbiterVer == arbiterVer;

    internal bool IsLatestVelArbiter(
        OnlineActorRecord capture,
        ulong arbiterVer) =>
        _projections.TryFetchLatest(capture.ServerOid, out OnlineActorRecord? latest)
        && ReferenceEquals(latest, capture)
        && latest.VelArbiterVer == arbiterVer;

    internal bool IsLatestVelArbiter(
        SimActorRecord canon,
        ulong arbiterVer) =>
        _directory.IsCurrent(canon)
        && canon.VelArbiterVer == arbiterVer;

    internal bool IsLatestTravelArbiter(
        OnlineActorRecord capture,
        ulong arbiterVer) =>
        _projections.TryFetchLatest(capture.ServerOid, out OnlineActorRecord? latest)
        && ReferenceEquals(latest, capture)
        && latest.TravelArbiterVer == arbiterVer;

    internal bool IsLatestObjRefDscArbiter(
        OnlineActorRecord capture,
        ulong arbiterVer) =>
        _projections.TryFetchLatest(capture.ServerOid, out OnlineActorRecord? latest)
        && ReferenceEquals(latest, capture)
        && latest.ObjRefDscArbiterVer == arbiterVer;

    internal void DuplicateSpatialTrunkObjectRecordsTo(List<OnlineActorRecord> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        _physics.DuplicateSpatialTrunksTo(_spatialTrunkCanonTemp);
        for (int ordinal = 0;
             ordinal < _spatialTrunkCanonTemp.Count;
             ordinal++)
        {
            SimActorRecord canon =
                _spatialTrunkCanonTemp[ordinal];
            if (_projections.TryGet(
                    canon,
                    out OnlineActorRecord? latest)
                && HasSpatialCoreProj(latest))
            {
                dest.Add(latest);
            }
        }
    }

    internal bool IsLatestSpatialTrunkObject(OnlineActorRecord capture) =>
        IsLatestCapture(capture)
        && _physics.IsSpatialTrunk(capture.Canonical)
        && HasSpatialCoreProj(capture);

    internal bool IsLatestSpatialAnim(
        OnlineActorRecord capture,
        IOnlineActorMotionEngine anim) =>
        IsLatestSpatialTrunkObject(capture)
        && ReferenceEquals(capture.AnimationRuntime, anim)
        && capture.ProjTag is { } tag
        && _spatialAnims.TryGetValue(tag, out var indexed)
        && ReferenceEquals(indexed, anim);

    internal bool IsLatestSpatialAnim(
        uint ownActorIdent,
        IOnlineActorMotionEngine anim) =>
        _projections.TryFetchByOwnIdent(ownActorIdent, out OnlineActorRecord capture)
        && IsLatestSpatialAnim(capture, anim);

    internal bool IsLatestAnimHolder(
        OnlineActorRecord capture,
        IOnlineActorMotionEngine anim) =>
        IsLatestCapture(capture)
        && ReferenceEquals(capture.AnimationRuntime, anim);

    internal void DuplicateSpatialAnimRuntimesTo<TAnimation>(
        List<KeyValuePair<uint, TAnimation>> dest)
        where TAnimation : class, IOnlineActorMotionEngine
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach ((SimActorKey tag, IOnlineActorMotionEngine anim)
                 in _spatialAnims)
        {
            if (anim is TAnimation typed)
            {
                dest.Add(
                    new KeyValuePair<uint, TAnimation>(
                        tag.LocalEntityId,
                        typed));
            }
        }
    }

    internal void DuplicateSpatialAnimOwnIdentsTo(HashSet<uint> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach (SimActorKey tag in _spatialAnims.Keys)
            dest.Add(tag.LocalEntityId);
    }

    internal bool TrySealAuthoritativeVel(
        OnlineActorRecord capture,
        KineticBody corpus,
        Vector3 vel,
        double latestMoment) =>
        TrySealAuthoritativeVectorCore(
            capture,
            corpus,
            vel,
            angularVel: null,
            latestMoment);

    internal bool TryCommitAuthoritativeVector(
        OnlineActorRecord capture,
        KineticBody corpus,
        Vector3 vel,
        Vector3 angularVel,
        double latestMoment) =>
        TrySealAuthoritativeVectorCore(
            capture,
            corpus,
            vel,
            angularVel,
            latestMoment);

    private bool TrySealAuthoritativeVectorCore(
        OnlineActorRecord capture,
        KineticBody corpus,
        Vector3 vel,
        Vector3? angularVel,
        double latestMoment)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        bool ProjIsLatest() =>
            _projections.TryFetchLatest(
                capture.ServerOid,
                out OnlineActorRecord? latest)
            && ReferenceEquals(latest, capture)
            && ReferenceEquals(latest.KineticBody, corpus);
        return _physics.TryCommitAuthoritativeVector(
            capture.Canonical,
            corpus,
            vel,
            angularVel,
            latestMoment,
            ProjIsLatest);
    }

    internal void DuplicateSpatialDistantLocomotionRecordsTo(List<OnlineActorRecord> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        _physics.DuplicateSpatialRemotesTo(_spatialDistantCanonTemp);
        for (int ordinal = 0;
             ordinal < _spatialDistantCanonTemp.Count;
             ordinal++)
        {
            SimActorRecord canon =
                _spatialDistantCanonTemp[ordinal];
            if (_projections.TryGet(
                    canon,
                    out OnlineActorRecord? capture)
                && HasSpatialCoreProj(capture))
            {
                dest.Add(capture);
            }
        }
    }

    internal bool IsLatestSpatialDistantLocomotion(
        OnlineActorRecord capture,
        ISimPeerMotion core) =>
        IsLatestCapture(capture)
        && _physics.IsSpatialDistant(capture.Canonical, core)
        && HasSpatialCoreProj(capture);

    internal void DuplicateSpatialMissileRecordsTo(List<OnlineActorRecord> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();

        _physics.DuplicateSpatialMissilesTo(_spatialTrunkCanonTemp);
        for (int idx = 0; idx < _spatialTrunkCanonTemp.Count; idx++)
        {
            SimActorRecord canon = _spatialTrunkCanonTemp[idx];
            if (_projections.TryGet(canon, out OnlineActorRecord? capture)
                && HasSpatialCoreProj(capture))
            {
                dest.Add(capture);
            }
        }
    }

    internal bool IsLatestSpatialMissile(
        OnlineActorRecord capture,
        ISimMissile core) =>
        IsLatestCapture(capture)
        && _physics.IsSpatialMissile(capture.Canonical, core)
        && HasSpatialCoreProj(capture);

    public bool IsHidden(uint srvOid) =>
        _projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
        && (capture.FinalKineticsPhase & KineticStateFlags.Hidden) != 0;

    public bool TryFlagRealmSummonPublished(uint srvOid)
    {
        if (!_projections.TryFetchLatest(srvOid, out OnlineActorRecord? capture)
            || capture.WorldEntity is null
            || capture.ProjSort is not OnlineActorMirrorKind.World
            || capture.RealmSummonPublished)
            return false;
        capture.RealmSummonPublished = true;
        return true;
    }

    internal bool TryFlagStartingHydrationFinished(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedBuildIntegrationVer)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (!IsLatestCapture(anticipatedCapture)
            || anticipatedCapture.BuildIntegrationVer != anticipatedBuildIntegrationVer
            || anticipatedCapture.WorldEntity is null
            || !anticipatedCapture.ResourcesRegistered)
        {
            return false;
        }

        anticipatedCapture.StartingHydrationFinished = true;
        return true;
    }

    internal bool TryFlagBuildProjSynchronizationQueued(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedBuildIntegrationVer)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (!IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer))
        {
            return false;
        }

        anticipatedCapture.BuildProjSynchronizationQueued = true;
        return true;
    }

    internal bool TryDoneBuildProjSynchronization(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedBuildIntegrationVer)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (!IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer))
        {
            return false;
        }

        anticipatedCapture.BuildProjSynchronizationQueued = false;
        return true;
    }

    internal bool TryCommenceProjHydration(
        OnlineActorRecord anticipatedCapture,
        ulong? anticipatedBuildIntegrationVer)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (!IsLatestCapture(anticipatedCapture)
            || (anticipatedBuildIntegrationVer is { } anticipated
                && anticipatedCapture.BuildIntegrationVer != anticipated))
        {
            return false;
        }

        if (anticipatedCapture.ProjHydrationInHeadway)
        {
            if (anticipatedCapture.BuildIntegrationVer
                != anticipatedCapture.ProjHydrationBuildVer)
            {
                anticipatedCapture.ProjHydrationReattemptAsked = true;
            }
            return false;
        }

        anticipatedCapture.ProjHydrationInHeadway = true;
        anticipatedCapture.ProjHydrationBuildVer =
            anticipatedCapture.BuildIntegrationVer;
        anticipatedCapture.ProjHydrationReattemptAsked = false;
        return true;
    }

    internal bool FinishProjHydration(OnlineActorRecord anticipatedCapture)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        bool reattempt = IsLatestCapture(anticipatedCapture)
            && (anticipatedCapture.ProjHydrationReattemptAsked
                || anticipatedCapture.BuildIntegrationVer
                    != anticipatedCapture.ProjHydrationBuildVer);
        if (reattempt)
        {
            anticipatedCapture.BuildProjSynchronizationQueued = true;
        }
        anticipatedCapture.ProjHydrationInHeadway = false;
        anticipatedCapture.ProjHydrationBuildVer = 0UL;
        anticipatedCapture.ProjHydrationReattemptAsked = false;
        return reattempt;
    }

    internal bool TryCommenceLooksHydration(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedObjRefDscArbiterVer)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (!anticipatedCapture.LooksProjSynchronizationQueued
            || !IsLatestObjRefDscArbiter(
                anticipatedCapture,
                anticipatedObjRefDscArbiterVer))
        {
            return false;
        }

        anticipatedCapture.LooksProjSynchronizationQueued = true;
        if (anticipatedCapture.LooksHydrationInHeadway)
        {
            anticipatedCapture.LooksHydrationReattemptAsked = true;
            return false;
        }

        anticipatedCapture.LooksHydrationInHeadway = true;
        anticipatedCapture.LooksHydrationVer =
            anticipatedObjRefDscArbiterVer;
        anticipatedCapture.LooksHydrationReattemptAsked = false;
        return true;
    }

    internal bool FinishLooksHydration(OnlineActorRecord anticipatedCapture)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        bool reattempt = IsLatestCapture(anticipatedCapture)
            && (anticipatedCapture.LooksHydrationReattemptAsked
                || anticipatedCapture.ObjRefDscArbiterVer
                    != anticipatedCapture.LooksHydrationVer);
        anticipatedCapture.LooksHydrationInHeadway = false;
        anticipatedCapture.LooksHydrationVer = 0UL;
        anticipatedCapture.LooksHydrationReattemptAsked = false;
        return reattempt;
    }

    internal bool TryDoneLooksProjSynchronization(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedObjRefDscArbiterVer)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (!anticipatedCapture.LooksProjSynchronizationQueued
            || !IsLatestObjRefDscArbiter(
                anticipatedCapture,
                anticipatedObjRefDscArbiterVer))
        {
            return false;
        }

        anticipatedCapture.LooksProjSynchronizationQueued = false;
        return true;
    }

    internal bool IsLatestBuildIntegration(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedBuildIntegrationVer) =>
        IsLatestCapture(anticipatedCapture)
        && anticipatedCapture.BuildIntegrationVer == anticipatedBuildIntegrationVer;

    internal void RetireGenProj(
        SimActorRecord canon)
    {
        ArgumentNullException.ThrowIfNull(canon);
        if (_isClearing || _isRegisteringAssetList)
        {
            throw new InvalidOperationException(
                _isClearing
                    ? "Live entity projection teardown is by now in progress"
                    : "Live entity projection teardown can't begin inside atomic resource registration");
        }

        OnlineActorRecord? capture = null;
        if (_projections.TryGet(canon, out OnlineActorRecord engaged))
        {
            if (!_projections.RemoveActive(engaged))
            {
                throw new InvalidOperationException(
                    $"Exact App projection for 0x{canon.ServerGuid:X8}/{canon.Incarnation} could not be retired during generation reset");
            }
            _projections.KeepTeardown(engaged);
            capture = engaged;
        }
        else if (_projections.TryFetchTeardown(
                     canon,
                     out OnlineActorRecord kept))
        {
            capture = kept;
        }

        if (capture is null)
            return;

        _isClearing = true;
        try
        {
            TearDownCapture(capture, doneCanon: false);
            _projections.FreeTeardown(capture);
        }
        finally
        {
            _isClearing = false;
        }
    }

    internal void ConcludeGenProjSunset()
    {
        if (_projections.EngagedTally != 0
            || _projections.TeardownTally != 0)
        {
            throw new InvalidOperationException(
                "Live entity projection storage hasn't converged at the generation-reset boundary");
        }

        _spatialAnims.Clear();
        _physics.WipeSpatialWorksets();
        _projections.WipeConverged();
        _spatial.WipeOnlineActorLifespanPhase();
        _sessWipeQueuedFinalization = false;
    }

    public void Clear()
    {
        if (_isClearing || _isRegisteringAssetList)
        {
            throw new InvalidOperationException(
                _isClearing
                    ? "Live entity session teardown is by now in progress"
                    : "Live entity session teardown can't begin inside atomic resource registration");
        }

        _isClearing = true;
        _sessWipeQueuedFinalization = true;
        IReadOnlyList<SimActorRecord> retiredCanonicals =
            _entityObjects.OpenSessWipe();
        List<Exception>? misses = null;
        try
        {
            foreach (SimActorRecord canon in retiredCanonicals)
            {
                if (_projections.TryGet(
                        canon,
                        out OnlineActorRecord? capture))
                {
                    if (!_projections.RemoveActive(capture))
                    {
                        throw new InvalidOperationException(
                            $"Exact App projection for 0x{capture.ServerOid:X8}/{capture.Generation} could not be retired during session clear");
                    }
                    KeepTeardownCapture(capture);
                }
                else
                {
                    TearDownCanonSole(canon);
                }
            }

            foreach (OnlineActorRecord capture in _projections.TeardownRecords.ToArray())
            {
                if (!_projections.IsKeptTeardown(capture))
                {
                    continue;
                }

                if (capture.TeardownInHeadway)
                    continue;
                try
                {
                    TearDownCapture(capture);
                    FreeTeardownCapture(capture);
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }

            ConcludeSessWipeIfConverged();
        }
        finally
        {
            _isClearing = false;
        }

        if (misses is not null)
            throw new AggregateException("One or more live entities failed session teardown", misses);
    }

    public int ReattemptPendingTeardowns()
    {
        if (_projections.TeardownTally == 0
            || _isClearing
            || _isRegisteringAssetList
            || _logicalTeardownZDepth != 0)
            return 0;

        int finished = 0;
        List<Exception>? misses = null;
        foreach (OnlineActorRecord capture in _projections.TeardownRecords.ToArray())
        {
            if (!_projections.IsKeptTeardown(capture))
            {
                continue;
            }

            if (capture.TeardownInHeadway)
                continue;

            _logicalTeardownZDepth++;
            try
            {
                try
                {
                    TearDownCapture(capture);
                    FreeTeardownCapture(capture);
                    finished++;
                    if (!_directory.TryFetchEngaged(capture.ServerOid, out _)
                        && !HasQueuedTeardown(capture.ServerOid)
                        && !_directory.TryGetSnapshot(capture.ServerOid, out _))
                    {
                        _spatial.DropOnlineActor(capture.ServerOid);
                    }
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }
            finally
            {
                _logicalTeardownZDepth--;
            }
        }

        return misses is not null
            ? throw new AggregateException("One or more pending live-entity teardowns failed again", misses)
            : finished;
    }

    private void RenewCapture(
        uint oid,
        RealmSession.MoverSpawn approved,
        bool renewLocus = false)
    {
        if (!_directory.TryFetchEngaged(oid, out SimActorRecord canon))
            return;
        _directory.RenewCapture(canon, approved, renewLocus);
    }

    private ulong ProgressLifespanAlteration(uint srvOid)
        => _directory.ProgressLifespanAlteration(srvOid);

    private ulong LatestLifespanAlteration(uint srvOid) =>
        _directory.LatestLifespanAlteration(srvOid);

    private static SimActorKey DemandProjTag(
        OnlineActorRecord capture) =>
        capture.ProjTag ?? capture.Canonical.Key
        ?? throw new InvalidOperationException(
            $"Live entity 0x{capture.ServerOid:X8}/{capture.Generation} has no materialized projection key");

    private bool IsLatestProjOp(
        uint srvOid,
        OnlineActorRecord capture,
        ulong projOp) =>
        _projections.TryFetchLatest(srvOid, out OnlineActorRecord? latest)
        && ReferenceEquals(latest, capture)
        && capture.ProjAlterationVer == projOp;

    private static void HurlFollowingSealedProjEdit(
        uint srvOid,
        Exception? spatialNotificationMiss,
        Exception? coreNotificationMiss)
    {
        if (spatialNotificationMiss is not null
            && coreNotificationMiss is not null)
        {
            throw new AggregateException(
                $"Projection change for live entity 0x{srvOid:X8} committed, but spatial and runtime observers failed",
                spatialNotificationMiss,
                coreNotificationMiss);
        }

        Exception? miss = spatialNotificationMiss ?? coreNotificationMiss;
        if (miss is not null)
            ExceptionDispatchInfo.Capture(miss).Throw();
    }

    private void AcknowledgeCelllessCanonSeal(
        SimActorRecord canon)
    {
        if (!_directory.IsCurrent(canon)
            || !_projections.TryGet(
                canon,
                out OnlineActorRecord? capture))
        {
            return;
        }

        SynchronizeKineticsCorpusEngagedPhase(capture);
        RenewSpatialCoreIndexes(capture);
    }

    internal bool IsLatestCapture(OnlineActorRecord capture) =>
        _projections.TryFetchLatest(capture.ServerOid, out OnlineActorRecord? latest)
        && ReferenceEquals(latest, capture);

    private static bool HasSpatialCoreProj(OnlineActorRecord capture) =>
        capture.WorldEntity is not null
        && capture.ProjSort is OnlineActorMirrorKind.World
        && capture.IsSpatiallyProjected
        && capture.IsSpatiallyVisible
        && capture.WholeChamberIdent != 0;

    private void RenewSpatialCoreIndexes(OnlineActorRecord capture)
    {
        bool latest = IsLatestCapture(capture);
        bool spatial = latest && HasSpatialCoreProj(capture);
        if (capture.ProjTag is not { } tag)
        {
            if (capture.WorldEntity is not null)
            {
                throw new InvalidOperationException(
                    $"Materialized live entity 0x{capture.ServerOid:X8}/{capture.Generation} has no exact projection key");
            }
            return;
        }

        _physics.AcknowledgeSpatialProj(capture.Canonical, spatial);

        RenewSpatialExhibitIndexes(capture, latest, spatial, tag);
    }

    private void RenewSpatialExhibitIndexes(OnlineActorRecord capture)
    {
        bool latest = IsLatestCapture(capture);
        bool spatial = latest && HasSpatialCoreProj(capture);
        if (capture.ProjTag is not { } tag)
        {
            if (capture.WorldEntity is not null)
            {
                throw new InvalidOperationException(
                    $"Materialized live entity 0x{capture.ServerOid:X8}/{capture.Generation} has no exact projection key");
            }
            return;
        }

        RenewSpatialExhibitIndexes(capture, latest, spatial, tag);
    }

    private void RenewSpatialExhibitIndexes(
        OnlineActorRecord capture,
        bool latest,
        bool spatial,
        SimActorKey tag)
    {

        if (capture.WorldEntity is not null)
        {
            if (spatial && capture.AnimationRuntime is { } anim)
            {
                _spatialAnims[tag] = anim;
            }
            else if (latest
                      || (capture.AnimationRuntime is { } keptAnim
                          && _spatialAnims.TryGetValue(tag, out var indexedAnim)
                          && ReferenceEquals(indexedAnim, keptAnim)))
            {
                _spatialAnims.Remove(tag);
            }
        }

    }

    private void DropSpatialCoreIndexes(OnlineActorRecord capture)
    {
        if (capture.ProjTag is not { } tag)
            return;

        _physics.DropSpatialProj(capture.Canonical);

        if (capture.WorldEntity is not null
            && _spatialAnims.TryGetValue(tag, out var indexedAnim)
            && ReferenceEquals(indexedAnim, capture.AnimationRuntime))
        {
            _spatialAnims.Remove(tag);
        }

    }

    private void OnSpatialVisAltered(SimActorKey tag, bool shown)
    {
        if (_spatial.IsOnlineActorProjHoused(tag) != shown)
            return;
        if (!_projections.TryGet(
                tag,
                out OnlineActorRecord? capture)
            || capture.WorldEntity is not { } actor
            || actor.Id != tag.LocalEntityId)
            return;

        uint srvOid = capture.ServerOid;
        bool wasShown = capture.IsSpatiallyVisible;
        if (DemandProjTag(capture) != tag)
            return;
        if (_exhibitSoleSpatialAlterationZDepth.ContainsKey(tag))
        {
            capture.IsSpatiallyVisible = shown;
            RenewSpatialExhibitIndexes(capture);
            RenewExhibit(capture);
            return;
        }
        bool wasPlainTrunk = _physics.IsSpatialTrunk(capture.Canonical);
        capture.IsSpatiallyVisible = shown;
        bool isPlainTrunk = capture.ProjSort is OnlineActorMirrorKind.World
            && capture.IsSpatiallyProjected
            && shown
            && capture.WholeChamberIdent != 0;
        if (_rebucketingOid != srvOid && wasPlainTrunk != isPlainTrunk)
        {
            if (isPlainTrunk)
                capture.RestartObjectTimerForJoinRealm(
                    (capture.FinalKineticsPhase & KineticStateFlags.Static) != 0);
            else
                capture.SuspendObjectTimer();
            SynchronizeKineticsCorpusEngagedPhase(capture);
        }
        RenewSpatialCoreIndexes(capture);
        RenewExhibit(capture);
        if (_rebucketingOid != srvOid && wasShown != shown)
            BroadcastProjVisAltered(capture, shown);
    }

    private void OnCoreKineticsChamberSealed(
        SimKineticsCellCommit seal)
    {
        if (seal.Record.SpatialAuthorityVersion
                != seal.SpatialAuthorityVersion
            || !_directory.IsCurrent(seal.Record)
            || !_projections.TryGet(
                seal.Record,
                out OnlineActorRecord? proj)
            || proj.WorldEntity is null)
        {
            return;
        }

        RebucketLiveEntity(
            seal.Record.ServerGuid,
            seal.FullCellId);
    }

    private static void SynchronizeKineticsCorpusEngagedPhase(
        OnlineActorRecord capture)
    {
        if (capture.KineticBody is not { } corpus)
            return;
        if (capture.ObjectTimer.IsActive)
            corpus.TransientState |= TransientPhaseFlagSet.Active;
        else
            corpus.TransientState &= ~TransientPhaseFlagSet.Active;
    }

    private void BroadcastProjVisAltered(OnlineActorRecord capture, bool shown)
    {
        Delegate[] subscribers = ProjectionVisibilityChanged?.GetInvocationList()
            ?? [];
        List<Exception>? misses = null;
        for (int idx = 0; idx < subscribers.Length; idx++)
        {
            try
            {
                ((Action<OnlineActorRecord, bool>)subscribers[idx])(capture, shown);
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }

        if (misses is not null)
        {
            throw new AggregateException(
                $"One or more projection observers failed for live entity 0x{capture.ServerOid:X8}.",
                misses);
        }
    }

    private void RenewExhibit(OnlineActorRecord capture)
    {
        if (capture.WorldEntity is not { } actor)
            return;

        KineticStateFlags phase = capture.FinalKineticsPhase;
        bool residenceShown = capture.MaterializationResidence is not
                OnlineActorMaterializationResidence.AwaitRuntimePlacement
            || capture.IsSpatiallyProjected;
        actor.IsPaintShown = residenceShown
            && (phase & (KineticStateFlags.NoDraw | KineticStateFlags.Hidden)) == 0;

        bool dealingShown = capture.IsSpatiallyVisible
            && capture.ProjSort is OnlineActorMirrorKind.World
            && (phase & KineticStateFlags.Hidden) == 0;
        if (dealingShown)
            _projections.AssignShown(capture, true);
        else
            _projections.AssignShown(capture, false);
    }

    private void KeepTeardownCapture(OnlineActorRecord capture)
    {
        _directory.HoldTeardown(capture.Canonical);
        _projections.KeepTeardown(capture);
    }

    private Exception? RetirePrecedingProj(
        SimActorRecord canon)
    {
        if (!_projections.TryGet(
                canon,
                out OnlineActorRecord? proj))
        {
            return _entityObjects.RetireCanonSole(canon);
        }

        if (!_projections.RemoveActive(proj))
        {
            return new InvalidOperationException(
                $"Exact App projection for 0x{canon.ServerGuid:X8}/{canon.Incarnation} could not be retired");
        }

        KeepTeardownCapture(proj);
        _logicalTeardownZDepth++;
        try
        {
            try
            {
                TearDownCapture(proj);
                FreeTeardownCapture(proj);
                return null;
            }
            catch (Exception problem)
            {
                return problem;
            }
        }
        finally
        {
            _logicalTeardownZDepth--;
        }
    }

    private void TearDownCanonSole(SimActorRecord canon)
    {
        Exception? miss = _entityObjects.RetireCanonSole(canon);
        if (miss is not null)
            ExceptionDispatchInfo.Capture(miss).Throw();
    }

    private void FreeTeardownCapture(OnlineActorRecord capture)
    {
        _projections.FreeTeardown(capture);
        _directory.RelinquishTeardown(capture.Canonical);
        ConcludeSessWipeIfConverged();
    }

    private bool HasQueuedTeardown(uint srvOid) =>
        _directory.HasQueuedTeardown(srvOid);

    private void ConcludeSessWipeIfConverged()
    {
        if (!_sessWipeQueuedFinalization
            || !_entityObjects.FinishSessWipeIfConverged())
        {
            return;
        }

        _spatialAnims.Clear();
        _physics.WipeSpatialWorksets();
        _projections.WipeConverged();
        _spatial.WipeOnlineActorLifespanPhase();
        _sessWipeQueuedFinalization = false;
    }

    private void TearDownCapture(
        OnlineActorRecord capture,
        bool doneCanon = true)
    {
        if (capture.TeardownInHeadway)
            throw new InvalidOperationException(
                $"Live entity 0x{capture.ServerOid:X8} teardown is by now in progress");

        capture.TeardownInHeadway = true;
        List<Exception>? misses = null;
        bool TryTidy(Action tidy)
        {
            try
            {
                tidy();
                return true;
            }
            catch (Exception problem)
            {
                (misses ??= new List<Exception>()).Add(problem);
                return false;
            }
        }

        try
        {
            DropSpatialCoreIndexes(capture);
            _physics.WipeMissile(capture.Canonical);

            if (!capture.CoreModulesTeardownFinished)
            {
                capture.CoreModulesTeardownFinished =
                    TryTidy(() => _coreModuleLifecycle.TearDown(capture));
            }

            if (capture.WorldEntity is { } actor)
            {
                if (!capture.SpatialProjTeardownFinished)
                {
                    capture.SpatialProjTeardownFinished =
                        TryTidy(() => _spatial.RemoveLiveEntityProjection(actor));
                }
                if (capture.ResourcesRegistered
                    && TryTidy(() => _assetList.Unregister(actor)))
                {
                    capture.ResourcesRegistered = false;
                }
            }

            if (misses is not null)
            {
                throw new AggregateException(
                    $"Live entity 0x{capture.ServerOid:X8} teardown failed",
                    misses);
            }

            if (capture.WorldEntity is not null)
                _ = DemandProjTag(capture);
            if (doneCanon)
            {
                _entityObjects.CompleteProjectionRetirement(
                    capture.Canonical);
            }

            capture.AnimationRuntime = null;
            capture.EffectProfile = null;
            capture.IsSpatiallyProjected = false;
            capture.IsSpatiallyVisible = false;
            capture.RealmSummonPublished = false;
            capture.StartingHydrationFinished = false;
            capture.BuildProjSynchronizationQueued = false;
            capture.ProjHydrationInHeadway = false;
            capture.ProjHydrationBuildVer = 0UL;
            capture.ProjHydrationReattemptAsked = false;
            capture.LooksProjSynchronizationQueued = false;
            capture.LooksHydrationInHeadway = false;
            capture.LooksHydrationVer = 0UL;
            capture.LooksHydrationReattemptAsked = false;
            capture.WorldEntity = null;
        }
        finally
        {
            capture.TeardownInHeadway = false;
        }
    }

}
