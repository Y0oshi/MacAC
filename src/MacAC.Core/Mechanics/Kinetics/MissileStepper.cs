using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public readonly record struct MissileContactSphere(
    Vector3 SetupLocalOrigin,
    float SetupRadius,
    float ObjectScale = 1f)
{
    public Vector3 LocalOrigin => SetupLocalOrigin * ObjectScale;
    public float Radius => SetupRadius * ObjectScale;

    public bool IsValid
    {
        get
        {
            Vector3 origin = LocalOrigin;
            float radius = Radius;
            return float.IsFinite(SetupRadius) && SetupRadius > 0f
                && float.IsFinite(ObjectScale) && ObjectScale > 0f
                && float.IsFinite(radius) && radius > KineticConstants.EPSILON
                && float.IsFinite(origin.X)
                && float.IsFinite(origin.Y)
                && float.IsFinite(origin.Z);
        }
    }
}

public readonly record struct MissileAdvanceOutcome(
    uint CellId,
    int QuantumCount,
    bool Simulated,
    bool CollisionNormalValid,
    Vector3 CollisionNormal,
    bool TransitionOk)
{
    internal static MissileAdvanceOutcome Idle(uint chamberIdent, bool changeoverOk) =>
        new(chamberIdent, 0, false, false, default, changeoverOk);
}

public readonly record struct MissileQuantumPrep(
    uint CellId,
    float Quantum,
    bool Simulated,
    bool RequiresTransition,
    Vector3 BeginPosition,
    Quaternion BeginOrientation,
    Locus BeginCellPosition,
    bool BeginInWorld,
    MissileQuantumDynamics BeginDynamics,
    Vector3 CandidatePosition,
    Quaternion CandidateOrientation,
    MissileQuantumDynamics CandidateDynamics,
    bool PreviousContact,
    bool PreviousOnWalkable)
{
    internal static MissileQuantumPrep Skipped(uint chamberIdent, float quantum)
    {
        return new(
        CellId: chamberIdent,
        Quantum: quantum,
        Simulated: false,
        RequiresTransition: false,
        BeginPosition: default,
        BeginOrientation: default,
        BeginCellPosition: default,
        BeginInWorld: false,
        BeginDynamics: default,
        CandidatePosition: default,
        CandidateOrientation: default,
        CandidateDynamics: default,
        PreviousContact: false,
        PreviousOnWalkable: false);
    }
}

/// <summary>The integrator's mutable outputs, captured so a quantum can be replayed or undone.</summary>
public readonly record struct MissileQuantumDynamics(
    Vector3 Velocity,
    Vector3 CachedVelocity,
    Vector3 Acceleration,
    Vector3 Omega,
    TransientPhaseFlagSet TransientState,
    double LastUpdateTime)
{
    internal static MissileQuantumDynamics Of(KineticBody corpus)
    {
        return new(
        corpus.Velocity,
        corpus.StashedVel,
        corpus.Acceleration,
        corpus.Omega,
        corpus.TransientState,
        corpus.PreviousRefreshMoment);
    }

    internal void ImposeTo(KineticBody corpus)
    {
        corpus.Velocity = Velocity;
        corpus.StashedVel = CachedVelocity;
        corpus.Acceleration = Acceleration;
        corpus.Omega = Omega;
        corpus.TransientState = TransientState;
        corpus.PreviousRefreshMoment = LastUpdateTime;
    }
}

public sealed class MissileStepper(KineticEngine physics)
{
    private readonly KineticEngine _physics = physics ?? throw new ArgumentNullException(nameof(physics));

    public MissileAdvanceOutcome Advance(
        KineticBody corpus,
        double currentTime,
        uint chamberIdent,
        MissileContactSphere orb,
        uint movingActorIdent,
        uint designatedMarkIdent = 0,
        bool isParented = false)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (!double.IsFinite(currentTime))
            throw new ArgumentOutOfRangeException(nameof(currentTime), currentTime, "The game clock has to be finite");
        if (!double.IsFinite(corpus.PreviousRefreshMoment))
            throw new InvalidOperationException("The projectile's prior update timestamp has to be finite");

        if (!orb.IsValid)
            return MissileAdvanceOutcome.Idle(chamberIdent, changeoverOk: false);

        if (Dormant(corpus, chamberIdent, isParented))
        {
            corpus.TransientState &= ~TransientPhaseFlagSet.Active;
            return MissileAdvanceOutcome.Idle(chamberIdent, changeoverOk: true);
        }
        if (!corpus.IsActive)
            return MissileAdvanceOutcome.Idle(chamberIdent, changeoverOk: true);

        double owed = currentTime - corpus.PreviousRefreshMoment;
        if (owed <= KineticConstants.EPSILON || owed > KineticBody.HugeQuantum)
        {
            corpus.PreviousRefreshMoment = currentTime;
            return MissileAdvanceOutcome.Idle(chamberIdent, changeoverOk: true);
        }

        var count = new Tally(chamberIdent);
        double simulated = corpus.PreviousRefreshMoment;
        while (owed > KineticBody.MaxQuantum)
        {
            simulated += KineticBody.MaxQuantum;
            AdvanceLoop(corpus, KineticBody.MaxQuantum, orb, movingActorIdent, designatedMarkIdent, ref count);
            owed -= KineticBody.MaxQuantum;
        }
        if (owed > KineticBody.LowerQuantum)
        {
            simulated += owed;
            AdvanceLoop(corpus, (float)owed, orb, movingActorIdent, designatedMarkIdent, ref count);
        }

        corpus.PreviousRefreshMoment = simulated;
        return new MissileAdvanceOutcome(
            count.CellId,
            count.Quanta,
            count.Quanta is not 0,
            count.CollisionNormalValid,
            count.CollisionNormal,
            count.ChangeoverOk);
    }

    public MissileAdvanceOutcome ProgressQuantum(
        KineticBody corpus,
        float quantum,
        uint chamberIdent,
        MissileContactSphere orb,
        uint movingActorIdent,
        uint designatedMarkIdent = 0,
        bool isParented = false)
    {
        var prep = CommenceQuantum(corpus, quantum, chamberIdent, orb, isParented);
        return ConcludeQuantum(corpus, prep, orb, movingActorIdent, designatedMarkIdent);
    }

    // Running totals across the quanta of one Advance
    private struct Tally(uint chamberIdent)
    {
        public uint CellId = chamberIdent;
        public int Quanta;
        public bool CollisionNormalValid;
        public Vector3 CollisionNormal;
        public bool ChangeoverOk = true;

        public void Absorb(in MissileAdvanceOutcome verdict)
        {
            ++Quanta;
            if (verdict.CellId is not 0)
                CellId = verdict.CellId;
            if (verdict.CollisionNormalValid)
            {
                CollisionNormalValid = true;
                CollisionNormal = verdict.CollisionNormal;
            }
            ChangeoverOk &= verdict.TransitionOk;
        }
    }

    public MissileQuantumPrep CommenceQuantum(
        KineticBody corpus,
        float quantum,
        uint chamberIdent,
        MissileContactSphere orb,
        bool isParented = false)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (!float.IsFinite(quantum) || quantum <= KineticConstants.EPSILON || quantum > KineticBody.MaxQuantum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantum),
                quantum,
                "An admitted object quantum has to be finite and no larger than MaxQuantum");
        }

        if (!orb.IsValid)
            return MissileQuantumPrep.Skipped(chamberIdent, quantum);

        if (Dormant(corpus, chamberIdent, isParented))
        {
            corpus.TransientState &= ~TransientPhaseFlagSet.Active;
            return MissileQuantumPrep.Skipped(chamberIdent, quantum);
        }
        if (!corpus.IsActive)
            return MissileQuantumPrep.Skipped(chamberIdent, quantum);

        // Remember everything the integrator will touch, then let it run
        Vector3 commenceLocus = corpus.Position;
        Quaternion commenceFacing = corpus.Orientation;
        Locus commenceChamber = corpus.CellPosition;
        bool commenceInRealm = corpus.InWorld;
        var commenceDynamics = MissileQuantumDynamics.Of(corpus);
        bool earlierLink = corpus.InContact;
        bool earlierOnPassable = corpus.OnWalkable;

        corpus.calc_acceleration();
        corpus.RefreshKineticsInternal(quantum);

        Vector3 contenderLocus = corpus.Position;
        Quaternion contenderFacing = corpus.Orientation;
        Vector3 displacement = contenderLocus - commenceLocus;
        bool moved = displacement != Vector3.Zero;
        if (moved && (corpus.State & KineticStateFlags.AlignPath) != 0)
            contenderFacing = CanonPoseMath.AssignVectorBearing(contenderFacing, displacement);

        if (moved)
        {
            corpus.Position = commenceLocus;
            corpus.Orientation = commenceFacing;
        }
        else
        {
            CommenceQuantumBranch(corpus, contenderFacing, quantum);
        }

        // Hand back the candidate, but leave the body exactly as we found it.
        var contenderDynamics = MissileQuantumDynamics.Of(corpus);
        ReinstateCycle(corpus, commenceLocus, commenceFacing, commenceChamber, commenceInRealm);
        commenceDynamics.ImposeTo(corpus);

        return new MissileQuantumPrep(
            chamberIdent,
            quantum,
            Simulated: true,
            RequiresTransition: moved,
            commenceLocus,
            commenceFacing,
            commenceChamber,
            commenceInRealm,
            commenceDynamics,
            contenderLocus,
            contenderFacing,
            contenderDynamics,
            earlierLink,
            earlierOnPassable);
    }

    private void CommenceQuantumBranch(KineticBody corpus, Quaternion contenderFacing, float quantum)
    {
        corpus.StashedVel = Vector3.Zero;
        corpus.Orientation = contenderFacing;
        corpus.PreviousRefreshMoment += quantum;
    }

    public MissileAdvanceOutcome ConcludeQuantum(
        KineticBody corpus,
        in MissileQuantumPrep prep,
        MissileContactSphere orb,
        uint movingActorIdent,
        uint designatedMarkIdent = 0)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (!prep.Simulated)
            return MissileAdvanceOutcome.Idle(prep.CellId, changeoverOk: true);

        prep.CandidateDynamics.ImposeTo(corpus);
        if (!prep.RequiresTransition)
        {
            corpus.AssignCycleInLatestChamber(prep.CandidatePosition, prep.CandidateOrientation);
            return new MissileAdvanceOutcome(prep.CellId, 1, true, false, default, true);
        }

        MoverState carrierFlagSet = (corpus.State & KineticStateFlags.PathClipped) != 0 ? MoverState.PathClipped : MoverState.None;
        var settled = _physics.ResolveWithTransition(
            prep.BeginPosition,
            prep.CandidatePosition,
            prep.CellId,
            orbRadius: orb.Radius,
            orbHeight: 0f,
            hopUpHeight: KineticConstants.DefaultHopHeight,
            hopDownHeight: 0f,
            isOnTerrain: prep.PreviousOnWalkable,
            corpus: corpus,
            carrierFlagSet: carrierFlagSet,
            movingActorIdent: movingActorIdent,
            ownOrbOrigin: orb.LocalOrigin,
            commenceFacing: prep.BeginOrientation,
            finishFacing: prep.CandidateOrientation,
            designatedMarkIdent: designatedMarkIdent);

        uint chamberIdent = prep.CellId;
        bool impactNormValid = false;
        Vector3 impactNorm = default;
        if (settled.Ok)
        {
            corpus.StashedVel = (settled.Position - prep.BeginPosition) / prep.Quantum;
            corpus.Position = settled.Position;
            corpus.Orientation = settled.Orientation == default ? prep.CandidateOrientation : settled.Orientation;
            if (settled.CellId is not 0)
                chamberIdent = settled.CellId;

            KineticObjUpdate.ImposeSetLocusLink(corpus, settled.InContact, settled.OnWalkable);
            KineticObjUpdate.HandleAllCollisions(
                corpus,
                settled.CollisionNormalValid,
                settled.CollisionNormal,
                prep.PreviousContact,
                prep.PreviousOnWalkable,
                instantOnPassable: corpus.OnWalkable);
            if (settled.CollisionNormalValid)
            {
                impactNormValid = true;
                impactNorm = settled.CollisionNormal;
            }
        }
        else
        {
            corpus.AssignCycleInLatestChamber(prep.CandidatePosition, prep.CandidateOrientation);
            corpus.StashedVel = Vector3.Zero;
        }

        corpus.PreviousRefreshMoment += prep.Quantum;
        return new MissileAdvanceOutcome(chamberIdent, 1, true, impactNormValid, impactNorm, settled.Ok);
    }

    private static bool Dormant(KineticBody corpus, uint chamberIdent, bool isParented)
    {
        return isParented
        || !corpus.InWorld
        || (corpus.State & (KineticStateFlags.Frozen | KineticStateFlags.Static)) != 0
        || chamberIdent is 0;
    }

    private void AdvanceLoop(KineticBody corpus, float dt, MissileContactSphere orb, uint movingActorIdent, uint designatedMarkIdent, ref Tally count)
    {
        var prep = CommenceQuantum(corpus, dt, count.CellId, orb);
        count.Absorb(ConcludeQuantum(corpus, prep, orb, movingActorIdent, designatedMarkIdent));
    }

    private static void ReinstateCycle(KineticBody corpus, Vector3 locus, Quaternion facing, in Locus chamber, bool inRealm)
    {
        corpus.Orientation = facing;
        if (chamber.ObjCellId is not 0)
            corpus.SnapToChamber(chamber.ObjCellId, locus, chamber.Frame.Origin);
        else
            corpus.AssignCycleInLatestChamber(locus, facing);
        corpus.InWorld = inRealm;
    }
}
