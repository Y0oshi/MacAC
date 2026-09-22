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

// Operations parked until their cell (or collision generation) becomes resident, and the buckets
// that index them
internal sealed partial class SimSetPositionLedger
{
    private readonly Dictionary<CellEpoch, List<SimActorKey>> _shelvedByChamberEpoch = [];

    private readonly List<CellEpoch> _shelvedBinOrdering = [];

    private readonly Dictionary<UnboundCell, List<SimActorKey>> _shelvedUnboundByChamber = [];

    private readonly List<UnboundCell> _shelvedUnboundOrdering = [];

    internal int TryRecoverUnboundPostponedWhenSummonPrimed(uint chamberIdent)
    {
        Live();
        if (chamberIdent is 0u
            || !_register.Engine.IsSummonChamberPrimed(chamberIdent))

            return 0;

        uint stem = chamberIdent & 0xFFFF0000u;
        ulong arbiter = _register.ImpactGenArbiter(chamberIdent);
        if (arbiter is 0UL
            || !_register.IsImpactEvaluationStemAdmissible(chamberIdent))

            return 0;

        var reattempts = new List<SimActorPlacementTicket>();
        int recovered = ReclaimUnboundShelved(
            chamberIdent,
            stem,
            arbiter,
            reattempts);
        recovered += ReclaimStaleShelved(
            chamberIdent,
            stem,
            arbiter,
            reattempts);

        foreach (SimActorPlacementTicket ticket in reattempts)
        {
            if (_register.ImpactGenArbiter(chamberIdent) != arbiter
                || !_register.IsImpactEvaluationStemAdmissible(chamberIdent)
                || !_register.Engine.IsSummonChamberPrimed(chamberIdent))

                break;
            if (OpHoldsForTicket(ticket.Entity, ticket, out SimOperation? op)
                && op.WakeableLostCell
                && op.ExactCellId == chamberIdent
                && op.CollisionPrefix == stem
                && op.CollisionGeneration == arbiter
                && op.CollisionGenerationReady
                && op.WithdrawalAcknowledged)

                ReattemptShelved(op, preservePostponedOrdering: true);
        }
        return recovered;
    }

    internal int TryRecoverPostponedForLb(uint lbIdent)
    {
        Live();
        uint stem = lbIdent & 0xFFFF0000u;
        if (stem is 0u)
            return 0;

        HashSet<uint> chambers = new HashSet<uint>();
        for (int ordinal = 0; ordinal < _shelvedUnboundOrdering.Count; ++ordinal)
        {
            UnboundCell tag = _shelvedUnboundOrdering[ordinal];
            if (tag.CollisionPrefix == stem)
                chambers.Add(tag.CellId);
        }
        for (int ordinal = 0; ordinal < _shelvedBinOrdering.Count; ++ordinal)
        {
            CellEpoch tag = _shelvedBinOrdering[ordinal];
            if (tag.CollisionPrefix == stem)
                chambers.Add(tag.CellId);
        }

        int recovered = 0;
        foreach (uint chamberIdent in chambers)
            recovered += TryRecoverUnboundPostponedWhenSummonPrimed(chamberIdent);
        return recovered;
    }

    private SimSetPositionUpshot ParkOp(
        SimOperation op,
        in PlaceOutcome outcome,
        bool broadcastImmediately = true,
        ulong? impactGenOverride = null,
        uint? impactStemOverride = null,
        bool restorableOnAbort = false)
    {
        KineticBody corpus = op.Body!;
        bool precedingInRealm = corpus.InWorld;
        var precedingTransientPhase = corpus.TransientState;
        bool precedingTimerEngaged = op.Record.ObjectClock.IsActive;
        corpus.Orientation = outcome.Orientation;
        corpus.SnapToChamber(
            outcome.CellId,
            outcome.Position,
            outcome.CellLocalPosition);
        uint shelvedChamberIdent = corpus.CellPosition.ObjCellId;
        op.ParkedWithdrawal = new ParkedWithdrawal(
            Captured: restorableOnAbort
                && (shelvedChamberIdent is 0u
                    || !IsImpactStemQuiescing(shelvedChamberIdent)),
            InWorld: precedingInRealm,
            TransientState: precedingTransientPhase,
            ClockActive: precedingTimerEngaged);
        if (KineticTelemetry.ProbeParkEnabled)
        {
            string parkCause = impactStemOverride is uint blockedStem
                ? FormattableString.Invariant($"quiescence:0x{blockedStem:X8}")
                : "unplaceable";
            Console.WriteLine(FormattableString.Invariant(
                $"[park] guid=0x{op.Record.ServerGuid:X8} cause={parkCause} resultCell=0x{outcome.CellId:X8} restoreCell=0x{shelvedChamberIdent:X8} eligible={restorableOnAbort} captured={op.ParkedWithdrawal.Captured}"));
        }
        corpus.InWorld = false;
        corpus.TransientState &= ~TransientPhaseFlagSet.Active;
        if (op.Record.PeerMotion is ISimPeerPlacement distant)
        {
            distant.LastServerPosition = outcome.Position;
            distant.LastServerPositionTime = _register.UtcInstantSecs;
            distant.LastShadowSyncPosition = Vector3.Zero;
            distant.LastShadowSyncOrientation = Quaternion.Zero;
        }

        WithdrawStance(op.Record);
        _actors.SuspendObjectTimer(op.Record);
        op.SpatialAuthorityVersion =
            op.Record.SpatialAuthorityVersion;
        _actors.ProgressStanceSeal(op.Record);
        op.PlacementCommitVersion =
            op.Record.PlacementCommitVersion;
        op.ExactCellId = outcome.CellId;
        op.WakeableLostCell = true;
        op.EnteringWorldFromCelllessResidence = true;
        ArmLostForClan(op);
        op.CollisionGeneration = impactGenOverride
            ?? _register.AnticipatedImpactGen(outcome.CellId);
        op.CollisionPrefix = impactStemOverride
            ?? outcome.CellId & 0xFFFF0000u;
        op.CollisionQuiescenceHeld = impactStemOverride.HasValue;
        op.Command = op.Command with
        {
            Physics = op.Command.Physics with
            {
                Position = outcome.Position,
                Orientation = outcome.Orientation,
                CellId = outcome.CellId,
                CellLocalPosition = outcome.CellLocalPosition,
                CurrentCellId = null,
            },
        };
        if (_loadingAuthorities.ContainsKey(op.Key))
        {
            RebindLinedDirective(op);
        }
        else
        {
            _loadingAuthorities[op.Key] =
                LoadingArbiterOf(
                    op,
                    WireLocusOf(
                        outcome.CellId,
                        outcome.CellLocalPosition,
                        outcome.Orientation),
                    readied: true,
                    op.Command);
        }
        FileShelved(op);
        op.Stage = op.RequiresPreparation
            ? SimActorPlacementStage.AwaitingPreparation
            : op.CollisionQuiescenceHeld
                ? SimActorPlacementStage.QuiescenceHeld
                : SimActorPlacementStage.AwaitingWithdrawalAcknowledgement;
        var proj = BroadcastProj(
            op,
            SimPlacementMirrorKind.Withdraw,
            outcome,
            broadcastImmediately);
        return Upshot(
            SimSetPositionStatus.DeferredCell,
            outcome,
            proj);
    }

    private void ReattemptShelved(SimOperation op, bool preservePostponedOrdering = false)
    {
        if (op.DormantLocalActivation)
            return;
        if (!OpHolds(op)
            || !op.WakeableLostCell
            || op.RequiresPreparation
            || op.Expired
            || !op.WithdrawalAcknowledged
            || !op.CollisionGenerationReady
            || op.ProjectionSequence is not 0UL)

            return;

        if (!ShelvedWakeLoadingHolds(op))
            return;

        var opTicket = op.Token;
        SimContactPrefixLullTicket restoringStillness = default;
        if (TryBlockingLull(
                op.Command.Physics,
                out Lull? blocking))
        {
            if (blocking!.FreeInHeadway
                && blocking.FreeGenPrimed
                && op.CollisionQuiescenceHeld
                && op.CollisionPrefix
                    == blocking.Token.LandblockPrefix)
            {
                restoringStillness = blocking.Token;
            }
            else
            {
                UnfileShelved(op, preservePostponedOrdering);
                op.CollisionPrefix = blocking.Token.LandblockPrefix;
                op.CollisionGeneration = blocking.Token.CollisionGeneration;
                op.CollisionGenerationReady = false;
                op.CollisionQuiescenceHeld = true;
                op.Stage = SimActorPlacementStage.QuiescenceHeld;
                FileShelved(op);
                return;
            }
        }

        UnfileShelved(op, preservePostponedOrdering);
        op.CollisionQuiescenceHeld = false;
        op.Command = op.Command with
        {
            GameTime = _register.StanceSimulationMoment(
                op.Command.GameTime),
        };
        RebindLinedDirective(op);
        PlaceOutcome outcome;
        if (WellFormed(op.Command.Physics))
        {
            _linkTapPile.Push(new ContactHookFrame(
                op.Record,
                op.PositionAuthorityVersion,
                op.SpatialAuthorityVersion,
                op.SourceVelocityAuthorityVersion,
                op.Command.GameTime,
                op.PreviousContact,
                op.PreviousOnWalkable));
            try
            {
                outcome = _register.Engine.SetPosition(
                    op.Command.Physics,
                    _linkTap);
            }
            finally
            {
                _linkTapPile.Pop();
            }
        }
        else
        {
            outcome = InvalidPlace(op.Command.Physics);
        }
        op.CollisionGenerationReady = false;
        if (!OpHoldsForTicket(
                opTicket.Entity,
                opTicket,
                out SimOperation? refreshed))

            return;
        op = refreshed;
        if (outcome.IsSuccessful
            && TryBlockingLull(
                outcome,
                out Lull? queriedStillness,
                restoringStillness))
        {
            var pinned = outcome with
            {
                Residence = KineticResidenceVerdict.DeferredCell,
            };
            op.Result = pinned;
            op.ExactCellId = pinned.CellId;
            op.CollisionPrefix =
                queriedStillness!.Token.LandblockPrefix;
            op.CollisionGeneration =
                queriedStillness.Token.CollisionGeneration;
            op.CollisionQuiescenceHeld = true;
            op.Stage = SimActorPlacementStage.QuiescenceHeld;
            _linedCarriers[op.Key] = op.Command.Physics;
            FileShelved(op);
            return;
        }
        if (outcome.IsPostponed)
        {
            op.Result = outcome;
            op.ExactCellId = outcome.CellId;
            _linedCarriers[op.Key] = op.Command.Physics;
            op.CollisionPrefix = outcome.CellId & 0xFFFF0000u;
            op.CollisionGeneration = _register
                .AnticipatedImpactGen(outcome.CellId);
            FileShelved(op);
            return;
        }
        if (!outcome.IsSuccessful)
        {
            if (outcome.Error is PlaceError.InvalidArguments)
            {
                op.RequiresPreparation = true;
                op.Stage = SimActorPlacementStage
                    .AwaitingPreparation;
                op.CollisionPrefix = op.ExactCellId
                    & 0xFFFF0000u;
                op.CollisionGeneration = _register
                    .AnticipatedImpactGen(op.ExactCellId);
                FileShelved(op);
            }
            else
            {
                op.Stage = SimActorPlacementStage.AwaitingCell;
                op.CollisionPrefix = op.ExactCellId
                    & 0xFFFF0000u;
                op.CollisionGeneration = _register
                    .AnticipatedImpactGen(op.ExactCellId);
                FileShelved(op);
            }
            return;
        }
        if (!OpHoldsForTicket(
                opTicket.Entity,
                opTicket,
                out SimOperation? stillLatest))

            return;
        op = stillLatest;
        _linedCarriers[op.Key] = op.Command.Physics;
        if (!SealStance(op, outcome))
        {
            BroadcastAbort(AbortOp(opTicket.Entity, opTicket));
            return;
        }

        if (!_ops.TryGetValue(
                opTicket.Entity,
                out SimOperation? stillOwns)
            || stillOwns.Token != opTicket)

            return;

        op.Stage = SimActorPlacementStage
            .AwaitingCommitAcknowledgement;
        _ = BroadcastProj(
            op,
            SimPlacementMirrorKind.Place,
            outcome);
    }

    private bool ShelvedWakeLoadingHolds(SimOperation op)
    {
        if (_loadingAuthorities.TryGetValue(
                op.Key,
                out StagingAuthority arbiter)
            && arbiter.OperationId == op.Token.OperationId
            && arbiter.Prepared
            && arbiter.PreparedCommand == op.Command
            && LoadingArbiterHolds(op, arbiter))

            return true;

        op.SourceVelocityAuthorityVersion =
            op.Record.VelArbiterVer;
        op.RequiresPreparation = true;
        op.Stage = SimActorPlacementStage.AwaitingPreparation;
        op.PreparedCommandAwaitingWithdrawalAck = null;
        _loadingAuthorities[op.Key] =
            LoadingArbiterOf(
                op,
                WireLocusOf(
                    op.ExactCellId,
                    op.Result.CellLocalPosition,
                    op.Result.Orientation),
                readied: false);
        return false;
    }

    private void FileShelved(SimOperation op)
    {
        if (op.ExactCellId is 0u
            || op.CollisionGeneration is 0UL)

            return;
        CellEpoch bin = new CellEpoch(
            op.ExactCellId,
            op.CollisionPrefix,
            op.CollisionGeneration);
        if (!_shelvedByChamberEpoch.TryGetValue(
                bin,
                out List<SimActorKey>? actors))
        {
            actors = [];
            _shelvedByChamberEpoch.Add(bin, actors);
            _shelvedBinOrdering.Add(bin);
        }
        if (!actors.Contains(op.Key))
            actors.Add(op.Key);
    }

    private void FileUnboundShelved(SimOperation op)
    {
        if (op.ExactCellId is 0u)
            return;
        UnboundCell tag = new UnboundCell(
            op.ExactCellId,
            op.CollisionPrefix);
        if (!_shelvedUnboundByChamber.TryGetValue(
                tag,
                out List<SimActorKey>? actors))
        {
            actors = [];
            _shelvedUnboundByChamber.Add(tag, actors);
            _shelvedUnboundOrdering.Add(tag);
        }
        if (!actors.Contains(op.Key))
            actors.Add(op.Key);
    }

    private void UnfileShelved(SimOperation op, bool preserveOrdering = false)
    {
        if (op.ExactCellId is 0u)

            return;
        if (op.CollisionGeneration is 0UL)
        {
            UnboundCell tag = new UnboundCell(
                op.ExactCellId,
                op.CollisionPrefix);
            if (_shelvedUnboundByChamber.TryGetValue(
                    tag,
                    out List<SimActorKey>? unbound))
            {
                int unboundOrdinal = unbound.IndexOf(op.Key);
                if (unboundOrdinal >= 0)
                {
                    int previous = unbound.Count - 1;
                    if (!preserveOrdering)
                        unbound[unboundOrdinal] = unbound[previous];
                    unbound.RemoveAt(preserveOrdering ? unboundOrdinal : previous);
                }
                if (unbound.Count is 0)
                {
                    _shelvedUnboundByChamber.Remove(tag);
                    _shelvedUnboundOrdering.Remove(tag);
                }
            }
            return;
        }
        CellEpoch bin = new CellEpoch(
            op.ExactCellId,
            op.CollisionPrefix,
            op.CollisionGeneration);
        if (_shelvedByChamberEpoch.TryGetValue(
                bin,
                out List<SimActorKey>? actors))
        {
            int ordinal = actors.IndexOf(op.Key);
            if (ordinal >= 0)
            {
                int previous = actors.Count - 1;
                if (!preserveOrdering)
                    actors[ordinal] = actors[previous];
                actors.RemoveAt(preserveOrdering ? ordinal : previous);
            }
            if (actors.Count is 0)
                DiscardShelvedBin(bin);
        }
    }

    private void LoosenShelvedBin(CellEpoch bin)
    {
        if (!_shelvedByChamberEpoch.TryGetValue(
                bin,
                out List<SimActorKey>? actors))

            return;
        DiscardShelvedBin(bin);
        UnboundCell unboundTag = new UnboundCell(
            bin.CellId,
            bin.CollisionPrefix);
        if (!_shelvedUnboundByChamber.TryGetValue(
                unboundTag,
                out List<SimActorKey>? unbound))
        {
            unbound = [];
            _shelvedUnboundByChamber.Add(unboundTag, unbound);
            _shelvedUnboundOrdering.Add(unboundTag);
        }
        for (int ordinal = 0; ordinal < actors.Count; ++ordinal)
        {
            SimActorKey tag = actors[ordinal];
            if (!_ops.TryGetValue(tag, out SimOperation? op)
                || !op.WakeableLostCell
                || op.ExactCellId != bin.CellId
                || op.CollisionPrefix != bin.CollisionPrefix
                || op.CollisionGeneration != bin.CollisionGeneration)

                continue;
            op.CollisionGeneration = 0UL;
            op.CollisionGenerationReady = false;
            if (!unbound.Contains(tag))
                unbound.Add(tag);
        }
        if (unbound.Count is 0)
        {
            _shelvedUnboundByChamber.Remove(unboundTag);
            _shelvedUnboundOrdering.Remove(unboundTag);
        }
    }

    private void DiscardShelvedBin(CellEpoch bin)
    {
        _shelvedByChamberEpoch.Remove(bin);
        int ordinal = _shelvedBinOrdering.IndexOf(bin);
        if (ordinal < 0)
            return;
        int previous = _shelvedBinOrdering.Count - 1;
        _shelvedBinOrdering[ordinal] = _shelvedBinOrdering[previous];
        _shelvedBinOrdering.RemoveAt(previous);
    }

    private int ReclaimUnboundShelved(
        uint chamberIdent,
        uint stem,
        ulong arbiter,
        List<SimActorPlacementTicket> reattempts)
    {
        UnboundCell unboundTag = new UnboundCell(chamberIdent, stem);
        if (!_shelvedUnboundByChamber.Remove(
                unboundTag,
                out List<SimActorKey>? kept))

            return 0;

        _shelvedUnboundOrdering.Remove(unboundTag);
        int recovered = 0;
        List<SimActorKey>? leftover = null;
        for (int ordinal = 0; ordinal < kept.Count; ++ordinal)
        {
            SimActorKey actor = kept[ordinal];
            if (!_ops.TryGetValue(actor, out SimOperation? op)
                || !op.WakeableLostCell
                || op.ExactCellId != chamberIdent
                || op.CollisionPrefix != stem
                || op.CollisionGeneration is not 0UL)
            {
                leftover ??= [];
                leftover.Add(actor);
                continue;
            }

            NoteShelvedRecovered(op, arbiter, reattempts);
            ++recovered;
        }

        if (leftover is { Count: > 0 })
        {
            _shelvedUnboundByChamber.Add(unboundTag, leftover);
            _shelvedUnboundOrdering.Add(unboundTag);
        }

        return recovered;
    }

    private int ReclaimStaleShelved(
        uint chamberIdent,
        uint stem,
        ulong arbiter,
        List<SimActorPlacementTicket> reattempts)
    {
        CellEpoch[] stale = _shelvedBinOrdering
            .Where(tag => tag.CellId == chamberIdent
                && tag.CollisionPrefix == stem
                && tag.CollisionGeneration != arbiter)
            .ToArray();
        if (stale.Length is 0)
            return 0;

        int recovered = 0;
        for (int binOrdinal = 0; binOrdinal < stale.Length; ++binOrdinal)
        {
            CellEpoch bin = stale[binOrdinal];
            if (!_shelvedByChamberEpoch.TryGetValue(
                    bin,
                    out List<SimActorKey>? indexed))

                continue;

            var actors = indexed.ToArray();
            DiscardShelvedBin(bin);
            for (int ordinal = 0; ordinal < actors.Length; ++ordinal)
            {
                SimActorKey actor = actors[ordinal];
                if (!_ops.TryGetValue(actor, out SimOperation? op)
                    || !op.WakeableLostCell
                    || op.ExactCellId != chamberIdent
                    || op.CollisionPrefix != stem
                    || op.CollisionGeneration != bin.CollisionGeneration)

                    continue;

                NoteShelvedRecovered(op, arbiter, reattempts);
                ++recovered;
            }
        }

        return recovered;
    }

    private void NoteShelvedRecovered(
        SimOperation op,
        ulong arbiter,
        List<SimActorPlacementTicket> reattempts)
    {
        ulong preceding = op.CollisionGeneration;
        op.CollisionGeneration = arbiter;
        op.CollisionGenerationReady = true;
        FileShelved(op);
        reattempts.Add(op.Token);
        if (Mechanics.Kinetics.KineticTelemetry.ProbeParkEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[wake] recover-spawn cell=0x{op.ExactCellId:X8} gen={arbiter} (was {preceding}) guid=0x{op.Record.ServerGuid:X8} dormant={op.DormantLocalActivation}"));
        }
    }
}
