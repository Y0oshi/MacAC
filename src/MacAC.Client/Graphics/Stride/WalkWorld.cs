using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public sealed class StridePolygon
{
    public Vector3[] Vertices = [];
    public StridePlane Plane;
}

public struct StrideCellPortal
{
    public uint OtherCellId;
    public int PolygIdx;
    public int PortalSide;
    public int AnotherGatewayTag;
    public bool PreciseMatch;
}

public sealed class StrollChamber
{
    public uint CellId;
    public StrideCellPortal[] Portals = [];
    public StridePolygon[] PortalPolygons = [];
    public uint[] StabList = [];

    public Matrix4x4 WorldTransform = Matrix4x4.Identity;
    public Matrix4x4 InverseWorldTransform = Matrix4x4.Identity;

    public int CountLens;
    public readonly List<StridePortalView> GatewayViews = [];
    public StrollChamber?[] StashedNeighbors = [];

    public StridePortalView TopLens => GatewayViews[CountLens - 1];

    public void PushLens()
    {
        while (GatewayViews.Count <= CountLens)
            GatewayViews.Add(new StridePortalView());
        GatewayViews[CountLens].RestartForPush();
        ++CountLens;
        if (StashedNeighbors.Length < Portals.Length)
            StashedNeighbors = new StrollChamber?[Portals.Length];
    }

    public void TakeLens() => CountLens--;
}

public interface IStrideFrameScope
{
    Vector3 ViewpointIn(StrollChamber chamber);

    Matrix4x4 ObjectToClip(StrollChamber chamber);

    StrollChamber? FetchShown(uint chamberIdent);

    IStrideRayCaster Rays { get; }
    Vector3 RealmViewpoint { get; }
    float ViewRectWidth { get; }
    float ViewRectHeight { get; }

    bool ClipScenery => true;
}
