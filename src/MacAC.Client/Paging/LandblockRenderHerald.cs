using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Stride;
using MacAC.Mechanics.Landscape;

namespace MacAC.Client.Paging;

public sealed class LandblockRasterizeBulletin
{
    internal LandblockRasterizeBulletin(
        object holder,
        LandblockAssemble assemble,
        LandblockTessellationData triMeshBlob,
        Vector3 origin,
        Dictionary<uint, FetchedChamber> visChambers,
        Vector3 aabbLower,
        Vector3 aabbUpper,
        StructureRegistryBulletin? structureBulletin,
        EnvironChamberLandblockBulletin? environChamberBulletin)
    {
        Owner = holder;
        Build = assemble;
        TriMeshBlob = triMeshBlob;
        Origin = origin;
        _visChambers = new ReadOnlyDictionary<uint, FetchedChamber>(visChambers);
        AabbLower = aabbLower;
        AabbUpper = aabbUpper;
        StructureBulletin = structureBulletin;
        EnvironChamberBulletin = environChamberBulletin;
    }

    internal object Owner { get; }
    internal LandblockAssemble Build { get; }
    internal LandblockTessellationData TriMeshBlob { get; }
    internal Vector3 AabbLower { get; }
    internal Vector3 AabbUpper { get; }
    internal StructureRegistryBulletin? StructureBulletin { get; }
    internal EnvironChamberLandblockBulletin? EnvironChamberBulletin { get; }
    internal bool LandSealed { get; set; }
    internal bool VisSealed { get; set; }
    internal bool AabbSealed { get; set; }
    internal bool CommenceSealed { get; set; }
    internal bool StructureRegistrySealed { get; set; }
    internal bool EnvironChambersSealed { get; set; }
    internal bool WrapUpSealed { get; set; }

    public uint LandblockId => Build.Landblock.LandblockId;
    public Vector3 Origin { get; }
    private readonly IReadOnlyDictionary<uint, FetchedChamber> _visChambers;

    public IReadOnlyDictionary<uint, FetchedChamber> VisCells => _visChambers;
}

public readonly record struct LandblockRenderHeraldTelemetry(
    long BeginCount,
    long CompleteCount,
    long TerrainPublishTicks,
    long BeginPublishTicks,
    long CompletePublishTicks,
    long EnvCellPreparationCount,
    long TerrainRemovalCount,
    long CellVisibilityRemovalCount,
    long BuildingRegistryRemovalCount,
    long EnvCellRemovalCount);

public sealed class LandblockRenderHerald
{
    private readonly object _receiptHolder = new();
    private readonly Action<uint, LandblockTessellationData, Vector3> _broadcastLand;
    private readonly Action<uint> _dropLand;
    private readonly ChamberVis _chamberVis;
    private readonly GpuRealmPhase _realmPhase;
    private readonly Action<EnvironChamberLandblockAssemble>? _sealEnvironChambers;
    private readonly IEnvCellLandblockHerald? _environChamberPublisher;
    private readonly Action<EnvironChamberLandblockAssemble>? _readyEnvironChambers;
    private readonly Action<uint>? _dropEnvironChambers;
    private readonly Dictionary<uint, StructureRegistry> _buildingRegistries = [];
    private long _commenceTally;
    private long _doneTally;
    private long _landBroadcastBeats;
    private long _commenceBroadcastBeats;
    private long _doneBroadcastBeats;
    private long _environChamberPrepTally;
    private long _landDeletionTally;
    private long _chamberVisDeletionTally;
    private long _structureRegistryDeletionTally;
    private long _environChamberDeletionTally;

    public LandblockRenderHerald(
        Action<uint, LandblockTessellationData, Vector3> broadcastLand,
        Action<uint> dropLand,
        ChamberVis chamberVis,
        GpuRealmPhase realmPhase,
        Action<EnvironChamberLandblockAssemble>? sealEnvironChambers = null,
        Action<EnvironChamberLandblockAssemble>? readyEnvironChambers = null,
        Action<uint>? dropEnvironChambers = null,
        IEnvCellLandblockHerald? envCellPublisher = null)
    {
        ArgumentNullException.ThrowIfNull(broadcastLand);
        ArgumentNullException.ThrowIfNull(dropLand);
        ArgumentNullException.ThrowIfNull(chamberVis);
        ArgumentNullException.ThrowIfNull(realmPhase);

        _broadcastLand = broadcastLand;
        _dropLand = dropLand;
        _chamberVis = chamberVis;
        _realmPhase = realmPhase;
        _sealEnvironChambers = sealEnvironChambers;
        _environChamberPublisher = envCellPublisher;
        if (_sealEnvironChambers is not null && _environChamberPublisher is not null)
        {
            throw new ArgumentException(
                "Supply either the retained EnvCell publisher or the compatibility callback, not both",
                nameof(envCellPublisher));
        }
        _readyEnvironChambers = readyEnvironChambers;
        _dropEnvironChambers = dropEnvironChambers;
    }

    public IReadOnlyCollection<StructureRegistry> BuildingRegistries =>
        _buildingRegistries.Values;

    public StrollStructureRegistry StrollBuildings { get; } = new();

    public StrideLandscapeAssembler StrideScenery { get; } = new();

    public LandblockRenderHeraldTelemetry Diagnostics
    {
        get
        {
            return new(
        _commenceTally,
        _doneTally,
        _landBroadcastBeats,
        _commenceBroadcastBeats,
        _doneBroadcastBeats,
        _environChamberPrepTally,
        _landDeletionTally,
        _chamberVisDeletionTally,
        _structureRegistryDeletionTally,
        _environChamberDeletionTally);
        }
    }

    public LandblockRasterizeBulletin BeginPublication(
        LandblockAssemble assemble,
        LandblockTessellationData triMeshBlob)
    {
        var bulletin = ReadyPublication(
            assemble,
            triMeshBlob);
        BeginPublication(bulletin);
        return bulletin;
    }

    public LandblockRasterizeBulletin ReadyPublication(
        LandblockAssemble build,
        LandblockTessellationData triMeshBlob)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(triMeshBlob);
        if (!build.Origin.IsSpecified)
            throw new ArgumentException(
                "Render publication needs the build's captured origin",
                nameof(build));

        uint lbIdent = build.Landblock.LandblockId;
        if (build.EnvCells is { } contenderEnvironChambers &&
            contenderEnvironChambers.LbIdent != lbIdent)
        {
            throw new InvalidOperationException(
                $"EnvCell transaction 0x{contenderEnvironChambers.LbIdent:X8} was attached to " +
                $"landblock 0x{lbIdent:X8}.");
        }

        Vector3 origin = CalculateOrigin(lbIdent, build.Origin);

        var visChambers = new Dictionary<uint, FetchedChamber>();
        if (build.EnvCells is { } environChambers)
        {
            foreach (FetchedChamber chamber in environChambers.VisChambers)
                visChambers[chamber.CellId] = chamber;
        }
        (Vector3 aabbLower, Vector3 aabbUpper) = CalculateAabb(triMeshBlob, origin);
        StructureRegistryBulletin? structureBulletin =
            build.Landblock.PhysicsDats?.Info is { } details
                ? BuildingFetcher.ReadyBulletin(
                    details,
                    lbIdent,
                    visChambers)
                : null;
        EnvironChamberLandblockBulletin? environChamberBulletin =
            build.EnvCells is { } keptEnvironChambers
                ? _environChamberPublisher?.StageBulletin(keptEnvironChambers)
                : null;
        return new LandblockRasterizeBulletin(
            _receiptHolder,
            build,
            triMeshBlob,
            origin,
            visChambers,
            aabbLower,
            aabbUpper,
            structureBulletin,
            environChamberBulletin);
    }

    public void BeginPublication(LandblockRasterizeBulletin bulletin)
    {
        VetReceipt(bulletin);
        while (!AdvanceBeginOne(bulletin))
        {
        }
    }

    public void FinishBulletin(LandblockRasterizeBulletin bulletin)
    {
        VetReceipt(bulletin);
        if (!bulletin.CommenceSealed)
            throw new InvalidOperationException(
                "Render publication can't complete prior to its prefix commits");
        while (!ProgressDoneOne(bulletin))
        {
        }
    }

    public void ReadyFollowingRasterizePins(EnvironChamberLandblockAssemble assemble)
    {
        ArgumentNullException.ThrowIfNull(assemble);
        _readyEnvironChambers?.Invoke(assemble);
        ++_environChamberPrepTally;
    }

    public void DropLand(uint lbIdent)
    {
        _dropLand(lbIdent);
        StrideScenery.RetireLb(lbIdent);
        ++_landDeletionTally;
    }

    public void DropChamberVis(uint lbIdent)
    {
        _chamberVis.RemoveLandblock((lbIdent >> 16) & 0xFFFFu);
        ++_chamberVisDeletionTally;
    }

    public void DropStructureRegistry(uint lbIdent)
    {
        _buildingRegistries.Remove(lbIdent & 0xFFFF0000u);
        // Demotion retires buildings, not the retained far-tier terrain
        StrollBuildings.Retire(lbIdent);
        StrideScenery.WipeStructures(lbIdent);
        ++_structureRegistryDeletionTally;
    }

    public void DropSurroundingsChambers(uint lbIdent)
    {
        _dropEnvironChambers?.Invoke(lbIdent);
        ++_environChamberDeletionTally;
    }

    internal bool FitsPhase(GpuRealmPhase phase) =>
        ReferenceEquals(_realmPhase, phase);

    internal bool AdvanceBeginOne(LandblockRasterizeBulletin bulletin)
    {
        VetReceipt(bulletin);
        if (bulletin.CommenceSealed)
            return true;

        long begun = Stopwatch.GetTimestamp();
        var assemble = bulletin.Build;
        uint lbIdent = bulletin.LandblockId;
        if (!bulletin.LandSealed)
        {
            long landBegun = Stopwatch.GetTimestamp();
            _broadcastLand(lbIdent, bulletin.TriMeshBlob, bulletin.Origin);
            _landBroadcastBeats += Stopwatch.GetTimestamp() - landBegun;
            bulletin.LandSealed = true;
        }
        else if (!bulletin.VisSealed)
        {
            if (assemble.EnvCells is { } environChambers)
                _chamberVis.LockLb(lbIdent, environChambers.VisChambers);
            bulletin.VisSealed = true;
        }
        else if (bulletin.AabbSealed)
        {
            bulletin.CommenceSealed = true;
            ++_commenceTally;
        }
        else
        {
            _realmPhase.AssignLbAabb(
                lbIdent,
                bulletin.AabbLower,
                bulletin.AabbUpper);
            bulletin.AabbSealed = true;
        }

        _commenceBroadcastBeats += Stopwatch.GetTimestamp() - begun;
        return bulletin.CommenceSealed;
    }

    internal bool ProgressDoneOne(LandblockRasterizeBulletin bulletin)
    {
        VetReceipt(bulletin);
        if (!bulletin.CommenceSealed)
            throw new InvalidOperationException(
                "Render publication can't complete prior to its prefix commits");
        if (bulletin.WrapUpSealed)
            return true;

        long begun = Stopwatch.GetTimestamp();
        var assemble = bulletin.Build;
        uint lbIdent = bulletin.LandblockId;
        if (bulletin.StructureBulletin is { PrepSealed: false }
            structureBulletin)
        {
            BuildingFetcher.ProgressPrepOne(structureBulletin);
        }
        else if (!bulletin.StructureRegistrySealed)
        {
            if (bulletin.StructureBulletin is { } finishedStructures)
            {
                BuildingFetcher.SealBulletin(finishedStructures);
                uint registryTag = lbIdent & 0xFFFF0000u;
                _buildingRegistries[registryTag] =
                    finishedStructures.Registry;
            }
            if (assemble.EnvCells is { } strollEnvironChambers)
                StrollBuildings.Publish(lbIdent, strollEnvironChambers.StrollStructures);
            else
                StrollBuildings.Retire(lbIdent);
            StrideScenery.BroadcastLb(
                lbIdent,
                assemble.TerrainBounds.MaxZ,
                assemble.TerrainBounds.MinZ,
                assemble.EnvCells is { } chambers
                    ? chambers.StrollStructures
                    : Array.Empty<StrideBuildingMint.Entry>());
            bulletin.StructureRegistrySealed = true;
        }
        else if (bulletin.EnvironChamberBulletin is { } environChamberBulletin
            && !environChamberBulletin.PrepSealed)
        {
            _environChamberPublisher?.StepPrepOne(environChamberBulletin);
        }
        else if (bulletin.EnvironChambersSealed)
        {
            bulletin.WrapUpSealed = true;
            ++_doneTally;
        }
        else
        {
            if (bulletin.EnvironChamberBulletin is { } kept)
            {
                (_environChamberPublisher
                    ?? throw new InvalidOperationException(
                        "A retained EnvCell receipt has no owning publisher"))
                    .LockBulletin(kept);
            }
            else if (assemble.EnvCells is { } environChambers)
            {
                _sealEnvironChambers?.Invoke(environChambers);
            }
            bulletin.EnvironChambersSealed = true;
        }

        _doneBroadcastBeats += Stopwatch.GetTimestamp() - begun;
        return bulletin.WrapUpSealed;
    }

    private static Vector3 CalculateOrigin(
        uint lbIdent,
        LandblockAssembleOrigin grabbedOrigin)
    {
        int lbX = (int)((lbIdent >> 24) & 0xFFu);
        int lbY = (int)((lbIdent >> 16) & 0xFFu);
        return new Vector3(
            (lbX - grabbedOrigin.CenterX) * 192f,
            (lbY - grabbedOrigin.CenterY) * 192f,
            0f);
    }

    private static (Vector3 Min, Vector3 Max) CalculateAabb(
        LandblockTessellationData triMeshBlob,
        Vector3 origin)
    {
        float zLower = float.MaxValue;
        float zUpper = float.MinValue;
        foreach (TerrainVert vert in triMeshBlob.Vertices)
        {
            float z = vert.Position.Z;
            if (z < zLower) zLower = z;
            if (z > zUpper) zUpper = z;
        }
        if (zLower == float.MaxValue)
        {
            zLower = 0f;
            zUpper = 0f;
        }

        zUpper += 50f;
        zLower -= 10f;
        return (
            new Vector3(origin.X, origin.Y, zLower),
            new Vector3(origin.X + 192f, origin.Y + 192f, zUpper));
    }

    private void VetReceipt(LandblockRasterizeBulletin publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!ReferenceEquals(publication.Owner, _receiptHolder))
        {
            throw new ArgumentException(
                "The render publication receipt belongs to another publisher",
                nameof(publication));
        }
    }
}
