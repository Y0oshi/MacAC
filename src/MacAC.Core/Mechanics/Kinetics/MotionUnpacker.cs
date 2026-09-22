using System.Numerics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class MotionUnpacker : IMotionDoneTap
{
    public const float StrollAnimPace = 3.11999989f;
    public const float ExecAnimPace = 4.0f;
    public const float SidestepAnimPace = 1.25f;
    public const float LeapVzEpsilon = 0.000199999995f;
    public const float DefaultLeapVz = 10.0f;
    public const float UpperLeapReach = 1.0f;

    public const float BackwardsFactor = 0.649999976f;

    public const float ExecPivotFactor = 1.5f;

    public const float UpperSidestepAnimRate = 3.0f;

    public const float SidestepFactor = 0.5f;

    private const uint NonFightingStyling = 0x8000003Du;
    private const uint ActBit = 0x10000000u;
    private const uint CommsEmoteBit = 0x2000000u;
    private const int UpperQueuedActs = 6;
    private const TransientPhaseFlagSet Grounded = TransientPhaseFlagSet.Contact | TransientPhaseFlagSet.OnWalkable;

    private readonly LinkedList<MotionLink> _owed = new();

    public KineticBody? KineticsObjRef { get; set; }

    public IWeenieActor? WeenieObjRef { get; set; }

    public CrudeLocomotionPhase RawPhase;

    public DecodedMotionState InterpretedPhase;

    public float LeapReach;

    public float MyExecRate = 1.0f;

    public HeldKey CurrentHoldKey => RawPhase.CurrentHoldKey;

    public bool StandingLongJump;

    public bool IsOwnAvatar;

    public int SrvActStamp;

    public IEnumerable<MotionLink> QueuedMotions => _owed;

    public Action? UnstickFromObject { get; set; }

    public Action? InterruptCurrentMovement { get; set; }

    public Action? RemoveLinkAnimations { get; set; }

    public Action? BootstrapLocomotionCharts { get; set; }

    public Action? VerifyForCompletedMotions { get; set; }

    public bool Initted { get; set; } = true;

    public Func<Vector3>? FetchCycleVel { get; set; }

    public IDecodedMotionTap? DefaultSink { get; set; }

    public MotionUnpacker()
    {
        RawPhase = new CrudeLocomotionPhase();
        InterpretedPhase = DecodedMotionState.Default();
    }

    public MotionUnpacker(KineticBody kineticsObjRef, IWeenieActor? weenieObjRef = null) : this()
    {
        KineticsObjRef = kineticsObjRef;
        WeenieObjRef = weenieObjRef;
    }

    private bool OnTerrain
    {
        get
        {
            return KineticsObjRef is not null && (KineticsObjRef.TransientState & Grounded) == Grounded;
        }
    }

    private bool HasGravity
    {
        get
        {
            return KineticsObjRef is not null && (KineticsObjRef.State & KineticStateFlags.Gravity) != 0;
        }
    }

    private bool WeenieIsBeast => WeenieObjRef is null || WeenieObjRef.IsBeast();

    private bool WeenieIsTheAvatar => WeenieObjRef is null || WeenieObjRef.IsTheAvatar();

    public WeenieProblem PerformMovement(LocomotionPacket mvs)
    {
        LocomotionParams p = mvs.Params ?? new LocomotionParams
        {
            Speed = mvs.Speed,
            Autonomous = mvs.Autonomous,
            ModifyInterpretedState = mvs.ModifyInterpretedPhase,
            ModifyRawState = mvs.ModifyRawPhase,
        };

        WeenieProblem outcome;
        switch (mvs.Type)
        {
            case TravelKind.RawCommand:
                outcome = DoMotion(mvs.Motion, p);
                break;
            case TravelKind.InterpretedCommand:
                outcome = DoInterpretedMotion(mvs.Motion, p);
                break;
            case TravelKind.StopRawCommand:
                outcome = StopMotion(mvs.Motion, p);
                break;
            case TravelKind.StopInterpretedCommand:
                outcome = CeaseInterpretedLocomotion(mvs.Motion, p);
                break;
            case TravelKind.StopCompletely:
                outcome = StopCompletely();
                break;
            default:
                return WeenieProblem.GeneralMovementFailure;
        }

        VerifyForCompletedMotions?.Invoke();
        return outcome;
    }

    public WeenieProblem DoMotion(uint locomotion, float pace = 1.0f) => DoMotion(locomotion, new LocomotionParams { Speed = pace });

    public WeenieProblem DoMotion(uint locomotion, LocomotionParams p)
    {
        if (KineticsObjRef is null)
            return WeenieProblem.NoPhysicsObject;

        uint asAsked = locomotion;
        float pace = p.Speed;

        if (p.CancelMoveTo) // bitfield high-byte sign bit
            InterruptCurrentMovement?.Invoke();
        if (p.SetHoldKey) // bitfield & 0x800
            SetHoldKey(p.GripTagToEnact, p.CancelMoveTo);

        adjust_motion(ref locomotion, ref pace, p.GripTagToEnact);

        if (InterpretedPhase.LatestStyle != NonFightingStyling)
        {
            switch (asAsked)
            {
                case LocomotionDirective.Crouch:
                    return WeenieProblem.CrouchInCombatStance;   // 0x3f
                case LocomotionDirective.Sitting:
                    return WeenieProblem.SitInCombatStance;      // 0x40
                case LocomotionDirective.Sleeping:
                    return WeenieProblem.SleepInCombatStance;    // 0x41
            }
            if ((asAsked & CommsEmoteBit) is not 0)
                return WeenieProblem.ChatEmoteOutsideNonCombat; // 0x42
        }

        if (IsAct(asAsked) && InterpretedPhase.FetchCountActs() >= UpperQueuedActs)
            return WeenieProblem.ActionDepthExceeded; // 0x45

        // The interpreted call gets fresh defaults plus the adjusted speed and hold-key.
        LocomotionParams own = new LocomotionParams { Speed = pace, GripTagToEnact = p.GripTagToEnact };
        WeenieProblem outcome = DoInterpretedMotion(locomotion, own);
        if (outcome == WeenieProblem.None && p.ModifyRawState) // bitfield & 0x2000
            RawPhase.ImposeMotion(asAsked, p);
        return outcome;
    }

    public WeenieProblem StopMotion(uint locomotion) => StopMotion(locomotion, new LocomotionParams());

    public WeenieProblem StopMotion(uint locomotion, LocomotionParams p)
    {
        if (KineticsObjRef is null)
            return WeenieProblem.NoPhysicsObject;

        uint asAsked = locomotion;
        float pace = p.Speed;

        if (p.CancelMoveTo)
            InterruptCurrentMovement?.Invoke();

        adjust_motion(ref locomotion, ref pace, p.GripTagToEnact);

        LocomotionParams own = new LocomotionParams { Speed = pace, GripTagToEnact = p.GripTagToEnact };
        WeenieProblem outcome = CeaseInterpretedLocomotion(locomotion, own);
        if (outcome == WeenieProblem.None && p.ModifyRawState)
            RawPhase.DeleteLocomotion(asAsked);
        return outcome;
    }

    public WeenieProblem StopCompletely()
    {
        if (KineticsObjRef is null)
            return WeenieProblem.NoPhysicsObject;

        InterruptCurrentMovement?.Invoke();

        WeenieProblem leapCapture = LocomotionAllowsLeap(InterpretedPhase.ForwardCommand);

        RawPhase.ForwardCommand = LocomotionDirective.Ready;
        RawPhase.ForwardSpeed = 1.0f;
        RawPhase.SidestepDirective = 0;
        RawPhase.TurnDirective = 0;

        InterpretedPhase.ForwardCommand = LocomotionDirective.Ready;
        InterpretedPhase.ForwardSpeed = 1.0f;
        InterpretedPhase.FlankHopDirective = 0;
        InterpretedPhase.PivotDirective = 0;

        DefaultSink?.StopCompletely();
        KineticsObjRef.set_velocity(Vector3.Zero);
        AttachToFifo(ctxIdent: 0, LocomotionDirective.Ready, (uint)leapCapture);
        PruneLinksIfOutOfRealm();
        return WeenieProblem.None;
    }

    public void adjust_motion(ref uint locomotion, ref float pace, HeldKey gripTag)
    {
        if (!WeenieIsBeast)
            return;

        switch (locomotion)
        {
            case LocomotionDirective.ExecAhead:
                return; // already normalized; no scale, no hold-key path.
            case LocomotionDirective.StrollBackward:
                locomotion = LocomotionDirective.WalkForward;
                pace *= -BackwardsFactor;
                break;
            case LocomotionDirective.PivotLeft:
                locomotion = LocomotionDirective.TurnRight;
                pace *= -1f;
                break;
            case LocomotionDirective.FlankHopLeft:
                locomotion = LocomotionDirective.FlankHopRight;
                pace *= -1f;
                break;
        }

        if (locomotion == LocomotionDirective.FlankHopRight)
            pace *= SidestepFactor * (StrollAnimPace / SidestepAnimPace);

        if (gripTag == HeldKey.Invalid)
            gripTag = CurrentHoldKey;
        if (gripTag == HeldKey.Run)
            apply_run_to_command(ref locomotion, ref pace);
    }

    public void apply_run_to_command(ref uint locomotion, ref float pace)
    {
        float paceMod = WeenieObjRef is null ? 1.0f : ExecuteRate();
        switch (locomotion)
        {
            case LocomotionDirective.WalkForward:
                if (pace > 0f)
                    locomotion = LocomotionDirective.ExecAhead;
                pace *= paceMod;
                break;
            case LocomotionDirective.TurnRight:
                pace *= ExecPivotFactor;
                break;
            case LocomotionDirective.FlankHopRight:
                pace *= paceMod;
                if (MathF.Abs(pace) > UpperSidestepAnimRate)
                    pace = pace > 0f ? UpperSidestepAnimRate : -UpperSidestepAnimRate;
                break;
        }
    }

    public Vector3 get_state_velocity()
    {
        Vector3 vel = Vector3.Zero;

        if (InterpretedPhase.FlankHopDirective == LocomotionDirective.FlankHopRight)
            vel.X = SidestepAnimPace * InterpretedPhase.FlankHopPace;

        Vector3? cycle = FetchCycleVel?.Invoke();
        bool cycleDrivesAhead = cycle.HasValue && MathF.Abs(cycle.Value.Y) > float.Epsilon;
        switch (InterpretedPhase.ForwardCommand)
        {
            case LocomotionDirective.WalkForward:
                vel.Y = cycleDrivesAhead ? cycle!.Value.Y : StrollAnimPace * InterpretedPhase.ForwardSpeed;
                break;
            case LocomotionDirective.ExecAhead:
                vel.Y = cycleDrivesAhead ? cycle!.Value.Y : ExecAnimPace * InterpretedPhase.ForwardSpeed;
                break;
        }

        float upperPace = ExecAnimPace * ExecuteRate();
        float length = vel.Length();
        if (length > upperPace && length > 0f)
            vel = Vector3.Normalize(vel) * upperPace;
        return vel;
    }

    public float FetchUpperPace() => ExecAnimPace * WeenieExecRateOrOne();

    public float FetchAdjustedUpperPace()
    {
        return InterpretedPhase.ForwardCommand == LocomotionDirective.ExecAhead
            ? InterpretedPhase.ForwardSpeed * ExecAnimPace
            : WeenieExecRateOrOne();
    }

    public void AttachToFifo(uint ctxIdent, uint locomotion, uint leapProblemCode) =>
        _owed.AddLast(new MotionLink(ctxIdent, locomotion, leapProblemCode));

    public bool MotionsQueued() => _owed.First is not null;

    public void MotionDone(uint locomotion, bool success)
    {
        if (KineticsObjRef is null || _owed.First is null)
            return;
        RetireFront();
    }

    public void ProcessExitWorld()
    {
        while (_owed.First is not null)
            RetireFront();
    }

    public bool IsStandingStill()
    {
        return OnTerrain
        && InterpretedPhase.ForwardCommand == LocomotionDirective.Ready
        && InterpretedPhase.FlankHopDirective is 0
        && InterpretedPhase.PivotDirective is 0;
    }

    public void EnterDefaultState()
    {
        RawPhase = new CrudeLocomotionPhase();
        InterpretedPhase = DecodedMotionState.Default();
        BootstrapLocomotionCharts?.Invoke();
        AttachToFifo(ctxIdent: 0, LocomotionDirective.Ready, leapProblemCode: 0);
        Initted = true;
        LeaveGround();
    }

    public WeenieProblem DoInterpretedMotion(uint locomotion, LocomotionParams p) => DoInterpretedLocomotion(locomotion, p, DefaultSink);

    public WeenieProblem CeaseInterpretedLocomotion(uint locomotion, LocomotionParams p) => HaltInterpretedLocomotion(locomotion, p, DefaultSink);

    public bool contact_allows_move(uint locomotion)
    {
        if (KineticsObjRef is null)
            return false;

        // Turns are always allowed; so are falling and dying
        if (locomotion > 0x40000015u)
        {
            if (locomotion is LocomotionDirective.TurnRight or LocomotionDirective.PivotLeft)
                return true;
        }
        else if (locomotion == LocomotionDirective.Falling || locomotion == LocomotionDirective.Dead)
        {
            return true;
        }

        return !WeenieIsBeast || !HasGravity ? true : OnTerrain;
    }

    private static bool IsAct(uint locomotion) => (locomotion & ActBit) is not 0;

    // The run rate the weenie reports, else the locally tracked one
    private float ExecuteRate() => WeenieObjRef is not null && WeenieObjRef.InqExecRate(out float rate) ? rate : MyExecRate;

    // Retail's "is the object out of the world?" tail: drop link animations when it is
    private void PruneLinksIfOutOfRealm()
    {
        if (!KineticsObjRef!.InWorld)
            RemoveLinkAnimations?.Invoke();
    }

    // Like ExecuteRate, but an absent weenie means unit rate rather than the tracked one
    private float WeenieExecRateOrOne() => WeenieObjRef is null ? 1.0f : ExecuteRate();

    // Retires the head of the queue, popping its action from both states if it was one
    private void RetireFront()
    {
        var front = _owed.First!;
        if (IsAct(front.Value.Motion))
        {
            UnstickFromObject?.Invoke();
            InterpretedPhase.DropAct();
            RawPhase.DeleteAct();
        }
        _owed.RemoveFirst();
    }

    // A standing long jump freezes locomotion changes to state-only updates
    private bool FrozenByStandingLeap(uint locomotion)
    {
        return StandingLongJump
        && locomotion is LocomotionDirective.WalkForward or LocomotionDirective.ExecAhead or LocomotionDirective.FlankHopRight;
    }

    private WeenieProblem DoInterpretedLocomotion(uint locomotion, LocomotionParams p, IDecodedMotionTap? drain)
    {
        if (KineticsObjRef is null)
            return WeenieProblem.NoPhysicsObject;

        WeenieProblem outcome;
        if (!contact_allows_move(locomotion))
        {
            // Airborne: styles and cycles still update state; actions are refused.
            if (IsAct(locomotion))
            {
                outcome = WeenieProblem.NotGrounded;
            }
            else
            {
                if (p.ModifyInterpretedState)
                    InterpretedPhase.ImposeLocomotion(locomotion, p);
                outcome = WeenieProblem.None;
            }
        }
        else if (FrozenByStandingLeap(locomotion))
        {
            // label_528440 - state-only: no dispatch, no queue.
            if (p.ModifyInterpretedState)
                InterpretedPhase.ImposeLocomotion(locomotion, p);
            return WeenieProblem.None;
        }
        else
        {
            if (locomotion == LocomotionDirective.Dead)
                RemoveLinkAnimations?.Invoke();

            if (drain?.EnactLocomotion(locomotion, p.Speed) ?? true)
            {
                AttachToFifo(p.CtxIdent, locomotion, (uint)LeapVerdictForConnect(locomotion, p));
                if (p.ModifyInterpretedState)
                    InterpretedPhase.ImposeLocomotion(locomotion, p);
                outcome = WeenieProblem.None;
            }
            else
            {
                outcome = WeenieProblem.GeneralMovementFailure;
            }
        }

        PruneLinksIfOutOfRealm();
        return outcome;
    }

    private WeenieProblem LeapVerdictForConnect(uint locomotion, LocomotionParams p)
    {
        if (p.DisableJumpDuringLink)
            return WeenieProblem.YouCantJumpFromThisPosition; // 0x48 - forced BLOCKED

        WeenieProblem verdict = LocomotionAllowsLeap(locomotion);
        if (verdict == WeenieProblem.None && !IsAct(locomotion))
            verdict = LocomotionAllowsLeap(InterpretedPhase.ForwardCommand);
        return verdict;
    }

    private WeenieProblem HaltInterpretedLocomotion(uint locomotion, LocomotionParams p, IDecodedMotionTap? drain)
    {
        if (KineticsObjRef is null)
            return WeenieProblem.NoPhysicsObject;

        WeenieProblem outcome;
        if (!contact_allows_move(locomotion) || FrozenByStandingLeap(locomotion))
        {
            if (p.ModifyInterpretedState)
                InterpretedPhase.DropLocomotion(locomotion);
            outcome = WeenieProblem.None;
        }
        else if (drain?.HaltLocomotion(locomotion) ?? true)
        {
            outcome = WeenieProblem.None;
            AttachToFifo(p.CtxIdent, LocomotionDirective.Ready, (uint)outcome);
            if (p.ModifyInterpretedState)
                InterpretedPhase.DropLocomotion(locomotion);
        }
        else
        {
            outcome = WeenieProblem.GeneralMovementFailure;
        }

        PruneLinksIfOutOfRealm();
        return outcome;
    }
}
