using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public readonly record struct LandblockLandscapeBounds(float MaxZ, float MinZ);

public sealed record LandblockAssemble(
    MountedLandblock Landblock,
    EnvironChamberLandblockAssemble? EnvCells = null,
    LandblockAssembleOrigin Origin = default,
    LandblockContactBuild? Collisions = null,
    LandblockLandscapeBounds TerrainBounds = default)
{
    public uint LandblockIdent => Landblock.LandblockId;
}
