using MacAC.Dat;
using DatEnvironment =  MacAC.Dat.InteriorShell;

namespace MacAC.Mechanics.Realm;

/// <summary>The DAT records physics needs for one landblock.</summary>
public sealed record KineticDatBundle(
    TerrainTileExtras? Info,
    IReadOnlyDictionary<uint, RoomCell> EnvCells,
    IReadOnlyDictionary<uint, DatEnvironment> Environments,
    IReadOnlyDictionary<uint, RigSpec> Setups,
    IReadOnlyDictionary<uint, PartMesh> GfxObjs)
{
    public static readonly KineticDatBundle Empty = new(
        null,
        new Dictionary<uint, RoomCell>(),
        new Dictionary<uint, DatEnvironment>(),
        new Dictionary<uint, RigSpec>(),
        new Dictionary<uint, PartMesh>());

    /// <summary>The same bundle without the records that carry collision BSPs.</summary>
    public KineticDatBundle WithoutImpactGraphs() => new(Info, EnvCells, Empty.Environments, Setups, Empty.GfxObjs);
}
