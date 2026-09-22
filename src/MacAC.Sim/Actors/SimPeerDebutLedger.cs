using MacAC.Assets;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal enum SimPeerDebutStatus : byte
{
    Completed,

    AwaitingCollisionSource,

    AwaitingPlacement,

    AwaitingReceiptAcknowledgement,

    AwaitingContinuationPlacement,

    Contention,

    RejectedToken,

    RejectedAuthority,
}

internal readonly record struct SimPeerDebutHoldingCapture(int ActiveCount)
{
    internal bool IsConverged => ActiveCount is 0;
}

internal sealed class SimPeerDebutLedger
{
    private enum SimStage : byte
    {
        // No progress yet, or the mover has not been prepared
        AwaitingMoverPreparation,

        MoverPrepared,

        BodyConstructed,

        PlacementSubmitted,

        // The Place projection token is known but not yet acknowledged
        PlacementCommitted,

        Acknowledged,
    }

    private sealed class Progress(ulong tenancyIdent)
    {
        internal ulong LeaseId { get; } = tenancyIdent;
        internal SimStage Stage { get; set; } = SimStage.AwaitingMoverPreparation;
        internal SimSetPositionDirective ReadiedDirective { get; set; }
        internal KineticBody? ConstructedCorpus { get; set; }
        internal SimPeerHullAssemblyStub Construction { get; set; }
        internal SimPlacementMirrorTicket Projection { get; set; }
    }

    private readonly ref struct Attempt(SimActorRecord capture, in SimSpawnTenancyTicket ticket, IBakedContactSource link, double playMoment, in SimSpawnExecutionInputs feeds)
    {
        public readonly SimActorRecord Record = capture;
        public readonly SimSpawnTenancyTicket Token = ticket;
        public readonly IBakedContactSource Contact = link;
        public readonly double GameTime = playMoment;
        public readonly SimSpawnExecutionInputs Feeds = feeds;
    }

    private readonly SimSpawnTenancyLedger _tenancies;
    private readonly SimSpawnFollowupRunner _followup;
    private readonly SimKineticsLedger _physics;
    private readonly Dictionary<SimActorKey, Progress> _progress = [];
    private readonly HashSet<SimActorKey> _occupied = [];

    internal SimPeerDebutLedger(SimSpawnTenancyLedger residences, SimSpawnFollowupRunner executor, SimKineticsLedger physics)
    {
        _tenancies = residences ?? throw new ArgumentNullException(nameof(residences));
        _followup = executor ?? throw new ArgumentNullException(nameof(executor));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    }

    internal bool TryFetchConstruction(SimActorKey tag, out SimPeerHullAssemblyStub construction)
    {
        if (_progress.TryGetValue(tag, out Progress? headway) && headway.ConstructedCorpus is not null)
        {
            construction = headway.Construction;
            return true;
        }
        construction = default;
        return false;
    }

    internal SimPeerDebutStatus Advance(
        SimActorRecord capture,
        in SimSpawnTenancyTicket residenceTicket,
        IBakedContactSource impactSrc,
        double playMoment,
        in SimSpawnExecutionInputs feeds,
        out SimSpawnExecutionStub receipt,
        out SimPeerHullAssemblyStub construction)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(impactSrc);
        receipt = default;
        construction = default;
        if (!residenceTicket.IsValid || capture.Key is not { } tag)
            return SimPeerDebutStatus.RejectedToken;

        if (!_occupied.Add(tag))
            return SimPeerDebutStatus.Contention;
        try
        {
            var attempt = new Attempt(capture, residenceTicket, impactSrc, playMoment, feeds);
            return Traverse(tag, in attempt, out receipt, out construction);
        }
        finally
        {
            _occupied.Remove(tag);
        }
    }

    internal void Drop(SimActorKey tag) => Toss(tag);

    internal void TossAll() => _progress.Clear();

    internal SimPeerDebutHoldingCapture GrabOwnership() => new(_progress.Count);

    private SimPeerDebutStatus Traverse(SimActorKey tag, in Attempt attempt, out SimSpawnExecutionStub receipt, out SimPeerHullAssemblyStub construction)
    {
        receipt = default;
        construction = default;

        _progress.TryGetValue(tag, out Progress? headway);
        if (headway is not null && headway.LeaseId != attempt.Token.LeaseId)
        {
            Toss(tag);
            headway = null;
        }

        if (headway is null || headway.Stage is SimStage.AwaitingMoverPreparation)
        {
            if (ReadyCarrier(tag, in attempt, ref headway) is { } halt)
                return halt;
            if (headway!.Stage is SimStage.Acknowledged)
                return Perform(tag, in attempt, headway, out receipt, out construction);
        }

        if (headway.Stage is SimStage.MoverPrepared && AssembleHull(tag, in attempt, headway) is { } stop2)
            return stop2;
        if (headway.Stage is SimStage.BodyConstructed && SubmitStance(tag, in attempt, headway) is { } stop3)
            return stop3;
        if (headway.Stage is SimStage.PlacementSubmitted && AwaitProj(tag, in attempt, headway) is { } stop4)
            return stop4;
        if (headway.Stage is SimStage.PlacementCommitted && AwaitAck(tag, in attempt, headway) is { } stop5)
            return stop5;

        return Perform(tag, in attempt, headway, out receipt, out construction);
    }

    private bool TenancyHolds(in Attempt attempt, out SimSpawnTenancyLease tenancy)
    {
        return _tenancies.TryFetchLatest(attempt.Record, out tenancy) && tenancy.Token == attempt.Token;
    }

    // The lease must still hold for this stage; otherwise the run is dropped
    private SimPeerDebutStatus? DemandTenancy(SimActorKey tag, in Attempt attempt, out SimSpawnTenancyLease tenancy)
    {
        if (TenancyHolds(in attempt, out tenancy))
            return null;
        Toss(tag);
        return SimPeerDebutStatus.RejectedAuthority;
    }

    private SimPeerDebutStatus? ReadyCarrier(SimActorKey tag, in Attempt attempt, ref Progress? headway)
    {
        if (!TenancyHolds(in attempt, out SimSpawnTenancyLease tenancy))
        {
            if (headway is null)
                return SimPeerDebutStatus.RejectedToken;
            Toss(tag);
            return SimPeerDebutStatus.RejectedAuthority;
        }

        if (tenancy.Route.OperationKind is not (SimSetPositionOperationKind.RemoteAuthoritative or SimSetPositionOperationKind.ProjectileAuthoritative))
            return SimPeerDebutStatus.RejectedToken;

        if (!tenancy.Route.PerformsSetLocus)
        {
            headway ??= new Progress(attempt.Token.LeaseId);
            headway.Stage = SimStage.Acknowledged;
            _progress[tag] = headway;
            return null;
        }

        var lined = _physics.SetPosition.TryReadyAuthoredCarrier(
            attempt.Record,
            tenancy.Placement,
            tenancy.Route.OperationKind,
            tenancy.Route.SetPositionFlags,
            attempt.Contact,
            attempt.GameTime,
            out SimSetPositionDirective directive,
            locateRealmShiftFromCoreCycle: true);
        if (lined.IsRetryable())
            return SimPeerDebutStatus.AwaitingCollisionSource;
        if (lined != SimSetPositionMoverStagingStatus.Prepared)
        {
            if (headway is not null)
                Toss(tag);
            return SimPeerDebutStatus.RejectedAuthority;
        }

        headway ??= new Progress(attempt.Token.LeaseId);
        headway.ReadiedDirective = directive;
        headway.Stage = SimStage.MoverPrepared;
        _progress[tag] = headway;
        return null;
    }

    private SimPeerDebutStatus? AssembleHull(SimActorKey tag, in Attempt attempt, Progress headway)
    {
        if (DemandTenancy(tag, in attempt, out SimSpawnTenancyLease tenancy) is { } halt)
            return halt;

        var capture = attempt.Record;
        if (capture.KineticBody is { } extant)
        {
            // Only a body this run built counts; anything else means someone else got there first.
            if (!ReferenceEquals(headway.ConstructedCorpus, extant))
            {
                Toss(tag);
                return SimPeerDebutStatus.RejectedAuthority;
            }
            headway.Stage = SimStage.BodyConstructed;
            return null;
        }

        if (capture.KineticsCorpusAcquisitionInHeadway || capture.DistantLocomotionMappingInHeadway)
            return SimPeerDebutStatus.Contention;

        SimPeerHullAssemblyStub built = default;
        KineticBody hull = _physics.GetOrCreatePhysicsBody(
            capture,
            r => SimPeerHullBlueprint.Construct(r, tenancy.InitialCreate.Physics, headway.ReadiedDirective, out built));
        headway.ConstructedCorpus = hull;
        headway.Construction = built;
        headway.Stage = SimStage.BodyConstructed;
        return null;
    }

    private SimPeerDebutStatus? SubmitStance(SimActorKey tag, in Attempt attempt, Progress headway)
    {
        if (DemandTenancy(tag, in attempt, out SimSpawnTenancyLease tenancy) is { } halt)
            return halt;

        var verdict = _physics.SetPosition.SubmitReadiedStance(tenancy.Placement, headway.ReadiedDirective);
        switch (verdict.Status)
        {
            case SimSetPositionStatus.CommittedHostAcknowledgementPending:
                headway.Projection = verdict.Projection;
                headway.Stage = SimStage.PlacementCommitted;
                return null;
            case SimSetPositionStatus.DeferredCell:
                headway.Stage = SimStage.PlacementSubmitted;
                return null;
            default:
                Toss(tag);
                return SimPeerDebutStatus.RejectedAuthority;
        }
    }

    // A deferred placement surfaces on the projection FIFO once its cell is ready; wait for our Place
    // token to reach the head
    private SimPeerDebutStatus? AwaitProj(SimActorKey tag, in Attempt attempt, Progress headway)
    {
        if (DemandTenancy(tag, in attempt, out _) is { } halt)
            return halt;

        while (true)
        {
            if (!_physics.SetPosition.TryGlimpseProj(out SimPlacementMirrorCapture front))
                return SimPeerDebutStatus.AwaitingPlacement;
            if (front.Token.Entity != tag)
            {
                return SimPeerDebutStatus.AwaitingReceiptAcknowledgement;
            }
            if (front.Kind is SimPlacementMirrorKind.Withdraw)
            {
                if (!_physics.SetPosition.AcknowledgeProj(front.Token))
                {
                    Toss(tag);
                    return SimPeerDebutStatus.RejectedAuthority;
                }
                continue;
            }
            if (front.Kind is SimPlacementMirrorKind.Place)
            {
                headway.Projection = front.Token;
                headway.Stage = SimStage.PlacementCommitted;
                return null;
            }
            Toss(tag);
            return SimPeerDebutStatus.RejectedAuthority;
        }
    }

    private SimPeerDebutStatus? AwaitAck(SimActorKey tag, in Attempt attempt, Progress headway)
    {
        if (!_physics.SetPosition.AcknowledgeProj(headway.Projection))
        {
            if (!SimDebutAck.IsStillQueued(_tenancies, _physics.SetPosition, attempt.Record, attempt.Token, headway.Projection))
            {
                Toss(tag);
                return SimPeerDebutStatus.RejectedAuthority;
            }
            return SimPeerDebutStatus.AwaitingReceiptAcknowledgement;
        }
        headway.Stage = SimStage.Acknowledged;
        return null;
    }

    private SimPeerDebutStatus Perform(SimActorKey tag, in Attempt attempt, Progress headway, out SimSpawnExecutionStub receipt, out SimPeerHullAssemblyStub construction)
    {
        construction = default;
        switch (_followup.Perform(attempt.Record, attempt.Token, attempt.Feeds, out receipt))
        {
            case SimSpawnExecutionStatus.Completed:
                construction = headway.Construction;
                _progress.Remove(tag);
                return SimPeerDebutStatus.Completed;
            case SimSpawnExecutionStatus.PendingPlacement:
                return SimPeerDebutStatus.AwaitingReceiptAcknowledgement;
            case SimSpawnExecutionStatus.AwaitingContinuationPlacement:
                return SimPeerDebutStatus.AwaitingContinuationPlacement;
            case SimSpawnExecutionStatus.RejectedToken:
                _progress.Remove(tag);
                return SimPeerDebutStatus.RejectedToken;
            default:
                _progress.Remove(tag);
                return SimPeerDebutStatus.RejectedAuthority;
        }
    }

    private void Toss(SimActorKey tag) => _progress.Remove(tag);
}
