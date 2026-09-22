using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>A cell's collision data flattened for JSON fixtures and diagnostics.</summary>
public sealed record CellSnapshot(
    uint CellId,
    Matrix4x4Wire WorldTransform,
    Matrix4x4Wire InverseWorldTransform,
    IReadOnlyList<PolygonSnapshot> ResolvedPolygons,
    IReadOnlyList<PolygonSnapshot> PortalPolygons,
    IReadOnlyList<PortalSnapshot> Portals,
    IReadOnlyList<uint> VisibleCellIds);

public sealed record PolygonSnapshot(
    ushort Id,
    int NumPoints,
    int SidesType,
    PlaneWire Plane,
    IReadOnlyList<Vector3Wire> Vertices);

public sealed record PortalSnapshot(ushort OtherCellId, ushort PolygonId, ushort Flags);

public sealed record Vector3Wire(float X, float Y, float Z)
{
    public static Vector3Wire From(Vector3 v) => new(v.X, v.Y, v.Z);
    public Vector3 ToVector3() => new(X, Y, Z);
}

public sealed record PlaneWire(Vector3Wire Normal, float D)
{
    public static PlaneWire From(Plane p) => new(Vector3Wire.From(p.Normal), p.D);
    public Plane ToPlane() => new(Normal.ToVector3(), D);
}

public sealed record Matrix4x4Wire(
    float M11, float M12, float M13, float M14,
    float M21, float M22, float M23, float M24,
    float M31, float M32, float M33, float M34,
    float M41, float M42, float M43, float M44)
{
    public static Matrix4x4Wire From(Matrix4x4 m)
    {
        return new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
    }

    public Matrix4x4 ToMatrix()
    {
        return new(
        M11, M12, M13, M14,
        M21, M22, M23, M24,
        M31, M32, M33, M34,
        M41, M42, M43, M44);
    }
}

public static class CellSnapshotWriter
{
    private static readonly JsonSerializerOptions Writing = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static readonly JsonSerializerOptions Reading = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static CellSnapshot Capture(uint chamberIdent, CellKinetics chamber)
    {
        var settled = new List<PolygonSnapshot>(chamber.Resolved.Count);
        foreach ((ushort ident, SettledPolygon polyg) in chamber.Resolved)
            settled.Add(Flatten(ident, polyg));

        var gatewayPolygs = new List<PolygonSnapshot>();
        if (chamber.PortalPolygons is { } throughGateways)
        {
            foreach ((ushort ident, SettledPolygon polyg) in throughGateways)
                gatewayPolygs.Add(Flatten(ident, polyg));
        }

        List<PortalSnapshot> gateways = new List<PortalSnapshot>(chamber.Portals.Count);
        foreach (PortalFacts gateway in chamber.Portals)
            gateways.Add(new PortalSnapshot(gateway.OtherCellId, gateway.PolygonId, gateway.Flags));

        return new CellSnapshot(
            CellId: chamberIdent,
            WorldTransform: Matrix4x4Wire.From(chamber.WorldTransform),
            InverseWorldTransform: Matrix4x4Wire.From(chamber.InverseWorldTransform),
            ResolvedPolygons: settled,
            PortalPolygons: gatewayPolygs,
            Portals: gateways,
            VisibleCellIds: new List<uint>(chamber.VisibleCellIds));
    }

    public static void Write(CellSnapshot print, string fileTrail)
    {
        string? direction = Path.GetDirectoryName(fileTrail);
        if (!string.IsNullOrEmpty(direction) && !Directory.Exists(direction))
            Directory.CreateDirectory(direction);

        using FileStream flow = File.Create(fileTrail);
        JsonSerializer.Serialize(flow, print, Writing);
    }

    public static CellSnapshot Read(string fileTrail)
    {
        using FileStream flow = File.OpenRead(fileTrail);
        return JsonSerializer.Deserialize<CellSnapshot>(flow, Reading)
            ?? throw new InvalidDataException($"Cell dump deserialized to null: {fileTrail}");
    }

    public static CellKinetics Hydrate(CellSnapshot print)
    {
        var settled = new Dictionary<ushort, SettledPolygon>(print.ResolvedPolygons.Count);
        foreach (PolygonSnapshot snapshot in print.ResolvedPolygons)
            settled[snapshot.Id] = Thaw(snapshot);

        var gatewayPolygs = new Dictionary<ushort, SettledPolygon>(print.PortalPolygons.Count);
        foreach (PolygonSnapshot snapshot in print.PortalPolygons)
            gatewayPolygs[snapshot.Id] = Thaw(snapshot);

        List<PortalFacts> gateways = new List<PortalFacts>(print.Portals.Count);
        foreach (PortalSnapshot snapshot in print.Portals)
            gateways.Add(new PortalFacts(snapshot.OtherCellId, snapshot.PolygonId, snapshot.Flags));

        return new CellKinetics
        {
            BSP = null,
            PhysicsPolygons = null,
            Vertices = null,
            WorldTransform = print.WorldTransform.ToMatrix(),
            InverseWorldTransform = print.InverseWorldTransform.ToMatrix(),
            Resolved = settled,
            CellBSP = null,
            Portals = gateways,
            PortalPolygons = gatewayPolygs.Count is 0 ? null : gatewayPolygs,
            VisibleCellIds = new HashSet<uint>(print.VisibleCellIds),
        };
    }

    internal static PolygonSnapshot Flatten(ushort ident, SettledPolygon polyg)
    {
        List<Vector3Wire> corners = new List<Vector3Wire>(polyg.Vertices.Length);
        foreach (Vector3 v in polyg.Vertices)
            corners.Add(Vector3Wire.From(v));

        return new PolygonSnapshot(
            Id: ident,
            NumPoints: polyg.NumPoints,
            SidesType: (int)polyg.SidesType,
            Plane: PlaneWire.From(polyg.Plane),
            Vertices: corners);
    }

    internal static SettledPolygon Thaw(PolygonSnapshot snapshot)
    {
        Vector3[] corners = new Vector3[snapshot.Vertices.Count];
        for (int idx = 0; idx < corners.Length; ++idx)
            corners[idx] = snapshot.Vertices[idx].ToVector3();

        return new SettledPolygon
        {
            Vertices = corners,
            Plane = snapshot.Plane.ToPlane(),
            NumPoints = snapshot.NumPoints,
            SidesType = (FaceCulling)snapshot.SidesType,
            Id = snapshot.Id,
        };
    }
}
