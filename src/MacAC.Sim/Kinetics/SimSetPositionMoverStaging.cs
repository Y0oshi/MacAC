using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal enum SimSetPositionMoverStagingStatus
{
    Prepared,

    RetrySetupUnavailable,

    RetryWorldFrameUnavailable,

    RejectedAuthority,
    InvalidData,
}

internal enum SimSetPositionParkReason
{
    None,
    AwaitingSetupCollision,
    AwaitingWorldFrame,
}

internal static class SimSetPositionMoverStagingStatusExtensions
{
    internal static bool IsRetryable(this SimSetPositionMoverStagingStatus condition)
    {
        return condition is SimSetPositionMoverStagingStatus.RetrySetupUnavailable or SimSetPositionMoverStagingStatus.RetryWorldFrameUnavailable;
    }

    internal static SimSetPositionParkReason ParkReason(this SimSetPositionMoverStagingStatus condition)
    {
        return condition switch
        {
            SimSetPositionMoverStagingStatus.RetrySetupUnavailable => SimSetPositionParkReason.AwaitingSetupCollision,
            SimSetPositionMoverStagingStatus.RetryWorldFrameUnavailable => SimSetPositionParkReason.AwaitingWorldFrame,
            _ => SimSetPositionParkReason.None,
        };
    }
}

// The mover's setup collision, or the knowledge that it has none; unresolved until looked up
internal readonly record struct SimSetPositionMoverSetup(bool IsResolved, uint SetupTableId, PackedSetupContact? Collision)
{
    internal static SimSetPositionMoverSetup Unavailable => default;

    internal static SimSetPositionMoverSetup SettledAbsent => new(true, 0u, null);

    internal static SimSetPositionMoverSetup Settled(uint setupTableId, PackedSetupContact collision)
    {
        return new(
        true,
        setupTableId is not 0u ? setupTableId : throw new ArgumentOutOfRangeException(nameof(setupTableId)),
        collision ?? throw new ArgumentNullException(nameof(collision)));
    }

    // The setup matches the record's canonical setup table: both absent, or the same id with collision
    // present
    internal bool Fits(uint canonRigChartIdent)
    {
        return canonRigChartIdent is 0u
            ? SetupTableId is 0u && Collision is null
            : SetupTableId == canonRigChartIdent && Collision is not null;
    }
}

internal readonly record struct SimSetPositionMoverStaging(
    SimSetPositionMoverSetup Setup,
    SimSetPositionOperationKind Kind,
    double GameTime,
    KineticPlacementClass PlacementClass,
    KineticSetPositionFlags Flags,
    Vector3 Line = default,
    float ScatterRadiusX = 0f,
    float ScatterRadiusY = 0f,
    uint ScatterAttempts = 0u,
    float ShadowWorldOffsetX = 0f,
    float ShadowWorldOffsetY = 0f,
    SimPortalPlacementAuthority Portal = default,
    bool ResolveWorldOffsetFromRuntimeFrame = false);

// Turns a resolved staging plus the accepted wire position into the set-position directive the
// physics ledger executes
internal static class SimSetPositionMoverStager
{
    internal static bool TryAssemble(
        SimActorRecord capture,
        in ObjectCreation.RemotePosition approvedLocus,
        uint canonRigChartIdent,
        SimSetPositionOperationKind approvedSort,
        SimPortalPlacementAuthority approvedGateway,
        ulong velArbiterVer,
        in SimSetPositionMoverStaging loading,
        out SimSetPositionDirective directive)
    {
        ArgumentNullException.ThrowIfNull(capture);
        directive = default;
        if (!loading.Setup.IsResolved || loading.Kind != approvedSort || loading.Portal != approvedGateway || !loading.Setup.Fits(canonRigChartIdent))
            return false;

        var position = approvedLocus;
        Vector3 chamberOwn = new Vector3(position.PositionX, position.PositionY, position.PositionZ);
        Vector3 realm = new Vector3(chamberOwn.X + loading.ShadowWorldOffsetX, chamberOwn.Y + loading.ShadowWorldOffsetY, chamberOwn.Z);
        Quaternion facing = new Quaternion(position.RotationX, position.RotationY, position.RotationZ, position.RotationW);

        float scaling = capture.Snapshot.Physics?.Scale ?? capture.Snapshot.ObjScale ?? 1f;
        var rig = loading.Setup.Collision;
        ImmutableArray<PackedContactSphere> orbs = rig?.Spheres ?? ImmutableArray<PackedContactSphere>.Empty;
        float hopUp = rig is null ? 0f : rig.StepUpHeight * scaling;
        float hopDown = rig is null ? 0f : rig.StepDownHeight * scaling;

        var linkFlagSet = EntityContactFlagsExt.FromPwdBitfield(capture.Snapshot.ObjectDescriptionFlags ?? 0u);
        MoverState carrier = linkFlagSet.ToCarrierPhase();
        if ((linkFlagSet & ActorImpactFlagSet.IsPlayer) != 0)
            carrier |= MoverState.IsPlayer;

        var req = new KineticSetPositionRequest(
            realm,
            facing,
            position.LandblockId,
            chamberOwn,
            orbs,
            scaling,
            hopUp,
            hopDown,
            capture.FinalKineticsCondition,
            carrier,
            capture.Key?.LocalEntityId ?? 0u,
            loading.PlacementClass,
            loading.Flags,
            loading.Line,
            loading.ScatterRadiusX,
            loading.ScatterRadiusY,
            loading.ScatterAttempts);
        directive = new SimSetPositionDirective(
            req, loading.Kind, loading.GameTime, velArbiterVer, loading.ShadowWorldOffsetX, loading.ShadowWorldOffsetY, loading.Portal);
        return true;
    }
}
