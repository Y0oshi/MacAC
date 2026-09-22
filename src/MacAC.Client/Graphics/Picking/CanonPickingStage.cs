using System.Numerics;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Graphics.Picking;

internal interface IRealmStagePickingFrame
{
    void BeginFrame(FrustumFacets? readiedLensFrustum = null);

    void ConcludeFrame();

    void CancelFrame();
}

internal sealed class CanonPickingStage(
    ICanonPickingGeometrySource geometry,
    CanonPickingLightingPulse? illuminationPulse = null) :
    ICanonPickingRenderSink,
    ICanonPickingRenderOracle,
    ICanonPickingLightingSource,
    IRealmStagePickingFrame
{
    private readonly ICanonPickingGeometrySource _geo = geometry ?? throw new ArgumentNullException(nameof(geometry));
    private readonly CanonPickingLightingPulse _illuminationPulse = illuminationPulse ?? new CanonPickingLightingPulse();
    private List<CanonPickPart> _structure = [];
    private List<CanonPickPart> _published = [];
    private readonly HashSet<PieceTag> _structureTags = [];
    private FrustumFacets? _lensFrustum;
    private ICurrentRenderPickingWatcher? _latestRasterizeTableauWatcher;
    private bool _cycleOpen;

    private readonly record struct PieceTag(uint LocalEntityId, int PartIndex, uint GfxObjId);

    public void OpenIlluminationPulse(uint srvOid, uint ownActorIdent)
        => _illuminationPulse.Start(srvOid, ownActorIdent);

    public void PulseIllumination()
        => _illuminationPulse.Tick();

    public bool TryFetchIllumination(
        uint srvOid,
        uint ownActorIdent,
        out CanonPickingLighting illumination)
        => _illuminationPulse.TryGet(srvOid, ownActorIdent, out illumination);

    public void Reset()
    {
        _latestRasterizeTableauWatcher?.CancelPickCycle();
        _cycleOpen = false;
        _structure.Clear();
        _published.Clear();
        _structureTags.Clear();
        _lensFrustum = null;
        _illuminationPulse.Clear();
    }

    public void BeginFrame(FrustumFacets? readiedLensFrustum = null)
    {
        if (_cycleOpen)
        {
            throw new InvalidOperationException(
                "The retail selection scene can't begin a second frame prior to completing or aborting the first");
        }

        _cycleOpen = true;
        _structure.Clear();
        _structureTags.Clear();
        _lensFrustum = readiedLensFrustum;
        _latestRasterizeTableauWatcher?.CommencePickCycle();
    }

    public void AssignLensFrustum(FrustumFacets lensFrustum)
        => _lensFrustum = lensFrustum;

    public void AddVisiblePart(
        RealmActor actor,
        int pieceOrdinal,
        uint gfxObjRefIdent,
        Matrix4x4 pieceRealm)
    {
        ArgumentNullException.ThrowIfNull(actor);
        AddVisiblePart(
            actor.ServerGuid,
            actor.Id,
            pieceOrdinal,
            gfxObjRefIdent,
            pieceRealm);
    }

    public void AddVisiblePart(
        uint srvOid,
        uint ownActorIdent,
        int pieceOrdinal,
        uint gfxObjRefIdent,
        Matrix4x4 pieceRealm)
    {
        if (!_cycleOpen)
            return;
        if (srvOid is 0u)
            return;
        if (!_structureTags.Add(new PieceTag(
                ownActorIdent,
                pieceOrdinal,
                gfxObjRefIdent)))
            return;
        if (!TryBuildShownPiece(
                srvOid,
                ownActorIdent,
                pieceOrdinal,
                gfxObjRefIdent,
                pieceRealm,
                out CanonPickPart piece))

            return;

        _structure.Add(piece);
        _latestRasterizeTableauWatcher?.WatchPickPiece(
            srvOid,
            ownActorIdent,
            pieceOrdinal,
            gfxObjRefIdent,
            pieceRealm,
            piece.Mesh);
    }

    public bool TryBuildShownPiece(
        uint srvOid,
        uint ownActorIdent,
        int pieceOrdinal,
        uint gfxObjRefIdent,
        Matrix4x4 pieceRealm,
        out CanonPickPart piece)
    {
        if (srvOid is 0u)
        {
            piece = default;
            return false;
        }

        var triMesh = _geo.Resolve(gfxObjRefIdent);
        if (triMesh is null
            || _lensFrustum is not { } frustum
            || !DrawingOrbIntersectsFrustum(
                triMesh,
                pieceRealm,
                frustum))
        {
            piece = default;
            return false;
        }

        piece = new CanonPickPart(
            srvOid,
            ownActorIdent,
            pieceOrdinal,
            pieceRealm,
            triMesh);
        return true;
    }

    public void ConcludeFrame()
    {
        if (!_cycleOpen)
        {
            throw new InvalidOperationException(
                "The retail selection scene can't complete without an open frame");
        }

        _cycleOpen = false;
        (_published, _structure) = (_structure, _published);
        _latestRasterizeTableauWatcher?.ConcludePickCycle();
    }

    public void CancelFrame()
    {
        if (!_cycleOpen)
            return;

        _cycleOpen = false;
        _latestRasterizeTableauWatcher?.CancelPickCycle();
        _structure.Clear();
        _structureTags.Clear();
        _lensFrustum = null;
    }

    public CanonPickHit? Pick(
        float pointerX,
        float pointerY,
        Vector2 viewRect,
        Matrix4x4 lens,
        Matrix4x4 proj,
        uint skipSrvOid)
    {
        if (viewRect.X <= 0f || viewRect.Y <= 0f)
            return null;
        var ray = ScenePicker.AssembleRay(
            pointerX, pointerY, viewRect.X, viewRect.Y, lens, proj);
        return CanonWorldPicker.Pick(
            ray.Origin, ray.Direction, _published, skipSrvOid);
    }

    internal void AssignLatestRasterizeTableauWatcher(
        ICurrentRenderPickingWatcher? watcher) =>
        _latestRasterizeTableauWatcher = watcher;

    internal static bool DrawingOrbIntersectsFrustum(
        CanonPickMesh triMesh,
        Matrix4x4 ownToRealm,
        FrustumFacets frustum)
    {
        Vector3 middle = Vector3.Transform(triMesh.SphereCenter, ownToRealm);
        float scalingX = new Vector3(ownToRealm.M11, ownToRealm.M12, ownToRealm.M13).Length();
        float scalingY = new Vector3(ownToRealm.M21, ownToRealm.M22, ownToRealm.M23).Length();
        float scalingZ = new Vector3(ownToRealm.M31, ownToRealm.M32, ownToRealm.M33).Length();
        float radius = triMesh.SphereRadius * MathF.Max(scalingX, MathF.Max(scalingY, scalingZ));
        return AssessPlane(frustum.Left, middle, radius)
            && AssessPlane(frustum.Right, middle, radius)
            && AssessPlane(frustum.Bottom, middle, radius)
            && AssessPlane(frustum.Top, middle, radius)
            && AssessPlane(frustum.Near, middle, radius)
            && AssessPlane(frustum.Far, middle, radius);
    }

    private static bool AssessPlane(Vector4 plane, Vector3 middle, float radius)
    {
        return plane.X * middle.X + plane.Y * middle.Y + plane.Z * middle.Z + plane.W >= -radius;
    }
}
