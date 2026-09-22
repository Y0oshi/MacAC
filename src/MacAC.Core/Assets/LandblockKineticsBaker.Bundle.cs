using System.Collections.Immutable;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Dat;
using DatEnvironment =  MacAC.Dat.InteriorShell;

namespace MacAC.Assets;

/// <summary>Gathering the DAT records and baked contact assets a landblock depends on.</summary>
public static partial class LandblockKineticsBaker
{
    public static KineticDatBundle CollectDatBundle(IDatAccess datFiles, uint lbIdent, IReadOnlyList<RealmActor> actors)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(actors);

        var environChambers = new Dictionary<uint, RoomCell>();
        var environments = new Dictionary<uint, DatEnvironment>();
        var setups = new Dictionary<uint, RigSpec>();
        var gfxObjs = new Dictionary<uint, PartMesh>();

        var details = datFiles.Get<TerrainTileExtras>(DetailsIdentOf(lbIdent));
        if (details is not null)
        {
            CollectDatBundleBranch(lbIdent, details, datFiles, environChambers, environments, setups);
        }

        foreach (RealmActor actor in actors)
        {
            if (Family(actor.SrcGfxObjRefOrRigIdent) == RigStem)
                Pull(datFiles, setups, actor.SrcGfxObjRefOrRigIdent);
            foreach (TriMeshRef piece in actor.MeshRefs)
            {
                if (Family(piece.GfxObjId) == GfxObjRefStem)
                    Pull(datFiles, gfxObjs, piece.GfxObjId);
            }
        }

        return new KineticDatBundle(details, environChambers, environments, setups, gfxObjs);
    }

    private static void CollectDatBundleBranch(uint lbIdent, TerrainTileExtras details, IDatAccess datFiles, Dictionary<uint, RoomCell> environChambers, Dictionary<uint, DatEnvironment> environments, Dictionary<uint, RigSpec> setups)
    {
        uint leadChamber = (lbIdent & 0xFFFF0000u) | 0x0100u;
        for (uint shift = 0; shift < details.CellCount; ++shift)
        {
            uint chamberIdent = leadChamber + shift;
            if (datFiles.Get<RoomCell>(chamberIdent) is not { } chamber)
                continue;
            environChambers[chamberIdent] = chamber;
            if (chamber.ShellId is not 0)
                Pull(datFiles, environments, 0x0D000000u | chamber.ShellId);
        }
        foreach (BuildingSpec structure in details.Structures)
        {
            if (Family(structure.ModelId) == RigStem)
                Pull(datFiles, setups, structure.ModelId);
        }
    }

    public static LandblockContactBuild LocateLinkClosure(IBakedContactSource src, MountedLandblock lb)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(lb);

        KineticDatBundle datFiles = lb.PhysicsDats ?? KineticDatBundle.Empty;
        ImmutableArray<uint> gfxObjRefIdents = [.. datFiles.GfxObjs.Keys.Order()];
        ImmutableArray<uint> rigIdents = [.. datFiles.Setups.Keys.Order()];
        ImmutableArray<uint> environChamberIdents = [.. datFiles.EnvCells.Keys.Order()];

        return new LandblockContactBuild(
            Chart(gfxObjRefIdents, ident => Demand(src.ScanGfxObjRefImpact(ident), "GfxObj collision", ident)),
            Chart(rigIdents, ident => Demand(src.ReadSetupCollision(ident), "Setup collision", ident)),
            Chart(environChamberIdents, ident => Demand(src.ScanChamberStructureImpact(ident), "CellStruct collision", ident)),
            Chart(environChamberIdents, ident => Demand(src.ScanEnvironChamberWiring(ident), "EnvCell topology", ident)),
            gfxObjRefIdents,
            rigIdents,
            environChamberIdents);
    }

    public static LandCanvas FormLand(MountedLandblock lb, ReadOnlySpan<float> heightTable)
    {
        ArgumentNullException.ThrowIfNull(lb);
        if (heightTable.Length < 256)
            throw new ArgumentException(HeightChartTooShort, nameof(heightTable));

        byte[] land = new byte[81];
        for (int idx = 0; idx < land.Length; ++idx)
            land[idx] = (byte)(ushort)lb.Heightmap.Samples[idx];

        return new LandCanvas(
            lb.Heightmap.Heights,
            heightTable,
            (lb.LandblockId >> 24) & 0xFFu,
            (lb.LandblockId >> 16) & 0xFFu,
            land);
    }

    // Fetches ident into into unless it is already there or absent
    private static void Pull<T>(IDatAccess datFiles, Dictionary<uint, T> into, uint ident) where T : class, IDatRecord
    {
        if (!into.ContainsKey(ident) && datFiles.Get<T>(ident) is { } capture)
            into[ident] = capture;
    }

    private static ImmutableDictionary<uint, T> Chart<T>(ImmutableArray<uint> idents, Func<uint, T> scan)
    {
        var chart = ImmutableDictionary.CreateBuilder<uint, T>();
        foreach (uint ident in idents)
            chart.Add(ident, scan(ident));
        return chart.ToImmutable();
    }

    private static T Demand<T>(BakedContactRead<T> scan, string sort, uint ident) where T : class
    {
        if (scan is { Status: BakedAssetReadStatus.Loaded, Data: { } blob })
            return blob;
        throw new InvalidDataException($"{sort} 0x{ident:X8} is {scan.Status}. The complete near-tier generation can't be published");
    }
}
