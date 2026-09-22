using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public readonly record struct GrantedKineticsTimestamps(
    ushort Instance,
    ushort ServerControlledMove,
    ushort Teleport,
    ushort ForcePosition,
    bool TeleportAdvanced = false,
    uint? PreMergeCommittedCellId = null,
    ushort PreviousTeleport = 0);

public readonly record struct CreateAnchorUpdate(
    uint ChildGuid,
    uint ParentGuid,
    uint ParentLocation,
    uint PlacementId,
    ushort ChildInstanceSequence,
    ushort ChildPositionSequence);

public readonly record struct SameEpochCreateObjectEvents(
    KineticSpawnData Description,
    ObjDescNotice.Parsed Appearance,
    CreateAnchorUpdate? Parent,
    RealmSession.MoverPositionUpdate? Position,
    PickupNotice.Parsed? Pickup,
    RealmSession.MoverMotionUpdate? Movement,
    GroupPhase.Parsed State,
    VelocityUpdate.Parsed Vector);

public readonly record struct IncomingBuildOutcome(
    SpawnStampVerdict Disposition,
    RealmSession.MoverSpawn Snapshot,
    SameEpochCreateObjectEvents? SameGenerationEvents,
    GrantedKineticsTimestamps Timestamps);
