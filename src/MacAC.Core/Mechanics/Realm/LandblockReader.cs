using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Data;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Realm;

/// <summary>Loads a landblock's heightmap and turns its static stabs and buildings into entities.</summary>
public static class LandblockReader
{
    private const uint GfxObjRefChunk = 0x01000000u;
    private const uint RigChunk = 0x02000000u;
    private const uint ChunkBitmask = 0xFF000000u;
    private const uint StemBitmask = 0xFFFF0000u;
    private const uint DetailsLo = 0xFFFEu;
    private const ushort NoChamber = 0xFFFF;

    public static MountedLandblock? Load(IDatRecordSource datFiles, uint lbIdent)
    {
        if (datFiles.Get<TerrainTile>(lbIdent) is not { } chunk)
            return null;
        var details = datFiles.Get<TerrainTileExtras>((lbIdent & StemBitmask) | DetailsLo);
        IReadOnlyList<RealmActor> actors = details is null ? [] : AssembleActorsFromDetails(details, lbIdent);
        return new MountedLandblock(lbIdent, chunk, actors);
    }

    /// <summary>One entity per stab and per building. Ids that are neither GfxObj nor Setup are skipped.</summary>
    public static IReadOnlyList<RealmActor> AssembleActorsFromDetails(TerrainTileExtras details, uint lbIdent = 0)
    {
        List<RealmActor> actors = new List<RealmActor>(details.Objects.Count + details.Structures.Count);
        IdSequence idents = new IdSequence(lbIdent);

        foreach (PlacedObject stab in details.Objects)
        {
            if (!IsModel(stab.Id))
                continue;
            actors.Add(Place(idents.Next(), stab.Id, stab.Pose, lbIdent));
        }

        foreach (BuildingSpec structure in details.Structures)
        {
            if (!IsModel(structure.ModelId))
                continue;
            actors.Add(Place(idents.Next(), structure.ModelId, structure.Pose, lbIdent, structure));
        }

        return actors;
    }

    private static RealmActor Place(uint ident, uint modelIdent, Pose cycle, uint lbIdent, BuildingSpec? structure = null)
    {
        RealmActor actor = new RealmActor
        {
            Id = ident,
            SrcGfxObjRefOrRigIdent = modelIdent,
            Position = cycle.Origin,
            Rotation = cycle.Orientation,
            MeshRefs = [],
            FxChamberIdent = ExteriorChamberIdent(lbIdent, cycle.Origin),
            IsStructureShell = structure is not null,
            StructureShellMooringChamberIdent = structure is null ? null : MooringChamberIdent(structure, lbIdent),
        };
        actor.RenewAabb();
        return actor;
    }

    private static bool IsModel(uint ident) => (ident & ChunkBitmask) is GfxObjRefChunk or RigChunk;

    private static uint? ExteriorChamberIdent(uint lbIdent, Vector3 own)
    {
        return lbIdent is 0 ? null : LandCanvas.ComputeOutdoorCellId(lbIdent, own.X, own.Y);
    }

    // The first portal that leads into a real cell anchors the building indoors
    private static uint? MooringChamberIdent(BuildingSpec structure, uint lbIdent)
    {
        if (lbIdent is 0)
            return null;
        foreach (BuildingDoorway gateway in structure.Doorways)
        {
            if (gateway.OtherCellId != NoChamber)
                return (lbIdent & StemBitmask) | gateway.OtherCellId;
        }
        return null;
    }

    // Entity ids: a plain counter for tests (landblock 0), the static pool otherwise
    private struct IdSequence(uint lbIdent)
    {
        private readonly uint _x = (lbIdent >> 24) & 0xFFu;
        private readonly uint _y = (lbIdent >> 16) & 0xFFu;
        private uint _upcoming = lbIdent is 0 ? 1u : 0u;

        public uint Next()
        {
            return lbIdent is 0 ? _upcoming++ : LandblockStaticIdPool.Allocate(_x, _y, ref _upcoming);
        }
    }
}
