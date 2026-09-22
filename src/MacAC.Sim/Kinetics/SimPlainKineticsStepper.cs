using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;
using MacAC.Dat;

namespace MacAC.Sim.Kinetics;

internal readonly record struct SimKineticsFrameCapture(Vector3 Position, Quaternion Orientation, uint FullCellId);

// A stepped frame waiting for the host to acknowledge it before the cell and proxy are committed
internal sealed class SimPlainKineticsCommit
{
    internal required SimPlainKineticsStepper Owner { get; init; }
    internal required SimActorRecord Record { get; init; }
    internal required KineticBody Body { get; init; }
    internal required ulong ObjectTimerEpoch { get; init; }
    internal required bool CycleAltered { get; init; }
    internal required Func<bool>? ExternalHolderValid { get; init; }
    internal bool Completed { get; set; }
    internal SimKineticsFrameCapture Capture { get; init; }
}

internal sealed class SimPlainKineticsStepper(SimKineticsLedger physics)
{
    private const float LowerLocateRadius = 0.05f;

    private readonly SimKineticsLedger _physics = physics ?? throw new ArgumentNullException(nameof(physics));

    // Who the step belongs to; every stage re-checks that nothing changed underneath it
    private readonly struct Hold(SimPlainKineticsStepper holder, SimActorRecord capture, KineticBody corpus, ulong timerEpoch, Func<bool>? externalHolderValid)
    {
        public readonly SimActorRecord Record = capture;
        public readonly KineticBody Body = corpus;
        public readonly ulong TimerEpoch = timerEpoch;
        public readonly Func<bool>? ExternalHolderValid = externalHolderValid;

        public bool Holds =>
            holder._physics.IsSpatialTrunk(Record)
            && Record.ObjectTimerEpoch == TimerEpoch
            && ReferenceEquals(Record.KineticBody, Body)
            && Record.PeerMotion is null
            && (ExternalHolderValid?.Invoke() ?? true);
    }

    private static bool IsAvatarOid(uint oid) => (oid & 0xFF000000u) == 0x50000000u;

    internal bool TryCommence(
        SimActorRecord capture,
        Pose trunkCycle,
        float objectScaling,
        float quantum,
        float radius,
        float height,
        ulong objectTimerEpoch,
        AnimSequencer? scheduler,
        Action<uint, AnimSequencer> grabAnimTaps,
        Func<bool>? externalHolderValid,
        out SimPlainKineticsCommit seal,
        ImmutableArray<PackedContactSphere> orbRoster = default,
        float orbScaling = 1f,
        float hopUpHeight = 0.4f,
        float hopDownHeight = 0.4f,
        MoverState carrierPvpPhase = MoverState.None)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(trunkCycle);
        ArgumentNullException.ThrowIfNull(grabAnimTaps);
        seal = null!;
        if (capture.KineticBody is not { } corpus)
            return false;
        var grip = new Hold(this, capture, corpus, objectTimerEpoch, externalHolderValid);
        if (!grip.Holds)
            return false;

        corpus.State = capture.FinalKineticsCondition;
        Vector3 fromLocus = corpus.Position;
        Quaternion fromFacing = corpus.Orientation;
        bool wasInLink = corpus.InContact;
        bool wasOnPassable = corpus.OnWalkable;

        // Root motion from the animation moves the body before integration
        Vector3 contender = fromLocus;
        if (corpus.OnWalkable && trunkCycle.Origin != Vector3.Zero)
            contender += Vector3.Transform(trunkCycle.Origin * objectScaling, fromFacing);
        Quaternion facing = fromFacing;
        if (!trunkCycle.Orientation.IsIdentity)
            facing = PoseOps.AssignSpin(contender, fromFacing, fromFacing * trunkCycle.Orientation);

        corpus.AssignCycleInLatestChamber(contender, facing);
        corpus.calc_acceleration();
        corpus.RefreshKineticsInternal(quantum);
        corpus.AssignCycleInLatestChamber(corpus.Position, corpus.Orientation);

        if (scheduler is not null)
        {
            uint ownIdent = capture.OwnActorTag
                ?? throw new InvalidOperationException($"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} has no local identity");
            grabAnimTaps(ownIdent, scheduler);
        }
        if (!grip.Holds)
            return false;

        Vector3 integrated = corpus.Position;
        uint srcChamber = capture.WholeChamberTag;
        uint settledChamber = srcChamber;
        bool cycleAltered = integrated != fromLocus || corpus.Orientation != fromFacing;

        bool worthResolving = integrated != fromLocus && srcChamber != 0 && radius >= LowerLocateRadius && _physics.Engine.LandblockTally > 0;
        corpus.StashedVel = Vector3.Zero;
        if (worthResolving)
        {
            MoverState carrier = (IsAvatarOid(capture.ServerGuid) ? MoverState.IsPlayer | MoverState.EdgeSlide : MoverState.EdgeSlide) | carrierPvpPhase;
            ResolveVerdict settled = _physics.Engine.ResolveWithTransition(
                fromLocus,
                integrated,
                srcChamber,
                radius,
                height,
                hopUpHeight: hopUpHeight,
                hopDownHeight: hopDownHeight,
                isOnTerrain: wasOnPassable,
                corpus: corpus,
                carrierFlagSet: carrier,
                movingActorIdent: capture.OwnActorTag ?? 0u,
                orbRoster: orbRoster,
                orbScaling: orbScaling);

            if (settled.Ok)
            {
                settledChamber = settled.CellId != 0 ? settled.CellId : srcChamber;
                corpus.SealChangeoverLocus(settledChamber, settled.Position);
                KineticObjUpdate.SealSetLocusChangeover(
                    corpus, settled.InContact, settled.OnWalkable, settled.CollisionNormalValid, settled.CollisionNormal, wasInLink, wasOnPassable);
                corpus.StashedVel = quantum > 0f ? (corpus.Position - fromLocus) / quantum : Vector3.Zero;
            }
        }

        if (!grip.Holds)
            return false;

        seal = new SimPlainKineticsCommit
        {
            Owner = this,
            Record = capture,
            Body = corpus,
            ObjectTimerEpoch = objectTimerEpoch,
            CycleAltered = cycleAltered,
            ExternalHolderValid = externalHolderValid,
            Capture = new SimKineticsFrameCapture(corpus.Position, corpus.Orientation, settledChamber),
        };
        return true;
    }

    internal bool Complete(SimPlainKineticsCommit seal, int onlineMiddleX, int onlineMiddleY, Func<SimKineticsFrameCapture, bool> acknowledgeProj)
    {
        ArgumentNullException.ThrowIfNull(seal);
        ArgumentNullException.ThrowIfNull(acknowledgeProj);
        if (!ReferenceEquals(seal.Owner, this))
            throw new InvalidOperationException("An ordinary-physics commit belongs to another Runtime owner");
        if (seal.Completed)
            throw new InvalidOperationException("An ordinary-physics commit has by now completed");
        seal.Completed = true;

        var grip = new Hold(this, seal.Record, seal.Body, seal.ObjectTimerEpoch, seal.ExternalHolderValid);
        if (!grip.Holds
            || !_physics.SealPlainChamber(seal.Record, seal.Body, seal.ObjectTimerEpoch, seal.Capture.FullCellId, seal.ExternalHolderValid)
            || !acknowledgeProj(seal.Capture)
            || !grip.Holds)
        {
            return false;
        }

        if (seal.CycleAltered && seal.Record.WholeChamberTag != 0)
        {
            ProxyPositionSynchronizer.Sync(
                _physics.Engine.ShadeObjects,
                seal.Record.OwnActorTag ?? 0u,
                seal.Body.Position,
                seal.Body.Orientation,
                seal.Record.WholeChamberTag,
                onlineMiddleX,
                onlineMiddleY);
        }
        return grip.Holds;
    }
}
