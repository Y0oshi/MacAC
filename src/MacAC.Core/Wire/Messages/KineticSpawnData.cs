using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Wire.Messages;

/// <summary>The nine u16 sequence stamps at the end of every PhysicsDesc, in wire order.</summary>
public readonly record struct KineticStamps(
    ushort Position,
    ushort Movement,
    ushort State,
    ushort Vector,
    ushort Teleport,
    ushort ServerControlledMove,
    ushort ForcePosition,
    ushort ObjDesc,
    ushort Instance);

public readonly record struct KineticAttachment(uint Guid, uint LocationId);

/// <summary>The raw movement blob plus what could be decoded from it.</summary>
public readonly record struct KineticMovementData(ReadOnlyMemory<byte> RawData, ObjectCreation.RemoteMotionState? MotionState, bool? IsAutonomous);

/// <summary>Everything CreateObject's PhysicsDesc block said; absent optional fields stay null.</summary>
public readonly record struct KineticSpawnData(
    uint RawState,
    ObjectCreation.RemotePosition? Position,
    KineticMovementData? Movement,
    uint? AnimationFrame,
    uint? SetupTableId,
    uint? MotionTableId,
    uint? SoundTableId,
    uint? PhysicsScriptTableId,
    KineticAttachment? Parent,
    ReadOnlyMemory<KineticAttachment>? Children,
    float? Scale,
    float? Friction,
    float? Elasticity,
    float? Translucency,
    Vector3? Velocity,
    Vector3? Acceleration,
    Vector3? AngularVelocity,
    uint? DefaultScriptType,
    float? DefaultScriptIntensity,
    KineticStamps Timestamps)
{
    public KineticStateFlags State => (KineticStateFlags)RawState;
}
