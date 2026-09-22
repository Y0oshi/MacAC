using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Play;

internal enum SimAvatarKineticsPublicationStatus
{
    Prepared,
    Committed,
    RejectedAuthority,
    RejectedToken,
    Discarded,
}

internal enum SimAvatarKineticsArmingStatus
{
    Evaluated,
    DeferredCell,
    RejectedPlacement,
    RejectedAuthority,
    RejectedToken,
}

internal enum SimAvatarProxyVerdict : byte
{
    RegisteredAuthoredPayload,
    ProvenShapeless,
}

internal readonly record struct SimAvatarKineticsArmingStaging(
    float Radius,
    float Height,
    SimAvatarProxyVerdict ShadowDisposition)
{
    internal bool IsValid
    {
        get
        {
            return float.IsFinite(Radius)
        && Radius >= 0f
        && float.IsFinite(Height)
        && Height >= 0f
        && ShadowDisposition is SimAvatarProxyVerdict
            .RegisteredAuthoredPayload
            or SimAvatarProxyVerdict.ProvenShapeless;
        }
    }
}

internal readonly record struct SimAvatarKineticsPublicationTicket(
    SimActorKey Entity,
    SimActorPlacementTicket Placement,
    ulong PublicationId,
    uint LocalPlayerServerGuid,
    long LocalPlayerIdentityRevision,
    ulong PhysicsOwnershipEpoch,
    ulong ObjectClockEpoch,
    ulong ControllerOwnershipEpoch,
    ulong SessionGenerationAuthority)
{
    internal bool IsValid
    {
        get
        {
            return PublicationId is not 0UL
        && LocalPlayerServerGuid is not 0u
        && Placement.IsValid
        && Entity == Placement.Entity;
        }
    }
}

internal readonly record struct SimAvatarKineticsArmingTicket(
    SimActorKey Entity,
    SimActorPlacementTicket Placement,
    ulong ActivationId,
    uint LocalPlayerServerGuid,
    long LocalPlayerIdentityRevision,
    ulong PhysicsOwnershipEpoch,
    ulong ObjectClockEpoch,
    ulong ControllerOwnershipEpoch,
    ulong SessionGenerationAuthority)
{
    internal bool IsValid
    {
        get
        {
            return ActivationId is not 0UL
        && LocalPlayerServerGuid is not 0u
        && Placement.IsValid
        && Entity == Placement.Entity;
        }
    }
}

internal readonly record struct SimAvatarKineticsArmingStub(
    SimAvatarKineticsArmingTicket Token,
    ulong EvaluationId,
    SimIdleSetPositionEvaluation Placement)
{
    internal bool IsValid
    {
        get
        {
            return Token.IsValid
        && EvaluationId is not 0UL
        && Placement.Placement == Token.Placement;
        }
    }
}

internal readonly record struct SimAvatarKineticsCandidateCapture(
    Vector3 Position,
    Quaternion Orientation,
    uint CellId,
    Vector3 CellLocalPosition,
    KineticStateFlags State,
    TransientPhaseFlagSet TransientState,
    bool InWorld);

public readonly record struct
    SimAvatarKineticsPublicationHoldingCapture(
        bool IsBound,
        bool IsDisposed,
        int CandidateCount,
        int PendingActivationCount,
        ulong LastPublicationId)
{
    internal bool IsConverged
    {
        get
        {
            return !IsBound
        || (IsDisposed
            && CandidateCount is 0
            && PendingActivationCount is 0);
        }
    }
}
