using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Realm;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;
using MacAC.Wire;

namespace MacAC.Client.Graphics;

internal sealed partial class DatOnlineActorMirrorAssembler
{
    private bool MaterializeProj(
        SimActorRecord anticipatedCanon,
        OnlineActorRecord? keptCapture,
        RealmSession.MoverSpawn summon,
        RigSpec rig,
        GaitResolver.RestCycle? idleCycle,
        Vector3 realmOrigin,
        Vector3 realmLocus,
        Quaternion spin,
        float scaling,
        IReadOnlyList<uint> impactPieceGfxObjRefIdents,
        IReadOnlyList<TriMeshRef> triMeshRefs,
        SwatchOverride? swatchOverride,
        IReadOnlyList<PartSwap> pieceSubstitutions,
        IReadOnlyList<Matrix4x4> indexedPieceXforms,
        IReadOnlyList<bool> indexedPieceOnHand,
        IReadOnlyList<OnlineMotionPartTemplate> movingPieceBlueprint,
        ExtentAccumulator limits,
        ulong anticipatedBuildIntegrationVer,
        bool synchronizeAnim)
    {
        ActorEffectProfile profile = summon.Physics is { } kinetics
            ? ActorEffectProfile.BuildOnline(rig, kinetics)
            : ActorEffectProfile.BuildDatStatic(rig);
        if (keptCapture is not null
            && !_runtime.TryFetchFxProfile(summon.Guid, out _))

            _runtime.AssignFxProfile(summon.Guid, profile);

        bool builtProj = false;
        OnlineActorMaterializationResidence residence =
            keptCapture?.MaterializationResidence
            ?? OnlineActorMaterializationResidence.AwaitRuntimePlacement;
        var actor = _runtime.MaterializeOnlineActor(
            anticipatedCanon,
            summon.Position!.Value.LandblockId,
            ownIdent =>
            {
                builtProj = true;
                RealmActor built = new RealmActor
                {
                    Id = ownIdent,
                    ServerGuid = summon.Guid,
                    SrcGfxObjRefOrRigIdent = summon.SetupTableId!.Value,
                    Position = realmLocus,
                    Rotation = spin,
                    MeshRefs = triMeshRefs,
                    SwatchOverride = swatchOverride,
                    PieceSubstitutions = pieceSubstitutions,
                    ParentCellId = summon.Position.Value.LandblockId,
                };
                built.AssignIndexedPiecePostures(indexedPieceXforms, indexedPieceOnHand);
                if (limits.TryGet(out Vector3 floor, out Vector3 ceiling))
                    built.AssignOwnLimits(floor, ceiling);
                return built;
            },
            OnlineActorMirrorKind.World,
            bootstrapProj: capture => capture.EffectProfile = profile,
            out OnlineActorRecord? anticipatedCapture,
            residence);
        if (actor is null
            || anticipatedCapture is null
            || !_runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || !ReferenceEquals(anticipatedCapture.WorldEntity, actor))

            return false;
        if (residence is OnlineActorMaterializationResidence.AwaitRuntimePlacement
            && anticipatedCanon.WholeChamberTag is not 0u
            && !_runtime.HasEngagedStartingBuildResidence(anticipatedCanon))
        {
            if (!_runtime.RebucketLiveEntity(
                    summon.Guid,
                    anticipatedCanon.WholeChamberTag)
                || !_runtime.IsLatestBuildIntegration(
                    anticipatedCapture,
                    anticipatedBuildIntegrationVer)
                || !ReferenceEquals(anticipatedCapture.WorldEntity, actor))

                return false;
        }

        if (!builtProj)
        {
            actor.SetPosition(realmLocus);
            actor.Rotation = spin;
            actor.ParentCellId = summon.Position.Value.LandblockId;
            actor.ImposeLooks(triMeshRefs, swatchOverride, pieceSubstitutions);
            actor.AssignIndexedPiecePostures(indexedPieceXforms, indexedPieceOnHand);
            if (limits.TryGet(out Vector3 floor, out Vector3 ceiling))
                actor.AssignOwnLimits(floor, ceiling);
            _fxPostures.BroadcastTriMeshRefs(actor);
        }

        bool keptAnim = !builtProj
            && anticipatedCapture.AnimationRuntime is OnlineActorMotionLedger;
        var snapshot = new MacAC.Extensibility.World.EntityFrame(
            actor.Id,
            actor.SrcGfxObjRefOrRigIdent,
            actor.Position,
            actor.Rotation);
        _realmPhase.Add(snapshot);
        _realmSignals.UpsertLatest(snapshot);
        if (_runtime.TryFlagRealmSummonPublished(summon.Guid))
            _realmSignals.TriggerActorSpawned(snapshot);

        if (!_runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || !ReferenceEquals(anticipatedCapture.WorldEntity, actor))

            return false;

        _equippedDescendants.OnRealmActorRegistered(summon.Guid);

        if (!_runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || !ReferenceEquals(anticipatedCapture.WorldEntity, actor))

            return false;

        if (_impactBuilder.Build(
                actor,
                rig,
                impactPieceGfxObjRefIdents,
                summon,
                anticipatedCapture.ServerOid,
                anticipatedCapture.Generation,
                anticipatedCapture.WorldEntity!,
                anticipatedCapture.FinalKineticsPhase,
                realmOrigin) is { } impact)

            OnlineActorContactAssembler.Register(_shades, impact);

        if (!_runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || !ReferenceEquals(anticipatedCapture.WorldEntity, actor))
            return false;
        bool startingResidenceEngaged =
            _runtime.HasEngagedStartingBuildResidence(anticipatedCanon);
        if (!startingResidenceEngaged)
        {
            _missiles.TryAttach(
                anticipatedCapture,
                rig,
                _gameTime.LatestProgramMoment,
                _origin.CenterX,
                _origin.CenterY);
        }

        if (!_runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || !ReferenceEquals(anticipatedCapture.WorldEntity, actor))

            return false;

        EnrollAnim(
            anticipatedCapture,
            actor,
            rig,
            summon,
            idleCycle,
            scaling,
            movingPieceBlueprint,
            indexedPieceOnHand,
            keptAnim,
            synchronizeAnim);

        return !_runtime.IsLatestBuildIntegration(
                anticipatedCapture,
                anticipatedBuildIntegrationVer)
            || !ReferenceEquals(anticipatedCapture.WorldEntity, actor)
            ? false
            : _runtime.IsLatestBuildIntegration(
            anticipatedCapture,
            anticipatedBuildIntegrationVer);
    }
}
