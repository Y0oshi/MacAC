using System.Collections.Immutable;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Realm;

/// <summary>Every packed collision asset a landblock needs, keyed by DAT id.</summary>
public sealed record LandblockContactBuild(
    ImmutableDictionary<uint, PackedGfxObjContactAsset> GfxObjs,
    ImmutableDictionary<uint, PackedSetupContact> Setups,
    ImmutableDictionary<uint, PackedCellStructContactAsset> CellStructures,
    ImmutableDictionary<uint, PackedEnvCellTopology> EnvCells,
    ImmutableArray<uint> GfxObjIds,
    ImmutableArray<uint> SetupIds,
    ImmutableArray<uint> EnvCellIds)
{
    public static readonly LandblockContactBuild Empty = new(
        ImmutableDictionary<uint, PackedGfxObjContactAsset>.Empty,
        ImmutableDictionary<uint, PackedSetupContact>.Empty,
        ImmutableDictionary<uint, PackedCellStructContactAsset>.Empty,
        ImmutableDictionary<uint, PackedEnvCellTopology>.Empty,
        [],
        [],
        []);
}
