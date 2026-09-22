using System.Numerics;
using System.Text;
using MacAC.Dat;
using Plane = System.Numerics.Plane;
using UcgEnvCell = MacAC.Mechanics.Realm.Cells.EnvCell;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Admitting GfxObjs, setups and cells, from dat objects and/or prepared packed assets.</summary>
public sealed partial class KineticAssetCache
{

    public void CacheGfxObj(uint gfxObjRefIdent, PartMesh gfxObjRef, PackedGfxObjContactAsset? readied = null)
    {
        ArgumentNullException.ThrowIfNull(gfxObjRef);

        bool hasKinetics = (gfxObjRef.Bits & PartMeshBits.HasPhysics) != 0;
        if (_readiedSole)
        {
            readied ??= Find(_denseGfxObjs, gfxObjRefIdent);
            bool needsImpact = hasKinetics && gfxObjRef.CollisionTree?.Root is not null;
            if (needsImpact && readied?.PhysicsBsp is null && !_denseGfxObjs.ContainsKey(gfxObjRefIdent))
                throw AbsentReadiedImpact("GfxObj", gfxObjRefIdent);
            if (readied is not null)
                AdmitReadiedGfxObjRef(gfxObjRefIdent, readied);
            return;
        }

        if (readied is not null)
            _denseGfxObjs.TryAdd(gfxObjRefIdent, readied);

        if (!_extents.ContainsKey(gfxObjRefIdent) && gfxObjRef.Vertices is not null)
            _extents[gfxObjRefIdent] = CalculateVisualLimits(gfxObjRef.Vertices);
        var decodedReach = Find(_extents, gfxObjRefIdent);

        if (_gfxObjs.TryGetValue(gfxObjRefIdent, out GfxObjKinetics? recognized))
        {
            if (readied is not null)
                recognized.DenseKineticBsp ??= readied.PhysicsBsp;
            return;
        }
        if (!hasKinetics || gfxObjRef.CollisionTree?.Root is null || gfxObjRef.Vertices is null)
            return;

        GfxObjKinetics kinetics = new GfxObjKinetics
        {
            SourceId = gfxObjRefIdent,
            BSP = gfxObjRef.CollisionTree,
            PhysicsPolygons = gfxObjRef.CollisionFacets,
            BoundingSphere = gfxObjRef.CollisionTree.Root.Bounds,
            Vertices = gfxObjRef.Vertices,
            Settled = LocatePolygs(gfxObjRef.CollisionFacets, gfxObjRef.Vertices),
            DenseKineticBsp = readied?.PhysicsBsp,
            VisualLimits = readied?.VisualBounds ?? decodedReach?.Pack(),
        };
        _gfxObjs[gfxObjRefIdent] = kinetics;

        if (KineticTelemetry.ProbeDumpGfxObjsEnabled && KineticTelemetry.ProbeDumpGfxObjIds.Contains(gfxObjRefIdent))
            PrintGfxObjRef(gfxObjRefIdent, kinetics);
    }

    public void CacheGfxObj(uint gfxObjRefIdent, PackedGfxObjContactAsset readied)
    {
        ArgumentNullException.ThrowIfNull(readied);
        AdmitReadiedGfxObjRef(gfxObjRefIdent, readied);
    }

    public void CacheSetup(uint rigIdent, RigSpec rig, PackedSetupContact? readied = null)
    {
        ArgumentNullException.ThrowIfNull(rig);

        if (_readiedSole)
        {
            readied ??= Find(_denseSetups, rigIdent);
            if (readied is null && !_denseSetups.ContainsKey(rigIdent))
                throw AbsentReadiedImpact("Setup", rigIdent);
            AdmitReadiedRig(rigIdent, readied ?? throw AbsentReadiedImpact("Setup", rigIdent));
            return;
        }

        if (readied is not null)
            _denseSetups.TryAdd(rigIdent, readied);

        if (_setups.TryGetValue(rigIdent, out SetupKinetics? recognized))
        {
            if (readied is not null)
                recognized.PlanarImpact ??= readied;
            return;
        }

        _setups[rigIdent] = new SetupKinetics
        {
            SourceId = rigIdent,
            CylSpheres = rig.Capsules ?? [],
            Spheres = rig.Orbs ?? [],
            Height = rig.Height,
            Radius = rig.Radius,
            StepUpHeight = rig.StepUpHeight,
            StepDownHeight = rig.StepDownHeight,
            PlanarImpact = readied,
        };
    }

    public void CacheSetup(uint rigIdent, PackedSetupContact readied)
    {
        ArgumentNullException.ThrowIfNull(readied);
        AdmitReadiedRig(rigIdent, readied);
    }

    public void CacheCellStruct(
        uint environChamberIdent,
        RoomCell environChamber,
        ShellCell chamberStruct,
        Matrix4x4 realmXform,
        PackedCellStructContactAsset? readiedStructure = null,
        PackedEnvCellTopology? readiedWiring = null)
    {
        ArgumentNullException.ThrowIfNull(environChamber);
        ArgumentNullException.ThrowIfNull(chamberStruct);

        if (_readiedSole)
        {
            readiedStructure ??= Find(DenseChambers, environChamberIdent);
            readiedWiring ??= Find(DenseEnvironChambers, environChamberIdent);
            if (readiedStructure is null && !DenseChambers.ContainsKey(environChamberIdent))
                throw AbsentReadiedImpact("CellStruct", environChamberIdent);
            if (readiedWiring is null && !DenseEnvironChambers.ContainsKey(environChamberIdent))
                throw AbsentReadiedImpact("EnvCell topology", environChamberIdent);

            AdmitReadiedChamber(
                environChamberIdent,
                environChamber,
                realmXform,
                readiedStructure ?? throw AbsentReadiedImpact("CellStruct", environChamberIdent),
                readiedWiring ?? throw AbsentReadiedImpact("EnvCell topology", environChamberIdent));
            return;
        }

        if (chamberStruct.CellTree?.Root is null)
            return;

        // A prepared structure without containment is no use; drop both halves
        if (readiedStructure?.ContainmentBsp.TrunkIdx < 0)
        {
            readiedStructure = null;
            readiedWiring = null;
        }
        if (readiedStructure is not null)
            World.TryAppendPlanarChamberStruct(environChamberIdent, readiedStructure);
        if (readiedWiring is not null)
            World.TryAppendPlanarEnvironChamber(environChamberIdent, readiedWiring);

        if (!ChamberGraph.Contains(environChamberIdent))
            ChamberGraph.Add(UcgEnvCell.FromDat(environChamberIdent, environChamber, chamberStruct, realmXform, readiedStructure?.ContainmentBsp));

        if (Cells.ContainsKey(environChamberIdent))
            return;

        Matrix4x4.Invert(realmXform, out Matrix4x4 inv);

        Dictionary<ushort, SettledPolygon> settled = chamberStruct.CollisionFacets is null
            ? new Dictionary<ushort, SettledPolygon>()
            : LocatePolygs(chamberStruct.CollisionFacets, chamberStruct.Vertices);

        // Visible polygons - portals reference these (NOT PhysicsPolygons)
        var gatewayPolygs = LocatePolygs(chamberStruct.Facets, chamberStruct.Vertices);

        List<PortalFacts> gateways = new List<PortalFacts>(environChamber.Doorways.Count);
        foreach (CellDoorway portal in environChamber.Doorways)
            gateways.Add(new PortalFacts(portal.OtherCellId, portal.PolygonId, (ushort)portal.Bits));

        HashSet<uint> shownChamberIdents = new HashSet<uint>();
        if (environChamber.VisibleCells is not null)
        {
            uint stem = environChamberIdent & StemBitmask;
            foreach (ushort loIdent in environChamber.VisibleCells)
                shownChamberIdents.Add(stem | loIdent);
        }

        CellKinetics chamber = new CellKinetics
        {
            SourceId = environChamberIdent,
            BSP = chamberStruct.CollisionTree,
            PhysicsPolygons = chamberStruct.CollisionFacets,
            Vertices = chamberStruct.Vertices,
            WorldTransform = realmXform,
            InverseWorldTransform = inv,
            Resolved = settled,
            PackedKineticBsp = readiedStructure?.PhysicsBsp,
            PlanarContainmentBsp = readiedStructure?.ContainmentBsp,
            PlanarGatewayPolygs = readiedStructure?.PortalPolygons,
            PlanarWiring = readiedWiring,
            CellBSP = chamberStruct.CellTree,
            Portals = gateways,
            PortalPolygons = gatewayPolygs,
            VisibleCellIds = shownChamberIdents,
            SeenOutside = (environChamber.Bits & RoomCellBits.SeenOutside) != 0,
            RestrictionObj = environChamber.RestrictionObjectId,
        };
        World.AssignChamberStruct(environChamberIdent, chamber);

        if (KineticTelemetry.ProbeDumpCellsEnabled && KineticTelemetry.ProbeDumpCellIds.Contains(environChamberIdent))
            PrintChamber(environChamberIdent, chamber);
        if (KineticTelemetry.ProbeCellCacheEnabled)
            InspectChamberStash(environChamberIdent, chamberStruct, realmXform, settled, gateways, shownChamberIdents.Count);
    }

    public void CacheCellStruct(
        uint environChamberIdent,
        RoomCell environChamber,
        Matrix4x4 realmXform,
        PackedCellStructContactAsset readiedStructure,
        PackedEnvCellTopology readiedWiring)
    {
        ArgumentNullException.ThrowIfNull(environChamber);
        ArgumentNullException.ThrowIfNull(readiedStructure);
        ArgumentNullException.ThrowIfNull(readiedWiring);
        AdmitReadiedChamber(environChamberIdent, environChamber, realmXform, readiedStructure, readiedWiring);
    }

    internal static GfxObjVisualExtent CalculateVisualLimits(MeshVertices vertArr)
    {
        if (vertArr.ByIndex is null || vertArr.ByIndex.Count is 0)
        {
            return new GfxObjVisualExtent
            {
                Min = Vector3.Zero,
                Max = Vector3.Zero,
                Center = Vector3.Zero,
                Radius = 0f,
                HalfExtents = Vector3.Zero,
            };
        }

        Vector3 lower = new Vector3(float.MaxValue);
        Vector3 upper = new Vector3(float.MinValue);
        foreach (var kv in vertArr.ByIndex)
        {
            Vector3 p = kv.Value.Position;
            if (p.X < lower.X) lower.X = p.X;
            if (p.Y < lower.Y) lower.Y = p.Y;
            if (p.Z < lower.Z) lower.Z = p.Z;
            if (p.X > upper.X) upper.X = p.X;
            if (p.Y > upper.Y) upper.Y = p.Y;
            if (p.Z > upper.Z) upper.Z = p.Z;
        }

        Vector3 halfExtents = (upper - lower) * 0.5f;
        return new GfxObjVisualExtent
        {
            Min = lower,
            Max = upper,
            Center = (lower + upper) * 0.5f,
            Radius = halfExtents.Length(),
            HalfExtents = halfExtents,
        };
    }

    // Looks up each polygon's vertices and fits its plane (fan-summed normal, averaged offset),
    // skipping anything degenerate
    internal static Dictionary<ushort, SettledPolygon> LocatePolygs(Dictionary<ushort, Facet> polys, MeshVertices vertArr)
    {
        var settled = new Dictionary<ushort, SettledPolygon>(polys.Count);
        foreach ((ushort ident, Facet poly) in polys)
        {
            int tally = poly.VertexIds.Count;
            if (tally < 3)
                continue;

            Vector3[] corners = new Vector3[tally];
            if (!ConsultCorners(poly, vertArr, corners))
                continue;

            Vector3 norm = Vector3.Zero;
            for (int idx = 1; idx < tally - 1; ++idx)
                norm += Vector3.Cross(corners[idx] - corners[0], corners[idx + 1] - corners[0]);
            float length = norm.Length();
            if (length < 1e-8f)
                continue;
            norm /= length;

            float dotTotal = 0f;
            for (int idx = 0; idx < tally; ++idx)
                dotTotal += Vector3.Dot(norm, corners[idx]);

            settled[ident] = new SettledPolygon
            {
                Vertices = corners,
                Plane = new Plane(norm, -(dotTotal / tally)),
                NumPoints = tally,
                SidesType = poly.Culling,
                Id = ident,
            };
        }
        return settled;
    }

    private void AdmitReadiedGfxObjRef(uint gfxObjRefIdent, PackedGfxObjContactAsset readied)
    {
        _denseGfxObjs.TryAdd(gfxObjRefIdent, readied);
        if (readied.VisualBounds is { } limits)
            _extents.TryAdd(gfxObjRefIdent, GfxObjVisualExtent.Unpack(limits));

        var bsp = readied.PhysicsBsp;
        if (bsp.TrunkOrdinal < 0)
            return;

        var trunk = bsp.Joints[bsp.TrunkOrdinal].BoundingSphere;
        _gfxObjs.TryAdd(gfxObjRefIdent, new GfxObjKinetics
        {
            SourceId = gfxObjRefIdent,
            BoundingSphere = new Orb { Center = trunk.Origin, Radius = trunk.Radius },
            DenseKineticBsp = bsp,
            VisualLimits = readied.VisualBounds,
        });
    }

    private static void PrintGfxObjRef(uint gfxObjRefIdent, GfxObjKinetics kinetics)
    {
        try
        {
            var print = GfxObjSnapshotWriter.Capture(gfxObjRefIdent, kinetics);
            string trail = Path.Combine(KineticTelemetry.ProbeDumpGfxObjsPath, FormattableString.Invariant($"0x{gfxObjRefIdent:X8}.gfxobj.json"));
            GfxObjSnapshotWriter.Write(print, trail);
            Console.WriteLine(FormattableString.Invariant($"[gfxobj-dump] wrote 0x{gfxObjRefIdent:X8} polys={print.ResolvedPolygons.Count} → {trail}"));
        }
        catch (Exception exc)
        {
            Console.WriteLine(FormattableString.Invariant($"[gfxobj-dump] FAILED to dump 0x{gfxObjRefIdent:X8}: {exc.GetType().Name}: {exc.Message}"));
        }
    }

    private void AdmitReadiedRig(uint rigIdent, PackedSetupContact readied)
    {
        _denseSetups.TryAdd(rigIdent, readied);
        _setups.TryAdd(rigIdent, new SetupKinetics
        {
            SourceId = rigIdent,
            Height = readied.Height,
            Radius = readied.Radius,
            StepUpHeight = readied.StepUpHeight,
            StepDownHeight = readied.StepDownHeight,
            PlanarImpact = readied,
        });
    }

    private void AdmitReadiedChamber(
        uint environChamberIdent,
        RoomCell environChamber,
        Matrix4x4 realmXform,
        PackedCellStructContactAsset structure,
        PackedEnvCellTopology wiring)
    {
        if (structure.ContainmentBsp.TrunkIdx < 0)
            return;

        World.TryAppendPlanarChamberStruct(environChamberIdent, structure);
        World.TryAppendPlanarEnvironChamber(environChamberIdent, wiring);

        if (!ChamberGraph.Contains(environChamberIdent))
            ChamberGraph.Add(UcgEnvCell.FromReadied(environChamberIdent, realmXform, structure, wiring));

        if (Cells.ContainsKey(environChamberIdent))
            return;

        Matrix4x4.Invert(realmXform, out Matrix4x4 inv);
        List<PortalFacts> gateways = new List<PortalFacts>(wiring.Portals.Length);
        foreach (PackedEnvCellPortal gateway in wiring.Portals)
            gateways.Add(new PortalFacts(gateway.OtherCellId, gateway.PolygonId, gateway.Flags));

        World.TryAppendChamberStruct(environChamberIdent, new CellKinetics
        {
            SourceId = environChamberIdent,
            WorldTransform = realmXform,
            InverseWorldTransform = inv,
            Resolved = new Dictionary<ushort, SettledPolygon>(),
            PackedKineticBsp = structure.PhysicsBsp,
            PlanarContainmentBsp = structure.ContainmentBsp,
            PlanarGatewayPolygs = structure.PortalPolygons,
            PlanarWiring = wiring,
            Portals = gateways,
            VisibleCellIds = new HashSet<uint>(wiring.VisibleCellIds),
            SeenOutside = wiring.SeenOutside,
            RestrictionObj = environChamber.RestrictionObjectId,
        });
    }

    private static void PrintChamber(uint environChamberIdent, CellKinetics chamber)
    {
        try
        {
            var print = CellSnapshotWriter.Capture(environChamberIdent, chamber);
            string trail = Path.Combine(KineticTelemetry.ProbeDumpCellsPath, FormattableString.Invariant($"0x{environChamberIdent:X8}.json"));
            CellSnapshotWriter.Write(print, trail);
            Console.WriteLine(FormattableString.Invariant($"[cell-dump] wrote 0x{environChamberIdent:X8} polys={print.ResolvedPolygons.Count} portals={print.Portals.Count} → {trail}"));
        }
        catch (Exception exc)
        {
            Console.WriteLine(FormattableString.Invariant($"[cell-dump] FAILED to dump 0x{environChamberIdent:X8}: {exc.GetType().Name}: {exc.Message}"));
        }
    }

    private static void InspectChamberStash(
        uint environChamberIdent,
        ShellCell chamberStruct,
        Matrix4x4 realmXform,
        Dictionary<ushort, SettledPolygon> settled,
        List<PortalFacts> gateways,
        int shownTally)
    {
        var trunk = chamberStruct.CollisionTree?.Root;
        int leafPolys = 0;
        int unmatched = 0;
        if (trunk is not null)
        {
            var pile = new Stack<PhysicsBspNode>();
            pile.Push(trunk);
            while (pile.TryPop(out PhysicsBspNode? num))
            {
                if (num.Polygons is not null)
                {
                    foreach (ushort pid in num.Polygons)
                    {
                        ++leafPolys;
                        if (!settled.ContainsKey(pid))
                            ++unmatched;
                    }
                }
                if (num.Front is not null) pile.Push(num.Front);
                if (num.Back is not null) pile.Push(num.Back);
            }
        }

        Orb? sphere = trunk?.Bounds;
        string bsPhrase = sphere is null
            ? "bsphere=n/a"
            : FormattableString.Invariant($"bsphere=({sphere.Center.X:F2},{sphere.Center.Y:F2},{sphere.Center.Z:F2}) r={sphere.Radius:F2}");

        Vector3 realmOrigin = Vector3.Transform(Vector3.Zero, realmXform);

        string gatewayPhrase;
        gatewayPhrase = gateways.Count is 0 ? "portalTargets=[]" : InspectChamberStashBranch(gateways);

        Console.WriteLine(FormattableString.Invariant(
            $"[cell-cache] envCellId=0x{environChamberIdent:X8} physicsPolyCount={chamberStruct.CollisionFacets?.Count ?? 0} resolvedCount={settled.Count} bspTotalLeafPolys={leafPolys} bspUnmatchedIds={unmatched} {bsPhrase} portalCount={gateways.Count} visibleCells={shownTally} cellBspRoot={(chamberStruct.CellTree?.Root is null ? "null" : "ok")} worldOrigin=({realmOrigin.X:F2},{realmOrigin.Y:F2},{realmOrigin.Z:F2}) {gatewayPhrase}"));
    }

    private static string InspectChamberStashBranch(List<PortalFacts> gateways)
    {
        string gatewayPhrase;
        StringBuilder builder = new StringBuilder("portalTargets=[");
        for (int idx = 0; idx < gateways.Count; ++idx)
        {
            if (idx > 0) builder.Append(',');
            builder.Append(FormattableString.Invariant($"(cell=0x{gateways[idx].OtherCellId:X4},poly=0x{gateways[idx].PolygonId:X4},flags=0x{gateways[idx].Flags:X4})"));
        }
        builder.Append(']');
        gatewayPhrase = builder.ToString();
        return gatewayPhrase;
    }

    private static bool ConsultCorners(Facet poly, MeshVertices vertArr, Vector3[] corners)
    {
        for (int idx = 0; idx < corners.Length; ++idx)
        {
            if (!vertArr.ByIndex.TryGetValue((ushort)poly.VertexIds[idx], out var vert))
                return false;
            corners[idx] = vert.Position;
        }
        return true;
    }
}
