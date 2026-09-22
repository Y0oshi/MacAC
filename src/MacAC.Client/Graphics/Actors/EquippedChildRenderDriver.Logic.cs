using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics;

public sealed partial class EquippedChildRenderDriver
{
    private AnchorAttachmentLedger Relations => _onlineActors.AncestorAffixes;

    internal int PreviousWholePostureCompositionVisits { get; private set; }

    internal int PreviousReconcilePostureCompositionVisits { get; private set; }

    public IEnumerable<uint> AffixedActorIdents
    {
        get
        {
            foreach (AffixedDescendant descendant in _attachedByChild.Values)
                yield return descendant.Entity.Id;
        }
    }

    public void ReconcileSpatialMutations()
    {
        ReattemptQueuedProjChangeovers();
        _engagedPostureCompositionVisits = 0;
        var failed = _refreshOrdering.ForEachAncestorLead(
            _attachedByChild,
            _ancestorOfAffixed,
            _reconcileAffixed);
        PreviousReconcilePostureCompositionVisits = _engagedPostureCompositionVisits;
        for (int idx = 0; idx < failed.Count; ++idx)
            WithdrawForPostureLoss(failed[idx]);
    }

    public void Tick()
    {
        ReattemptQueuedProjChangeovers();
        _engagedPostureCompositionVisits = 0;
        var failed = _refreshOrdering.ForEachAncestorLead(
            _attachedByChild,
            _ancestorOfAffixed,
            _beatAffixed);
        PreviousWholePostureCompositionVisits = _engagedPostureCompositionVisits;
        for (int idx = 0; idx < failed.Count; ++idx)
            WithdrawForPostureLoss(failed[idx]);
    }

    public uint? SeekDescendantOwnIdentAtPiece(uint ancestorOwnIdent, uint pieceOrdinal)
    {
        foreach (AffixedDescendant descendant in _attachedByChild.Values)
        {
            if (!TryLocatePreciseAffix(descendant, out RealmActor ancestor)
                || ancestor.Id != ancestorOwnIdent
                || !descendant.ParentSetup.HoldingSlots.TryGetValue(
                    descendant.ParentLocation,
                    out AttachPoint? holding)
                || holding.PartIndex != pieceOrdinal)

                continue;
            return descendant.Entity.Id;
        }
        return null;
    }

    public uint? SeekAncestorOwnIdent(uint descendantOwnIdent)
    {
        foreach (AffixedDescendant descendant in _attachedByChild.Values)
        {
            if (descendant.Entity.Id != descendantOwnIdent)
                continue;
            return TryLocatePreciseAffix(descendant, out RealmActor ancestor) ? ancestor.Id : null;
        }
        return null;
    }

    public void AssignStraightDescendantsNoPaint(uint ancestorOid, bool noPaint)
    {
        foreach (AffixedDescendant descendant in _attachedByChild.Values)
        {
            if (descendant.ParentGuid == ancestorOid
                && TryLocatePreciseAffix(descendant, out _))
                _onlineActors.AssignAffixedDescendantNoPaint(descendant.ChildGuid, noPaint);
        }
    }

    public void Clear()
    {
        AffixedDescendant[] affixed = [.. _attachedByChild.Values];
        for (int idx = 0; idx < affixed.Length; ++idx)
            SealProjDeletion(affixed[idx]);
        _pendingUnparentByChild.Clear();
        _pendingOrdinaryRemovalByRoot.Clear();
        _pendingDetachedRemovalByChild.Clear();
        _pendingReparentRemovalByChild.Clear();
        _pendingPoseLossRemovalByChild.Clear();
        _pendingOrphanRemovalByChild.Clear();
        _loggedUnaddressableAncestorRefusals.Clear();
        Relations.Clear();
    }

    public void Dispose()
    {
        Clear();
        _objects.ObjectMoved -= OnObjectMoved;
        _objects.MoveRolledBack -= OnRelocateRolledBack;
        _objects.ObjectRemovalClassified -= OnObjectDeletionClassified;
    }

    internal static ExactMirrorWithdrawalVerdict WithdrawAffixedProj(
        OnlineActorRecord descendantCapture,
        ulong locusArbiterVer,
        ulong projAlterationVer,
        Func<OnlineActorRecord, ulong, ulong, ExactMirrorWithdrawalVerdict>
            withdrawProj,
        Func<bool> sealDeletion)
    {
        ArgumentNullException.ThrowIfNull(descendantCapture);
        ArgumentNullException.ThrowIfNull(withdrawProj);
        ArgumentNullException.ThrowIfNull(sealDeletion);
        var verdict = withdrawProj(
            descendantCapture,
            locusArbiterVer,
            projAlterationVer);
        if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Pending)
            return verdict;
        try
        {
            sealDeletion();
            return verdict;
        }
        catch (Exception problem)
        {
            return new ExactMirrorWithdrawalVerdict(
                verdict.Disposition,
                verdict.Failure is null
                    ? problem
                    : new AggregateException(verdict.Failure, problem));
        }
    }

    internal static bool BroadcastActorPrimedPrecise(
        OnlineActorCore core,
        OnlineActorReadyCandidate contender,
        Action<OnlineActorReadyCandidate>? broadcast)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (!contender.IsLatest(core))
            return false;

        broadcast?.Invoke(contender);
        return contender.IsLatest(core);
    }

    internal static bool ImposeAncestorRealmPosture(RealmActor descendant, Matrix4x4 ancestorRealm)
    {
        if (!TryDecomposeRealmPosture(ancestorRealm, out Vector3 locus, out Quaternion spin))
            return false;
        descendant.SetPosition(locus);
        descendant.Rotation = spin;
        return true;
    }

    internal static void ImposeAncestorPaintVis(RealmActor descendant, RealmActor ancestor)
    {
        descendant.IsAncestorPaintShown =
            ancestor.IsPaintShown && ancestor.IsAncestorPaintShown;
    }

    internal ParentMirrorAuditDisposition VetAncestorProj(
        AnchorAttachmentRelation relation)
    {
        if (relation.ParentGuid == relation.ChildGuid)
            return ParentMirrorAuditDisposition.Rejected;
        if (!_onlineActors.TryFetchRecord(relation.ParentGuid, out OnlineActorRecord ancestor)
            || !_onlineActors.TryFetchCanon(relation.ChildGuid, out _)
            || ancestor.WorldEntity is null
            || !ancestor.HasPieceArr)

            return ParentMirrorAuditDisposition.Waiting;
        if (!_onlineActors.TryGetCapture(
                relation.ParentGuid,
                out RealmSession.MoverSpawn ancestorSummon)
            || ancestorSummon.SetupTableId is not { } ancestorRigIdent)

            return ParentMirrorAuditDisposition.Rejected;
        RigSpec? ancestorRig = _datFiles.Get<RigSpec>(ancestorRigIdent);
        return ancestorRig is not null
            && ancestorRig.HoldingSlots.ContainsKey(
                (AttachSlot)relation.ParentLocation)
                ? ParentMirrorAuditDisposition.Ready
                : ParentMirrorAuditDisposition.Rejected;
    }

    private void ReattemptQueuedProjChangeovers()
    {
        DuplicateListings(
            _pendingOrdinaryRemovalByRoot,
            _queuedPlainDeletionTemp);
        for (int idx = 0; idx < _queuedPlainDeletionTemp.Count; ++idx)
        {
            (SimActorKey trunkTag, QueuedPlainDeletion queued) =
                _queuedPlainDeletionTemp[idx];
            ProgressPlainDeletion(trunkTag, queued);
        }

        DuplicateListings(
            _pendingDetachedRemovalByChild,
            _queuedProjSubtreeTemp);
        for (int idx = 0; idx < _queuedProjSubtreeTemp.Count; ++idx)
        {
            (SimActorKey descendantTag, PendingMirrorSubtree queued) =
                _queuedProjSubtreeTemp[idx];
            ProgressDetachedDeletion(descendantTag, queued);
        }

        ReattemptProjSubtrees(_pendingReparentRemovalByChild);
        ReattemptProjSubtrees(_pendingPoseLossRemovalByChild);
        ReattemptProjSubtrees(_pendingOrphanRemovalByChild);

        DuplicateListings(_pendingUnparentByChild, _queuedUnparentTemp);
        for (int idx = 0; idx < _queuedUnparentTemp.Count; ++idx)
        {
            (SimActorKey descendantTag, QueuedUnparentChangeover queued) =
                _queuedUnparentTemp[idx];
            if (!_onlineActors.IsLatestCapture(queued.Record)
                || queued.Record.PositionAuthorityVersion
                    != queued.PositionAuthorityVersion)
            {
                _pendingUnparentByChild.Remove(descendantTag);
                continue;
            }
            ProgressUnparentChangeover(descendantTag, queued);
        }

        Relations.DuplicateQueuedProjDescendantsTo(_queuedProjDescendantsTemp);
        for (int idx = 0; idx < _queuedProjDescendantsTemp.Count; ++idx)
            LocateAndTryRealize(_queuedProjDescendantsTemp[idx]);
    }

    private bool SettleDescendant(SimActorKey descendantTag)
    {
        return !_attachedByChild.TryGetValue(descendantTag, out AffixedDescendant? descendant)
            || !TryLocatePreciseAffix(descendant, out RealmActor ancestor)
            ? false
            : AncestorExhibitFits(descendant, ancestor) || PulseDescendant(descendantTag);
    }

    private bool AncestorExhibitFits(
        AffixedDescendant descendant,
        RealmActor ancestor)
    {
        return ReferenceEquals(descendant.PreviousAncestorActor, ancestor)
               && descendant.PreviousAncestorPostureVer
                    == _postures.FetchPostureEditVer(ancestor.Id)
               && descendant.PreviousAncestorPaintShown == ancestor.IsPaintShown
               && descendant.PreviousParentAncestorDrawVisible
                    == ancestor.IsAncestorPaintShown
               && descendant.PreviousAncestorChamberIdent == ancestor.ParentCellId;
    }

    private void ReattemptWaitingDescendants(uint ancestorOid)
    {
        _relationRecoveryOrdering.RealizeDescendants(
            ancestorOid,
            Relations.DescendantsWaitingForAncestor,
            LocateAndTryRealize);
    }

    private bool WithdrawPrecedingProj(OnlineActorRecord descendantCapture)
    {
        SimActorKey tag = DemandProjTag(descendantCapture);
        return _pendingReparentRemovalByChild.TryGetValue(
                tag,
                out PendingMirrorSubtree queued)
            ? ProgressProjSubtree(
                _pendingReparentRemovalByChild,
                tag,
                queued)
            : BeginProjectionSubtreeWithdrawal(
            _pendingReparentRemovalByChild,
            descendantCapture,
            revertTrunkRelation: false,
            revertDescendantRelations: true);
    }

    private bool SealProjDeletion(AffixedDescendant descendant)
    {
        SimActorKey tag = DemandProjTag(descendant.ChildRecord);
        if (!_attachedByChild.TryGetValue(tag, out AffixedDescendant? latest)
            || !ReferenceEquals(latest, descendant))

            return false;

        _attachedByChild.Remove(tag);
        _shades.UnfastenDescendant(descendant.Entity.Id);
        ProjectionRemoved?.Invoke(descendant.ChildRecord);
        return true;
    }

    private void WithdrawForPostureLoss(SimActorKey descendantTag)
    {
        if (!_onlineActors.TryFetchCapture(
                descendantTag,
                out OnlineActorRecord capture))
            return;
        BeginProjectionSubtreeWithdrawal(
            _pendingPoseLossRemovalByChild,
            capture,
            revertTrunkRelation: true,
            revertDescendantRelations: true);
    }

    private void TearDownCurrentObjectProjections(uint oid)
    {
        if (_onlineActors.TryFetchRecord(oid, out OnlineActorRecord capture))
        {
            SimActorKey tag = DemandProjTag(capture);
            var captures = GrabCaptureSubtreeAncestorLead(capture);
            ProgressPlainDeletion(
                tag,
                new QueuedPlainDeletion(capture, captures, NextIndex: 0));
            return;
        }

        var stale = _attachedByChild.Values.FirstOrDefault(
            contender => contender.ChildGuid == oid);
        if (stale is not null)
            SealProjDeletion(stale);
        Relations.FinishDescendantProj(oid);
    }

    private void TearDownCaptureProjections(OnlineActorRecord capture)
    {
        SimActorKey tag = DemandProjTag(capture);
        if (_pendingUnparentByChild.TryGetValue(tag, out var queued)
            && ReferenceEquals(queued.Record, capture))

            _pendingUnparentByChild.Remove(tag);
        if (_pendingOrdinaryRemovalByRoot.TryGetValue(
                tag,
                out QueuedPlainDeletion plain)
            && ReferenceEquals(plain.RootRecord, capture))

            _pendingOrdinaryRemovalByRoot.Remove(tag);
        DropQueuedProjSubtree(_pendingDetachedRemovalByChild, capture);
        DropQueuedProjSubtree(_pendingReparentRemovalByChild, capture);
        DropQueuedProjSubtree(_pendingPoseLossRemovalByChild, capture);
        DropQueuedProjSubtree(_pendingOrphanRemovalByChild, capture);
        var subtree = GrabCaptureSubtreeAncestorLead(capture);
        for (int idx = 0; idx < subtree.Count; ++idx)
        {
            var grabbed = subtree[idx];
            var descendant = grabbed.Attached;
            if (ReferenceEquals(descendant.ChildRecord, capture))
            {
                SealProjDeletion(descendant);
                continue;
            }
            var verdict = WithdrawGrabbed(grabbed);
            if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Pending)
            {
                if (verdict.Failure is not null)
                    throw verdict.Failure;
                throw new InvalidOperationException(
                    $"Attached projection 0x{descendant.ChildGuid:X8} remains pending exact teardown of 0x{capture.ServerOid:X8}.");
            }
            if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Completed)
                Relations.ReinstatePreviousApproved(descendant.ChildGuid);
            if (verdict.Failure is not null)
                throw verdict.Failure;
        }
    }

    private ExactMirrorWithdrawalVerdict WithdrawGrabbed(
        AffixedDeletionGrab grabbed)
    {
        SimActorKey tag = DemandProjTag(
            grabbed.Attached.ChildRecord);
        return !_attachedByChild.TryGetValue(
                tag,
                out AffixedDescendant? latest)
            || !ReferenceEquals(latest, grabbed.Attached)
            ? new ExactMirrorWithdrawalVerdict(
                ExactMirrorWithdrawalDisposition.Superseded,
                Failure: null)
            : WithdrawAffixedProj(
            grabbed.Attached.ChildRecord,
            grabbed.PositionAuthorityVersion,
            grabbed.ProjectionMutationVersion,
            _withdrawProj,
            () => SealProjDeletion(grabbed.Attached));
    }

    private ExactMirrorWithdrawalVerdict WithdrawProjGrab(
        MirrorRemovalCapture grabbed)
    {
        if (grabbed.Attached is { } affixed)
        {
            return WithdrawGrabbed(new AffixedDeletionGrab(
                affixed,
                grabbed.PositionAuthorityVersion,
                grabbed.ProjectionMutationVersion));
        }
        if (!_onlineActors.IsLatestCapture(grabbed.Record)
            || grabbed.Record.PositionAuthorityVersion
                != grabbed.PositionAuthorityVersion
            || grabbed.Record.ProjAlterationVer
                != grabbed.ProjectionMutationVersion)
        {
            return new(
                ExactMirrorWithdrawalDisposition.Superseded,
                Failure: null);
        }
        return !grabbed.Record.IsSpatiallyProjected
            ? new(
                ExactMirrorWithdrawalDisposition.Completed,
                Failure: null)
            : _withdrawProj(
            grabbed.Record,
            grabbed.PositionAuthorityVersion,
            grabbed.ProjectionMutationVersion);
    }

    private void ReattemptProjSubtrees(
        Dictionary<SimActorKey, PendingMirrorSubtree> queuedByTrunk)
    {
        DuplicateListings(queuedByTrunk, _queuedProjSubtreeTemp);
        for (int idx = 0; idx < _queuedProjSubtreeTemp.Count; ++idx)
        {
            (SimActorKey trunkTag, PendingMirrorSubtree queued) =
                _queuedProjSubtreeTemp[idx];
            ProgressProjSubtree(queuedByTrunk, trunkTag, queued);
        }
    }

    private static SimActorKey DemandProjTag(
        OnlineActorRecord capture)
    {
        return capture.ProjTag
        ?? throw new InvalidOperationException(
            $"Live entity 0x{capture.ServerOid:X8}/{capture.Generation} " +
            "has no exact projection key");
    }

    private bool PulseDescendant(SimActorKey descendantTag)
    {
        if (!_attachedByChild.TryGetValue(descendantTag, out AffixedDescendant? descendant))
            return false;

        ++_engagedPostureCompositionVisits;
        if (TryLocatePreciseAffix(descendant, out RealmActor ancestor)
            && _postures.TryFetchTrunkPosture(ancestor.Id, out Matrix4x4 ancestorRealm)
            && _postures.TryFetchPiecePostureCapture(
                ancestor.Id,
                out var ancestorPiecePostures,
                out var ancestorPieceReadiness)
            && WornChildMount.TryConstructPostureInto(
                descendant.ParentSetup,
                ancestorPiecePostures,
                ancestorPieceReadiness,
                descendant.ChildSetup,
                descendant.ParentLocation,
                descendant.Placement,
                descendant.PartTemplate,
                descendant.Scale,
                descendant.PartPoseBuffer,
                descendant.AttachedPartBuffer,
                out WornChildPose posture))
        {
            descendant.Entity.MeshRefs = posture.AttachedParts;
            descendant.Entity.AssignIndexedPiecePostures(posture.PartLocal, descendant.PartAvailability);
            if (!ImposeAncestorRealmPosture(descendant.Entity, ancestorRealm))
                return false;
            ImposeAncestorPaintVis(descendant.Entity, ancestor);
            descendant.Entity.ParentCellId = ancestor.ParentCellId;
            GrabAncestorExhibit(descendant, ancestor);
            BroadcastDescendantPosture(descendant.Entity, ancestorRealm, ancestor.ParentCellId, posture);
            if (TryLocatePreciseAffix(descendant, out ancestor)
                && ancestor.ParentCellId is { } ancestorChamberIdent)
            {
                var disposition =
                    _onlineActors.RebucketEquippedDescendantExhibit(
                        descendant.ChildGuid,
                        ancestorChamberIdent);
                if (disposition is
                    EquippedChildDisplayRebucketDisposition.NoProjection)

                    return false;
            }
            ProjectionPoseReady?.Invoke(descendant.ChildGuid);
            return true;
        }
        return false;
    }

    private void GrabAncestorExhibit(
        AffixedDescendant descendant,
        RealmActor ancestor)
    {
        descendant.PreviousAncestorActor = ancestor;
        descendant.PreviousAncestorPostureVer = _postures.FetchPostureEditVer(ancestor.Id);
        descendant.PreviousAncestorPaintShown = ancestor.IsPaintShown;
        descendant.PreviousParentAncestorDrawVisible = ancestor.IsAncestorPaintShown;
        descendant.PreviousAncestorChamberIdent = ancestor.ParentCellId;
    }

    private List<AffixedDeletionGrab> GrabCaptureSubtreeAncestorLead(
        OnlineActorRecord capture)
    {
        var outcome = new List<AffixedDeletionGrab>();
        var visited = new HashSet<OnlineActorRecord>(ReferenceEqualityComparer.Instance);
        if (capture.ProjTag is { } tag
            && _attachedByChild.TryGetValue(tag, out AffixedDescendant? trunk)
            && ReferenceEquals(trunk.ChildRecord, capture))

            outcome.Add(Capture(trunk));
        GatherDescendantsAncestorLead(capture, outcome, visited);
        return outcome;
    }

    private static AffixedDeletionGrab Capture(AffixedDescendant descendant)
    {
        return new(
        descendant,
        descendant.ChildRecord.PositionAuthorityVersion,
        descendant.ChildRecord.ProjAlterationVer);
    }

    private PendingMirrorSubtree GrabProjSubtreeAncestorLead(
        OnlineActorRecord trunk,
        bool revertTrunkRelation,
        bool revertDescendantRelations)
    {
        var captures = new List<MirrorRemovalCapture>();
        var visited = new HashSet<OnlineActorRecord>(ReferenceEqualityComparer.Instance);
        GrabProjJoint(trunk, isTrunk: true, captures, visited);
        return new PendingMirrorSubtree(
            trunk,
            trunk.PositionAuthorityVersion,
            captures,
            NextIndex: 0,
            revertTrunkRelation,
            revertDescendantRelations);
    }

    private void GrabProjJoint(
        OnlineActorRecord capture,
        bool isTrunk,
        List<MirrorRemovalCapture> dest,
        HashSet<OnlineActorRecord> visited)
    {
        if (!visited.Add(capture))
            return;
        AffixedDescendant? affixed = null;
        if (capture.ProjTag is { } tag)
            _attachedByChild.TryGetValue(tag, out affixed);
        if (affixed is not null && !ReferenceEquals(affixed.ChildRecord, capture))
            affixed = null;
        if (capture.IsSpatiallyProjected || affixed is not null)
        {
            dest.Add(new MirrorRemovalCapture(
                capture,
                capture.PositionAuthorityVersion,
                capture.ProjAlterationVer,
                affixed,
                isTrunk));
        }

        AffixedDescendant[] descendants = [.. _attachedByChild.Values.Where(descendant => ReferenceEquals(descendant.ParentRecord, capture))];
        for (int idx = 0; idx < descendants.Length; ++idx)
        {
            GrabProjJoint(
                descendants[idx].ChildRecord,
                isTrunk: false,
                dest,
                visited);
        }
    }

    private void BroadcastDescendantPosture(
        RealmActor descendant,
        Matrix4x4 ancestorRealm,
        uint? ancestorChamberIdent,
        WornChildPose posture)
    {
        _postures.Publish(
            descendant.Id,
            posture.RootLocal * ancestorRealm,
            posture.PartLocal,
            ancestorChamberIdent ?? 0u,
            descendant.IndexedPieceOnHand);
    }

    private void AdmitLateTiedBuildObjectRelation(
        uint ancestorOid,
        uint descendantOid,
        uint ancestorLocale,
        uint stanceIdent,
        ushort descendantLocusSeries)
    {
        if (_onlineActors.TryGetCapture(ancestorOid, out RealmSession.MoverSpawn ancestorSummon))
        {
            Relations.AdmitBuildObjectRelation(new AnchorAttachmentRelation(
                ancestorOid,
                descendantOid,
                ancestorLocale,
                stanceIdent,
                ancestorSummon.InstanceSequence,
                descendantLocusSeries));
        }
        else if (_loggedUnaddressableAncestorRefusals.Add(descendantOid))
        {
            Console.Error.WriteLine(
                $"equipment: parent 0x{ancestorOid:X8} unaddressable for child " +
                $"0x{descendantOid:X8} at ObjectCreation-carried relation accept - " +
                "refusing (should be structurally unreachable - see " +
                "SimActorObjectLifetime.RegisterEntityCore's " +
                "EnqueueDeferredCreate gate). Logged once for this child; " +
                "further refusals for the same child are suppressed");
        }
    }

    private void LocateRelations(uint descendantOid)
    {
        Relations.Resolve(
            descendantOid,
            oid => _onlineActors.TryGetCapture(oid, out _),
            LocateOnlineAncestorInst,
            _admitAncestor);
    }

    private ushort? LocateOnlineAncestorInst(uint ancestorOid)
    {
        return _onlineActors.TryGetCapture(ancestorOid, out RealmSession.MoverSpawn summon)
            ? summon.InstanceSequence
            : null;
    }

    private bool LocateAndTryRealize(uint descendantOid)
    {
        bool projected = false;
        while (true)
        {
            LocateRelations(descendantOid);
            if (!Relations.TryFetchLinedProj(
                    descendantOid,
                    out AnchorAttachmentRelation lined))

                break;

            var validation =
                VetAncestorProj(lined);
            if (validation is ParentMirrorAuditDisposition.Waiting)
                break;
            if (validation is ParentMirrorAuditDisposition.Rejected)
            {
                Relations.RejectProj(lined);
                continue;
            }

            var outcome = ReadyAndTryRealize(
                lined,
                AnchorMirrorCandidateKind.Staged);
            projected |= outcome.Projected;
            if (!outcome.CanAdvanceWireQueue)
                return projected;
        }

        if (Relations.TryFetchRecoveryProj(
                descendantOid,
                out AnchorAttachmentRelation recovery)
            && VetAncestorProj(recovery)
                is ParentMirrorAuditDisposition.Ready)
        {
            projected |= ReadyAndTryRealize(
                recovery,
                AnchorMirrorCandidateKind.Recovery).Projected;
        }
        return projected;
    }

    private MirrorPreparationResult ReadyAndTryRealize(
        AnchorAttachmentRelation relation,
        AnchorMirrorCandidateKind contenderSort)
    {
        if (!_onlineActors.TryFetchCanon(
                relation.ChildGuid,
                out SimActorRecord descendantCanon))

            return default;

        ulong locusArbiterVer =
            descendantCanon.PositionAuthorityVersion;
        if (contenderSort is AnchorMirrorCandidateKind.Staged)
        {
            if (!Relations.CanSealIncarnation(relation, LocateOnlineAncestorInst))
            {
                Relations.RejectProj(relation);
                return new MirrorPreparationResult(
                    CanAdvanceWireQueue: true,
                    Projected: false);
            }
            if (!_onlineActors.SealLinedAncestor(relation, out _)
                || !Relations.SealProj(relation, LocateOnlineAncestorInst))

                return default;
            contenderSort = AnchorMirrorCandidateKind.Recovery;
        }
        else if (!Relations.IsSealed(relation))
        {
            return default;
        }

        if (!Relations.IsPending(relation, contenderSort)
            || !_onlineActors.SealApprovedAncestorCellless(
                descendantCanon,
                locusArbiterVer))

            return default;
        return _onlineActors.TryFetchProj(
                descendantCanon,
                out OnlineActorRecord? descendantCapture)
            && !WithdrawPrecedingProj(descendantCapture)
            ? default
            : new(
            CanAdvanceWireQueue: true,
            Projected: TryRealize(relation, contenderSort));
    }

    private IReadOnlyList<TriMeshRef> AssemblePieceBlueprint(
        RigSpec rig,
        RealmSession.MoverSpawn summon)
    {
        TriMeshRef[] outcome = new TriMeshRef[rig.PartIds.Count];
        for (int idx = 0; idx < outcome.Length; ++idx)
            outcome[idx] = new TriMeshRef((uint)rig.PartIds[idx], Matrix4x4.Identity);

        IReadOnlyList<ObjectCreation.AnimPartSwap> pieceEdits =
            summon.AnimPartChanges ?? Array.Empty<ObjectCreation.AnimPartSwap>();
        for (int idx = 0; idx < pieceEdits.Count; ++idx)
        {
            var edit = pieceEdits[idx];
            if (edit.PartIndex < outcome.Length)
                outcome[edit.PartIndex] = new TriMeshRef(edit.NewModelId, Matrix4x4.Identity);
        }

        IReadOnlyList<ObjectCreation.TextureSwap> textureEdits =
            summon.TextureChanges ?? Array.Empty<ObjectCreation.TextureSwap>();
        for (int pieceOrdinal = 0; pieceOrdinal < outcome.Length; ++pieceOrdinal)
        {
            Dictionary<uint, uint>? formerToNew = null;
            for (int t = 0; t < textureEdits.Count; ++t)
            {
                var edit = textureEdits[t];
                if (edit.PartIndex != pieceOrdinal) continue;
                formerToNew ??= [];
                formerToNew[edit.OldTexture] = edit.NewTexture;
            }
            if (formerToNew is null) continue;

            PartMesh? gfx = _datFiles.Get<PartMesh>(outcome[pieceOrdinal].GfxObjId);
            if (gfx is null) continue;
            Dictionary<uint, uint>? canvasSubstitutions = null;
            foreach (uint canvasQid in gfx.SkinIds)
            {
                uint canvasIdent = (uint)canvasQid;
                Skin? canvas = _datFiles.Get<Skin>(canvasIdent);
                if (canvas is null) continue;
                uint formerTexture = (uint)canvas.TextureId;
                if (!formerToNew.TryGetValue(formerTexture, out uint substitute)) continue;
                canvasSubstitutions ??= [];
                canvasSubstitutions[canvasIdent] = substitute;
            }

            if (canvasSubstitutions is not null)
            {
                outcome[pieceOrdinal] = new TriMeshRef(
                    outcome[pieceOrdinal].GfxObjId,
                    Matrix4x4.Identity)
                {
                    CanvasOverrides = canvasSubstitutions,
                };
            }
        }

        return outcome;
    }

    private bool[] AssemblePieceReadiness(IReadOnlyList<TriMeshRef> blueprint)
    {
        bool[] onHand = new bool[blueprint.Count];
        for (int idx = 0; idx < blueprint.Count; ++idx)
        {
            uint gfxObjRefIdent = blueprint[idx].GfxObjId;
            PartMesh? gfxObjRef = _datFiles.Get<PartMesh>(gfxObjRefIdent);
            if (gfxObjRef is null)
                continue;

            _broadcastGfxObjRef(gfxObjRefIdent, gfxObjRef);
            onHand[idx] = true;
        }
        return onHand;
    }

    private IReadOnlyList<ProxyShape> AssembleDescendantRasterizePieces(
        RigSpec rig, IReadOnlyList<TriMeshRef> blueprint, float scaling)
    {
        uint[] netGfxObjRefIdents = new uint[blueprint.Count];
        for (int idx = 0; idx < blueprint.Count; ++idx)
            netGfxObjRefIdents[idx] = blueprint[idx].GfxObjId;
        return ProxyShapeBuilder.FromRigRasterizePieces(
            rig,
            scaling,
            netGfxObjRefIdents,
            piecePostureOverride: null,
            _kineticsBlob.FetchGfxObjRef,
            _kineticsBlob.FetchVisualLimits);
    }

    private static SwatchOverride? AssembleSwatchOverride(RealmSession.MoverSpawn summon)
    {
        if (summon.SubPalettes is not { Count: > 0 } subSwatches)
            return null;

        var spans = new SwatchOverride.SubPaletteSpan[subSwatches.Count];
        for (int idx = 0; idx < subSwatches.Count; ++idx)
        {
            var swap = subSwatches[idx];
            spans[idx] = new SwatchOverride.SubPaletteSpan(
                swap.SubPaletteId,
                swap.Offset,
                swap.Length);
        }
        return new SwatchOverride(summon.BasePaletteId ?? 0, spans);
    }

    private void BeginDetachedRemoval(uint descendantOid)
    {
        if (!_onlineActors.TryFetchRecord(descendantOid, out OnlineActorRecord capture))
            return;
        BeginProjectionSubtreeWithdrawal(
            _pendingDetachedRemovalByChild,
            capture,
            revertTrunkRelation: false,
            revertDescendantRelations: true);
    }

    private bool BeginProjectionSubtreeWithdrawal(
        Dictionary<SimActorKey, PendingMirrorSubtree> queuedByTrunk,
        OnlineActorRecord trunk,
        bool revertTrunkRelation,
        bool revertDescendantRelations)
    {
        var queued = GrabProjSubtreeAncestorLead(
            trunk,
            revertTrunkRelation,
            revertDescendantRelations);
        return ProgressProjSubtree(
            queuedByTrunk,
            DemandProjTag(trunk),
            queued);
    }

    private bool Drop(uint descendantOid)
    {
        if (!_onlineActors.TryFetchRecord(
                descendantOid,
                out OnlineActorRecord capture)
            || capture.ProjTag is not { } tag
            || !_attachedByChild.TryGetValue(tag, out AffixedDescendant? descendant))
            return false;
        return !_onlineActors.IsLatestCapture(descendant.ChildRecord)
            ? SealProjDeletion(descendant)
            : Drop(
            descendant,
            descendant.ChildRecord.PositionAuthorityVersion,
            descendant.ChildRecord.ProjAlterationVer);
    }

    private bool Drop(
        AffixedDescendant descendant,
        ulong locusArbiterVer,
        ulong projAlterationVer)
    {
        if (!_onlineActors.IsLatestCapture(descendant.ChildRecord))
            return SealProjDeletion(descendant);
        var verdict = WithdrawAffixedProj(
            descendant.ChildRecord,
            locusArbiterVer,
            projAlterationVer,
            _withdrawProj,
            () => SealProjDeletion(descendant));
        return verdict.Failure is not null ? throw verdict.Failure : verdict.Disposition is ExactMirrorWithdrawalDisposition.Completed;
    }

    private static void DropQueuedProjSubtree(
        Dictionary<SimActorKey, PendingMirrorSubtree> queuedByTrunk,
        OnlineActorRecord capture)
    {
        if (capture.ProjTag is { } tag
            && queuedByTrunk.TryGetValue(
                tag,
                out PendingMirrorSubtree queued)
            && ReferenceEquals(queued.RootRecord, capture))

            queuedByTrunk.Remove(tag);
    }

    private void GatherDescendantsAncestorLead(
        OnlineActorRecord ancestor,
        List<AffixedDeletionGrab> dest,
        HashSet<OnlineActorRecord> visited)
    {
        if (!visited.Add(ancestor))
            return;
        AffixedDescendant[] descendants = [.. _attachedByChild.Values.Where(descendant => ReferenceEquals(descendant.ParentRecord, ancestor))];
        for (int idx = 0; idx < descendants.Length; ++idx)
        {
            dest.Add(Capture(descendants[idx]));
            GatherDescendantsAncestorLead(descendants[idx].ChildRecord, dest, visited);
        }
    }
}
