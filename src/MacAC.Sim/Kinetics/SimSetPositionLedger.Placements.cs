using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using MacAC.Assets;
using MacAC.Mechanics.Gear;
using MacAC.Sim.Play;
using System.Numerics;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

// Opening, staging, submitting, committing and cancelling placements
internal sealed partial class SimSetPositionLedger
{
    internal SimSetPositionUpshot Apply(
        SimActorRecord capture,
        ulong anticipatedLocusArbiterVer,
        in SimSetPositionDirective directive)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        var ticket = OpenStance(
            capture,
            anticipatedLocusArbiterVer,
            directive.Kind,
            directive.Portal,
            grabCarrierPrepArbiter: false);
        if (!ticket.IsValid)

            return Rejected(directive.Physics);

        return SubmitStance(
            ticket,
            directive,
            allowStraightUnsealed: true);
    }

    internal SimActorPlacementTicket CommenceApprovedStance(
        SimActorRecord capture,
        ulong anticipatedLocusArbiterVer,
        SimSetPositionOperationKind sort,
        SimPortalPlacementAuthority gateway = default)
    {
        return OpenStance(
            capture,
            anticipatedLocusArbiterVer,
            sort,
            gateway,
            grabCarrierPrepArbiter: false);
    }

    internal SimActorPlacementTicket CommenceAuthoredStance(
        SimActorRecord capture,
        ulong anticipatedLocusArbiterVer,
        SimSetPositionOperationKind sort,
        SimPortalPlacementAuthority gateway = default)
    {
        return OpenStance(
            capture,
            anticipatedLocusArbiterVer,
            sort,
            gateway,
            grabCarrierPrepArbiter: true);
    }

    internal bool IsStanceLatest(
        in SimActorPlacementTicket ticket)
    {
        Live();
        return ticket.IsValid
            && _ops.TryGetValue(ticket.Entity, out SimOperation? op)
            && op.Token == ticket
            && OpHolds(op);
    }

    internal bool MonitorStanceWrapUp(
        in SimActorPlacementTicket ticket)
    {
        Live();
        return IsStanceLatest(ticket)
            && _wrapUpWatches.Add(ticket);
    }

    internal bool IsStanceWrapUpFollowed(
        in SimActorPlacementTicket ticket)
    {
        Live();
        return ticket.IsValid
            && (IsStanceLatest(ticket)
                && _wrapUpWatches.Contains(ticket)
                || _ackedCompletions.ContainsKey(ticket));
    }

    internal bool TryGlimpseAcknowledgedStance(
        in SimActorPlacementTicket ticket,
        out SimPlacementMirrorTicket proj)
    {
        Live();
        if (ticket.IsValid
            && _ackedCompletions.TryGetValue(
                ticket,
                out proj))

            return true;
        proj = default;
        return false;
    }

    internal bool AbsorbAcknowledgedStance(
        in SimActorPlacementTicket ticket,
        in SimPlacementMirrorTicket anticipated)
    {
        Live();
        return ticket.IsValid
            && _ackedCompletions.TryGetValue(
                ticket,
                out SimPlacementMirrorTicket latest)
            && latest == anticipated
            && _ackedCompletions.Remove(ticket);
    }

    internal void DropStanceWrapUp(
        in SimActorPlacementTicket ticket)
    {
        Live();
        DiscardWrapUp(ticket);
    }

    internal SimPlacementAbortStub DropPreciseStance(
        in SimActorPlacementTicket ticket,
        bool revertCancelledPark = false)
    {
        Live();
        DiscardWrapUp(ticket);
        if (!ticket.IsValid
            || !_ops.TryGetValue(ticket.Entity, out SimOperation? op)
            || op.Token != ticket)

            return default;
        return AbortOp(
            ticket.Entity,
            ticket,
            revertCancelledPark: revertCancelledPark);
    }

    internal SimActorPlacementTicket TryCommenceExclusiveStance(
        SimActorRecord capture,
        ulong anticipatedLocusArbiterVer,
        SimSetPositionOperationKind sort)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag
            || _ops.ContainsKey(tag)
            || WrapUpKept(tag))

            return default;
        return OpenStance(capture, anticipatedLocusArbiterVer,
            sort, default, grabCarrierPrepArbiter: false);
    }

    internal SimActorPlacementTicket TryCommenceExclusiveAuthoredStance(
        SimActorRecord capture,
        ulong anticipatedLocusArbiterVer,
        SimSetPositionOperationKind sort,
        SimPortalPlacementAuthority gateway = default)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag
            || _ops.ContainsKey(tag)
            || WrapUpKept(tag))

            return default;
        return OpenStance(
            capture,
            anticipatedLocusArbiterVer,
            sort,
            gateway,
            grabCarrierPrepArbiter: true);
    }

    internal SimSetPositionMoverStagingStatus ReadyCarrier(
        in SimActorPlacementTicket ticket,
        in SimSetPositionMoverStaging prep,
        out SimSetPositionDirective directive)
    {
        Live();
        directive = default;
        if (!ticket.IsValid
            || !_ops.TryGetValue(ticket.Entity, out SimOperation? op)
            || op.Token != ticket
            || op.Stage is not (
                SimActorPlacementStage.AwaitingPreparation
                or SimActorPlacementStage.AwaitingCell)
            || op.Stage is SimActorPlacementStage.AwaitingCell
                && (!op.DormantLocalActivation
                    || !op.WakeableLostCell)
            || !OpHolds(op)
            || !_loadingAuthorities.TryGetValue(
                ticket.Entity,
                out StagingAuthority arbiter)
            || arbiter.OperationId != ticket.OperationId
            || !LoadingArbiterHolds(op, arbiter))

            return SimSetPositionMoverStagingStatus.RejectedAuthority;

        if (!prep.Setup.IsResolved)
        {
            return ParkFor(
                op,
                SimSetPositionMoverStagingStatus
                    .RetrySetupUnavailable);
        }

        var netPrep = prep;
        if (prep.ResolveWorldOffsetFromRuntimeFrame)
        {
            if (!_register.TryFetchRealmCycleShift(
                    arbiter.AcceptedPosition.LandblockId,
                    out float realmShiftX,
                    out float realmShiftY))
            {
                _register.HurlIfRealmCycleUnreachable(
                    arbiter.AcceptedPosition.LandblockId);
                return ParkFor(
                    op,
                    SimSetPositionMoverStagingStatus
                        .RetryWorldFrameUnavailable);
            }

            netPrep = prep with
            {
                ShadowWorldOffsetX = realmShiftX,
                ShadowWorldOffsetY = realmShiftY,
            };
        }

        if (!SimSetPositionMoverStager.TryAssemble(
                op.Record,
                arbiter.AcceptedPosition,
                arbiter.SetupTableId,
                op.Kind,
                op.Portal,
                arbiter.VelocityAuthorityVersion,
                netPrep,
                out directive)
            || !WellFormed(directive.Physics))
        {
            directive = default;
            return SimSetPositionMoverStagingStatus.InvalidData;
        }

        _loadingAuthorities[ticket.Entity] = arbiter with
        {
            Prepared = true,
            PreparedCommand = directive,
        };

        op.ParkReason = SimSetPositionParkReason.None;
        return SimSetPositionMoverStagingStatus.Prepared;
    }

    internal SimSetPositionMoverStagingStatus TryReadyAndSubmitAuthoredStance(
        SimActorRecord capture,
        in SimActorPlacementTicket ticket,
        SimSetPositionOperationKind opSort,
        KineticSetPositionFlags flagSet,
        IBakedContactSource impactSrc,
        double playMoment,
        out SimSetPositionUpshot verdict,
        KineticPlacementClass stanceClass = KineticPlacementClass.Ordinary,
        SimPortalPlacementAuthority gateway = default,
        Vector3 stroke = default,
        float scatterRadiusX = 0f,
        float scatterRadiusY = 0f,
        uint scatterAttempts = 0u,
        float shadeRealmShiftX = 0f,
        float shadeRealmShiftY = 0f,
        bool locateRealmShiftFromCoreCycle = false)
    {
        verdict = default;

        var condition =
            TryReadyAuthoredCarrier(
                capture,
                ticket,
                opSort,
                flagSet,
                impactSrc,
                playMoment,
                out SimSetPositionDirective directive,
                stanceClass,
                gateway,
                stroke,
                scatterRadiusX,
                scatterRadiusY,
                scatterAttempts,
                shadeRealmShiftX,
                shadeRealmShiftY,
                locateRealmShiftFromCoreCycle);
        if (condition != SimSetPositionMoverStagingStatus.Prepared)
            return condition;

        verdict = SubmitReadiedStance(ticket, directive);
        return SimSetPositionMoverStagingStatus.Prepared;
    }

    internal SimSetPositionMoverStagingStatus TryReadyAuthoredCarrier(
        SimActorRecord capture,
        in SimActorPlacementTicket ticket,
        SimSetPositionOperationKind opSort,
        KineticSetPositionFlags flagSet,
        IBakedContactSource impactSrc,
        double playMoment,
        out SimSetPositionDirective directive,
        KineticPlacementClass stanceClass = KineticPlacementClass.Ordinary,
        SimPortalPlacementAuthority gateway = default,
        Vector3 stroke = default,
        float scatterRadiusX = 0f,
        float scatterRadiusY = 0f,
        uint scatterAttempts = 0u,
        float shadeRealmShiftX = 0f,
        float shadeRealmShiftY = 0f,
        bool locateRealmShiftFromCoreCycle = false)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(impactSrc);
        directive = default;

        uint rigChartIdent = PrepareChartOf(capture);
        SimSetPositionMoverSetup rig;
        if (rigChartIdent is 0u)
        {
            rig = SimSetPositionMoverSetup.SettledAbsent;
        }
        else
        {
            var scan =
                impactSrc.ReadSetupCollision(rigChartIdent);
            if (scan.Status != BakedAssetReadStatus.Loaded
                || scan.Data is null)
            {
                return SimSetPositionMoverStagingStatus
                    .RetrySetupUnavailable;
            }
            rig = SimSetPositionMoverSetup.Settled(
                rigChartIdent, scan.Data);
        }

        var prep = new SimSetPositionMoverStaging(
            rig,
            opSort,
            playMoment,
            stanceClass,
            flagSet,
            stroke,
            scatterRadiusX,
            scatterRadiusY,
            scatterAttempts,
            shadeRealmShiftX,
            shadeRealmShiftY,
            gateway,
            locateRealmShiftFromCoreCycle);
        return ReadyCarrier(ticket, prep, out directive);
    }

    internal bool IsPreciseReadiedStanceLatest(
        SimActorRecord capture,
        in SimActorPlacementTicket ticket,
        in SimSetPositionDirective directive)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        return ticket.IsValid
            && ticket.Entity == capture.Key
            && _ops.TryGetValue(ticket.Entity, out SimOperation? op)
            && ReferenceEquals(op.Record, capture)
            && op.Token == ticket
            && op.Stage
                is SimActorPlacementStage.AwaitingPreparation
            && OpHolds(op)
            && _loadingAuthorities.TryGetValue(
                ticket.Entity,
                out StagingAuthority arbiter)
            && arbiter.OperationId == ticket.OperationId
            && arbiter.Prepared
            && arbiter.PreparedCommand == directive
            && LoadingArbiterHolds(op, arbiter);
    }

    internal SimSetPositionUpshot SubmitReadiedStance(
        in SimActorPlacementTicket ticket,
        in SimSetPositionDirective directive)
    {
        return SubmitStance(
            ticket,
            directive,
            allowStraightUnsealed: ticket.PreparationKind
                is SimActorPlacementStagingKind.LegacyDirect);
    }

    internal bool Cancel(SimActorRecord capture, bool broadcastWithdrawal)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag)
            return false;

        bool removed = AbortShelvedOp(
            tag,
            abortLostClan: false,
            preserveLostClan: false,
            out SimPlacementMirrorCapture? toss);
        if (!broadcastWithdrawal || !_actors.IsCurrent(capture))
        {
            if (toss is { } cancelled)
                BroadcastStance(cancelled);
            return removed;
        }

        ExitRealmStance(capture);
        if (capture.KineticBody is null)
        {
            if (toss is { } cancelledBodyless)
                BroadcastStance(cancelledBodyless);
            return removed;
        }
        var op = NewWithdrawalOp(capture, tag);
        _ops[tag] = op;
        var grabbedTicket = op.Token;
        if (toss is { } cancelledFormer)
            BroadcastStance(cancelledFormer);
        if (!OpHoldsForTicket(tag, grabbedTicket, out op))
            return true;
        _ = BroadcastProj(
            op,
            SimPlacementMirrorKind.Withdraw,
            op.Result);
        return true;
    }

    internal SimPlacementAbortStub Drop(
        SimActorRecord capture,
        bool freeReadiedCarrier = false,
        bool revertCancelledPark = false)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        SimPlacementAbortStub receipt = default;
        if (capture.Key is { } tag)
        {
            ParkedWithdrawal withdrawal =
                revertCancelledPark
                && _ops.TryGetValue(tag, out SimOperation? shelved)
                && shelved.WakeableLostCell
                && ReferenceEquals(shelved.Record, capture)
                    ? shelved.ParkedWithdrawal
                    : default;
            receipt = AbortOp(tag);
            if (freeReadiedCarrier)
                _linedCarriers.Remove(tag);
            if (withdrawal.Captured)
                ReinstateFollowingParkWithdrawal(capture, withdrawal);
        }
        return receipt;
    }

    internal void ExitRealm(SimActorRecord capture)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        SimPlacementAbortStub receipt = capture.Key is { } tag
            ? AbortOp(tag)
            : default;
        if (_actors.IsCurrent(capture))
            ExitRealmStance(capture);
        BroadcastAbort(receipt);
    }

    internal bool IsPostponed(SimActorRecord capture)
    {
        return capture.Key is { } tag
        && _ops.TryGetValue(tag, out SimOperation? op)
        && ReferenceEquals(op.Record, capture)
        && op.WakeableLostCell;
    }

    internal bool TryFetchReadiedCarrierOrbTally(
        SimActorRecord capture,
        out int orbTally)
    {
        Live();
        if (capture.Key is { } tag
            && _linedCarriers.TryGetValue(
                tag,
                out KineticSetPositionRequest req))
        {
            orbTally = req.Spheres.Length;
            return true;
        }
        orbTally = 0;
        return false;
    }

    internal bool TryFetchExpectingPrepTicket(
        SimActorRecord capture,
        out SimActorPlacementTicket ticket)
    {
        Live();
        if (capture.Key is { } tag
            && _ops.TryGetValue(tag, out SimOperation? op)
            && ReferenceEquals(op.Record, capture)
            && op.RequiresPreparation)
        {
            ticket = op.Token;
            return true;
        }
        ticket = default;
        return false;
    }

    internal void BroadcastAbort(
        in SimPlacementAbortStub receipt)
    {
        if (!receipt.IsValid)
            return;
        var proj = receipt.Projection;
        if (_projFifo.TryGetValue(
                proj.Token.Sequence,
                out SimPlacementMirrorCapture latest)
            && latest == proj)

            BroadcastStance(proj);
    }

    private SimActorPlacementTicket OpenStance(
        SimActorRecord capture,
        ulong anticipatedLocusArbiterVer,
        SimSetPositionOperationKind sort,
        SimPortalPlacementAuthority gateway,
        bool grabCarrierPrepArbiter)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ObjectCreation.RemotePosition? approvedLocus =
            capture.Snapshot.Physics?.Position ?? capture.Snapshot.Position;
        if (capture.Key is not { } tag
            || !_actors.IsCurrent(capture)
            || WrapUpKept(tag)
            || capture.PositionAuthorityVersion
                != anticipatedLocusArbiterVer
            || (grabCarrierPrepArbiter
                && approvedLocus is null)
            || !(gateway.IsVacant
                || (gateway.IsValid
                    && sort is SimSetPositionOperationKind
                        .LocalAuthoritative
                    && approvedLocus is { } gatewayLocus
                    && gateway.Projection.DestinationCell
                        == gatewayLocus.LandblockId)))

            return default;

        SimActorPlacementTicket ticket = new SimActorPlacementTicket(
            _actors.SessionLifetimeVersion,
            tag,
            anticipatedLocusArbiterVer,
            checked(++_opIdents),
            grabCarrierPrepArbiter
                ? SimActorPlacementStagingKind.AuthoredMover
                : SimActorPlacementStagingKind.LegacyDirect);
        List<SimActorKey>? inheritedLostClan = null;
        SimPlacementMirrorCapture? inheritedWithdrawal = null;
        bool inheritedWithdrawalAcknowledged = false;
        PlaceOutcome inheritedOutcome = default;
        uint inheritedPreciseChamberIdent = 0u;
        ulong inheritedImpactGen = 0UL;
        if (_ops.TryGetValue(tag, out SimOperation? displaced)
            && (displaced.WakeableLostCell
                || displaced.InheritedLostDeadline))
        {
            inheritedLostClan = displaced.LostFamilyKeys;
            displaced.LostFamilyKeys = null;
            inheritedWithdrawalAcknowledged =
                displaced.WithdrawalAcknowledged;
            inheritedOutcome = displaced.Result;
            inheritedPreciseChamberIdent = displaced.ExactCellId;
            inheritedImpactGen = displaced.CollisionGeneration;
            if (displaced.ProjectionSequence is not 0UL
                && _projFifo.TryGetValue(
                    displaced.ProjectionSequence,
                    out SimPlacementMirrorCapture queuedWithdrawal)
                && queuedWithdrawal.Kind
                    is SimPlacementMirrorKind.Withdraw)
            {
                inheritedWithdrawal = queuedWithdrawal;
                displaced.ProjectionSequence = 0UL;
            }
        }
        _ = AbortShelvedOp(
            tag,
            abortLostClan: false,
            preserveLostClan: inheritedLostClan is not null,
            out SimPlacementMirrorCapture? toss);

        SimOperation substitute = RentOp();
        substitute.Record = capture;
        substitute.Token = ticket;
        substitute.Key = tag;
        substitute.PositionAuthorityVersion = anticipatedLocusArbiterVer;
        substitute.SessionLifetimeVersion = _actors.SessionLifetimeVersion;
        substitute.SourceSpatialAuthorityVersion =
            capture.SpatialAuthorityVersion;
        substitute.SourceVelocityAuthorityVersion =
            capture.VelArbiterVer;
        substitute.PreviousContact = capture.KineticBody?.InContact ?? false;
        substitute.PreviousOnWalkable =
            capture.KineticBody?.OnWalkable ?? false;
        substitute.Command = default;
        substitute.Result = default;
        substitute.SpatialAuthorityVersion = capture.SpatialAuthorityVersion;
        substitute.PlacementCommitVersion = capture.PlacementCommitVersion;
        substitute.Stage = SimActorPlacementStage.AwaitingPreparation;
        substitute.Kind = sort;
        substitute.Portal = gateway;
        substitute.LostFamilyKeys = inheritedLostClan;
        substitute.InheritedLostDeadline = inheritedLostClan is not null;
        substitute.WithdrawalAcknowledged = inheritedWithdrawalAcknowledged;
        if (inheritedWithdrawal is { } keptWithdrawal)
        {
            OpenStanceBranch(substitute, keptWithdrawal, inheritedOutcome, inheritedPreciseChamberIdent, inheritedImpactGen);
        }
        _ops[tag] = substitute;
        if (grabCarrierPrepArbiter)
        {
            _loadingAuthorities[tag] = LoadingArbiterOf(
                substitute,
                approvedLocus!.Value,
                readied: false);
        }
        if (toss is { } cancelled)
            BroadcastStance(cancelled);
        return _ops.TryGetValue(tag, out SimOperation? latestOp)
            && latestOp.Token == ticket
            ? ticket
            : default;
    }

    private void OpenStanceBranch(SimOperation substitute, SimPlacementMirrorCapture keptWithdrawal, PlaceOutcome inheritedOutcome, uint inheritedPreciseChamberIdent, ulong inheritedImpactGen)
    {
        substitute.ProjectionSequence =
                    keptWithdrawal.Token.Sequence;
        substitute.Result = inheritedOutcome;
        substitute.ExactCellId = inheritedPreciseChamberIdent;
        substitute.CollisionGeneration = inheritedImpactGen;
    }

    private bool WrapUpKept(SimActorKey tag)
    {
        foreach (SimActorPlacementTicket ticket
            in _ackedCompletions.Keys)
        {
            if (ticket.Entity == tag)
                return true;
        }
        return false;
    }

    private static SimSetPositionMoverStagingStatus ParkFor(
        SimOperation op,
        SimSetPositionMoverStagingStatus condition)
    {
        op.ParkReason = condition.ParkReason();
        return condition;
    }

    private SimSetPositionUpshot SubmitStance(
        in SimActorPlacementTicket ticket,
        in SimSetPositionDirective directive,
        bool allowStraightUnsealed)
    {
        Live();
        SimOperation? op = null;
        bool ownsTicket = ticket.IsValid
            && _ops.TryGetValue(ticket.Entity, out op)
            && op.Token == ticket;
        StagingAuthority preciseArbiter = default;
        bool hasPrepArbiter = ticket.IsValid
            && _loadingAuthorities.TryGetValue(
                ticket.Entity,
                out preciseArbiter)
            && preciseArbiter.OperationId == ticket.OperationId;
        if (!ownsTicket
            || op is null
            || op.Stage
                is not SimActorPlacementStage.AwaitingPreparation
            || !OpHolds(op)
            || op.Record.KineticBody is not { } corpus
            || !double.IsFinite(directive.GameTime)
            || directive.Kind != op.Kind
            || directive.Portal != op.Portal
            || (hasPrepArbiter
                ? !preciseArbiter.Prepared
                    || preciseArbiter.PreparedCommand != directive
                    || !LoadingArbiterHolds(
                        op,
                        preciseArbiter)
                : !allowStraightUnsealed)
            || (directive.ExpectedVelocityAuthorityVersion is not 0UL
                && op.Record.VelArbiterVer
                    != directive.ExpectedVelocityAuthorityVersion))

            return Rejected(directive.Physics);

        op.Body = corpus;
        op.EnteringWorldFromCelllessResidence |=
            !corpus.InWorld || op.Record.WholeChamberTag is 0u;
        if (directive.ExpectedVelocityAuthorityVersion is not 0UL)
        {
            op.SourceVelocityAuthorityVersion =
                directive.ExpectedVelocityAuthorityVersion;
        }
        op.PreviousContact = corpus.InContact;
        op.PreviousOnWalkable = corpus.OnWalkable;
        var canonReq = directive.Physics with
        {
            Position = op.WakeableLostCell
                ? op.Result.Position
                : directive.Physics.Position,
            Orientation = op.WakeableLostCell
                ? op.Result.Orientation
                : directive.Physics.Orientation,
            CellId = op.WakeableLostCell
                ? op.ExactCellId
                : directive.Physics.CellId,
            CellLocalPosition = op.WakeableLostCell
                ? op.Result.CellLocalPosition
                : directive.Physics.CellLocalPosition,
            MoverPhysicsState = op.Record.FinalKineticsCondition,
            MovingEntityId = op.Key.LocalEntityId,
            CurrentCellId = op.WakeableLostCell
                ? null
                : corpus.InWorld
                    && op.Record.WholeChamberTag is not 0u
                ? op.Record.WholeChamberTag
                : null,
        };
        SimSetPositionDirective canonDirective = directive with { Physics = canonReq };
        op.Command = canonDirective;
        if (hasPrepArbiter)
        {
            _loadingAuthorities[ticket.Entity] = preciseArbiter with
            {
                PreparedCommand = canonDirective,
            };
        }

        if (op.InheritedLostDeadline
            && !op.WithdrawalAcknowledged
            && op.ProjectionSequence is not 0UL)
        {
            op.PreparedCommandAwaitingWithdrawalAck =
                canonDirective;
            op.Stage = SimActorPlacementStage
                .AwaitingWithdrawalAcknowledgement;
            return Upshot(
                SimSetPositionStatus.DeferredCell,
                op.Result,
                _projFifo.TryGetValue(
                    op.ProjectionSequence,
                    out SimPlacementMirrorCapture queued)
                    ? queued.Token
                    : default);
        }

        if (op.WakeableLostCell)
        {
            op.RequiresPreparation = false;
            op.Stage = SimActorPlacementStage.AwaitingCell;
            if (op.WithdrawalAcknowledged
                && op.CollisionGenerationReady
                && op.ProjectionSequence is 0UL)

                ReattemptShelved(op);
            return Upshot(
                SimSetPositionStatus.DeferredCell,
                op.Result,
                op.ProjectionSequence is not 0UL
                    && _projFifo.TryGetValue(
                        op.ProjectionSequence,
                        out SimPlacementMirrorCapture queued)
                        ? queued.Token
                        : default);
        }

        if (!WellFormed(canonReq))
        {
            var invalid = InvalidPlace(canonReq);
            op.Result = invalid;
            op.RequiresPreparation = true;
            op.Stage = SimActorPlacementStage.AwaitingPreparation;
            return Upshot(SimSetPositionStatus.Rejected, invalid, default);
        }

        if (TryBlockingLull(
                canonReq,
                out Lull? stillness))
        {
            PlaceOutcome postponed = new PlaceOutcome(
                PlaceError.Ok,
                KineticResidenceVerdict.DeferredCell,
                canonReq.Position,
                canonReq.Orientation,
                canonReq.CellId,
                canonReq.CellLocalPosition,
                InContact: op.PreviousContact,
                OnWalkable: op.PreviousOnWalkable,
                ContactPlane: corpus.ContactPlane,
                ContactPlaneCellId: corpus.ContactPlaneCellId,
                ContactPlaneIsWater: corpus.ContactPlaneIsWater,
                SlidingNormalValid: corpus.SlidingNormal != Vector3.Zero,
                SlidingNormal: corpus.SlidingNormal,
                FramesStationaryFall: corpus.FramesStationaryFall,
                CrossCellIds: ImmutableArray<uint>.Empty,
                CollidedObjectIds: ImmutableArray<uint>.Empty,
                QueriedCellIds: ImmutableArray<uint>.Empty);
            op.Result = postponed;
            op.RequiresPreparation = false;
            op.ExactCellId = postponed.CellId;
            _linedCarriers[op.Key] = canonReq;
            return ParkOp(
                op,
                postponed,
                impactGenOverride:
                    stillness!.Token.CollisionGeneration,
                impactStemOverride:
                    stillness.Token.LandblockPrefix,
                restorableOnAbort: true);
        }

        PlaceOutcome outcome;
        _linkTapPile.Push(new ContactHookFrame(
            op.Record,
            op.PositionAuthorityVersion,
            op.SourceSpatialAuthorityVersion,
            op.SourceVelocityAuthorityVersion,
            canonDirective.GameTime,
            op.PreviousContact,
            op.PreviousOnWalkable));
        try
        {
            outcome = _register.Engine.SetPosition(
                canonReq,
                _linkTap);
        }
        finally
        {
            _linkTapPile.Pop();
        }
        if (!OpHoldsForTicket(ticket.Entity, ticket, out op))
            return Upshot(SimSetPositionStatus.Cancelled, outcome, default);
        if (outcome.IsSuccessful
            && TryBlockingLull(
                outcome,
                out Lull? queriedStillness))
        {
            var pinned = outcome with
            {
                Residence = KineticResidenceVerdict.DeferredCell,
            };
            op.Result = pinned;
            op.RequiresPreparation = false;
            op.ExactCellId = pinned.CellId;
            _linedCarriers[op.Key] = canonReq;
            return ParkOp(
                op,
                pinned,
                impactGenOverride:
                    queriedStillness!.Token.CollisionGeneration,
                impactStemOverride:
                    queriedStillness.Token.LandblockPrefix,
                restorableOnAbort: true);
        }
        op.Result = outcome;
        if (!outcome.IsSuccessful)
        {
            op.RequiresPreparation = true;
            op.Stage = SimActorPlacementStage.AwaitingPreparation;
            return Upshot(SimSetPositionStatus.Rejected, outcome, default);
        }

        if (!OpHoldsForTicket(ticket.Entity, ticket, out op))
            return Upshot(SimSetPositionStatus.Cancelled, outcome, default);
        op.RequiresPreparation = false;
        op.ExactCellId = outcome.CellId;
        _linedCarriers[op.Key] = canonReq;

        if (outcome.IsPostponed)
            return ParkOp(op, outcome, restorableOnAbort: true);

        if (!SealStance(op, outcome))
        {
            BroadcastAbort(AbortOp(ticket.Entity, ticket));
            return Upshot(SimSetPositionStatus.Cancelled, outcome, default);
        }

        if (!_ops.TryGetValue(ticket.Entity, out SimOperation? stillOwns)
            || stillOwns.Token != ticket)
        {
            return Upshot(SimSetPositionStatus.Cancelled, outcome, default);
        }

        op.Stage = SimActorPlacementStage
            .AwaitingCommitAcknowledgement;
        var proj = BroadcastProj(
            op,
            SimPlacementMirrorKind.Place,
            outcome);
        return Upshot(
            SimSetPositionStatus.CommittedHostAcknowledgementPending,
            outcome,
            proj);
    }

    private bool SealStance(
        SimOperation op,
        in PlaceOutcome outcome)
    {
        if (!outcome.IsSealed || !OpHolds(op))
            return false;
        var capture = op.Record;
        KineticBody corpus = op.Body!;
        var opTicket = op.Token;
        SimActorKey opTag = op.Key;
        ulong locusArbiterVer = op.PositionAuthorityVersion;
        ulong srcVelArbiterVer =
            op.SourceVelocityAuthorityVersion;
        double directivePlayMoment = op.Command.GameTime;
        bool earlierLink = op.PreviousContact;
        bool earlierOnPassable = op.PreviousOnWalkable;
        float shadeRealmShiftX = op.Command.ShadowWorldOffsetX;
        float shadeRealmShiftY = op.Command.ShadowWorldOffsetY;
        corpus.Orientation = outcome.Orientation;
        corpus.SnapToChamber(
            outcome.CellId,
            outcome.Position,
            outcome.CellLocalPosition);
        bool isStatic = (capture.FinalKineticsCondition & KineticStateFlags.Static) != 0;
        if (op.EnteringWorldFromCelllessResidence)
        {
            corpus.PreviousRefreshMoment = directivePlayMoment;
            _actors.RestartObjectTimerForJoinRealm(capture, isStatic);
        }
        if (op.EnteringWorldFromCelllessResidence && !isStatic)
            corpus.TransientState |= TransientPhaseFlagSet.Active;
        corpus.ContactPlaneValid = outcome.InContact;
        corpus.ContactPlane = outcome.ContactPlane;
        corpus.ContactPlaneCellId = outcome.ContactPlaneCellId;
        corpus.ContactPlaneIsWater = outcome.ContactPlaneIsWater;
        if (outcome.InContact)
            corpus.GroundNormal = outcome.ContactPlane.Normal;
        corpus.SlidingNormal = outcome.SlidingNormal;
        if (outcome.SlidingNormalValid)
            corpus.TransientState |= TransientPhaseFlagSet.Sliding;
        else
            corpus.TransientState &= ~TransientPhaseFlagSet.Sliding;
        var distant =
            capture.PeerMotion as ISimPeerPlacement;
        if (capture.WholeChamberTag != outcome.CellId)
        {
            _actors.AssignWholeChamber(
                capture,
                outcome.CellId,
                (outcome.CellId & 0xFFFF0000u) | 0xFFFFu);
        }
        op.SpatialAuthorityVersion = capture.SpatialAuthorityVersion;
        ulong spatialArbiterVer = capture.SpatialAuthorityVersion;
        _actors.ProgressStanceSeal(capture);
        op.PlacementCommitVersion = capture.PlacementCommitVersion;
        ulong canonSealVer = capture.PlacementCommitVersion;
        if (distant is not null)
        {
            distant.CellId = outcome.CellId;
            distant.LastServerPosition = outcome.Position;
            distant.LastServerPositionTime = _register.UtcInstantSecs;
            distant.LastShadowSyncPosition = outcome.Position;
            distant.LastShadowSyncOrientation = outcome.Orientation;
        }

        uint sealedChamberIdent = outcome.CellId;
        bool collidedWithSurroundings = outcome.CollidedWithEnvironment;
        var collidedObjectIdents =
            outcome.CollidedObjectIds;
        if (!StanceSealHolds(
                locusArbiterVer,
                spatialArbiterVer,
                capture,
                corpus,
                canonSealVer,
                sealedChamberIdent,
                demandSpatialTrunk: false))
            return false;
        bool linkSealed;
        if (distant is null)
        {
            linkSealed = KineticObjUpdate.SealSetLocusLinkChangeover(
                corpus,
                outcome.InContact,
                outcome.OnWalkable,
                earlierOnPassable);
        }
        else
        {
            LinkSealGuard guard = new LinkSealGuard(
                this,
                locusArbiterVer,
                spatialArbiterVer,
                capture,
                corpus,
                canonSealVer,
                sealedChamberIdent);
            linkSealed = KineticObjUpdate.SealSetLocusLinkChangeover(
                corpus,
                outcome.InContact,
                outcome.OnWalkable,
                earlierOnPassable,
                distant.HitGround,
                distant.LeaveGround,
                guard.OpHolds);
        }
        if (!linkSealed
            || !StanceSealHolds(
                locusArbiterVer,
                spatialArbiterVer,
                capture,
                corpus,
                canonSealVer,
                sealedChamberIdent,
                demandSpatialTrunk: false))

            return false;
        bool reportingLatest = !DossiersLinks(capture, corpus)
            || _register.ProcessSetLocusImpactDossiers(
                capture,
                locusArbiterVer,
                spatialArbiterVer,
                directivePlayMoment,
                earlierLink,
                earlierOnPassable,
                collidedWithSurroundings,
                collidedObjectIdents,
                out _);
        if (!reportingLatest
            || !StanceSealHolds(
                locusArbiterVer,
                spatialArbiterVer,
                capture,
                corpus,
                canonSealVer,
                sealedChamberIdent,
                demandSpatialTrunk: false))
            return false;
        corpus.FramesStationaryFall = outcome.FramesStationaryFall;
        if (VelHolds(srcVelArbiterVer, capture))
        {
            KineticObjUpdate.HandleAllCollisions(
                corpus,
                outcome.CollisionNormalValid,
                outcome.CollisionNormal,
                earlierLink,
                earlierOnPassable,
                corpus.OnWalkable);
        }
        corpus.TransientState &= ~(TransientPhaseFlagSet.StationaryFall
            | TransientPhaseFlagSet.StationaryStop
            | TransientPhaseFlagSet.StationaryStuck);
        corpus.TransientState |= outcome.FramesStationaryFall switch
        {
            1 => TransientPhaseFlagSet.StationaryFall,
            2 => TransientPhaseFlagSet.StationaryStop,
            3 => TransientPhaseFlagSet.StationaryStuck,
            _ => TransientPhaseFlagSet.None,
        };
        if (distant is not null)
            distant.Airborne = !corpus.OnWalkable;
        if (!StanceSealHolds(
                locusArbiterVer,
                spatialArbiterVer,
                capture,
                corpus,
                canonSealVer,
                sealedChamberIdent,
                demandSpatialTrunk: false))
            return false;

        _register.Engine.ShadeObjects.SealSetLocus(
            opTag.LocalEntityId,
            outcome.Position,
            outcome.Orientation,
            outcome.CellId,
            shadeRealmShiftX,
            shadeRealmShiftY,
            outcome.ShadowAction,
            outcome.CrossCellIds);
        _register.AcknowledgeSpatialProj(capture, spatial: true);

        if (_ops.TryGetValue(opTag, out SimOperation? latestOp)
            && latestOp.Token == opTicket)
        {
            latestOp.ExactCellId = outcome.CellId;
            latestOp.Result = outcome;
            latestOp.WakeableLostCell = false;
            latestOp.EnteringWorldFromCelllessResidence = false;
            DisarmLostForClan(latestOp);
        }

        return StanceSealHolds(
            locusArbiterVer,
            spatialArbiterVer,
            capture,
            corpus,
            canonSealVer,
            sealedChamberIdent,
            demandSpatialTrunk: true);
    }

    private bool StanceSealHolds(
        ulong locusArbiterVer,
        ulong spatialArbiterVer,
        SimActorRecord capture,
        KineticBody corpus,
        ulong stanceSealVer,
        uint wholeChamberIdent,
        bool demandSpatialTrunk)
    {
        return _actors.IsCurrent(capture)
        && ReferenceEquals(capture.KineticBody, corpus)
        && capture.PositionAuthorityVersion == locusArbiterVer
        && capture.SpatialAuthorityVersion == spatialArbiterVer
        && capture.PlacementCommitVersion == stanceSealVer
        && capture.WholeChamberTag == wholeChamberIdent
        && (!demandSpatialTrunk || _register.IsSpatialTrunk(capture));
    }

    private bool DossiersLinks(
        SimActorRecord capture,
        KineticBody corpus)
    {
        return _actors.IsCurrent(capture)
        && ReferenceEquals(capture.KineticBody, corpus)
        && corpus.InWorld
        && (corpus.State & KineticStateFlags.Hidden) == 0;
    }

    private void WithdrawStance(SimActorRecord capture)
    {
        _register.ImpactDossiers.ExitRealm(capture);
        _register.DropSpatialProj(capture);
        if (capture.Key is { } tag)
            _register.Engine.ShadeObjects.Suspend(tag.LocalEntityId);
        if (capture.WholeChamberTag is not 0u)
            _actors.AssignWholeChamber(capture, 0u, 0u);
    }

    private void ExitRealmStance(SimActorRecord capture)
    {
        WithdrawStance(capture);
        if (capture.KineticBody is not { } corpus)
        {
            _actors.SuspendObjectTimer(capture);
            _actors.ProgressStanceSeal(capture);
            return;
        }
        corpus.SnapToChamber(
            0u,
            corpus.Position,
            corpus.CellPosition.Frame.Origin);
        corpus.InWorld = false;
        corpus.TransientState = TransientPhaseFlagSet.None;
        corpus.ContactPlaneValid = false;
        corpus.ContactPlaneCellId = 0u;
        corpus.ContactPlaneIsWater = false;
        corpus.SlidingNormal = Vector3.Zero;
        corpus.FramesStationaryFall = 0;
        corpus.calc_acceleration();
        if (capture.PeerMotion is ISimPeerPlacement distant)
            distant.CellId = 0u;
        _actors.SuspendObjectTimer(capture);
        _actors.ProgressStanceSeal(capture);
    }

    private SimPlacementMirrorTicket BroadcastProj(
        SimOperation op,
        SimPlacementMirrorKind sort,
        in PlaceOutcome outcome,
        bool broadcastImmediately = true)
    {
        if (op.ProjectionSequence is not 0UL)
            _projFifo.Remove(op.ProjectionSequence);
        ulong series = checked(++_projSeq);
        SimPlacementMirrorTicket ticket = new SimPlacementMirrorTicket(
            series,
            Revision: 1UL,
            op.Key,
            op.PositionAuthorityVersion,
            op.SpatialAuthorityVersion,
            op.PlacementCommitVersion,
            op.SessionLifetimeVersion,
            op.ExactCellId,
            op.CollisionGeneration,
            op.Command.Portal);
        var capture = new SimPlacementMirrorCapture(
            ticket,
            sort,
            outcome.Position,
            outcome.Orientation,
            outcome.CellLocalPosition,
            outcome.InContact,
            outcome.OnWalkable);
        op.ProjectionSequence = series;
        _projFifo.Add(series, capture);
        NoteLullWithdrawal(op, capture);
        NoteLullRevert(op, capture);
        if (broadcastImmediately)
            BroadcastStance(capture);
        return ticket;
    }

    private SimOperation NewWithdrawalOp(
        SimActorRecord capture,
        SimActorKey tag)
    {
        KineticBody corpus = capture.KineticBody
            ?? throw new InvalidOperationException(
                "A withdrawal projection needs the canonical KineticBody");
        var kinetics = new KineticSetPositionRequest(
            corpus.Position,
            corpus.Orientation,
            corpus.CellPosition.ObjCellId,
            corpus.CellPosition.Frame.Origin,
            ImmutableArray<PackedContactSphere>.Empty,
            1f,
            0f,
            0f);
        PlaceOutcome outcome = new PlaceOutcome(
            PlaceError.Ok,
            KineticResidenceVerdict.DeferredCell,
            corpus.Position,
            corpus.Orientation,
            corpus.CellPosition.ObjCellId,
            corpus.CellPosition.Frame.Origin,
            CrossCellIds: ImmutableArray<uint>.Empty,
            CollidedObjectIds: ImmutableArray<uint>.Empty);
        SimOperation op = RentOp();
        op.Record = capture;
        op.Body = corpus;
        op.Token = new SimActorPlacementTicket(
            _actors.SessionLifetimeVersion,
            tag,
            capture.PositionAuthorityVersion,
            checked(++_opIdents),
            SimActorPlacementStagingKind.LegacyDirect);
        op.Key = tag;
        op.PositionAuthorityVersion = capture.PositionAuthorityVersion;
        op.SessionLifetimeVersion = _actors.SessionLifetimeVersion;
        op.SourceSpatialAuthorityVersion = capture.SpatialAuthorityVersion;
        op.SourceVelocityAuthorityVersion = capture.VelArbiterVer;
        op.PreviousContact = corpus.InContact;
        op.PreviousOnWalkable = corpus.OnWalkable;
        op.Command = new SimSetPositionDirective(
            kinetics,
            SimSetPositionOperationKind.RemoteAuthoritative,
            corpus.PreviousRefreshMoment,
            capture.VelArbiterVer,
            ShadowWorldOffsetX: 0f,
            ShadowWorldOffsetY: 0f);
        op.Result = outcome;
        op.SpatialAuthorityVersion = capture.SpatialAuthorityVersion;
        op.PlacementCommitVersion = capture.PlacementCommitVersion;
        op.ExactCellId = outcome.CellId;
        op.WakeableLostCell = false;
        op.Stage = SimActorPlacementStage
            .AwaitingWithdrawalAcknowledgement;
        op.Kind = SimSetPositionOperationKind.RemoteAuthoritative;
        op.Portal = default;
        return op;
    }

    private SimPlacementAbortStub AbortOp(
        SimActorKey tag)
    {
        _ = AbortShelvedOp(
            tag,
            abortLostClan: false,
            preserveLostClan: false,
            out SimPlacementMirrorCapture? toss);
        return toss is { } proj
            ? new SimPlacementAbortStub(proj)
            : default;
    }

    private SimPlacementAbortStub AbortOp(
        SimActorKey tag,
        in SimActorPlacementTicket anticipatedTicket,
        bool preserveLostClan = false,
        bool revertCancelledPark = false)
    {
        if (!_ops.TryGetValue(tag, out SimOperation? latest)
            || latest.Token != anticipatedTicket)

            return default;
        var shelvedCapture = latest.Record;
        ParkedWithdrawal withdrawal =
            revertCancelledPark && latest.WakeableLostCell
                ? latest.ParkedWithdrawal
                : default;
        _ = AbortShelvedOp(
            tag,
            abortLostClan: false,
            preserveLostClan,
            out SimPlacementMirrorCapture? toss);
        if (withdrawal.Captured)
            ReinstateFollowingParkWithdrawal(shelvedCapture, withdrawal);
        return toss is { } proj
            ? new SimPlacementAbortStub(proj)
            : default;
    }

    private bool AbortShelvedOp(
        SimActorKey tag,
        bool abortLostClan,
        bool preserveLostClan,
        out SimPlacementMirrorCapture? toss)
    {
        toss = null;
        if (!preserveLostClan)
            DisarmLost(tag);
        if (!_ops.Remove(tag, out SimOperation? op))
            return false;
        DiscardWrapUp(op.Token);
        _loadingAuthorities.Remove(tag);
        UnfileShelved(op);
        if (!preserveLostClan && abortLostClan)
            DisarmLostForClan(op);
        if (op.ProjectionSequence is not 0UL
            && _projFifo.TryGetValue(
                op.ProjectionSequence,
                out SimPlacementMirrorCapture queued))
        {
            var cancelled = queued with
            {
                Token = queued.Token with
                {
                    Revision = checked(queued.Token.Revision + 1UL),
                },
                Kind = SimPlacementMirrorKind.Discard,
            };
            _projFifo[op.ProjectionSequence] = cancelled;
            if (queued.Kind is SimPlacementMirrorKind.Withdraw)
            {
                foreach (Lull phase
                         in _lulls.Values)
                {
                    if (phase.QueuedWithdrawals.ContainsKey(
                            op.ProjectionSequence))
                    {
                        phase.QueuedWithdrawals[
                            op.ProjectionSequence] = cancelled.Token;
                        for (int keptOrdinal = 0;
                             keptOrdinal < phase.KeptWithdrawals.Count;
                             ++keptOrdinal)
                        {
                            if (phase.KeptWithdrawals[keptOrdinal].Sequence
                                == op.ProjectionSequence)
                            {
                                phase.KeptWithdrawals[keptOrdinal] =
                                    cancelled.Token;
                                break;
                            }
                        }
                        break;
                    }
                }
            }
            op.Stage = SimActorPlacementStage
                .CancelledAwaitingAcknowledgement;
            toss = cancelled;
        }
        YieldOp(op);
        return true;
    }

    private void DiscardWrapUp(
        in SimActorPlacementTicket ticket)
    {
        if (!ticket.IsValid)
            return;
        _wrapUpWatches.Remove(ticket);
        _ackedCompletions.Remove(ticket);
    }

    private void BroadcastStance(
        in SimPlacementMirrorCapture proj) =>
        _signals?.BroadcastStance(proj);

    private void BroadcastRestoredFollowingWithdrawal(SimActorRecord capture)
    {
        if (capture.Key is not { } tag)
            return;
        var corpus = capture.KineticBody;
        ulong series = checked(++_projSeq);
        SimPlacementMirrorTicket ticket = new SimPlacementMirrorTicket(
            series,
            Revision: 1UL,
            tag,
            capture.PositionAuthorityVersion,
            capture.SpatialAuthorityVersion,
            capture.PlacementCommitVersion,
            _actors.SessionLifetimeVersion,
            capture.WholeChamberTag,
            _register.AnticipatedImpactGen(capture.WholeChamberTag),
            Portal: default);
        var snapshot = new SimPlacementMirrorCapture(
            ticket,
            SimPlacementMirrorKind.WithdrawalRestored,
            corpus?.Position ?? Vector3.Zero,
            corpus?.Orientation ?? Quaternion.Identity,
            corpus?.CellPosition.Frame.Origin ?? Vector3.Zero,
            corpus?.InContact ?? false,
            corpus?.OnWalkable ?? false);
        _projFifo.Add(series, snapshot);
        BroadcastStance(snapshot);
    }

    private void ReinstateFollowingParkWithdrawal(
        SimActorRecord capture,
        in ParkedWithdrawal withdrawal)
    {
        if (!_actors.IsCurrent(capture))
            return;
        uint housedChamberIdent = 0u;
        if (capture.KineticBody is { } corpus)
        {
            corpus.InWorld = withdrawal.InWorld;
            corpus.TransientState = withdrawal.TransientState;
            housedChamberIdent = corpus.CellPosition.ObjCellId;
        }
        if (withdrawal.ClockActive)
            _actors.ReactivateObjectTimer(capture);
        bool residencyRestored = false;
        if (housedChamberIdent is not 0u
            && !IsImpactStemQuiescing(housedChamberIdent)
            && capture.WholeChamberTag is 0u)
        {
            _actors.AssignWholeChamber(
                capture,
                housedChamberIdent,
                (housedChamberIdent & 0xFFFF0000u) | 0xFFFFu);
            _register.AcknowledgeSpatialProj(capture, spatial: true);
            residencyRestored = true;
        }
        bool canonicallyWhole = capture.WholeChamberTag is not 0u
            && capture.KineticBody is { InWorld: true };
        if (canonicallyWhole)
            BroadcastRestoredFollowingWithdrawal(capture);
        if (KineticTelemetry.ProbeParkEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[park-restore] guid=0x{capture.ServerGuid:X8} restoreCell=0x{housedChamberIdent:X8} inWorld={withdrawal.InWorld} residency={residencyRestored} presentation={canonicallyWhole}"));
        }
    }
}
