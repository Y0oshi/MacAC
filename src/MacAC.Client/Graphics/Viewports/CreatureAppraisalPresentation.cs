using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Realm;
using MacAC.Client.Shell;
using MacAC.Client.Shell.Panels;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface ICreatureAssayPainter
{
    void AssignBeast(
        RealmActor? beast,
        Vector3 limitsLower,
        Vector3 limitsUpper);

    uint Render(int width, int height);
}

internal interface ICreatureAssayFrameView
{
    bool TryFetchShownMark(
        out uint srvOid,
        out int width,
        out int height);

    void ApplyTextureHnd(uint textureHnd);
}

internal interface ICreatureAssayActorLookup
{
    bool TryGet(uint srvOid, out RealmActor actor);
}

internal interface ICreatureAssayCloneMint
{
    bool TrySynchronize(
        uint srvOid,
        RealmActor? latestReplicate,
        out RealmActor? synchronizedReplicate,
        out Vector3 limitsLower,
        out Vector3 limitsUpper);
}

internal sealed class CreatureAssayFrameExhibitor(
    ICreatureAssayPainter renderer,
    ICreatureAssayFrameView view,
    ICreatureAssayCloneMint factory) :
    IPrivateActorViewportFrame
{
    private readonly ICreatureAssayPainter _painter = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly ICreatureAssayFrameView _lens = view ?? throw new ArgumentNullException(nameof(view));
    private readonly ICreatureAssayCloneMint _maker = factory ?? throw new ArgumentNullException(nameof(factory));
    private RealmActor? _replicate;
    private uint _srvOid;

    public void Render()
    {
        if (!_lens.TryFetchShownMark(
                out uint srvOid,
                out int width,
                out int height))

            return;

        if (_srvOid != srvOid)
        {
            _srvOid = srvOid;
            _replicate = null;
        }

        if (!_maker.TrySynchronize(
                srvOid,
                _replicate,
                out RealmActor? synchronized,
                out Vector3 limitsLower,
                out Vector3 limitsUpper))
        {
            _replicate = null;
            _painter.AssignBeast(null, Vector3.Zero, Vector3.Zero);
            _lens.ApplyTextureHnd(0u);
            return;
        }

        _replicate = synchronized;
        _painter.AssignBeast(_replicate, limitsLower, limitsUpper);
        _lens.ApplyTextureHnd(_painter.Render(width, height));
    }
}

internal sealed class CanonCreatureAssayFrameView(
    WidgetViewport viewport,
    WidgetElem windowFrame,
    AssayWidgetDriver controller) :
    ICreatureAssayFrameView
{
    private readonly WidgetViewport _viewRect = viewport ?? throw new ArgumentNullException(nameof(viewport));
    private readonly WidgetElem _paneCycle = windowFrame ?? throw new ArgumentNullException(nameof(windowFrame));
    private readonly AssayWidgetDriver _driver = controller ?? throw new ArgumentNullException(nameof(controller));

    public bool TryFetchShownMark(
        out uint srvOid,
        out int width,
        out int height)
    {
        srvOid = 0u;
        width = 0;
        height = 0;
        if (_driver.EngagedLens is not (
                AssayView.Creature or AssayView.Character))

            return false;
        if (!IsEffectivelyShown(_paneCycle))
            return false;
        if (!IsEffectivelyShown(_viewRect))
            return false;
        if (_driver.LatestObjectIdent is 0u)
            return false;

        srvOid = _driver.LatestObjectIdent;
        width = (int)_viewRect.Width;
        height = (int)_viewRect.Height;
        return width > 0 && height > 0;
    }

    public void ApplyTextureHnd(uint textureHnd) =>
        _viewRect.TextureSlot = WidgetTextureChartHandle.ToSocket(textureHnd);

    private static bool IsEffectivelyShown(WidgetElem elem)
    {
        for (WidgetElem? latest = elem; latest is not null; latest = latest.Ancestor)
            if (!latest.Visible)
                return false;
        return true;
    }
}

internal sealed class OnlineCreatureAssayActorLookup(OnlineActorCore liveEntities) :
    ICreatureAssayActorLookup
{
    private readonly OnlineActorCore _onlineActors = liveEntities
            ?? throw new ArgumentNullException(nameof(liveEntities));

    public bool TryGet(uint srvOid, out RealmActor actor) =>
        _onlineActors.TryFetchRealmActor(srvOid, out actor);
}

internal sealed class CanonCreatureAssayCloneMint(
    ICreatureAssayActorLookup entities) :
    ICreatureAssayCloneMint
{
    private readonly ICreatureAssayActorLookup _actors = entities ?? throw new ArgumentNullException(nameof(entities));

    public bool TrySynchronize(
        uint srvOid,
        RealmActor? latestReplicate,
        out RealmActor? synchronizedReplicate,
        out Vector3 limitsLower,
        out Vector3 limitsUpper)
    {
        synchronizedReplicate = null;
        limitsLower = Vector3.Zero;
        limitsUpper = Vector3.Zero;
        if (!_actors.TryGet(srvOid, out RealmActor src))
            return false;
        if (src.MeshRefs.Count is 0)
            return false;

        RealmActor replicate = latestReplicate is not null
            && latestReplicate.SrcGfxObjRefOrRigIdent == src.SrcGfxObjRefOrRigIdent
                ? latestReplicate
                : CreatureAssayActorAssembler.Build(src);

        replicate.ImposeLooks(
            src.MeshRefs,
            src.SwatchOverride,
            src.PieceSubstitutions);
        replicate.IsPaintShown = src.IsPaintShown;
        replicate.IsAncestorPaintShown = src.IsAncestorPaintShown;
        if (src.HasOwnLimits)
            replicate.AssignOwnLimits(src.OwnTiedLower, src.OwnTiedUpper);

        (limitsLower, limitsUpper) =
            CreatureAssayActorAssembler.RotatedLimits(src);
        synchronizedReplicate = replicate;
        return true;
    }
}

internal static class CreatureAssayActorAssembler
{
    public const uint RasterizeIdent = 0xDA11_D022u;
    public const uint SrvGuid = 0xDA11_D021u;
    private const float BearingDeg = 191.367905f;
    private static readonly Quaternion Bearing = Quaternion.CreateFromAxisAngle(
        Vector3.UnitZ,
        -BearingDeg * (MathF.PI / 180f));

    public static RealmActor Build(RealmActor src)
    {
        ArgumentNullException.ThrowIfNull(src);
        RealmActor replicate = new RealmActor
        {
            Id = RasterizeIdent,
            ServerGuid = SrvGuid,
            SrcGfxObjRefOrRigIdent = src.SrcGfxObjRefOrRigIdent,
            Position = Vector3.Zero,
            Rotation = Bearing,
            MeshRefs = src.MeshRefs,
            SwatchOverride = src.SwatchOverride,
            PieceSubstitutions = src.PieceSubstitutions,
            ConcealedPiecesBitmask = src.ConcealedPiecesBitmask,
            Scale = src.Scale,
            ParentCellId = null,
            FxChamberIdent = null,
        };
        if (src.HasOwnLimits)
            replicate.AssignOwnLimits(src.OwnTiedLower, src.OwnTiedUpper);
        return replicate;
    }

    public static (Vector3 Min, Vector3 Max) RotatedLimits(
        RealmActor src)
    {
        Vector3 lower;
        Vector3 upper;
        if (src.HasOwnLimits)
        {
            lower = src.OwnTiedLower;
            upper = src.OwnTiedUpper;
        }
        else
        {
            lower = new Vector3(-0.5f, -0.5f, 0f);
            upper = new Vector3(0.5f, 0.5f, 2f);
            foreach (TriMeshRef triMesh in src.MeshRefs)
            {
                Vector3 p = triMesh.PartTransform.Translation;
                lower = Vector3.Min(lower, p - new Vector3(0.5f));
                upper = Vector3.Max(upper, p + new Vector3(0.5f));
            }
        }

        Vector3 rotatedLower = default;
        Vector3 rotatedUpper = default;
        for (int corner = 0; corner < 8; ++corner)
        {
            Vector3 pt = new(
                (corner & 1) is 0 ? lower.X : upper.X,
                (corner & 2) is 0 ? lower.Y : upper.Y,
                (corner & 4) is 0 ? lower.Z : upper.Z);
            pt = Vector3.Transform(pt, Bearing);
            if (corner is 0)
                rotatedLower = rotatedUpper = pt;
            else
            {
                rotatedLower = Vector3.Min(rotatedLower, pt);
                rotatedUpper = Vector3.Max(rotatedUpper, pt);
            }
        }
        return (rotatedLower, rotatedUpper);
    }
}

internal sealed class CreatureAssayCamera :
    IPrivateActorViewportCamera
{
    private const float FitGapFactor = 1.20710683f;
    private Vector3 _limitsLower = new(-0.5f, -0.5f, 0f);
    private Vector3 _limitsUpper = new(0.5f, 0.5f, 2f);
    private Vector3 _eyePt;

    public CreatureAssayCamera() => Recalculate();

    public float Aspect
    {
        get;
        set
        {
            field = float.IsFinite(value) && value > 0f ? value : 1f;
            Recalculate();
        }
    } = 1f;

    public Vector3 Eye => _eyePt;
    public float FovRadians { get; set; } = MathF.PI / 4f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 2048f;

    public Matrix4x4 View =>
        Matrix4x4.CreateLookAt(_eyePt, _eyePt + Vector3.UnitY, Vector3.UnitZ);

    public Matrix4x4 Projection
    {
        get
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(
        FovRadians,
        Aspect,
        Near,
        Far);
        }
    }

    public void Fit(Vector3 lower, Vector3 upper)
    {
        _limitsLower = Vector3.Min(lower, upper);
        _limitsUpper = Vector3.Max(lower, upper);
        Recalculate();
    }

    private void Recalculate()
    {
        Vector3 span = Vector3.Max(
            _limitsUpper - _limitsLower,
            new Vector3(0.001f));
        Vector3 middle = (_limitsLower + _limitsUpper) * 0.5f;
        float fittedVertical = MathF.Max(span.Z, span.X / Aspect);
        float gap = fittedVertical * FitGapFactor + span.Y * 0.5f;
        _eyePt = new Vector3(middle.X, middle.Y - gap, middle.Z);
    }
}

// Examination-specific facade over the shared private renderer
internal sealed class CreatureAssayViewportPainter :
    IWidgetViewportPainter,
    ICreatureAssayPainter,
    IDisposable
{
    private readonly CreatureAssayCamera _cam = new();
    private readonly PrivateActorViewportPainter _painter;

    public CreatureAssayViewportPainter(
        IRealmPassScope ambit,
        MacAC.Client.Graphics.Gpu.IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycles,
        RealmPaintRouter router,
        StageLightingUboWiring lampUbo,
        IActorTextureLifetime textureLifespan,
        IBatchMeshBridge triMeshBridge)
    {
        _painter = new PrivateActorViewportPainter(
            ambit,
            dev,
            cycles,
            router,
            lampUbo,
            textureLifespan,
            triMeshBridge,
            CreatureAssayActorAssembler.RasterizeIdent,
            _cam,
            "creature examination");
    }

    public bool TextureIsBottomUp => _painter.TextureIsBottomUp;

    public void AssignBeast(
        RealmActor? beast,
        Vector3 limitsLower,
        Vector3 limitsUpper)
    {
        if (beast is not null)
            _cam.Fit(limitsLower, limitsUpper);
        _painter.AssignActor(beast);
    }

    public uint Render(int width, int height) =>
        _painter.Render(width, height);

    public void Dispose() => _painter.Dispose();
}
