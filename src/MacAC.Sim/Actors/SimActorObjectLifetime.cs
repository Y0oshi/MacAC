using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Play;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

/// <summary>
/// Owns every entity and object the client knows about: the entity index, the object table,
/// physics, the spawn tenancies and debut ledgers, and the event flow that announces changes.
/// Construction and ownership live here; registration in <c>.Register.cs</c>, inbound wire
/// application in <c>.Inbound.cs</c>, projection commits in <c>.Commits.cs</c> and tenancy plumbing
/// in <c>.Tenancy.cs</c>.
/// </summary>
public sealed partial class SimActorObjectLifetime : IDisposable
{
    private bool _clearingSess;

    private bool _destroyed;

    private Action<SimActorRecord>? _tenancyBegan;

    private readonly List<Func<int>> _debutSteerGauges = [];

    private readonly List<Func<int>> _grantedLocusSteerGauges = [];

    private readonly List<Func<int>> _counterpartStanceSteerGauges = [];

    private Func<SimEpochTicket>? _gen;

    private readonly SimActorPvpBitfieldCaptureSync _pvpSynchronize;

    /// <summary>
    /// Shared construction. The three public entry points differ only in where the physics ledger
    /// comes from; everything after that — the object table, the lenses, the event flow, the spawn
    /// ledgers and the wiring between them — is the same, so it lives here and the callers say only
    /// what physics to build.
    /// </summary>
    private SimActorObjectLifetime(
        SimActorIndex entities,
        Func<SimActorIndex, SimKineticsLedger> buildPhysics)
    {
        Entities = entities;
        Physics = buildPhysics(entities);
        Objects = new ClientThingChart();
        _pvpSynchronize = new SimActorPvpBitfieldCaptureSync(Entities, Objects);
        Physics.Engine.Objects = Objects;

        var views = new SimActorObjectLenses(Entities, Objects);
        ActorLens = views.Entities;
        StashLens = views.Inventory;
        Events = new SimActorObjectEventFlow(Entities, Objects);
        Physics.SetPosition.AttachSignalFlow(Events);

        StartingBuildResidences = new SimSpawnTenancyLedger(
            Entities,
            Physics.SetPosition);
        StartingBuildExecution = new SimSpawnFollowupRunner(
            Entities,
            StartingBuildResidences,
            Physics,
            Events,
            (summon, isOwnAvatar) =>
                EnrollActorWithStartingResidence(summon, isOwnAvatar),
            (canon, ver, summon, replaceGen) =>
                ImposeApprovedSummon(canon, ver, summon, replaceGen));
        OwnAvatarLeadListing = new SimAvatarDebutLedger(
            StartingBuildResidences,
            StartingBuildExecution,
            Physics);
        DistantLeadListing = new SimPeerDebutLedger(
            StartingBuildResidences,
            StartingBuildExecution,
            Physics);

        // A tenancy ending has to reach everything that was holding onto it.
        StartingBuildResidences.AttachSunsetNotification(
            tag => StartingBuildExecution.TossHeadway(tag));
        StartingBuildResidences.AttachSunsetNotification(
            tag => OwnAvatarLeadListing.Drop(tag));
        StartingBuildResidences.AttachSunsetNotification(
            tag => DistantLeadListing.Drop(tag));
        Physics.SetPosition.AttachExecutorWrapUpAcknowledgement(
            (tag, series) =>
                StartingBuildExecution.DropWrapUpReceipt(tag, series));

        Placements = new SimPlacementMirrorChannel(
            Events,
            Physics.SetPosition,
            StartingBuildExecution);
    }

    /// <summary>Physics of its own, for a client that has no collision data to hand.</summary>
    public SimActorObjectLifetime(
        uint leadOwnActorIdent = SimActorIndex.LeadOwnActorIdent,
        TimeProvider? momentSupplier = null,
        ISimCoreClock? playTimer = null)
        : this(
            new SimActorIndex(leadOwnActorIdent),
            entities => new SimKineticsLedger(
                entities,
                momentSupplier: momentSupplier,
                playTimer: playTimer))
    {
    }

    /// <summary>Physics reading collision out of an already-loaded cache.</summary>
    internal SimActorObjectLifetime(
        KineticAssetCache kineticsBlobStash,
        uint leadOwnActorIdent = SimActorIndex.LeadOwnActorIdent,
        TimeProvider? momentSupplier = null,
        ISimCoreClock? playTimer = null)
        : this(
            new SimActorIndex(leadOwnActorIdent),
            PhysicsFrom(kineticsBlobStash, momentSupplier, playTimer))
    {
    }

    /// <summary>An engine supplied whole, for tests and for a second world.</summary>
    internal SimActorObjectLifetime(
        KineticEngine kineticsEngine,
        uint leadOwnActorIdent = SimActorIndex.LeadOwnActorIdent,
        TimeProvider? momentSupplier = null,
        ISimCoreClock? playTimer = null)
        : this(
            new SimActorIndex(leadOwnActorIdent),
            PhysicsFrom(kineticsEngine, momentSupplier, playTimer))
    {
    }

    // Built before the shared constructor runs, so a null argument is still refused up front
    // rather than reaching the ledger.
    private static Func<SimActorIndex, SimKineticsLedger> PhysicsFrom(
        KineticAssetCache kineticsBlobStash,
        TimeProvider? momentSupplier,
        ISimCoreClock? playTimer)
    {
        ArgumentNullException.ThrowIfNull(kineticsBlobStash);
        return entities => new SimKineticsLedger(
            entities, kineticsBlobStash, momentSupplier, playTimer);
    }

    /// <inheritdoc cref="PhysicsFrom(KineticAssetCache, TimeProvider, ISimCoreClock)"/>
    private static Func<SimActorIndex, SimKineticsLedger> PhysicsFrom(
        KineticEngine kineticsEngine,
        TimeProvider? momentSupplier,
        ISimCoreClock? playTimer)
    {
        ArgumentNullException.ThrowIfNull(kineticsEngine);
        return entities => new SimKineticsLedger(
            entities, kineticsEngine, momentSupplier, playTimer);
    }

    public SimActorIndex Entities { get; }

    public SimKineticsLedger Physics { get; }

    public ClientThingChart Objects { get; }

    public ISimActorLens ActorLens { get; }

    public ISimStashLens StashLens { get; }

    public SimActorObjectEventFlow Events { get; }

    public SimPlacementMirrorChannel Placements { get; }

    internal SimSpawnTenancyLedger StartingBuildResidences
    { get; }

    internal SimSpawnFollowupRunner StartingBuildExecution
    { get; }

    internal SimAvatarDebutLedger OwnAvatarLeadListing { get; }

    internal SimPeerDebutLedger DistantLeadListing { get; }

    public SimActorObjectHoldingCapture GrabOwnership()
    {
        AnchorAttachmentLedger parents = Entities.AncestorAttachments;
        SimSpawnTenancyHoldingCapture startingResidence =
            StartingBuildResidences.GrabOwnership();
        return new SimActorObjectHoldingCapture(
            Entities.Count,
            Entities.PendingTeardownCount,
            Entities.ClaimedOwnIdentTally,
            Entities.Snapshots.Count,
            parents.UnresolvedRelationTally,
            parents.PostponedBuildTally,
            parents.LinedRelationTally,
            parents.RecoveryRelationTally,
            parents.SealedRelationTally,
            Objects.ObjectCount,
            Objects.VesselTally,
            Objects.VesselProjTally,
            Objects.EquipmentHolderTally,
            Objects.QueuedRelocateTally,
            startingResidence.ActiveLeaseCount
                + startingResidence.PendingAdoptionCount,
            StartingBuildExecution.HeadwayTally,
            Events.SubscriberCount,
            Events.StanceSubscriberTally,
            Events.DispatchMissCount,
            Events.LastRelayFailure is not null,
            Events.QueuedRelayTally,
            Events.IsDispatching,
            _clearingSess,
            _destroyed,
            parents.PostponedApprovedRelationTally,
            StartingBuildExecution.RerunMissTally,
            StartingBuildExecution.PreviousRerunMiss is not null,
            StartingBuildExecution.QueuedWrapUpReceiptTally,
            OwnAvatarLeadListing.GrabOwnership().ActiveCount,
            DistantLeadListing.GrabOwnership().ActiveCount,
            QueuedDebutDrives(),
            QueuedGrantedLocusDrives(),
            QueuedCounterpartStanceDrives());
    }

    private int QueuedDebutDrives()
    {
        int sum = 0;
        for (int idx = 0; idx < _debutSteerGauges.Count; idx++)
            sum = checked(sum + _debutSteerGauges[idx]());
        return sum;
    }

    private int QueuedGrantedLocusDrives()
    {
        int sum = 0;
        for (int idx = 0; idx < _grantedLocusSteerGauges.Count; idx++)
            sum = checked(sum + _grantedLocusSteerGauges[idx]());
        return sum;
    }

    private int QueuedCounterpartStanceDrives()
    {
        int sum = 0;
        for (int idx = 0; idx < _counterpartStanceSteerGauges.Count; idx++)
            sum = checked(sum + _counterpartStanceSteerGauges[idx]());
        return sum;
    }

    public void EnrollLeadListingSteerOwnership(Func<int> queuedTally)
    {
        ArgumentNullException.ThrowIfNull(queuedTally);
        Live();
        _debutSteerGauges.Add(queuedTally);
    }

    public void EnrollApprovedLocusSteerOwnership(Func<int> queuedTally)
    {
        ArgumentNullException.ThrowIfNull(queuedTally);
        Live();
        _grantedLocusSteerGauges.Add(queuedTally);
    }

    public void EnrollDistantStanceSteerOwnership(Func<int> queuedTally)
    {
        ArgumentNullException.ThrowIfNull(queuedTally);
        Live();
        _counterpartStanceSteerGauges.Add(queuedTally);
    }

    public void AttachSignalCtx(
        Func<SimEpochTicket> gen,
        Func<ulong> cycleNumber)
    {
        Live();
        _gen = gen;
        Events.AttachCtx(gen, cycleNumber);
        Placements.AttachGen(gen);
        StartingBuildResidences.AttachGen(gen);
        StartingBuildExecution.AttachGen(gen);
    }

    public void AttachStartingResidenceCommenceNotification(
        Action<SimActorRecord> began)
    {
        ArgumentNullException.ThrowIfNull(began);
        Live();
        _tenancyBegan += began;
    }

    public void AttachOnlineFeeds(
        Func<bool> useLocusFromSrv,
        Func<Vector3?> ownAvatarLocus)
    {
        Live();
        StartingBuildExecution.AttachOnlineFeeds(
            useLocusFromSrv, ownAvatarLocus);
    }

    public void WipeObjects()
    {
        Live();
        Objects.Clear();
    }

    public IReadOnlyList<SimActorRecord> OpenSessWipe()
    {
        Live();
        if (_clearingSess)
            return Array.Empty<SimActorRecord>();

        _clearingSess = true;
        SimActorRecord[] engaged = Entities.ActiveRecords.ToArray();
        StartingBuildResidences.Clear();
        StartingBuildExecution.TossAll();
        OwnAvatarLeadListing.TossAll();
        DistantLeadListing.TossAll();
        Physics.ImpactDossiers.ExitRealmLot(engaged);
        Physics.RestartSessKinetics();
        Entities.CommenceSessWipe();
        foreach (SimActorRecord canon in engaged)
        {
            Physics.SetPosition.Drop(canon);
            if (!Entities.RemoveActive(canon))
                continue;
            Entities.HoldTeardown(canon);
            PublishEntity(SimActorChange.Deleted, canon);
        }
        return engaged;
    }

    public IReadOnlyList<SimActorRecord> GrabSessWipeRetirements()
    {
        Live();
        if (!_clearingSess)
        {
            throw new InvalidOperationException(
                "Session-clear retirements are only available while the "
                + "canonical clear transaction is active.");
        }
        return Entities.TeardownRecords.ToArray();
    }

    public void ConcludeSessActorSunset(
        SimActorRecord canon)
    {
        Live();
        ArgumentNullException.ThrowIfNull(canon);
        if (!_clearingSess
            || !Entities.TryGetTeardown(
                canon.ServerGuid,
                canon.Incarnation,
                out SimActorRecord kept)
            || !ReferenceEquals(kept, canon))
        {
            throw new InvalidOperationException(
                $"Live entity 0x{canon.ServerGuid:X8}/{canon.Incarnation} is not retained by the active session-clear transaction.");
        }

        CompleteProjectionRetirement(canon);
        Entities.RelinquishTeardown(canon);
    }

    public bool FinishSessWipeIfConverged()
    {
        Live();
        if (!_clearingSess
            || !Entities.ConcludeSessWipeIfConverged())
        {
            return false;
        }

        _clearingSess = false;
        return true;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        Events.UnfastenWatchers();
        List<Exception>? misses = null;
        try
        {
            if (!_clearingSess)
                _ = OpenSessWipe();

            try
            {
                WipeObjects();
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }

            foreach (SimActorRecord canon
                in Entities.TeardownRecords.ToArray())
            {
                Exception? miss = RetireCanonSole(canon);
                if (miss is not null)
                    (misses ??= []).Add(miss);
            }

            if (!FinishSessWipeIfConverged())
            {
                (misses ??= []).Add(new InvalidOperationException(
                    "Runtime entity/object disposal did not converge every canonical owner."));
            }
        }
        finally
        {
            _destroyed = true;
            _gen = null;
            _pvpSynchronize.Dispose();
            Events.Dispose();
            Physics.Dispose();
        }

        if (misses is not null)
        {
            throw new AggregateException(
                "Runtime entity/object disposal failed to converge.",
                misses);
        }
    }

    private void Live() =>
        ObjectDisposedException.ThrowIf(_destroyed, this);
}
