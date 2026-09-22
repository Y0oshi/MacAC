using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

internal enum DirectionalShadeCasterMaterial : byte
{
    Opaque,
    AlphaCutout,
}

internal readonly record struct DirectionalShadePreparedBatch(
    GpuTextureSlot TextureSlot,
    uint TextureLayer,
    FaceCulling CullMode,
    DirectionalShadeCasterMaterial Material,
    uint FoliageFlags = 0u);

internal readonly record struct DirectionalShadePreparedRun(
    int StartCommand,
    int CommandCount,
    FaceCulling CullMode,
    DirectionalShadeCasterMaterial Material);

internal readonly record struct DirectionalShadePreparationStats(
    int SourceCasters,
    int SourceMeshRefs,
    int SourceParts,
    int SourceBatches,
    int PreparedInstances,
    int PreparedOpaqueCommands,
    int PreparedAlphaCutoutCommands,
    int RejectedTransparentBatches,
    int RejectedFadedParts,
    int MissingMeshes,
    int UnresolvedAlphaCutoutTextures,
    int ActiveInstances = 0,
    int ActiveCommands = 0);

internal readonly record struct DirectionalShadeMeshGeometry(
    IClientGpuBuffer VertexBuffer,
    IClientGpuBuffer IndexBuffer);

internal readonly record struct DirectionalShadeTransformSource(
    bool Refreshable,
    int CasterIndex,
    int MeshIndex,
    bool IsSetupPart,
    Matrix4x4 SetupPartTransform)
{
    public static DirectionalShadeTransformSource Static(int invokerOrdinal)
    {
        return new(
            false,
            invokerOrdinal,
            MeshIndex: 0,
            IsSetupPart: false,
            SetupPartTransform: default);
    }

    public static DirectionalShadeTransformSource Dynamic(
        int invokerOrdinal,
        int triMeshOrdinal,
        bool isRigPiece,
        in Matrix4x4 rigPieceXform)
    {
        return new(
            true,
            invokerOrdinal,
            triMeshOrdinal,
            isRigPiece,
            rigPieceXform);
    }
}

internal sealed partial class DirectionalShadePreparedDraws
{
    private DirectionalShadeSourceDraw[] _src = [];

    private Matrix4x4[] _xforms = [];

    private DirectionalShadeTransformSource[] _xformSrcs = [];

    private int[] _dynamicXformSockets = [];

    private int[] _allDynamicXformSockets = [];

    private int[] _leadDynamicXformByInvoker = [];

    private int[] _upcomingDynamicXform = [];

    private int[] _denseAlteredPostureByInvoker = [];

    private RenderMirrorId[] _mappedInvokerIdents = [];

    private RenderMirrorClass[] _mappedInvokerClasses = [];

    private bool[] _mappedInvokerPersonaPresent = [];

    private DrawElementsIndirectDirective[] _commands = [];

    private DirectionalShadePreparedBatch[] _lots = [];

    private DirectionalShadePreparedRun[] _executions = [];

    private DrawElementsIndirectDirective[] _engagedDirectives = [];

    private DirectionalShadePreparedBatch[] _engagedLots = [];

    private DirectionalShadePreparedRun[] _engagedExecutions = [];

    private int[] _paintUpcomingInCluster = [];

    private int[] _clusterFront = [];

    private int[] _clusterRear = [];

    private int[] _clusterTallyByCluster = [];

    private ulong[] _clusterTagHi = [];

    private ulong[] _clusterTagLo = [];

    private int[] _clusterLeadPaint = [];

    private int[] _clusterOrdering = [];

    private readonly Dictionary<DirectionalShadeDrawKey, int> _clusterByTag = [];

    private int _srcTally;

    private int _directiveTally;

    private int _execTally;

    private int _engagedDirectiveTally;

    private int _engagedExecTally;

    private int _dynamicXformSocketTally;

    private int _allDynamicXformSocketTally;

    private int _mappedInvokerTally;

    private bool _structure;

    private bool _reattemptTaxonomyUpcomingCycle;

    private static void SecureCap<T>(ref T[] vals, int needed)
    {
        if (vals.Length >= needed)
            return;
        int cap = vals.Length is 0 ? 16 : vals.Length;
        while (cap < needed)
            cap = checked(cap * 2);
        Array.Resize(ref vals, cap);
    }

    private readonly struct ClusterOrderComparer(
        ulong[] tagHi,
        ulong[] tagLo,
        int[] leadPaint) : IComparer<int>
    {
        public int Compare(int x, int y)
        {
            ulong left = tagHi[x];
            ulong right = tagHi[y];
            if (left != right)
                return left < right ? -1 : 1;
            left = tagLo[x];
            right = tagLo[y];
            return left != right ? left < right ? -1 : 1 : leadPaint[x].CompareTo(leadPaint[y]);
        }
    }

    private readonly record struct DirectionalShadeDrawKey(
        uint FirstIndex,
        int BaseVertex,
        int IndexCount,
        GpuTextureSlot TextureSlot,
        uint TextureLayer,
        FaceCulling CullMode,
        DirectionalShadeCasterMaterial Material,
        uint FoliageFlags = 0u);

    private readonly record struct DirectionalShadeSourceDraw(
        DirectionalShadeDrawKey Key,
        Matrix4x4 Transform,
        DirectionalShadeTransformSource TransformSource);
}

public sealed partial class RealmPaintRouter
{
    private readonly DirectionalShadePreparedDraws _directedShadeDraws = new();

    internal DirectionalShadeMeshGeometry FetchDirectedShadeGeo()
    {
        GlobalTriMeshBuffer triMesh = _triMeshBridge.TriMeshKeeper?.GlobalBuf
            ?? throw new InvalidOperationException("The shared mesh arena isn't published");
        return new DirectionalShadeMeshGeometry(
            triMesh.VertVault ?? throw new InvalidOperationException(
                "The shared mesh arena has no vertex store"),
            triMesh.OrdinalVault ?? throw new InvalidOperationException(
                "The shared mesh arena has no index store"));
    }

    internal long DirectedShadeReadinessVer =>
        _triMeshBridge.TriMeshKeeper?.RasterizeBlobReadinessVer ?? 0L;

    internal DirectionalShadePreparedDraws ReadyDirectedShadeDraws(
        DirectionalShadeCasterFrame casters,
        bool allowWiringReassemble = true)
    {
        ArgumentNullException.ThrowIfNull(casters);
        var src = casters.Casters;
        long rasterizeBlobReadinessVer =
            _triMeshBridge.TriMeshKeeper?.RasterizeBlobReadinessVer ?? 0L;
        ulong seeThroughFadeRev = _seeThroughFades.Revision;
        if (!_directedShadeDraws.RequiresWiringAssemble(
                casters.Generation,
                casters.AssembleSequence,
                rasterizeBlobReadinessVer,
                seeThroughFadeRev))
        {
            _directedShadeDraws.RefreshDynamicTransforms(casters);
            _directedShadeDraws.ApplySelection(casters);
            return _directedShadeDraws;
        }
        if (!allowWiringReassemble
            && _directedShadeDraws.SrcInvokerAssembleSeries
                == casters.AssembleSequence
            && _directedShadeDraws.SrcGen == casters.Generation)
        {
            _directedShadeDraws.RefreshDynamicTransforms(casters);
            _directedShadeDraws.ApplySelection(casters);
            return _directedShadeDraws;
        }

        int estimatedInsts = 0;
        for (int idx = 0; idx < src.Length; ++idx)
        {
            estimatedInsts = checked(
                estimatedInsts
                + src[idx].Projection.EntityPayload.MeshRefs.Count);
        }
        if (!_directedShadeDraws.TryCommence(
                casters.Generation,
                casters.AssembleSequence,
                estimatedInsts,
                rasterizeBlobReadinessVer,
                seeThroughFadeRev))
        {
            _directedShadeDraws.ApplySelection(casters);
            return _directedShadeDraws;
        }

        int triMeshRefs = 0;
        int pieces = 0;
        int lots = 0;
        int rejectedSeeThru = 0;
        int rejectedFaded = 0;
        int absentTriMeshes = 0;
        int unresolvedCutoutTextures = 0;
        try
        {
            for (int invokerOrdinal = 0; invokerOrdinal < src.Length; ++invokerOrdinal)
            {
                var proj = src[invokerOrdinal].Projection;
                _directedShadeDraws.ChartInvokerPersona(
                    invokerOrdinal,
                    proj.Id,
                    proj.ProjectionClass);
                var projTriMeshes =
                    proj.EntityPayload.MeshRefs;
                var cycleContender = new RenderFrameActorCandidate(
                    proj,
                    MeshPartOffset: 0,
                    MeshPartCount: projTriMeshes.Count,
                    Animated: src[invokerOrdinal].UsesLatestMovingXforms);
                var contender =
                    RasterizeInstContender.FromCycle(
                        in cycleContender,
                        proj.Residency.OwnerLandblockId);
                SwatchCompoundPersona swatchPersona =
                    proj.EntityPayload.PaletteOverride is null
                        ? default
                        : BitmapStash.FetchSwatchPersona(
                            proj.EntityPayload.PaletteOverride);

                for (int triMeshOrdinal = 0;
                     triMeshOrdinal < projTriMeshes.Count;
                     ++triMeshOrdinal)
                {
                    ++triMeshRefs;
                    TriMeshRef triMeshRef = projTriMeshes[triMeshOrdinal];
                    var rasterizeBlob =
                        _triMeshBridge.TryFetchRenderData(triMeshRef.GfxObjId);
                    if (rasterizeBlob is null)
                    {
                        ++absentTriMeshes;
                        _triMeshBridge.SecureFetched(triMeshRef.GfxObjId);
                        continue;
                    }

                    if (rasterizeBlob.IsSetup && rasterizeBlob.SetupParts.Count > 0)
                    {
                        bool actorHasCutoutSubset = FoliageWindTaxonomy
                            .CalculateActorHasCutoutSubset(
                                rasterizeBlob.SetupParts,
                                _triMeshBridge,
                                static (bridge, piece) => bridge.TryFetchRenderData(piece.GfxObjId)
                                    is { HasCutoutSubset: true });

                        for (int rigPieceOrdinal = 0;
                             rigPieceOrdinal < rasterizeBlob.SetupParts.Count;
                             ++rigPieceOrdinal)
                        {
                            ++pieces;
                            if (PieceFadeExcludesInvoker(
                                    proj.Source.LocalEntityId,
                                    rigPieceOrdinal))
                            {
                                ++rejectedFaded;
                                continue;
                            }

                            (ulong pieceGfxObjRefIdent, Matrix4x4 pieceXform) =
                                rasterizeBlob.SetupParts[rigPieceOrdinal];
                            var pieceBlob =
                                _triMeshBridge.TryFetchRenderData(pieceGfxObjRefIdent);
                            if (pieceBlob is null)
                            {
                                ++absentTriMeshes;
                                _triMeshBridge.SecureFetched(pieceGfxObjRefIdent);
                                continue;
                            }

                            Matrix4x4 model = ConstructPieceRealmMatrix(
                                proj.Transform.LocalToWorld,
                                triMeshRef.PartTransform,
                                pieceXform);
                            DirectionalShadeTransformSource xformSrc =
                                src[invokerOrdinal].UsesLatestMovingXforms
                                    ? DirectionalShadeTransformSource.Dynamic(
                                        invokerOrdinal,
                                        triMeshOrdinal,
                                        true,
                                        in pieceXform)
                                    : DirectionalShadeTransformSource.Static(
                                        invokerOrdinal);
                            AppendDirectedShadeLots(
                                pieceBlob,
                                in contender,
                                triMeshRef,
                                swatchPersona,
                                in model,
                                in xformSrc,
                                ref lots,
                                ref rejectedSeeThru,
                                ref unresolvedCutoutTextures,
                                actorHasCutoutSubset);
                        }
                    }
                    else
                    {
                        ++pieces;
                        if (PieceFadeExcludesInvoker(
                                proj.Source.LocalEntityId,
                                triMeshOrdinal))
                        {
                            ++rejectedFaded;
                            continue;
                        }

                        Matrix4x4 model = triMeshRef.PartTransform
                            * proj.Transform.LocalToWorld;
                        Matrix4x4 noRigPiece = default;
                        DirectionalShadeTransformSource xformSrc =
                            src[invokerOrdinal].UsesLatestMovingXforms
                                ? DirectionalShadeTransformSource.Dynamic(
                                    invokerOrdinal,
                                    triMeshOrdinal,
                                    false,
                                    in noRigPiece)
                                : DirectionalShadeTransformSource.Static(
                                    invokerOrdinal);
                        AppendDirectedShadeLots(
                            rasterizeBlob,
                            in contender,
                            triMeshRef,
                            swatchPersona,
                            in model,
                            in xformSrc,
                            ref lots,
                            ref rejectedSeeThru,
                            ref unresolvedCutoutTextures);
                    }
                }
            }

            var stats = new DirectionalShadePreparationStats(
                SourceCasters: src.Length,
                SourceMeshRefs: triMeshRefs,
                SourceParts: pieces,
                SourceBatches: lots,
                PreparedInstances: 0,
                PreparedOpaqueCommands: 0,
                PreparedAlphaCutoutCommands: 0,
                RejectedTransparentBatches: rejectedSeeThru,
                RejectedFadedParts: rejectedFaded,
                MissingMeshes: absentTriMeshes,
                UnresolvedAlphaCutoutTextures: unresolvedCutoutTextures);
            _directedShadeDraws.Complete(
                casters.Generation,
                casters.AssembleSequence,
                in stats,
                rasterizeBlobReadinessVer,
                seeThroughFadeRev);
            _directedShadeDraws.ApplySelection(casters);
            return _directedShadeDraws;
        }
        catch
        {
            _directedShadeDraws.Cancel();
            throw;
        }
    }

    private bool PieceFadeExcludesInvoker(uint actorIdent, int pieceOrdinal)
    {
        return _seeThroughFades.TryFetchLatestVal(
            actorIdent,
            checked((uint)pieceOrdinal),
            out float seeThrough)
        && DirectionalShadePreparedDraws.FadeExcludesInvoker(seeThrough);
    }

    private void AppendDirectedShadeLots(
        ThingRasterizeBlob rasterizeBlob,
        in RasterizeInstContender contender,
        TriMeshRef triMeshRef,
        SwatchCompoundPersona swatchPersona,
        in Matrix4x4 model,
        in DirectionalShadeTransformSource xformSrc,
        ref int srcLots,
        ref int rejectedSeeThru,
        ref int unresolvedCutoutTextures,
        bool? actorHasCutoutSubsetOverride = null)
    {
        if (_triMeshBridge.IsCoreConcealedMarker(triMeshRef.GfxObjId))
            return;

        bool actorHasCutoutSubset = actorHasCutoutSubsetOverride ?? rasterizeBlob.HasCutoutSubset;
        for (int lotOrdinal = 0;
             lotOrdinal < rasterizeBlob.Batches.Count;
             ++lotOrdinal)
        {
            var lot = rasterizeBlob.Batches[lotOrdinal];

            if (!CanonBareSubsetPolicy.Draws(contender.IsBuildingShell, lot.Key.IsSolid))
                continue;

            ++srcLots;
            if (!DirectionalShadePreparedDraws.TryClassifyMatl(
                    lot.Translucency,
                    out DirectionalShadeCasterMaterial matl))
            {
                ++rejectedSeeThru;
                continue;
            }

            var textureSocket = GpuTextureSlot.Unassigned;
            uint textureStratum = 0;
            if (matl is DirectionalShadeCasterMaterial.AlphaCutout)
            {
                if (lot.Key.SurfaceId is 0 or uint.MaxValue)
                {
                    ++unresolvedCutoutTextures;
                    continue;
                }
                var texture = LocateTexture(
                    in contender,
                    triMeshRef,
                    lot,
                    swatchPersona,
                    out bool compoundQueued);
                if (compoundQueued || !texture.Slot.IsAssigned)
                {
                    ++unresolvedCutoutTextures;
                    continue;
                }
                textureSocket = texture.Slot;
                textureStratum = texture.Layer;
            }

            uint foliageFlagSet = FoliageWindTaxonomy.Classify(
                contender.LocalEntityId,
                FoliageWindExclusions.Contains(triMeshRef.GfxObjId),
                lot.Translucency,
                actorHasCutoutSubset);

            _directedShadeDraws.Add(
                lot.LeadIdx,
                checked((int)lot.BaseVertex),
                lot.OrdinalTally,
                textureSocket,
                textureStratum,
                lot.CullMode,
                matl,
                in model,
                in xformSrc,
                foliageFlagSet);
        }
    }
}
