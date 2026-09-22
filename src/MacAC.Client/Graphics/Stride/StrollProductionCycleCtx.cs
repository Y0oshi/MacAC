using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public sealed class StrollProductionCycleCtx : IStrideFrameScope, ICanonFrameStrideScope
{
    public const float ZNearby = 0.1f;

    private sealed class InverseViewMirrorRayCaster : IStrideRayCaster
    {
        private Matrix4x4 _invLensProj;
        private float _viewRectWidth;
        private float _viewRectHeight;

        public InverseViewMirrorRayCaster(
            Matrix4x4 lensProj, float viewRectWidth, float viewRectHeight)
            => Reset(lensProj, viewRectWidth, viewRectHeight);

        internal void Reset(
            Matrix4x4 viewProjection, float viewRectWidth, float viewRectHeight)
        {
            if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 invLensProj))
            {
                throw new ArgumentException(
                    "The walk's view-projection matrix has to be invertible",
                    nameof(viewProjection));
            }
            _invLensProj = invLensProj;
            _viewRectWidth = viewRectWidth;
            _viewRectHeight = viewRectHeight;
        }

        public Vector3 RayThrough(float monitorX, float monitorY)
        {
            float ndcX = monitorX / _viewRectWidth * 2f - 1f;
            float ndcY = 1f - monitorY / _viewRectHeight * 2f;
            Vector4 nearby = Vector4.Transform(
                new Vector4(ndcX, ndcY, 0f, 1f), _invLensProj);
            Vector4 faraway = Vector4.Transform(
                new Vector4(ndcX, ndcY, 1f, 1f), _invLensProj);
            Vector3 nearbyRealm = new Vector3(nearby.X, nearby.Y, nearby.Z) / nearby.W;
            Vector3 farawayRealm = new Vector3(faraway.X, faraway.Y, faraway.Z) / faraway.W;
            return farawayRealm - nearbyRealm;
        }
    }

    private readonly ChamberVis _chambers;
    private readonly StrollStructureRegistry _structures;
    private Matrix4x4 _lensProj;
    private readonly InverseViewMirrorRayCaster _rays;

    private Vector2[] _engagedLensVerts = new Vector2[32];
    private int _engagedLensVertTally;

    public StrollProductionCycleCtx(
        ChamberVis cells,
        StrollStructureRegistry buildings,
        Vector3 realmViewpoint,
        Vector3 ahead,
        Matrix4x4 lensProj,
        float viewRectWidth,
        float viewRectHeight,
        uint beholderChamberIdent = 0u,
        bool weatherLatchOpen = false,
        bool structureDegradesDisabled = false)
    {
        _chambers = cells ?? throw new ArgumentNullException(nameof(cells));
        _structures = buildings ?? throw new ArgumentNullException(nameof(buildings));
        _rays = new InverseViewMirrorRayCaster(
            lensProj, viewRectWidth, viewRectHeight);
        Reset(
            realmViewpoint, ahead, lensProj, viewRectWidth, viewRectHeight,
            beholderChamberIdent, weatherLatchOpen, structureDegradesDisabled);
    }

    public Vector3 ViewpointIn(StrollChamber chamber)
        => Vector3.Transform(RealmViewpoint, chamber.InverseWorldTransform);

    public Vector3 RealmViewpoint { get; private set; }
    public float ViewRectWidth { get; private set; }
    public float ViewRectHeight { get; private set; }
    public uint BeholderCellId { get; private set; }
    public bool WeatherLatchOpen { get; private set; }
    public bool BuildingDegradesDisabled { get; private set; }
    public StridePlane CyPlane { get; private set; }
    public IStrideRayCaster Rays => _rays;
    public IStrideFrameScope ChamberCtx => this;

    public Matrix4x4 ObjectToClip(StrollChamber chamber) => chamber.WorldTransform * _lensProj;

    public StrollChamber? FetchShown(uint chamberIdent)
    {
        return _chambers.TryFetchChamber(chamberIdent, out FetchedChamber? chamber) ? chamber?.Stroll : null;
    }

    public void AssignEngagedLens(StridePortalView views, int ordinal)
    {
        var poly = views.View.Polys[ordinal];
        if (_engagedLensVerts.Length < poly.VertexCount)
            _engagedLensVerts = new Vector2[poly.VertexCount];
        for (int kdx = 0; kdx < poly.VertexCount; ++kdx)
            _engagedLensVerts[kdx] = views.View.Vertices[poly.VertexIndex + kdx].Point;
        _engagedLensVertTally = poly.VertexCount;
    }

    public Vector3 ViewpointInStructure(StrollStructure structure)
    {
        return Vector3.Transform(RealmViewpoint, FetchListing(structure).InvPieceZeroRealmXform);
    }

    public float BeholderGapTo(StrollStructure structure)
    {
        var listing = FetchListing(structure);
        float gap = Vector3.Distance(
            RealmViewpoint,
            Vector3.Transform(structure.SortCenter, listing.PieceZeroRealmXform));
        return gap / structure.PieceZeroScalingZ;
    }

    public int ClipStructurePolyg(
        StrollStructure structure, StridePolygon polyg, int flank, Span<StrideScreenPoint> product)
    {
        Matrix4x4 objectToClip =
            FetchListing(structure).PieceZeroRealmXform * _lensProj;
        Span<StrideScreenPoint> projected = stackalloc StrideScreenPoint[polyg.Vertices.Length];
        for (int idx = 0; idx < polyg.Vertices.Length; ++idx)
        {
            projected[idx] = StrideScreenClip.ConvertToMonitor(
                polyg.Vertices[idx], objectToClip, ViewRectWidth, ViewRectHeight);
        }
        if (flank is not 0)
            projected.Reverse();
        return StrideScreenClip.ClipAgainstLens(
            projected, _engagedLensVerts.AsSpan(0, _engagedLensVertTally), product);
    }

    internal void Reset(
        Vector3 realmViewpoint,
        Vector3 ahead,
        Matrix4x4 lensProj,
        float viewRectWidth,
        float viewRectHeight,
        uint beholderChamberIdent = 0u,
        bool weatherLatchOpen = false,
        bool structureDegradesDisabled = false)
    {
        _rays.Reset(lensProj, viewRectWidth, viewRectHeight);
        RealmViewpoint = realmViewpoint;
        _lensProj = lensProj;
        ViewRectWidth = viewRectWidth;
        ViewRectHeight = viewRectHeight;
        BeholderCellId = beholderChamberIdent;
        WeatherLatchOpen = weatherLatchOpen;
        BuildingDegradesDisabled = structureDegradesDisabled;
        _engagedLensVertTally = 0;
        CyPlane = new StridePlane(ahead, -Vector3.Dot(realmViewpoint, ahead) - ZNearby);
    }

    private StrideBuildingMint.Entry FetchListing(StrollStructure structure)
    {
        return !_structures.TryFetchListing(structure, out var listing)
            ? throw new InvalidOperationException(
                "StrollProductionCycleCtx was asked to place a StrollStructure " +
                "that isn't committed in its WalkBuildingRegistry")
            : listing;
    }
}
