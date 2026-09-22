using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using ArchWorld = Arch.Core.World;

namespace MacAC.Client.Graphics.Stage.Arch;

internal sealed class ArchRenderStage(RenderStageEpoch startingGen) : IRenderStage, IRenderStageProbeSource
{
    private const int StartingActorCap = 256;
    private const int StartingArchetypeCap = 8;

    private readonly int _holderThreadIdent = Environment.CurrentManagedThreadId;
    private readonly Dictionary<RenderMirrorId, TableauListing> _listings = [];
    private readonly HashSet<RenderMirrorId> _exteriorStatics = [];
    private readonly HashSet<RenderMirrorId> _insideChamberStatics = [];
    private readonly HashSet<RenderMirrorId> _dynamics = [];
    private readonly HashSet<RenderMirrorId> _exteriorDynamics = [];
    private readonly HashSet<RenderMirrorId> _gatewayStraddlingDynamics = [];
    private readonly HashSet<RenderMirrorId> _translucent = [];
    private readonly HashSet<RenderMirrorId> _selectable = [];
    private readonly HashSet<RenderMirrorId> _lampContenders = [];
    private readonly HashSet<RenderMirrorId> _stale = [];
    private readonly Dictionary<uint, RenderMirrorId> _byOwnActorIdent = [];
    private ArchWorld _world = BuildRealm();
    private RenderMirrorCounts _counts;
    private ulong _previousImposedJournalSeries;
    private ulong _ordinalRev = 1;
    private ulong _directedShadeWiringRev = 1;
    private DirectionalShadeTransformChange[]? _directedShadeXformEdits;
    private Dictionary<RenderMirrorId, DirectionalShadePartPoseCapture>?
        _directedShadePiecePostures;
    private ulong _directedShadeXformRev;
    private int _directedShadeXformEditTally;
    private bool _destroyed;

    public RenderStageEpoch Generation { get; private set; } = startingGen;

    public RenderMirrorCounts Counts
    {
        get
        {
            SecureOnHand();
            return _counts;
        }
    }

    public RenderStageMemoryAccounting Memory
    {
        get
        {
            SecureOnHand();

            int allocatedChunkTally = 0;
            long estimatedChunkCargoOctets = 0;
            foreach (Archetype archetype in _world.Archetypes.AsSpan())
            {
                allocatedChunkTally += archetype.ChunkCount;
                estimatedChunkCargoOctets +=
                    (long)archetype.ChunkSize * archetype.ChunkCount;
            }

            int consultCap = _listings.EnsureCapacity(0);
            long consultOctets =
                (long)consultCap * Unsafe.SizeOf<MirrorLookupSlotEstimate>();
            long ordinalOctets = GuessOrdinalOctets();
            long directedShadeJournalOctets =
                _directedShadeXformEdits is null
                    ? 0
                    : checked((long)_directedShadeXformEdits.Length
                        * Unsafe.SizeOf<DirectionalShadeTransformChange>());
            if (_directedShadePiecePostures is not null)
            {
                directedShadeJournalOctets = checked(
                    directedShadeJournalOctets
                    + (long)_directedShadePiecePostures.EnsureCapacity(0)
                        * (sizeof(int)
                            + Unsafe.SizeOf<KeyValuePair<
                                RenderMirrorId,
                                DirectionalShadePartPoseCapture>>())
                    + _directedShadePiecePostures.Values.Sum(static posture =>
                        (long)posture.Count * Unsafe.SizeOf<Matrix4x4>()));
            }

            return new RenderStageMemoryAccounting(
                EntityCount: _world.Size,
                ArchEntityCapacity: _world.Capacity,
                ArchetypeCount: _world.Archetypes.Count,
                AllocatedChunkCount: allocatedChunkTally,
                EstimatedChunkPayloadBytes: estimatedChunkCargoOctets,
                ProjectionLookupCapacity: consultCap,
                EstimatedProjectionLookupBytes: consultOctets,
                EstimatedIndexBytes: ordinalOctets,
                EstimatedJournalBufferBytes: directedShadeJournalOctets,
                EstimatedSynchronizationSourceBytes: 0);
        }
    }

    public RenderDiffApplyResult Apply(
        ReadOnlySpan<RenderMirrorDiff> diffs)
    {
        SecureAlterationThread();
        var outcome = new ApplyResultAssembler();

        foreach (ref readonly RenderMirrorDiff delta in diffs)
        {
            if (delta.Generation != Generation)
            {
                outcome.RejectedGen++;
                continue;
            }

            if (delta.JournalSequence is 0
                || delta.JournalSequence <= _previousImposedJournalSeries)
            {
                outcome.RejectedOutOfOrderingSeries++;
                continue;
            }

            _previousImposedJournalSeries = delta.JournalSequence;
            var capture = delta.Record;

            switch (delta.Kind)
            {
                case RenderMirrorDiffKind.Register:
                    ImposeEnroll(in capture, ref outcome);
                    break;
                case RenderMirrorDiffKind.UpdateTransform:
                case RenderMirrorDiffKind.UpdateAppearance:
                case RenderMirrorDiffKind.UpdateFlags:
                case RenderMirrorDiffKind.Rebucket:
                    ImposeRefresh(delta.Kind, in capture, ref outcome);
                    break;
                case RenderMirrorDiffKind.Unregister:
                    ImposeWithdraw(in capture, ref outcome);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(delta.Kind),
                        delta.Kind,
                        null);
            }
        }

        return outcome.Build();
    }

    public void SynchronizeDynamicSrcs(
        in DynamicMirrorSyncInput feed)
    {
        SecureAlterationThread();
        if (feed.Generation != Generation)
        {
            throw new InvalidOperationException(
                $"Dynamic synchronization belongs to {feed.Generation}, "
                + $"but the scene is {Generation}.");
        }

        foreach (ref readonly DynamicMirrorPulse refresh in feed.Updates)
        {
            if (!_listings.TryGetValue(refresh.Id, out TableauListing listing)
                || listing.OwnerIncarnation != refresh.OwnerIncarnation)

                continue;

            ref RasterizeTransform latest =
                ref _world.Get<RasterizeTransform>(listing.Entity);
            ref RenderRealmBounds limits =
                ref _world.Get<RenderRealmBounds>(listing.Entity);
            bool xformAltered = !ConvertBitsetEqual(latest, refresh.Transform);
            if (!xformAltered && limits == refresh.Bounds)
                continue;

            _world.Set(
                listing.Entity,
                new EarlierRasterizeTransform(latest.LocalToWorld));
            _world.Set(listing.Entity, refresh.Transform);
            _world.Set(listing.Entity, refresh.Bounds);
            if (xformAltered
                && HasRefreshableDirectedShadeXforms(listing.ProjectionClass)
                && _directedShadeXformEdits is not null)
            {
                var latestCapture = ScanCapture(in listing);
                BroadcastDirectedShadeXformEdit(
                    in latestCapture,
                    DirectionalShadeTransformChangeKind.DynamicSynchronization);
            }

            ref RasterizeStaleBitmask stale =
                ref _world.Get<RasterizeStaleBitmask>(listing.Entity);
            stale |= RasterizeStaleBitmask.Transform | RasterizeStaleBitmask.WorldBounds;
            _stale.Add(refresh.Id);
        }
    }

    public RenderStageDigest AssembleDigest(RenderStageDigestBuffer reuse)
    {
        ArgumentNullException.ThrowIfNull(reuse);
        SecureOnHand();

        var records = reuse.Records;
        records.Clear();
        if (records.Capacity < _listings.Count)
            records.Capacity = _listings.Count;

        foreach (TableauListing listing in _listings.Values)
            records.Add(ScanCapture(in listing));

        records.Sort(RenderMirrorRecordComparer.Instance);

        var digest = StableRasterizeHash128.Create();
        digest.Add(Generation.RawValue);
        digest.Add(records.Count);
        foreach (RenderMirrorRecord capture in records)
            AppendCapture(ref digest, in capture);

        return new RenderStageDigest(Generation, _counts, digest.Finish());
    }

    public RenderStageProbe OpenAsk()
    {
        SecureOnHand();
        return new RenderStageProbe(this, Generation);
    }

    public void Clear(RenderStageEpoch replacementGeneration)
    {
        SecureAlterationThread();
        if (replacementGeneration.CompareTo(Generation) <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacementGeneration),
                replacementGeneration,
                "A replacement render-scene generation must advance");
        }

        ArchWorld.Destroy(_world);
        _world = BuildRealm();
        _listings.Clear();
        WipeOrdinals();
        _counts = default;
        _previousImposedJournalSeries = 0;
        RestartDirectedShadeXformEdits();
        Generation = replacementGeneration;
        ProgressOrdinalRev();
        ProgressDirectedShadeWiringRev();
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        SecureAlterationThread();
        ArchWorld.Destroy(_world);
        _listings.Clear();
        WipeOrdinals();
        _directedShadeXformEdits = null;
        _directedShadePiecePostures = null;
        _directedShadeXformRev = 0;
        _directedShadeXformEditTally = 0;
        _counts = default;
        _destroyed = true;
    }

    public void WipeStale()
    {
        SecureAlterationThread();
        foreach (RenderMirrorId ident in _stale)
        {
            if (_listings.TryGetValue(ident, out TableauListing listing))
                _world.Set(listing.Entity, RasterizeStaleBitmask.None);
        }
        _stale.Clear();
    }

    RenderMirrorCounts IRenderStageProbeSource.GetCounts(
        RenderStageEpoch gen)
    {
        SecureAskGen(gen);
        return _counts;
    }

    RenderStageIndexCounts IRenderStageProbeSource.GetIndexCounts(
        RenderStageEpoch gen)
    {
        SecureAskGen(gen);
        return new RenderStageIndexCounts(
            _exteriorStatics.Count,
            _insideChamberStatics.Count,
            _dynamics.Count,
            _exteriorDynamics.Count,
            _gatewayStraddlingDynamics.Count,
            _translucent.Count,
            _selectable.Count,
            _lampContenders.Count,
            _stale.Count);
    }

    ulong IRenderStageProbeSource.GetIndexRevision(
        RenderStageEpoch gen)
    {
        SecureAskGen(gen);
        return _ordinalRev;
    }

    ulong IRenderStageProbeSource.GetDirectionalShadowTopologyRevision(
        RenderStageEpoch gen)
    {
        SecureAskGen(gen);
        return _directedShadeWiringRev;
    }

    ulong IRenderStageProbeSource.GetDirectionalShadowTransformRevision(
        RenderStageEpoch gen)
    {
        SecureAskGen(gen);
        SecureDirectedShadeXformJournal();
        return _directedShadeXformRev;
    }

    DirectionalShadeTransformChanges
        IRenderStageProbeSource.CopyDirectionalShadowTransformChanges(
            RenderStageEpoch gen,
            ulong followingRev,
            Span<DirectionalShadeTransformCapture> dest)
    {
        SecureAskGen(gen);
        SecureDirectedShadeXformJournal();
        ulong current = _directedShadeXformRev;
        if (followingRev == current)
            return new DirectionalShadeTransformChanges(current, 0, false);
        if (followingRev is 0
            || followingRev > current
            || current - followingRev
                > checked((ulong)_directedShadeXformEditTally))
        {
            return new DirectionalShadeTransformChanges(current, 0, true);
        }

        int tally = checked((int)(current - followingRev));
        if (dest.Length < tally)
            return new DirectionalShadeTransformChanges(current, 0, true);
        var journal =
            _directedShadeXformEdits!;
        int refreshXformTally = 0;
        int refreshLooksTally = 0;
        int dynamicSynchronizationTally = 0;
        int engagedMovingStaticTally = 0;
        int onlineDynamicTrunkTally = 0;
        int equippedDescendantTally = 0;
        for (int ordinal = 0; ordinal < tally; ++ordinal)
        {
            ulong rev = checked(followingRev + (ulong)ordinal + 1UL);
            var edit =
                journal[(int)(rev % (ulong)journal.Length)];
            if (edit.Revision != rev)
                return new DirectionalShadeTransformChanges(current, 0, true);
            dest[ordinal] = edit.Projection;
            switch (edit.Kind)
            {
                case DirectionalShadeTransformChangeKind.UpdateTransform:
                    ++refreshXformTally;
                    break;
                case DirectionalShadeTransformChangeKind.UpdateAppearance:
                    ++refreshLooksTally;
                    break;
                case DirectionalShadeTransformChangeKind.DynamicSynchronization:
                    ++dynamicSynchronizationTally;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unrecognized directional-shadow change kind {edit.Kind}.");
            }
            switch (edit.Projection.ProjClass)
            {
                case RenderMirrorClass.ActiveAnimatedStatic:
                    ++engagedMovingStaticTally;
                    break;
                case RenderMirrorClass.LiveDynamicRoot:
                    ++onlineDynamicTrunkTally;
                    break;
                case RenderMirrorClass.EquippedChild:
                    ++equippedDescendantTally;
                    break;
            }
        }
        return new DirectionalShadeTransformChanges(
            current,
            tally,
            false,
            refreshXformTally,
            refreshLooksTally,
            dynamicSynchronizationTally,
            engagedMovingStaticTally,
            onlineDynamicTrunkTally,
            equippedDescendantTally);
    }

    bool IRenderStageProbeSource.TryGet(
        RenderStageEpoch gen,
        RenderMirrorId ident,
        out RenderMirrorRecord capture)
    {
        SecureAskGen(gen);
        if (_listings.TryGetValue(ident, out TableauListing listing))
        {
            capture = ScanCapture(in listing);
            return true;
        }

        capture = default;
        return false;
    }

    bool IRenderStageProbeSource.TryGetByLocalEntityId(
        RenderStageEpoch gen,
        uint ownActorIdent,
        out RenderMirrorRecord capture)
    {
        SecureAskGen(gen);
        if (_byOwnActorIdent.TryGetValue(ownActorIdent, out RenderMirrorId ident)
            && _listings.TryGetValue(ident, out TableauListing listing))
        {
            capture = ScanCapture(in listing);
            return true;
        }

        capture = default;
        return false;
    }

    int IRenderStageProbeSource.CopyById(
        RenderStageEpoch gen,
        ReadOnlySpan<RenderMirrorId> idents,
        Span<RenderMirrorRecord> destination)
    {
        SecureAskGen(gen);
        if (destination.Length < idents.Length)
        {
            throw new ArgumentException(
                "The render-scene ID-copy destination is too small",
                nameof(destination));
        }
        for (int ordinal = 0; ordinal < idents.Length; ++ordinal)
        {
            if (!_listings.TryGetValue(idents[ordinal], out TableauListing listing))
            {
                throw new InvalidOperationException(
                    $"Render-scene projection {idents[ordinal]} disappeared during a batched copy");
            }
            destination[ordinal] = ScanCapture(in listing);
        }
        return idents.Length;
    }

    int IRenderStageProbeSource.CopyTo(
        RenderStageEpoch gen,
        RenderMirrorClass? projClass,
        Span<RenderMirrorRecord> destination)
    {
        SecureAskGen(gen);
        int needed = projClass.HasValue
            ? _counts.For(projClass.Value)
            : _counts.Total;
        if (destination.Length < needed)
        {
            throw new ArgumentException(
                $"Destination holds {destination.Length} records; {needed} needed",
                nameof(destination));
        }

        int tally = 0;
        foreach (TableauListing listing in _listings.Values)
        {
            if (projClass.HasValue
                && listing.ProjectionClass != projClass.Value)

                continue;

            destination[tally++] = ScanCapture(in listing);
        }

        return tally;
    }

    int IRenderStageProbeSource.CopyIndexTo(
        RenderStageEpoch gen,
        RenderStageIndex ordinal,
        Span<RenderMirrorRecord> dest)
    {
        SecureAskGen(gen);
        var src = Index(ordinal);
        return DuplicateIdentsTo(src, dest);
    }

    private static ArchWorld BuildRealm()
    {
        return ArchWorld.Create(
            archetypeCapacity: StartingArchetypeCap,
            entityCapacity: StartingActorCap);
    }

    private void ImposeEnroll(
        in RenderMirrorRecord capture,
        ref ApplyResultAssembler outcome)
    {
        if (_listings.TryGetValue(capture.Id, out TableauListing extant))
        {
            int incarnationOrdering =
                capture.OwnerIncarnation.CompareTo(extant.OwnerIncarnation);
            if (incarnationOrdering < 0)
            {
                outcome.RejectedStaleIncarnation++;
                return;
            }

            if (incarnationOrdering is 0
                && capture.ProjectionClass == extant.ProjectionClass)
            {
                var preceding = ScanCapture(in extant);
                EmitCapture(extant.Entity, in capture);
                RefreshOrdinals(in preceding, in capture);
                if (HasRefreshableDirectedShadeXforms(capture.ProjectionClass))
                {
                    if (!ConvertBitsetEqual(preceding.Transform, capture.Transform))
                    {
                        BroadcastDirectedShadeXformEdit(
                            in capture,
                            DirectionalShadeTransformChangeKind.UpdateTransform);
                    }
                    BroadcastDirectedShadePiecePostureEditIfNeeded(in capture);
                }
                outcome.Imposed++;
                outcome.Updated++;
                return;
            }

            Destroy(in extant);
            outcome.Replaced++;
        }

        Entity actor = BuildActor(in capture);
        _listings[capture.Id] = new TableauListing(
            actor,
            capture.OwnerIncarnation,
            capture.ProjectionClass);
        IncrementTally(capture.ProjectionClass);
        AppendToOrdinals(in capture);
        SynchronizeDirectedShadePiecePosture(in capture);
        outcome.Imposed++;
        outcome.Registered++;
    }

    private void ImposeRefresh(
        RenderMirrorDiffKind kind,
        in RenderMirrorRecord capture,
        ref ApplyResultAssembler outcome)
    {
        if (!TryFetchLatest(in capture, ref outcome, out TableauListing listing))
            return;

        var preceding = ScanCapture(in listing);
        switch (kind)
        {
            case RenderMirrorDiffKind.UpdateTransform:
                _world.Set(listing.Entity, capture.PreviousTransform);
                _world.Set(listing.Entity, capture.Transform);
                _world.Set(listing.Entity, capture.Bounds);
                _world.Set(listing.Entity, capture.SortKey);
                _world.Set(listing.Entity, capture.Source);
                OrStale(
                    listing.Entity,
                    capture.Id,
                    RasterizeStaleBitmask.Transform
                    | RasterizeStaleBitmask.WorldBounds
                    | RasterizeStaleBitmask.SortKey);
                break;
            case RenderMirrorDiffKind.UpdateAppearance:
                _world.Set(listing.Entity, capture.MeshSet);
                _world.Set(listing.Entity, capture.Material);
                _world.Set(listing.Entity, capture.DegradeState);
                _world.Set(listing.Entity, capture.Source);
                _world.Set(listing.Entity, capture.EntityPayload);
                OrStale(
                    listing.Entity,
                    capture.Id,
                    RasterizeStaleBitmask.Appearance);
                break;
            case RenderMirrorDiffKind.UpdateFlags:
                _world.Set(listing.Entity, capture.Flags);
                _world.Set(listing.Entity, capture.Source);
                OrStale(listing.Entity, capture.Id, RasterizeStaleBitmask.Flags);
                break;
            case RenderMirrorDiffKind.Rebucket:
                _world.Set(listing.Entity, capture.Residency);
                _world.Set(listing.Entity, capture.Source);
                OrStale(
                    listing.Entity,
                    capture.Id,
                    RasterizeStaleBitmask.SpatialResidency);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        var latest = ScanCapture(in listing);
        RefreshOrdinals(in preceding, in latest);
        if (HasRefreshableDirectedShadeXforms(latest.ProjectionClass))
        {
            if (kind is RenderMirrorDiffKind.UpdateTransform
                && !ConvertBitsetEqual(preceding.Transform, latest.Transform))
            {
                BroadcastDirectedShadeXformEdit(
                    in latest,
                    DirectionalShadeTransformChangeKind.UpdateTransform);
            }
            else if (kind is RenderMirrorDiffKind.UpdateAppearance)
            {
                BroadcastDirectedShadePiecePostureEditIfNeeded(in latest);
            }
        }
        outcome.Imposed++;
        outcome.Updated++;
    }

    private void ImposeWithdraw(
        in RenderMirrorRecord capture,
        ref ApplyResultAssembler outcome)
    {
        if (!TryFetchLatest(in capture, ref outcome, out TableauListing listing))
            return;

        Destroy(in listing);
        _listings.Remove(capture.Id);
        outcome.Imposed++;
        outcome.Unregistered++;
    }

    private bool TryFetchLatest(
        in RenderMirrorRecord capture,
        ref ApplyResultAssembler outcome,
        out TableauListing listing)
    {
        if (!_listings.TryGetValue(capture.Id, out listing))
        {
            outcome.RejectedAbsent++;
            return false;
        }

        if (capture.OwnerIncarnation != listing.OwnerIncarnation)
        {
            outcome.RejectedStaleIncarnation++;
            return false;
        }

        return true;
    }

    private Entity BuildActor(in RenderMirrorRecord record)
    {
        return record.ProjectionClass switch
        {
            RenderMirrorClass.OutdoorStatic =>
                BuildActor(in record, new ExteriorStaticMarker()),
            RenderMirrorClass.IndoorCellStatic =>
                BuildActor(in record, new InsideChamberStaticMarker()),
            RenderMirrorClass.LiveDynamicRoot =>
                BuildActor(in record, new OnlineDynamicRootTag()),
            RenderMirrorClass.ActiveAnimatedStatic =>
                BuildActor(in record, new EngagedMovingStaticMarker()),
            RenderMirrorClass.EquippedChild =>
                BuildActor(in record, new EquippedDescendantMarker()),
            _ => throw new ArgumentOutOfRangeException(
                nameof(record.ProjectionClass),
                record.ProjectionClass,
                null),
        };
    }

    private Entity BuildActor<TTag>(
        in RenderMirrorRecord capture,
        TTag tag)
        where TTag : struct
    {
        return _world.Create(
            new MirrorIdentity(capture.Id),
            capture.Transform,
            capture.PreviousTransform,
            capture.MeshSet,
            capture.Material,
            capture.Residency,
            capture.Bounds,
            capture.Flags,
            capture.DegradeState,
            capture.SortKey,
            capture.OwnerIncarnation,
            capture.DirtyMask,
            capture.Source,
            capture.EntityPayload,
            tag);
    }

    private void EmitCapture(
        Entity actor,
        in RenderMirrorRecord capture)
    {
        _world.Set(actor, capture.Transform);
        _world.Set(actor, capture.PreviousTransform);
        _world.Set(actor, capture.MeshSet);
        _world.Set(actor, capture.Material);
        _world.Set(actor, capture.Residency);
        _world.Set(actor, capture.Bounds);
        _world.Set(actor, capture.Flags);
        _world.Set(actor, capture.DegradeState);
        _world.Set(actor, capture.SortKey);
        _world.Set(actor, capture.OwnerIncarnation);
        _world.Set(actor, capture.DirtyMask);
        _world.Set(actor, capture.Source);
        _world.Set(actor, capture.EntityPayload);
    }

    private RenderMirrorRecord ScanCapture(in TableauListing listing)
    {
        return new(
            _world.Get<MirrorIdentity>(listing.Entity).Id,
            listing.ProjectionClass,
            _world.Get<RasterizeHolderIncarnation>(listing.Entity),
            _world.Get<RasterizeTransform>(listing.Entity),
            _world.Get<EarlierRasterizeTransform>(listing.Entity),
            _world.Get<RasterizeTriMeshGroup>(listing.Entity),
            _world.Get<RasterizeMatlVariant>(listing.Entity),
            _world.Get<RenderSpatialTenancy>(listing.Entity),
            _world.Get<RenderRealmBounds>(listing.Entity),
            _world.Get<RenderMirrorFlags>(listing.Entity),
            _world.Get<RenderDegradeLedger>(listing.Entity),
            _world.Get<RasterizeOrderTag>(listing.Entity),
            _world.Get<RasterizeStaleBitmask>(listing.Entity),
            _world.Get<RasterizeOriginMetadata>(listing.Entity),
            _world.Get<RenderActorPayload>(listing.Entity));
    }

    private void Destroy(in TableauListing listing)
    {
        var capture = ScanCapture(in listing);
        _directedShadePiecePostures?.Remove(capture.Id);
        DropFromOrdinals(in capture);
        _world.Destroy(listing.Entity);
        DecrementTally(listing.ProjectionClass);
    }

    private void OrStale(
        Entity actor,
        RenderMirrorId ident,
        RasterizeStaleBitmask val)
    {
        ref RasterizeStaleBitmask stale = ref _world.Get<RasterizeStaleBitmask>(actor);
        stale |= val;
        _stale.Add(ident);
    }

    private void RefreshOrdinals(
        in RenderMirrorRecord preceding,
        in RenderMirrorRecord latest)
    {
        if (OrdinalMembershipEquals(in preceding, in latest))
        {
            if (!DirectedShadeWiringEquals(in preceding, in latest))
                ProgressDirectedShadeWiringRev();
            SynchronizeStaleOrdinal(in latest);
            return;
        }

        DropFromOrdinals(in preceding);
        AppendToOrdinals(in latest);
    }

    private void AppendToOrdinals(in RenderMirrorRecord capture)
    {
        bool dynamic = IsDynamic(capture.ProjectionClass);
        bool staticProj = !dynamic;
        bool inside = InteriorActorPartition.IsIndoorCellId(
            capture.Source.ParentCellId is 0
                ? null
                : capture.Source.ParentCellId);

        if (staticProj)
        {
            if (inside)
                _insideChamberStatics.Add(capture.Id);
            else
                _exteriorStatics.Add(capture.Id);
        }
        else
        {
            _dynamics.Add(capture.Id);
            if (!inside)
                _exteriorDynamics.Add(capture.Id);
            if ((capture.Flags & RenderMirrorFlags.PortalStraddling) != 0)
                _gatewayStraddlingDynamics.Add(capture.Id);
        }

        if ((capture.Flags & RenderMirrorFlags.Translucent) != 0)
            _translucent.Add(capture.Id);
        if ((capture.Flags & RenderMirrorFlags.Selectable) != 0)
            _selectable.Add(capture.Id);
        if ((capture.Flags & RenderMirrorFlags.LightCandidate) != 0)
            _lampContenders.Add(capture.Id);
        if (capture.DirtyMask != RasterizeStaleBitmask.None)
            _stale.Add(capture.Id);
        if (capture.Source.LocalEntityId is not 0 && !IsEnvironChamberShell(in capture))
            _byOwnActorIdent[capture.Source.LocalEntityId] = capture.Id;
        ProgressOrdinalRev();
        ProgressDirectedShadeWiringRev();
    }

    private void DropFromOrdinals(in RenderMirrorRecord capture)
    {
        _exteriorStatics.Remove(capture.Id);
        _insideChamberStatics.Remove(capture.Id);
        _dynamics.Remove(capture.Id);
        _exteriorDynamics.Remove(capture.Id);
        _gatewayStraddlingDynamics.Remove(capture.Id);
        _translucent.Remove(capture.Id);
        _selectable.Remove(capture.Id);
        _lampContenders.Remove(capture.Id);
        _stale.Remove(capture.Id);
        if (capture.Source.LocalEntityId is not 0
            && _byOwnActorIdent.TryGetValue(
                capture.Source.LocalEntityId,
                out RenderMirrorId mapped)
            && mapped == capture.Id)

            _byOwnActorIdent.Remove(capture.Source.LocalEntityId);
        ProgressOrdinalRev();
        ProgressDirectedShadeWiringRev();
    }

    private static bool IsEnvironChamberShell(in RenderMirrorRecord capture)
    {
        return capture.ProjectionClass == RenderMirrorClass.IndoorCellStatic
        && capture.EntityPayload.MeshRefs is null;
    }

    private static bool OrdinalMembershipEquals(
        in RenderMirrorRecord left,
        in RenderMirrorRecord right)
    {
        const RenderMirrorFlags indexedFlagSet =
            RenderMirrorFlags.Translucent
            | RenderMirrorFlags.Selectable
            | RenderMirrorFlags.LightCandidate
            | RenderMirrorFlags.PortalStraddling
            | RenderMirrorFlags.SpatiallyResident;

        return left.ProjectionClass == right.ProjectionClass
            && left.Source.LocalEntityId == right.Source.LocalEntityId
            && left.Source.ParentCellId == right.Source.ParentCellId
            && left.Residency.FullCellId == right.Residency.FullCellId
            && (left.Flags & indexedFlagSet) == (right.Flags & indexedFlagSet)
            && left.MeshSet.MeshCount == right.MeshSet.MeshCount
            && left.SortKey == right.SortKey;
    }

    private static bool DirectedShadeWiringEquals(
        in RenderMirrorRecord left,
        in RenderMirrorRecord right)
    {
        const RenderMirrorFlags eligibilityFlagSet =
            RenderMirrorFlags.Draw
            | RenderMirrorFlags.SpatiallyResident
            | RenderMirrorFlags.Translucent;

        if (left.ProjectionClass != right.ProjectionClass
            || left.OwnerIncarnation != right.OwnerIncarnation
            || left.Source.ParentCellId != right.Source.ParentCellId
            || (left.Flags & eligibilityFlagSet) != (right.Flags & eligibilityFlagSet)
            || left.SortKey != right.SortKey
            || left.MeshSet.MeshCount != right.MeshSet.MeshCount
            || left.Material != right.Material
            || left.DegradeState != right.DegradeState
            || left.Source.AppearanceFingerprint
                != right.Source.AppearanceFingerprint
            || left.Source.DirectionalShadowTopologyFingerprint
                != right.Source.DirectionalShadowTopologyFingerprint
            || left.EntityPayload.IsBuildingShell
                != right.EntityPayload.IsBuildingShell
            || left.EntityPayload.CasterIdentity
                != right.EntityPayload.CasterIdentity
            || !SwatchEquals(
                left.EntityPayload.PaletteOverride,
                right.EntityPayload.PaletteOverride))

            return false;

        bool refreshableXforms =
            HasRefreshableDirectedShadeXforms(left.ProjectionClass);
        if (!refreshableXforms
            && (left.Transform != right.Transform
                || left.MeshSet != right.MeshSet
                || left.Source.GeometryFingerprint
                    != right.Source.GeometryFingerprint))

            return false;

        IReadOnlyList<MacAC.Mechanics.Realm.TriMeshRef>? leftTriMeshes =
            left.EntityPayload.MeshRefs;
        IReadOnlyList<MacAC.Mechanics.Realm.TriMeshRef>? rightTriMeshes =
            right.EntityPayload.MeshRefs;
        if (ReferenceEquals(leftTriMeshes, rightTriMeshes))
            return true;
        if (leftTriMeshes is null
            || rightTriMeshes is null
            || leftTriMeshes.Count != rightTriMeshes.Count)

            return false;

        for (int triMeshOrdinal = 0; triMeshOrdinal < leftTriMeshes.Count; ++triMeshOrdinal)
        {
            var leftTriMesh = leftTriMeshes[triMeshOrdinal];
            var rightTriMesh = rightTriMeshes[triMeshOrdinal];
            if (leftTriMesh.GfxObjId != rightTriMesh.GfxObjId
                || !CanvasSubstitutionsEqual(
                    leftTriMesh.CanvasOverrides,
                    rightTriMesh.CanvasOverrides)
                || (!refreshableXforms
                    && leftTriMesh.PartTransform != rightTriMesh.PartTransform))

                return false;
        }

        return true;
    }

    private static bool HasRefreshableDirectedShadeXforms(
        RenderMirrorClass projClass)
    {
        return projClass is RenderMirrorClass.ActiveAnimatedStatic
            or RenderMirrorClass.LiveDynamicRoot
            or RenderMirrorClass.EquippedChild;
    }

    private static bool ConvertBitsetEqual(
        in RasterizeTransform left,
        in RasterizeTransform right)
    {
        Matrix4x4 leftMatrix = left.LocalToWorld;
        Matrix4x4 rightMatrix = right.LocalToWorld;
        var leftSpan = MemoryMarshal.CreateReadOnlySpan(
            in leftMatrix,
            1);
        var rightSpan = MemoryMarshal.CreateReadOnlySpan(
            in rightMatrix,
            1);
        return MemoryMarshal.AsBytes(leftSpan).SequenceEqual(
            MemoryMarshal.AsBytes(rightSpan));
    }

    private void SecureDirectedShadeXformJournal()
    {
        if (_directedShadeXformEdits is not null)
            return;
        _directedShadeXformEdits = new DirectionalShadeTransformChange[
            DirectionalShadeTransformChangeDiary.Capacity];
        _directedShadeXformRev = 1;
        _directedShadeXformEditTally = 0;
        _directedShadePiecePostures = [];
        foreach (TableauListing listing in _listings.Values)
        {
            if (!HasRefreshableDirectedShadeXforms(listing.ProjectionClass))
                continue;
            var capture = ScanCapture(in listing);
            SynchronizeDirectedShadePiecePosture(in capture);
        }
    }

    private void BroadcastDirectedShadeXformEdit(
        in RenderMirrorRecord proj,
        DirectionalShadeTransformChangeKind sort)
    {
        var journal =
            _directedShadeXformEdits;
        if (journal is null)
            return;
        if (_directedShadeXformRev == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "Directional-shadow transform revision space was exhausted");
        }
        ulong rev = ++_directedShadeXformRev;
        journal[(int)(rev % (ulong)journal.Length)] =
            new DirectionalShadeTransformChange(
                rev,
                DirectionalShadeTransformCapture.Capture(in proj),
                sort);
        if (_directedShadeXformEditTally < journal.Length)
            ++_directedShadeXformEditTally;
    }

    private void RestartDirectedShadeXformEdits()
    {
        if (_directedShadeXformEdits is null)
            return;
        _directedShadeXformRev = 1;
        _directedShadeXformEditTally = 0;
        _directedShadePiecePostures!.Clear();
    }

    private void SynchronizeDirectedShadePiecePosture(
        in RenderMirrorRecord capture)
    {
        var
            postures = _directedShadePiecePostures;
        if (postures is null
            || !HasRefreshableDirectedShadeXforms(capture.ProjectionClass))

            return;
        if (!postures.TryGetValue(capture.Id, out DirectionalShadePartPoseCapture? posture))
        {
            postures.Add(capture.Id, DirectionalShadePartPoseCapture.Capture(in capture));
            return;
        }
        posture.GrabLatest(in capture);
    }

    private void BroadcastDirectedShadePiecePostureEditIfNeeded(
        in RenderMirrorRecord capture)
    {
        var
            postures = _directedShadePiecePostures;
        if (postures is null)
            return;
        if (!postures.TryGetValue(capture.Id, out DirectionalShadePartPoseCapture? posture))
        {
            postures.Add(capture.Id, DirectionalShadePartPoseCapture.Capture(in capture));
            return;
        }
        if (!posture.GrabLatest(in capture))
            return;
        BroadcastDirectedShadeXformEdit(
            in capture,
            DirectionalShadeTransformChangeKind.UpdateAppearance);
    }

    private static bool SwatchEquals(
        MacAC.Mechanics.Realm.SwatchOverride? left,
        MacAC.Mechanics.Realm.SwatchOverride? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null
            || right is null
            || left.BasePaletteId != right.BasePaletteId
            || left.SubPalettes.Count != right.SubPalettes.Count)

            return false;

        for (int ordinal = 0; ordinal < left.SubPalettes.Count; ++ordinal)
        {
            if (left.SubPalettes[ordinal] != right.SubPalettes[ordinal])
                return false;
        }

        return true;
    }

    private static bool CanvasSubstitutionsEqual(
        IReadOnlyDictionary<uint, uint>? left,
        IReadOnlyDictionary<uint, uint>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;

        foreach ((uint canvasIdent, uint textureIdent) in left)
        {
            if (!right.TryGetValue(canvasIdent, out uint contender)
                || contender != textureIdent)

                return false;
        }

        return true;
    }

    private void SynchronizeStaleOrdinal(
        in RenderMirrorRecord capture)
    {
        if (capture.DirtyMask == RasterizeStaleBitmask.None)
            _stale.Remove(capture.Id);
        else
            _stale.Add(capture.Id);
    }

    private void ProgressOrdinalRev()
    {
        if (_ordinalRev == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "Render-scene index revision space was exhausted");
        }

        ++_ordinalRev;
    }

    private void ProgressDirectedShadeWiringRev()
    {
        if (_directedShadeWiringRev == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "Directional-shadow topology revision space was exhausted");
        }

        ++_directedShadeWiringRev;
    }

    private static bool IsDynamic(RenderMirrorClass projClass)
    {
        return projClass is RenderMirrorClass.LiveDynamicRoot
            or RenderMirrorClass.EquippedChild;
    }

    private HashSet<RenderMirrorId> Index(RenderStageIndex index)
    {
        return index switch
        {
            RenderStageIndex.OutdoorStatic => _exteriorStatics,
            RenderStageIndex.IndoorCellStatic => _insideChamberStatics,
            RenderStageIndex.Dynamic => _dynamics,
            RenderStageIndex.OutdoorDynamic => _exteriorDynamics,
            RenderStageIndex.PortalStraddlingDynamic =>
                _gatewayStraddlingDynamics,
            RenderStageIndex.Translucent => _translucent,
            RenderStageIndex.Selectable => _selectable,
            RenderStageIndex.LightCandidate => _lampContenders,
            RenderStageIndex.Dirty => _stale,
            _ => throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                null),
        };
    }

    private int DuplicateIdentsTo(
        HashSet<RenderMirrorId> src,
        Span<RenderMirrorRecord> destination)
    {
        if (destination.Length < src.Count)
        {
            throw new ArgumentException(
                $"Destination holds {destination.Length} records; {src.Count} needed",
                nameof(destination));
        }

        int tally = 0;
        foreach (RenderMirrorId ident in src)
        {
            if (!_listings.TryGetValue(ident, out TableauListing listing))
            {
                throw new InvalidOperationException(
                    $"Render index retained absent {ident}.");
            }
            destination[tally++] = ScanCapture(in listing);
        }
        return tally;
    }

    private long GuessOrdinalOctets()
    {
        long sockets =
            _exteriorStatics.EnsureCapacity(0)
            + _insideChamberStatics.EnsureCapacity(0)
            + _dynamics.EnsureCapacity(0)
            + _exteriorDynamics.EnsureCapacity(0)
            + _gatewayStraddlingDynamics.EnsureCapacity(0)
            + _translucent.EnsureCapacity(0)
            + _selectable.EnsureCapacity(0)
            + _lampContenders.EnsureCapacity(0)
            + _stale.EnsureCapacity(0);
        long chamberConsultSockets = _byOwnActorIdent.EnsureCapacity(0);
        return checked(sockets * 24L + chamberConsultSockets * 40L);
    }

    private void WipeOrdinals()
    {
        _exteriorStatics.Clear();
        _insideChamberStatics.Clear();
        _dynamics.Clear();
        _exteriorDynamics.Clear();
        _gatewayStraddlingDynamics.Clear();
        _translucent.Clear();
        _selectable.Clear();
        _lampContenders.Clear();
        _stale.Clear();
        _byOwnActorIdent.Clear();
    }

    private void IncrementTally(RenderMirrorClass projClass)
    {
        _counts = WithClassTally(
            _counts,
            projClass,
            _counts.For(projClass) + 1,
            _counts.Total + 1);
    }

    private void DecrementTally(RenderMirrorClass projClass)
    {
        _counts = WithClassTally(
            _counts,
            projClass,
            _counts.For(projClass) - 1,
            _counts.Total - 1);
    }

    private static RenderMirrorCounts WithClassTally(
        RenderMirrorCounts counts,
        RenderMirrorClass projectionClass,
        int classTally,
        int sum)
    {
        return projectionClass switch
        {
            RenderMirrorClass.OutdoorStatic =>
                counts with { Total = sum, OutdoorStatic = classTally },
            RenderMirrorClass.IndoorCellStatic =>
                counts with { Total = sum, IndoorCellStatic = classTally },
            RenderMirrorClass.LiveDynamicRoot =>
                counts with { Total = sum, LiveDynamicRoot = classTally },
            RenderMirrorClass.ActiveAnimatedStatic =>
                counts with { Total = sum, ActiveAnimatedStatic = classTally },
            RenderMirrorClass.EquippedChild =>
                counts with { Total = sum, EquippedChild = classTally },
            _ => throw new ArgumentOutOfRangeException(
                nameof(projectionClass),
                projectionClass,
                null),
        };
    }

    private void SecureAlterationThread()
    {
        SecureOnHand();
        if (Environment.CurrentManagedThreadId != _holderThreadIdent)
        {
            throw new InvalidOperationException(
                "Render-scene mutation must remain on its owning update thread");
        }
    }

    private void SecureAskGen(RenderStageEpoch gen)
    {
        SecureOnHand();
        if (gen != Generation)
        {
            throw new InvalidOperationException(
                $"Borrowed render-scene query belongs to {gen}; "
                + $"the current scene is {Generation}.");
        }
    }

    private void SecureOnHand() =>
        ObjectDisposedException.ThrowIf(_destroyed, this);

    private static void AppendCapture(
        ref StableRasterizeHash128 digest,
        in RenderMirrorRecord capture)
    {
        digest.Add(capture.Id.RawValue);
        digest.Add((byte)capture.ProjectionClass);
        digest.Add(capture.OwnerIncarnation.RawValue);
        digest.Add(capture.Transform.Position);
        digest.Add(capture.Transform.Rotation);
        digest.Add(capture.Transform.UniformScale);
        digest.Add(capture.Transform.LocalToWorld);
        digest.Add(capture.PreviousTransform.LocalToWorld);
        digest.Add(capture.MeshSet.Handle.RawValue);
        digest.Add(capture.MeshSet.MeshCount);
        digest.Add(capture.MeshSet.Revision);
        digest.Add(capture.Material.PaletteKey);
        digest.Add(capture.Material.TextureReplacementKey);
        digest.Add(capture.Material.Opacity);
        digest.Add(capture.Residency.Bucket.RawValue);
        digest.Add(capture.Residency.OwnerLandblockId);
        digest.Add(capture.Residency.FullCellId);
        digest.Add(capture.Bounds.Minimum);
        digest.Add(capture.Bounds.Maximum);
        digest.Add((uint)capture.Flags);
        digest.Add(capture.DegradeState.Level);
        digest.Add(capture.DegradeState.Revision);
        digest.Add(capture.SortKey.Value);
        digest.Add(capture.Source.LocalEntityId);
        digest.Add(capture.Source.ServerGuid);
        digest.Add(capture.Source.SourceId);
        digest.Add(capture.Source.ParentCellId);
        digest.Add(capture.Source.EffectCellId);
        digest.Add(capture.Source.BuildingShellAnchorCellId);
        digest.Add(capture.Source.TransformFingerprint.Low);
        digest.Add(capture.Source.TransformFingerprint.High);
        digest.Add(capture.Source.GeometryFingerprint.Low);
        digest.Add(capture.Source.GeometryFingerprint.High);
        digest.Add(capture.Source.AppearanceFingerprint.Low);
        digest.Add(capture.Source.AppearanceFingerprint.High);
        digest.Add(capture.Source.CurrentProjectionFlags);
        digest.Add((byte)capture.EntityPayload.CasterIdentity);
    }

    private readonly record struct MirrorIdentity(RenderMirrorId Id);

    private readonly record struct DirectionalShadeTransformChange(
        ulong Revision,
        DirectionalShadeTransformCapture Projection,
        DirectionalShadeTransformChangeKind Kind);

    private sealed class DirectionalShadePartPoseCapture
    {
        private Matrix4x4[] _pieces;

        private DirectionalShadePartPoseCapture(Matrix4x4[] pieces) =>
            _pieces = pieces;

        internal int Count => _pieces.Length;

        internal static DirectionalShadePartPoseCapture Capture(
            in RenderMirrorRecord capture)
        {
            IReadOnlyList<MacAC.Mechanics.Realm.TriMeshRef>? triMeshes =
                capture.EntityPayload.MeshRefs;
            Matrix4x4[] pieces = new Matrix4x4[triMeshes?.Count ?? 0];
            for (int ordinal = 0; ordinal < pieces.Length; ++ordinal)
                pieces[ordinal] = triMeshes![ordinal].PartTransform;
            return new DirectionalShadePartPoseCapture(pieces);
        }

        internal bool GrabLatest(in RenderMirrorRecord capture)
        {
            IReadOnlyList<MacAC.Mechanics.Realm.TriMeshRef>? triMeshes =
                capture.EntityPayload.MeshRefs;
            int tally = triMeshes?.Count ?? 0;
            bool altered = _pieces.Length != tally;
            if (altered)
                _pieces = new Matrix4x4[tally];
            for (int ordinal = 0; ordinal < tally; ++ordinal)
            {
                Matrix4x4 latest = triMeshes![ordinal].PartTransform;
                if (!MatrixBitsetEqual(in _pieces[ordinal], in latest))
                {
                    _pieces[ordinal] = latest;
                    altered = true;
                }
            }
            return altered;
        }

        private static bool MatrixBitsetEqual(
            in Matrix4x4 left,
            in Matrix4x4 right)
        {
            var leftSpan = MemoryMarshal.CreateReadOnlySpan(
                in left,
                1);
            var rightSpan = MemoryMarshal.CreateReadOnlySpan(
                in right,
                1);
            return MemoryMarshal.AsBytes(leftSpan).SequenceEqual(
                MemoryMarshal.AsBytes(rightSpan));
        }
    }

    private readonly record struct ExteriorStaticMarker;

    private readonly record struct InsideChamberStaticMarker;

    private readonly record struct OnlineDynamicRootTag;

    private readonly record struct EngagedMovingStaticMarker;

    private readonly record struct EquippedDescendantMarker;

    private readonly record struct TableauListing(
        Entity Entity,
        RasterizeHolderIncarnation OwnerIncarnation,
        RenderMirrorClass ProjectionClass);

    private readonly record struct MirrorLookupSlotEstimate(
        int HashCode,
        int Next,
        RenderMirrorId Key,
        TableauListing Entry);

    private struct ApplyResultAssembler
    {
        public int Imposed;
        public int Registered;
        public int Updated;
        public int Replaced;
        public int Unregistered;
        public int RejectedGen;
        public int RejectedOutOfOrderingSeries;
        public int RejectedStaleIncarnation;
        public int RejectedAbsent;

        public readonly RenderDiffApplyResult Build()
        {
            return new(
                Imposed,
                Registered,
                Updated,
                Replaced,
                Unregistered,
                RejectedGen,
                RejectedOutOfOrderingSeries,
                RejectedStaleIncarnation,
                RejectedAbsent);
        }
    }

    private sealed class RenderMirrorRecordComparer :
        IComparer<RenderMirrorRecord>
    {
        public static RenderMirrorRecordComparer Instance { get; } = new();

        public int Compare(
            RenderMirrorRecord x,
            RenderMirrorRecord y)
        {
            int val = x.Id.CompareTo(y.Id);
            return val is not 0
                ? val
                : x.ProjectionClass.CompareTo(y.ProjectionClass);
        }
    }
}
