using System.Diagnostics;
using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public sealed class LandblockKineticsPublication : IDisposable
{
    internal LandblockKineticsPublication(
        object holder,
        LandblockAssemble assemble,
        Vector3 origin,
        uint latestChamberIdent,
        BuildingSpec[] structures,
        uint[] precedingStaticHolderIdents,
        SimKineticsLedger kinetics,
        SimContactIntake impactAdmission,
        StagedLandblockContactEpoch readiedGen)
    {
        Owner = holder;
        Build = assemble;
        Origin = origin;
        LatestChamberIdent = latestChamberIdent;
        Buildings = structures;
        PrecedingStaticHolderIdents = precedingStaticHolderIdents;
        Physics = kinetics;
        ImpactAdmission = impactAdmission;
        ReadiedGen = readiedGen;
    }

    internal object Owner { get; }
    internal LandblockAssemble Build { get; }
    internal uint LatestChamberIdent { get; }
    internal BuildingSpec[] Buildings { get; }
    internal uint[] PrecedingStaticHolderIdents { get; }
    internal SimKineticsLedger Physics { get; }
    internal SimContactIntake ImpactAdmission { get; }
    internal StagedLandblockContactEpoch ReadiedGen { get; }
    internal KineticAssetCache StagingCache => ReadiedGen.DataCache;
    internal KineticEngine LoadingEngine => ReadiedGen.Engine;
    internal SortedSet<uint> GfxObjectIdentSet { get; } = [];
    internal uint[] GfxObjectIdents { get; set; } = [];
    internal int PrepCur { get; set; }
    internal bool PrepSealed { get; set; }
    internal bool PrecedingStashRemoved { get; set; }
    internal LandCanvas? LandCanvas { get; set; }
    internal List<CellFacet> ChamberCanvases { get; } = [];
    internal List<PortalFace> GatewayPlanes { get; } = [];
    internal uint ChamberCur { get; set; }
    internal int StructureCur { get; set; }
    internal bool BaseSealed { get; set; }
    internal int GfxCur { get; set; }
    internal uint[] RigObjectIdents { get; set; } = [];
    internal int RigCur { get; set; }
    internal int PrecedingStaticCur { get; set; }
    internal int StaticCur { get; set; }
    internal int BspHolderTally { get; set; }
    internal int CylinderHolderTally { get; set; }
    internal int NoImpactTally { get; set; }
    internal int SceneryTried { get; set; }
    internal IReadOnlyList<uint>? RefloodHolderIdents { get; set; }
    internal int RefloodCur { get; set; }
    internal bool RefloodSealed { get; set; }
    internal bool SealSealed { get; set; }
    internal bool EngineAlterationSealed { get; set; }
    internal bool CoreAlterationQueued { get; set; }
    internal bool CommenceSealed { get; set; }
    internal bool WrapUpSealed { get; set; }
    internal bool AbortAsked { get; private set; }

    public void Dispose()
    {
        if (!TryAbort())
        {
            throw new InvalidOperationException(
                "Collision publication cancellation is waiting for exact placement acknowledgements and must remain retained");
        }
    }

    internal bool TryAbort()
    {
        if (WrapUpSealed)
            return true;
        AbortAsked = true;
        for (int sample = 0; sample < 2; sample++)
        {
            if (Physics.AbortImpactGen(
                    ImpactAdmission,
                    ReadiedGen))
            {
                return true;
            }
            if (Physics.CaptureOwnership()
                    .PendingCollisionPrefixProjectionCount != 0)
            {
                return false;
            }
        }
        return false;
    }

    public uint LandblockId => Build.Landblock.LandblockId;
    public Vector3 Origin { get; }
}

public readonly record struct LandblockKineticsHeraldTelemetry(
    long BeginCount,
    long CompleteCount,
    long BasePublishTicks,
    long GfxCacheTicks,
    long CompletePublishTicks,
    long CellSurfaceCount,
    long PortalPlaneCount,
    long BuildingCount,
    long StaticBspOwnerCount,
    long StaticCylinderOwnerCount,
    long RefloodCount,
    long DemotionCount,
    long FullRemovalCount);

public sealed class LandblockKineticsHerald
{
    private readonly object _receiptHolder = new();
    private readonly SimKineticsLedger _physics;
    private readonly KineticEngine _kineticsEngine;
    private readonly KineticAssetCache _kineticsBlobStash;
    private readonly float[] _heightTable;

    private long _commenceTally;
    private long _doneTally;
    private long _baseBroadcastBeats;
    private long _gfxStashBeats;
    private long _doneBroadcastBeats;
    private long _chamberCanvasTally;
    private long _gatewayPlaneTally;
    private long _structureTally;
    private long _staticBspHolderTally;
    private long _staticCylinderHolderTally;
    private long _refloodTally;
    private long _demotionTally;
    private long _wholeDeletionTally;

    public LandblockKineticsHerald(
        SimKineticsLedger kinetics,
        float[] heightTable)
    {
        ArgumentNullException.ThrowIfNull(kinetics);
        ArgumentNullException.ThrowIfNull(heightTable);
        if (heightTable.Length < 256)
            throw new ArgumentException(
                "The retail terrain height table must contain no fewer than 256 entries",
                nameof(heightTable));

        _physics = kinetics;
        _kineticsEngine = kinetics.Engine;
        _kineticsBlobStash = kinetics.DataCache;
        _heightTable = (float[])heightTable.Clone();
    }

    public LandblockKineticsHeraldTelemetry Diagnostics => new(
        _commenceTally,
        _doneTally,
        _baseBroadcastBeats,
        _gfxStashBeats,
        _doneBroadcastBeats,
        _chamberCanvasTally,
        _gatewayPlaneTally,
        _structureTally,
        _staticBspHolderTally,
        _staticCylinderHolderTally,
        _refloodTally,
        _demotionTally,
        _wholeDeletionTally);

    public LandblockKineticsPublication BeginPublication(
        LandblockRasterizeBulletin rasterizeBulletin)
    {
        LandblockKineticsPublication bulletin = ReadyBulletin(
            rasterizeBulletin);
        BeginPublication(bulletin);
        return bulletin;
    }

    public LandblockKineticsPublication ReadyBulletin(
        LandblockRasterizeBulletin rasterizeBulletin)
    {
        LandblockKineticsPublication bulletin =
            BuildBulletin(rasterizeBulletin);
        while (!ProgressPrepOne(bulletin))
        {
        }
        return bulletin;
    }

    internal LandblockKineticsPublication BuildBulletin(
        LandblockRasterizeBulletin renderPublication)
    {
        ArgumentNullException.ThrowIfNull(renderPublication);
        LandblockAssemble assemble = renderPublication.Build;
        Vector3 origin = renderPublication.Origin;
        if (!IsFinite(origin))
            throw new ArgumentOutOfRangeException(nameof(renderPublication));

        KineticDatBundle datBundle =
            assemble.Landblock.PhysicsDats ?? KineticDatBundle.Empty;
        BuildingSpec[] structures = datBundle.Info?.Structures.ToArray()
            ?? [];
        SimContactIntake impactAdmission =
            _physics.BeginCollisionAdmission(assemble.Landblock.LandblockId);
        StagedLandblockContactEpoch? readied = null;
        try
        {
            readied = _physics.ReadyImpactGen(impactAdmission);
            var bulletin = new LandblockKineticsPublication(
                _receiptHolder,
                assemble,
                origin,
                _kineticsBlobStash.ChamberGraph.CurrChamber?.Id ?? 0u,
                structures,
                _kineticsEngine.ShadeObjects.GrabStaticHoldersForLb(
                    assemble.Landblock.LandblockId),
                _physics,
                impactAdmission,
                readied)
            {
                RigObjectIdents = assemble.Collisions is { } impacts
                    ? [.. impacts.SetupIds]
                    : [.. datBundle.Setups.Keys.Order()]
            };
            bulletin.ReadiedGen.AssignAssetClosure(
                bulletin.GfxObjectIdents,
                bulletin.RigObjectIdents);
            return bulletin;
        }
        catch (Exception bulletinProblem)
        {
            if (!_physics.AbortImpactGen(impactAdmission, readied))
            {
                throw new AggregateException(
                    "Collision preparation failed and its pre-engine cancellation didn't converge",
                    bulletinProblem);
            }
            throw;
        }
    }

    internal bool ProgressPrepOne(
        LandblockKineticsPublication bulletin)
    {
        VetReceipt(bulletin);
        if (bulletin.PrepSealed)
            return true;

        if (!_physics.ProgressImpactGenPrep(
                bulletin.ImpactAdmission,
                bulletin.ReadiedGen).Completed)
        {
            return false;
        }

        IReadOnlyList<RealmActor> actors =
            bulletin.Build.Landblock.Entities;
        if (bulletin.Build.Collisions is { } impacts)
        {
            bulletin.GfxObjectIdents = [.. impacts.GfxObjIds];
            bulletin.ReadiedGen.AssignAssetClosure(
                bulletin.GfxObjectIdents,
                bulletin.RigObjectIdents);
            bulletin.PrepCur = actors.Count;
            bulletin.PrepSealed = true;
            return true;
        }

        if (bulletin.PrepCur < actors.Count)
        {
            RealmActor actor = actors[bulletin.PrepCur];
            for (int idx = 0; idx < actor.MeshRefs.Count; idx++)
            {
                uint ident = actor.MeshRefs[idx].GfxObjId;
                if ((ident & 0xFF000000u) == 0x01000000u)
                    bulletin.GfxObjectIdentSet.Add(ident);
            }
            bulletin.PrepCur++;
            return false;
        }

        bulletin.GfxObjectIdents = [.. bulletin.GfxObjectIdentSet];
        bulletin.ReadiedGen.AssignAssetClosure(
            bulletin.GfxObjectIdents,
            bulletin.RigObjectIdents);
        bulletin.PrepSealed = true;
        return true;
    }

    public void BeginPublication(LandblockKineticsPublication bulletin)
    {
        VetReceipt(bulletin);
        if (!bulletin.PrepSealed)
            throw new InvalidOperationException(
                "Physics publication can't begin prior to preparation commits");
        while (!AdvanceBeginOne(bulletin))
        {
        }
    }

    internal bool AdvanceBeginOne(LandblockKineticsPublication bulletin)
    {
        VetReceipt(bulletin);
        if (bulletin.CommenceSealed)
            return true;

        long begun = Stopwatch.GetTimestamp();
        LandblockAssemble assemble = bulletin.Build;
        Vector3 origin = bulletin.Origin;
        MountedLandblock lb = assemble.Landblock;
        KineticDatBundle datBundle =
            lb.PhysicsDats ?? KineticDatBundle.Empty;
        TerrainTileExtras? lbDetails = datBundle.Info;

        if (!bulletin.PrecedingStashRemoved)
        {
            bulletin.StagingCache.DropChambersForLb(lb.LandblockId);
            bulletin.StagingCache.DropStructuresForLb(lb.LandblockId);
            bulletin.StagingCache.ChamberGraph.DropEnvironChambersForLb(
                lb.LandblockId);
            bulletin.PrecedingStashRemoved = true;
        }
        else if (bulletin.LandCanvas is null)
        {
            uint lbX = (lb.LandblockId >> 24) & 0xFFu;
            uint lbY = (lb.LandblockId >> 16) & 0xFFu;
            var landOctets = new byte[81];
            for (int idx = 0; idx < landOctets.Length; idx++)
                landOctets[idx] = (byte)(ushort)lb.Heightmap.Samples[idx];
            bulletin.LandCanvas = new LandCanvas(
                lb.Heightmap.Heights,
                _heightTable,
                lbX,
                lbY,
                landOctets);
        }
        else if (lbDetails is not null
            && bulletin.ChamberCur < lbDetails.CellCount)
        {
            BroadcastChamber(bulletin, datBundle, bulletin.ChamberCur);
            bulletin.ChamberCur++;
        }
        else if (bulletin.StructureCur < bulletin.Buildings.Length)
        {
            BroadcastStructure(
                lb,
                datBundle,
                bulletin.StagingCache,
                bulletin.LandCanvas,
                origin,
                bulletin.Buildings[bulletin.StructureCur]);
            bulletin.StructureCur++;
        }
        else if (!bulletin.BaseSealed)
        {
            _physics.StageCollisionAssets(
                bulletin.ImpactAdmission,
                bulletin.ReadiedGen,
                new SimLandblockContactAssets(
                    lb.LandblockId,
                    bulletin.LandCanvas,
                    bulletin.ChamberCanvases.ToArray(),
                    bulletin.GatewayPlanes.ToArray(),
                    origin.X,
                    origin.Y,
                    bulletin.LatestChamberIdent));
            bulletin.BaseSealed = true;
        }
        else
        {
            _chamberCanvasTally += bulletin.ChamberCanvases.Count;
            _gatewayPlaneTally += bulletin.GatewayPlanes.Count;
            _structureTally += bulletin.Buildings.Length;
            bulletin.CommenceSealed = true;
            _commenceTally++;
        }

        _baseBroadcastBeats += Stopwatch.GetTimestamp() - begun;
        return bulletin.CommenceSealed;
    }

    public bool ConcludeBulletin(
        LandblockKineticsPublication bulletin,
        Action<RealmActor>? priorStaticImpact = null)
    {
        VetReceipt(bulletin);
        if (!bulletin.CommenceSealed)
            throw new InvalidOperationException(
                "Physics publication can't complete prior to its prefix commits");
        while (!ProgressDoneOne(bulletin, priorStaticImpact))
        {
            if (bulletin.CoreAlterationQueued)
            {
                if (!CanContinueAlterationSynchronously())
                    return false;
                bulletin.CoreAlterationQueued = false;
            }
        }
        return true;
    }

    internal bool CanContinueAlterationSynchronously()
    {
        SimKineticsHoldingCapture ownership = _physics.CaptureOwnership();
        return ownership.PendingCollisionPrefixProjectionCount == 0
            && ownership.PendingCollisionReportCount == 0
            && ownership.PendingCollisionSetPositionDispatchCount == 0
            && ownership.PendingShadowSetPositionDispatchCount == 0
            && !ownership.IsCollisionReportDispatching;
    }

    internal bool ProgressDoneOne(
        LandblockKineticsPublication bulletin,
        Action<RealmActor>? priorStaticImpact = null)
    {
        VetReceipt(bulletin);
        if (bulletin.AbortAsked)
            throw new InvalidOperationException(
                "A cancelled collision publication can't resume");
        if (!bulletin.CommenceSealed)
            throw new InvalidOperationException(
                "Physics publication can't complete prior to its prefix commits");
        if (bulletin.WrapUpSealed)
            return true;
        bulletin.CoreAlterationQueued = false;

        long begun = Stopwatch.GetTimestamp();
        MountedLandblock lb = bulletin.Build.Landblock;
        KineticDatBundle datBundle =
            lb.PhysicsDats ?? KineticDatBundle.Empty;

        if (bulletin.GfxCur < bulletin.GfxObjectIdents.Length)
        {
            long stashBegun = Stopwatch.GetTimestamp();
            uint gfxObjectIdent = bulletin.GfxObjectIdents[bulletin.GfxCur];
            if (bulletin.Build.Collisions?.GfxObjs.TryGetValue(
                    gfxObjectIdent,
                    out PackedGfxObjContactAsset? readied) == true)
            {
                bulletin.StagingCache.CacheGfxObj(gfxObjectIdent, readied);
            }
            else if (datBundle.GfxObjs.TryGetValue(
                         gfxObjectIdent,
                         out var src))
            {
                bulletin.StagingCache.CacheGfxObj(gfxObjectIdent, src);
            }
            bulletin.GfxCur++;
            _gfxStashBeats += Stopwatch.GetTimestamp() - stashBegun;
        }
        else if (bulletin.RigCur < bulletin.RigObjectIdents.Length)
        {
            uint rigIdent =
                bulletin.RigObjectIdents[bulletin.RigCur];
            if (bulletin.Build.Collisions?.Setups.TryGetValue(
                    rigIdent,
                    out PackedSetupContact? readied) == true)
            {
                bulletin.StagingCache.CacheSetup(rigIdent, readied);
            }
            else if (datBundle.Setups.TryGetValue(rigIdent, out var src))
            {
                bulletin.StagingCache.CacheSetup(rigIdent, src);
            }
            bulletin.RigCur++;
        }
        else if (bulletin.PrecedingStaticCur
            < bulletin.PrecedingStaticHolderIdents.Length)
        {
            bulletin.LoadingEngine.ShadeObjects.DeregisterStaticHolderForLb(
                bulletin.PrecedingStaticHolderIdents[
                    bulletin.PrecedingStaticCur],
                lb.LandblockId);
            bulletin.PrecedingStaticCur++;
        }
        else if (bulletin.StaticCur < lb.Entities.Count)
        {
            RealmActor actor = lb.Entities[bulletin.StaticCur];
            priorStaticImpact?.Invoke(actor);
            BroadcastStaticActor(bulletin, actor);
            bulletin.StaticCur++;
        }
        else if (bulletin.RefloodHolderIdents is null)
        {
            SimContactHolderCaptureStep grab =
                _physics.ProgressImpactKeptHolderGrab(
                    bulletin.ImpactAdmission,
                    bulletin.ReadiedGen);
            if (grab.Completed)
            {
                bulletin.RefloodHolderIdents =
                    bulletin.ReadiedGen.KeptHolderIdents;
            }
        }
        else if (bulletin.RefloodCur
            < bulletin.RefloodHolderIdents.Count)
        {
            _physics.RenewImpactKeptHolder(
                bulletin.ImpactAdmission,
                bulletin.ReadiedGen,
                bulletin.RefloodHolderIdents[bulletin.RefloodCur]);
            bulletin.RefloodCur++;
        }
        else if (!bulletin.RefloodSealed)
        {
            if (KineticTelemetry.ProbeBuildingEnabled
                && bulletin.SceneryTried > 0)
            {
                Console.WriteLine(
                    $"lb 0x{lb.LandblockId:X8}: scenery tried={bulletin.SceneryTried} " +
                    $"(outdoorNone={bulletin.NoImpactTally})");
            }
            TraceAbsentSceneryLimits(lb, bulletin.StagingCache);
            bulletin.RefloodSealed = true;
        }
        else if (!bulletin.SealSealed)
        {
            SimContactSealStep seal =
                _physics.ProgressImpactGenSeal(
                bulletin.ImpactAdmission,
                bulletin.ReadiedGen);
            if (seal.WorkUnits > 1)
            {
                throw new InvalidOperationException(
                    "Collision seal exceeded its one-unit publication budget");
            }
            bulletin.SealSealed = seal.Completed;
            if (seal.Restarted)
            {
                bulletin.RefloodHolderIdents = null;
                bulletin.RefloodCur = 0;
                bulletin.RefloodSealed = false;
            }
        }

        if (bulletin.SealSealed && !bulletin.WrapUpSealed)
        {
            SimContactEpochCommit commit =
                _physics.CommitCollisionGeneration(
                    bulletin.ImpactAdmission,
                    bulletin.ReadiedGen);
            bulletin.EngineAlterationSealed |= commit.EngineCommitted;
            if (!commit.Completed)
            {
                bulletin.CoreAlterationQueued = true;
                if (!commit.EngineCommitted)
                {
                    bulletin.SealSealed = false;
                }
                _doneBroadcastBeats += Stopwatch.GetTimestamp() - begun;
                return false;
            }
            _refloodTally++;
            _staticBspHolderTally += bulletin.BspHolderTally;
            _staticCylinderHolderTally += bulletin.CylinderHolderTally;
            bulletin.WrapUpSealed = true;
            _doneTally++;
        }

        _doneBroadcastBeats += Stopwatch.GetTimestamp() - begun;
        return bulletin.WrapUpSealed;
    }

    public bool DemoteToLand(uint lbIdent)
    {
        for (int sample = 0; sample < 2; sample++)
        {
            if (ProgressDemotion(lbIdent))
                return true;
            if (_physics.CaptureOwnership()
                    .PendingCollisionPrefixProjectionCount != 0)
            {
                return false;
            }
        }
        return false;
    }

    internal bool ProgressDemotion(uint lbIdent)
    {
        SimContactEditResult outcome =
            _physics.DemoteImpactToLand(lbIdent);
        if (!outcome.Completed)
            return false;
        _demotionTally++;
        return true;
    }

    public bool RemoveLandblock(uint lbIdent)
    {
        for (int sample = 0; sample < 2; sample++)
        {
            if (ProgressDeletion(lbIdent))
                return true;
            if (_physics.CaptureOwnership()
                    .PendingCollisionPrefixProjectionCount != 0)
            {
                return false;
            }
        }
        return false;
    }

    internal bool ProgressDeletion(uint lbIdent)
    {
        SimContactEditResult outcome =
            _physics.WithdrawImpact(lbIdent);
        if (!outcome.Completed)
            return false;
        _wholeDeletionTally++;
        return true;
    }

    /// <summary>
    /// Where a room sits in the world: the orientation it was authored with, and its origin once
    /// the landblock's own origin is added. Both kinds of room place their vertices through it.
    /// </summary>
    private readonly record struct ChamberPlacement(Quaternion Spin, Vector3 Origin)
    {
        public static ChamberPlacement For(RoomCell chamber, Vector3 lbOrigin) =>
            new(chamber.Position.Orientation, chamber.Position.Origin + lbOrigin);

        public Matrix4x4 Transform =>
            Matrix4x4.CreateFromQuaternion(Spin) * Matrix4x4.CreateTranslation(Origin);

        /// <summary>A point in the room's own frame, placed into the world.</summary>
        public Vector3 ToRealm(Vector3 own) => Vector3.Transform(own, Spin) + Origin;
    }

    /// <summary>
    /// Publishes one room's collision shape and its doorways.
    ///
    /// A room's shape comes from baked collision when the landblock has it. When it does not, the
    /// room is described by the environment shell it points at instead, which stores its geometry
    /// a different way — that is the only reason the two paths below exist.
    /// </summary>
    private void BroadcastChamber(
        LandblockKineticsPublication bulletin,
        KineticDatBundle datBundle,
        uint shift)
    {
        uint chamberIdent =
            (bulletin.Build.Landblock.LandblockId & 0xFFFF0000u) | (0x0100u + shift);
        if (!datBundle.EnvCells.TryGetValue(chamberIdent, out RoomCell? chamber))
            return;

        if (bulletin.Build.Collisions is not { } impacts)
        {
            BroadcastShellChamber(bulletin, datBundle, chamberIdent, chamber);
            return;
        }

        if (!impacts.CellStructures.TryGetValue(chamberIdent, out PackedCellStructContactAsset? shape)
            || !impacts.EnvCells.TryGetValue(chamberIdent, out PackedEnvCellTopology? wiring))
        {
            return;
        }

        var placement = ChamberPlacement.For(chamber, bulletin.Origin);
        bulletin.StagingCache.CacheCellStruct(
            chamberIdent, chamber, placement.Transform, shape, wiring);

        // Gathered before anything is published, so a room either arrives whole or not at all.
        List<PortalFace> doorways = BakedDoorways(chamberIdent, placement, shape, wiring);
        bulletin.ChamberCanvases.Add(new CellFacet(
            chamberIdent, shape.PhysicsBsp.PolygChart, placement.Spin, placement.Origin));
        bulletin.GatewayPlanes.AddRange(doorways);
    }

    /// <summary>The doorway faces of a room whose collision was baked into the package.</summary>
    private static List<PortalFace> BakedDoorways(
        uint chamberIdent,
        in ChamberPlacement placement,
        PackedCellStructContactAsset shape,
        PackedEnvCellTopology wiring)
    {
        var faces = new List<PortalFace>();
        PackedPolygonTable table = shape.PortalPolygons;
        foreach (PackedEnvCellPortal doorway in wiring.Portals)
        {
            if ((uint)doorway.PolygonIndex >= (uint)table.Polygons.Length)
                continue;

            PackedContactPolygon polygon = table.Polygons[doorway.PolygonIndex];
            if (polygon.VertexRange.Count < 3)
                continue;

            var corners = new Vector3[polygon.VertexRange.Count];
            for (int corner = 0; corner < corners.Length; corner++)
            {
                corners[corner] =
                    placement.ToRealm(table.Vertices[polygon.VertexRange.Start + corner]);
            }

            faces.Add(Doorway(corners, doorway.OtherCellId, chamberIdent, (ushort)doorway.Flags));
        }
        return faces;
    }

    /// <summary>
    /// Publishes a room that has no baked collision, by reading the environment shell it names.
    /// The shell keeps its vertices in a table addressed by id, so the doorways are assembled by
    /// lookup rather than by walking a range, and a doorway naming a vertex the shell does not
    /// have is skipped whole.
    /// </summary>
    private void BroadcastShellChamber(
        LandblockKineticsPublication bulletin,
        KineticDatBundle datBundle,
        uint chamberIdent,
        RoomCell chamber)
    {
        if (chamber.ShellId == 0
            || !datBundle.Environments.TryGetValue(
                0x0D000000u | chamber.ShellId, out var surroundings)
            || !surroundings.Cells.TryGetValue(chamber.ShellCellIndex, out var shell))
        {
            return;
        }

        var placement = ChamberPlacement.For(chamber, bulletin.Origin);
        bulletin.StagingCache.CacheCellStruct(
            chamberIdent, chamber, shell, placement.Transform);

        var realmVerts = new Dictionary<ushort, Vector3>(shell.Vertices.ByIndex.Count);
        foreach ((ushort vertIdent, var vert) in shell.Vertices.ByIndex)
            realmVerts[vertIdent] = placement.ToRealm(vert.Position);

        var polygVertIdents = new List<List<short>>(shell.CollisionFacets.Count);
        foreach (var polygon in shell.CollisionFacets.Values)
            polygVertIdents.Add([.. polygon.VertexIds]);

        List<PortalFace> doorways = ShellDoorways(chamberIdent, chamber, shell, realmVerts);
        bulletin.ChamberCanvases.Add(new CellFacet(chamberIdent, realmVerts, polygVertIdents));
        bulletin.GatewayPlanes.AddRange(doorways);
    }

    /// <summary>The doorway faces of a shell-described room.</summary>
    private static List<PortalFace> ShellDoorways(
        uint chamberIdent,
        RoomCell chamber,
        ShellCell shell,
        Dictionary<ushort, Vector3> realmVerts)
    {
        var faces = new List<PortalFace>();
        foreach (var doorway in chamber.Doorways)
        {
            if (!shell.Facets.TryGetValue(doorway.PolygonId, out var polygon)
                || polygon.VertexIds.Count < 3)
            {
                continue;
            }

            var corners = new Vector3[polygon.VertexIds.Count];
            if (!TryPlaceCorners(polygon.VertexIds, realmVerts, corners))
                continue;

            faces.Add(Doorway(corners, doorway.OtherCellId, chamberIdent, (ushort)doorway.Bits));
        }
        return faces;
    }

    /// <summary>Looks every corner up; false the moment one is missing, leaving the face unusable.</summary>
    private static bool TryPlaceCorners(
        IReadOnlyList<short> vertIdents,
        Dictionary<ushort, Vector3> realmVerts,
        Vector3[] corners)
    {
        for (int corner = 0; corner < corners.Length; corner++)
        {
            if (!realmVerts.TryGetValue((ushort)vertIdents[corner], out corners[corner]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// A doorway face. The engine wants the room's index within its landblock rather than the whole
    /// cell id, which is the one detail both callers used to repeat.
    /// </summary>
    private static PortalFace Doorway(
        ReadOnlySpan<Vector3> corners,
        uint otherCellIdent,
        uint chamberIdent,
        ushort flagSet) =>
        PortalFace.FromVertices(corners, otherCellIdent, chamberIdent & 0xFFFFu, flagSet);

    private void BroadcastStructure(
        MountedLandblock lb,
        KineticDatBundle datBundle,
        KineticAssetCache stash,
        LandCanvas landCanvas,
        Vector3 origin,
        BuildingSpec structure)
    {
        uint lbStem = lb.LandblockId & 0xFFFF0000u;
        var gateways = new List<BuildingPortalFacts>(structure.Doorways.Count);
        foreach (BuildingDoorway gateway in structure.Doorways)
        {
            gateways.Add(new BuildingPortalFacts(
                anotherChamberIdent: lbStem | (uint)gateway.OtherCellId,
                anotherGatewayIdent: unchecked((short)gateway.OtherPortalId),
                flagSet: (ushort)gateway.Bits));
        }

        Vector3 structureOrigin = structure.Pose.Origin + origin;
        Matrix4x4 structureXform =
            Matrix4x4.CreateFromQuaternion(structure.Pose.Orientation)
            * Matrix4x4.CreateTranslation(structureOrigin);
        uint landcellLo = landCanvas.ComputeOutdoorCellId(
            structure.Pose.Origin.X,
            structure.Pose.Origin.Y);
        uint landcellIdent = lbStem | landcellLo;

        uint shellPieceZero = structure.ModelId;
        if ((shellPieceZero & 0xFF000000u) == 0x02000000u)
        {
            datBundle.Setups.TryGetValue(
                structure.ModelId,
                out var rig);
            shellPieceZero = rig is not null && rig.PartIds.Count > 0
                ? rig.PartIds[0]
                : 0u;
        }
        stash.StashStructure(
            landcellIdent,
            gateways,
            structureXform,
            modelIdent: shellPieceZero);
    }

    /// <summary>
    /// Registers one piece of static scenery with the collision world.
    ///
    /// There are three ways it can go in and they are tried in order: the baked BSP pieces if the
    /// landblock has them, otherwise the rig's cylinders or spheres, and failing both, the drawn
    /// pieces alone so the object is still known about even though nothing will collide with it.
    /// All three enrol the same way and differ only in the shapes they hand over.
    /// </summary>
    private void BroadcastStaticActor(
        LandblockKineticsPublication bulletin,
        RealmActor actor)
    {
        if (actor.IsStructureShell)
            return;

        MountedLandblock lb = bulletin.Build.Landblock;
        uint srcStem = actor.SrcGfxObjRefOrRigIdent & 0xFF000000u;
        if (IsExteriorTriMesh(actor.Id, srcStem))
            bulletin.SceneryTried++;

        IReadOnlyList<ProxyShape> bspForms =
            ProxyShapeBuilder.FromLbBspPieces(
                actor.MeshRefs,
                actor.IsStructureShell,
                bulletin.StagingCache.FetchGfxObjRef);
        IReadOnlyList<ProxyShape> pieceArr =
            ProxyShapeBuilder.FromStaticRasterizePieces(
                actor.MeshRefs,
                bulletin.StagingCache.FetchGfxObjRef,
                bulletin.StagingCache.FetchVisualLimits,
                out _);

        // Fetched whether or not it ends up being used, so the staging cache warms the same way
        // for every actor.
        PackedSetupContact? rig = PlanarRigFor(bulletin, actor);

        int bspTally = 0;
        int cylinderTally = 0;
        if (bspForms.Count > 0)
        {
            Enroll(bulletin, actor, lb, bspForms, pieceArr);
            TraceMultipartEnrollment(lb, actor, bspForms);
            bspTally = bspForms.Count;
        }
        else if (rig is not null)
        {
            List<ProxyShape> rigForms = RigProxies(rig, actor);
            if (rigForms.Count > 0)
            {
                Enroll(bulletin, actor, lb, rigForms, pieceArr);
                TraceRigEnrollment(lb, actor, rigForms);
                cylinderTally = rigForms.Count;
            }
        }

        if (bspTally == 0 && cylinderTally == 0 && pieceArr.Count > 0)
            Enroll(bulletin, actor, lb, Array.Empty<ProxyShape>(), pieceArr);

        if (bspTally > 0)
            bulletin.BspHolderTally++;
        if (cylinderTally > 0)
            bulletin.CylinderHolderTally++;
        if (bspTally == 0 && cylinderTally == 0
            && (srcStem == 0x01000000u || srcStem == 0x02000000u))
        {
            bulletin.NoImpactTally++;
        }
    }

    /// <summary>
    /// Scenery standing out on the landscape rather than inside a building, which is counted so the
    /// publication can report how much of it was attempted.
    /// </summary>
    private static bool IsExteriorTriMesh(uint actorIdent, uint srcStem) =>
        (actorIdent & 0x80000000u) != 0
        || (actorIdent < 0x40000000u
            && (srcStem == 0x01000000u || srcStem == 0x02000000u));

    /// <summary>The flattened rig for an actor, from the package if it is there and the dats if not.</summary>
    private static PackedSetupContact? PlanarRigFor(
        LandblockKineticsPublication bulletin,
        RealmActor actor)
    {
        if (bulletin.StagingCache.FetchPlanarRig(actor.SrcGfxObjRefOrRigIdent) is { } planar)
            return planar;
        return bulletin.StagingCache.FetchRig(actor.SrcGfxObjRefOrRigIdent) is { } graphRig
            ? PackedContactAssetBuilder.FlattenSetup(graphRig)
            : null;
    }

    /// <summary>
    /// The collision proxies a rig contributes, scaled by the actor. A rig is described by cylinders
    /// or by spheres, never both: the spheres are only read when there are no cylinders at all.
    /// A cylinder that was authored without a height stands four radii tall.
    /// </summary>
    private static List<ProxyShape> RigProxies(PackedSetupContact rig, RealmActor actor)
    {
        float scaling = actor.Scale > 0f ? actor.Scale : 1f;
        var forms = new List<ProxyShape>();

        foreach (PackedContactCylinder cylinder in rig.Cylinders)
        {
            float radius = cylinder.Radius * scaling;
            if (radius <= 0f)
                continue;

            float upright = (cylinder.Height > 0f ? cylinder.Height : cylinder.Radius * 4f) * scaling;
            forms.Add(ProxyShape.Cylinder(
                gfxObjRefIdent: actor.SrcGfxObjRefOrRigIdent,
                ownLocus: cylinder.Origin * scaling,
                ownSpin: Quaternion.Identity,
                scaling: scaling,
                radius: radius,
                cylHeight: upright));
        }

        if (rig.Cylinders.Length > 0)
            return forms;

        foreach (PackedContactSphere orb in rig.Spheres)
        {
            if (orb.Radius <= 0f)
                continue;

            forms.Add(ProxyShape.Sphere(
                gfxObjRefIdent: actor.SrcGfxObjRefOrRigIdent,
                ownLocus: orb.Origin * scaling,
                ownSpin: Quaternion.Identity,
                scaling: scaling,
                radius: orb.Radius * scaling));
        }
        return forms;
    }

    /// <summary>
    /// Hands one actor to the collision world. Only the shapes differ between the three ways in,
    /// so everything else is settled here rather than written out at each of them.
    /// </summary>
    private static void Enroll(
        LandblockKineticsPublication bulletin,
        RealmActor actor,
        MountedLandblock lb,
        IReadOnlyList<ProxyShape> forms,
        IReadOnlyList<ProxyShape> pieceArr) =>
        bulletin.LoadingEngine.ShadeObjects.EnrollMultiPiece(
            actor.Id,
            actor.Position,
            actor.Rotation,
            forms,
            0u,
            ActorImpactFlagSet.None,
            bulletin.Origin.X,
            bulletin.Origin.Y,
            lb.LandblockId,
            seedChamberIdent: actor.ParentCellId ?? 0u,
            isStatic: true,
            pieceArr: pieceArr);

    private static void TraceRigEnrollment(
        MountedLandblock lb,
        RealmActor actor,
        IReadOnlyList<ProxyShape> forms)
    {
        if (!KineticTelemetry.ProbeBuildingEnabled)
            return;

        for (int ordinal = 0; ordinal < forms.Count; ordinal++)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[entity-source] id=0x{actor.Id:X8} entityId=0x{actor.Id:X8} src=0x{actor.SrcGfxObjRefOrRigIdent:X8} gfxObj=0x{forms[ordinal].GfxObjId:X8} lb=0x{lb.LandblockId:X8} type={forms[ordinal].ImpactKind} note=setup-part{ordinal} state=0x{0u:X8} flags={ActorImpactFlagSet.None}"));
        }
    }

    private static void TraceMultipartEnrollment(
        MountedLandblock lb,
        RealmActor actor,
        IReadOnlyList<ProxyShape> forms)
    {
        if (!KineticTelemetry.ProbeBuildingEnabled)
            return;

        for (int ordinal = 0; ordinal < forms.Count; ordinal++)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[entity-source] id=0x{actor.Id:X8} entityId=0x{actor.Id:X8} src=0x{actor.SrcGfxObjRefOrRigIdent:X8} gfxObj=0x{forms[ordinal].GfxObjId:X8} lb=0x{lb.LandblockId:X8} type=BSP note=multipart-part{ordinal} hasPhys=true state=0x{0u:X8} flags={ActorImpactFlagSet.None}"));
        }
    }

    private static void TraceAbsentSceneryLimits(
        MountedLandblock lb,
        KineticAssetCache stash)
    {
        if (!KineticTelemetry.ProbeBuildingEnabled)
            return;

        int absentTally = 0;
        var specimens = new List<uint>();
        foreach (RealmActor actor in lb.Entities)
        {
            if ((actor.Id & 0x80000000u) == 0)
                continue;

            bool hasLimits = false;
            foreach (TriMeshRef triMeshRef in actor.MeshRefs)
            {
                GfxObjVisualExtent? limits =
                    stash.FetchVisualLimits(triMeshRef.GfxObjId);
                if (limits is not null && limits.Radius > 0f)
                {
                    hasLimits = true;
                    break;
                }
            }
            if (hasLimits)
                continue;

            absentTally++;
            if (specimens.Count < 3)
                specimens.Add(actor.SrcGfxObjRefOrRigIdent);
        }

        if (absentTally > 0)
        {
            string specimenPhrase = string.Join(",", specimens.Select(
                val => $"0x{val:X8}"));
            Console.WriteLine(
                $"  → {absentTally} scenery entities had no visual bounds cached. " +
                $"Samples: {specimenPhrase}");
        }
    }

    private static bool IsFinite(Vector3 val) =>
        float.IsFinite(val.X)
        && float.IsFinite(val.Y)
        && float.IsFinite(val.Z);

    private void VetReceipt(LandblockKineticsPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!ReferenceEquals(publication.Owner, _receiptHolder))
        {
            throw new ArgumentException(
                "The physics publication receipt belongs to another publisher",
                nameof(publication));
        }
        if (!publication.WrapUpSealed
            && !publication.EngineAlterationSealed)
        {
            ObjectDisposedException.ThrowIf(
                publication.ReadiedGen.IsDestroyed,
                publication);
        }
    }
}
