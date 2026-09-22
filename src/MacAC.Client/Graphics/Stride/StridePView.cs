using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public sealed class StridePView
{
    internal static int MasterStampForTelemetry { get; private set; }

    private readonly struct TodoListing(StrollChamber chamber, float distance)
    {
        public readonly StrollChamber Cell = chamber;
        public readonly float Distance = distance;
    }

    public readonly StridePortalView BeyondLens = new();
    public readonly List<StrollChamber> ChamberPaintRoster = [];
    private readonly List<TodoListing> _todo = [];
    private readonly StridePortalView _anotherGatewayTemp = new();
    private Vector2[] _engagedLensVerts = new Vector2[32];
    private int _engagedLensVertTally;
    private readonly StrideScreenPoint[] _projectTemp = new StrideScreenPoint[64];
    private readonly StrideScreenPoint[] _clipTemp = new StrideScreenPoint[64];

    public bool PaintLandscape = true;

    public StridePortalView? GatewayRoster { get; private set; }

    public void FabricateLens(StrollChamber seed, int throughGatewayOrdinal, IStrideFrameScope cx)
    {
        BeyondLens.RestartForPush();
        ++MasterStampForTelemetry;
        _todo.Clear();
        ChamberPaintRoster.Clear();
        PrimeChamber(seed, throughGatewayOrdinal, cx);
        InsChamberTodoRoster(seed, 0f);
        while (_todo.Count > 0)
        {
            StrollChamber chamber = _todo[^1].Cell;
            _todo.RemoveAt(_todo.Count - 1);
            ChamberPaintRoster.Add(chamber);
            chamber.TopLens.ChamberLensDone = true;
            if (ClipPortals(chamber, 0, cx))
                AppendLensToGateways(chamber, cx);
        }
    }

    public bool PrimeChamber(StrollChamber chamber, int listingGatewayOrdinal, IStrideFrameScope cx)
    {
        var top = chamber.TopLens;
        if (top.ViewCount is 0) return false;

        Vector3 viewpoint = cx.ViewpointIn(chamber);
        top.ChamberLensDone = false;
        top.LensStamp = MasterStampForTelemetry;
        if (top.GatewayFlagSet.Length < chamber.Portals.Length)
            top.GatewayFlagSet = new StridePortalFlags[chamber.Portals.Length];

        float upperDistanceSquared = 0f;
        bool anyGazeThrough = false;

        for (int idx = 0; idx < chamber.Portals.Length; ++idx)
        {
            ref StridePortalFlags flagSet = ref top.GatewayFlagSet[idx];
            var poly = chamber.PortalPolygons[chamber.Portals[idx].PolygIdx];

            if (idx == listingGatewayOrdinal && !flagSet.InView)
            {
                // Entered-through portal: forced facing + armed - never re-traversed
                flagSet.InView = true;
                flagSet.Observed = true;
            }
            else
            {
                flagSet.Observed = false;
                float d = Vector3.Dot(poly.Plane.Normal, viewpoint) + poly.Plane.D;
                if (d is <= StrideVisibilityMath.Epsilon and >= -StrideVisibilityMath.Epsilon)
                {
                    flagSet.InView = false;      // IN_PLANE: neither surface nor opening
                    anyGazeThrough = true;
                }
                else
                {
                    int flank = d > StrideVisibilityMath.Epsilon ? 0 : 1;
                    if (flank == chamber.Portals[idx].PortalSide)
                    {
                        flagSet.InView = false;  // viewer on the look-through side (opening)
                        anyGazeThrough = true;
                    }
                    else
                    {
                        flagSet.InView = true;   // portal polygon faces the viewer
                    }
                }
            }

            if (flagSet.InView)
            {
                foreach (Vector3 v in poly.Vertices)
                {
                    float dx = viewpoint.X - v.X;
                    float dy = viewpoint.Y - v.Y;
                    float dz = viewpoint.Z - v.Z;
                    float d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 > upperDistanceSquared) upperDistanceSquared = d2;
                }
            }
        }
        top.UpperInDistanceSquared = upperDistanceSquared;

        if (anyGazeThrough && top.ViewCount > 0)
        {
            for (int jdx = 0; jdx < chamber.Portals.Length; ++jdx)
            {
                ref StridePortalFlags flagSet = ref top.GatewayFlagSet[jdx];
                if (!flagSet.InView && !flagSet.Observed) flagSet.Observed = true;
            }
        }

        top.RefreshTally = top.ViewCount;
        return true;
    }

    public void InsChamberTodoRoster(StrollChamber chamber, float distance)
    {
        int spot = _todo.Count;
        while (spot > 0 && !(distance < _todo[spot - 1].Distance))
            --spot;
        _todo.Insert(spot, new TodoListing(chamber, distance));
    }

    public bool ClipPortals(StrollChamber chamber, int beginLens, IStrideFrameScope cx)
    {
        var top = chamber.TopLens;
        GatewayRoster = top;
        if (chamber.Portals.Length <= 0) return false;

        bool anyOnline = false;
        for (int jdx = 0; jdx < chamber.Portals.Length; ++jdx)
        {
            ref StridePortalFlags flagSet = ref top.GatewayFlagSet[jdx];
            ref StrideCellPortal gateway = ref chamber.Portals[jdx];
            if (!flagSet.Observed || flagSet.InView)

                continue;
            if (chamber.StashedNeighbors[jdx] is null && gateway.OtherCellId != 0xFFFFFFFFu)
            {
                chamber.StashedNeighbors[jdx] = cx.FetchShown(gateway.OtherCellId);
                if (chamber.StashedNeighbors[jdx] is null)
                {
                    continue;   // not loaded: silently dead
                }
            }
            anyOnline = true;
        }
        if (!anyOnline) return false;

        for (int idx = beginLens; idx < top.ViewCount; ++idx)
        {
            AssignLens(top, idx);
            for (int jdx = 0; jdx < chamber.Portals.Length; ++jdx)
            {
                ref StridePortalFlags flagSet = ref top.GatewayFlagSet[jdx];
                if (!flagSet.Observed || flagSet.InView) continue;
                ref StrideCellPortal gateway = ref chamber.Portals[jdx];
                int num = FetchClip(
                    chamber, gateway.PortalSide,
                    chamber.PortalPolygons[gateway.PolygIdx],
                    doClip: true, cx, _clipTemp);
                if (num is 0)

                    continue;

                if (gateway.OtherCellId == 0xFFFFFFFFu)
                {
                    if (PaintLandscape)
                    {
                        if (cx.ClipScenery)
                            StrideCopyView.Append(
                                BeyondLens, _clipTemp.AsSpan(0, num),
                                cx.Rays, cx.RealmViewpoint);
                        else
                            StrideCopyView.AffixWholeViewRectQuad(
                                BeyondLens, cx.Rays, cx.RealmViewpoint,
                                cx.ViewRectWidth, cx.ViewRectHeight);
                    }
                }
                else if (chamber.StashedNeighbors[jdx] is StrollChamber neighbor)
                {
                    if (!gateway.PreciseMatch && gateway.AnotherGatewayTag >= 0)
                    {
                        num = OtherPortalClip(chamber, jdx, num, cx);
                        AssignLens(top, idx);   // restore after the far-frame excursion
                        if (num is 0)

                            continue;
                    }
                    if (neighbor.CountLens is not 0)
                        StrideCopyView.Append(
                            neighbor.TopLens, _clipTemp.AsSpan(0, num),
                            cx.Rays, cx.RealmViewpoint);
                }
            }
        }
        return true;
    }

    public void AppendLensToGateways(StrollChamber chamber, IStrideFrameScope cx)
    {
        for (int jdx = 0; jdx < chamber.Portals.Length; ++jdx)
        {
            ref StrideCellPortal gateway = ref chamber.Portals[jdx];
            StrollChamber? neighbor = chamber.StashedNeighbors[jdx];
            ref StridePortalFlags flagSet = ref chamber.TopLens.GatewayFlagSet[jdx];
            if (neighbor is null || flagSet.InView || !flagSet.Observed || neighbor.CountLens is 0)
                continue;
            var neighborTop = neighbor.TopLens;
            if (neighborTop.ViewCount is 0) continue;

            if (neighborTop.RefreshTally is 0)
            {
                // First touch this flood: schedule the neighbor
                if (PrimeChamber(neighbor, ToListingOrdinal(gateway.AnotherGatewayTag), cx))
                    InsChamberTodoRoster(neighbor, neighborTop.UpperInDistanceSquared);
            }
            else if (neighborTop.RefreshTally != neighborTop.ViewCount)
            {
                AppendToChamber(neighbor, ToListingOrdinal(gateway.AnotherGatewayTag));
                if (neighborTop.ChamberLensDone)
                    RepairChamberRoster(neighbor, chamber, cx);
                neighborTop.RefreshTally = neighborTop.ViewCount;   // fresh re-read after recursion
            }
            else
            {
                continue;   // nothing new; NO SetOtherSeen either
            }

            if (gateway.AnotherGatewayTag >= 0)     // full-width signed test (−1 sentinel skips)
                AssignAnotherObserved(chamber, jdx);
        }
    }

    public void AppendToChamber(StrollChamber chamber, int listingGatewayOrdinal)
    {
        var top = chamber.TopLens;
        for (int idx = top.RefreshTally; idx < top.ViewCount; ++idx)
        {
            for (int jdx = 0; jdx < chamber.Portals.Length; ++jdx)
            {
                ref StridePortalFlags flagSet = ref top.GatewayFlagSet[jdx];
                if (jdx == listingGatewayOrdinal && !flagSet.InView) flagSet.InView = true;
                if (!flagSet.InView && !flagSet.Observed) flagSet.Observed = true;
            }
        }
    }

    public void AssignAnotherObserved(StrollChamber chamber, int gatewayOrdinal)
    {
        StrollChamber? neighbor = chamber.StashedNeighbors[gatewayOrdinal];
        if (neighbor is null) return;
        int backOrdinal = chamber.Portals[gatewayOrdinal].AnotherGatewayTag;
        ref StrideCellPortal backGateway = ref neighbor.Portals[backOrdinal];
        if (neighbor.StashedNeighbors[backOrdinal] is null)
            neighbor.StashedNeighbors[backOrdinal] = chamber;
        ref StridePortalFlags backFlagSet = ref neighbor.TopLens.GatewayFlagSet[backOrdinal];
        if (backFlagSet.InView) backFlagSet.Observed = true;
    }

    public void RepairChamberRoster(StrollChamber moved, StrollChamber reachedThrough, IStrideFrameScope cx)
    {
        TuneChamberPlace(moved, reachedThrough);
        TuneChamberLens(moved, cx);
    }

    public void AssignLens(StridePortalView gatewayLens, int polyOrdinal)
    {
        var poly = gatewayLens.View.Polys[polyOrdinal];
        if (_engagedLensVerts.Length < poly.VertexCount)
            _engagedLensVerts = new Vector2[poly.VertexCount];
        for (int kdx = 0; kdx < poly.VertexCount; ++kdx)
            _engagedLensVerts[kdx] = gatewayLens.View.Vertices[poly.VertexIndex + kdx].Point;
        _engagedLensVertTally = poly.VertexCount;
    }

    public int FetchClip(
        StrollChamber chamber, int flank, StridePolygon polyg, bool doClip,
        IStrideFrameScope cx, Span<StrideScreenPoint> product)
    {
        int num = polyg.Vertices.Length;
        Matrix4x4 objectToClip = cx.ObjectToClip(chamber);
        for (int idx = 0; idx < num; ++idx)
        {
            _projectTemp[idx] = StrideScreenClip.ConvertToMonitor(
                polyg.Vertices[idx], objectToClip, cx.ViewRectWidth, cx.ViewRectHeight);
        }
        if (flank is not 0)
        {
            for (int idx = 0; idx < num / 2; ++idx)
                (_projectTemp[idx], _projectTemp[num - 1 - idx])
                    = (_projectTemp[num - 1 - idx], _projectTemp[idx]);
        }
        if (!doClip)
        {
            _projectTemp.AsSpan(0, num).CopyTo(product);
            return num;
        }
        return StrideScreenClip.ClipAgainstLens(
            _projectTemp.AsSpan(0, num),
            _engagedLensVerts.AsSpan(0, _engagedLensVertTally),
            product);
    }

    private int OtherPortalClip(
        StrollChamber chamber, int gatewayOrdinal, int tally, IStrideFrameScope cx)
    {
        _anotherGatewayTemp.RestartForPush();
        if (!StrideCopyView.Append(
                _anotherGatewayTemp, _clipTemp.AsSpan(0, tally),
                cx.Rays, cx.RealmViewpoint))
            return 0;
        ref StrideCellPortal gateway = ref chamber.Portals[gatewayOrdinal];
        StrollChamber faraway = chamber.StashedNeighbors[gatewayOrdinal]
            ?? throw new InvalidOperationException(
                "OtherPortalClip needs a resolved neighbor (ClipPortals pass 1 contract)");
        ref StrideCellPortal farawayGateway = ref faraway.Portals[gateway.AnotherGatewayTag];
        AssignLens(_anotherGatewayTemp, 0);
        return FetchClip(
            faraway, farawayGateway.PortalSide is 0 ? 1 : 0,
            faraway.PortalPolygons[farawayGateway.PolygIdx],
            doClip: true, cx, _clipTemp);
    }

    private static int ToListingOrdinal(int anotherGatewayIdent)
        => anotherGatewayIdent < 0 ? 0xFFFF : anotherGatewayIdent;

    private void TuneChamberPlace(StrollChamber moved, StrollChamber reachedThrough)
    {
        var top = reachedThrough.TopLens;
        if (!TunePaintRoster(moved, reachedThrough)) return;
        for (int idx = 0; idx < reachedThrough.Portals.Length; ++idx)
        {
            ref StridePortalFlags flagSet = ref top.GatewayFlagSet[idx];
            if (flagSet.Observed && flagSet.InView && reachedThrough.StashedNeighbors[idx] is StrollChamber upcoming)
                TuneChamberPlace(reachedThrough, upcoming);
        }
    }

    private bool TunePaintRoster(StrollChamber moved, StrollChamber reachedThrough)
    {
        for (int idx = 0; idx < ChamberPaintRoster.Count; ++idx)
        {
            uint ident = ChamberPaintRoster[idx].CellId;
            if (ident == reachedThrough.CellId) break;     // already earlier: nothing to do
            if (ident != moved.CellId) continue;

            int at = idx;
            while (at < ChamberPaintRoster.Count && ChamberPaintRoster[at].CellId != reachedThrough.CellId)
                ++at;
            if (at == ChamberPaintRoster.Count)
                ChamberPaintRoster.Add(reachedThrough);
            for (int kdx = at; kdx > idx; --kdx)
                ChamberPaintRoster[kdx] = ChamberPaintRoster[kdx - 1];
            ChamberPaintRoster[idx] = reachedThrough;
            return true;
        }
        return false;
    }

    private void TuneChamberLens(StrollChamber chamber, IStrideFrameScope cx)
    {
        if (ClipPortals(chamber, chamber.TopLens.RefreshTally, cx))
            AppendLensToGateways(chamber, cx);
    }
}
