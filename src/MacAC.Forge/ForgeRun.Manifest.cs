using System.Collections.Concurrent;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Assets.Pak;

namespace MacAC.Forge;

internal sealed partial class ForgeRun
{
    private sealed class Manifest
    {
        public required List<uint> GfxObjRefIdents { get; init; }
        public required List<uint> RigIdents { get; init; }
        public required int EnvironChamberTally { get; init; }
        public required CellShellRegistry Shells { get; init; }
        public required List<MeshOrder> TriMeshJob { get; init; }
        public required HashSet<ulong> ScheduledTriMeshTags { get; init; }

        public static Manifest Assemble(
            DatVault datFiles,
            DatCollectionBridge bridge,
            ForgeJob job,
            ConcurrentBag<Fault> flaws,
            CancellationToken abort)
        {
            List<uint> gfxObjRefIdents = datFiles.IdsOf<PartMesh>().OrderBy(static ident => ident).ToList();
            List<uint> rigIdents = datFiles.IdsOf<RigSpec>().OrderBy(static ident => ident).ToList();
            List<uint> environChamberIdents = EnvironChamberIdents(datFiles, job.LbSift);
            if (job.IdentSift is { } sole)
            {
                gfxObjRefIdents = gfxObjRefIdents.Where(sole.Contains).ToList();
                rigIdents = rigIdents.Where(sole.Contains).ToList();
                environChamberIdents = environChamberIdents.Where(sole.Contains).ToList();
            }

            CellShellAssembler shells = new CellShellAssembler();
            foreach (uint fileIdent in environChamberIdents)
            {
                abort.ThrowIfCancellationRequested();
                if (!bridge.Cell.TryGet<RoomCell>(fileIdent, out RoomCell? chamber) || chamber is null)
                {
                    flaws.Add(new Fault(PakAssetKind.EnvCellMesh, fileIdent, "EnvCell DAT record was not found"));
                    continue;
                }

                shells.Add(new CellShellSeed(fileIdent, chamber.ShellId, chamber.ShellCellIndex, chamber.SkinIds));
            }

            List<MeshOrder> triMeshJob = new List<MeshOrder>(gfxObjRefIdents.Count + rigIdents.Count);
            triMeshJob.AddRange(gfxObjRefIdents.Select(static ident => MeshOrder.For(PakAssetKind.GfxObjMesh, ident)));
            triMeshJob.AddRange(rigIdents.Select(static ident => MeshOrder.For(PakAssetKind.SetupMesh, ident)));
            HashSet<ulong> scheduled = triMeshJob.Select(static ordering => ordering.Key).ToHashSet();
            triMeshJob.Sort(static (a, b) => a.Key.CompareTo(b.Key));

            return new Manifest
            {
                GfxObjRefIdents = gfxObjRefIdents,
                RigIdents = rigIdents,
                EnvironChamberTally = environChamberIdents.Count,
                Shells = shells.Build(),
                TriMeshJob = triMeshJob,
                ScheduledTriMeshTags = scheduled,
            };
        }

        public void Proclaim()
        {
            Console.WriteLine(
                $"enumerated: {GfxObjRefIdents.Count:N0} GfxObj, {RigIdents.Count:N0} Setup, "
                + $"{EnvironChamberTally:N0} EnvCell");
            double dedup = Shells.UniqueGeoTally is 0 ? 0 : (double)Shells.ChamberTally / Shells.UniqueGeoTally;
            Console.WriteLine(
                $"EnvCell catalog: {Shells.ChamberTally:N0} valid cells -> "
                + $"{Shells.UniqueGeoTally:N0} unique geometries + "
                + $"{Shells.AliasTally:N0} aliases "
                + $"({dedup:F1}x)");
            Console.WriteLine();
        }

        private static List<uint> EnvironChamberIdents(DatVault datFiles, HashSet<uint>? lbs)
        {
            List<uint> detailsIdents = new List<uint>();
            foreach (var file in datFiles.Cell.Entries)
            {
                if ((file.Id & 0xFFFFu) != 0xFFFEu)
                    continue;
                if (lbs is not null && !lbs.Contains(file.Id & 0xFFFF_0000u))
                    continue;
                detailsIdents.Add(file.Id);
            }

            detailsIdents.Sort();
            List<uint> chamberIdents = new List<uint>();
            foreach (uint detailsIdent in detailsIdents)
            {
                if (!datFiles.Cell.TryFetch<TerrainTileExtras>(detailsIdent, out TerrainTileExtras? details)
                    || details is null
                    || details.CellCount is 0)

                    continue;

                uint lead = (detailsIdent & 0xFFFF_0000u) | 0x0100u;
                for (uint shift = 0; shift < details.CellCount; ++shift)
                    chamberIdents.Add(lead + shift);
            }

            return chamberIdents;
        }
    }

    // A keyed unit of work: one pak key, its asset type and the DAT file behind it
    private readonly record struct MeshOrder(ulong Key, PakAssetKind Type, uint FileId)
    {
        public static MeshOrder For(PakAssetKind kind, uint fileIdent) => new(PakTag.Compose(kind, fileIdent), kind, fileIdent);
    }
}
