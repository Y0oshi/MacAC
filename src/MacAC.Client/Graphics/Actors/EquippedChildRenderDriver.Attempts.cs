using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;
using MacAC.Wire;

namespace MacAC.Client.Graphics;

public sealed partial class EquippedChildRenderDriver
{
    internal bool TryEnactAffixedLooks(
        OnlineActorRecord anticipatedCapture,
        ulong anticipatedObjRefDscArbiterVer)
    {
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        lock (_datMutex)
        {
            if (!_onlineActors.IsLatestObjRefDscArbiter(
                    anticipatedCapture,
                    anticipatedObjRefDscArbiterVer)
                || anticipatedCapture.ProjSort is not
                    OnlineActorMirrorKind.Attached
                || !anticipatedCapture.IsSpatiallyProjected
                || anticipatedCapture.WorldEntity is not { } anticipatedActor
                || !Relations.ReinstatePreviousApproved(anticipatedCapture.ServerOid))

                return false;

            bool projected = LocateAndTryRealize(anticipatedCapture.ServerOid);
            if (projected)

                ReattemptWaitingDescendants(anticipatedCapture.ServerOid);
            return projected
                && _onlineActors.IsLatestObjRefDscArbiter(
                    anticipatedCapture,
                    anticipatedObjRefDscArbiterVer)
                && anticipatedCapture.ProjSort is
                    OnlineActorMirrorKind.Attached
                && anticipatedCapture.IsSpatiallyProjected
                && ReferenceEquals(anticipatedCapture.WorldEntity, anticipatedActor);
        }
    }

    private bool TryRealize(
        AnchorAttachmentRelation queued,
        AnchorMirrorCandidateKind contenderSort)
    {
        uint descendantOid = queued.ChildGuid;

        if (!_onlineActors.TryFetchRecord(queued.ParentGuid, out OnlineActorRecord ancestorCapture)
            || ancestorCapture.WorldEntity is not { } ancestorActor
            || !ancestorCapture.IsSpatiallyProjected
            || !_onlineActors.TryFetchCanon(
                descendantOid,
                out SimActorRecord descendantCanon)
            || !_onlineActors.TryGetCapture(queued.ParentGuid, out RealmSession.MoverSpawn ancestorSummon)
            || !_onlineActors.TryGetCapture(descendantOid, out RealmSession.MoverSpawn descendantSummon))
            return false;

        if (ancestorSummon.SetupTableId is not { } ancestorRigIdent
            || descendantSummon.SetupTableId is not { } descendantRigIdent
            || ancestorActor.ParentCellId is not { } ancestorChamberIdent)
            return false;

        RigSpec? ancestorRig = _datFiles.Get<RigSpec>(ancestorRigIdent);
        RigSpec? descendantRig = _datFiles.Get<RigSpec>(descendantRigIdent);
        if (ancestorRig is null || descendantRig is null)
            return false;

        AttachSlot ancestorLocale = (AttachSlot)queued.ParentLocation;
        PlacementId stance = (PlacementId)queued.PlacementId;
        var blueprint = AssemblePieceBlueprint(descendantRig, descendantSummon);
        bool[] descendantPieceReadiness = AssemblePieceReadiness(blueprint);
        float scaling = descendantSummon.ObjScale is { } objRefScaling && objRefScaling > 0f
            ? objRefScaling
            : 1.0f;
        if (!_postures.TryFetchPiecePostureCapture(
                ancestorActor.Id,
                out var ancestorPiecePostures,
                out var ancestorPieceReadiness))
            return false;
        if (!_postures.TryFetchTrunkPosture(ancestorActor.Id, out Matrix4x4 ancestorRealm)
            || !TryDecomposeRealmPosture(ancestorRealm, out Vector3 ancestorLocus, out Quaternion ancestorSpin))

            return false;

        if (!WornChildMount.TryConstructPostureInto(
                ancestorRig,
                ancestorPiecePostures,
                ancestorPieceReadiness,
                descendantRig,
                ancestorLocale,
                stance,
                blueprint,
                scaling,
                piecePostureBuf: null,
                affixedPieceBuf: null,
                out WornChildPose posture))
            return false;

        // A parented object may materialize here before the ordinary world hydration path sees it.
        var fxProfile = descendantSummon.Physics is { } kinetics
            ? Effects.ActorEffectProfile.BuildOnline(descendantRig, kinetics)
            : Effects.ActorEffectProfile.BuildDatStatic(descendantRig);
        if (_onlineActors.TryFetchProj(
                descendantCanon,
                out OnlineActorRecord? keptDescendant)
            && !_onlineActors.TryFetchFxProfile(descendantOid, out _))

            _onlineActors.AssignFxProfile(descendantOid, fxProfile);

        if (!_onlineActors.IsLatestCapture(ancestorCapture)
            || !_onlineActors.IsLatestCanon(descendantCanon)
            || !Relations.IsPending(queued, contenderSort))

            return false;
        if (keptDescendant is not null
            && _onlineActors.IsLatestCapture(keptDescendant))
        {
            _onlineActors.TranslateMaterializationResidenceToLegacyImmediate(
                keptDescendant);
        }
        var actor = _onlineActors.MaterializeOnlineActor(
            descendantCanon,
            ancestorChamberIdent,
            ownIdent =>
            {
                RealmActor built = new RealmActor
                {
                    Id = ownIdent,
                    ServerGuid = descendantOid,
                    SrcGfxObjRefOrRigIdent = descendantRigIdent,
                    Position = ancestorLocus,
                    Rotation = ancestorSpin,
                    MeshRefs = posture.AttachedParts,
                    SwatchOverride = AssembleSwatchOverride(descendantSummon),
                    ParentCellId = ancestorChamberIdent,
                };
                built.AssignIndexedPiecePostures(posture.PartLocal, descendantPieceReadiness);
                return built;
            },
            OnlineActorMirrorKind.Attached,
            bootstrapProj: capture => capture.EffectProfile = fxProfile,
            out OnlineActorRecord? descendantCapture);
        if (actor is null || descendantCapture is null)
            return false;
        if (!_onlineActors.IsLatestCapture(ancestorCapture)
            || !_onlineActors.IsLatestCapture(descendantCapture)
            || !ReferenceEquals(descendantCapture.WorldEntity, actor)
            || !Relations.IsPending(queued, contenderSort))
        {
            if (_onlineActors.IsLatestCapture(descendantCapture)
                && ReferenceEquals(descendantCapture.WorldEntity, actor)
                && descendantCapture.IsSpatiallyProjected)
            {
                BeginProjectionSubtreeWithdrawal(
                    _pendingOrphanRemovalByChild,
                    descendantCapture,
                    revertTrunkRelation: false,
                    revertDescendantRelations: false);
            }
            return false;
        }
        descendantCapture.HasPieceArr = true;
        ImposeAncestorRealmPosture(actor, ancestorRealm);
        ImposeAncestorPaintVis(actor, ancestorActor);
        actor.ParentCellId = ancestorChamberIdent;
        actor.ImposeLooks(
            posture.AttachedParts,
            AssembleSwatchOverride(descendantSummon),
            descendantSummon.AnimPartChanges is { Count: > 0 } edits
                ? edits.Select(edit => new PartSwap(edit.PartIndex, edit.NewModelId)).ToArray()
                : []);
        actor.AssignIndexedPiecePostures(posture.PartLocal, descendantPieceReadiness);
        AffixedDescendant affixed = new AffixedDescendant(
            ancestorCapture,
            descendantCapture,
            queued.ParentGuid,
            descendantOid,
            ancestorLocale,
            stance,
            ancestorRig,
            descendantRig,
            blueprint,
            descendantPieceReadiness,
            posture.PartLocal,
            posture.AttachedParts,
            scaling,
            actor);
        GrabAncestorExhibit(affixed, ancestorActor);
        SimActorKey descendantTag = DemandProjTag(descendantCapture);
        _attachedByChild[descendantTag] = affixed;
        _shades.FastenDescendant(
            actor.Id,
            ancestorActor.Id,
            AssembleDescendantRasterizePieces(descendantRig, blueprint, scaling));
        _pendingUnparentByChild.Remove(descendantTag);
        if ((ancestorCapture.FinalKineticsPhase & KineticStateFlags.Hidden) != 0)
        {
            _onlineActors.AssignAffixedDescendantNoPaint(descendantOid, noPaint: true);
        }
        BroadcastDescendantPosture(actor, ancestorRealm, ancestorActor.ParentCellId, posture);
        Console.WriteLine(
            $"equipment: attached child=0x{descendantOid:X8} parent=0x{queued.ParentGuid:X8} " +
            $"location={ancestorLocale} placement={stance}");
        Relations.FlagProjected(queued, contenderSort);
        var primedContender =
            OnlineActorReadyCandidate.Capture(descendantCapture);
        ProjectionPoseReady?.Invoke(descendantOid);
        return BroadcastActorPrimedPrecise(
            _onlineActors,
            primedContender,
            EntityReady);
    }

    private static bool TryDecomposeRealmPosture(
        Matrix4x4 realm,
        out Vector3 locus,
        out Quaternion spin)
    {
        locus = realm.Translation;
        if (!Matrix4x4.Decompose(realm, out Vector3 scaling, out spin, out _)
            || Vector3.DistanceSquared(scaling, Vector3.One) > 1e-6f)
            return false;
        spin = Quaternion.Normalize(spin);
        return true;
    }

    private bool TryLocatePreciseAffix(
        AffixedDescendant descendant,
        out RealmActor ancestor)
    {
        if (_onlineActors.IsLatestCapture(descendant.ParentRecord)
            && _onlineActors.IsLatestCapture(descendant.ChildRecord)
            && descendant.ParentRecord.IsSpatiallyProjected
            && descendant.ChildRecord.IsSpatiallyProjected
            && descendant.ParentRecord.WorldEntity is { } preciseAncestor
            && descendant.ChildRecord.WorldEntity is { } preciseDescendant
            && ReferenceEquals(preciseDescendant, descendant.Entity))
        {
            ancestor = preciseAncestor;
            return true;
        }
        ancestor = null!;
        return false;
    }
}
