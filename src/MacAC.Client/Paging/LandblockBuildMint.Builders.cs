using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Paging;

public sealed partial class LandblockBuildMint
{
    public MacAC.Client.Paging.LandblockAssemble? Build(
        MacAC.Client.Paging.LandblockBuildAsk request)
    {
        if (!request.Origin.IsSpecified)
            throw new ArgumentException(
                "A landblock build needs a specified captured origin",
                nameof(request));

        LandblockAssemble? assemble;
        if (MacAC.Mechanics.Kinetics.KineticTelemetry.ProbeTeleportEnabled)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            lock (_datMutex)
            {
                long waitedMsec = stopwatch.ElapsedMilliseconds;
                stopwatch.Restart();
                assemble = AssembleBolted(request);
                MacAC.Mechanics.Kinetics.KineticTelemetry.TraceWarp(
                    "BUILD", request.LandblockId,
                    $"waited={waitedMsec}ms held={stopwatch.ElapsedMilliseconds}ms kind={request.Kind}");
            }
        }
        else
        {
            lock (_datMutex)
                assemble = AssembleBolted(request);
        }

        if (assemble is null ||
            request.Kind == LandblockFlowJobFlavor.LoadFar)

            return assemble;

        var impacts =
            LandblockKineticsBaker.LocateLinkClosure(
                _readiedImpacts,
                assemble.Landblock);
        var bulletinDatFiles =
            (assemble.Landblock.PhysicsDats
                ?? MacAC.Mechanics.Realm.KineticDatBundle.Empty)
            .WithoutImpactGraphs();
        return assemble with
        {
            Landblock = assemble.Landblock with
            {
                PhysicsDats = bulletinDatFiles,
            },
            Collisions = impacts,
        };
    }

    private MacAC.Client.Paging.LandblockAssemble? AssembleBolted(
        MacAC.Client.Paging.LandblockBuildAsk req)
    {
        uint lbIdent = req.LandblockId;

        if (req.Kind == MacAC.Client.Paging.LandblockFlowJobFlavor.LoadFar)
        {
            var heightmapSole = _datFiles.Get<TerrainTile>(lbIdent);
            if (heightmapSole is null) return null;
            (float upperZ, float lowerZ) = CalculateStrollZSlab(heightmapSole.Heights);
            return new MacAC.Client.Paging.LandblockAssemble(
                new MacAC.Mechanics.Realm.MountedLandblock(
                    lbIdent,
                    heightmapSole,
                    System.Array.Empty<MacAC.Mechanics.Realm.RealmActor>(),
                    MacAC.Mechanics.Realm.KineticDatBundle.Empty),
                Origin: req.Origin,
                TerrainBounds: new(upperZ, lowerZ));
        }

        var baseFetched = MacAC.Mechanics.Realm.LandblockReader.Load(_datFiles, lbIdent);
        if (baseFetched is null) return null;

        int lbX = (int)((lbIdent >> 24) & 0xFFu);
        int lbY = (int)((lbIdent >> 16) & 0xFFu);
        System.Numerics.Vector3 realmShift = new System.Numerics.Vector3(
            (lbX - req.Origin.CenterX) * 192f,
            (lbY - req.Origin.CenterY) * 192f,
            0f);

        var hydrated =
            LandblockKineticsBaker.FastenStaticTriMeshes(
                _datFiles,
                baseFetched,
                realmShift);

        var merged = new List<MacAC.Mechanics.Realm.RealmActor>(hydrated);
        merged.AddRange(
            _printSceneryZ
                ? BuildSceneryEntitiesForStreaming(
                    baseFetched,
                    lbX,
                    lbY,
                    req.Origin)
                : LandblockKineticsBaker
                    .ExpandScenery(
                        _datFiles,
                        baseFetched,
                        realmShift,
                        _heightTable));
        var environChamberAssemble = new MacAC.Client.Graphics.Batching.EnvCellLandblockBuildAssembler(lbIdent);
        (float strollUpperZ, float strollLowerZ) = CalculateStrollZSlab(baseFetched.Heightmap.Heights);
        environChamberAssemble.AssignStrollZSlab(strollUpperZ, strollLowerZ);
        merged.AddRange(BuildInteriorEntitiesForStreaming(
            lbIdent,
            lbX,
            lbY,
            req.Origin,
            environChamberAssemble));

        var kineticsDatFiles = LandblockKineticsBaker.CollectDatBundle(
            _datFiles,
            lbIdent,
            merged);
        var finishedEnvironChambers = environChamberAssemble.Build();
        return new MacAC.Client.Paging.LandblockAssemble(
            new MacAC.Mechanics.Realm.MountedLandblock(
                baseFetched.LandblockId,
                baseFetched.Heightmap,
                merged,
                kineticsDatFiles),
            finishedEnvironChambers,
            req.Origin,
            TerrainBounds: new(strollUpperZ, strollLowerZ));
    }

    private List<MacAC.Mechanics.Realm.RealmActor> BuildSceneryEntitiesForStreaming(
        MacAC.Mechanics.Realm.MountedLandblock landblock,
        int lbX,
        int lbY,
        MacAC.Client.Paging.LandblockAssembleOrigin origin)
    {
        var outcome = new List<MacAC.Mechanics.Realm.RealmActor>();

        var zone = _datFiles.Get<WorldRegion>(0x13000000u);
        if (zone is null) return outcome;

        HashSet<int>? structureChambers = null;
        var lbDetails = _datFiles.Get<TerrainTileExtras>(
            (landblock.LandblockId & 0xFFFF0000u) | 0xFFFEu);
        if (lbDetails is not null)
        {
            structureChambers = [];
            foreach (BuildingSpec bldg in lbDetails.Structures)
            {
                BuildSceneryEntitiesForStreamingLoop(bldg, structureChambers);
            }
        }

        var spawns = MacAC.Mechanics.Realm.SceneryGrower.Produce(
            _datFiles, zone, landblock.Heightmap, landblock.LandblockId, structureChambers, _heightTable);
        if (spawns.Count is 0) return outcome;

        System.Numerics.Vector3 lbShift = new System.Numerics.Vector3(
            (lbX - origin.CenterX) * 192f,
            (lbY - origin.CenterY) * 192f,
            0f);

        uint lbXByte = (landblock.LandblockId >> 24) & 0xFFu;
        uint lbYByte = (landblock.LandblockId >> 16) & 0xFFu;
        uint sceneryCounter = 0;

        foreach (var summon in spawns)
        {
            var triMeshRefs = new List<MacAC.Mechanics.Realm.TriMeshRef>();
            var sceneryLimits = new MacAC.Mechanics.Geometry.ExtentAccumulator();
            var scalingMat = System.Numerics.Matrix4x4.CreateScale(summon.Scale);

            if ((summon.ObjectId & 0xFF000000u) == 0x01000000u)
            {
                var gfx = _datFiles.Get<PartMesh>(summon.ObjectId);
                if (gfx is not null)
                {
                    sceneryLimits = BuildSceneryEntitiesForStreamingBranch2(gfx, sceneryLimits, scalingMat, triMeshRefs, summon);
                }
            }
            else if ((summon.ObjectId & 0xFF000000u) == 0x02000000u)
            {
                var rig = _datFiles.Get<RigSpec>(summon.ObjectId);
                if (rig is not null)
                {
                    var planar = MacAC.Mechanics.Geometry.RigTriMesh.Flatten(rig);
                    foreach (var mr in planar)
                    {
                        var gfx = _datFiles.Get<PartMesh>(mr.GfxObjId);
                        if (gfx is null) continue;
                        var pieceXf = mr.PartTransform * scalingMat;
                        var pb = MacAC.Mechanics.Geometry.GfxObjExtent.Get(gfx);
                        if (pb is not null) sceneryLimits.Add(pieceXf, pb.Value);
                        triMeshRefs.Add(new MacAC.Mechanics.Realm.TriMeshRef(mr.GfxObjId, pieceXf));
                    }
                }
            }

            if (triMeshRefs.Count is 0) continue;

            // Sample terrain Z at (localX, localY) to lift scenery onto the ground.
            float ownX = summon.LocalPosition.X;
            float ownY = summon.LocalPosition.Y;
            float realmPx = ownX + lbShift.X;
            float realmPy = ownY + lbShift.Y;
            float terrainZ = ProbeLandZ(landblock.Heightmap, _heightTable, ownX, ownY);
            float finalZ = terrainZ + summon.LocalPosition.Z;

            if (_printSceneryZ)
            {
                string src = "heightmap";
                foreach (var mr in triMeshRefs)
                {
                    var dgfx = _datFiles.Get<PartMesh>(mr.GfxObjId);
                    if (dgfx is null) continue;

                    float zLower = float.PositiveInfinity, zUpper = float.NegativeInfinity;
                    foreach (MeshVertex vertex in dgfx.Vertices.ByIndex.Values)
                    {
                        if (vertex.Position.Z < zLower) zLower = vertex.Position.Z;
                        if (vertex.Position.Z > zUpper) zUpper = vertex.Position.Z;
                    }
                    if (float.IsPositiveInfinity(zLower)) { zLower = 0f; zUpper = 0f; }

                    System.Numerics.Vector3 pieceT = mr.PartTransform.Translation;

                    bool hasDD = dgfx.Bits.HasFlag(PartMeshBits.HasDIDDegrade);
                    string ddDetails = string.Empty;
                    if (hasDD && dgfx.LodTableId is not 0)
                    {
                        var ddi = _datFiles.Get<LodTable>(dgfx.LodTableId);
                        if (ddi is not null && ddi.Levels.Count > 0)
                        {
                            uint slot0Ident = (uint)ddi.Levels[0].PartMeshId;
                            float slot0Lower = 0f;
                            var slot0Gfx = _datFiles.Get<PartMesh>(slot0Ident);
                            if (slot0Gfx is not null && slot0Gfx.Vertices.ByIndex.Count > 0)
                            {
                                slot0Lower = BuildSceneryEntitiesForStreamingBranch(slot0Lower, slot0Gfx);
                            }
                            ddDetails = $" deg[0]=0x{slot0Ident:X8} deg[0]ZMin={slot0Lower:F3}";
                        }
                    }

                    // partWorldZMin = the lowest vertex of this part in world space. = finalZ (setup origin in world
                    // Z) + partT.Z (part offset) + zMin (mesh-local lowest vertex) If everything is right and the
                    // lowest part of the tree should touch the ground, we expect partWorldZMin <= groundZ for at least
                    // one part of a multi-part setup.
                    float pieceRealmZLower = finalZ + pieceT.Z + zLower;

                    Console.WriteLine(
                        $"[scenery-z] lb=0x{landblock.LandblockId:X8} root=0x{summon.ObjectId:X8} gfx=0x{mr.GfxObjId:X8}" +
                        $" source={src}" +
                        $" world=({realmPx:F2},{realmPy:F2}) localXY=({ownX:F2},{ownY:F2})" +
                        $" groundZ={terrainZ:F3} BaseLoc.Z={summon.LocalPosition.Z:F3} finalZ={finalZ:F3}" +
                        $" partT=({pieceT.X:F2},{pieceT.Y:F2},{pieceT.Z:F3}) spawnScale={summon.Scale:F3}" +
                        $" zRange=[{zLower:F3}..{zUpper:F3}] partWorldZMin={pieceRealmZLower:F3} delta={pieceRealmZLower - terrainZ:F3}" +
                        $" hasDIDDegrade={hasDD}{ddDetails}");
                }
            }

            var hydrated = new MacAC.Mechanics.Realm.RealmActor
            {
                Id = MacAC.Mechanics.Realm.SceneryIdPool.Allocate(
                    lbXByte,
                    lbYByte,
                    ref sceneryCounter),
                SrcGfxObjRefOrRigIdent = summon.ObjectId,
                Position = new System.Numerics.Vector3(ownX, ownY, finalZ) + lbShift,
                Rotation = summon.Rotation,
                MeshRefs = triMeshRefs,
                Scale = summon.Scale,
                FxChamberIdent = MacAC.Mechanics.Kinetics.LandCanvas.ComputeOutdoorCellId(
                    landblock.LandblockId,
                    ownX,
                    ownY),
            };
            if (sceneryLimits.TryGet(out var scbLower, out var scbUpper))
                hydrated.AssignOwnLimits(scbLower, scbUpper);
            outcome.Add(hydrated);
        }

        return outcome;

    }

    private float BuildSceneryEntitiesForStreamingBranch(float slot0Lower, PartMesh slot0Gfx)
    {
        slot0Lower = float.PositiveInfinity;
        foreach (MeshVertex vertex in slot0Gfx.Vertices.ByIndex.Values)
            if (vertex.Position.Z < slot0Lower) slot0Lower = vertex.Position.Z;
        if (float.IsPositiveInfinity(slot0Lower)) slot0Lower = 0f;
        return slot0Lower;
    }

    private Mechanics.Geometry.ExtentAccumulator BuildSceneryEntitiesForStreamingBranch2(PartMesh gfx, Mechanics.Geometry.ExtentAccumulator sceneryLimits, System.Numerics.Matrix4x4 scalingMat, List<Mechanics.Realm.TriMeshRef> triMeshRefs, Mechanics.Realm.SceneryGrower.SceneryPlacement summon)
    {
        var pb = MacAC.Mechanics.Geometry.GfxObjExtent.Get(gfx);
        if (pb is not null) sceneryLimits.Add(scalingMat, pb.Value);
        triMeshRefs.Add(new MacAC.Mechanics.Realm.TriMeshRef(summon.ObjectId, scalingMat));
        return sceneryLimits;
    }

    private void BuildSceneryEntitiesForStreamingLoop(BuildingSpec bldg, HashSet<int> structureChambers)
    {
        int cx = Math.Clamp((int)(bldg.Pose.Origin.X / 24f), 0, 8);
        int cy = Math.Clamp((int)(bldg.Pose.Origin.Y / 24f), 0, 8);
        structureChambers.Add(cx * 9 + cy);
    }

    private List<MacAC.Mechanics.Realm.RealmActor> BuildInteriorEntitiesForStreaming(
        uint lbIdent,
        int lbX,
        int lbY,
        MacAC.Client.Paging.LandblockAssembleOrigin origin,
        MacAC.Client.Graphics.Batching.EnvCellLandblockBuildAssembler environChamberAssemble)
    {
        var outcome = new List<MacAC.Mechanics.Realm.RealmActor>();

        var lbDetails = _datFiles.Get<TerrainTileExtras>((lbIdent & 0xFFFF0000u) | 0xFFFEu);
        if (lbDetails is null || lbDetails.CellCount is 0) return outcome;

        System.Numerics.Vector3 lbShift = new System.Numerics.Vector3(
            (lbX - origin.CenterX) * 192f,
            (lbY - origin.CenterY) * 192f,
            0f);

        environChamberAssemble.AppendStrollStructures(
            MacAC.Client.Graphics.Stride.StrideBuildingMint.Build(
                _datFiles, lbIdent, lbDetails.Structures, lbShift));

        uint interiorLbX = (lbIdent >> 24) & 0xFFu;
        uint interiorLbY = (lbIdent >> 16) & 0xFFu;
        uint ownCounter = 0;

        uint leadChamberIdent = (lbIdent & 0xFFFF0000u) | 0x0100u;
        for (uint shift = 0; shift < lbDetails.CellCount; ++shift)
        {
            uint environChamberIdent = leadChamberIdent + shift;
            var environChamber = _datFiles.Get<RoomCell>(environChamberIdent);
            if (environChamber is null)
            {
                Console.WriteLine($"[cell-miss] EnvCell 0x{environChamberIdent:X8} null during interior hydration (NumCells={lbDetails.CellCount})");
                continue;
            }

            ShellCell? chamberStruct = null;
            if (environChamber.ShellId is not 0)
            {
                var surroundings = _datFiles.Get<InteriorShell>(0x0D000000u | environChamber.ShellId);
                if (surroundings is null)
                {
                    Console.WriteLine($"[cell-miss] Environment 0x{0x0D000000u | environChamber.ShellId:X8} null for EnvCell 0x{environChamberIdent:X8} -> walls not registered");
                }
                if (surroundings is not null
                    && surroundings.Cells.TryGetValue(environChamber.ShellCellIndex, out chamberStruct))
                {
                    System.Numerics.Vector3 kineticsChamberOrigin = environChamber.Position.Origin + lbShift;
                    System.Numerics.Vector3 chamberOrigin = kineticsChamberOrigin;
                    var chamberXform =
                        System.Numerics.Matrix4x4.CreateFromQuaternion(environChamber.Position.Orientation) *
                        System.Numerics.Matrix4x4.CreateTranslation(chamberOrigin);
                    var kineticsChamberXform = chamberXform;

                    bool hasDrawableGeo =
                        MacAC.Mechanics.Geometry.CellTessellation.HasDrawableGeo(environChamber, chamberStruct, _datFiles);
                    environChamberAssemble.AppendChamber(
                        environChamberIdent,
                        environChamber,
                        chamberStruct,
                        kineticsChamberOrigin,
                        kineticsChamberXform,
                        chamberOrigin,
                        chamberXform,
                        hasDrawableGeo: hasDrawableGeo);
                }
            }

            foreach (PlacedObject stab in environChamber.StaticObjects)
            {
                if ((stab.Id & 0xFF000000u) == 0x01000000u
                    && MacAC.Mechanics.Geometry.GfxObjLodResolver.IsRuntimeHiddenMarker(_datFiles, stab.Id))
                    continue;

                var triMeshRefs = new List<MacAC.Mechanics.Realm.TriMeshRef>();
                var interiorLimits = new MacAC.Mechanics.Geometry.ExtentAccumulator();
                int stabLampTally = 0;
                if ((stab.Id & 0xFF000000u) == 0x01000000u)
                {
                    var gfx = _datFiles.Get<PartMesh>(stab.Id);
                    if (gfx is not null)
                    {
                        interiorLimits = BuildInteriorEntitiesForStreamingBranch(gfx, interiorLimits, triMeshRefs, stab);
                    }
                }
                else if ((stab.Id & 0xFF000000u) == 0x02000000u)
                {
                    var rig = _datFiles.Get<RigSpec>(stab.Id);
                    if (rig is not null)
                    {
                        stabLampTally = rig.Lamps.Count;
                        var planar = MacAC.Mechanics.Geometry.RigTriMesh.Flatten(rig);
                        foreach (var mr in planar)
                        {
                            if (MacAC.Mechanics.Geometry.GfxObjLodResolver.IsRuntimeHiddenMarker(_datFiles, mr.GfxObjId))
                                continue;
                            var gfx = _datFiles.Get<PartMesh>(mr.GfxObjId);
                            if (gfx is null)

                                continue;
                            var pb = MacAC.Mechanics.Geometry.GfxObjExtent.Get(gfx);
                            if (pb is not null) interiorLimits.Add(mr.PartTransform, pb.Value);
                            triMeshRefs.Add(mr);
                        }
                    }
                }

                if (!MacAC.Mechanics.Geometry.EntityFleshingRules.ShouldKeepEntity(triMeshRefs.Count, stabLampTally))

                    continue;

                System.Numerics.Vector3 realmSpot = stab.Pose.Origin + lbShift;
                var realmRot = stab.Pose.Orientation;

                var hydrated = new MacAC.Mechanics.Realm.RealmActor
                {
                    Id = MacAC.Mechanics.Realm.InteriorIdPool.Allocate(
                        interiorLbX,
                        interiorLbY,
                        ref ownCounter),
                    SrcGfxObjRefOrRigIdent = stab.Id,
                    Position = realmSpot,
                    Rotation = realmRot,
                    MeshRefs = triMeshRefs,
                    ParentCellId = environChamberIdent,
                };
                if (interiorLimits.TryGet(out var ibLower, out var ibUpper))
                    hydrated.AssignOwnLimits(ibLower, ibUpper);

                outcome.Add(hydrated);
            }
        }

        return outcome;
    }

    private Mechanics.Geometry.ExtentAccumulator BuildInteriorEntitiesForStreamingBranch(PartMesh gfx, Mechanics.Geometry.ExtentAccumulator interiorLimits, List<Mechanics.Realm.TriMeshRef> triMeshRefs, PlacedObject stab)
    {
        var pb = MacAC.Mechanics.Geometry.GfxObjExtent.Get(gfx);
        if (pb is not null) interiorLimits.Add(System.Numerics.Matrix4x4.Identity, pb.Value);
        triMeshRefs.Add(new MacAC.Mechanics.Realm.TriMeshRef(stab.Id, System.Numerics.Matrix4x4.Identity));
        return interiorLimits;
    }
}
