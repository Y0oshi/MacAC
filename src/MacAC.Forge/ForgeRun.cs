using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Assets.Pak;

namespace MacAC.Forge;

// One pass over the DAT collection into a staged pak
internal sealed partial class ForgeRun
{
    // Items extracted in parallel per batch before the sequential sorted write
    private const int Stride = 16;

    // Mesh items between forced LOH compactions
    private const int CompactionStride = 2_048;

    private const double HeadwayCadenceSecs = 5;

    private readonly ForgeJob _job;
    private readonly string _publishedTrail;
    private readonly CancellationToken _abort;
    private readonly ParallelOptions _fanOut;
    private readonly ConcurrentBag<Fault> _flaws = [];
    private readonly HashSet<ulong> _written = [];
    private readonly Stopwatch _sinceDossier = Stopwatch.StartNew();

    public ForgeRun(ForgeJob job, string publishedTrail)
    {
        _job = job;
        _publishedTrail = publishedTrail;
        _abort = job.AbortTicket;
        _fanOut = new ParallelOptions
        {
            MaxDegreeOfParallelism = job.Threads,
            CancellationToken = _abort,
        };
    }

    public ForgeTally Execute()
    {
        Console.WriteLine("macac-bake");
        Console.WriteLine($"dat dir: {_job.DatDirection}");
        Console.WriteLine($"out:     {_publishedTrail}");
        Console.WriteLine($"threads: {_job.Threads}");
        Console.WriteLine();

        Stopwatch timer = Stopwatch.StartNew();
        using DatVault datFiles = new DatVault(_job.DatDirection);
        using DatCollectionBridge bridge = new DatCollectionBridge(datFiles);
        var flankLined = new ConcurrentQueue<HarvestedMesh>();
        MeshHarvester harvester = new MeshHarvester(bridge, new StderrLogger(nameof(MeshHarvester)), flankLined.Enqueue);

        Manifest manifest = Manifest.Assemble(datFiles, bridge, _job, _flaws, _abort);
        manifest.Proclaim();

        PakPreamble preamble = new()
        {
            FmtVer = PakFmt.LatestFmtVer,
            PortalIteration = (uint)datFiles.Portal.Revision!.Current,
            CellIteration = (uint)datFiles.Cell.Revision!.Current,
            HighResIteration = (uint)datFiles.HighRes.Revision!.Current,
            LanguageIteration = (uint)datFiles.Local.Revision!.Current,
            BakeToolVer = PakFmt.LatestBakeToolVer,
        };

        using PakEmitter writer = new PakEmitter(_job.OutTrail, preamble);
        MeshTally triMeshes = ForgeTriMeshes(writer, bridge, harvester, flankLined, manifest);
        var link = ForgeLink(writer, bridge, manifest, triMeshes.SideStagedFileIds);

        writer.Finish();
        timer.Stop();

        var kindCounts = new Dictionary<PakAssetKind, int>
        {
            [PakAssetKind.GfxObjMesh] = triMeshes.GfxObj + triMeshes.SideStaged,
            [PakAssetKind.SetupMesh] = triMeshes.Setup,
            [PakAssetKind.EnvCellMesh] = triMeshes.EnvCellKeys,
            [PakAssetKind.GfxObjCollision] = link.GfxObj.Keys,
            [PakAssetKind.SetupCollision] = link.Setup.Keys,
            [PakAssetKind.CellStructureCollision] = link.CellStruct.Keys,
            [PakAssetKind.EnvCellTopology] = link.Topologies,
            [PakAssetKind.TexturePayload] = writer.TextureBlobTally,
        };
        using Process self = Process.GetCurrentProcess();
        ForgeTally count = new ForgeTally
        {
            Header = preamble,
            GfxObjRefTags = triMeshes.GfxObj,
            RigTags = triMeshes.Setup,
            EnvironChamberTags = triMeshes.EnvCellKeys,
            UniqueEnvironChamberGeometries = triMeshes.EnvCellGeometries,
            EnvironChamberAliases = triMeshes.EnvCellKeys - triMeshes.EnvCellGeometries,
            GfxObjRefImpactTags = link.GfxObj.Keys,
            UniqueGfxObjRefImpacts = link.GfxObj.Blobs,
            GfxObjRefImpactAliases = link.GfxObj.Aliases,
            RigImpactTags = link.Setup.Keys,
            UniqueRigImpacts = link.Setup.Blobs,
            RigImpactAliases = link.Setup.Aliases,
            ChamberStructureImpactTags = link.CellStruct.Keys,
            UniqueChamberStructureImpacts = link.CellStruct.Blobs,
            ChamberStructureImpactAliases = link.CellStruct.Aliases,
            EnvironChamberWiringTags = link.Topologies,
            TextureCargoTags = writer.TextureBlobTally,
            FlankLinedTags = triMeshes.SideStaged,
            FlankLinedDuplicateTags = triMeshes.SideStagedDuplicates,
            PhysicalBlobs = writer.PhysicalBlobTally,
            SumTags = writer.ListingTally,
            Failures = _flaws.Count,
            ExtractionAndEmitPassed = timer.Elapsed,
            Passed = timer.Elapsed,
            ProductOctets = new FileInfo(_job.OutTrail).Length,
            PeakWorkingSetOctets = self.PeakWorkingSet64,
            PeakPrivateOctets = self.PeakPagedMemorySize64,
            DecodedCargoBytes = writer.DecodedCargoOctets,
            StoredCargoBytes = writer.StoredCargoOctets,
            CompressedBlobs = writer.CompressedBlobTally,
            KindCounts = kindCounts,
        };

        ForgeSummary.DumpFlaws(_flaws);
        return count;
    }

    // One DAT record the forge could not turn into a pak entry
    internal sealed record Fault(PakAssetKind Type, uint FileId, string Reason);

    private void Fail(PakAssetKind kind, uint fileIdent, string cause) =>
        _flaws.Add(new Fault(kind, fileIdent, cause));

    // A counter that lambdas on worker threads can bump
    private sealed class Meter
    {
        public long Done;

        public void Bump() => Interlocked.Increment(ref Done);

        public void Bump(int by) => Interlocked.Add(ref Done, by);
    }

    // Slices gearList into Stride-sized arrays with an is-last flag
    private IEnumerable<(T[] Batch, bool Last)> Slices<T>(IReadOnlyList<T> gearList)
    {
        for (int begin = 0; begin < gearList.Count; begin += Stride)
        {
            _abort.ThrowIfCancellationRequested();
            yield return (gearList.Skip(begin).Take(Stride).ToArray(), begin + Stride >= gearList.Count);
        }
    }

    // Runs corpus over a batch in parallel
    private TResult[] Fan<TItem, TResult>(
        TItem[] lot,
        Meter gauge,
        Func<TItem, TResult?> corpus,
        Action<TItem, Exception> onProblem)
        where TResult : class
    {
        var yield = new ConcurrentBag<TResult>();
        Parallel.ForEach(
            lot,
            _fanOut,
            gear =>
            {
                try
                {
                    if (corpus(gear) is { } outcome)
                        yield.Add(outcome);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception problem)
                {
                    onProblem(gear, problem);
                }
                finally
                {
                    gauge.Bump();
                }
            });
        return yield.ToArray();
    }

    private void Report(string stage, Meter gauge, int sum, TimeSpan passed, bool final)
    {
        if (!final && _sinceDossier.Elapsed.TotalSeconds < HeadwayCadenceSecs)
            return;

        long done = gauge.Done;
        double rate = passed.TotalSeconds > 0 ? done / passed.TotalSeconds : 0;
        double eta = rate > 0 ? (sum - done) / rate : 0;
        using Process self = Process.GetCurrentProcess();
        self.Refresh();
        ForgeProgressLine.Write(
            Console.Out,
            _job.Progress,
            stage,
            done,
            sum,
            _flaws.Count,
            passed,
            eta,
            self.PrivateMemorySize64,
            GC.GetGCMemoryInfo().HeapSizeBytes);
        _sinceDossier.Restart();
    }

    // Forces a compacting LOH collection every CompactionStride items and at the end of a phase
    private static void CompactIfDue(int gearListSoFaraway, bool final)
    {
        if (!final && gearListSoFaraway % CompactionStride is not 0)
            return;

        var earlier = GCSettings.LargeObjectHeapCompactionMode;
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        finally
        {
            GCSettings.LargeObjectHeapCompactionMode = earlier;
        }
    }
}
