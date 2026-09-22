using System.Numerics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Play;

/// <summary>Input state for a single frame of player movement.</summary>
public readonly record struct LocomotionInput(
    bool Forward = false,
    bool Backward = false,
    bool StrafeLeft = false,
    bool StrafeRight = false,
    bool TurnLeft = false,
    bool TurnRight = false,
    bool Run = false,
    float MouseDeltaX = 0f,
    bool Jump = false,
    bool IsPersistentCommand = false);

public readonly record struct AvatarLocomotionAssemblyOptions(
    int RunSkill,
    int JumpSkill)
{
    public const int BackupExecAptitude = 200;
    public const int BackupLeapAptitude = 300;

    public static AvatarLocomotionAssemblyOptions Backup =>
        new(BackupExecAptitude, BackupLeapAptitude);

    public static AvatarLocomotionAssemblyOptions From(
        SimLocomotionSkillCapture aptitudes)
    {
        return new(
            aptitudes.RunSkill >= 0 ? aptitudes.RunSkill : BackupExecAptitude,
            aptitudes.JumpSkill >= 0 ? aptitudes.JumpSkill : BackupLeapAptitude);
    }
}

public readonly record struct JumpChargeCapture(bool IsCharging, float Power);

public readonly record struct LocomotionResult(
    Vector3 Position,
    Vector3 RenderPosition,
    uint CellId,
    bool IsOnGround,
    bool MotionStateChanged,
    uint? ForwardCommand,
    uint? SidestepCommand,
    uint? TurnCommand,
    float? ForwardSpeed,
    float? SidestepSpeed,
    float? TurnSpeed,
    bool IsRunning = false,
    bool JustLanded = false,              // true on the single frame we transitioned airborne → grounded
    float? JumpExtent = null,       // non-null when a jump was triggered this frame
    Vector3? JumpVelocity = null,
    bool ShouldSendMovementEvent = false,
    bool TurnUsesRunHold = false,
    bool SidestepUsesRunHold = false,
    bool IsMouseLookMovementEvent = false,
    uint CurrentStyle = 0x8000003Du,
    CrudeLocomotionPhase? RawMotionStateOverride = null);

public enum AvatarPhase { InWorld, PortalSpace }

internal enum AvatarLocomotionDriverPublicationLifespan
{
    StandalonePublished,
    CandidatePreparing,
    CandidateSealed,
    RuntimeOwnedDormant,
    RuntimePublished,
    RuntimeRetired,
    Discarded,
}
