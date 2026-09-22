using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

// A prepared missile quantum waiting for the host's acknowledgement before it is completed
internal sealed class SimMissileKineticsCommit
{
    internal required SimMissileKineticsStepper Owner { get; init; }
    internal required SimActorRecord Record { get; init; }
    internal required SimMissile Projectile { get; init; }
    internal required MissileQuantumPrep Prep { get; init; }
    internal required ulong PredictionArbiterVer { get; init; }
    internal required ulong ObjectTimerEpoch { get; init; }
    internal required Func<bool>? ExternalHolderValid { get; init; }
    internal bool Completed { get; set; }
}

internal sealed class SimMissileKineticsStepper
{
    private const float LbSpan = 192f;

    private readonly SimKineticsLedger _physics;
    private readonly MissileStepper _stepper;

    internal SimMissileKineticsStepper(SimKineticsLedger physics)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _stepper = new MissileStepper(physics.Engine);
    }

    internal bool TryCommence(SimActorRecord capture, float quantum, ulong objectTimerEpoch, Func<bool>? externalHolderValid, out SimMissileKineticsCommit seal)
    {
        ArgumentNullException.ThrowIfNull(capture);
        seal = null!;
        if (capture.Projectile is not SimMissile missile
            || !SpatialHolds(capture, missile, missile.PredictionArbiterVer, objectTimerEpoch, externalHolderValid)
            || (capture.FinalKineticsCondition & KineticStateFlags.Hidden) != 0
            || capture.WholeChamberTag is 0)

            return false;

        missile.Body.State = capture.FinalKineticsCondition;
        bool parented = capture.Snapshot.ParentGuid is not null || capture.Snapshot.Physics?.Parent is not null;
        var prep = _stepper.CommenceQuantum(missile.Body, quantum, capture.WholeChamberTag, missile.ImpactOrb, parented);
        if (!prep.Simulated || !SpatialHolds(capture, missile, missile.PredictionArbiterVer, objectTimerEpoch, externalHolderValid))
            return false;

        seal = new SimMissileKineticsCommit
        {
            Owner = this,
            Record = capture,
            Projectile = missile,
            Prep = prep,
            PredictionArbiterVer = missile.PredictionArbiterVer,
            ObjectTimerEpoch = objectTimerEpoch,
            ExternalHolderValid = externalHolderValid,
        };
        return true;
    }

    internal bool Complete(SimMissileKineticsCommit seal, int onlineMiddleX, int onlineMiddleY, Func<SimKineticsFrameCapture, bool> acknowledgeProj)
    {
        ArgumentNullException.ThrowIfNull(seal);
        ArgumentNullException.ThrowIfNull(acknowledgeProj);
        if (!ReferenceEquals(seal.Owner, this))
            throw new InvalidOperationException("A projectile-physics commit belongs to another Runtime owner");
        if (seal.Completed)
            throw new InvalidOperationException("A projectile-physics commit has by now completed");
        seal.Completed = true;

        var capture = seal.Record;
        SimMissile missile = seal.Projectile;
        ulong prediction = seal.PredictionArbiterVer;
        Func<bool>? holder = seal.ExternalHolderValid;
        if (!SpatialHolds(capture, missile, prediction, seal.ObjectTimerEpoch, holder))
            return false;

        KineticBody corpus = missile.Body;
        var verdict = _stepper.ConcludeQuantum(corpus, seal.Prep, missile.ImpactOrb, capture.OwnActorTag ?? 0u, designatedMarkIdent: 0u);
        if (!verdict.Simulated || !SpatialHolds(capture, missile, prediction, seal.ObjectTimerEpoch, holder))
            return false;

        uint chamber = verdict.CellId is not 0 ? verdict.CellId : capture.WholeChamberTag;
        corpus.SnapToChamber(chamber, corpus.Position, ChamberOwn(corpus.Position, chamber, onlineMiddleX, onlineMiddleY));

        SimKineticsFrameCapture cycle = new SimKineticsFrameCapture(corpus.Position, corpus.Orientation, chamber);
        if (!_physics.SealMissileChamber(capture, missile, prediction, chamber, holder)
            || !PersonaHolds(capture, missile, prediction, holder)
            || !acknowledgeProj(cycle)
            || !PersonaHolds(capture, missile, prediction, holder))

            return false;

        uint ownIdent = capture.OwnActorTag ?? 0u;
        if (_physics.IsSpatialMissile(capture, missile) && (capture.FinalKineticsCondition & KineticStateFlags.Hidden) == 0)
            ProxyPositionSynchronizer.Sync(_physics.Engine.ShadeObjects, ownIdent, corpus.Position, corpus.Orientation, capture.WholeChamberTag, onlineMiddleX, onlineMiddleY);
        else
            _physics.Engine.ShadeObjects.Suspend(ownIdent);

        return PersonaHolds(capture, missile, prediction, holder);
    }

    internal bool ImposeAuthoritativeVector(
        SimActorRecord capture,
        ulong anticipatedVectorArbiterVer,
        ulong anticipatedVelArbiterVer,
        Vector3 vel,
        Vector3 angularVel,
        double latestMoment,
        Func<bool>? externalHolderValid = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!TryFetchOnline(capture, externalHolderValid, out SimMissile missile)
            || capture.VectorArbiterVer != anticipatedVectorArbiterVer
            || capture.VelArbiterVer != anticipatedVelArbiterVer)

            return false;
        if ((capture.FinalKineticsCondition & KineticStateFlags.Missile) == 0 && capture.PeerMotion is not null)
            return false;
        // Non-finite vectors are accepted but ignored
        if (!Finite(vel) || !Finite(angularVel) || !double.IsFinite(latestMoment))
            return true;

        missile.DirtyPrediction();
        _ = _physics.TryCommitAuthoritativeVector(capture, missile.Body, vel, angularVel, latestMoment, externalHolderValid);
        return true;
    }

    internal bool ImposeAuthoritativePhase(
        SimActorRecord capture,
        ulong anticipatedPhaseArbiterVer,
        KineticStateFlags phase,
        double netTimer,
        int onlineMiddleX,
        int onlineMiddleY,
        Func<bool>? externalHolderValid = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!TryFetchOnline(capture, externalHolderValid, out SimMissile missile) || capture.PhaseArbiterVer != anticipatedPhaseArbiterVer)
            return false;

        missile.DirtyPrediction();
        KineticBody corpus = missile.Body;
        if (!double.IsFinite(netTimer))
            netTimer = double.IsFinite(corpus.PreviousRefreshMoment) ? corpus.PreviousRefreshMoment : 0d;

        bool wasMissile = (corpus.State & KineticStateFlags.Missile) != 0;
        corpus.State = phase;
        // Becoming a missile restarts its clock and re-seats it in its cell.
        if ((phase & KineticStateFlags.Missile) != 0 && !wasMissile)
        {
            corpus.PreviousRefreshMoment = netTimer;
            if (capture.WholeChamberTag is not 0)
                corpus.SnapToChamber(capture.WholeChamberTag, corpus.Position, ChamberOwn(corpus.Position, capture.WholeChamberTag, onlineMiddleX, onlineMiddleY));
        }
        return true;
    }

    // The record still owns this projectile and body, the prediction is unchanged, and the external
    // owner agrees
    private bool PersonaHolds(SimActorRecord capture, SimMissile missile, ulong predictionVer, Func<bool>? externalHolderValid)
    {
        return _physics.Entities.IsCurrent(capture)
        && ReferenceEquals(capture.Projectile, missile)
        && ReferenceEquals(capture.KineticBody, missile.Body)
        && missile.PredictionArbiterVer == predictionVer
        && (externalHolderValid?.Invoke() ?? true);
    }

    // Identity plus spatial root status and an unchanged object clock
    private bool SpatialHolds(SimActorRecord capture, SimMissile missile, ulong predictionVer, ulong timerEpoch, Func<bool>? externalHolderValid)
    {
        return _physics.IsSpatialMissile(capture, missile)
        && capture.ObjectTimerEpoch == timerEpoch
        && PersonaHolds(capture, missile, predictionVer, externalHolderValid);
    }

    private bool TryFetchOnline(SimActorRecord capture, Func<bool>? externalHolderValid, out SimMissile missile)
    {
        if (_physics.Entities.IsCurrent(capture)
            && capture.Projectile is SimMissile online
            && ReferenceEquals(capture.KineticBody, online.Body)
            && (externalHolderValid?.Invoke() ?? true))
        {
            missile = online;
            return true;
        }
        missile = null!;
        return false;
    }

    private static Vector3 ChamberOwn(Vector3 realm, uint chamberIdent, int onlineMiddleX, int onlineMiddleY)
    {
        int bx = (int)((chamberIdent >> 24) & 0xFFu);
        int by = (int)((chamberIdent >> 16) & 0xFFu);
        return realm - new Vector3((bx - onlineMiddleX) * LbSpan, (by - onlineMiddleY) * LbSpan, 0f);
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
