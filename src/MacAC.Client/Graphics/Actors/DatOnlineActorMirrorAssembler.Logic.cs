using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics;

internal sealed partial class DatOnlineActorMirrorAssembler
{
    public void RestartSessPhase()
    {
    }

    private uint LocateImpactPiece(uint baseIdent)
    {
        if (!GfxObjLodResolver.TryResolveCloseGfxObj(
                _datFiles,
                baseIdent,
                out uint socketZeroIdent,
                out PartMesh? socketZeroGfx))

            return baseIdent;

        if (socketZeroGfx is not null)
            _impactHoldings.StashGfxObjRef(socketZeroIdent, socketZeroGfx);
        return socketZeroIdent;
    }

    private Dictionary<int, Dictionary<uint, uint>>? LocateCanvasSubstitutions(
        IReadOnlyList<TriMeshRef> pieces,
        IReadOnlyList<ObjectCreation.TextureSwap> textureEdits)
    {
        if (textureEdits.Count is 0)
            return null;

        var formerToNewByPiece = new Dictionary<int, Dictionary<uint, uint>>();
        foreach (ObjectCreation.TextureSwap edit in textureEdits)
        {
            if (!formerToNewByPiece.TryGetValue(edit.PartIndex, out var formerToNew))
            {
                formerToNew = [];
                formerToNewByPiece.Add(edit.PartIndex, formerToNew);
            }
            formerToNew[edit.OldTexture] = edit.NewTexture;
        }

        var outcome = new Dictionary<int, Dictionary<uint, uint>>();
        for (int pieceOrdinal = 0; pieceOrdinal < pieces.Count; ++pieceOrdinal)
        {
            if (!formerToNewByPiece.TryGetValue(pieceOrdinal, out var formerToNew))
                continue;

            PartMesh? gfx = _datFiles.Get<PartMesh>(pieces[pieceOrdinal].GfxObjId);
            if (gfx is null)

                continue;
            _impactHoldings.StashGfxObjRef(pieces[pieceOrdinal].GfxObjId, gfx);

            Dictionary<uint, uint>? settled = null;
            foreach (uint canvasQid in gfx.SkinIds)
            {
                uint canvasIdent = (uint)canvasQid;
                Skin? canvas = _datFiles.Get<Skin>(canvasIdent);
                if (canvas is null)
                    continue;
                uint originalTexture = (uint)canvas.TextureId;
                if (originalTexture is 0
                    || !formerToNew.TryGetValue(originalTexture, out uint newTexture))

                    continue;

                (settled ??= [])[canvasIdent] = newTexture;
            }

            if (settled is not null)
                outcome[pieceOrdinal] = settled;
        }

        return outcome.Count is 0 ? null : outcome;
    }

    private void ImposeCanonShutDegrades(List<TriMeshRef> pieces)
    {
        for (int pieceOrdinal = 0; pieceOrdinal < pieces.Count; ++pieceOrdinal)
        {
            TriMeshRef piece = pieces[pieceOrdinal];
            if (!GfxObjLodResolver.TryResolveCloseGfxObj(
                    _datFiles,
                    piece.GfxObjId,
                    out uint settledIdent,
                    out _)
                || settledIdent == piece.GfxObjId)

                continue;

            pieces[pieceOrdinal] = new TriMeshRef(settledIdent, piece.PartTransform);
        }
    }

    private bool ImposeLooks(
        OnlineActorRecord anticipatedCapture,
        RealmSession.MoverSpawn summon,
        OnlineActorAppearancePulseLedger visualRefresh,
        RigSpec rig,
        float scaling,
        Vector3 realmOrigin,
        IReadOnlyList<uint> impactPieceGfxObjRefIdents,
        IReadOnlyList<TriMeshRef> triMeshRefs,
        SwatchOverride? swatchOverride,
        IReadOnlyList<PartSwap> pieceSubstitutions,
        IReadOnlyList<Matrix4x4> indexedPieceXforms,
        IReadOnlyList<bool> indexedPieceOnHand,
        IReadOnlyList<OnlineMotionPartTemplate> movingPieceBlueprint,
        ExtentAccumulator limits,
        ulong anticipatedBuildIntegrationVer)
    {
        RealmActor actor = visualRefresh.Entity;
        if (!_runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || !ReferenceEquals(anticipatedCapture.WorldEntity, actor))

            return false;

        var looksImpact =
            OnlineActorAppearanceWiring.ReadyImpact(
                _runtime,
                _impactBuilder,
                actor,
                rig,
                impactPieceGfxObjRefIdents,
                summon,
                realmOrigin);

        bool published = _summonBridge.OnLooksAltered(
            actor,
            triMeshRefs,
            pieceSubstitutions,
            () =>
            {
                _textures.FreeHolder(actor.Id);
                actor.ImposeLooks(triMeshRefs, swatchOverride, pieceSubstitutions);
            },
            () =>
            {
                if (!_runtime.IsLatestBuildIntegration(
                        anticipatedCapture,
                        anticipatedBuildIntegrationVer)
                    || !ReferenceEquals(anticipatedCapture.WorldEntity, actor))

                    return;
                if (looksImpact is not null)
                {
                    OnlineActorAppearanceWiring.SealImpact(
                        _runtime,
                        _shades,
                        looksImpact);
                }
                actor.AssignIndexedPiecePostures(indexedPieceXforms, indexedPieceOnHand);
                if (limits.TryGet(out Vector3 floor, out Vector3 ceiling))
                    actor.AssignOwnLimits(floor, ceiling);
                if (visualRefresh.Animation is { } anim)
                {
                    OnlineActorAppearanceWiring.RebindAnim(
                        anim,
                        actor,
                        rig,
                        scaling,
                        movingPieceBlueprint,
                        indexedPieceOnHand);
                }
                _taxonomy.DirtyActor(actor.Id);
                if (_runtime.IsLatestBuildIntegration(
                        anticipatedCapture,
                        anticipatedBuildIntegrationVer)
                    && anticipatedCapture.ProjSort is OnlineActorMirrorKind.Attached)
                {
                    _equippedDescendants.OnSummon(anticipatedCapture.Snapshot);
                }
                else
                {
                    _fxPostures.BroadcastTriMeshRefs(actor);
                    if (!_runtime.IsLatestBuildIntegration(
                            anticipatedCapture,
                            anticipatedBuildIntegrationVer))

                        return;
                    _equippedDescendants.OnPosturePublished(summon.Guid);
                }
            });
        return published
            && _runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            && ReferenceEquals(anticipatedCapture.WorldEntity, actor);
    }

    private static SwatchOverride? BuildSwatchOverride(
        RealmSession.MoverSpawn summon)
    {
        if (summon.SubPalettes is not { Count: > 0 } subSwatches)
            return null;

        var spans = new SwatchOverride.SubPaletteSpan[subSwatches.Count];
        for (int idx = 0; idx < subSwatches.Count; ++idx)
        {
            spans[idx] = new SwatchOverride.SubPaletteSpan(
                subSwatches[idx].SubPaletteId,
                subSwatches[idx].Offset,
                subSwatches[idx].Length);
        }
        return new SwatchOverride(summon.BasePaletteId ?? 0u, spans);
    }

    private static PartSwap[] BuildPieceSubstitutions(
        IReadOnlyList<ObjectCreation.AnimPartSwap> edits)
    {
        if (edits.Count is 0)
            return [];

        PartSwap[] outcome = new PartSwap[edits.Count];
        for (int idx = 0; idx < edits.Count; ++idx)
            outcome[idx] = new PartSwap(edits[idx].PartIndex, edits[idx].NewModelId);
        return outcome;
    }

    private AnimSequencer? BuildLocomotionScheduler(
        RigSpec rig,
        RealmSession.MoverSpawn summon)
    {
        uint locomotionChartIdent = summon.MotionTableId ?? (uint)rig.DefaultMotionBookId;
        return locomotionChartIdent is not 0
            && _datFiles.Get<MotionBook>(locomotionChartIdent) is { } locomotionChart
            ? SummonLocomotionInitializer.Create(
                rig,
                locomotionChart,
                _animFetcher,
                summon.MotionState)
            : null;
    }

    private void SynchronizeKeptAnim(
        OnlineActorRecord anticipatedCapture,
        OnlineActorMotionLedger anim,
        RigSpec rig,
        RealmSession.MoverSpawn summon,
        GaitResolver.RestCycle? idleCycle)
    {
        uint locomotionChartIdent = summon.MotionTableId ?? (uint)rig.DefaultMotionBookId;
        MotionBook? locomotionChart = locomotionChartIdent is 0
            ? null
            : _datFiles.Get<MotionBook>(locomotionChartIdent);
        OnlineActorCreateMotionSynchronization
            .TrySynchronizeInterruptedStartingHolder(
                anticipatedCapture,
                anim,
                idleCycle?.Animation,
                idleCycle is null ? 0 : Math.Max(0, idleCycle.LowFrame),
                idleCycle is null
                    ? 0
                    : Math.Min(
                        idleCycle.HighFrame,
                        idleCycle.Animation.Frames.Count - 1),
                idleCycle?.Framerate ?? 0f,
                locomotionChart,
                summon.MotionState);
    }

    private void InspectRealmCycleAgreement(uint lbIdent)
    {
        uint coreMiddle = _runtime.Physics.RealmCycleMiddleLbIdent;
        if (coreMiddle is 0u || !_origin.IsKnown)
            return;

        int coreMiddleX = (int)((coreMiddle >> 24) & 0xFFu);
        int coreMiddleY = (int)((coreMiddle >> 16) & 0xFFu);
        Console.WriteLine(System.FormattableString.Invariant(
            $"[world-frame] agree centre=({coreMiddleX},{coreMiddleY}) projecting=0x{lbIdent:X8}"));
    }

    private static bool HasHumanoidNullPieceArrangement(RigSpec rig)
    {
        if (rig.PartIds.Count is not 34)
            return false;
        const uint nullPieceGfx = 0x010001ECu;
        int nullSockets = 0;
        for (int idx = 17; idx < rig.PartIds.Count; ++idx)
        {
            if ((uint)rig.PartIds[idx] == nullPieceGfx)
                ++nullSockets;
        }
        return nullSockets >= 8;
    }
}
