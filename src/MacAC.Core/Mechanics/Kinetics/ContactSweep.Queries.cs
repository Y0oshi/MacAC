using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

// The individual dual-path queries
internal static partial class ContactSweep
{

    private readonly struct CellContainmentPresence(CellKinetics chamber) : IDualQuery<bool>
    {
        public string Kind => "HasCellContainment";
        public uint SourceId => chamber.SourceId;
        public bool Graph(Changeover? _) => chamber.CellBSP?.Root is not null;
        public bool Flat(Changeover? _)
        {
            return (chamber.PlanarContainmentBsp ?? throw AbsentPlanar("cell containment")).TrunkIdx >= 0;
        }

        public string Input() => string.Empty;
        public void Record(ProxyShadowVerifier verifier, long s, bool g, bool f, Changeover? _, Changeover? __, string idx) => CaptureBool(this, verifier, s, g, f, idx);
    }

    private readonly struct CellPhysicsPresence(CellKinetics chamber) : IDualQuery<bool>
    {
        public string Kind => "HasCellPhysics";
        public uint SourceId => chamber.SourceId;
        public bool Graph(Changeover? _) => chamber.BSP?.Root is not null;
        public bool Flat(Changeover? _)
        {
            return (chamber.PackedKineticBsp ?? throw AbsentPlanar("cell physics")).TrunkOrdinal >= 0;
        }

        public string Input() => string.Empty;
        public void Record(ProxyShadowVerifier verifier, long s, bool g, bool f, Changeover? _, Changeover? __, string idx) => CaptureBool(this, verifier, s, g, f, idx);
    }

    private readonly struct GfxObjPhysicsPresence(GfxObjKinetics gfxObject) : IDualQuery<bool>
    {
        public string Kind => "HasGfxObjPhysics";
        public uint SourceId => gfxObject.SourceId;
        public bool Graph(Changeover? _) => gfxObject.BSP?.Root is not null;
        public bool Flat(Changeover? _)
        {
            return (gfxObject.DenseKineticBsp ?? throw AbsentPlanar("GfxObj physics")).TrunkOrdinal >= 0;
        }

        public string Input() => string.Empty;
        public void Record(ProxyShadowVerifier verifier, long s, bool g, bool f, Changeover? _, Changeover? __, string idx) => CaptureBool(this, verifier, s, g, f, idx);
    }

    internal static bool HasCellContainment(KineticAssetCache stash, CellKinetics chamber)
    {
        return Rule<CellContainmentPresence, bool>(stash, new CellContainmentPresence(chamber), null);
    }

    internal static bool HasPhysics(KineticAssetCache stash, CellKinetics chamber)
    {
        return Rule<CellPhysicsPresence, bool>(stash, new CellPhysicsPresence(chamber), null);
    }

    internal static bool HasPhysics(KineticAssetCache stash, GfxObjKinetics gfxObject)
    {
        return Rule<GfxObjPhysicsPresence, bool>(stash, new GfxObjPhysicsPresence(gfxObject), null);
    }

    private readonly struct CellRootSphere(CellKinetics chamber) : IDualQuery<PackedContactSphere>
    {
        public string Kind => "RootBoundingSphere";
        public uint SourceId => chamber.SourceId;

        public PackedContactSphere Graph(Changeover? _)
        {
            Orb orb = chamber.BSP!.Root!.Bounds;
            return new PackedContactSphere(orb.Center, orb.Radius);
        }

        public PackedContactSphere Flat(Changeover? _)
        {
            PackedKineticBsp planar = chamber.PackedKineticBsp ?? throw AbsentPlanar("cell physics");
            return planar.Joints[planar.TrunkOrdinal].BoundingSphere;
        }

        public string Input() => string.Empty;

        public void Record(ProxyShadowVerifier verifier, long s, PackedContactSphere sphere, PackedContactSphere f, Changeover? _, Changeover? __, string idx) =>
            verifier.CaptureOrb(s, Kind, SourceId, sphere, f);
    }

    internal static PackedContactSphere RootBoundingSphere(KineticAssetCache stash, CellKinetics chamber)
    {
        return Rule<CellRootSphere, PackedContactSphere>(stash, new CellRootSphere(chamber), null);
    }

    private readonly struct PointInCellQuery(CellKinetics chamber, Vector3 ownPt) : IDualQuery<bool>
    {
        public string Kind => "PointInsideCell";
        public uint SourceId => chamber.SourceId;
        public bool Graph(Changeover? _) => CellBspProbe.PtInsideChamberBsp(chamber.CellBSP?.Root, ownPt);
        public bool Flat(Changeover? _)
        {
            return PackedBspQuery.PtInsideCellBsp(chamber.PlanarContainmentBsp ?? throw AbsentPlanar("cell containment"), ownPt);
        }

        public string Input() => ProxyShadowVerifier.ComposeFeed(ownPt, 0f);
        public void Record(ProxyShadowVerifier verifier, long s, bool g, bool f, Changeover? _, Changeover? __, string idx) => CaptureBool(this, verifier, s, g, f, idx);
    }

    private readonly struct SphereInCellQuery(CellKinetics chamber, Vector3 ownMiddle, float radius) : IDualQuery<bool>
    {
        public string Kind => "SphereIntersectsCell";
        public uint SourceId => chamber.SourceId;
        public bool Graph(Changeover? _)
        {
            return CellBspProbe.SphereIntersectsCellBsp(chamber.CellBSP?.Root, ownMiddle, radius);
        }

        public bool Flat(Changeover? _)
        {
            return PackedBspQuery.SphereIntersectsCellBsp(chamber.PlanarContainmentBsp ?? throw AbsentPlanar("cell containment"), ownMiddle, radius);
        }

        public string Input() => ProxyShadowVerifier.ComposeFeed(ownMiddle, radius);
        public void Record(ProxyShadowVerifier verifier, long s, bool g, bool f, Changeover? _, Changeover? __, string idx) => CaptureBool(this, verifier, s, g, f, idx);
    }

    private readonly struct BoxInCellQuery(CellKinetics chamber, Vector3 ownLower, Vector3 ownUpper) : IDualQuery<bool>
    {
        public string Kind => "BoxIntersectsCell";
        public uint SourceId => chamber.SourceId;
        public bool Graph(Changeover? _)
        {
            return CellBspProbe.BoxIntersectsCellBsp(chamber.CellBSP?.Root, ownLower, ownUpper);
        }

        public bool Flat(Changeover? _)
        {
            return PackedBspQuery.BoxIntersectsCellBsp(chamber.PlanarContainmentBsp ?? throw AbsentPlanar("cell containment"), ownLower, ownUpper);
        }

        public string Input() => ProxyShadowVerifier.ComposeFeed(ownLower, ownUpper);
        public void Record(ProxyShadowVerifier verifier, long s, bool g, bool f, Changeover? _, Changeover? __, string idx) => CaptureBool(this, verifier, s, g, f, idx);
    }

    internal static bool PointInsideCell(KineticAssetCache stash, CellKinetics chamber, Vector3 ownPt)
    {
        return Rule<PointInCellQuery, bool>(stash, new PointInCellQuery(chamber, ownPt), null);
    }

    internal static bool SphereIntersectsCell(KineticAssetCache stash, CellKinetics chamber, Vector3 ownMiddle, float radius)
    {
        return Rule<SphereInCellQuery, bool>(stash, new SphereInCellQuery(chamber, ownMiddle, radius), null);
    }

    internal static bool BoxIntersectsCell(KineticAssetCache stash, CellKinetics chamber, Vector3 ownLower, Vector3 ownUpper)
    {
        return Rule<BoxInCellQuery, bool>(stash, new BoxInCellQuery(chamber, ownLower, ownUpper), null);
    }

    // The sphere-path arguments a collision sweep takes, kept together so both forms see the same
    // values
    private readonly record struct SweepArgs(
        Vector3 LocalSphereCenter,
        float LocalSphereRadius,
        bool HasLocalSphere1,
        Vector3 LocalSphere1Center,
        float LocalSphere1Radius,
        Vector3 LocalCurrentCenter,
        Vector3 LocalSpaceZ,
        float Scale,
        Quaternion LocalToWorld,
        KineticEngine? Engine,
        Vector3 WorldOrigin)
    {
        public ShiftVerdict Flat(PackedKineticBsp bsp, Changeover changeover)
        {
            return PackedBspQuery.SeekImpacts(
            bsp, changeover,
            LocalSphereCenter, LocalSphereRadius, HasLocalSphere1, LocalSphere1Center, LocalSphere1Radius,
            LocalCurrentCenter, LocalSpaceZ, Scale, LocalToWorld, Engine, WorldOrigin);
        }

        public ShiftVerdict Graph(PhysicsBspNode? trunk, Dictionary<ushort, SettledPolygon> settled, Changeover changeover)
        {
            return CellBspProbe.SeekImpacts(
                trunk, settled, changeover,
                LocalSphereCenter, LocalSphereRadius, HasLocalSphere1, LocalSphere1Center, LocalSphere1Radius,
                LocalCurrentCenter, LocalSpaceZ, Scale, LocalToWorld, Engine, WorldOrigin);
        }

        public string Input()
        {
            return ProxyShadowVerifier.ComposeFeed(
            LocalSphereCenter, LocalSphereRadius, HasLocalSphere1, LocalSphere1Center, LocalSphere1Radius,
            LocalCurrentCenter, LocalSpaceZ, Scale, LocalToWorld, WorldOrigin);
        }
    }

    private readonly struct CellSweep(CellKinetics chamber, SweepArgs arguments) : IDualQuery<ShiftVerdict>
    {
        public string Kind => "FindCellCollisions";
        public uint SourceId => chamber.SourceId;
        public ShiftVerdict Graph(Changeover? transition) => arguments.Graph(chamber.BSP?.Root, chamber.Resolved, transition!);
        public ShiftVerdict Flat(Changeover? transition)
        {
            return arguments.Flat(chamber.PackedKineticBsp ?? throw AbsentPlanar("cell physics"), transition!);
        }

        public string Input() => arguments.Input();
        public void Record(ProxyShadowVerifier verifier, long s, ShiftVerdict verdict, ShiftVerdict f, Changeover? transition, Changeover? ft, string idx)
        {
            verifier.CaptureChangeover(s, Kind, SourceId, verdict, f, transition!, ft!, idx);
        }
    }

    private readonly struct GfxObjSweep(GfxObjKinetics gfxObject, SweepArgs arguments) : IDualQuery<ShiftVerdict>
    {
        public string Kind => "FindGfxObjCollisions";
        public uint SourceId => gfxObject.SourceId;
        public ShiftVerdict Graph(Changeover? transition) => arguments.Graph(gfxObject.BSP!.Root!, gfxObject.Settled, transition!);
        public ShiftVerdict Flat(Changeover? transition)
        {
            return arguments.Flat(gfxObject.DenseKineticBsp ?? throw AbsentPlanar("GfxObj physics"), transition!);
        }

        public string Input() => arguments.Input();
        public void Record(ProxyShadowVerifier verifier, long s, ShiftVerdict verdict, ShiftVerdict f, Changeover? transition, Changeover? ft, string idx)
        {
            verifier.CaptureChangeover(s, Kind, SourceId, verdict, f, transition!, ft!, idx);
        }
    }

    internal static ShiftVerdict SeekImpacts(
        KineticAssetCache stash,
        CellKinetics chamber,
        Changeover changeover,
        Vector3 ownOrbMiddle,
        float ownOrbRadius,
        bool hasOwnSphere1,
        Vector3 ownSphere1Middle,
        float ownSphere1Radius,
        Vector3 ownLatestMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm,
        KineticEngine? engine,
        Vector3 realmOrigin)
    {
        SweepArgs arguments = new SweepArgs(
            ownOrbMiddle, ownOrbRadius, hasOwnSphere1, ownSphere1Middle, ownSphere1Radius,
            ownLatestMiddle, ownSpaceZ, scaling, ownToRealm, engine, realmOrigin);
        return Rule<CellSweep, ShiftVerdict>(stash, new CellSweep(chamber, arguments), changeover);
    }

    internal static ShiftVerdict SeekImpacts(
        KineticAssetCache stash,
        GfxObjKinetics gfxObject,
        Changeover changeover,
        Vector3 ownOrbMiddle,
        float ownOrbRadius,
        bool hasOwnSphere1,
        Vector3 ownSphere1Middle,
        float ownSphere1Radius,
        Vector3 ownLatestMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm,
        KineticEngine? engine,
        Vector3 realmOrigin)
    {
        SweepArgs arguments = new SweepArgs(
            ownOrbMiddle, ownOrbRadius, hasOwnSphere1, ownSphere1Middle, ownSphere1Radius,
            ownLatestMiddle, ownSpaceZ, scaling, ownToRealm, engine, realmOrigin);
        return Rule<GfxObjSweep, ShiftVerdict>(stash, new GfxObjSweep(gfxObject, arguments), changeover);
    }
}
