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
    private sealed class Lull
    {
        internal required SimContactPrefixLullTicket Token
        { get; init; }
        internal required bool IncludeExteriorChambers { get; init; }
        internal required ulong ProjBarrierSeries { get; init; }
        internal SortedDictionary<ulong, SimPlacementMirrorTicket>
            QueuedWithdrawals
        { get; } = [];
        internal SortedDictionary<ulong, SimPlacementMirrorTicket>
            QueuedRevertStances
        { get; } = [];
        internal List<SimPlacementMirrorTicket> KeptWithdrawals
        { get; } = [];
        internal bool ResidentsShelved { get; set; }
        internal bool PermissionIssued { get; set; }
        internal bool FreeInHeadway { get; set; }
        internal ulong FreeGen { get; set; }
        internal bool FreeGenPrimed { get; set; }
    }

    private sealed class LinkSealGuard(
        SimSetPositionLedger holder,
        ulong locusArbiterVer,
        ulong spatialArbiterVer,
        SimActorRecord capture,
        KineticBody corpus,
        ulong stanceSealVer,
        uint wholeChamberIdent)
    {
        internal bool OpHolds()
        {
            return holder.StanceSealHolds(
                locusArbiterVer,
                spatialArbiterVer,
                capture,
                corpus,
                stanceSealVer,
                wholeChamberIdent,
                demandSpatialTrunk: false);
        }
    }

    private readonly record struct ContactHookFrame(
        SimActorRecord Record,
        ulong PositionAuthorityVersion,
        ulong SpatialAuthorityVersion,
        ulong VelocityAuthorityVersion,
        double GameTime,
        bool PreviousContact,
        bool PreviousOnWalkable);

    private readonly Dictionary<uint, Lull> _lulls = [];

    private ulong _lullOpIdents;

    private readonly Stack<ContactHookFrame> _linkTapPile
        = new(4);

    private readonly Func<PlaceContactReport, bool> _linkTap;

    internal bool IsImpactStemQuiescing(uint lbIdent)
    {
        return _lulls.ContainsKey(
            lbIdent & 0xFFFF0000u);
    }

    internal SimContactPrefixLullTicket CommenceImpactStemStillness(
            uint landblockId,
            ulong collisionGeneration,
            bool includeExteriorChambers)
    {
        Live();
        if (collisionGeneration is 0UL)
            throw new ArgumentOutOfRangeException(nameof(collisionGeneration));
        if (landblockId is 0u)
            throw new ArgumentOutOfRangeException(nameof(landblockId));
        uint stem = landblockId & 0xFFFF0000u;

        if (_lulls.TryGetValue(
                stem,
                out Lull? engaged)
            && engaged.FreeInHeadway)
        {
            throw new InvalidOperationException(
                $"Collision quiescence 0x{stem:X8}/{engaged.Token.OperationId} is releasing its retained residents");
        }
        _lulls.Remove(
            stem,
            out Lull? superseded);

        var ticket = new SimContactPrefixLullTicket(
            _actors.SessionLifetimeVersion,
            stem,
            collisionGeneration,
            checked(++_lullOpIdents));
        Lull substitute = new Lull
        {
            Token = ticket,
            IncludeExteriorChambers = includeExteriorChambers,
            ProjBarrierSeries = superseded is null
                ? _projSeq
                : Math.Max(
                    _projSeq,
                    superseded.ProjBarrierSeries),
            ResidentsShelved = superseded?.ResidentsShelved ?? false,
        };
        if (superseded is not null)
        {
            foreach ((ulong series, SimPlacementMirrorTicket queued)
                     in superseded.QueuedWithdrawals)
                substitute.QueuedWithdrawals.Add(series, queued);
            substitute.KeptWithdrawals.AddRange(
                superseded.KeptWithdrawals);
        }
        _lulls.Add(stem, substitute);
        if (superseded is not null)
        {
            RebindLulledShelved(
                superseded.Token,
                collisionGeneration,
                primed: false);
        }
        _register.ProgressImpactStillnessArbiter();
        return ticket;
    }

    internal bool TryObtainImpactStemAlterationPermission(
        in SimContactPrefixLullTicket ticket,
        out SimContactPrefixEditLeave permission)
    {
        Live();
        permission = default;
        if (!TryLull(ticket, out Lull? phase))
            return false;
        Lull latest = phase!;

        if (ProjQueuedThrough(latest.ProjBarrierSeries))
            return false;
        AbortUnstagedStemStances(latest);
        if (!TryLull(
                ticket,
                out Lull? followingAbort)
            || !ReferenceEquals(followingAbort, latest))

            return false;
        if (FormerStemStanceOwed(latest))
            return false;

        if (!latest.ResidentsShelved)
        {
            ParkResidentsForLull(latest);
            if (!TryLull(
                    ticket,
                    out Lull? followingParking)
                || !ReferenceEquals(followingParking, latest))

                return false;
            latest.ResidentsShelved = true;
        }
        else if (AnyHousedAffected(
                     ticket.LandblockPrefix,
                     latest.IncludeExteriorChambers))
        {
            ParkResidentsForLull(latest);
            if (!TryLull(
                    ticket,
                    out Lull? followingAdditionalParking)
                || !ReferenceEquals(followingAdditionalParking, latest))

                return false;
        }

        DiscardRetiredLullWithdrawals(latest);
        if (latest.QueuedWithdrawals.Count is not 0
            || AnyHousedAffected(
                ticket.LandblockPrefix,
                latest.IncludeExteriorChambers)
            || FormerStemStanceOwed(latest)
            || LinkRelayOwed())

            return false;

        latest.PermissionIssued = true;
        permission = new SimContactPrefixEditLeave(
            latest.Token,
            latest.KeptWithdrawals.Count is 0
                ? default
                : latest.KeptWithdrawals.ToImmutableArray());
        return true;
    }

    internal bool IsImpactStemAlterationPermissionLatest(
        in SimContactPrefixEditLeave permission)
    {
        Live();
        return permission.IsValid
            && TryLull(
                permission.Quiescence,
                out Lull? phase)
            && phase!.PermissionIssued
            && phase.QueuedWithdrawals.Count is 0
            && !AnyHousedAffected(
                phase.Token.LandblockPrefix,
                phase.IncludeExteriorChambers)
            && !FormerStemStanceOwed(phase)
            && !LinkRelayOwed();
    }

    internal bool AbortImpactStemStillness(
        in SimContactPrefixLullTicket ticket,
        ulong successorGen = 0UL,
        bool successorPrimed = false)
    {
        Live();
        if (!TryLull(ticket, out Lull? phase))
            return false;

        Lull latest = phase!;
        if (!latest.ResidentsShelved
            && latest.QueuedWithdrawals.Count is 0
            && latest.QueuedRevertStances.Count is 0
            && !AnyLulledShelved(ticket.LandblockPrefix))
        {
            bool removedPriorPark = _lulls.Remove(
                ticket.LandblockPrefix);
            if (removedPriorPark)
                _register.ProgressImpactStillnessArbiter();
            return removedPriorPark;
        }

        if (successorGen is 0UL)
            return false;

        return PushLullFree(
            ticket,
            successorGen,
            successorPrimed,
            demandAlterationPermission: false);
    }

    internal bool AbortImpactStemStillnessToUnavailable(
        in SimContactPrefixLullTicket ticket)
    {
        Live();
        return PushLullFree(
            ticket,
            gen: 0UL,
            primed: false,
            demandAlterationPermission: false);
    }

    internal bool FreeImpactStemFollowingAlteration(
        in SimContactPrefixLullTicket ticket,
        ulong engagedGen,
        bool primed)
    {
        Live();
        return PushLullFree(
            ticket,
            engagedGen,
            primed,
            demandAlterationPermission: true);
    }

    internal void ParkImpactResidents(
        uint lbIdent,
        bool includeExteriorChambers)
    {
        Live();
        uint stem = lbIdent & 0xFFFF0000u;
        List<SimActorRecord> trunks = new List<SimActorRecord>();
        _register.DuplicateSpatialTrunksTo(trunks);
        var affected = trunks
            .Where(capture => HousedAffected(
                capture,
                stem,
                includeExteriorChambers))
            .ToArray();
        for (int ordinal = 0; ordinal < affected.Length; ++ordinal)
        {
            if (affected[ordinal].Key is { } tag
                && _ops.ContainsKey(tag))
            {
                throw new InvalidOperationException(
                    $"Collision retirement for 0x{stem:X8} can't overlap active placement for 0x{affected[ordinal].ServerGuid:X8}/{affected[ordinal].Incarnation}.");
            }
        }
        _register.ImpactDossiers.ExitRealmLot(affected);
        var linedWithdrawals = new List<SimPlacementMirrorCapture>(
            trunks.Count);
        for (int ordinal = 0; ordinal < trunks.Count; ++ordinal)
        {
            var record = trunks[ordinal];
            uint chamberIdent = record.WholeChamberTag;
            if (!HousedAffected(
                    record,
                    stem,
                    includeExteriorChambers)
                || record.Key is not { } tag
                || record.KineticBody is not { } corpus
                || _ops.ContainsKey(tag)
                || !affected.Any(contender =>
                    ReferenceEquals(contender, record)
                    && contender.Key == tag))

                continue;

            bool hasReadied = _linedCarriers.TryGetValue(
                tag,
                out KineticSetPositionRequest readied);
            var req = (hasReadied
                ? readied
                : new KineticSetPositionRequest(
                    corpus.Position,
                    corpus.Orientation,
                    chamberIdent,
                    corpus.CellPosition.Frame.Origin,
                    ImmutableArray<PackedContactSphere>.Empty,
                    1f,
                    0f,
                    0f)) with
            {
                Position = corpus.Position,
                Orientation = corpus.Orientation,
                CellId = chamberIdent,
                CellLocalPosition = corpus.CellPosition.Frame.Origin,
                MoverPhysicsState = record.FinalKineticsCondition,
                MovingEntityId = tag.LocalEntityId,
                CurrentCellId = null,
            };
            PlaceOutcome outcome = new PlaceOutcome(
                PlaceError.Ok,
                KineticResidenceVerdict.DeferredCell,
                corpus.Position,
                corpus.Orientation,
                chamberIdent,
                corpus.CellPosition.Frame.Origin,
                InContact: corpus.InContact,
                OnWalkable: corpus.OnWalkable,
                ContactPlane: corpus.ContactPlane,
                ContactPlaneCellId: corpus.ContactPlaneCellId,
                ContactPlaneIsWater: corpus.ContactPlaneIsWater,
                SlidingNormalValid: corpus.SlidingNormal != Vector3.Zero,
                SlidingNormal: corpus.SlidingNormal,
                FramesStationaryFall: corpus.FramesStationaryFall,
                CrossCellIds: ImmutableArray<uint>.Empty,
                CollidedObjectIds: ImmutableArray<uint>.Empty);
            SimSetPositionDirective directive = new SimSetPositionDirective(
                req,
                SimSetPositionOperationKind.RemoteAuthoritative,
                corpus.PreviousRefreshMoment,
                record.VelArbiterVer);
            SimOperation op = RentOp();
            op.Record = record;
            op.Body = corpus;
            op.Token = new SimActorPlacementTicket(
                _actors.SessionLifetimeVersion,
                tag,
                record.PositionAuthorityVersion,
                checked(++_opIdents),
                SimActorPlacementStagingKind.AuthoredMover);
            op.Key = tag;
            op.PositionAuthorityVersion = record.PositionAuthorityVersion;
            op.SessionLifetimeVersion = _actors.SessionLifetimeVersion;
            op.SourceSpatialAuthorityVersion =
                record.SpatialAuthorityVersion;
            op.SourceVelocityAuthorityVersion =
                record.VelArbiterVer;
            op.PreviousContact = corpus.InContact;
            op.PreviousOnWalkable = corpus.OnWalkable;
            op.Command = directive;
            op.Result = outcome;
            op.SpatialAuthorityVersion = record.SpatialAuthorityVersion;
            op.PlacementCommitVersion = record.PlacementCommitVersion;
            op.ExactCellId = chamberIdent;
            op.Stage = SimActorPlacementStage.AwaitingPreparation;
            op.Kind = SimSetPositionOperationKind.RemoteAuthoritative;
            op.Portal = default;
            op.RequiresPreparation = !hasReadied;
            _ops.Add(tag, op);
            _loadingAuthorities[tag] = LoadingArbiterOf(
                op,
                WireLocusOf(
                    chamberIdent,
                    corpus.CellPosition.Frame.Origin,
                    corpus.Orientation),
                readied: hasReadied,
                directive);
            Lull? stillness =
                _lulls.GetValueOrDefault(stem);
            var shelved = ParkOp(
                op,
                outcome,
                broadcastImmediately: false,
                impactGenOverride:
                    stillness?.Token.CollisionGeneration,
                impactStemOverride:
                    stillness?.Token.LandblockPrefix,
                restorableOnAbort: false);
            if (_projFifo.TryGetValue(
                    shelved.Projection.Sequence,
                    out SimPlacementMirrorCapture lined))

                linedWithdrawals.Add(lined);
            if (op.RequiresPreparation)
            {
                op.Stage = SimActorPlacementStage
                    .AwaitingPreparation;
            }
        }

        for (int ordinal = 0; ordinal < linedWithdrawals.Count; ++ordinal)
        {
            var lined =
                linedWithdrawals[ordinal];
            if (_projFifo.TryGetValue(
                    lined.Token.Sequence,
                    out SimPlacementMirrorCapture latest)
                && latest == lined)

                BroadcastStance(lined);
        }
    }

    internal void CommenceImpactGen(uint lbIdent, ulong generation)
    {
        Live();
        if (generation is 0UL)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[wake] begin lb=0x{lbIdent:X8} gen={generation} unboundCells={_shelvedUnboundOrdering.Count} buckets={_shelvedBinOrdering.Count}"));
        }
        uint stem = lbIdent & 0xFFFF0000u;
        for (int ordinal = 0; ordinal < _shelvedUnboundOrdering.Count;)
        {
            UnboundCell unboundTag = _shelvedUnboundOrdering[ordinal];
            if (unboundTag.CollisionPrefix != stem
                || !_shelvedUnboundByChamber.Remove(
                    unboundTag,
                    out List<SimActorKey>? kept))
            {
                ++ordinal;
                continue;
            }
            _shelvedUnboundOrdering.RemoveAt(ordinal);
            CellEpoch bin = new CellEpoch(
                unboundTag.CellId,
                unboundTag.CollisionPrefix,
                generation);
            List<SimActorKey> rebound = new List<SimActorKey>(kept.Count);
            for (int actorOrdinal = 0;
                 actorOrdinal < kept.Count;
                 ++actorOrdinal)
            {
                SimActorKey actor = kept[actorOrdinal];
                if (_ops.TryGetValue(actor, out SimOperation? op)
                    && op.WakeableLostCell
                    && op.ExactCellId == unboundTag.CellId
                    && op.CollisionPrefix == stem
                    && op.CollisionGeneration is 0UL)
                {
                    op.CollisionGeneration = generation;
                    rebound.Add(actor);
                }
            }
            if (rebound.Count is not 0)
            {
                if (_shelvedByChamberEpoch.TryGetValue(
                        bin,
                        out List<SimActorKey>? futureTied))
                {
                    List<SimActorKey> merged = new List<SimActorKey>(
                        rebound.Count + futureTied.Count);
                    merged.AddRange(rebound);
                    merged.AddRange(futureTied);
                    _shelvedByChamberEpoch[bin] = merged;
                }
                else
                {
                    _shelvedByChamberEpoch.Add(bin, rebound);
                    _shelvedBinOrdering.Add(bin);
                }
            }
        }
        foreach (SimOperation op in _ops.Values)
        {
            if (!op.WakeableLostCell
                || op.CollisionGeneration is not 0UL
                || op.CollisionPrefix != stem)

                continue;
            op.CollisionGeneration = generation;
            FileShelved(op);
        }
    }

    internal void AbortImpactGen(uint lbIdent, ulong gen)
    {
        Live();
        if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[wake] cancel lb=0x{lbIdent:X8} gen={gen} buckets={_shelvedBinOrdering.Count}"));
        }
        if (_shelvedBinOrdering.Count is 0)
            return;
        uint stem = lbIdent & 0xFFFF0000u;
        CellEpoch[] matching = _shelvedBinOrdering
            .Where(tag => tag.CollisionGeneration == gen
                && tag.CollisionPrefix == stem)
            .ToArray();
        for (int ordinal = 0; ordinal < matching.Length; ++ordinal)
        {
            LoosenShelvedBin(matching[ordinal]);
        }
    }

    internal void CommitCollisionGeneration(
        uint lbIdent,
        ulong gen,
        bool primed)
    {
        Live();
        if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[wake] commit lb=0x{lbIdent:X8} gen={gen} ready={primed} buckets={_shelvedBinOrdering.Count}"));
        }
        if (!primed || _shelvedBinOrdering.Count is 0)
            return;

        uint stem = lbIdent & 0xFFFF0000u;
        List<CellEpoch> chambers = new List<CellEpoch>();
        for (int ordinal = 0; ordinal < _shelvedBinOrdering.Count; ++ordinal)
        {
            CellEpoch tag = _shelvedBinOrdering[ordinal];
            if (tag.CollisionGeneration == gen
                && tag.CollisionPrefix == stem)

                chambers.Add(tag);
        }
        foreach (CellEpoch chamber in chambers)
        {
            if (!_shelvedByChamberEpoch.TryGetValue(
                    chamber,
                    out List<SimActorKey>? indexed))

                continue;
            var precise = indexed.ToArray();
            if (!_register.Engine.IsSummonChamberPrimed(chamber.CellId))
            {
                if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[wake] STRAND cell=0x{chamber.CellId:X8} gen={chamber.CollisionGeneration} spawnReady=false ops={precise.Length} -> unbound"));
                }
                LoosenShelvedBin(chamber);
                continue;
            }
            if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[wake] wake cell=0x{chamber.CellId:X8} gen={chamber.CollisionGeneration} ops={precise.Length}"));
            }
            DiscardShelvedBin(chamber);
            foreach (SimActorKey actor in precise)
            {
                if (!_ops.TryGetValue(
                        actor,
                        out SimOperation? op)
                    || !op.WakeableLostCell
                    || op.ExactCellId != chamber.CellId
                    || op.CollisionPrefix != stem
                    || op.CollisionGeneration != gen)

                    continue;
                op.CollisionGenerationReady = true;
                if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[wake] op guid=0x{op.Record.ServerGuid:X8} dormant={op.DormantLocalActivation} ack={op.WithdrawalAcknowledged} stage={op.Stage} reqPrep={op.RequiresPreparation} projSeq={op.ProjectionSequence}"));
                }
                if (op.WithdrawalAcknowledged)
                    ReattemptShelved(op);
            }
        }
    }

    private bool OnLinkTap(
        PlaceContactReport dossier)
    {
        var ctx = _linkTapPile.Peek();
        return _register.HandleSetPositionCollisions(
            ctx.Record,
            ctx.PositionAuthorityVersion,
            ctx.SpatialAuthorityVersion,
            ctx.VelocityAuthorityVersion,
            ctx.GameTime,
            ctx.PreviousContact,
            ctx.PreviousOnWalkable,
            dossier);
    }

    private bool PushLullFree(
        in SimContactPrefixLullTicket ticket,
        ulong gen,
        bool primed,
        bool demandAlterationPermission)
    {
        if ((gen is 0UL && primed)
            || !TryLull(
                ticket,
                out Lull? phase))

            return false;

        Lull latest = phase!;
        if (latest.QueuedWithdrawals.Count is not 0)
            return false;
        if (demandAlterationPermission
            && !latest.PermissionIssued
            && !latest.FreeInHeadway)

            return false;
        if (!latest.FreeInHeadway)
        {
            latest.FreeInHeadway = true;
            latest.FreeGen = gen;
            latest.FreeGenPrimed = primed;
            latest.PermissionIssued = false;
        }
        else if (latest.FreeGen != gen
            || latest.FreeGenPrimed != primed)
        {
            return false;
        }

        if (_ops.Count is not 0)
        {
            RebindLulledShelved(
                ticket,
                primed ? gen : 0UL,
                primed,
                freeUnavailable: !primed);
        }

        if (latest.QueuedRevertStances.Count is not 0
            || AnyLulledShelved(ticket.LandblockPrefix))

            return false;
        bool removed = _lulls.Remove(
            ticket.LandblockPrefix);
        if (removed)
            _register.ProgressImpactStillnessArbiter();
        return removed;
    }

    private bool HousedAffected(
        SimActorRecord capture,
        uint stem,
        bool includeExteriorChambers)
    {
        uint chamberIdent = capture.WholeChamberTag;
        bool preciseResidence = (chamberIdent & 0xFFFF0000u) == stem
            && (includeExteriorChambers || (chamberIdent & 0xFFFFu) >= 0x0100u);
        return preciseResidence
            && capture.Key is not null
            && _actors.IsCurrent(capture)
            && capture.KineticBody is not null
            && _register.IsSpatialTrunk(capture)
            && (capture.FinalKineticsCondition & KineticStateFlags.Static) == 0
            && !_actors.AncestorAttachments.HasSealedAncestor(
                capture.ServerGuid);
    }

    private void ParkResidentsForLull(
        Lull phase)
    {
        if (!TryLull(phase.Token, out Lull? latest)
            || !ReferenceEquals(latest, phase))

            return;
        ParkImpactResidents(
            phase.Token.LandblockPrefix,
            phase.IncludeExteriorChambers);
    }

    private bool AnyHousedAffected(
        uint stem,
        bool includeExteriorChambers)
    {
        if (_register.SpatialTrunkTally is 0)
            return false;
        List<SimActorRecord> trunks = new List<SimActorRecord>();
        _register.DuplicateSpatialTrunksTo(trunks);
        for (int ordinal = 0; ordinal < trunks.Count; ++ordinal)
        {
            if (HousedAffected(
                    trunks[ordinal],
                    stem,
                    includeExteriorChambers))

                return true;
        }
        return false;
    }

    private bool TryLull(
        in SimContactPrefixLullTicket ticket,
        out Lull? phase)
    {
        if (ticket.IsValid
            && ticket.SessionLifetimeVersion == _actors.SessionLifetimeVersion
            && _lulls.TryGetValue(
                ticket.LandblockPrefix,
                out phase)
            && phase.Token == ticket)

            return true;
        phase = null;
        return false;
    }

    private bool ProjQueuedThrough(ulong barrierSeries)
    {
        return barrierSeries is not 0UL
        && _projFifo.Count is not 0
        && HeadProjection().Key <= barrierSeries;
    }

    private bool LinkRelayOwed()
    {
        var dossiers =
            _register.ImpactDossiers.GrabOwnership();
        return dossiers.PendingReportCount is not 0
            || dossiers.LeavingOwnerCount is not 0
            || dossiers.AdmissionBlockedOwnerCount is not 0
            || dossiers.PendingSetPositionDispatchCount is not 0
            || dossiers.IsDispatching
            || _register.Engine.ShadeObjects
                .QueuedSetLocusRelayTally is not 0;
    }

    private bool FormerStemStanceOwed(Lull phase)
    {
        uint stem = phase.Token.LandblockPrefix;
        foreach (SimOperation op in _ops.Values)
        {
            if (op.WakeableLostCell || op.DormantLocalActivation)
                continue;
            if (StanceInStem(op.Command.Physics, stem)
                || VerdictInStem(op.Result, stem)
                || _loadingAuthorities.TryGetValue(
                    op.Key,
                    out StagingAuthority prep)
                    && prep.OperationId
                        == op.Token.OperationId
                    && (prep.AcceptedPosition.LandblockId
                        & 0xFFFF0000u) == stem
                || HousedAffected(
                    op.Record,
                    stem,
                    phase.IncludeExteriorChambers))

                return true;
        }
        return false;
    }

    private void AbortUnstagedStemStances(
        Lull phase)
    {
        uint stem = phase.Token.LandblockPrefix;
        List<SimActorKey>? cancelled = null;
        foreach (SimOperation op in _ops.Values)
        {
            if (op.WakeableLostCell
                || op.DormantLocalActivation
                || op.Stage is not SimActorPlacementStage
                    .AwaitingPreparation
                || !_loadingAuthorities.TryGetValue(
                    op.Key,
                    out StagingAuthority prep)
                || prep.OperationId != op.Token.OperationId
                || prep.Prepared
                || !((prep.AcceptedPosition.LandblockId
                        & 0xFFFF0000u) == stem
                    || HousedAffected(
                        op.Record,
                        stem,
                        phase.IncludeExteriorChambers)))

                continue;

            (cancelled ??= []).Add(op.Key);
        }

        if (cancelled is null)
            return;

        for (int ordinal = 0; ordinal < cancelled.Count; ++ordinal)
        {
            _ = AbortShelvedOp(
                cancelled[ordinal],
                abortLostClan: false,
                preserveLostClan: false,
                out SimPlacementMirrorCapture? toss);
            if (toss is { } proj)
                BroadcastStance(proj);
        }
    }

    private static bool StanceInStem(
        in KineticSetPositionRequest req,
        uint stem)
    {
        return (req.CellId & 0xFFFF0000u) == stem
        || req.CurrentCellId is uint latest
            && (latest & 0xFFFF0000u) == stem;
    }

    private static bool VerdictInStem(
        in PlaceOutcome outcome,
        uint stem)
    {
        if ((outcome.CellId & 0xFFFF0000u) == stem)
            return true;
        if (!outcome.QueriedCellIds.IsDefaultOrEmpty)
        {
            foreach (uint chamberIdent in outcome.QueriedCellIds)
            {
                if ((chamberIdent & 0xFFFF0000u) == stem)
                    return true;
            }
        }
        return false;
    }

    private bool TryBlockingLull(
        in KineticSetPositionRequest req,
        out Lull? phase)
    {
        phase = null;
        foreach (Lull contender
                 in _lulls.Values)
        {
            if (StanceInStem(
                    req,
                    contender.Token.LandblockPrefix)
                && (phase is null
                    || contender.Token.OperationId
                        < phase.Token.OperationId))

                phase = contender;
        }
        return phase is not null;
    }

    private bool TryBlockingLull(
        in PlaceOutcome outcome,
        out Lull? phase,
        in SimContactPrefixLullTicket excluded = default)
    {
        phase = null;
        foreach (Lull contender
                 in _lulls.Values)
        {
            if (excluded.IsValid && contender.Token == excluded)
                continue;
            if (VerdictInStem(
                    outcome,
                    contender.Token.LandblockPrefix)
                && (phase is null
                    || contender.Token.OperationId
                        < phase.Token.OperationId))

                phase = contender;
        }
        return phase is not null;
    }

    private void NoteLullWithdrawal(
        SimOperation op,
        in SimPlacementMirrorCapture capture)
    {
        if (capture.Kind is not SimPlacementMirrorKind.Withdraw
            || !_lulls.TryGetValue(
                op.CollisionPrefix,
                out Lull? phase)
            || capture.Token.CollisionGeneration
                != phase.Token.CollisionGeneration)

            return;
        phase.QueuedWithdrawals[capture.Token.Sequence] = capture.Token;
        phase.KeptWithdrawals.Add(capture.Token);
        phase.PermissionIssued = false;
    }

    private void NoteLullRevert(
        SimOperation op,
        in SimPlacementMirrorCapture capture)
    {
        if (capture.Kind is not SimPlacementMirrorKind.Place
            || !_lulls.TryGetValue(
                op.CollisionPrefix,
                out Lull? phase)
            || !phase.FreeInHeadway
            || !phase.FreeGenPrimed)

            return;
        phase.QueuedRevertStances[capture.Token.Sequence] =
            capture.Token;
    }

    private void RetireLullProj(ulong series)
    {
        foreach (Lull phase
                 in _lulls.Values)
        {
            if (phase.QueuedWithdrawals.TryGetValue(
                    series,
                    out _))
            {
                phase.QueuedWithdrawals.Remove(series);
                phase.PermissionIssued = false;
                return;
            }
            if (phase.QueuedRevertStances.Remove(series))
                return;
        }
    }

    private void DiscardRetiredLullWithdrawals(
        Lull phase)
    {
        if (phase.QueuedWithdrawals.Count is 0)
            return;
        ulong[] stale = phase.QueuedWithdrawals
            .Where(duo => !_projFifo.ContainsKey(duo.Key))
            .Select(duo => duo.Key)
            .ToArray();
        for (int ordinal = 0; ordinal < stale.Length; ++ordinal)
            phase.QueuedWithdrawals.Remove(stale[ordinal]);
    }

    private void RebindLulledShelved(
        in SimContactPrefixLullTicket ticket,
        ulong successorGen,
        bool primed,
        bool freeUnavailable = false)
    {
        var capture =
            new List<(SimActorKey Key, SimActorPlacementTicket Token)>(
                _ops.Count);
        foreach (SimOperation extant in _ops.Values)
            capture.Add((extant.Key, extant.Token));
        foreach ((SimActorKey tag, SimActorPlacementTicket grabbedTicket)
                 in capture)
        {
            if (!OpHoldsForTicket(tag, grabbedTicket, out SimOperation? op))
                continue;
            bool unavailableFollowingPrimedSeal = primed
                && op.CollisionQuiescenceHeld
                && op.CollisionGeneration is 0UL
                && op.CollisionPrefix == ticket.LandblockPrefix;
            if (!op.WakeableLostCell
                || !op.CollisionQuiescenceHeld
                || op.CollisionPrefix != ticket.LandblockPrefix
                || (op.CollisionGeneration
                        != ticket.CollisionGeneration
                    && !unavailableFollowingPrimedSeal))

                continue;
            UnfileShelved(op);
            ulong reboundGen = freeUnavailable
                || unavailableFollowingPrimedSeal
                    ? 0UL
                    : successorGen;
            op.CollisionGeneration = reboundGen;
            op.CollisionGenerationReady = primed
                && reboundGen is not 0UL;
            if (freeUnavailable || unavailableFollowingPrimedSeal)
            {
                op.CollisionQuiescenceHeld = false;
                op.Stage = op.RequiresPreparation
                    ? SimActorPlacementStage.AwaitingPreparation
                    : SimActorPlacementStage.AwaitingCell;
            }
            if (reboundGen is not 0UL)
                FileShelved(op);
            else
                FileUnboundShelved(op);
            if (op.CollisionGenerationReady
                && op.WithdrawalAcknowledged)

                ReattemptShelved(op);
        }
    }

    private bool AnyLulledShelved(uint stem)
    {
        foreach (SimOperation op in _ops.Values)
        {
            if (op.WakeableLostCell
                && op.CollisionQuiescenceHeld
                && op.CollisionPrefix == stem)

                return true;
        }
        return false;
    }
}
