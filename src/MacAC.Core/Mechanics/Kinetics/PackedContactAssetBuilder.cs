using System.Collections.Immutable;
using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public static class PackedContactAssetBuilder
{

    public static PackedKineticBsp FlattenKineticsBsp(PhysicsBspNode? trunk, IReadOnlyDictionary<ushort, SettledPolygon> settled)
    {
        ArgumentNullException.ThrowIfNull(settled);

        var polygChart = FlattenPolygChart(settled);
        if (trunk is null)
            return new PackedKineticBsp(-1, [], [], polygChart);

        var ranks = new List<(BspTag Type, Plane Plane, int Leaf, int Solid, PackedContactSphere Sphere, PackedIndexRange Polygons)>();
        List<int> polygOrdinals = new List<int>();

        List<Links> links = TraversePreOrdering(
            trunk,
            static num => num.Tag,
            static num => num.Front,
            static num => num.Back,
            "physics BSP source node",
            "Physics BSP source contains a cycle or shared child.",
            "Physics BSP child has no valid parent.",
            (joint, ordinal) =>
            {
                int begin = polygOrdinals.Count;
                if (joint.Polygons is null)
                    throw new InvalidDataException($"Physics BSP node {ordinal} has no polygon-reference list");
                foreach (ushort polygIdent in joint.Polygons)
                {
                    if (!polygChart.TrySeekPolygOrdinal(polygIdent, out int polygOrdinal))
                        throw new InvalidDataException($"Physics BSP leaf references absent polygon 0x{polygIdent:X4}.");
                    polygOrdinals.Add(polygOrdinal);
                }

                Orb orb = joint.Bounds
                    ?? throw new InvalidDataException($"Physics BSP node {ordinal} has no bounding sphere");
                ranks.Add((
                    joint.Tag,
                    joint.Splitter,
                    joint.LeafIndex,
                    joint.Solid,
                    new PackedContactSphere(orb.Center, orb.Radius),
                    new PackedIndexRange(begin, polygOrdinals.Count - begin)));
            });

        var joints = ImmutableArray.CreateBuilder<PackedKineticBspNode>(ranks.Count);
        for (int idx = 0; idx < ranks.Count; ++idx)
        {
            var rank = ranks[idx];
            joints.Add(new PackedKineticBspNode(
                rank.Type, rank.Plane, links[idx].Positive, links[idx].Negative, rank.Leaf, rank.Solid, rank.Sphere, rank.Polygons));
        }

        return new PackedKineticBsp(0, joints.MoveToImmutable(), ImmutableArray.CreateRange(polygOrdinals), polygChart);
    }

    // Child indices for one emitted row, filled in as the walk reaches each child
    private struct Links
    {
        public int Positive;
        public int Negative;

        public static Links None => new() { Positive = -1, Negative = -1 };
    }

    private readonly record struct MechPending<TNode>(TNode Node, int Parent, bool IsPositive);

    public static PackedCellContainmentBsp FlattenChamberContainmentBsp(CellBspNode? trunk)
    {
        if (trunk is null)
            return new PackedCellContainmentBsp(-1, []);

        var ranks = new List<(BspTag Type, Plane Plane, int Leaf)>();
        List<Links> links = TraversePreOrdering(
            trunk,
            static num => num.Tag,
            static num => num.Front,
            static num => num.Back,
            "cell-containment BSP source node",
            "Cell-containment BSP source contains a cycle or shared child.",
            "Cell BSP child has no valid parent.",
            (joint, _) => ranks.Add((joint.Tag, joint.Splitter, joint.LeafIndex)));

        var joints = ImmutableArray.CreateBuilder<PackedCellBspNode>(ranks.Count);
        for (int idx = 0; idx < ranks.Count; ++idx)
            joints.Add(new PackedCellBspNode(ranks[idx].Type, ranks[idx].Plane, links[idx].Positive, links[idx].Negative, ranks[idx].Leaf));

        return new PackedCellContainmentBsp(0, joints.MoveToImmutable());
    }

    public static PackedPolygonTable FlattenPolygChart(IReadOnlyDictionary<ushort, SettledPolygon> settled)
    {
        ArgumentNullException.ThrowIfNull(settled);

        var byIdent = new List<KeyValuePair<ushort, SettledPolygon>>(settled);
        byIdent.Sort(static (left, right) => left.Key.CompareTo(right.Key));

        int vertTally = 0;
        foreach ((ushort ident, SettledPolygon src) in byIdent)
        {
            DemandSane(ident, src);
            vertTally = checked(vertTally + src.Vertices.Length);
        }

        var polygs = ImmutableArray.CreateBuilder<PackedContactPolygon>(byIdent.Count);
        var verts = ImmutableArray.CreateBuilder<Vector3>(vertTally);
        foreach ((ushort ident, SettledPolygon src) in byIdent)
        {
            int begin = verts.Count;
            verts.AddRange(src.Vertices);
            polygs.Add(new PackedContactPolygon(
                ident,
                src.Plane,
                src.SidesType,
                src.NumPoints,
                new PackedIndexRange(begin, verts.Count - begin)));
        }

        return new PackedPolygonTable(polygs.MoveToImmutable(), verts.MoveToImmutable());
    }

    public static PackedSetupContact FlattenSetup(SetupKinetics src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new PackedSetupContact(
            BundleCylinders(src.CylSpheres),
            BundleOrbs(src.Spheres),
            src.Height,
            src.Radius,
            src.StepUpHeight,
            src.StepDownHeight);
    }

    public static PackedSetupContact FlattenSetup(RigSpec src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new PackedSetupContact(
            BundleCylinders(src.Capsules),
            BundleOrbs(src.Orbs),
            src.Height,
            src.Radius,
            src.StepUpHeight,
            src.StepDownHeight);
    }

    public static PackedGfxObjContactAsset FlattenGfxObj(GfxObjKinetics src, GfxObjVisualExtent? visualLimits = null)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(src.BSP);

        PackedContactSphere? boundingOrb = src.BoundingSphere is { } orb
            ? new PackedContactSphere(orb.Center, orb.Radius)
            : null;
        return new PackedGfxObjContactAsset(FlattenKineticsBsp(src.BSP.Root, src.Settled), boundingOrb, Pack(visualLimits));
    }

    public static PackedGfxObjContactAsset FlattenGfxObj(PartMesh src)
    {
        ArgumentNullException.ThrowIfNull(src);

        var visual = Pack(src.Vertices is null ? null : KineticAssetCache.CalculateVisualLimits(src.Vertices));

        bool hasKinetics = (src.Bits & PartMeshBits.HasPhysics) != 0
            && src.CollisionTree?.Root is not null
            && src.Vertices is not null;
        if (!hasKinetics)
            return new PackedGfxObjContactAsset(VacantKineticsBsp(), null, visual);

        var settled = KineticAssetCache.LocatePolygs(src.CollisionFacets, src.Vertices!);
        Orb trunk = src.CollisionTree!.Root!.Bounds
            ?? throw new InvalidDataException("GfxObj physics BSP root has no bounding sphere");

        return new PackedGfxObjContactAsset(
            FlattenKineticsBsp(src.CollisionTree.Root, settled),
            new PackedContactSphere(trunk.Center, trunk.Radius),
            visual);
    }

    public static PackedCellStructContactAsset FlattenChamberStructure(ShellCell src)
    {
        ArgumentNullException.ThrowIfNull(src);

        PackedKineticBsp kineticsBsp = src.CollisionTree?.Root is { } trunk
            ? FlattenKineticsBsp(trunk, KineticAssetCache.LocatePolygs(src.CollisionFacets, src.Vertices))
            : VacantKineticsBsp();

        var gatewaySettled = KineticAssetCache.LocatePolygs(src.Facets, src.Vertices);
        return new PackedCellStructContactAsset(
            kineticsBsp,
            FlattenChamberContainmentBsp(src.CellTree?.Root),
            FlattenPolygChart(gatewaySettled));
    }

    public static PackedEnvCellTopology FlattenEnvCellTopology(uint environChamberIdent, RoomCell src, PackedPolygonTable gatewayPolygs)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(gatewayPolygs);

        var gateways = ImmutableArray.CreateBuilder<PackedEnvCellPortal>(src.Doorways.Count);
        foreach (CellDoorway gateway in src.Doorways)
        {
            gateways.Add(BundleGateway(
                gatewayPolygs,
                gateway.OtherCellId,
                gateway.PolygonId,
                (ushort)gateway.Bits,
                $"EnvCell 0x{environChamberIdent:X8} portal references missing polygon 0x{gateway.PolygonId:X4}."));
        }

        uint stem = environChamberIdent & 0xFFFF_0000u;
        ImmutableArray<uint> shown = src.VisibleCells is null
            ? []
            : SortedUnique(src.VisibleCells.Select(ident => stem | ident));

        return new PackedEnvCellTopology(gateways.MoveToImmutable(), shown, (src.Bits & RoomCellBits.SeenOutside) != 0);
    }

    public static PackedEnvCellTopology FlattenEnvCellTopology(
        IReadOnlyList<PortalFacts> gateways,
        IEnumerable<uint> shownChamberIdents,
        bool observedBeyond,
        PackedPolygonTable gatewayPolygs)
    {
        ArgumentNullException.ThrowIfNull(gateways);
        ArgumentNullException.ThrowIfNull(shownChamberIdents);
        ArgumentNullException.ThrowIfNull(gatewayPolygs);

        var dense = ImmutableArray.CreateBuilder<PackedEnvCellPortal>(gateways.Count);
        foreach (PortalFacts gateway in gateways)
        {
            dense.Add(BundleGateway(
                gatewayPolygs,
                gateway.OtherCellId,
                gateway.PolygonId,
                gateway.Flags,
                $"EnvCell portal references missing polygon 0x{gateway.PolygonId:X4}."));
        }

        return new PackedEnvCellTopology(dense.MoveToImmutable(), SortedUnique(shownChamberIdents), observedBeyond);
    }

    public static PackedCellContactAsset FlattenChamber(CellKinetics src)
    {
        ArgumentNullException.ThrowIfNull(src);

        var gatewayPolygs = FlattenPolygChart(src.PortalPolygons ?? new Dictionary<ushort, SettledPolygon>());
        var structure = new PackedCellStructContactAsset(
            FlattenKineticsBsp(src.BSP?.Root, src.Resolved),
            FlattenChamberContainmentBsp(src.CellBSP?.Root),
            gatewayPolygs);
        var wiring = FlattenEnvCellTopology(src.Portals, src.VisibleCellIds, src.SeenOutside, gatewayPolygs);
        return new PackedCellContactAsset(structure, wiring);
    }
    private static PackedKineticBsp VacantKineticsBsp() => new(-1, [], [], PackedPolygonTable.Empty);

    private static List<Links> TraversePreOrdering<TNode>(
        TNode trunk,
        Func<TNode, BspTag> kind,
        Func<TNode, TNode?> positive,
        Func<TNode, TNode?> negative,
        string srcLabel,
        string cycleMsg,
        string orphanMsg,
        Action<TNode, int> emit)
        where TNode : class
    {
        List<Links> links = new List<Links>();
        HashSet<TNode> observed = new HashSet<TNode>(ReferenceEqualityComparer.Instance);
        var queued = new Stack<MechPending<TNode>>();
        queued.Push(new MechPending<TNode>(trunk, -1, false));

        while (queued.TryPop(out MechPending<TNode> cycle))
        {
            TNode joint = cycle.Node;
            TNode? spot = positive(joint);
            TNode? neg = negative(joint);
            PackedBspNodeContract.Validate(kind(joint), spot is not null, neg is not null, srcLabel);
            if (!observed.Add(joint))
                throw new InvalidDataException(cycleMsg);

            int ordinal = links.Count;
            if (cycle.Parent >= 0)
            {
                if (cycle.Parent >= links.Count)
                    throw new InvalidDataException(orphanMsg);
                Links ancestor = links[cycle.Parent];
                if (cycle.IsPositive)
                    ancestor.Positive = ordinal;
                else
                    ancestor.Negative = ordinal;
                links[cycle.Parent] = ancestor;
            }

            links.Add(Links.None);
            emit(joint, ordinal);

            // Stack order is reversed so positive is emitted before negative
            if (neg is not null)
                queued.Push(new MechPending<TNode>(neg, ordinal, false));
            if (spot is not null)
                queued.Push(new MechPending<TNode>(spot, ordinal, true));
        }

        return links;
    }

    private static void DemandSane(ushort ident, SettledPolygon src)
    {
        if (src is null)
            throw new InvalidDataException($"Polygon 0x{ident:X4} is null");
        if (src.Id != ident)
            throw new InvalidDataException($"Polygon dictionary key 0x{ident:X4} doesn't match row ID 0x{src.Id:X4}.");
        if (src.Vertices is null)
            throw new InvalidDataException($"Polygon 0x{ident:X4} has no vertices");
        if (src.NumPoints != src.Vertices.Length)
            throw new InvalidDataException($"Polygon 0x{ident:X4} NumPoints {src.NumPoints} doesn't match vertex count {src.Vertices.Length}.");
    }

    private static ImmutableArray<PackedContactCylinder> BundleCylinders(IReadOnlyCollection<Capsule>? ranks)
    {
        var dense = ImmutableArray.CreateBuilder<PackedContactCylinder>(ranks?.Count ?? 0);
        if (ranks is null)
            return dense.MoveToImmutable();
        foreach (Capsule rank in ranks)
        {
            if (rank is null)
                throw new InvalidDataException("Setup cylinder row is null");
            dense.Add(new PackedContactCylinder(rank.Center, rank.Radius, rank.Height));
        }
        return dense.MoveToImmutable();
    }

    private static ImmutableArray<PackedContactSphere> BundleOrbs(IReadOnlyCollection<Orb>? ranks)
    {
        var dense = ImmutableArray.CreateBuilder<PackedContactSphere>(ranks?.Count ?? 0);
        if (ranks is null)
            return dense.MoveToImmutable();
        foreach (Orb rank in ranks)
        {
            if (rank is null)
                throw new InvalidDataException("Setup sphere row is null");
            dense.Add(new PackedContactSphere(rank.Center, rank.Radius));
        }
        return dense.MoveToImmutable();
    }

    private static PackedGfxObjVisualExtent? Pack(GfxObjVisualExtent? reach)
    {
        return reach is null ? null : new PackedGfxObjVisualExtent(reach.Min, reach.Max, reach.Center, reach.Radius, reach.HalfExtents);
    }

    private static ImmutableArray<uint> SortedUnique(IEnumerable<uint> idents)
    {
        SortedSet<uint> unique = new SortedSet<uint>(idents);
        var dense = ImmutableArray.CreateBuilder<uint>(unique.Count);
        foreach (uint ident in unique)
            dense.Add(ident);
        return dense.MoveToImmutable();
    }

    private static PackedEnvCellPortal BundleGateway(PackedPolygonTable gatewayPolygs, ushort anotherChamberIdent, ushort polygIdent, ushort flagSet, string absentMsg)
    {
        if (!gatewayPolygs.TrySeekPolygOrdinal(polygIdent, out int polygOrdinal))
            throw new InvalidDataException(absentMsg);
        return new PackedEnvCellPortal(anotherChamberIdent, polygIdent, flagSet, polygOrdinal);
    }
}
