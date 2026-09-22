using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal readonly record struct SimSpawnTenancyTicket(
    SimActorKey Entity,
    ulong LeaseId,
    ulong SessionLifetimeVersion,
    ulong PositionAuthorityVersion,
    ulong CreateIntegrationVersion,
    ulong SourcePlacementCommitVersion)
{
    internal bool IsValid
    {
        get
        {
            return Entity.LocalEntityId is not 0u
        && LeaseId is not 0UL
        && PositionAuthorityVersion is not 0UL
        && CreateIntegrationVersion is not 0UL;
        }
    }
}

internal readonly record struct SimSpawnTenancyLease(
    SimSpawnTenancyTicket Token,
    SimSovereignPositionRoute Route,
    SimActorPlacementTicket Placement,
    RealmSession.MoverSpawn InitialCreate,
    ImmutableArray<SimSpawnTenancyFollowup> Continuations)
{
    internal bool IsValid
    {
        get
        {
            return Token.IsValid
        && Route.Accepted
        && Route.Authority.Entity == Token.Entity
        && InitialCreate.Guid is not 0u
        && InitialCreate.InstanceSequence == Token.Entity.Incarnation
        && !Continuations.IsDefault
        && HasValidContinuationChain()
        && (!Route.PerformsSetLocus
            || Placement.IsValid
                && Placement.Entity == Token.Entity);
        }
    }

    private bool HasValidContinuationChain()
    {
        uint holderOid = InitialCreate.Guid;
        for (int ordinal = 0; ordinal < Continuations.Length; ++ordinal)
        {
            var continuation =
                Continuations[ordinal];
            if (!continuation.IsValid
                || continuation.Sequence != (ulong)ordinal + 1UL
                || continuation.InstanceSequence != Token.Entity.Incarnation
                || continuation.Actions.Any(
                    act => act.OwnerGuid != holderOid))

                return false;
        }
        return true;
    }
}

internal enum SimSpawnFollowupKind : byte
{
    SameIncarnationCreate,
    ObjDesc,
    Parent,
    Pickup,
    Position,
    Movement,
    State,
    Vector,
}

internal enum SimSpawnTailActionKind : byte
{
    PreTailDescriptionAdaptation,
    ObjDesc,
    CreateParent,
    Parent,
    Pickup,
    Position,
    Movement,
    State,
    Vector,
    WeenieDescription,
    ResidentCellCleanup,
}

internal readonly record struct SimSpawnTailAction(
    SimSpawnTailActionKind Kind,
    uint OwnerGuid,
    KineticSpawnData? Description = null,
    ObjDescNotice.Parsed? ObjDesc = null,
    CreateAnchorUpdate? CreateParent = null,
    AncestorSignal.Parsed? Parent = null,
    PickupNotice.Parsed? Pickup = null,
    RealmSession.MoverPositionUpdate? Position = null,
    RealmSession.MoverMotionUpdate? Movement = null,
    GroupPhase.Parsed? State = null,
    VelocityUpdate.Parsed? Vector = null,
    RealmSession.MoverSpawn? WeenieDescription = null,
    SimGrantedPositionSource PositionSource =
        SimGrantedPositionSource.Unknown,
    PoseStampVerdict PositionDisposition =
        PoseStampVerdict.Rejected,
    ushort PreviousTeleportSequence = 0,
    GrantedKineticsTimestamps AcceptedTimestamps = default,
    bool AppliesMovementPayload = false,
    bool RetainMovementPayload = true,
    bool HasTimestampMutation = false)
{
    internal bool IsStructurallyValid
    {
        get
        {
            return HasExclusiveCargo()
        && Kind switch
        {
            SimSpawnTailActionKind.PreTailDescriptionAdaptation =>
                Description is not null,
            SimSpawnTailActionKind.ObjDesc => ObjDesc is { } objRefDsc
                && objRefDsc.Guid == OwnerGuid,
            SimSpawnTailActionKind.CreateParent =>
                CreateParent is { } buildAncestor
                && buildAncestor.ChildGuid == OwnerGuid,
            SimSpawnTailActionKind.Parent => Parent is { } ancestor
                && ancestor.ChildGuid == OwnerGuid,
            SimSpawnTailActionKind.Pickup => Pickup is { } lift
                && lift.Guid == OwnerGuid,
            SimSpawnTailActionKind.Position => Position is { } locus
                && locus.Guid == OwnerGuid
                && PositionSource is SimGrantedPositionSource.PositionEvent
                    or SimGrantedPositionSource.SameIncarnationCreate
                && (PositionDisposition is PoseStampVerdict.Apply
                        or PoseStampVerdict.ForcePosition
                    || PositionDisposition is PoseStampVerdict.Rejected
                        && HasTimestampMutation),
            SimSpawnTailActionKind.Movement => Movement is { } travel
                && travel.Guid == OwnerGuid
                && (AppliesMovementPayload || HasTimestampMutation),
            SimSpawnTailActionKind.State => State is { } phase
                && phase.Guid == OwnerGuid,
            SimSpawnTailActionKind.Vector => Vector is { } vector
                && vector.Guid == OwnerGuid,
            SimSpawnTailActionKind.WeenieDescription =>
                WeenieDescription is { } weenie
                && weenie.Guid == OwnerGuid,
            SimSpawnTailActionKind.ResidentCellCleanup => true,
            _ => false,
        };
        }
    }

    internal bool FitsInst(ushort instSeries)
    {
        return Kind switch
        {
            SimSpawnTailActionKind.PreTailDescriptionAdaptation =>
                Description is { } blurb
                && blurb.Timestamps.Instance == instSeries,
            SimSpawnTailActionKind.ObjDesc =>
                ObjDesc is { } objRefDsc
                && objRefDsc.InstanceSequence == instSeries,
            SimSpawnTailActionKind.CreateParent =>
                CreateParent is { } buildAncestor
                && buildAncestor.ChildInstanceSequence == instSeries,
            SimSpawnTailActionKind.Parent =>
                AcceptedTimestamps.Instance == instSeries,
            SimSpawnTailActionKind.Pickup =>
                Pickup is { } lift
                && lift.InstanceSequence == instSeries,
            SimSpawnTailActionKind.Position =>
                Position is { } locus
                && locus.InstanceSequence == instSeries,
            SimSpawnTailActionKind.Movement =>
                Movement is { } travel
                && travel.InstanceSequence == instSeries,
            SimSpawnTailActionKind.State =>
                State is { } phase
                && phase.InstanceSequence == instSeries,
            SimSpawnTailActionKind.Vector =>
                Vector is { } vector
                && vector.InstanceSequence == instSeries,
            SimSpawnTailActionKind.WeenieDescription =>
                WeenieDescription is { } weenie
                && weenie.InstanceSequence == instSeries,
            SimSpawnTailActionKind.ResidentCellCleanup => true,
            _ => false,
        };
    }

    private bool HasExclusiveCargo()
    {
        int cargoTally = (Description is null ? 0 : 1)
            + (ObjDesc is null ? 0 : 1)
            + (CreateParent is null ? 0 : 1)
            + (Parent is null ? 0 : 1)
            + (Pickup is null ? 0 : 1)
            + (Position is null ? 0 : 1)
            + (Movement is null ? 0 : 1)
            + (State is null ? 0 : 1)
            + (Vector is null ? 0 : 1)
            + (WeenieDescription is null ? 0 : 1);
        return Kind is SimSpawnTailActionKind.ResidentCellCleanup
            ? cargoTally is 0
            : cargoTally is 1;
    }
}

internal readonly record struct SimSpawnTenancyFollowup(
    ulong Sequence,
    SimSpawnFollowupKind Kind,
    ushort InstanceSequence,
    SimGrantedPositionSource PositionSource,
    ImmutableArray<SimSpawnTailAction> Actions)
{
    internal bool IsValid
    {
        get
        {
            return Sequence is not 0UL
        && !Actions.IsDefaultOrEmpty
        && Actions.All(static act => act.IsStructurallyValid)
        && HasMatchingInsts()
        && HasValidForm();
        }
    }

    private bool HasMatchingInsts()
    {
        for (int ordinal = 0; ordinal < Actions.Length; ++ordinal)
        {
            if (!Actions[ordinal].FitsInst(InstanceSequence))
                return false;
        }
        return true;
    }

    private bool HasValidForm()
    {
        if (Kind is not SimSpawnFollowupKind.SameIncarnationCreate)
        {
            if (Actions.Length is not 1)
                return false;
            var act = Actions[0];
            SimSpawnTailActionKind anticipated = Kind switch
            {
                SimSpawnFollowupKind.ObjDesc =>
                    SimSpawnTailActionKind.ObjDesc,
                SimSpawnFollowupKind.Parent =>
                    SimSpawnTailActionKind.Parent,
                SimSpawnFollowupKind.Pickup =>
                    SimSpawnTailActionKind.Pickup,
                SimSpawnFollowupKind.Position =>
                    SimSpawnTailActionKind.Position,
                SimSpawnFollowupKind.Movement =>
                    SimSpawnTailActionKind.Movement,
                SimSpawnFollowupKind.State =>
                    SimSpawnTailActionKind.State,
                SimSpawnFollowupKind.Vector =>
                    SimSpawnTailActionKind.Vector,
                _ => throw new InvalidOperationException(
                    $"Not supported initial-Create continuation kind {Kind}."),
            };
            return act.Kind == anticipated
                && (Kind is SimSpawnFollowupKind.Position
                    ? PositionSource is SimGrantedPositionSource.PositionEvent
                        && act.PositionSource == PositionSource
                    : PositionSource is SimGrantedPositionSource.Unknown);
        }

        if (Actions.Length < 2
            || Actions[^2].Kind
                is not SimSpawnTailActionKind.WeenieDescription
            || Actions[^1].Kind
                is not SimSpawnTailActionKind.ResidentCellCleanup)

            return false;

        bool hasLocus = Actions.Any(
            static action => action.Kind
                is SimSpawnTailActionKind.Position);
        if (hasLocus
                ? PositionSource
                    is not SimGrantedPositionSource.SameIncarnationCreate
                : PositionSource is not SimGrantedPositionSource.Unknown)

            return false;

        int earlierJuncture = -1;
        int locusBranchTally = 0;
        for (int ordinal = 0; ordinal < Actions.Length; ++ordinal)
        {
            var act = Actions[ordinal];
            int juncture = SameBuildJuncture(act.Kind);
            if (juncture <= earlierJuncture)
                return false;
            if (act.Kind is SimSpawnTailActionKind.Position
                && act.PositionSource != PositionSource)

                return false;
            if (act.Kind is SimSpawnTailActionKind.CreateParent
                or SimSpawnTailActionKind.Pickup
                or SimSpawnTailActionKind.Position)

                ++locusBranchTally;
            earlierJuncture = juncture;
        }
        bool hasKineticsBlurb = Actions.Any(
            static action => action.Kind
                is SimSpawnTailActionKind
                    .PreTailDescriptionAdaptation);
        return hasKineticsBlurb
            ? locusBranchTally <= 1
            : locusBranchTally is 0;
    }

    private static int SameBuildJuncture(SimSpawnTailActionKind sort)
    {
        return sort switch
        {
            SimSpawnTailActionKind.PreTailDescriptionAdaptation => 0,
            SimSpawnTailActionKind.ObjDesc => 1,
            SimSpawnTailActionKind.CreateParent
                or SimSpawnTailActionKind.Pickup
                or SimSpawnTailActionKind.Position => 2,
            SimSpawnTailActionKind.Movement => 3,
            SimSpawnTailActionKind.State => 4,
            SimSpawnTailActionKind.Vector => 5,
            SimSpawnTailActionKind.WeenieDescription => 6,
            SimSpawnTailActionKind.ResidentCellCleanup => 7,
            _ => int.MinValue,
        };
    }
}

internal readonly record struct SimSpawnTenancyAdoptionTicket(
    SimActorKey Entity,
    ulong LeaseId,
    ulong AdoptionId,
    ulong SessionLifetimeVersion,
    ulong Revision)
{
    internal bool IsValid
    {
        get
        {
            return Entity.LocalEntityId is not 0u
        && LeaseId is not 0UL
        && AdoptionId is not 0UL
        && Revision is not 0UL;
        }
    }
}

internal enum SimSpawnTenancyFinishStatus : byte
{
    Completed,
    PendingPlacement,
    RejectedToken,
    RejectedAuthority,
}

internal enum SimSpawnTenancyRunnerReleaseStatus : byte
{
    Released,
    Revised,
    RejectedToken,
    RejectedAuthority,
}

internal readonly record struct SimSpawnTenancyStub(
    SimSpawnTenancyTicket Token,
    SimWarpHookPhase TeleportHookPhase,
    SimPlacementMirrorTicket Projection,
    uint FullCellId,
    ulong PlacementCommitVersion,
    SimSpawnTenancyAdoptionTicket Adoption,
    ImmutableArray<SimSpawnTenancyFollowup> Continuations);

internal readonly record struct SimSpawnTenancyHoldingCapture(
    int ActiveLeaseCount,
    int PendingAdoptionCount,
    ulong LastLeaseId)
{
    internal bool IsConverged
    {
        get
        {
            return ActiveLeaseCount is 0
        && PendingAdoptionCount is 0;
        }
    }
}

[Flags]
internal enum SimRunnerBaselineFields : byte
{
    None = 0,
    PositionAuthorityVersion = 1 << 0,
    CreateIntegrationVersion = 1 << 1,
    FullCellId = 1 << 2,
    PlacementCommitVersion = 1 << 3,
}
