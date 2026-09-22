using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics;

internal sealed partial class DatOnlineActorMirrorAssembler
{
    public bool TryMaterialize(
        SimActorRecord anticipatedCanon,
        RealmSession.MoverSpawn canonSummon,
        OnlineMirrorPurpose purpose,
        ulong anticipatedBuildIntegrationVer,
        OnlineActorAppearancePulseLedger? appearanceUpdate = null)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCanon);
        if (purpose is OnlineMirrorPurpose.AppearanceMutation
            != (appearanceUpdate is not null))
        {
            throw new ArgumentException(
                "Appearance mutation needs the exact captured visual owner",
                nameof(appearanceUpdate));
        }
        if (!_runtime.IsLatestBuildIntegration(
                anticipatedCanon,
                anticipatedBuildIntegrationVer)
            || canonSummon.Guid != anticipatedCanon.ServerGuid
            || canonSummon.InstanceSequence != anticipatedCanon.Incarnation)

            return false;
        _runtime.TryFetchProj(
            anticipatedCanon,
            out OnlineActorRecord? anticipatedCapture);
        if (appearanceUpdate is not null && anticipatedCapture is null)
            return false;

        if (!_origin.IsKnown)
            return false;
        if (canonSummon.Position is null || canonSummon.SetupTableId is null)

            return false;

        var locus = canonSummon.Position.Value;
        int lbX = (int)((locus.LandblockId >> 24) & 0xFFu);
        int lbY = (int)((locus.LandblockId >> 16) & 0xFFu);
        if (!_origin.TrySecureAgreesWithCoreCycle(
                _runtime.Physics.RealmCycleMiddleLbIdent,
                locus.LandblockId,
                passageInFlight: _passage.IsWarpEngaged))

            return false;
        if (MacAC.Mechanics.Kinetics.KineticTelemetry.ProbeWorldFrameEnabled)
            InspectRealmCycleAgreement(locus.LandblockId);
        Vector3 realmOrigin = new Vector3(
            (lbX - _origin.CenterX) * 192f,
            (lbY - _origin.CenterY) * 192f,
            0f);
        Vector3 realmLocus = new(
            locus.PositionX + realmOrigin.X,
            locus.PositionY + realmOrigin.Y,
            locus.PositionZ);
        Quaternion spin = new Quaternion(
            locus.RotationX,
            locus.RotationY,
            locus.RotationZ,
            locus.RotationW);

        RigSpec? rig = _datFiles.Get<RigSpec>(canonSummon.SetupTableId.Value);
        if (rig is not null)
            _impactHoldings.StashRig(canonSummon.SetupTableId.Value, rig);
        if (rig is null)

            return false;

        _runtime.AssignHasPieceArr(anticipatedCanon, true);
        ushort? stanceOverride = canonSummon.MotionState?.Stance;
        ushort? directiveOverride = canonSummon.MotionState?.ForwardCommand;
        GaitResolver.RestCycle? idleCycle = GaitResolver.FetchIdleCycle(
            rig,
            _datFiles,
            _animFetcher,
            locomotionChartIdentOverride: canonSummon.MotionTableId,
            stanceOverride: stanceOverride,
            directiveOverride: directiveOverride);
        MotionFrame? idleFrame = null;
        if (idleCycle is not null)
        {
            int beginOrdinal = idleCycle.LowFrame;
            if (beginOrdinal < 0 || beginOrdinal >= idleCycle.Animation.Frames.Count)
                beginOrdinal = 0;
            idleFrame = idleCycle.Animation.Frames[beginOrdinal];
        }

        List<TriMeshRef> flattened = [.. RigTriMesh.Flatten(rig, idleFrame)];
        IReadOnlyList<ObjectCreation.AnimPartSwap> animPieceEdits =
            canonSummon.AnimPartChanges ?? Array.Empty<ObjectCreation.AnimPartSwap>();

        foreach (ObjectCreation.AnimPartSwap edit in animPieceEdits)
        {
            if (edit.PartIndex < flattened.Count)
            {
                flattened[edit.PartIndex] = new TriMeshRef(
                    edit.NewModelId,
                    flattened[edit.PartIndex].PartTransform);
            }
        }

        uint[] impactPieceGfxObjRefIdents =
            OnlineActorContactAssembler.LocateNetPieceIdentities(
                flattened.Select(static piece => piece.GfxObjId).ToArray(),
                LocateImpactPiece);

        if (_knobs.RetailCloseDegrades && HasHumanoidNullPieceArrangement(rig))
            ImposeCanonShutDegrades(flattened);

        IReadOnlyList<ObjectCreation.TextureSwap> textureEdits =
            canonSummon.TextureChanges ?? Array.Empty<ObjectCreation.TextureSwap>();
        var canvasSubstitutions =
            LocateCanvasSubstitutions(
                flattened,
                textureEdits);

        float scaling = canonSummon.ObjScale ?? 1f;
        Matrix4x4 scalingMatrix = Matrix4x4.CreateScale(scaling);
        var rigidPieceXforms =
            SetupPartPoses.Compute(rig, idleFrame, scaling);
        List<TriMeshRef> triMeshRefs = new List<TriMeshRef>();
        Matrix4x4[] indexedPieceXforms = new Matrix4x4[flattened.Count];
        bool[] indexedPieceOnHand = new bool[flattened.Count];
        var movingPieceBlueprint = new OnlineMotionPartTemplate[flattened.Count];
        ExtentAccumulator limits = new ExtentAccumulator();

        for (int pieceOrdinal = 0; pieceOrdinal < flattened.Count; ++pieceOrdinal)
        {
            TriMeshRef part = flattened[pieceOrdinal];
            IReadOnlyDictionary<uint, uint>? substitutions = null;
            if (canvasSubstitutions?.TryGetValue(pieceOrdinal, out var located) == true)
                substitutions = located;

            Matrix4x4 xform = scaling == 1f
                ? part.PartTransform
                : part.PartTransform * scalingMatrix;
            indexedPieceXforms[pieceOrdinal] = pieceOrdinal < rigidPieceXforms.Count
                ? rigidPieceXforms[pieceOrdinal]
                : Matrix4x4.Identity;
            PartMesh? gfx = _datFiles.Get<PartMesh>(part.GfxObjId);
            bool drawable = gfx is not null;
            indexedPieceOnHand[pieceOrdinal] = drawable;
            movingPieceBlueprint[pieceOrdinal] = new OnlineMotionPartTemplate(
                part.GfxObjId,
                substitutions,
                drawable);
            if (gfx is null)

                continue;

            _impactHoldings.StashGfxObjRef(part.GfxObjId, gfx);

            if (GfxObjExtent.Get(gfx) is { } pieceLimits)
                limits.Add(xform, pieceLimits);
            triMeshRefs.Add(new TriMeshRef(part.GfxObjId, xform)
            {
                CanvasOverrides = substitutions,
            });
        }

        if (triMeshRefs.Count is 0)

            return false;

        var swatchOverride = BuildSwatchOverride(canonSummon);
        PartSwap[] pieceSubstitutions = BuildPieceSubstitutions(animPieceEdits);

        bool supersessionRecovery =
            purpose is OnlineMirrorPurpose.CreateSupersessionRecovery;
        if (supersessionRecovery
            && anticipatedCapture?.WorldEntity is not null)
        {
            return OnlineActorCreateSupersessionRecovery.TryApply(
                _runtime,
                anticipatedCapture,
                anticipatedBuildIntegrationVer,
                grabLooks: () => appearanceUpdate
                    ?? OnlineActorAppearanceWiring.Capture(
                        _runtime,
                        anticipatedCapture.ServerOid),
                broadcastLooks: visualRefresh => ImposeLooks(
                    anticipatedCapture,
                    canonSummon,
                    visualRefresh,
                    rig,
                    scaling,
                    realmOrigin,
                    impactPieceGfxObjRefIdents,
                    triMeshRefs,
                    swatchOverride,
                    pieceSubstitutions,
                    indexedPieceXforms,
                    indexedPieceOnHand,
                    movingPieceBlueprint,
                    limits,
                    anticipatedBuildIntegrationVer),
                broadcastLatestCapture: visualRefresh =>
                {
                    var capture = new MacAC.Extensibility.World.EntityFrame(
                        visualRefresh.Entity.Id,
                        visualRefresh.Entity.SrcGfxObjRefOrRigIdent,
                        visualRefresh.Entity.Position,
                        visualRefresh.Entity.Rotation);
                    _realmPhase.Add(capture);
                    _realmSignals.UpsertLatest(capture);
                },
                synchronizeAnim: visualRefresh => EnrollAnim(
                    anticipatedCapture,
                    visualRefresh.Entity,
                    rig,
                    canonSummon,
                    idleCycle,
                    scaling,
                    movingPieceBlueprint,
                    indexedPieceOnHand,
                    keptAnim:
                        anticipatedCapture.AnimationRuntime is OnlineActorMotionLedger,
                    synchronizeAnim: true));
        }

        return appearanceUpdate is { } visualUpdate
            ? ImposeLooks(
                anticipatedCapture!,
                canonSummon,
                visualUpdate,
                rig,
                scaling,
                realmOrigin,
                impactPieceGfxObjRefIdents,
                triMeshRefs,
                swatchOverride,
                pieceSubstitutions,
                indexedPieceXforms,
                indexedPieceOnHand,
                movingPieceBlueprint,
                limits,
                anticipatedBuildIntegrationVer)
            : MaterializeProj(
            anticipatedCanon,
            anticipatedCapture,
            canonSummon,
            rig,
            idleCycle,
            realmOrigin,
            realmLocus,
            spin,
            scaling,
            impactPieceGfxObjRefIdents,
            triMeshRefs,
            swatchOverride,
            pieceSubstitutions,
            indexedPieceXforms,
            indexedPieceOnHand,
            movingPieceBlueprint,
            limits,
            anticipatedBuildIntegrationVer,
            synchronizeAnim: supersessionRecovery);
    }
}
