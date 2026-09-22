using MacAC.Dat;

namespace MacAC.Mechanics.Realm;

public sealed record MountedLandblock(
    uint LandblockId,
    TerrainTile Heightmap,
    IReadOnlyList<RealmActor> Entities,
    KineticDatBundle? PhysicsDats = null);
