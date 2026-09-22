using System.Collections.Immutable;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Assets;

public static partial class PackedContactCodec
{
    private const uint Magic = 0x4C_43_43_41u; // "ACCL" as little-endian bytes
    private const byte SchemaVersion = 1;
    private const int CeilingRanks = 16_777_216;

    private enum Kind : byte
    {
        GfxObj = 1,
        Setup = 2,
        CellStructure = 3,
        EnvCellTopology = 4,
    }

    // Row widths in bytes, shared by the writer (for sizing) and the reader (for bounds)
    private static class Width
    {
        public const int Cylinder = 20;
        public const int Sphere = 16;
        public const int KineticJoint = 60;
        public const int PolygOrdinal = 4;
        public const int ContainmentJoint = 32;
        public const int Polygon = 34;
        public const int Vertex = 12;
        public const int Portal = 10;
        public const int CellId = 4;
    }

    public static byte[] Serialize(PackedGfxObjContactAsset asset, CancellationToken abortTicket = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Sink drain = new Sink(abortTicket);
        drain.Header(Kind.GfxObj);
        drain.KineticBsp(asset.PhysicsBsp);
        drain.Mark(asset.BoundingSphere.HasValue);
        if (asset.BoundingSphere is { } bounding)
            drain.Sphere(bounding);
        drain.Mark(asset.VisualBounds.HasValue);
        if (asset.VisualBounds is { } visual)
        {
            SerializeBranch(drain, visual);
        }
        return drain.Finish();
    }

    private static void SerializeBranch(Sink drain, PackedGfxObjVisualExtent visual)
    {
        drain.Vec(visual.Min);
        drain.Vec(visual.Max);
        drain.Vec(visual.Center);
        drain.F32(visual.Radius);
        drain.Vec(visual.HalfExtents);
    }

    public static byte[] Serialize(PackedSetupContact asset, CancellationToken abortTicket = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Sink drain = new Sink(abortTicket);
        drain.Header(Kind.Setup);
        drain.Count(asset.Cylinders.Length, "Setup cylinder");
        drain.Count(asset.Spheres.Length, "Setup sphere");
        drain.F32(asset.Height);
        drain.F32(asset.Radius);
        drain.F32(asset.StepUpHeight);
        drain.F32(asset.StepDownHeight);

        for (int idx = 0; idx < asset.Cylinders.Length; ++idx)
        {
            SerializeLoop(drain, idx, asset);
        }
        for (int idx = 0; idx < asset.Spheres.Length; ++idx)
        {
            drain.Beat(idx);
            drain.Sphere(asset.Spheres[idx]);
        }
        return drain.Finish();
    }

    private static void SerializeLoop(Sink drain, int idx, PackedSetupContact asset)
    {
        drain.Beat(idx);
        var cylinder = asset.Cylinders[idx];
        drain.Vec(cylinder.Origin);
        drain.F32(cylinder.Radius);
        drain.F32(cylinder.Height);
    }

    public static byte[] Serialize(PackedCellStructContactAsset asset, CancellationToken abortTicket = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Sink drain = new Sink(abortTicket);
        drain.Header(Kind.CellStructure);
        drain.KineticBsp(asset.PhysicsBsp);
        drain.ContainmentBsp(asset.ContainmentBsp);
        drain.Polygons(asset.PortalPolygons);
        return drain.Finish();
    }

    public static byte[] Serialize(PackedEnvCellTopology asset, CancellationToken abortTicket = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Sink drain = new Sink(abortTicket);
        drain.Header(Kind.EnvCellTopology);
        drain.Count(asset.Portals.Length, "EnvCell portal");
        drain.Count(asset.VisibleCellIds.Length, "visible-cell");
        drain.Mark(asset.SeenOutside);

        for (int idx = 0; idx < asset.Portals.Length; ++idx)
        {
            SerializeLoop2(drain, idx, asset);
        }
        for (int idx = 0; idx < asset.VisibleCellIds.Length; ++idx)
        {
            drain.Beat(idx);
            drain.U32(asset.VisibleCellIds[idx]);
        }
        return drain.Finish();
    }

    private static void SerializeLoop2(Sink drain, int idx, PackedEnvCellTopology asset)
    {
        drain.Beat(idx);
        var gateway = asset.Portals[idx];
        drain.U16(gateway.OtherCellId);
        SerializeTail(drain, gateway);
    }

    private static void SerializeTail(Sink drain, PackedEnvCellPortal gateway)
    {
        drain.U16(gateway.PolygonId);
        drain.U16(gateway.Flags);
        drain.Idx32(gateway.PolygonIndex);
    }

    public static PackedGfxObjContactAsset DeserializeGfxObjRef(ReadOnlySpan<byte> octets, CancellationToken abortTicket = default)
    {
        Cursor cur = new Cursor(octets, abortTicket);
        cur.Header(Kind.GfxObj);
        var bsp = cur.KineticBsp();
        PackedContactSphere? bounding = cur.Tag() ? cur.Sphere() : null;
        PackedGfxObjVisualExtent? visual = null;
        if (cur.Tag())
            visual = new PackedGfxObjVisualExtent(cur.Vec(), cur.Vec(), cur.Vec(), cur.F32(), cur.Vec());
        cur.Finish();
        return new PackedGfxObjContactAsset(bsp, bounding, visual);
    }

    public static PackedSetupContact DeserializeRig(ReadOnlySpan<byte> octets, CancellationToken abortTicket = default)
    {
        Cursor cur = new Cursor(octets, abortTicket);
        cur.Header(Kind.Setup);
        int cylinderTally = cur.Count("Setup cylinder", Width.Cylinder);
        int orbTally = cur.Count("Setup sphere", Width.Sphere);
        cur.Earmark(checked(16L + Width.Cylinder * (long)cylinderTally + Width.Sphere * (long)orbTally), "Setup collision rows");
        float height = cur.F32();
        float radius = cur.F32();
        float hopUp = cur.F32();
        float hopDown = cur.F32();

        var cylinders = cur.Ranks(cylinderTally,
            static (ref Cursor c) => new PackedContactCylinder(c.Vec(), c.F32(), c.F32()));
        var orbs = cur.Ranks(orbTally,
            static (ref Cursor c) => c.Sphere());

        cur.Finish();
        return new PackedSetupContact(cylinders, orbs, height, radius, hopUp, hopDown);
    }

    public static PackedCellStructContactAsset DeserializeChamberStructure(ReadOnlySpan<byte> octets, CancellationToken abortTicket = default)
    {
        Cursor cur = new Cursor(octets, abortTicket);
        cur.Header(Kind.CellStructure);
        var kinetic = cur.KineticBsp();
        var containment = cur.ContainmentBsp();
        var gateways = cur.Polygons();
        cur.Finish();
        return new PackedCellStructContactAsset(kinetic, containment, gateways);
    }

    public static PackedEnvCellTopology DeserializeEnvironChamberWiring(ReadOnlySpan<byte> octets, CancellationToken abortTicket = default)
    {
        Cursor cur = new Cursor(octets, abortTicket);
        cur.Header(Kind.EnvCellTopology);
        int gatewayTally = cur.Count("EnvCell portal", Width.Portal);
        int shownTally = cur.Count("visible-cell", Width.CellId);
        cur.Earmark(checked(1L + Width.Portal * (long)gatewayTally + Width.CellId * (long)shownTally), "EnvCell topology rows");
        bool observedBeyond = cur.Tag();

        var gateways = cur.Ranks(gatewayTally,
            static (ref Cursor c) => new PackedEnvCellPortal(c.U16(), c.U16(), c.U16(), c.I32()));
        var shown = cur.Ranks(shownTally, static (ref Cursor c) => c.U32());

        cur.Finish();
        return new PackedEnvCellTopology(gateways, shown, observedBeyond);
    }
}
