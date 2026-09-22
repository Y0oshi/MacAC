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

internal sealed partial class SimSetPositionLedger : IDisposable
{
    private const uint UpperSynchronousScatterAttempts = 64u;

    private readonly record struct CellEpoch(
        uint CellId,
        uint CollisionPrefix,
        ulong CollisionGeneration);

    private readonly record struct UnboundCell(
        uint CellId,
        uint CollisionPrefix);

    private readonly record struct StagingAuthority(
        ulong OperationId,
        ObjectCreation.RemotePosition AcceptedPosition,
        uint SetupTableId,
        ulong PositionAuthorityVersion,
        ulong VelocityAuthorityVersion,
        ulong StateAuthorityVersion,
        ulong VectorAuthorityVersion,
        ulong ObjDescAuthorityVersion,
        ulong CreateIntegrationVersion,
        ulong PhysicsStateMutationVersion,
        bool Prepared,
        SimSetPositionDirective PreparedCommand);

    private sealed class SimOperation
    {
        internal SimActorRecord Record { get; set; } = null!;
        internal KineticBody? Body { get; set; }
        internal SimActorPlacementTicket Token { get; set; }
        internal SimActorKey Key { get; set; }
        internal ulong PositionAuthorityVersion { get; set; }
        internal ulong SessionLifetimeVersion { get; set; }
        internal ulong SourceSpatialAuthorityVersion { get; set; }
        internal ulong SourceVelocityAuthorityVersion { get; set; }
        internal bool PreviousContact { get; set; }
        internal bool PreviousOnWalkable { get; set; }
        internal SimSetPositionDirective Command { get; set; }
        internal PlaceOutcome Result { get; set; }
        internal ulong SpatialAuthorityVersion { get; set; }
        internal ulong PlacementCommitVersion { get; set; }
        internal uint ExactCellId { get; set; }
        internal ulong CollisionGeneration { get; set; }
        internal uint CollisionPrefix { get; set; }
        internal bool WithdrawalAcknowledged { get; set; }
        internal bool CollisionGenerationReady { get; set; }
        internal bool CollisionQuiescenceHeld { get; set; }
        internal ulong ProjectionSequence { get; set; }
        internal bool WakeableLostCell { get; set; }
        internal SimActorPlacementStage Stage { get; set; }
        internal SimSetPositionOperationKind Kind { get; set; }
        internal SimPortalPlacementAuthority Portal { get; set; }
        internal bool RequiresPreparation { get; set; }
        internal bool Expired { get; set; }
        internal List<SimActorKey>? LostFamilyKeys { get; set; }
        internal bool InheritedLostDeadline { get; set; }
        internal bool EnteringWorldFromCelllessResidence { get; set; }
        internal bool DormantLocalActivation { get; set; }

        internal ParkedWithdrawal ParkedWithdrawal { get; set; }

        internal SimSetPositionParkReason ParkReason { get; set; }
        internal SimSetPositionDirective? PreparedCommandAwaitingWithdrawalAck
        {
            get;
            set;
        }

        internal bool InPool { get; set; }

        internal void ResetAllFieldsToDefault()
        {
            Record = null!;
            Body = null;
            Token = default;
            Key = default;
            PositionAuthorityVersion = 0UL;
            SessionLifetimeVersion = 0UL;
            SourceSpatialAuthorityVersion = 0UL;
            SourceVelocityAuthorityVersion = 0UL;
            PreviousContact = false;
            PreviousOnWalkable = false;
            Command = default;
            Result = default;
            SpatialAuthorityVersion = 0UL;
            PlacementCommitVersion = 0UL;
            ExactCellId = 0u;
            CollisionGeneration = 0UL;
            CollisionPrefix = 0u;
            WithdrawalAcknowledged = false;
            CollisionGenerationReady = false;
            CollisionQuiescenceHeld = false;
            ProjectionSequence = 0UL;
            WakeableLostCell = false;
            Stage = default;
            Kind = default;
            Portal = default;
            RequiresPreparation = false;
            Expired = false;
            LostFamilyKeys = null;
            InheritedLostDeadline = false;
            EnteringWorldFromCelllessResidence = false;
            DormantLocalActivation = false;
            ParkReason = SimSetPositionParkReason.None;
            PreparedCommandAwaitingWithdrawalAck = null;
            ParkedWithdrawal = default;
            InPool = false;
        }
    }

    private readonly record struct ParkedWithdrawal(
        bool Captured,
        bool InWorld,
        TransientPhaseFlagSet TransientState,
        bool ClockActive);

    private readonly SimKineticsLedger _register;

    private readonly SimActorIndex _actors;

    private readonly Dictionary<SimActorKey, SimOperation> _ops = [];

    private readonly Dictionary<SimActorKey, KineticSetPositionRequest> _linedCarriers = [];

    private readonly Dictionary<SimActorKey, StagingAuthority> _loadingAuthorities = [];

    private ulong _opIdents;

    private SimActorObjectEventFlow? _signals;

    private Action<SimActorKey, ulong>? _onWrapUpAcked;

    private bool _destroyed;

    private const int UpperPooledOps = 64;

    private readonly Stack<SimOperation> _opReservoir = new();

    internal SimSetPositionLedger(
        SimKineticsLedger physics,
        SimActorIndex entities)
    {
        _register = physics ?? throw new ArgumentNullException(nameof(physics));
        _actors = entities ?? throw new ArgumentNullException(nameof(entities));
        _linkTap =
            OnLinkTap;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        Wipe();
        _signals = null;
        _destroyed = true;
    }

    internal SimSetPositionHoldingCapture GrabOwnership()
    {
        int postponed = 0;
        int expectingPrep = 0;
        int shelvedExpectingRig = 0;
        int shelvedExpectingRealmCycle = 0;
        foreach (SimOperation op in _ops.Values)
        {
            if (op.Stage
                is SimActorPlacementStage.AwaitingPreparation)

                ++expectingPrep;
            if (op.WakeableLostCell)
                ++postponed;
            switch (op.ParkReason)
            {
                case SimSetPositionParkReason.AwaitingSetupCollision:
                    ++shelvedExpectingRig;
                    break;
                case SimSetPositionParkReason.AwaitingWorldFrame:
                    ++shelvedExpectingRealmCycle;
                    break;
            }
        }
        int queuedStillnessProjections = 0;
        foreach (Lull stillness
                 in _lulls.Values)
        {
            queuedStillnessProjections +=
                stillness.QueuedWithdrawals.Count
                + stillness.QueuedRevertStances.Count;
        }
        return new SimSetPositionHoldingCapture(
            _ops.Count,
            expectingPrep,
            postponed,
            _projFifo.Count,
            _lostBy.Count,
            _lostHeap.Count,
            _lostHeapOrdinal.Count,
            _expiredLost.Count,
            _expiredLostJoints.Count,
            _shelvedByChamberEpoch.Count,
            _shelvedBinOrdering.Count,
            _shelvedUnboundByChamber.Count,
            _shelvedUnboundOrdering.Count,
            _linedCarriers.Count,
            _loadingAuthorities.Count,
            _wrapUpWatches.Count,
            _ackedCompletions.Count,
            _lulls.Count,
            queuedStillnessProjections,
            _opReservoir.Count,
            shelvedExpectingRig,
            shelvedExpectingRealmCycle);
    }

    internal void AttachSignalFlow(SimActorObjectEventFlow signals)
    {
        Live();
        ArgumentNullException.ThrowIfNull(signals);
        if (_signals is not null)
            throw new InvalidOperationException(
                "The Runtime placement event stream is by now bound");
        _signals = signals;
    }

    internal bool CanCommenceAuthoredStanceSeries =>
        _opIdents != ulong.MaxValue;

    internal void AttachExecutorWrapUpAcknowledgement(
        Action<SimActorKey, ulong> acknowledged)
    {
        ArgumentNullException.ThrowIfNull(acknowledged);
        if (_onWrapUpAcked is not null)
        {
            throw new InvalidOperationException(
                "The executor-completion acknowledgement notification is by now bound");
        }
        _onWrapUpAcked = acknowledged;
    }

    internal void RestartSess()
    {
        Live();
        Wipe();
    }

    private SimOperation RentOp()
    {
        if (_opReservoir.Count is 0)
            return new SimOperation();
        SimOperation pooled = _opReservoir.Pop();
        pooled.ResetAllFieldsToDefault();
        return pooled;
    }

    private void YieldOp(SimOperation op)
    {
        if (op.InPool)
        {
            throw new InvalidOperationException(
                "Operation was by now retired to the pool - a double-retire " +
                "without an intervening rent would duplicate it in the pool " +
                "stack.");
        }
        if (_opReservoir.Count >= UpperPooledOps)
            return;
        op.InPool = true;
        _opReservoir.Push(op);
    }

    private void Wipe()
    {
        _ops.Clear();
        _shelvedByChamberEpoch.Clear();
        _shelvedBinOrdering.Clear();
        _shelvedUnboundByChamber.Clear();
        _shelvedUnboundOrdering.Clear();
        _lostBy.Clear();
        _lostHeap.Clear();
        _lostHeapOrdinal.Clear();
        _linedCarriers.Clear();
        _loadingAuthorities.Clear();
        _wrapUpWatches.Clear();
        _ackedCompletions.Clear();
        _lulls.Clear();
        _projFifo.Clear();
        _expiredLost.Clear();
        _expiredLostJoints.Clear();
        _opReservoir.Clear();
    }

    private bool OpHolds(SimOperation op)
    {
        return _ops.TryGetValue(op.Key, out SimOperation? latest)
        && ReferenceEquals(latest, op)
        && OpConsistent(op);
    }

    private bool OpConsistent(SimOperation op)
    {
        return _actors.SessionLifetimeVersion == op.SessionLifetimeVersion
        && _actors.IsCurrent(op.Record)
        && op.Record.Key == op.Key
        && (op.Body is null
            || ReferenceEquals(op.Record.KineticBody, op.Body))
        && op.Record.PositionAuthorityVersion
            == op.PositionAuthorityVersion
        && op.Record.SpatialAuthorityVersion
            == op.SpatialAuthorityVersion
        && op.Record.PlacementCommitVersion
            == op.PlacementCommitVersion;
    }

    private bool OpHoldsForTicket(
        SimActorKey tag,
        in SimActorPlacementTicket grabbedTicket,
        [NotNullWhen(true)] out SimOperation? op)
    {
        if (_ops.TryGetValue(tag, out SimOperation? latest)
            && latest.Token == grabbedTicket
            && OpConsistent(latest))
        {
            op = latest;
            return true;
        }
        op = null;
        return false;
    }

    private bool VelHolds(SimOperation op)
    {
        return VelHolds(
            op.SourceVelocityAuthorityVersion,
            op.Record);
    }

    private static bool VelHolds(
        ulong srcVelArbiterVer,
        SimActorRecord capture)
    {
        return srcVelArbiterVer is 0UL
        || capture.VelArbiterVer == srcVelArbiterVer;
    }

    private static StagingAuthority LoadingArbiterOf(
        SimOperation op,
        in ObjectCreation.RemotePosition approvedLocus,
        bool readied,
        in SimSetPositionDirective readiedDirective = default)
    {
        return new(
            op.Token.OperationId,
            approvedLocus,
            PrepareChartOf(op.Record),
            op.Record.PositionAuthorityVersion,
            op.Record.VelArbiterVer,
            op.Record.PhaseArbiterVer,
            op.Record.VectorArbiterVer,
            op.Record.ObjRefDscArbiterVer,
            op.Record.BuildIntegrationVersion,
            op.Record.KineticsPhaseAlterationVer,
            readied,
            readiedDirective);
    }

    private static uint PrepareChartOf(SimActorRecord capture)
    {
        return capture.Snapshot.Physics?.SetupTableId
            ?? capture.Snapshot.SetupTableId
            ?? 0u;
    }

    private static ObjectCreation.RemotePosition WireLocusOf(
        uint chamberIdent,
        Vector3 chamberOwn,
        Quaternion facing)
    {
        return new(
            chamberIdent,
            chamberOwn.X,
            chamberOwn.Y,
            chamberOwn.Z,
            facing.W,
            facing.X,
            facing.Y,
            facing.Z);
    }

    private static bool LoadingArbiterHolds(
        SimOperation op,
        in StagingAuthority arbiter)
    {
        return op.Record.PositionAuthorityVersion
            == arbiter.PositionAuthorityVersion
        && op.Record.VelArbiterVer
            == arbiter.VelocityAuthorityVersion
        && op.Record.PhaseArbiterVer
            == arbiter.StateAuthorityVersion
        && op.Record.VectorArbiterVer
            == arbiter.VectorAuthorityVersion
        && op.Record.ObjRefDscArbiterVer
            == arbiter.ObjDescAuthorityVersion
        && op.Record.BuildIntegrationVersion
            == arbiter.CreateIntegrationVersion
        && op.Record.KineticsPhaseAlterationVer
            == arbiter.PhysicsStateMutationVersion;
    }

    private void RebindLinedDirective(SimOperation op)
    {
        if (_loadingAuthorities.TryGetValue(
                op.Key,
                out StagingAuthority arbiter)
            && arbiter.OperationId == op.Token.OperationId
            && arbiter.Prepared)
        {
            _loadingAuthorities[op.Key] = arbiter with
            {
                PreparedCommand = op.Command,
            };
        }
    }

    private static SimSetPositionUpshot Upshot(
        SimSetPositionStatus condition,
        in PlaceOutcome outcome,
        in SimPlacementMirrorTicket proj)
    {
        return new(
            condition,
            outcome.Error,
            outcome.Residence,
            outcome.CellId,
            proj);
    }

    private static SimSetPositionUpshot Rejected(
        in KineticSetPositionRequest req)
    {
        return new(
            SimSetPositionStatus.Rejected,
            PlaceError.InvalidArguments,
            KineticResidenceVerdict.Unchanged,
            req.CellId,
            default);
    }

    private static PlaceOutcome InvalidPlace(
        in KineticSetPositionRequest req)
    {
        return new(
            PlaceError.InvalidArguments,
            KineticResidenceVerdict.Unchanged,
            req.Position,
            req.Orientation,
            req.CellId,
            req.CellLocalPosition,
            CrossCellIds: ImmutableArray<uint>.Empty,
            CollidedObjectIds: ImmutableArray<uint>.Empty);
    }

    private static bool WellFormed(
        in KineticSetPositionRequest req)
    {
        static bool Finite(Vector3 val) => float.IsFinite(val.X)
            && float.IsFinite(val.Y)
            && float.IsFinite(val.Z);

        if (!PoseValidation.IsValid(
                req.CellId,
                req.CellLocalPosition,
                req.Orientation)
            || !Finite(req.Position)
            || !Finite(req.CellLocalPosition)
            || !float.IsFinite(req.StepUpHeight)
            || !float.IsFinite(req.StepDownHeight))

            return false;
        if (!req.Spheres.IsDefaultOrEmpty)
        {
            if (!float.IsFinite(req.Scale))
                return false;
            int tally = Math.Min(req.Spheres.Length, 2);
            for (int ordinal = 0; ordinal < tally; ++ordinal)
            {
                var orb = req.Spheres[ordinal];
                if (!Finite(orb.Origin) || !float.IsFinite(orb.Radius))
                    return false;
            }
        }
        if (req.Flags.HasFlag(KineticSetPositionFlags.Line)
            && !Finite(req.Line))

            return false;
        bool scatter = req.Flags.HasFlag(KineticSetPositionFlags.Scatter)
            || req.Flags.HasFlag(KineticSetPositionFlags.RandomScatter);
        if (scatter
            && (!float.IsFinite(req.ScatterRadiusX)
                || !float.IsFinite(req.ScatterRadiusY)
                || req.ScatterAttempts > UpperSynchronousScatterAttempts))

            return false;
        return true;
    }

    private void Live() =>
        ObjectDisposedException.ThrowIf(_destroyed, this);
}
