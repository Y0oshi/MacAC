namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class LocomotionParams
{
    private const float Epsilon = 0.000199999995f;

    [Flags]
    private enum WireBits : uint
    {
        CanWalk = 0x1,
        CanRun = 0x2,
        CanSidestep = 0x4,
        CanWalkBackwards = 0x8,
        CanCharge = 0x10,
        FailWalk = 0x20,
        UseFinalHeading = 0x40,
        Sticky = 0x80,
        MoveAway = 0x100,
        MoveTowards = 0x200,
        UseSpheres = 0x400,
        SetHoldKey = 0x800,
        Autonomous = 0x1000,
        ModifyRawState = 0x2000,
        ModifyInterpretedState = 0x4000,
        CancelMoveTo = 0x8000,
        StopCompletely = 0x10000,
        DisableJumpDuringLink = 0x20000,
    }

    /// <summary>Mask 0x1 - default true.</summary>
    public bool CanWalk { get; set; } = true;

    /// <summary>Mask 0x2 - default true.</summary>
    public bool CanRun { get; set; } = true;

    /// <summary>Mask 0x4 - default true.</summary>
    public bool CanSidestep { get; set; } = true;

    /// <summary>Mask 0x8 - default true.</summary>
    public bool CanWalkBackwards { get; set; } = true;

    public bool CanCharge { get; set; }

    /// <summary>Mask 0x20 - default false.</summary>
    public bool FailWalk { get; set; }

    /// <summary>Mask 0x40 - default false.</summary>
    public bool UseFinalHeading { get; set; }

    /// <summary>Mask 0x80 - default false.</summary>
    public bool Sticky { get; set; }

    /// <summary>Mask 0x100 - default false.</summary>
    public bool MoveAway { get; set; }

    /// <summary>Mask 0x200 - default true.</summary>
    public bool MoveTowards { get; set; } = true;

    /// <summary>Mask 0x400 - default true.</summary>
    public bool UseSpheres { get; set; } = true;

    public bool SetHoldKey { get; set; } = true;

    public bool Autonomous { get; set; }

    public bool ModifyRawState { get; set; } = true;

    /// <summary>Mask 0x4000 - default true. Mirrors into <c>DecodedMotionState</c>.</summary>
    public bool ModifyInterpretedState { get; set; } = true;

    public bool CancelMoveTo { get; set; } = true;

    public bool HaltCompletelyBit { get; set; } = true;

    public bool DisableJumpDuringLink { get; set; }

    public float GapToObject { get; set; } = 0.6f;

    public float LowerGap { get; set; }

    public float WantedBearing { get; set; }

    public float Speed { get; set; } = 1f;

    public float FailGap { get; set; } = float.MaxValue;

    public float StrollExecThreshhold { get; set; } = 15f;

    public uint CtxIdent { get; set; }

    public HeldKey GripTagToEnact { get; set; } = HeldKey.Invalid;

    public uint ActStamp { get; set; }

    /// <summary>Overwrites every field from <paramref name="another"/>.</summary>
    public void DuplicateFrom(LocomotionParams another)
    {
        CanWalk = another.CanWalk;
        CanRun = another.CanRun;
        CanSidestep = another.CanSidestep;
        CanWalkBackwards = another.CanWalkBackwards;
        CanCharge = another.CanCharge;
        FailWalk = another.FailWalk;
        UseFinalHeading = another.UseFinalHeading;
        Sticky = another.Sticky;
        MoveAway = another.MoveAway;
        MoveTowards = another.MoveTowards;
        UseSpheres = another.UseSpheres;
        SetHoldKey = another.SetHoldKey;
        Autonomous = another.Autonomous;
        ModifyRawState = another.ModifyRawState;
        ModifyInterpretedState = another.ModifyInterpretedState;
        CancelMoveTo = another.CancelMoveTo;
        HaltCompletelyBit = another.HaltCompletelyBit;
        DisableJumpDuringLink = another.DisableJumpDuringLink;

        GapToObject = another.GapToObject;
        LowerGap = another.LowerGap;
        WantedBearing = another.WantedBearing;
        Speed = another.Speed;
        FailGap = another.FailGap;
        StrollExecThreshhold = another.StrollExecThreshhold;
        CtxIdent = another.CtxIdent;
        GripTagToEnact = another.GripTagToEnact;
        ActStamp = another.ActStamp;
    }

    /// <summary>Clears every flag and the context id; the retail move-to manager's blank slate.</summary>
    public void WipeFlagSet()
    {
        CanWalk = false;
        CanRun = false;
        CanSidestep = false;
        CanWalkBackwards = false;
        CanCharge = false;
        FailWalk = false;
        UseFinalHeading = false;
        Sticky = false;
        MoveAway = false;
        MoveTowards = false;
        UseSpheres = false;
        SetHoldKey = false;
        Autonomous = false;
        ModifyRawState = false;
        ModifyInterpretedState = false;
        CancelMoveTo = false;
        HaltCompletelyBit = false;
        DisableJumpDuringLink = false;
        CtxIdent = 0;
    }

    /// <summary>The motion and hold-key to use at a given distance from the target.</summary>
    public void FetchDirective(float distance, float bearingDiff, out uint locomotion, out HeldKey gripTag, out bool movingAway)
    {
        _ = bearingDiff;

        if (MoveTowards && MoveAway)
        {
            TowardsAndAway(distance, out locomotion, out movingAway);
        }
        else if (MoveAway)
        {
            movingAway = distance < LowerGap;
            locomotion = movingAway ? LocomotionDirective.WalkForward : 0u;
        }
        else
        {
            movingAway = false;
            locomotion = distance > GapToObject ? LocomotionDirective.WalkForward : 0u;
        }

        gripTag = ChooseGripTag(distance);
    }

    public void TowardsAndAway(float distance, out uint cmd, out bool movingAway)
    {
        if (distance > GapToObject)
        {
            cmd = LocomotionDirective.WalkForward;
            movingAway = false;
        }
        else if (distance - LowerGap < Epsilon)
        {
            cmd = LocomotionDirective.StrollBackward;
            movingAway = true;
        }
        else
        {
            cmd = 0u;
            movingAway = false;
        }
    }

    /// <summary>180° when the body should face away from where it is going, else 0°.</summary>
    public float FetchWantedBearing(uint directive, bool movingAway)
    {
        bool ahead = directive == LocomotionDirective.ExecAhead || directive == LocomotionDirective.WalkForward;
        if (ahead)
            return movingAway ? 180f : 0f;
        return directive == LocomotionDirective.StrollBackward && !movingAway ? 180f : 0f;
    }

    public static LocomotionParams FromWire(
        uint bitfield,
        float gapToObject,
        float lowerGap,
        float failGap,
        float pace,
        float strollExecThreshhold,
        float wantedBearing)
    {
        var p = Unpack(bitfield);
        p.GapToObject = gapToObject;
        p.LowerGap = lowerGap;
        p.FailGap = failGap;
        p.Speed = pace;
        p.StrollExecThreshhold = strollExecThreshhold;
        p.WantedBearing = wantedBearing;
        return p;
    }

    public static LocomotionParams FromWirePivotTo(uint bitfield, float pace, float wantedBearing)
    {
        var p = Unpack(bitfield);
        p.Speed = pace;
        p.WantedBearing = wantedBearing;
        return p;
    }

    private HeldKey ChooseGripTag(float distance)
    {
        if (CanCharge)
            return HeldKey.Run;
        if (!CanRun)
            return HeldKey.None;
        if (CanWalk && (distance - GapToObject) <= StrollExecThreshhold)
            return HeldKey.None;
        return HeldKey.Run;
    }

    private static LocomotionParams Unpack(uint bitfield)
    {
        WireBits bitset = (WireBits)bitfield;
        bool On(WireBits bit) => (bitset & bit) != 0;
        return new LocomotionParams
        {
            CanWalk = On(WireBits.CanWalk),
            CanRun = On(WireBits.CanRun),
            CanSidestep = On(WireBits.CanSidestep),
            CanWalkBackwards = On(WireBits.CanWalkBackwards),
            CanCharge = On(WireBits.CanCharge),
            FailWalk = On(WireBits.FailWalk),
            UseFinalHeading = On(WireBits.UseFinalHeading),
            Sticky = On(WireBits.Sticky),
            MoveAway = On(WireBits.MoveAway),
            MoveTowards = On(WireBits.MoveTowards),
            UseSpheres = On(WireBits.UseSpheres),
            SetHoldKey = On(WireBits.SetHoldKey),
            Autonomous = On(WireBits.Autonomous),
            ModifyRawState = On(WireBits.ModifyRawState),
            ModifyInterpretedState = On(WireBits.ModifyInterpretedState),
            CancelMoveTo = On(WireBits.CancelMoveTo),
            HaltCompletelyBit = On(WireBits.StopCompletely),
            DisableJumpDuringLink = On(WireBits.DisableJumpDuringLink),
        };
    }
}
