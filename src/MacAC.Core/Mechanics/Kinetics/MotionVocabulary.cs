namespace MacAC.Mechanics.Kinetics;

/// <summary>The full 32-bit motion commands the interpreter reasons about by name.</summary>
public static class LocomotionDirective
{
    public const uint Ready = 0x41000003u;
    public const uint WalkForward = 0x45000005u;
    public const uint ExecAhead = 0x44000007u;
    public const uint StrollBackward = 0x45000006u;
    public const uint TurnRight = 0x6500000Du;
    public const uint PivotLeft = 0x6500000Eu;
    public const uint FlankHopRight = 0x6500000Fu;
    public const uint FlankHopLeft = 0x65000010u;
    public const uint Fallen = 0x40000008u;
    public const uint Falling = 0x40000015u;
    public const uint Jump = 0x2500003Bu;
    public const uint Jumpup = 0x1000004Bu;
    public const uint FallDown = 0x10000050u;
    public const uint Dead = 0x40000011u;
    public const uint Sanctuary = 0x10000057u;
    public const uint Crouch = 0x41000012u;
    public const uint Sitting = 0x41000013u;
    public const uint Sleeping = 0x41000014u;
    public const uint CrouchLowerTied = 0x41000011u;
    public const uint CrouchUpperExclusive = 0x41000015u;
}

public enum TravelKind
{
    Invalid = 0,
    RawCommand = 1,
    InterpretedCommand = 2,
    StopRawCommand = 3,
    StopInterpretedCommand = 4,
    StopCompletely = 5,
    MoveToObject = 6,
    MoveToPosition = 7,
    TurnToObject = 8,
    TurnToHeading = 9,
}

/// <summary>One movement request, in any of the retail MovementStruct shapes.</summary>
public struct LocomotionPacket
{
    public TravelKind Type;
    public uint Motion;
    public float Speed;
    public bool Autonomous;
    public bool ModifyInterpretedPhase;
    public bool ModifyRawPhase;

    public uint ObjectId;
    public uint TopTierIdent;
    public Locus Spot;
    public float Radius;
    public float Height;
    public Gait.LocomotionParams? Params;
}

/// <summary>The weenie behind a moving object, as far as movement needs to know it.</summary>
public interface IWeenieActor
{
    /// <summary>vtable +0x30 - InqJumpVelocity. Returns true and sets vz if valid.</summary>
    bool InqLeapVel(float reach, out float vz);

    /// <summary>vtable +0x34 - InqRunRate. Returns true and sets rate if valid.</summary>
    bool InqExecRate(out float rate);

    bool CanLeap(float reach);

    bool IsBeast() => true;

    bool IsTheAvatar() => false;

    bool HopStaminaPrice(float reach, out int price)
    {
        price = 0;
        return true;
    }
}
