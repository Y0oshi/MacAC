using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Assets.Pak;
using DatEnvironment =  MacAC.Dat.InteriorShell;

namespace MacAC.Forge;

internal sealed partial class ForgeRun
{
    private sealed record MeshTally(
        int GfxObj,
        int Setup,
        int EnvCellKeys,
        int EnvCellGeometries,
        int SideStaged,
        int SideStagedDuplicates,
        uint[] SideStagedFileIds);

    private sealed record MeshYield(ulong Key, PakAssetKind Type, HarvestedMesh Data);

    private sealed record ShellYield(CellShellFamily Family, HarvestedMesh Data);

    private MeshTally ForgeTriMeshes(
        PakEmitter writer,
        DatCollectionBridge bridge,
        MeshHarvester harvester,
        ConcurrentQueue<HarvestedMesh> flankLined,
        Manifest manifest)
    {
        Stopwatch timer = Stopwatch.StartNew();
        Meter gauge = new Meter();
        int sum = manifest.TriMeshJob.Count + manifest.Shells.UniqueGeoTally;
        var lined = new Dictionary<ulong, HarvestedMesh>();
        int gfxObjRef = 0, rig = 0, environChamberTags = 0, environChamberGeometries = 0, duplicates = 0;
        int observed = 0;

        foreach (var (lot, previous) in Slices(manifest.TriMeshJob))
        {
            MeshYield[] yields = Fan<MeshOrder, MeshYield>(
                lot,
                gauge,
                ordering =>
                {
                    var triMesh = harvester.Harvest(ordering.FileId, ordering.Type == PakAssetKind.SetupMesh, _abort);
                    if (triMesh is null)
                        Fail(ordering.Type, ordering.FileId, "extractor returned null");
                    return triMesh is null ? null : new MeshYield(ordering.Key, ordering.Type, triMesh);
                },
                (ordering, problem) => Fail(ordering.Type, ordering.FileId, problem.Message));

            foreach (MeshYield yield in yields.OrderBy(static y => y.Key))
            {
                writer.AddBlob(yield.Key, yield.Data);
                _written.Add(yield.Key);
                if (yield.Type == PakAssetKind.GfxObjMesh)
                    ++gfxObjRef;
                else
                    ++rig;
            }

            duplicates += Drain(flankLined, lined, manifest.ScheduledTriMeshTags);
            observed += lot.Length;
            CompactIfDue(observed, previous);
            Report("mesh", gauge, sum, timer.Elapsed, previous && manifest.Shells.UniqueGeoTally is 0);
        }

        observed = 0;
        foreach (var (lot, previous) in Slices(manifest.Shells.Groups))
        {
            var yields = Fan<CellShellFamily, ShellYield>(
                lot,
                gauge,
                clan =>
                {
                    uint surroundingsDid = 0x0D00_0000u | clan.EnvironmentId;
                    if (!bridge.Portal.TryGet<DatEnvironment>(surroundingsDid, out DatEnvironment? surroundings)
                        || surroundings is null
                        || !surroundings.Cells.TryGetValue(clan.CellStructure, out ShellCell? form))
                    {
                        Fail(
                            PakAssetKind.EnvCellMesh,
                            clan.FileIds[0],
                            $"environment 0x{surroundingsDid:X8} cell structure {clan.CellStructure} was not found");
                        return null;
                    }

                    var triMesh = harvester.HarvestChamberStruct(
                        clan.GeometryId,
                        form,
                        clan.Surfaces,
                        Matrix4x4.Identity,
                        _abort);
                    if (triMesh is null)
                        Fail(PakAssetKind.EnvCellMesh, clan.FileIds[0], "unique cell-structure extraction returned null");
                    return triMesh is null ? null : new ShellYield(clan, triMesh);
                },
                (clan, problem) => Fail(PakAssetKind.EnvCellMesh, clan.FileIds[0], problem.Message));

            foreach (var (family, triMesh) in yields.OrderBy(static y => y.Family.FileIds[0]))
            {
                ulong primary = PakTag.Compose(PakAssetKind.EnvCellMesh, family.FileIds[0]);
                writer.AddBlob(primary, triMesh);
                _written.Add(primary);
                ++environChamberTags;
                ++environChamberGeometries;

                foreach (uint twin in family.FileIds.AsSpan(1))
                {
                    ulong alias = PakTag.Compose(PakAssetKind.EnvCellMesh, twin);
                    writer.AppendAlias(alias, primary);
                    _written.Add(alias);
                    ++environChamberTags;
                }
            }

            duplicates += Drain(flankLined, lined, manifest.ScheduledTriMeshTags);
            observed += lot.Length;
            CompactIfDue(observed, previous);
            Report("mesh", gauge, sum, timer.Elapsed, previous);
        }

        int flushed = 0;
        foreach (var (tag, triMesh) in lined.OrderBy(static duo => duo.Key))
        {
            _abort.ThrowIfCancellationRequested();
            if (_written.Contains(tag))
            {
                ++duplicates;
                continue;
            }

            writer.AddBlob(tag, triMesh);
            _written.Add(tag);
            ++flushed;
        }

        uint[] linedFileIdents = lined.Keys.Select(static lookupKey => PakTag.Decompose(lookupKey).FileId).ToArray();
        return new MeshTally(gfxObjRef, rig, environChamberTags, environChamberGeometries, flushed, duplicates, linedFileIdents);
    }

    private int Drain(
        ConcurrentQueue<HarvestedMesh> flankLined,
        Dictionary<ulong, HarvestedMesh> lined,
        HashSet<ulong> scheduled)
    {
        int duplicates = 0;
        while (flankLined.TryDequeue(out HarvestedMesh? triMesh))
        {
            ulong tag = PakTag.Compose(PakAssetKind.GfxObjMesh, (uint)(triMesh.ObjectId & 0xFFFF_FFFFu));
            if (_written.Contains(tag) || scheduled.Contains(tag) || !lined.TryAdd(tag, triMesh))
                ++duplicates;
        }

        return duplicates;
    }
}
