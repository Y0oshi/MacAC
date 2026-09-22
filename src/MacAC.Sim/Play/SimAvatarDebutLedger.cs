using MacAC.Assets;
using MacAC.Sim.Actors;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Play;

internal enum SimAvatarDebutStatus : byte
{
    Completed,

    AwaitingCollisionSource,

    AwaitingActivation,

    AwaitingReceiptAcknowledgement,

    AwaitingContinuationPlacement,

    Contention,

    // The residence/placement token no longer matches anything tracked
    RejectedToken,

    RejectedAuthority,
}

internal readonly record struct SimAvatarDebutHoldingCapture(int ActiveCount)
{
    internal bool IsConverged => ActiveCount is 0;
}

internal sealed class SimAvatarDebutLedger
{
    private enum StageDef : byte
    {
        // No progress yet, or the mover has not been prepared
        AwaitingMoverPreparation,

        MoverPrepared,

        PublicationCommitted,

        ActivationCommitted,

        Acknowledged,
    }

    private sealed class Progress(ulong tenancyIdent)
    {
        internal ulong LeaseId { get; } = tenancyIdent;
        internal StageDef Stage { get; set; } = StageDef.AwaitingMoverPreparation;
        internal SimSetPositionDirective ReadiedDirective { get; set; }
        internal SimAvatarKineticsPublicationTicket BulletinTicket { get; set; }
        internal SimAvatarKineticsArmingTicket ActivationTicket { get; set; }
        internal SimPlacementMirrorTicket Projection { get; set; }
    }

    // Everything one Advance call carries, so the stage methods stay short
    private readonly ref struct Attempt(
        SimActorRecord capture,
        in SimSpawnTenancyTicket ticket,
        AvatarLocomotionAssemblyOptions knobs,
        in SimAvatarKineticsArmingStaging arming,
        IBakedContactSource link,
        double playMoment,
        in SimSpawnExecutionInputs feeds)
    {
        public readonly SimActorRecord Record = capture;
        public readonly SimSpawnTenancyTicket Token = ticket;
        public readonly AvatarLocomotionAssemblyOptions Options = knobs;
        public readonly SimAvatarKineticsArmingStaging Arming = arming;
        public readonly IBakedContactSource Contact = link;
        public readonly double GameTime = playMoment;
        public readonly SimSpawnExecutionInputs Inputs = feeds;
    }

    private readonly SimSpawnTenancyLedger _tenancies;
    private readonly SimSpawnFollowupRunner _followup;
    private readonly SimKineticsLedger _physics;
    private readonly Dictionary<SimActorKey, Progress> _progress = [];
    private readonly HashSet<SimActorKey> _occupied = [];
    private SimAvatarKineticsPublicationLedger? _bulletin;

    internal SimAvatarDebutLedger(SimSpawnTenancyLedger residences, SimSpawnFollowupRunner executor, SimKineticsLedger physics)
    {
        _tenancies = residences ?? throw new ArgumentNullException(nameof(residences));
        _followup = executor ?? throw new ArgumentNullException(nameof(executor));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    }

    internal void AttachBulletin(SimAvatarKineticsPublicationLedger bulletin)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        if (_bulletin is not null)
            throw new InvalidOperationException("The local-player first-entry conductor's publication owner is by now bound");
        _bulletin = bulletin;
    }

    private SimAvatarKineticsPublicationLedger Bulletin
    {
        get
        {
            return _bulletin ?? throw new InvalidOperationException("The local-player first-entry conductor's publication owner isn't yet bound");
        }
    }

    internal SimAvatarDebutStatus Advance(
        SimActorRecord capture,
        in SimSpawnTenancyTicket residenceTicket,
        AvatarLocomotionAssemblyOptions knobs,
        in SimAvatarKineticsArmingStaging activationPrep,
        IBakedContactSource impactSrc,
        double playMoment,
        in SimSpawnExecutionInputs feeds,
        out SimSpawnExecutionStub receipt)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(impactSrc);
        receipt = default;
        if (!residenceTicket.IsValid || capture.Key is not { } tag)
            return SimAvatarDebutStatus.RejectedToken;

        if (!_occupied.Add(tag))
            return SimAvatarDebutStatus.Contention;
        try
        {
            var attempt = new Attempt(capture, residenceTicket, knobs, activationPrep, impactSrc, playMoment, feeds);
            return Traverse(tag, in attempt, out receipt);
        }
        finally
        {
            _occupied.Remove(tag);
        }
    }

    internal void Drop(SimActorKey tag) => Toss(tag);

    internal void TossAll()
    {
        if (_bulletin is not null)
        {
            foreach (Progress headway in _progress.Values)
            {
                Bulletin.Toss(headway.BulletinTicket);
                Bulletin.TossActivation(headway.ActivationTicket);
            }
        }
        _progress.Clear();
    }

    internal SimAvatarDebutHoldingCapture GrabOwnership() => new(_progress.Count);

    private SimAvatarDebutStatus Traverse(SimActorKey tag, in Attempt attempt, out SimSpawnExecutionStub receipt)
    {
        receipt = default;
        _ = Bulletin;

        _progress.TryGetValue(tag, out Progress? headway);
        if (headway is not null && headway.LeaseId != attempt.Token.LeaseId)
        {
            Toss(tag);
            headway = null;
        }

        if (headway is null || headway.Stage is StageDef.AwaitingMoverPreparation)
        {
            if (ReadyCarrier(tag, in attempt, ref headway) is { } halt)
                return halt;
            if (headway!.Stage is StageDef.Acknowledged)
                return Perform(tag, in attempt, out receipt);
        }

        if (headway.Stage is StageDef.MoverPrepared && Publish(tag, in attempt, headway) is { } stop2)
            return stop2;
        if (headway.Stage is StageDef.PublicationCommitted && Arm(tag, headway) is { } stop3)
            return stop3;
        if (headway.Stage is StageDef.ActivationCommitted && AwaitAck(tag, in attempt, headway) is { } stop4)
            return stop4;

        return Perform(tag, in attempt, out receipt);
    }

    // The lease as it stands now, if it is still the one the caller holds
    private bool TenancyHolds(in Attempt attempt, out SimSpawnTenancyLease tenancy)
    {
        return _tenancies.TryFetchLatest(attempt.Record, out tenancy) && tenancy.Token == attempt.Token;
    }

    private SimAvatarDebutStatus? ReadyCarrier(SimActorKey tag, in Attempt attempt, ref Progress? headway)
    {
        if (!TenancyHolds(in attempt, out SimSpawnTenancyLease tenancy))
        {
            if (headway is null)
                return SimAvatarDebutStatus.RejectedToken;
            Toss(tag);
            return SimAvatarDebutStatus.RejectedAuthority;
        }

        if (!tenancy.Route.PerformsSetLocus)
        {
            // Nothing to place: skip straight to the continuation
            headway ??= new Progress(attempt.Token.LeaseId);
            headway.Stage = StageDef.Acknowledged;
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
            out SimSetPositionDirective directive);
        if (lined.IsRetryable())
            return SimAvatarDebutStatus.AwaitingCollisionSource;
        if (lined != SimSetPositionMoverStagingStatus.Prepared)
        {
            if (headway is not null)
                Toss(tag);
            return SimAvatarDebutStatus.RejectedAuthority;
        }

        headway ??= new Progress(attempt.Token.LeaseId);
        headway.ReadiedDirective = directive;
        headway.Stage = StageDef.MoverPrepared;
        _progress[tag] = headway;
        return null;
    }

    private SimAvatarDebutStatus? Publish(SimActorKey tag, in Attempt attempt, Progress headway)
    {
        if (!TenancyHolds(in attempt, out SimSpawnTenancyLease tenancy))
        {
            Toss(tag);
            return SimAvatarDebutStatus.RejectedAuthority;
        }

        var readied = Bulletin.Prepare(
            attempt.Record, tenancy.Placement, headway.ReadiedDirective, attempt.Options, attempt.Arming,
            out SimAvatarKineticsPublicationTicket bulletinTicket);
        if (readied != SimAvatarKineticsPublicationStatus.Prepared)
        {
            Toss(tag);
            return SimAvatarDebutStatus.RejectedAuthority;
        }
        headway.BulletinTicket = bulletinTicket;

        var committed = Bulletin.Seal(bulletinTicket, out SimAvatarKineticsArmingTicket armingTicket);
        if (committed != SimAvatarKineticsPublicationStatus.Committed)
        {
            Toss(tag);
            return committed is SimAvatarKineticsPublicationStatus.RejectedToken
                ? SimAvatarDebutStatus.RejectedToken
                : SimAvatarDebutStatus.RejectedAuthority;
        }

        headway.ActivationTicket = armingTicket;
        headway.Stage = StageDef.PublicationCommitted;
        return null;
    }

    private SimAvatarDebutStatus? Arm(SimActorKey tag, Progress headway)
    {
        var evaluated = Bulletin.EvaluateActivation(headway.ActivationTicket, out SimAvatarKineticsArmingStub stub);
        if (evaluated is SimAvatarKineticsArmingStatus.RejectedToken or SimAvatarKineticsArmingStatus.RejectedAuthority)
        {
            Toss(tag);
            return evaluated is SimAvatarKineticsArmingStatus.RejectedToken
                ? SimAvatarDebutStatus.RejectedToken
                : SimAvatarDebutStatus.RejectedAuthority;
        }
        if (!stub.IsValid)
            return SimAvatarDebutStatus.AwaitingActivation;

        switch (Bulletin.SealActivation(stub, out SimPlacementMirrorTicket proj))
        {
            case SimIdleSetPositionCommitStatus.Committed:
                headway.Projection = proj;
                headway.Stage = StageDef.ActivationCommitted;
                return null;
            case SimIdleSetPositionCommitStatus.DeferredCell:
            case SimIdleSetPositionCommitStatus.RejectedPlacement:
                return SimAvatarDebutStatus.AwaitingActivation;
            default:
                Toss(tag);
                return SimAvatarDebutStatus.RejectedAuthority;
        }
    }

    private SimAvatarDebutStatus? AwaitAck(SimActorKey tag, in Attempt attempt, Progress headway)
    {
        if (!_physics.SetPosition.AcknowledgeProj(headway.Projection))
        {
            if (!SimDebutAck.IsStillQueued(_tenancies, _physics.SetPosition, attempt.Record, attempt.Token, headway.Projection))
            {
                Toss(tag);
                return SimAvatarDebutStatus.RejectedAuthority;
            }
            return SimAvatarDebutStatus.AwaitingReceiptAcknowledgement;
        }
        headway.Stage = StageDef.Acknowledged;
        return null;
    }

    private SimAvatarDebutStatus Perform(SimActorKey tag, in Attempt attempt, out SimSpawnExecutionStub receipt)
    {
        switch (_followup.Perform(attempt.Record, attempt.Token, attempt.Inputs, out receipt))
        {
            case SimSpawnExecutionStatus.Completed:
                _progress.Remove(tag);
                return SimAvatarDebutStatus.Completed;
            case SimSpawnExecutionStatus.PendingPlacement:
                return SimAvatarDebutStatus.AwaitingReceiptAcknowledgement;
            case SimSpawnExecutionStatus.AwaitingContinuationPlacement:
                return SimAvatarDebutStatus.AwaitingContinuationPlacement;
            case SimSpawnExecutionStatus.RejectedToken:
                _progress.Remove(tag);
                return SimAvatarDebutStatus.RejectedToken;
            default:
                _progress.Remove(tag);
                return SimAvatarDebutStatus.RejectedAuthority;
        }
    }

    private void Toss(SimActorKey tag)
    {
        if (!_progress.Remove(tag, out Progress? headway) || _bulletin is null)
            return;
        Bulletin.Toss(headway.BulletinTicket);
        Bulletin.TossActivation(headway.ActivationTicket);
    }
}
