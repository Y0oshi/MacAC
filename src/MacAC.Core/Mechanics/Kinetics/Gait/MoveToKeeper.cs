using System.Numerics;

namespace MacAC.Mechanics.Kinetics.Gait;

public readonly record struct TargetFacts(
    uint ObjectId,
    TargetPhase Status,
    Locus TargetPosition,
    Locus InterpolatedPosition,
    uint ContextId = 0,
    float Radius = 0f,
    double Quantum = 0.0,
    Vector3 InterpolatedHeading = default,
    Vector3 Velocity = default,
    double LastUpdateTime = 0.0);

public enum TargetPhase
{
    /// <summary>0 - undefined/uninitialized.</summary>
    Undefined = 0,
    /// <summary>1 - target resolved and tracked normally.</summary>
    Ok = 1,
    /// <summary>2 - target left the world (despawned).</summary>
    ExitWorld = 2,
    /// <summary>3 - target teleported.</summary>
    Teleported = 3,
    Contained = 4,
    /// <summary>5 - target became parented (e.g. mounted/wielded).</summary>
    Parented = 5,
    /// <summary>6 - tracker timed out without an update.</summary>
    TimedOut = 6,
}

public sealed partial class MoveToKeeper
{
    private const float ArrivalConeDeg = 20f;
    private const float HeadwayRatePerSecond = 0.25f;

    private static readonly Locus PersonaLocus = new(0u, Vector3.Zero, Quaternion.Identity);

    // The callbacks a host object supplies; bundled so the keeper reads like retail's physics_obj
    // access
    private sealed record Host(
        Action StopCompletely,
        Func<Locus> Position,
        Func<float> Heading,
        Action<float, bool> SetHeading,
        Func<float> Radius,
        Func<float> Height,
        Func<bool> Contact,
        Func<bool> IsInterpolating,
        Func<Vector3> Velocity,
        Func<uint> SelfId,
        Action<uint, uint, float, double> SetTarget,
        Action ClearTarget,
        Func<double> TargetQuantum,
        Action<double> SetTargetQuantum,
        Func<double> Now);

    private readonly MotionUnpacker _lerp;
    private readonly Host _hub;
    private readonly LinkedList<ApproachNode> _plan = new();

    public Action? Unstick { get; set; }

    public Action<uint, float, float>? StickTo { get; set; }

    public Action<WeenieProblem>? RelocateToDone { get; set; }

    public Action<WeenieProblem>? RelocateToCancelled { get; set; }

    public bool HasKineticsObjRef { get; set; } = true;

    public MoveToKeeper(
        MotionUnpacker interp,
        Action stopCompletely,
        Func<Locus> getPosition,
        Func<float> getHeading,
        Action<float, bool> setHeading,
        Func<float> getOwnRadius,
        Func<float> getOwnHeight,
        Func<bool> contact,
        Func<bool> isInterpolating,
        Func<Vector3> getVelocity,
        Func<uint> getSelfId,
        Action<uint, uint, float, double> setTarget,
        Action clearTarget,
        Func<double> getTargetQuantum,
        Action<double> setTargetQuantum,
        Func<double>? curMoment = null)
    {
        _lerp = interp ?? throw new ArgumentNullException(nameof(interp));

        double fakeTimer = 0.0;
        _hub = new Host(
            stopCompletely ?? throw new ArgumentNullException(nameof(stopCompletely)),
            getPosition ?? throw new ArgumentNullException(nameof(getPosition)),
            getHeading ?? throw new ArgumentNullException(nameof(getHeading)),
            setHeading ?? throw new ArgumentNullException(nameof(setHeading)),
            getOwnRadius ?? throw new ArgumentNullException(nameof(getOwnRadius)),
            getOwnHeight ?? throw new ArgumentNullException(nameof(getOwnHeight)),
            contact ?? throw new ArgumentNullException(nameof(contact)),
            isInterpolating ?? throw new ArgumentNullException(nameof(isInterpolating)),
            getVelocity ?? throw new ArgumentNullException(nameof(getVelocity)),
            getSelfId ?? throw new ArgumentNullException(nameof(getSelfId)),
            setTarget ?? throw new ArgumentNullException(nameof(setTarget)),
            clearTarget ?? throw new ArgumentNullException(nameof(clearTarget)),
            getTargetQuantum ?? throw new ArgumentNullException(nameof(getTargetQuantum)),
            setTargetQuantum ?? throw new ArgumentNullException(nameof(setTargetQuantum)),
            curMoment ?? (() => fakeTimer += 1.0 / 30.0));

        BootstrapOwnVariables();
    }

    public TravelKind TravelKindPhase { get; private set; } = TravelKind.Invalid;

    public Locus SoughtLocus { get; private set; } = PersonaLocus;

    public Locus LatestMarkLocus { get; private set; } = PersonaLocus;

    public Locus StartingLocus { get; private set; } = PersonaLocus;

    public LocomotionParams Parameters { get; private set; } = new();

    public float EarlierBearing { get; private set; }

    public float EarlierGap { get; private set; }

    public double EarlierGapMoment { get; private set; }

    public float OriginalGap { get; private set; }

    public double OriginalGapMoment { get; private set; }

    public uint FailHeadwayTally { get; private set; }

    public uint SoughtObjectIdent { get; private set; }

    public uint TopTierObjectIdent { get; private set; }

    public float SoughtObjectRadius { get; private set; }

    public float SoughtObjectHeight { get; private set; }

    public uint LatestDirective { get; private set; }

    public uint AuxDirective { get; private set; }

    public bool MovingAway { get; private set; }

    public bool Initialized { get; private set; }

    /// <summary>Read-only inspection surface (tests): the node queue in head-to-tail order.</summary>
    public IEnumerable<ApproachNode> QueuedActs => _plan;

    public void BootstrapOwnVariables()
    {
        TravelKindPhase = TravelKind.Invalid;
        Parameters.WipeFlagSet();

        RestartGapTimer();
        EarlierBearing = 0f;
        FailHeadwayTally = 0;
        LatestDirective = 0;
        AuxDirective = 0;
        MovingAway = false;
        Initialized = false;

        SoughtLocus = PersonaLocus;
        LatestMarkLocus = PersonaLocus;

        SoughtObjectIdent = 0;
        TopTierObjectIdent = 0;
        SoughtObjectRadius = 0f;
        SoughtObjectHeight = 0f;
    }

    public void Destroy()
    {
        _plan.Clear();
        BootstrapOwnVariables();
    }

    public bool IsMovingTo() => TravelKindPhase != TravelKind.Invalid;

    public WeenieProblem PerformMovement(LocomotionPacket mvs)
    {
        CancelMoveTo(WeenieProblem.ActionCancelled);
        Unstick?.Invoke();

        LocomotionParams p = mvs.Params ?? new LocomotionParams();
        switch (mvs.Type)
        {
            case TravelKind.MoveToObject:
                ShiftToObject(mvs.ObjectId, mvs.TopTierIdent, mvs.Radius, mvs.Height, p);
                break;
            case TravelKind.MoveToPosition:
                ShiftToLocus(mvs.Spot, p);
                break;
            case TravelKind.TurnToObject:
                PivotToObject(mvs.ObjectId, mvs.TopTierIdent, p);
                break;
            case TravelKind.TurnToHeading:
                RotateToBearing(p);
                break;
        }

        return WeenieProblem.None;
    }

    public void ShiftToObject(uint objectIdent, uint topTierIdent, float radius, float height, LocomotionParams p)
    {
        if (!HasKineticsObjRef)
            return;

        _hub.StopCompletely();
        StartingLocus = _hub.Position();
        SoughtObjectIdent = objectIdent;
        SoughtObjectRadius = radius;
        SoughtObjectHeight = height;
        TravelKindPhase = TravelKind.MoveToObject;
        TopTierObjectIdent = topTierIdent;
        Parameters.DuplicateFrom(p);
        Initialized = false;
        AimAt(topTierIdent);
    }

    private Vector3 Here => _hub.Position().Frame.Origin;

    public void ShiftToLocus(Locus mark, LocomotionParams p)
    {
        if (!HasKineticsObjRef)
            return;

        _hub.StopCompletely();
        LatestMarkLocus = mark;
        SoughtObjectRadius = 0f;

        float distance = FetchLatestGap();
        float toMark = BearingTo(mark.Frame.Origin);
        p.FetchDirective(distance, PivotNeeded(toMark), out uint cmd, out _, out _);
        if (cmd is not 0)
        {
            AppendPivotToBearingJoint(toMark);
            AppendRelocateToLocusJoint();
        }
        if (p.UseFinalHeading)
            AppendPivotToBearingJoint(p.WantedBearing);

        SoughtLocus = mark;
        StartingLocus = _hub.Position();
        TravelKindPhase = TravelKind.MoveToPosition;
        Parameters.DuplicateFrom(p);
        Parameters.Sticky = false;

        CommenceUpcomingJoint();
    }

    public void PivotToObject(uint objectIdent, uint topTierIdent, LocomotionParams p)
    {
        if (!HasKineticsObjRef)
            return;

        if (p.HaltCompletelyBit)
            _hub.StopCompletely();

        TravelKindPhase = TravelKind.TurnToObject;
        SoughtObjectIdent = objectIdent;
        LatestMarkLocus = WithBearing(LatestMarkLocus, p.WantedBearing);
        TopTierObjectIdent = topTierIdent;
        Parameters.DuplicateFrom(p);
        AimAt(topTierIdent);
    }

    public void RotateToBearing(LocomotionParams p)
    {
        if (!HasKineticsObjRef)
            return;

        if (p.HaltCompletelyBit)
            _hub.StopCompletely();

        Parameters.DuplicateFrom(p);
        Parameters.Sticky = false;
        SoughtLocus = WithBearing(SoughtLocus, p.WantedBearing);
        TravelKindPhase = TravelKind.TurnToHeading;

        AppendPivotToBearingJoint(p.WantedBearing);
        CommenceUpcomingJoint();
    }

    public void ProcessRefreshObjective(TargetFacts details)
    {
        if (!HasKineticsObjRef)
        {
            CancelMoveTo(WeenieProblem.NoPhysicsObject);
            return;
        }
        if (TopTierObjectIdent != details.ObjectId)
            return;

        if (Initialized)
        {
            if (details.Status != TargetPhase.Ok)
            {
                CancelMoveTo(WeenieProblem.ObjectGone);
                return;
            }
            if (TravelKindPhase == TravelKind.MoveToObject)
            {
                SoughtLocus = details.InterpolatedPosition;
                LatestMarkLocus = details.TargetPosition;
                RestartGapTimer();
            }
            return;
        }

        if (TopTierObjectIdent == _hub.SelfId())
        {
            Locus self = _hub.Position();
            SoughtLocus = self;
            LatestMarkLocus = self;
            CleanUpAndCallWeenie(WeenieProblem.None);
            return;
        }
        if (details.Status != TargetPhase.Ok)
        {
            CancelMoveTo(WeenieProblem.NoObject);
            return;
        }

        switch (TravelKindPhase)
        {
            case TravelKind.MoveToObject:
                MoveToObject_Internal(details.TargetPosition, details.InterpolatedPosition);
                break;
            case TravelKind.TurnToObject:
                TurnToObject_Internal(details.TargetPosition);
                break;
        }
    }

    public void MoveToObject_Internal(Locus mark, Locus interpolated)
    {
        if (!HasKineticsObjRef)
        {
            CancelMoveTo(WeenieProblem.NoPhysicsObject);
            return;
        }

        SoughtLocus = interpolated;
        LatestMarkLocus = mark;

        float toInterpolated = BearingTo(interpolated.Frame.Origin);
        float distance = FetchLatestGap();
        Parameters.FetchDirective(distance, PivotNeeded(toInterpolated), out uint cmd, out _, out _);
        if (cmd is not 0)
        {
            AppendPivotToBearingJoint(toInterpolated);
            AppendRelocateToLocusJoint();
        }

        if (Parameters.UseFinalHeading)
        {
            float final = toInterpolated + Parameters.WantedBearing;
            if (final >= 360f)
                final -= 360f;
            AppendPivotToBearingJoint(final);
        }

        Initialized = true;
        CommenceUpcomingJoint();
    }

    public void TurnToObject_Internal(Locus mark)
    {
        if (!HasKineticsObjRef)
        {
            CancelMoveTo(WeenieProblem.NoPhysicsObject);
            return;
        }

        LatestMarkLocus = mark;

        float soughtBearing = ApproachMath.FetchBearing(SoughtLocus.Frame.Orientation);
        float final = (BearingTo(mark.Frame.Origin) + soughtBearing) % 360f;
        if (final < 0f)
            final += 360f;

        SoughtLocus = WithBearing(SoughtLocus, final);
        AppendPivotToBearingJoint(final);

        Initialized = true;
        CommenceUpcomingJoint();
    }

    public void HitGround()
    {
        if (TravelKindPhase != TravelKind.Invalid)
            CommenceUpcomingJoint();
    }

    public void CancelMoveTo(WeenieProblem problem)
    {
        if (TravelKindPhase == TravelKind.Invalid)
            return;

        _plan.Clear();
        CleanUp();
        if (HasKineticsObjRef)
            _hub.StopCompletely();
        RelocateToCancelled?.Invoke(problem);
    }

    public void CleanUp()
    {
        if (HasKineticsObjRef)
        {
            var own = OwnParameters(withPace: false);
            if (LatestDirective is not 0)
                HaltLocomotion(LatestDirective, own);
            if (AuxDirective is not 0)
                HaltLocomotion(AuxDirective, own);
            if (TopTierObjectIdent is not 0 && TravelKindPhase != TravelKind.Invalid)
                _hub.ClearTarget();
        }

        BootstrapOwnVariables();
    }

    public void CleanUpAndCallWeenie(WeenieProblem problem)
    {
        CleanUp();
        if (HasKineticsObjRef)
            _hub.StopCompletely();
        RelocateToDone?.Invoke(problem);
    }

    // Forgets all progress so the next tick starts a fresh stall watch
    private void RestartGapTimer()
    {
        EarlierGap = float.MaxValue;
        EarlierGapMoment = _hub.Now();
        OriginalGap = float.MaxValue;
        OriginalGapMoment = _hub.Now();
    }

    private LocomotionParams OwnParameters(bool withPace = true)
    {
        return new()
        {
            CancelMoveTo = false,
            GripTagToEnact = Parameters.GripTagToEnact,
            Speed = withPace ? Parameters.Speed : 1f,
        };
    }

    // Heading from the body to to, 0..360
    private float BearingTo(Vector3 to) => ApproachMath.PlaceBearing(Here, to);

    // Retail's normalised turn from the body's heading to bearing: [0, 360) with an epsilon dead-band
    private float PivotNeeded(float bearing)
    {
        float diff = bearing - _hub.Heading();
        if (MathF.Abs(diff) < ApproachMath.Epsilon)
            diff = 0f;
        if (diff < -ApproachMath.Epsilon)
            diff += 360f;
        return diff;
    }

    private static Locus WithBearing(Locus locus, float bearingDeg)
    {
        return locus with
        {
            Frame = new CellPose(locus.Frame.Origin, ApproachMath.ApplyBearing(locus.Frame.Orientation, bearingDeg)),
        };
    }

    // Targeting yourself completes at once; anyone else is subscribed to for position updates
    private void AimAt(uint topTierIdent)
    {
        if (topTierIdent == _hub.SelfId())
        {
            CleanUp();
            _hub.StopCompletely();
        }
        else
        {
            Initialized = false;
            _hub.SetTarget(0, topTierIdent, 0.5f, 0.0);
        }
    }

    private WeenieProblem DoLocomotion(uint locomotion, LocomotionParams p)
    {
        if (!HasKineticsObjRef)
            return WeenieProblem.NoPhysicsObject;

        float pace = p.Speed;
        _lerp.adjust_motion(ref locomotion, ref pace, p.GripTagToEnact);
        p.Speed = pace;
        return _lerp.DoInterpretedMotion(locomotion, p);
    }

    private WeenieProblem HaltLocomotion(uint locomotion, LocomotionParams p)
    {
        if (!HasKineticsObjRef)
            return WeenieProblem.NoPhysicsObject;

        float pace = p.Speed;
        _lerp.adjust_motion(ref locomotion, ref pace, p.GripTagToEnact);
        p.Speed = pace;
        return _lerp.CeaseInterpretedLocomotion(locomotion, p);
    }

    // Stops the auxiliary turn, if one is running
    private void DiscardAuxPivot(LocomotionParams own)
    {
        if (AuxDirective is 0)
            return;
        HaltLocomotion(AuxDirective, own);
        AuxDirective = 0;
    }
}
