using System.Diagnostics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Assets.Pak;
using MacAC.Mechanics.Kinetics;
using DatEnvironment =  MacAC.Dat.InteriorShell;

namespace MacAC.Forge;

internal sealed partial class ForgeRun
{
    // Keys written for one contact asset type, split into physical blobs and byte-identical aliases
    private struct AliasCount
    {
        public int Keys;
        public int Blobs;
        public int Aliases;

        public void Add(int tags, int blobs, int aliases)
        {
            Keys += tags;
            Blobs += blobs;
            Aliases += aliases;
        }
    }

    private sealed record ContactTally(AliasCount GfxObj, AliasCount Setup, AliasCount CellStruct, int Topologies);

    private sealed record ContactYield(ulong Key, PakAssetKind Type, uint FileId, byte[] Payload);

    private sealed record StructYield(CellShellFamily Family, PackedCellStructContactAsset Structure, byte[] Payload);

    // Phase two: packed contact geometry
    private ContactTally ForgeLink(
        PakEmitter writer,
        DatCollectionBridge bridge,
        Manifest manifest,
        uint[] flankLinedFileIdents)
    {
        uint[] gfxIdents = manifest.GfxObjRefIdents.Concat(flankLinedFileIdents).Distinct().Order().ToArray();
        List<MeshOrder> job = new List<MeshOrder>(gfxIdents.Length + manifest.RigIdents.Count);
        job.AddRange(gfxIdents.Select(static ident => MeshOrder.For(PakAssetKind.GfxObjCollision, ident)));
        job.AddRange(manifest.RigIdents.Select(static ident => MeshOrder.For(PakAssetKind.SetupCollision, ident)));
        job.Sort(static (a, b) => a.Key.CompareTo(b.Key));

        var twins = new Dictionary<PakAssetKind, ByteTwinIndex>
        {
            [PakAssetKind.GfxObjCollision] = new(),
            [PakAssetKind.SetupCollision] = new(),
            [PakAssetKind.CellStructureCollision] = new(),
        };
        AliasCount gfx = default, rig = default, chamberStruct = default;
        int topologies = 0;
        Meter gauge = new Meter();
        int sum = job.Count + manifest.Shells.UniqueGeoTally + manifest.Shells.ChamberTally;
        Stopwatch timer = Stopwatch.StartNew();
        _sinceDossier.Restart();

        Console.WriteLine();
        Console.WriteLine(
            $"collision catalog: {gfxIdents.Length:N0} GfxObj, "
            + $"{manifest.RigIdents.Count:N0} Setup, "
            + $"{manifest.Shells.UniqueGeoTally:N0} CellStruct source groups, "
            + $"{manifest.Shells.ChamberTally:N0} EnvCell topology records");

        foreach (var (lot, _) in Slices(job))
        {
            var yields = Fan<MeshOrder, ContactYield>(
                lot,
                gauge,
                ordering => BundleLink(bridge, ordering),
                (ordering, problem) => Fail(ordering.Type, ordering.FileId, problem.Message));

            foreach (ContactYield yield in yields.OrderBy(static y => y.Key))
            {
                bool aliased = EmitOrAlias(writer, yield.Key, yield.Payload, twins[yield.Type]);
                _written.Add(yield.Key);
                ref AliasCount tally = ref (yield.Type == PakAssetKind.GfxObjCollision ? ref gfx : ref rig);
                tally.Add(1, aliased ? 0 : 1, aliased ? 1 : 0);
            }

            Report("collision", gauge, sum, timer.Elapsed, final: false);
        }

        foreach (var (lot, _) in Slices(manifest.Shells.Groups))
        {
            var yields = Fan<CellShellFamily, StructYield>(
                lot,
                gauge,
                clan =>
                {
                    uint surroundingsDid = 0x0D00_0000u | clan.EnvironmentId;
                    if (!bridge.Portal.TryGet<DatEnvironment>(surroundingsDid, out DatEnvironment? surroundings)
                        || surroundings is null
                        || !surroundings.Cells.TryGetValue(clan.CellStructure, out ShellCell? form)
                        || form is null)
                    {
                        Fail(
                            PakAssetKind.CellStructureCollision,
                            clan.FileIds[0],
                            $"environment 0x{surroundingsDid:X8} cell structure {clan.CellStructure} was not found");
                        gauge.Bump(clan.FileIds.Length);
                        return null;
                    }

                    var structure = PackedContactAssetBuilder.FlattenChamberStructure(form);
                    return new StructYield(clan, structure, PackedContactCodec.Serialize(structure, _abort));
                },
                (clan, problem) =>
                {
                    Fail(PakAssetKind.CellStructureCollision, clan.FileIds[0], problem.Message);
                    gauge.Bump(clan.FileIds.Length);
                });

            foreach (StructYield yield in yields.OrderBy(static y => y.Family.FileIds[0]))
            {
                (int blobs, int aliases) = EmitClan(
                    writer,
                    PakAssetKind.CellStructureCollision,
                    yield.Family.FileIds,
                    yield.Payload,
                    twins[PakAssetKind.CellStructureCollision]);
                chamberStruct.Add(yield.Family.FileIds.Length, blobs, aliases);

                foreach (var (chambers, _) in Slices(yield.Family.FileIds))
                {
                    var records = Fan<uint, ContactYield>(
                        chambers,
                        gauge,
                        fileIdent =>
                        {
                            if (!bridge.Cell.TryGet<RoomCell>(fileIdent, out RoomCell? chamber) || chamber is null)
                            {
                                Fail(PakAssetKind.EnvCellTopology, fileIdent, "EnvCell DAT record was not found");
                                return null;
                            }

                            var wiring = PackedContactAssetBuilder.FlattenEnvCellTopology(
                                fileIdent,
                                chamber,
                                yield.Structure.PortalPolygons);
                            return new ContactYield(
                                PakTag.Compose(PakAssetKind.EnvCellTopology, fileIdent),
                                PakAssetKind.EnvCellTopology,
                                fileIdent,
                                PackedContactCodec.Serialize(wiring, _abort));
                        },
                        (fileIdent, problem) => Fail(PakAssetKind.EnvCellTopology, fileIdent, problem.Message));

                    foreach (ContactYield capture in records.OrderBy(static r => r.Key))
                    {
                        writer.AddBlob(capture.Key, capture.Payload);
                        _written.Add(capture.Key);
                        ++topologies;
                    }

                    Report("collision", gauge, sum, timer.Elapsed, final: false);
                }
            }
        }

        timer.Stop();
        Report("collision", gauge, sum, timer.Elapsed, final: true);
        return new ContactTally(gfx, rig, chamberStruct, topologies);
    }

    // Flattens and serializes one GfxObj or Setup contact record; null (with a fault) when the DAT
    // lacks it
    private ContactYield? BundleLink(DatCollectionBridge bridge, MeshOrder ordering)
    {
        byte[] cargo;
        if (ordering.Type == PakAssetKind.GfxObjCollision)
        {
            if (!bridge.Portal.TryGet<PartMesh>(ordering.FileId, out PartMesh? gfxObjRef) || gfxObjRef is null)
            {
                Fail(ordering.Type, ordering.FileId, "GfxObj DAT record was not found");
                return null;
            }

            var asset = PackedContactAssetBuilder.FlattenGfxObj(gfxObjRef);
            cargo = PackedContactCodec.Serialize(asset, _abort);
        }
        else
        {
            if (!bridge.Portal.TryGet<RigSpec>(ordering.FileId, out RigSpec? rig) || rig is null)
            {
                Fail(ordering.Type, ordering.FileId, "Setup DAT record was not found");
                return null;
            }

            var asset = PackedContactAssetBuilder.FlattenSetup(rig);
            cargo = PackedContactCodec.Serialize(asset, _abort);
        }

        return new ContactYield(ordering.Key, ordering.Type, ordering.FileId, cargo);
    }

    // Writes the payload under tag, or aliases a byte-identical earlier blob
    private static bool EmitOrAlias(PakEmitter writer, ulong tag, byte[] cargo, ByteTwinIndex twins)
    {
        if (twins.Match(cargo, out ulong primary))
        {
            writer.AppendAlias(tag, primary);
            return true;
        }

        writer.AddBlob(tag, cargo);
        twins.Remember(tag, cargo);
        return false;
    }

    private (int Blobs, int Aliases) EmitClan(
        PakEmitter writer,
        PakAssetKind kind,
        IReadOnlyList<uint> fileIdents,
        byte[] cargo,
        ByteTwinIndex twins)
    {
        if (fileIdents.Count is 0)
            throw new InvalidDataException("collision alias group is empty");

        int from = 0;
        int blobs = 0;
        if (!twins.Match(cargo, out ulong primary))
        {
            primary = PakTag.Compose(kind, fileIdents[0]);
            writer.AddBlob(primary, cargo);
            twins.Remember(primary, cargo);
            _written.Add(primary);
            from = 1;
            blobs = 1;
        }

        int aliases = 0;
        for (int idx = from; idx < fileIdents.Count; ++idx)
        {
            ulong alias = PakTag.Compose(kind, fileIdents[idx]);
            writer.AppendAlias(alias, primary);
            _written.Add(alias);
            ++aliases;
        }

        return (blobs, aliases);
    }
}
