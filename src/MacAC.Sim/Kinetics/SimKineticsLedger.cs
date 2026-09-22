using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

public sealed partial class SimKineticsLedger : IDisposable
{
    private readonly TimeProvider _clock;

    private readonly ISimCoreClock? _simTimer;

    private readonly Dictionary<SimActorKey, SimActorRecord> _trunks = new();

    private readonly Dictionary<SimActorKey, ISimPeerMotion> _remotes = new();

    private readonly Dictionary<SimActorKey, ISimMissile> _missiles = new();

    private bool _avatarBuildObserved;

    private uint _cycleMiddleLb;

    private bool _destroyed;

    internal bool IsDestroyed => _destroyed;

    public event Action<SimKineticsCellCommit>? CellCommitted;

    internal SimKineticsLedger(
        SimActorIndex entities,
        KineticAssetCache? blobStash = null,
        TimeProvider? momentSupplier = null,
        ISimCoreClock? playTimer = null)
    {
        Entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _clock = momentSupplier ?? TimeProvider.System;
        _simTimer = playTimer;
        DataCache = blobStash ?? KineticAssetCache.CreateProduction();
        Engine = new KineticEngine
        {
            DataCache = DataCache,
        };
        ImpactDossiers = new SimContactNoticesLedger(
            Entities,
            Engine.ShadeObjects);
        SetPosition = new SimSetPositionLedger(this, Entities);
    }

    internal SimKineticsLedger(
        SimActorIndex entities,
        KineticEngine engine,
        TimeProvider? momentSupplier = null,
        ISimCoreClock? playTimer = null)
    {
        Entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _clock = momentSupplier ?? TimeProvider.System;
        _simTimer = playTimer;
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        DataCache = engine.DataCache
            ?? KineticAssetCache.CreateProduction(engine.ImpactRealm);
        Engine.DataCache = DataCache;
        ImpactDossiers = new SimContactNoticesLedger(
            Entities,
            Engine.ShadeObjects);
        SetPosition = new SimSetPositionLedger(this, Entities);
    }

    internal SimActorIndex Entities { get; }

    public KineticEngine Engine { get; }

    public KineticAssetCache DataCache { get; }

    internal SimContactNoticesLedger ImpactDossiers { get; }

    internal SimSetPositionLedger SetPosition { get; }

    public int SpatialTrunkTally => _trunks.Count;

    public int SpatialDistantTally => _remotes.Count;

    public int SpatialMissileTally => _missiles.Count;

    internal double UtcInstantSecs =>
        (_clock.GetUtcNow() - DateTimeOffset.UnixEpoch)
            .TotalSeconds;

    internal double MonotonicInstantSecs =>
        _clock.GetTimestamp()
        / (double)_clock.TimestampFrequency;

    internal double StanceSimulationMoment(double backup) =>
        _simTimer?.SimulationMomentSecs ?? backup;

    internal void WatchOwnRealmCycle(
        uint wholeChamberIdent,
        bool warpAdvanced)
    {
        Live();
        if (wholeChamberIdent == 0u)
            return;
        if (_cycleMiddleLb == 0u || warpAdvanced)
        {
            _cycleMiddleLb =
                (wholeChamberIdent & 0xFFFF0000u) | 0xFFFFu;
        }
    }

    internal uint RealmCycleMiddleLbIdent => _cycleMiddleLb;

    internal void WatchOwnAvatarBuild(uint wholeChamberIdent)
    {
        Live();
        _avatarBuildObserved = true;
        WatchOwnRealmCycle(wholeChamberIdent, warpAdvanced: false);
    }

    internal void HurlIfRealmCycleUnreachable(uint wholeChamberIdent)
    {
        if (!_avatarBuildObserved || _cycleMiddleLb != 0u)
            return;

        throw new InvalidOperationException(
            "Runtime's world frame is unreachable: the local-player Create "
            + "was accepted without publishing a frame, so the placement for "
            + $"landblock 0x{wholeChamberIdent & 0xFFFF0000u:X8} can never resolve. "
            + "A local-player ObjectCreation must carry a non-zero landblock");
    }

    internal bool TryFetchRealmCycleShift(
        uint wholeChamberIdent,
        out float realmShiftX,
        out float realmShiftY)
    {
        if (_cycleMiddleLb == 0u || wholeChamberIdent == 0u)
        {
            realmShiftX = 0f;
            realmShiftY = 0f;
            return false;
        }

        int middleX = (int)((_cycleMiddleLb >> 24) & 0xFFu);
        int middleY = (int)((_cycleMiddleLb >> 16) & 0xFFu);
        int lbX = (int)((wholeChamberIdent >> 24) & 0xFFu);
        int lbY = (int)((wholeChamberIdent >> 16) & 0xFFu);
        realmShiftX = (lbX - middleX) * 192f;
        realmShiftY = (lbY - middleY) * 192f;
        return true;
    }

    public SimKineticsHoldingCapture CaptureOwnership()
    {
        SimSetPositionHoldingCapture setLocus =
            SetPosition.GrabOwnership();
        SimContactNoticesHoldingCapture impactDossiers =
            ImpactDossiers.GrabOwnership();
        return new(
            Engine.LandblockTally,
            Engine.ShadeObjects.KeptEnrollmentTally,
            _trunks.Count,
            _remotes.Count,
            _missiles.Count,
            setLocus.ActiveOperationCount,
            setLocus.AwaitingPreparationCount,
            setLocus.DeferredCellCount,
            setLocus.PendingProjectionAcknowledgementCount,
            setLocus.LostDeadlineCount,
            setLocus.LostDeadlineNodeCount,
            setLocus.LostDeadlineIndexCount,
            setLocus.ExpiredLostCellCount,
            setLocus.ExpiredLostCellIndexCount,
            setLocus.DeferredBucketCount,
            setLocus.DeferredBucketOrderCount,
            setLocus.UnboundDeferredCellCount,
            setLocus.UnboundDeferredCellOrderCount,
            setLocus.PreparedMoverCount,
            impactDossiers.OwnerCount,
            impactDossiers.TrackedObjectCount,
            impactDossiers.ReversePeerCount,
            impactDossiers.ObserverCount,
            impactDossiers.PendingReportCount,
            impactDossiers.LeavingOwnerCount,
            impactDossiers.AdmissionBlockedOwnerCount,
            impactDossiers.PendingSetPositionDispatchCount,
            Engine.ShadeObjects.QueuedSetLocusRelayTally,
            impactDossiers.IsDispatching,
            setLocus.CollisionPrefixQuiescenceCount,
            setLocus.PendingQuiescenceProjectionCount,
            _stemEdits.Count,
            _stemEdits.Values.Count(
                alteration => alteration.EngineAlterationSealed),
            _linkIntakes.Count,
            _linkEpochs.Count,
            ReferenceEquals(Engine.DataCache, DataCache),
            _destroyed);
    }

    public void RenewDistantModule(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag
            || !_trunks.TryGetValue(tag, out SimActorRecord? trunk)
            || !ReferenceEquals(trunk, capture)
            || !Entities.IsCurrent(capture))
        {
            DiscardDistant(capture);
            return;
        }

        if (capture.PeerMotion is { } distant)
            _remotes[tag] = distant;
        else
            _remotes.Remove(tag);
    }

    public void RenewMissileModule(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag
            || !_trunks.TryGetValue(tag, out SimActorRecord? trunk)
            || !ReferenceEquals(trunk, capture)
            || !Entities.IsCurrent(capture))
        {
            DiscardMissile(capture);
            return;
        }

        if (capture.Projectile is { } missile)
            _missiles[tag] = missile;
        else
            _missiles.Remove(tag);
    }

    public ISimMissile AttachMissile(
        SimActorRecord capture,
        KineticBody corpus,
        MissileContactSphere collisionSphere,
        Func<bool>? externalHolderValid = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        DemandLatest(capture);
        if (!collisionSphere.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionSphere),
                "A Runtime projectile needs one valid prepared Setup sphere");
        }
        if (!(externalHolderValid?.Invoke() ?? true))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed external ownership prior to projectile binding");
        }
        if (capture.Projectile is { } kept)
        {
            if (!ReferenceEquals(kept.Body, corpus)
                || !ReferenceEquals(capture.KineticBody, corpus))
            {
                throw new InvalidOperationException(
                    $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} projectile changed its canonical physics body");
            }

            kept.Body.State = capture.FinalKineticsCondition;
            RenewMissileModule(capture);
            return kept;
        }
        if (capture.MissileMappingInHeadway)
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} projectile binding is by now in progress");
        }
        if (!ReferenceEquals(capture.KineticBody, corpus))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} projectile must borrow its canonical physics body");
        }

        ulong sessVer = Entities.SessionLifetimeVersion;
        Entities.AssignMissileMappingInHeadway(capture, true);
        try
        {
            var component = new SimMissile(corpus, collisionSphere);
            if (Entities.SessionLifetimeVersion != sessVer
                || !Entities.IsCurrent(capture)
                || !ReferenceEquals(capture.KineticBody, corpus)
                || capture.Projectile is not null
                || !(externalHolderValid?.Invoke() ?? true))
            {
                throw new InvalidOperationException(
                    $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed ownership during projectile binding");
            }

            Entities.AssignMissile(capture, component);
            corpus.State = capture.FinalKineticsCondition;
            MirrorEngagedPhase(capture);
            RenewMissileModule(capture);
            return component;
        }
        finally
        {
            if (Entities.IsCurrent(capture))
                Entities.AssignMissileMappingInHeadway(capture, false);
            else
                capture.MissileMappingInHeadway = false;
        }
    }

    public bool WipeMissile(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Projectile is null)
            return false;

        Entities.AssignMissile(capture, null);
        Entities.AssignMissileMappingInHeadway(capture, false);
        RenewMissileModule(capture);
        return true;
    }

    internal bool TryCommitAuthoritativeVector(
        SimActorRecord capture,
        KineticBody corpus,
        Vector3 vel,
        Vector3? angularVel,
        double latestMoment,
        Func<bool>? externalHolderValid = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        if (!Finite(vel)
            || (angularVel is { } omega && !Finite(omega))
            || !double.IsFinite(latestMoment)
            || !Entities.IsCurrent(capture)
            || !ReferenceEquals(capture.KineticBody, corpus)
            || !(externalHolderValid?.Invoke() ?? true))
        {
            return false;
        }

        bool wasCorpusEngaged =
            (corpus.TransientState & TransientPhaseFlagSet.Active) != 0;
        corpus.set_velocity(vel);
        if ((capture.FinalKineticsCondition & KineticStateFlags.Static) != 0)
        {
            if (!wasCorpusEngaged)
                corpus.TransientState &= ~TransientPhaseFlagSet.Active;
        }
        else
        {
            bool timerReactivated = capture.ObjectClock.Engage();
            if (timerReactivated || !wasCorpusEngaged)
                corpus.PreviousRefreshMoment = latestMoment;
        }

        if (angularVel is { } approvedOmega)
            corpus.Omega = approvedOmega;
        return Entities.IsCurrent(capture)
            && ReferenceEquals(capture.KineticBody, corpus)
            && (externalHolderValid?.Invoke() ?? true);
    }

    public void ApplyDistantLocomotion(
        SimActorRecord capture,
        ISimPeerMotion core,
        Func<bool>? externalHolderValid = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(core);
        DemandLatest(capture);
        if (!(externalHolderValid?.Invoke() ?? true))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed external ownership prior to remote-motion binding");
        }
        if (capture.DistantLocomotionMappingInHeadway)
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} remote-motion binding is by now in progress");
        }

        KineticBody contenderCorpus = core.Body
            ?? throw new InvalidOperationException(
                "A remote-motion runtime returned no physics body");
        if (ReferenceEquals(capture.PeerMotion, core))
        {
            if (!ReferenceEquals(capture.KineticBody, contenderCorpus))
            {
                throw new InvalidOperationException(
                    $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} remote motion changed its canonical physics body");
            }
            contenderCorpus.State = capture.FinalKineticsCondition;
            MirrorEngagedPhase(capture);
            RenewDistantModule(capture);
            return;
        }
        if (capture.KineticsCorpusAcquisitionInHeadway
            && capture.KineticBody is null)
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} can't bind remote motion during physics-body acquisition");
        }
        if (capture.KineticBody is { } canonCorpus
            && !ReferenceEquals(canonCorpus, contenderCorpus))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} can't replace its canonical physics body");
        }
        if (capture.RequiresDistantStanceCore
            && core is not ISimPeerPlacement)
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} can't discard its remote-placement contract");
        }

        ulong sessVer = Entities.SessionLifetimeVersion;
        KineticBody? anticipatedCorpus = capture.KineticBody;
        ISimPeerMotion? anticipatedCore = capture.PeerMotion;
        bool anticipatedStanceContract =
            capture.RequiresDistantStanceCore;
        Func<IKineticObjHost?> scanKineticsHub =
            () => Entities.IsCurrent(capture)
                && ReferenceEquals(capture.PeerMotion, core)
                    ? capture.PhysicsHost
                    : null;
        Func<uint> scanChamber = () => capture.WholeChamberTag;
        Action<uint> emitChamber = chamberIdent =>
            SealChamber(capture, core, chamberIdent);

        Entities.AssignDistantLocomotionMappingInHeadway(capture, true);
        try
        {
            if (core is ISimCanonicalKineticsConsumer canonConsumer)
            {
                canonConsumer.AttachCanonCore(
                    scanKineticsHub,
                    scanChamber,
                    emitChamber);
            }
            else
            {
                bool consumesHub = core is ISimKineticsHarborConsumer;
                bool consumesChamber = core is ISimCanonicalCellConsumer;
                if (consumesHub && consumesChamber)
                {
                    throw new InvalidOperationException(
                        "A remote runtime that consumes both canonical host and cell identity must bind them atomically");
                }
                if (core is ISimKineticsHarborConsumer hubConsumer)
                    hubConsumer.AttachKineticsHub(scanKineticsHub);
                if (core is ISimCanonicalCellConsumer chamberConsumer)
                    chamberConsumer.AttachCanonChamber(scanChamber, emitChamber);
            }

            if (Entities.SessionLifetimeVersion != sessVer
                || !Entities.IsCurrent(capture)
                || !ReferenceEquals(capture.KineticBody, anticipatedCorpus)
                || !ReferenceEquals(capture.PeerMotion, anticipatedCore)
                || capture.RequiresDistantStanceCore
                    != anticipatedStanceContract
                || !(externalHolderValid?.Invoke() ?? true))
            {
                throw new InvalidOperationException(
                    $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed ownership during remote-motion binding");
            }

            Entities.AssignRequiresDistantStanceCore(
                capture,
                anticipatedStanceContract
                    || core is ISimPeerPlacement);
            if (anticipatedCorpus is null)
                SeedCorpus(capture, contenderCorpus);
            Entities.AssignDistantLocomotion(capture, core);
            Entities.AssignKineticsCorpus(capture, contenderCorpus);
            contenderCorpus.State = capture.FinalKineticsCondition;
            MirrorEngagedPhase(capture);
            RenewDistantModule(capture);
        }
        finally
        {
            if (Entities.IsCurrent(capture))
            {
                Entities.AssignDistantLocomotionMappingInHeadway(capture, false);
            }
        }
    }

    internal PeerMotion GetOrCreateRemoteMotion(
        SimActorRecord capture,
        Func<bool>? externalHolderValid = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        DemandLatest(capture);
        if (capture.PeerMotion is { } kept)
        {
            return kept as PeerMotion
                ?? throw new InvalidOperationException(
                    $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} owns a non-production remote-motion component");
        }

        var built = new PeerMotion(capture.KineticBody);
        built.Movement.EngageKineticsObject = () =>
            TryActivateOrdinaryObject(capture, built);
        ApplyDistantLocomotion(capture, built, externalHolderValid);
        return built;
    }

    internal bool TryActivateOrdinaryObject(
        SimActorRecord capture,
        ISimPeerMotion core,
        Func<bool>? externalHolderValid = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(core);
        if (!Entities.IsCurrent(capture)
            || !ReferenceEquals(capture.PeerMotion, core)
            || (capture.FinalKineticsCondition & KineticStateFlags.Static) != 0
            || !(externalHolderValid?.Invoke() ?? true))
        {
            return false;
        }

        capture.ObjectClock.Engage();
        core.Body.TransientState |= TransientPhaseFlagSet.Active;
        return true;
    }

    public KineticBody GetOrCreatePhysicsBody(
        SimActorRecord capture,
        Func<SimActorRecord, KineticBody> maker,
        Func<bool>? externalHolderValid = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(maker);
        DemandLatest(capture);
        if (!(externalHolderValid?.Invoke() ?? true))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed external ownership prior to physics-body acquisition");
        }
        if (capture.KineticBody is { } kept)
            return kept;
        if (capture.KineticsCorpusAcquisitionInHeadway)
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} physics-body acquisition is by now in progress");
        }

        Entities.AssignKineticsCorpusAcquisitionInHeadway(capture, true);
        try
        {
            KineticBody contender = maker(capture)
                ?? throw new InvalidOperationException(
                    "Physics-body factory returned null");
            if (!Entities.IsCurrent(capture)
                || !(externalHolderValid?.Invoke() ?? true))
            {
                throw new InvalidOperationException(
                    $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed ownership during physics-body acquisition");
            }
            if (capture.KineticBody is { } concurrentlyTied)
            {
                if (ReferenceEquals(concurrentlyTied, contender))
                    return concurrentlyTied;
                throw new InvalidOperationException(
                    $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} acquired two physics bodies within one incarnation");
            }

            SeedCorpus(capture, contender);
            Entities.AssignKineticsCorpus(capture, contender);
            contender.State = capture.FinalKineticsCondition;
            MirrorEngagedPhase(capture);
            return contender;
        }
        finally
        {
            if (Entities.IsCurrent(capture))
                Entities.AssignKineticsCorpusAcquisitionInHeadway(capture, false);
            else
                capture.KineticsCorpusAcquisitionInHeadway = false;
        }
    }

    public void PlaceKineticsHub(
        SimActorRecord capture,
        IKineticObjHost host,
        Func<bool>? externalHolderValid = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(host);
        DemandLatest(capture);
        if (host.Id != capture.ServerGuid)
        {
            throw new ArgumentException(
                "A physics host must match its runtime entity GUID",
                nameof(host));
        }
        if (!(externalHolderValid?.Invoke() ?? true))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed external ownership prior to physics-host installation");
        }
        if (capture.PhysicsHost is not null)
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} by now owns its incarnation-stable physics host");
        }

        Entities.AssignKineticsHub(capture, host);
        if (!Entities.IsCurrent(capture)
            || !(externalHolderValid?.Invoke() ?? true))
        {
            if (ReferenceEquals(capture.PhysicsHost, host))
                capture.PhysicsHost = null;
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed ownership during physics-host installation");
        }
    }

    public ActorKineticsHarbor SetupOrRebindKineticsHub(
        SimActorRecord capture,
        ActorKineticsHarbor configuration,
        Func<bool>? externalHolderValid = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(configuration);
        DemandLatest(capture);
        if (!(externalHolderValid?.Invoke() ?? true))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed external ownership prior to physics-host composition");
        }

        if (capture.PhysicsHost is null)
        {
            PlaceKineticsHub(capture, configuration, externalHolderValid);
            return configuration;
        }
        if (capture.PhysicsHost is not ActorKineticsHarbor extant)
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} owns an incompatible physics-host implementation");
        }

        extant.RebindFrom(configuration);
        if (!Entities.IsCurrent(capture)
            || !(externalHolderValid?.Invoke() ?? true))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed ownership during physics-host composition");
        }
        return extant;
    }

    public ActorKineticsHarbor PickStableKineticsHubWithoutRebind(
        SimActorRecord capture,
        ActorKineticsHarbor configuration,
        Func<bool>? externalHolderValid = null)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(configuration);
        DemandLatest(capture);
        if (!(externalHolderValid?.Invoke() ?? true))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} changed external ownership during physics-host preparation");
        }

        return capture.PhysicsHost switch
        {
            null => configuration,
            ActorKineticsHarbor extant => extant,
            _ => throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} owns an incompatible physics-host implementation"),
        };
    }

    public bool TryGetPhysicsHost(
        uint srvOid,
        out IKineticObjHost hub)
    {
        Live();
        if (Entities.TryFetchEngaged(srvOid, out SimActorRecord capture)
            && capture.PhysicsHost is { } extant)
        {
            hub = extant;
            return true;
        }

        hub = null!;
        return false;
    }

    private Func<uint, IKineticObjHost?>? _chartHubLocator;

    public void AttachObjectChartHubLocator(
        Func<uint, IKineticObjHost?>? locator)
    {
        Live();
        _chartHubLocator = locator;
    }

    public IKineticObjHost? LocateObjectChartHub(
        uint srvOid)
    {
        Live();
        if (_chartHubLocator is { } locator)
            return locator(srvOid);
        return TryGetPhysicsHost(srvOid, out var hub) ? hub : null;
    }

    public bool WipeDistantLocomotion(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (!Entities.IsCurrent(capture)
            || capture.PeerMotion is null)
        {
            return false;
        }

        Entities.AssignDistantLocomotion(capture, null);
        RenewDistantModule(capture);
        return true;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        foreach ((_, StagedLandblockContactEpoch readied) in
                 _linedLinkEpochs)
        {
            readied.Dispose();
        }
        _linedLinkEpochs.Clear();
        SetPosition.Dispose();
        ImpactDossiers.Dispose();
        Engine.Clear();
        _remotes.Clear();
        _missiles.Clear();
        _trunks.Clear();
        _stemEdits.Clear();
        _linkIntakes.Clear();
        _linkEpochs.Clear();
        _cycleMiddleLb = 0u;
        _avatarBuildObserved = false;
        CellCommitted = null;
        _epochSealTaps.Clear();
        _chartHubLocator = null;
        _destroyed = true;
    }

    private void Live() =>
        ObjectDisposedException.ThrowIf(_destroyed, this);

    private void DemandLatest(SimActorRecord capture)
    {
        if (!Entities.IsCurrent(capture))
        {
            throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} isn't current");
        }
    }

    internal ClientThingChart? ObjectChart => Engine.Objects;

    internal ulong ObjectChartMappingArbiter =>
        Engine.ObjectsMappingRev;

    internal ulong ObjectChartArbiter =>
        ObjectChart?.AlterationRev ?? 0UL;

    internal ulong ShadeRealmArbiter =>
        Engine.ShadeObjects.AlterationRev;

    private static bool Finite(Vector3 val) =>
        float.IsFinite(val.X)
        && float.IsFinite(val.Y)
        && float.IsFinite(val.Z);

    private static void MirrorEngagedPhase(SimActorRecord capture)
    {
        if (capture.KineticBody is not { } corpus)
            return;
        if (capture.ObjectClock.IsActive)
            corpus.TransientState |= TransientPhaseFlagSet.Active;
        else
            corpus.TransientState &= ~TransientPhaseFlagSet.Active;
    }

    private static void SeedCorpus(
        SimActorRecord capture,
        KineticBody corpus)
    {
        corpus.State = capture.FinalKineticsCondition;
        if (capture.Snapshot.Physics is not { } kinetics)
            return;
        if (kinetics.Velocity is { } vel && Finite(vel))
            corpus.set_velocity(vel);
        if (kinetics.AngularVelocity is { } omega && Finite(omega))
            corpus.Omega = omega;
    }

    internal bool TryReadySpatialTrunkAdmission(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is null || !Entities.IsCurrent(capture))
            return false;
        _trunks.EnsureCapacity(_trunks.Count + 1);
        _remotes.EnsureCapacity(_remotes.Count + 1);
        _missiles.EnsureCapacity(_missiles.Count + 1);
        return Entities.IsCurrent(capture);
    }
}
