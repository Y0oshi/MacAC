using MacAC.Dat;
using System.Numerics;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Graphics.Stride;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class RealmPaintRouter
{
    private readonly HashSet<StrideDrawnPartKey> _strollDrawnPieces = [];
    private bool _strollPieceCycleEngaged;
    private readonly record struct StrideDrawnPartKey(
        RenderMirrorId ProjectionId,
        int PartIndex);

    internal void CommenceStrollPieceCycle()
    {
        if (_strollPieceCycleEngaged)
        {
            throw new InvalidOperationException(
                "A walk part frame was opened prior to the previous scope closed");
        }

        _missRequested.Clear();
        _strollDrawnPieces.Clear();
        _strollPieceCycleEngaged = true;
    }

    internal void ProgressStrollPiecePassStamp()
    {
        if (!_strollPieceCycleEngaged)
        {
            throw new InvalidOperationException(
                "The walk part pass stamp can't advance beyond an active frame");
        }

        _strollDrawnPieces.Clear();
    }

    internal void FinishStrollPieceCycle()
    {
        _strollPieceCycleEngaged = false;
        _strollDrawnPieces.Clear();
    }

    private bool TryStampStrollPiece(
        in RenderMirrorRecord proj,
        int pieceOrdinal) =>
        !_strollPieceCycleEngaged
        || proj.EntityPayload.CasterIdentity
            == RasterizeInvokerPersonaFlavor.LocalPlayer
        || _strollDrawnPieces.Add(new StrideDrawnPartKey(proj.Id, pieceOrdinal));

    internal readonly record struct StrideClassifiedBatch(
        ClusterTag Key,
        Matrix4x4 Transform,
        uint ClipSlot,
        InstLampGroup Lights,
        uint IndoorFlag,
        float Alpha,
        Vector2 SelectionLighting,
        uint DetailCategory,
        bool IsOpaque,
        Vector3 LocalSortCenter,
        float SortDistanceSq = 0f);

    internal readonly record struct StrideClassifiedPickingPart(
        uint ServerGuid,
        uint LocalEntityId,
        int PartIndex,
        uint GfxObjId,
        Matrix4x4 LocalToWorld);

    internal readonly record struct StrideShelvedPart(
        ThingRasterizeBlob RenderData,
        StrideClassifiedPickingPart Selection,
        int BatchStart,
        int BatchCount);

    internal long StrollTriMeshReadinessVer =>
        _triMeshBridge.TriMeshKeeper?.RasterizeBlobReadinessVer ?? 0;

    internal bool StrollTaxonomyQueued { get; private set; }

    internal bool AdmitStashedStrollPiece(
        in RenderMirrorRecord capture,
        in StrideShelvedPart piece,
        IStrideLookInViewSource views,
        int courseOrdinal) =>
        LocatePieceShown(views, courseOrdinal, piece.RenderData,
            piece.Selection.LocalToWorld, out _, out _, out _)
        && TryStampStrollPiece(in capture, piece.Selection.PartIndex);

    internal void LocateStashedStrollIllumination(
        in RenderMirrorRecord capture,
        uint tupleLbIdent,
        out InstLampGroup lamps,
        out uint insideBit,
        out Vector2 pick)
    {
        var actor = RasterizeInstContender.FromProj(
            in capture, tupleLbIdent, moving: false);
        LocateStrollLampSet(in actor, out lamps, out bool inside);
        insideBit = inside ? 1u : 0u;
        pick = _pickIllumination?.TryFetchIllumination(
            actor.ServerGuid, actor.LocalEntityId, out CanonPickingLighting illumination) == true
            ? new Vector2(illumination.Luminosity, illumination.Diffuse)
            : new Vector2(0f, 1f);
    }

    private bool TryClassifyLot(
        ThingRasterizeBlob rasterizeBlob,
        int lotOrdinal,
        in RasterizeInstContender actor,
        TriMeshRef triMeshRef,
        SwatchCompoundPersona swatchPersona,
        float densityMultiplier,
        bool actorHasCutoutSubset,
        out ClusterTag tag,
        out bool compoundQueued)
    {
        tag = default;
        compoundQueued = false;
        ThingRasterizeLot lot = rasterizeBlob.Batches[lotOrdinal];

        if (!CanonBareSubsetPolicy.Draws(actor.IsBuildingShell, lot.Key.IsSolid))
            return false;

        SeeThroughKind seeThrough = lot.Translucency;

        if (densityMultiplier < 1.0f && IsSolid(seeThrough))
            seeThrough = SeeThroughKind.AlphaBlend;

        SettledBitmap texture = LocateTexture(
            in actor, triMeshRef, lot, swatchPersona, out compoundQueued);
        if (!texture.Slot.IsAssigned)
            return false;

        uint foliageFlagSet = FoliageWindTaxonomy.Classify(
            actor.LocalEntityId,
            FoliageWindExclusions.Contains(triMeshRef.GfxObjId),
            lot.Translucency,
            actorHasCutoutSubset);
        tag = new ClusterTag(
            lot.LeadIdx, (int)lot.BaseVertex,
            lot.OrdinalTally, texture.Slot, texture.Layer, seeThrough,
            MaterialState: lot.MaterialState,
            FoliageFlags: foliageFlagSet,
            SurfaceOpacity: lot.SurfaceOpacity,
            CullMode: lot.CullMode);
        return true;
    }

    internal void ClassifyActorForStroll(
        in RenderMirrorRecord proj,
        uint tupleLbIdent,
        List<StrideClassifiedBatch> lots,
        List<StrideClassifiedPickingPart> pickPieces,
        bool onlineDynamic = false,
        IStrideLookInViewSource? gazeInViews = null,
        int gazeInCourseOrdinal = -1,
        uint gazeInChamberIdent = 0,
        StrideBuildingPicking? structurePick = null,
        Matrix4x4 structurePieceXform = default,
        List<StrideShelvedPart>? keptPieces = null)
    {
        ArgumentNullException.ThrowIfNull(lots);
        ArgumentNullException.ThrowIfNull(pickPieces);
        StrollTaxonomyQueued = false;

        if ((proj.Flags & RenderMirrorFlags.Draw) == 0)
            return;

        var actor =
            RasterizeInstContender.FromProj(
                in proj,
                tupleLbIdent,
                moving: onlineDynamic);

        (uint socket, bool culled) = LocateSocketForCycle();
        if (culled)
            return;

        LocateStrollLampSet(in actor, out InstLampGroup lamps, out bool inside);
        Vector2 pickIllumination =
            _pickIllumination?.TryFetchIllumination(
                actor.ServerGuid, actor.LocalEntityId, out CanonPickingLighting illumination) == true
                ? new Vector2(illumination.Luminosity, illumination.Diffuse)
                : new Vector2(0f, 1f);
        uint specificsBucket = actor.IsBuildingShell ? 1u : 0u;

        SwatchCompoundPersona swatchPersona = default;
        if (actor.PaletteOverride is not null)
            swatchPersona = BitmapStash.FetchSwatchPersona(actor.PaletteOverride);

        IReadOnlyList<TriMeshRef>? triMeshRefs = proj.EntityPayload.MeshRefs;
        if (structurePick is null && triMeshRefs is null)
            return;

        IReadOnlyDictionary<uint, uint>? structureCanvasSubstitutions =
            structurePick is not null && triMeshRefs is { Count: > 0 }
                ? triMeshRefs[0].CanvasOverrides
                : null;
        int triMeshRefTally = structurePick is null ? triMeshRefs!.Count : 1;
        for (int pieceOrdinal = 0; pieceOrdinal < triMeshRefTally; pieceOrdinal++)
        {
            TriMeshRef triMeshRef = structurePick is StrideBuildingPicking chosen
                ? new TriMeshRef(chosen.GfxObjId, structurePieceXform)
                {
                    CanvasOverrides = structureCanvasSubstitutions,
                }
                : triMeshRefs![pieceOrdinal];
            if (_triMeshBridge.IsCoreConcealedMarker(triMeshRef.GfxObjId))
                continue;

            ThingRasterizeBlob? rasterizeBlob = _triMeshBridge.TryFetchRenderData(triMeshRef.GfxObjId);
            if (rasterizeBlob is null)
            {
                StrollTaxonomyQueued = true;
                if (_missRequested.Add(triMeshRef.GfxObjId))
                    _triMeshBridge.SecureFetched(triMeshRef.GfxObjId);
                continue;
            }

            if (structurePick is null
                && rasterizeBlob.IsSetup
                && rasterizeBlob.SetupParts.Count > 0)
            {
                bool actorHasCutoutSubset = FoliageWindTaxonomy.CalculateActorHasCutoutSubset(
                    rasterizeBlob.SetupParts,
                    _triMeshBridge,
                    static (bridge, piece) => bridge.TryFetchRenderData(piece.GfxObjId)
                        is { HasCutoutSubset: true });

                for (int rigPieceOrdinal = 0; rigPieceOrdinal < rasterizeBlob.SetupParts.Count; rigPieceOrdinal++)
                {
                    (ulong gfxObjRefIdent, Matrix4x4 pieceXform) = rasterizeBlob.SetupParts[rigPieceOrdinal];
                    if (_triMeshBridge.IsCoreConcealedMarker((uint)gfxObjRefIdent))
                        continue;

                    ThingRasterizeBlob? pieceBlob = _triMeshBridge.TryFetchRenderData(gfxObjRefIdent);
                    if (pieceBlob is null)
                    {
                        StrollTaxonomyQueued = true;
                        if (_missRequested.Add(gfxObjRefIdent))
                            _triMeshBridge.SecureFetched(gfxObjRefIdent);
                        continue;
                    }

                    float density = onlineDynamic
                        ? TraversePieceDensity(
                            actor.ServerGuid,
                            actor.LocalEntityId,
                            (uint)rigPieceOrdinal)
                        : 1f;
                    if (density <= 0f)
                    {
                        continue;
                    }

                    Matrix4x4 restPosture = pieceXform * triMeshRef.PartTransform;
                    Matrix4x4 model = restPosture * actor.RootWorld;
                    int pickPieceOrdinal = unchecked((pieceOrdinal << 16) | (rigPieceOrdinal & 0xFFFF));

                    bool shown = LocatePieceShown(
                        gazeInViews,
                        gazeInCourseOrdinal,
                        pieceBlob,
                        model,
                        out _,
                        out _,
                        out _);
                    bool leadAdmission = shown
                        && (keptPieces is not null || TryStampStrollPiece(in proj, pickPieceOrdinal));
                    if (!leadAdmission)
                        continue;
                    int lotBegin = lots.Count;
                    WriteClassifiedLots(
                        pieceBlob, model, in actor, triMeshRef, swatchPersona,
                        actorHasCutoutSubset, socket, lamps, inside,
                        pickIllumination, specificsBucket, density,
                        lots);
                    pickPieces.Add(new StrideClassifiedPickingPart(
                        actor.ServerGuid, actor.LocalEntityId, pickPieceOrdinal,
                        (uint)gfxObjRefIdent, model));
                    keptPieces?.Add(new StrideShelvedPart(
                        pieceBlob, pickPieces[^1], lotBegin, lots.Count - lotBegin));
                }
            }
            else
            {
                float density = onlineDynamic
                    ? TraversePieceDensity(
                        actor.ServerGuid,
                        actor.LocalEntityId,
                        (uint)pieceOrdinal)
                    : 1f;
                if (density <= 0f)
                {
                    continue;
                }

                Matrix4x4 model = triMeshRef.PartTransform * actor.RootWorld;
                bool shown = LocatePieceShown(
                    gazeInViews,
                    gazeInCourseOrdinal,
                    rasterizeBlob,
                    model,
                    out _,
                    out _,
                    out _);
                bool leadAdmission = shown
                    && (keptPieces is not null || TryStampStrollPiece(in proj, pieceOrdinal));
                if (!leadAdmission)
                    continue;
                int lotBegin = lots.Count;
                WriteClassifiedLots(
                    rasterizeBlob, model, in actor, triMeshRef, swatchPersona,
                    actorHasCutoutSubsetOverride: null, socket, lamps, inside,
                    pickIllumination, specificsBucket, density,
                    lots);
                pickPieces.Add(new StrideClassifiedPickingPart(
                    actor.ServerGuid, actor.LocalEntityId, pieceOrdinal,
                    (uint)triMeshRef.GfxObjId, model));
                keptPieces?.Add(new StrideShelvedPart(
                    rasterizeBlob, pickPieces[^1], lotBegin, lots.Count - lotBegin));
            }
        }
    }

    private float TraversePieceDensity(
        uint srvOid,
        uint ownActorIdent,
        uint rigPieceOrdinal)
    {
        float density = ActorDensity(srvOid);
        if (density <= 0f)
            return 0f;
        return !_seeThroughFades.TryFetchLatestVal(
                ownActorIdent,
                rigPieceOrdinal,
                out float seeThrough)
            ? density
            : seeThrough >= 1f
            ? 0f
            : density * (1f - seeThrough);
    }

    private void LocateStrollLampSet(
        in RasterizeInstContender actor,
        out InstLampGroup lamps,
        out bool inside)
    {
        inside = InsideObjectReceivesTorches(actor.AncestorChamber);
        lamps = InstLampGroup.Disabled;
        IReadOnlyList<LightEmitter>? capture = _ptCapture;
        if (!inside || capture is null || capture.Count == 0)
            return;

        Vector3 middle =
            (actor.Bounds.Minimum + actor.Bounds.Maximum) * 0.5f;
        float radius =
            (actor.Bounds.Maximum - actor.Bounds.Minimum)
            .Length() * 0.5f;
        Span<int> chosen =
            stackalloc int[LightKeeper.UpperLightsPerObject];
        chosen.Fill(-1);
        LightKeeper.PickForObject(
            capture,
            middle,
            radius,
            chosen);
        lamps = InstLampGroup.From(chosen);
    }

    private static bool LocatePieceShown(
        IStrideLookInViewSource? gazeInViews,
        int courseOrdinal,
        ThingRasterizeBlob rasterizeBlob,
        Matrix4x4 ownToRealm,
        out Vector3 orbMiddle,
        out float orbRadius,
        out bool hasOrb)
    {
        orbMiddle = default;
        orbRadius = 0f;
        hasOrb = false;
        if (gazeInViews is null)
            return true;

        if (rasterizeBlob.SelectionSphere is not { Radius: > 0f } orb)
        {
            return gazeInViews.OrbShownInGazeInPivot(
                courseOrdinal, Vector3.Zero, radius: 0f, testOrb: false);
        }

        hasOrb = true;
        ConvertDrawingOrb(orb, ownToRealm, out orbMiddle, out orbRadius);
        return gazeInViews.OrbShownInGazeInPivot(courseOrdinal, in orbMiddle, orbRadius);
    }

    internal static bool GazeInDrawingOrbShown(
        IStrideLookInViewSource gazeInViews,
        int courseOrdinal,
        Orb orb,
        Matrix4x4 ownToRealm,
        out Vector3 middle,
        out float radius)
    {
        ArgumentNullException.ThrowIfNull(gazeInViews);
        ArgumentNullException.ThrowIfNull(orb);

        ConvertDrawingOrb(orb, ownToRealm, out middle, out radius);
        return gazeInViews.OrbShownInGazeInPivot(
            courseOrdinal,
            in middle,
            radius);
    }

    private static void ConvertDrawingOrb(
        Orb orb,
        Matrix4x4 ownToRealm,
        out Vector3 middle,
        out float radius)
    {
        middle = Vector3.Transform(orb.Center, ownToRealm);
        float scalingX = new Vector3(
            ownToRealm.M11,
            ownToRealm.M12,
            ownToRealm.M13).Length();
        float scalingY = new Vector3(
            ownToRealm.M21,
            ownToRealm.M22,
            ownToRealm.M23).Length();
        float scalingZ = new Vector3(
            ownToRealm.M31,
            ownToRealm.M32,
            ownToRealm.M33).Length();
        radius = orb.Radius
            * MathF.Max(scalingX, MathF.Max(scalingY, scalingZ));
    }

    private void WriteClassifiedLots(
        ThingRasterizeBlob rasterizeBlob,
        Matrix4x4 model,
        in RasterizeInstContender actor,
        TriMeshRef triMeshRef,
        SwatchCompoundPersona swatchPersona,
        bool? actorHasCutoutSubsetOverride,
        uint socket,
        InstLampGroup lamps,
        bool inside,
        Vector2 pickIllumination,
        uint specificsBucket,
        float density,
        List<StrideClassifiedBatch> drain)
    {
        bool actorHasCutoutSubset = actorHasCutoutSubsetOverride ?? rasterizeBlob.HasCutoutSubset;
        for (int lotIndex = 0; lotIndex < rasterizeBlob.Batches.Count; lotIndex++)
        {
            bool survives = TryClassifyLot(
                rasterizeBlob, lotIndex, in actor, triMeshRef, swatchPersona,
                densityMultiplier: density, actorHasCutoutSubset,
                out ClusterTag tag, out bool compoundQueued);
            StrollTaxonomyQueued |= compoundQueued;
            if (!survives)
                continue;

            drain.Add(new StrideClassifiedBatch(
                tag, model, socket, lamps, inside ? 1u : 0u, Alpha: density,
                pickIllumination, specificsBucket, IsOpaque: IsSolid(tag.Translucency),
                LocalSortCenter: rasterizeBlob.SortCenter));
        }
    }

    internal void BroadcastStrollPickPiece(in StrideClassifiedPickingPart piece) =>
        _pickDrain?.AddVisiblePart(
            piece.ServerGuid, piece.LocalEntityId, piece.PartIndex, piece.GfxObjId, piece.LocalToWorld);

    internal void SubmitWalkAlphaInstance(
        in StrideClassifiedBatch lot,
        Matrix4x4 lensProj)
    {
        CanonAlphaFifo fifo = _alphaFifo
            ?? throw new InvalidOperationException(
                "SubmitWalkAlphaInstance needs an active RetailAlphaQueue");

        if (_deferredAlpha.Count == 0)
            _postponedAlphaLensProj = lensProj;
        else if (_postponedAlphaLensProj != lensProj)
            throw new InvalidOperationException(
                "One retail alpha scope can't combine different view-projection matrices");

        var contender = new PostponedAlphaInst(
            lot.Key,
            new InstFacts(
                lot.Transform,
                SubmissionOrder: 0,
                lot.ClipSlot,
                lot.Lights,
                lot.IndoorFlag,
                lot.DetailCategory,
                lot.Alpha,
                lot.SelectionLighting));
        SubmitToAlphaFifo(
            fifo, lot.Key.Translucency, in contender, lot.DetailCategory == 1u, lensProj);
    }
}
