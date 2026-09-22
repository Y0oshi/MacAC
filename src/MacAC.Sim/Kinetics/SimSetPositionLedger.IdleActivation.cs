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

internal sealed partial class SimSetPositionLedger
{
    internal void ReadyDormantOwnActivationOwnership(
        SimActorRecord capture,
        KineticBody corpus,
        in SimActorPlacementTicket ticket)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        if (!ticket.IsValid
            || capture.Key != ticket.Entity
            || !_ops.TryGetValue(ticket.Entity, out SimOperation? op)
            || op.Token != ticket
            || op.Stage is not SimActorPlacementStage
                .AwaitingPreparation
            || !ReferenceEquals(op.Record, capture)
            // B2 dependency (C4 route 4b-2): this `is not null` is what makes DormantLocalActivation and "the
            // record has a body" mutually exclusive, which is the step SimPeerPlacementPilot's
            // pre-engine-Rejected argument uses to dismiss the AwaitingCell stage divergence between
            // PrepareMover and SubmitPlacement.
            || capture.KineticBody is not null
            || !OpHolds(op)
            || corpus.InWorld
            || (corpus.TransientState & TransientPhaseFlagSet.Active) != 0)
        {
            throw new InvalidOperationException(
                "Dormant local activation must bind to the exact current placement owner");
        }

        op.Body = corpus;
        op.DormantLocalActivation = true;
    }

    internal bool TryEvaluateDormantOwnActivation(
        SimActorRecord capture,
        KineticBody corpus,
        in SimActorPlacementTicket ticket,
        in SimSetPositionDirective directive,
        out SimIdleSetPositionEvaluation evaluation)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        evaluation = default;
        if (_ops.TryGetValue(ticket.Entity, out SimOperation? queued)
            && queued.WakeableLostCell
            && queued.ExactCellId is not 0u)
        {
            TryRecoverUnboundPostponedWhenSummonPrimed(queued.ExactCellId);
        }

        if (!PreciseIdleActivationHolds(
                capture,
                corpus,
                ticket,
                directive,
                out _)
            && !TryRearmShelvedIdleActivation(
                capture,
                corpus,
                ticket,
                directive))

            return false;
        if (!PreciseIdleActivationHolds(
                capture,
                corpus,
                ticket,
                directive,
                out SimOperation? op))

            return false;

        var canonReq = directive.Physics with
        {
            MoverPhysicsState = capture.FinalKineticsCondition,
            MovingEntityId = ticket.Entity.LocalEntityId,
            CurrentCellId = null,
        };
        if (!WellFormed(canonReq))
            return false;

        SimSetPositionDirective canonDirective = directive with { Physics = canonReq };
        ulong impactRealmArbiter = _register.ImpactRealmArbiter;
        ulong shadeRealmArbiter = _register.ShadeRealmArbiter;
        var objectChart = _register.ObjectChart;
        ulong objectChartMappingArbiter =
            _register.ObjectChartMappingArbiter;
        ulong objectChartArbiter = objectChart?.AlterationRev ?? 0UL;
        var outcome = _register.Engine.SetPosition(
            canonReq,
            hndImpacts: null);
        if (!PreciseIdleActivationHolds(
                capture,
                corpus,
                ticket,
                directive,
                out SimOperation? latest)
            || !ReferenceEquals(latest, op))

            return false;

        if (!_register.TrySealImpactEvaluationArbiter(
                outcome,
                impactRealmArbiter,
                shadeRealmArbiter,
                objectChart,
                objectChartMappingArbiter,
                objectChartArbiter,
                out SimContactEvaluationAuthority impactArbiter))
        {
            if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[rearm] guid=0x{capture.ServerGuid:X8} seal-refused (transient; lease retained)"));
            }
            return false;
        }

        evaluation = new SimIdleSetPositionEvaluation(
            ticket,
            canonDirective,
            outcome,
            impactArbiter);
        return true;
    }

    internal bool IsDormantOwnEvaluationLatest(
        SimActorRecord capture,
        KineticBody corpus,
        in SimIdleSetPositionEvaluation evaluation)
    {
        return evaluation.IsValid
        && PreciseIdleActivationHolds(
            capture,
            corpus,
            evaluation.Placement,
            evaluation.Command,
            out _,
            allowCanonDirective: true)
        && _register.IsImpactEvaluationArbiterLatest(
            evaluation.CollisionAuthority);
    }

    internal bool IsDormantOwnActivationTenancyLatest(
        SimActorRecord capture,
        KineticBody corpus,
        in SimActorPlacementTicket ticket,
        in SimSetPositionDirective directive)
    {
        return PreciseIdleActivationHolds(
            capture,
            corpus,
            ticket,
            directive,
            out _,
            allowPostponedTenancy: true);
    }

    internal bool IsDormantOwnActivationExpectingChamber(
        SimActorRecord capture,
        KineticBody corpus,
        in SimActorPlacementTicket ticket,
        in SimSetPositionDirective directive)
    {
        return PreciseIdleActivationHolds(
                capture,
                corpus,
                ticket,
                directive,
                out SimOperation? op,
                allowPostponedTenancy: true)
            && op is not null
            && op.Stage is SimActorPlacementStage.AwaitingCell
            && op.DormantLocalActivation
            && op.WakeableLostCell;
    }

    internal bool TryReadyDormantOwnActivationSeal(
        SimActorRecord capture,
        KineticBody corpus,
        in SimIdleSetPositionEvaluation evaluation,
        bool provenShapeless,
        out StagedIdleSetPositionCommit? readied)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        readied = null;
        if (!IsDormantOwnEvaluationLatest(capture, corpus, evaluation)
            || !PreciseIdleActivationHolds(
                capture,
                corpus,
                evaluation.Placement,
                evaluation.Command,
                out SimOperation? op,
                allowCanonDirective: true)
            || op is null)

            return false;

        var outcome = evaluation.Result;
        ProxyRegistry.BakedPlaceProxyCommit? shade = null;
        SimContactNoticesLedger.StagedSetPositionContactBatch?
            impact = null;
        SimPlacementMirrorCapture proj = default;
        SortedDictionary<ulong, SimPlacementMirrorCapture>?
            queuedProj = null;
        ulong postponedImpactGen = 0UL;
        bool postponedImpactGenPrimed = false;
        List<SimActorKey>? postponedBin = null;
        bool postponedBinIsNew = false;

        if (!outcome.IsPostponed && (!_register.ImpactDossiers.TryReadySetLocusLot(
                    capture,
                    corpus,
                    evaluation.Command.GameTime,
                    outcome.IsSealed && op.PreviousContact,
                    outcome.IsSealed && op.PreviousOnWalkable,
                    outcome.IsSealed && outcome.OnWalkable,
                    outcome.CollidedWithEnvironment,
                    outcome.CollidedObjectIds,
                    out impact)
                || impact is null))

            return false;

        if (outcome.IsPostponed)
        {
            // When the spawn EnvCell is already ready under a committed, admissible landblock authority, park
            // on that authority with ready=true so TryRearm can publish first-entry immediately.
            ulong arbiter = _register.ImpactGenArbiter(outcome.CellId);
            bool summonPrimed = outcome.CellId is not 0u
                && _register.Engine.IsSummonChamberPrimed(outcome.CellId);
            bool admissible = outcome.CellId is not 0u
                && _register.IsImpactEvaluationStemAdmissible(outcome.CellId);
            bool wakeImmediately = arbiter is not 0UL && summonPrimed && admissible;
            postponedImpactGen = wakeImmediately
                ? arbiter
                : _register.AnticipatedImpactGen(outcome.CellId);
            postponedImpactGenPrimed = wakeImmediately;
            _linedCarriers.EnsureCapacity(_linedCarriers.Count + 1);
            if (outcome.CellId is not 0u && postponedImpactGen is not 0UL)
            {
                CellEpoch binTag = new CellEpoch(
                    outcome.CellId,
                    outcome.CellId & 0xFFFF0000u,
                    postponedImpactGen);
                if (_shelvedByChamberEpoch.TryGetValue(
                        binTag,
                        out postponedBin))
                {
                    postponedBin.EnsureCapacity(postponedBin.Count + 1);
                }
                else
                {
                    postponedBin = [op.Key];
                    postponedBinIsNew = true;
                    _shelvedByChamberEpoch.EnsureCapacity(
                        _shelvedByChamberEpoch.Count + 1);
                    _shelvedBinOrdering.EnsureCapacity(
                        _shelvedBinOrdering.Count + 1);
                }
            }
            if (!_register.Engine.ShadeObjects.TryReadySetLocus(
                    op.Key.LocalEntityId,
                    outcome.Position,
                    outcome.Orientation,
                    outcome.CellId,
                    evaluation.Command.ShadowWorldOffsetX,
                    evaluation.Command.ShadowWorldOffsetY,
                    ProxyCommitAction.Preserve,
                    ImmutableArray<uint>.Empty,
                    provenShapeless,
                    suspendHolder: true,
                    out shade)
                || shade is null)

                return false;
        }

        ulong anticipatedProjSeries = _projSeq;
        if (outcome.IsSealed)
        {
            _ = checked(capture.ObjectTimerEpoch + 1UL);
            _ = checked(capture.PlacementCommitVersion + 1UL);
            if (capture.WholeChamberTag != outcome.CellId)
                _ = checked(capture.SpatialAuthorityVersion + 1UL);
            if (!_register.TryReadySpatialTrunkAdmission(capture))
                return false;

            ulong series = checked(anticipatedProjSeries + 1UL);
            ulong spatial = capture.SpatialAuthorityVersion
                + (capture.WholeChamberTag == outcome.CellId ? 0UL : 1UL);
            ulong stance = checked(capture.PlacementCommitVersion + 1UL);
            SimPlacementMirrorTicket ticket = new SimPlacementMirrorTicket(
                series,
                Revision: 1UL,
                op.Key,
                op.PositionAuthorityVersion,
                spatial,
                stance,
                op.SessionLifetimeVersion,
                outcome.CellId,
                op.CollisionGeneration,
                evaluation.Command.Portal);
            proj = new SimPlacementMirrorCapture(
                ticket,
                SimPlacementMirrorKind.Place,
                outcome.Position,
                outcome.Orientation,
                outcome.CellLocalPosition,
                outcome.InContact,
                outcome.OnWalkable);
            queuedProj = new SortedDictionary<
                ulong,
                SimPlacementMirrorCapture>(_projFifo)
            {
                [series] = proj,
            };
        }

        readied = new StagedIdleSetPositionCommit
        {
            Evaluation = evaluation,
            Entity = op.Key,
            OpIdent = op.Token.OperationId,
            AnticipatedProjSeries = anticipatedProjSeries,
            Shadow = shade,
            Collision = impact,
            Projection = proj,
            QueuedProj = queuedProj,
            PostponedImpactGen = postponedImpactGen,
            PostponedImpactGenPrimed = postponedImpactGenPrimed,
            PostponedBin = postponedBin,
            PostponedBinIsNew = postponedBinIsNew,
        };
        return LinedIdleSealHolds(capture, corpus, readied);
    }

    internal bool TryEnactDormantOwnActivationSeal(
        SimActorRecord capture,
        KineticBody corpus,
        AvatarLocomotionDriver driver,
        ActorKineticsHarbor kineticsHub,
        StagedIdleSetPositionCommit readied,
        out SimIdleSetPositionCommitStub receipt)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(kineticsHub);
        ArgumentNullException.ThrowIfNull(readied);
        receipt = default;
        if (!LinedIdleSealHolds(capture, corpus, readied)
            || !_ops.TryGetValue(
                readied.Entity,
                out SimOperation? op))

            return false;

        var outcome = readied.Evaluation.Result;
        op.Body = corpus;
        op.DormantLocalActivation = true;
        if (outcome.IsPostponed)
        {
            if (readied.Shadow is null
                || !_register.Engine.ShadeObjects.TryEnactSetLocus(
                    readied.Shadow,
                    out ProxyRegistry.PlaceProxyCommitReceipt
                        postponedShadeReceipt))

                return false;
            corpus.Orientation = outcome.Orientation;
            corpus.JunctureDormantChamberCycle(
                outcome.CellId,
                outcome.Position,
                outcome.CellLocalPosition);
            corpus.InWorld = false;
            corpus.TransientState &= ~TransientPhaseFlagSet.Active;
            op.Result = outcome;
            op.ExactCellId = outcome.CellId;
            op.WakeableLostCell = true;
            op.CollisionGeneration = readied
                .PostponedImpactGen;
            op.CollisionPrefix = outcome.CellId & 0xFFFF0000u;
            op.CollisionGenerationReady = readied
                .PostponedImpactGenPrimed;
            op.Stage = SimActorPlacementStage.AwaitingCell;
            _linedCarriers[op.Key] = readied.Evaluation.Command.Physics;
            if (readied.PostponedBin is { } postponedBin)
            {
                CellEpoch binTag = new CellEpoch(
                    outcome.CellId,
                    op.CollisionPrefix,
                    readied.PostponedImpactGen);
                if (readied.PostponedBinIsNew)
                {
                    _shelvedByChamberEpoch.Add(binTag, postponedBin);
                    _shelvedBinOrdering.Add(binTag);
                }
                else if (!postponedBin.Contains(op.Key))
                {
                    postponedBin.Add(op.Key);
                }
            }
            receipt = new SimIdleSetPositionCommitStub(
                SimIdleSetPositionCommitStatus.DeferredCell,
                op.Key,
                op.Token.OperationId,
                default,
                default,
                postponedShadeReceipt,
                readied.Evaluation.CollisionAuthority,
                op.Record.VectorArbiterVer,
                HitGround: false,
                LeaveGround: false);
            return true;
        }

        if (readied.Collision is null
            || !_register.ImpactDossiers.TryInstallSetLocusLot(
                readied.Collision,
                out SimContactNoticesLedger
                    .SetPositionContactBatchStub impactReceipt))

            return false;

        if (!outcome.IsSealed)
        {
            op.Result = outcome;
            receipt = new SimIdleSetPositionCommitStub(
                SimIdleSetPositionCommitStatus.RejectedPlacement,
                op.Key,
                op.Token.OperationId,
                default,
                impactReceipt,
                default,
                readied.Evaluation.CollisionAuthority,
                op.Record.VectorArbiterVer,
                HitGround: false,
                LeaveGround: false);
            return true;
        }

        bool earlierOnPassable = op.PreviousOnWalkable;
        bool strikeTerrain = !earlierOnPassable
            && outcome.InContact
            && outcome.OnWalkable;
        bool departTerrain = earlierOnPassable
            && !(outcome.InContact && outcome.OnWalkable);

        UnfileShelved(op);
        corpus.Orientation = outcome.Orientation;
        corpus.JunctureDormantChamberCycle(
            outcome.CellId,
            outcome.Position,
            outcome.CellLocalPosition);
        corpus.PreviousRefreshMoment = readied.Evaluation.Command.GameTime;
        corpus.ContactPlaneValid = outcome.InContact;
        corpus.ContactPlane = outcome.ContactPlane;
        corpus.ContactPlaneCellId = outcome.ContactPlaneCellId;
        corpus.ContactPlaneIsWater = outcome.ContactPlaneIsWater;
        if (outcome.InContact)
            corpus.GroundNormal = outcome.ContactPlane.Normal;
        _ = KineticObjUpdate.SealSetLocusLinkStem(
            corpus,
            outcome.InContact,
            outcome.OnWalkable,
            earlierOnPassable);
        op.Result = outcome;
        op.ExactCellId = outcome.CellId;
        op.WakeableLostCell = false;
        op.CollisionGenerationReady = false;
        op.EnteringWorldFromCelllessResidence = false;
        op.Stage = SimActorPlacementStage
            .AwaitingFinalShadowPreparation;
        receipt = new SimIdleSetPositionCommitStub(
            SimIdleSetPositionCommitStatus
                .AwaitingFinalShadowPreparation,
            op.Key,
            op.Token.OperationId,
            readied.Projection,
            impactReceipt,
            default,
            readied.Evaluation.CollisionAuthority,
            op.Record.VectorArbiterVer,
            strikeTerrain,
            departTerrain);
        return true;
    }

    internal SetPositionContactBatchDispatchResult RelayDormantOwnActivationImpact(
        in SimIdleSetPositionCommitStub receipt)
    {
        if (receipt.Status
            is SimIdleSetPositionCommitStatus.DeferredCell
                or SimIdleSetPositionCommitStatus.RejectedAuthority)
        {
            return new(
                SetPositionContactBatchDispatchStatus.RejectedReceipt,
                Reported: false);
        }
        var relay = _register
            .ImpactDossiers.RelaySetLocusLotOutcome(receipt.Collision);
        bool reported = relay.Reported;
        if (receipt.Status
                is SimIdleSetPositionCommitStatus.RejectedPlacement
            && _ops.TryGetValue(receipt.Entity, out SimOperation? op)
            && op.Token.OperationId == receipt.OperationId
            && OpHolds(op)
            && !op.Result.IsSealed
            && !op.Result.IsPostponed)
        {
            op.Result = op.Result with
            {
                Error = reported
                    ? PlaceError.Collided
                    : PlaceError.NoValidPosition,
                CollisionHandlerResult = reported,
            };
        }
        return relay;
    }

    internal bool IsDormantOwnActivationPrephaseLatest(
        SimActorRecord capture,
        KineticBody corpus,
        in SimIdleSetPositionCommitStub receipt)
    {
        return receipt.Status is SimIdleSetPositionCommitStatus
                .AwaitingFinalShadowPreparation
            && _ops.TryGetValue(receipt.Entity, out SimOperation? op)
            && op.Token.OperationId == receipt.OperationId
            && op.Stage is SimActorPlacementStage
                .AwaitingFinalShadowPreparation
            && op.DormantLocalActivation
            && OpHolds(op)
            && ReferenceEquals(op.Record, capture)
            && ReferenceEquals(capture.KineticBody, corpus)
            && !corpus.InWorld
            && (corpus.TransientState & TransientPhaseFlagSet.Active) == 0
            && capture.PhysicsHost is null
            && capture.PeerMotion is null
            && capture.Projectile is null
            && !_register.IsSpatialTrunk(capture)
            && _loadingAuthorities.TryGetValue(
                receipt.Entity,
                out StagingAuthority arbiter)
            && arbiter.OperationId == receipt.OperationId
            && arbiter.Prepared
            && capture.PositionAuthorityVersion
                == arbiter.PositionAuthorityVersion
            && capture.ObjRefDscArbiterVer
                == arbiter.ObjDescAuthorityVersion
            && capture.BuildIntegrationVersion
                == arbiter.CreateIntegrationVersion
            && PrepareChartOf(capture) == arbiter.SetupTableId;
    }

    internal bool IsDormantOwnActivationResponseLatest(
        SimActorRecord capture,
        KineticBody corpus,
        in SimIdleSetPositionCommitStub receipt)
    {
        if (receipt.Status is SimIdleSetPositionCommitStatus
                .AwaitingFinalShadowPreparation)
            return IsDormantOwnActivationPrephaseLatest(capture, corpus, receipt);
        return receipt.Status is SimIdleSetPositionCommitStatus
                .RejectedPlacement
            && _ops.TryGetValue(receipt.Entity, out SimOperation? op)
            && op.Token.OperationId == receipt.OperationId
            && op.DormantLocalActivation
            && OpHolds(op)
            && ReferenceEquals(op.Record, capture)
            && ReferenceEquals(capture.KineticBody, corpus)
            && !op.Result.IsSealed
            && !op.Result.IsPostponed
            && !corpus.InWorld
            && (corpus.TransientState & TransientPhaseFlagSet.Active) == 0
            && capture.PhysicsHost is null
            && capture.PeerMotion is null
            && capture.Projectile is null
            && !_register.IsSpatialTrunk(capture);
    }

    internal bool SealDormantOwnActivationPostTerrain(
        SimActorRecord capture,
        KineticBody corpus,
        in SimIdleSetPositionCommitStub receipt)
    {
        if (!IsDormantOwnActivationPrephaseLatest(capture, corpus, receipt))
            return false;
        KineticObjUpdate.SealSetLocusPostTerrain(corpus);
        var outcome = _ops[receipt.Entity].Result;
        corpus.SlidingNormal = outcome.SlidingNormal;
        if (outcome.SlidingNormalValid)
            corpus.TransientState |= TransientPhaseFlagSet.Sliding;
        else
            corpus.TransientState &= ~TransientPhaseFlagSet.Sliding;
        return IsDormantOwnActivationPrephaseLatest(capture, corpus, receipt);
    }

    internal bool SealDormantOwnActivationPostImpact(
        SimActorRecord capture,
        KineticBody corpus,
        in SimIdleSetPositionCommitStub receipt)
    {
        if (!_ops.TryGetValue(receipt.Entity, out SimOperation? op)
            || op.Token.OperationId != receipt.OperationId
            || !OpHolds(op)
            || !ReferenceEquals(op.Record, capture)
            || !ReferenceEquals(capture.KineticBody, corpus))

            return false;
        if (receipt.Status is SimIdleSetPositionCommitStatus
                .AwaitingFinalShadowPreparation
            && !IsDormantOwnActivationPrephaseLatest(capture, corpus, receipt))

            return false;
        var outcome = op.Result;
        corpus.FramesStationaryFall = outcome.FramesStationaryFall;
        if (VelHolds(op))
        {
            KineticObjUpdate.HandleAllCollisions(
                corpus,
                outcome.CollisionNormalValid,
                outcome.CollisionNormal,
                op.PreviousContact,
                op.PreviousOnWalkable,
                corpus.OnWalkable);
        }
        SettleStationaryBitset(corpus, outcome.FramesStationaryFall);
        return OpHolds(op);
    }

    internal bool TryReadyDormantOwnActivationFinalSeal(
        SimActorRecord capture,
        KineticBody corpus,
        in SimIdleSetPositionCommitStub receipt,
        bool provenShapeless,
        out StagedIdleArmingFinalCommit? readied)
    {
        readied = null;
        if (!IsDormantOwnActivationPrephaseLatest(capture, corpus, receipt)
            || !_ops.TryGetValue(receipt.Entity, out SimOperation? op))

            return false;
        var outcome = op.Result;
        if (!_register.Engine.ShadeObjects.TryReadySetLocus(
                op.Key.LocalEntityId,
                outcome.Position,
                outcome.Orientation,
                outcome.CellId,
                op.Command.ShadowWorldOffsetX,
                op.Command.ShadowWorldOffsetY,
                outcome.ShadowAction,
                outcome.CrossCellIds,
                provenShapeless,
                suspendHolder: false,
                out ProxyRegistry.BakedPlaceProxyCommit? shade)
            || shade is null
            || _projSeq + 1UL
                != receipt.Projection.Token.Sequence)

            return false;
        var queued = new SortedDictionary<
            ulong,
            SimPlacementMirrorCapture>(_projFifo)
        {
            [receipt.Projection.Token.Sequence] = receipt.Projection,
        };
        readied = new StagedIdleArmingFinalCommit
        {
            Entity = receipt.Entity,
            OpIdent = receipt.OperationId,
            AnticipatedProjSeries = _projSeq,
            Shadow = shade,
            Projection = receipt.Projection,
            QueuedProj = queued,
        };
        bool latest = IsDormantOwnActivationPrephaseLatest(
            capture, corpus, receipt);
        bool shadeLatest = _register.Engine.ShadeObjects
            .IsReadiedSetLocusLatest(shade);
        return latest && shadeLatest;
    }

    internal bool TryEnactDormantOwnActivationFinalSeal(
        SimActorRecord capture,
        KineticBody corpus,
        AvatarLocomotionDriver driver,
        ActorKineticsHarbor kineticsHub,
        in SimIdleSetPositionCommitStub prephase,
        StagedIdleArmingFinalCommit readied,
        out SimIdleSetPositionCommitStub committed)
    {
        committed = default;
        if (readied.Entity != prephase.Entity
            || readied.OpIdent != prephase.OperationId
            || readied.AnticipatedProjSeries != _projSeq
            || readied.Projection != prephase.Projection
            || !IsDormantOwnActivationPrephaseLatest(capture, corpus, prephase)
            || !_register.Engine.ShadeObjects.TryEnactSetLocus(
                readied.Shadow,
                out ProxyRegistry.PlaceProxyCommitReceipt shade))

            return false;
        SimOperation op = _ops[prephase.Entity];
        var outcome = op.Result;
        if (capture.WholeChamberTag != outcome.CellId)
        {
            _actors.AssignWholeChamber(capture, outcome.CellId,
                (outcome.CellId & 0xFFFF0000u) | 0xFFFFu);
        }
        op.SpatialAuthorityVersion = capture.SpatialAuthorityVersion;
        _actors.ProgressStanceSeal(capture);
        op.PlacementCommitVersion = capture.PlacementCommitVersion;
        corpus.InWorld = true;
        bool isStatic = (capture.FinalKineticsCondition & KineticStateFlags.Static) != 0;
        if (!isStatic)
            corpus.TransientState |= TransientPhaseFlagSet.Active;
        _actors.AssignKineticsHub(capture, kineticsHub);
        driver.SealCoreActivationCycle();
        _register.Engine.RefreshAvatarCurrChamber(outcome.CellId);
        _register.AcknowledgeSpatialProj(capture, spatial: true);
        _actors.RestartObjectTimerForJoinRealm(capture, isStatic);
        op.Stage = SimActorPlacementStage.AwaitingCommitAcknowledgement;
        op.ProjectionSequence = readied.Projection.Token.Sequence;
        _projFifo = readied.QueuedProj;
        _projSeq = readied.Projection.Token.Sequence;
        DisarmLostForClan(op);
        driver.EngageCoreBulletin();
        committed = prephase with
        {
            Status = SimIdleSetPositionCommitStatus.Committed,
            Shadow = shade,
        };
        return true;
    }

    internal void RelayDormantOwnActivationShade(
        in SimIdleSetPositionCommitStub receipt)
    {
        if (receipt.Status is not (
                SimIdleSetPositionCommitStatus.Committed
                or SimIdleSetPositionCommitStatus.DeferredCell))
            return;
        _register.Engine.ShadeObjects.RelaySetLocusSeal(receipt.Shadow);
    }

    internal void RelayDormantOwnActivationStance(
        in SimIdleSetPositionCommitStub receipt)
    {
        if (!receipt.IsSealed)
            return;
        BroadcastStance(receipt.Projection);
    }

    internal void TossDormantOwnActivationDispatches(
        in SimIdleSetPositionCommitStub receipt,
        bool impactAlreadyDispatched)
    {
        if (!impactAlreadyDispatched)
            _register.ImpactDossiers.TossSetLocusLot(receipt.Collision);
        _register.Engine.ShadeObjects.TossSetLocusSeal(receipt.Shadow);
    }

    internal void RetireDormantOwnActivation(
        in SimIdleSetPositionCommitStub receipt,
        bool impactAlreadyDispatched)
    {
        TossDormantOwnActivationDispatches(
            receipt,
            impactAlreadyDispatched);
        _register.ImpactDossiers.RetireSetLocusLotHolder(
            receipt.Collision);
        if (_ops.TryGetValue(receipt.Entity, out SimOperation? op)
            && op.Token.OperationId == receipt.OperationId)

            _ = AbortOp(receipt.Entity, op.Token);
    }

    internal void RetireDormantOwnActivationTicket(
        SimActorRecord capture,
        in SimActorPlacementTicket ticket)
    {
        if (!ticket.IsValid
            || capture.Key != ticket.Entity
            || !_ops.TryGetValue(ticket.Entity, out SimOperation? op)
            || op.Token != ticket
            || !ReferenceEquals(op.Record, capture))

            return;
        _ = AbortOp(ticket.Entity, ticket);
    }

    internal bool IsDormantOwnActivationSealLatest(
        SimActorRecord capture,
        KineticBody corpus,
        in SimIdleSetPositionCommitStub receipt)
    {
        if (!receipt.IsSealed
            || !receipt.Projection.Token.IsValid
            || capture.Key != receipt.Projection.Token.Entity
            || !ReferenceEquals(capture.KineticBody, corpus)
            || !_projFifo.TryGetValue(
                receipt.Projection.Token.Sequence,
                out SimPlacementMirrorCapture queued)
            || queued != receipt.Projection
            || !_ops.TryGetValue(
                receipt.Projection.Token.Entity,
                out SimOperation? op)
            || op.Stage is not SimActorPlacementStage
                .AwaitingCommitAcknowledgement
            || op.ProjectionSequence
                != receipt.Projection.Token.Sequence
            || !OpHolds(op)
            || !corpus.InWorld
            || !_register.IsSpatialTrunk(capture))

            return false;
        return true;
    }

    internal bool TryGrabDormantOwnActivationOutcome(
        in SimActorPlacementTicket ticket,
        out PlaceOutcome outcome)
    {
        if (!_destroyed
            && ticket.IsValid
            && _ops.TryGetValue(ticket.Entity, out SimOperation? op)
            && op.Token == ticket)
        {
            outcome = op.Result;
            return true;
        }
        outcome = default;
        return false;
    }

    private bool TryRearmShelvedIdleActivation(
        SimActorRecord capture,
        KineticBody corpus,
        in SimActorPlacementTicket ticket,
        in SimSetPositionDirective directive)
    {
        bool latest = PreciseIdleActivationHolds(
            capture,
            corpus,
            ticket,
            directive,
            out SimOperation? op,
            allowPostponedTenancy: true);
        if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
        {
            string why = !latest || op is null
                ? "not-current"
                : op.Stage is not SimActorPlacementStage.AwaitingCell
                    ? $"stage={op.Stage}"
                : !op.DormantLocalActivation ? "not-dormant"
                : !op.WakeableLostCell ? "not-wakeable"
                : !op.CollisionGenerationReady ? "gen-not-ready"
                : op.ProjectionSequence is not 0UL ? "proj-seq"
                : op.CollisionGeneration != _register
                    .ImpactGenArbiter(op.ExactCellId)
                    ? $"gen-mismatch({op.CollisionGeneration}!={_register.ImpactGenArbiter(op.ExactCellId)})"
                : !_register.Engine.IsSummonChamberPrimed(op.ExactCellId)
                    ? "spawn-not-ready"
                : !_register.IsImpactEvaluationStemAdmissible(
                    op.ExactCellId)
                    ? "prefix-inadmissible"
                : "OK";
            Console.WriteLine(FormattableString.Invariant(
                $"[rearm] guid=0x{capture.ServerGuid:X8} verdict={why}"));
        }
        if (!latest
            || op is null
            || op.Stage is not SimActorPlacementStage.AwaitingCell
            || !op.DormantLocalActivation
            || !op.WakeableLostCell
            || !op.CollisionGenerationReady
            || op.ProjectionSequence is not 0UL
            || op.CollisionGeneration != _register
                .ImpactGenArbiter(op.ExactCellId)
            || !_register.Engine.IsSummonChamberPrimed(op.ExactCellId)
            || !_register.IsImpactEvaluationStemAdmissible(
                op.ExactCellId))

            return false;

        UnfileShelved(op);
        op.WakeableLostCell = false;
        op.CollisionGenerationReady = false;
        op.Stage = SimActorPlacementStage.AwaitingPreparation;
        return true;
    }

    private bool LinedIdleSealHolds(
        SimActorRecord capture,
        KineticBody corpus,
        StagedIdleSetPositionCommit readied)
    {
        if (_projSeq != readied.AnticipatedProjSeries
            || !IsDormantOwnEvaluationLatest(
                capture,
                corpus,
                readied.Evaluation)
            || !_ops.TryGetValue(
                readied.Entity,
                out SimOperation? op)
            || op.Token.OperationId != readied.OpIdent)

            return false;
        if (readied.Collision is not null
            && !_register.ImpactDossiers.IsReadiedSetLocusLotLatest(
                readied.Collision))
            return false;
        return readied.Shadow is null
            || _register.Engine.ShadeObjects.IsReadiedSetLocusLatest(
                readied.Shadow);
    }

    private static void SettleStationaryBitset(
        KineticBody corpus,
        int cyclesStationaryFall)
    {
        corpus.TransientState &= ~(TransientPhaseFlagSet.StationaryFall
            | TransientPhaseFlagSet.StationaryStop
            | TransientPhaseFlagSet.StationaryStuck);
        corpus.TransientState |= cyclesStationaryFall switch
        {
            1 => TransientPhaseFlagSet.StationaryFall,
            2 => TransientPhaseFlagSet.StationaryStop,
            3 => TransientPhaseFlagSet.StationaryStuck,
            _ => TransientPhaseFlagSet.None,
        };
    }

    private bool PreciseIdleActivationHolds(
        SimActorRecord capture,
        KineticBody corpus,
        in SimActorPlacementTicket ticket,
        in SimSetPositionDirective directive,
        out SimOperation? op,
        bool allowCanonDirective = false,
        bool allowPostponedTenancy = false)
    {
        op = null;
        if (!ticket.IsValid
            || ticket.Entity != capture.Key
            || ticket.PreparationKind
                is not SimActorPlacementStagingKind.AuthoredMover
            || directive.Kind is not (SimSetPositionOperationKind.InitialLogin
                or SimSetPositionOperationKind.LocalAuthoritative)
            || directive.Portal != default
                && !directive.Portal.IsValid
            || !_ops.TryGetValue(ticket.Entity, out op)
            || op.Token != ticket
            || op.Stage
                is not SimActorPlacementStage.AwaitingPreparation
                && !(allowPostponedTenancy
                    && op.DormantLocalActivation
                    && (op.Stage
                            is SimActorPlacementStage.AwaitingCell
                        && op.WakeableLostCell
                        || op.Stage is SimActorPlacementStage
                            .AwaitingFinalShadowPreparation)
                    && op.ProjectionSequence is 0UL)
            || !ReferenceEquals(op.Record, capture)
            || !OpHolds(op)
            || !ReferenceEquals(capture.KineticBody, corpus)
            || corpus.InWorld
            || (corpus.TransientState & TransientPhaseFlagSet.Active) != 0
            || capture.PhysicsHost is not null
            || capture.PeerMotion is not null
            || capture.Projectile is not null
            || capture.KineticsCorpusAcquisitionInHeadway
            || capture.DistantLocomotionMappingInHeadway
            || capture.MissileMappingInHeadway
            || capture.RequiresDistantStanceCore
            || capture.EraseApprovedForTeardown
            || _register.IsSpatialTrunk(capture)
            || !_loadingAuthorities.TryGetValue(
                ticket.Entity,
                out StagingAuthority arbiter)
            || arbiter.OperationId != ticket.OperationId
            || !arbiter.Prepared
            || !LoadingArbiterHolds(op, arbiter))
        {
            op = null;
            return false;
        }

        if (arbiter.PreparedCommand == directive)
            return true;
        if (!allowCanonDirective)
            return false;

        var authored = arbiter.PreparedCommand;
        return authored with
        {
            Physics = authored.Physics with
            {
                MoverPhysicsState = capture.FinalKineticsCondition,
                MovingEntityId = ticket.Entity.LocalEntityId,
                CurrentCellId = null,
            },
        } == directive;
    }
}
