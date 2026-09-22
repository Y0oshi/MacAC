using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public struct StrideBldPortal
{
    public int PortalSide;
    public uint OtherCellId;
    public int AnotherGatewayIdent;
    public bool PreciseFit;
    public uint[] StabRoster;
}

public struct StridePortalRef
{
    public int GatewayOrdinal;
    public StridePolygon Polygon;
}

public sealed class StrideBspNode
{
    public StridePlane SplittingPlane;
    public StrideBspNode? PosNode;
    public StrideBspNode? NegNode;
    public bool IsFail;
    public StridePortalRef[]? InGateways;   // non-null = PORT node

    public bool IsGateway => InGateways is not null;
}

public readonly record struct StrideBuildingDegradeLevel(
    uint GfxObjId,
    uint Mode,
    float MinDist,
    float IdealDist,
    float MaxDist,
    StrideBspNode? DrawingBsp);

public readonly record struct StrideBuildingPicking(
    uint GfxObjId,
    StrideBspNode? DrawingBsp,
    int Level,
    uint Mode);

public sealed class StrollStructure
{
    public uint LocusChamberIdent;

    public StrideBldPortal[] Portals = [];

    public StrideBspNode? DrawingBsp;

    public uint GfxObjId;

    public StrideBuildingDegradeLevel[] DowngradeTiers = [];

    public Vector3 SortCenter;

    /// <summary>Part-zero Setup resting/default-scale transform. Direct Gfx buildings use identity.</summary>
    public Matrix4x4 PieceZeroXform = Matrix4x4.Identity;

    public float PieceZeroScalingZ = 1f;

    public StrideBuildingPicking Select(
        float beholderGap,
        float downgradeGap,
        float downgradeMultiplier,
        bool degradesDisabled = false,
        int forcedTier = -1)
    {
        if (DowngradeTiers.Length is 0)
            return new StrideBuildingPicking(GfxObjId, DrawingBsp, 0, 1u);
        if (degradesDisabled)
            return PickAt(0);
        if (forcedTier >= 0)
            return PickAt(Math.Min(forcedTier, DowngradeTiers.Length - 1));

        float net = MathF.Max(0f, MathF.Abs(beholderGap) - downgradeGap);
        for (int idx = 0; idx < DowngradeTiers.Length; ++idx)
        {
            var tier = DowngradeTiers[idx];
            double threshold = downgradeMultiplier >= 0f
                ? (double)tier.IdealDist
                    - ((double)tier.IdealDist - tier.MaxDist) * downgradeMultiplier
                : (double)tier.IdealDist
                    + ((double)tier.IdealDist - tier.MinDist) * downgradeMultiplier;
            if (net < threshold)
                return new StrideBuildingPicking(
                    tier.GfxObjId, tier.DrawingBsp, idx, tier.Mode);
        }
        return PickAt(DowngradeTiers.Length - 1);

        StrideBuildingPicking PickAt(int ordinal)
        {
            var tier = DowngradeTiers[ordinal];
            return new StrideBuildingPicking(
                tier.GfxObjId, tier.DrawingBsp, ordinal, tier.Mode);
        }
    }
}

public static class StrideBuildingPortals
{
    public interface IStridePortalPassSink
    {
        /// <summary>Pass 1 drew the portal polygon as a far-Z punch (<c>DrawPortalPolyInternal(poly, 1)</c>).</summary>
        void OnPunch(StridePolygon polyg);

        void OnPaintChambers(StridePView pview);
    }

    public static void AssemblePaintGatewaysSole(
        StrideBspNode? trunk, int pass, Vector3 viewpointInStructure,
        Action<StridePortalRef, int> emitGateway)
    {
        if (trunk is null || trunk.IsFail) return;
        Traverse(trunk, pass, viewpointInStructure, emitGateway);
    }

    public static bool PaintGateway(
        StridePView pview, StrollStructure structure, in StridePortalRef gatewayRef,
        int pass, IStrideBuildingFrameScope cx, IStridePortalPassSink drain)
    {
        ref readonly StrideBldPortal bldGateway = ref structure.Portals[gatewayRef.GatewayOrdinal];
        AppendViews(bldGateway.StabRoster, cx);
        bool ok = FabricateStructureLens(
            pview, structure, in bldGateway, gatewayRef.Polygon, pass, cx, drain);
        if (ok && pass is not 1)
            drain.OnPaintChambers(pview);
        DropViews(bldGateway.StabRoster, cx);
        return ok;
    }

    public static bool FabricateStructureLens(
        StridePView pview, StrollStructure structure, in StrideBldPortal bldGateway,
        StridePolygon polyg, int pass, IStrideBuildingFrameScope cx,
        IStridePortalPassSink drain)
    {
        Vector3 viewpoint = cx.ViewpointInStructure(structure);
        float d = Vector3.Dot(polyg.Plane.Normal, viewpoint) + polyg.Plane.D;
        int flank = d > StrideVisibilityMath.Epsilon ? 0
            : d < -StrideVisibilityMath.Epsilon ? 1 : 2;
        if (bldGateway.PortalSide is not 0)
        {
            if (flank is not 1) return false;
        }
        else if (flank is not 0)
        {
            return false;
        }

        Span<StrideScreenPoint> clipped = stackalloc StrideScreenPoint[64];
        int num = cx.ClipStructurePolyg(structure, polyg, flank, clipped);
        if (num is 0) return false;

        StrollChamber? chamber = cx.FetchShown(bldGateway.OtherCellId);
        if (chamber is null) return false;
        if (!StrideCopyView.Append(
                chamber.TopLens, clipped[..num], cx.Rays, cx.RealmViewpoint))
            return false;

        if (pass is not 2)
            drain.OnPunch(polyg);   // DrawPortalPolyInternal(poly, pass == 1)
        if (pass is not 1)
            pview.FabricateLens(chamber, ToListingOrdinal(bldGateway.AnotherGatewayIdent), cx.ChamberCtx);
        return true;
    }

    private static void Traverse(
        StrideBspNode joint, int pass, Vector3 viewpoint,
        Action<StridePortalRef, int> emitGateway)
    {
        while (true)
        {
            float d = Vector3.Dot(joint.SplittingPlane.Normal, viewpoint) + joint.SplittingPlane.D;
            int flank = d > StrideVisibilityMath.Epsilon ? 0
                : d < -StrideVisibilityMath.Epsilon ? 1 : 2;

            StrideBspNode? upcoming;
            if (joint.IsGateway)
            {
                if (flank is 0)
                {
                    Tour(joint.NegNode, pass, viewpoint, emitGateway);
                    foreach (StridePortalRef gateway in joint.InGateways!)
                        emitGateway(gateway, pass);
                    upcoming = joint.PosNode;
                }
                else if (flank is 1)
                {
                    Tour(joint.PosNode, pass, viewpoint, emitGateway);
                    foreach (StridePortalRef gateway in joint.InGateways!)
                        emitGateway(gateway, pass);
                    upcoming = joint.NegNode;
                }
                else
                {
                    Tour(joint.PosNode, pass, viewpoint, emitGateway);
                    upcoming = joint.NegNode;
                }
            }
            else
            {
                if (flank is 0)
                {
                    Tour(joint.NegNode, pass, viewpoint, emitGateway);
                    upcoming = joint.PosNode;
                }
                else
                {
                    Tour(joint.PosNode, pass, viewpoint, emitGateway);
                    upcoming = joint.NegNode;
                }
            }

            if (upcoming is null || upcoming.IsFail) return;
            joint = upcoming;
        }
    }

    private static void Tour(
        StrideBspNode? descendant, int pass, Vector3 viewpoint,
        Action<StridePortalRef, int> emitGateway)
    {
        if (descendant is null || descendant.IsFail) return;
        Traverse(descendant, pass, viewpoint, emitGateway);
    }

    private static int ToListingOrdinal(int anotherGatewayIdent)
        => anotherGatewayIdent < 0 ? 0xFFFF : anotherGatewayIdent;

    private static void AppendViews(uint[] stabRoster, IStrideBuildingFrameScope cx)
    {
        foreach (uint ident in stabRoster)
            cx.FetchShown(ident)?.PushLens();
    }

    private static void DropViews(uint[] stabRoster, IStrideBuildingFrameScope cx)
    {
        foreach (uint ident in stabRoster)
            cx.FetchShown(ident)?.TakeLens();
    }
}

public interface IStrideBuildingFrameScope
{
    Vector3 ViewpointInStructure(StrollStructure structure);

    float BeholderGapTo(StrollStructure structure);

    int ClipStructurePolyg(
        StrollStructure structure, StridePolygon polyg, int flank, Span<StrideScreenPoint> product);

    StrollChamber? FetchShown(uint chamberIdent);
    IStrideRayCaster Rays { get; }
    Vector3 RealmViewpoint { get; }
    IStrideFrameScope ChamberCtx { get; }
}
