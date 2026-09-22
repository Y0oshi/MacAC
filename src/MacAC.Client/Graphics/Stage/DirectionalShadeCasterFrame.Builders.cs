namespace MacAC.Client.Graphics.Stage;

internal sealed partial class DirectionalShadeCasterFrame
{
    public ulong AssembleSequence { get; private set; }

    public void Build(in RenderStageProbe ask, bool allowWiringReassemble = true)
    {
        ulong wiringRev = ask.DirectedShadeWiringRev;
        bool latest = AssembleSequence is not 0
            && Generation == ask.Generation
            && _wiringRev == wiringRev;
        bool deferStale = !allowWiringReassemble
            && !latest
            && AssembleSequence is not 0
            && Generation == ask.Generation
            && !RenewRequiresWholeDuplicate(in ask);
        if (latest || deferStale)
        {
            int refreshes = RenewAlteredXforms(
                in ask,
                tolerateStaleWiring: deferStale);
            Stats = Stats with
            {
                IndexCopies = 0,
                Classifications = 0,
                DynamicTransformRefreshes = refreshes,
                TopologyRebuilt = false,
                CopiedTransformChanges = _previousXformEdits.Count,
                DedupedChangedCasterSlots = _alteredInvokerPostureTally,
                TransformJournalFullRefresh =
                    _previousXformEdits.RequiresFullRefresh,
                DensityBulkRefresh = _previousDensityBulkRenew,
                BatchedProjectionCopyCalls = _previousBatchedProjDuplicateCalls,
                UpdateTransformChanges =
                    _previousXformEdits.UpdateTransformCount,
                UpdateAppearanceChanges =
                    _previousXformEdits.UpdateAppearanceCount,
                DynamicSynchronizationChanges =
                    _previousXformEdits.DynamicSynchronizationCount,
                ActiveAnimatedStaticChanges =
                    _previousXformEdits.ActiveAnimatedStaticCount,
                LiveDynamicRootChanges =
                    _previousXformEdits.LiveDynamicRootCount,
                EquippedChildChanges =
                    _previousXformEdits.EquippedChildCount,
            };
            return;
        }

        var counts = ask.OrdinalCounts;
        SecureCap(ref _exteriorStaticTemp, counts.OutdoorStatic);
        SecureCap(ref _exteriorDynamicTemp, counts.OutdoorDynamic);
        int staticTally = ask.DuplicateOrdinalTo(
            RenderStageIndex.OutdoorStatic,
            _exteriorStaticTemp.AsSpan(0, counts.OutdoorStatic));
        int dynamicTally = ask.DuplicateOrdinalTo(
            RenderStageIndex.OutdoorDynamic,
            _exteriorDynamicTemp.AsSpan(0, counts.OutdoorDynamic));
        SecureCap(ref _casters, checked(staticTally + dynamicTally));
        SecureCap(ref _chosenCasters, checked(staticTally + dynamicTally));
        _invokerTally = 0;

        int rejectedNotDrawable = 0;
        int rejectedNotHoused = 0;
        int rejectedSeeThru = 0;
        int rejectedInside = 0;
        int rejectedAbsentTriMesh = 0;
        int exteriorStatics = 0;
        int structures = 0;
        int movingStatics = 0;
        int ownAvatars = 0;
        int distantAvatars = 0;
        int nonAvatarBeasts = 0;
        int anotherOnlineDynamics = 0;
        int equippedDescendants = 0;
        for (int idx = 0; idx < staticTally; ++idx)
            Append(_exteriorStaticTemp[idx]);
        for (int idx = 0; idx < dynamicTally; ++idx)
            Append(_exteriorDynamicTemp[idx]);

        OrderCasters();
        int renewInvokerTally = 0;
        for (int invokerOrdinal = 0; invokerOrdinal < _invokerTally; ++invokerOrdinal)
        {
            if (_casters[invokerOrdinal].UsesLatestMovingXforms)
                ++renewInvokerTally;
        }
        SecureCap(ref _renewInvokerSockets, renewInvokerTally);
        SecureCap(ref _alteredInvokerPostures, renewInvokerTally);
        SecureCap(ref _alteredInvokerFlagSet, _invokerTally);
        SecureCap(ref _invokerIdents, _invokerTally);
        SecureCap(ref _invokerClasses, _invokerTally);
        SecureCap(ref _denseIdentTemp, renewInvokerTally);
        SecureCap(ref _denseCaptureTemp, renewInvokerTally);
        _renewInvokerSocketTally = 0;
        _alteredInvokerPostureTally = 0;
        _renewInvokerSocketByIdent.Clear();
        _renewInvokerSocketByIdent.EnsureCapacity(renewInvokerTally);
        for (int invokerOrdinal = 0; invokerOrdinal < _invokerTally; ++invokerOrdinal)
        {
            _invokerIdents[invokerOrdinal] = _casters[invokerOrdinal].Projection.Id;
            _invokerClasses[invokerOrdinal] =
                _casters[invokerOrdinal].Projection.ProjectionClass;
            if (_casters[invokerOrdinal].UsesLatestMovingXforms)
            {
                _renewInvokerSockets[_renewInvokerSocketTally++] = invokerOrdinal;
                _renewInvokerSocketByIdent.Add(
                    _invokerIdents[invokerOrdinal],
                    invokerOrdinal);
            }
        }
        Generation = ask.Generation;
        _wiringRev = wiringRev;
        XformRev = ask.DirectedShadeXformRev;
        _previousXformEdits = default;
        _previousDensityBulkRenew = false;
        _previousBatchedProjDuplicateCalls = 0;
        AssembleSequence = checked(AssembleSequence + 1);
        Stats = new DirectionalShadeCasterBuildStats(
            staticTally,
            dynamicTally,
            _invokerTally,
            rejectedNotDrawable,
            rejectedNotHoused,
            rejectedSeeThru,
            rejectedInside,
            rejectedAbsentTriMesh,
            IndexCopies: 2,
            Classifications: _invokerTally,
            DynamicTransformRefreshes: 0,
            TopologyRebuilt: true)
        {
            InvokerClasses = new DirectionalShadeCasterClassTelemetry(
                TerrainCommands: 0,
                exteriorStatics,
                structures,
                movingStatics,
                ownAvatars,
                distantAvatars,
                nonAvatarBeasts,
                anotherOnlineDynamics,
                equippedDescendants),
        };
        return;

        void Append(in RenderMirrorRecord proj)
        {
            if ((proj.Flags & RenderMirrorFlags.Draw) == 0)
            {
                ++rejectedNotDrawable;
                return;
            }
            if ((proj.Flags & RenderMirrorFlags.SpatiallyResident) == 0)
            {
                ++rejectedNotHoused;
                return;
            }
            if ((proj.Flags & RenderMirrorFlags.Translucent) != 0)
            {
                ++rejectedSeeThru;
                return;
            }
            if (proj.Source.ParentCellId is not 0
                && InteriorActorPartition.IsIndoorCellId(
                    proj.Source.ParentCellId))
            {
                ++rejectedInside;
                return;
            }
            if (proj.MeshSet.MeshCount <= 0
                || proj.EntityPayload.MeshRefs is null
                || proj.EntityPayload.MeshRefs.Count is 0)
            {
                ++rejectedAbsentTriMesh;
                return;
            }

            var kind = Classify(in proj);
            _casters[_invokerTally++] = new DirectionalShadeCaster(
                proj,
                kind);
            switch (kind)
            {
                case DirectionalShadeCasterKind.OutdoorStatic:
                    ++exteriorStatics;
                    break;
                case DirectionalShadeCasterKind.Building:
                    ++structures;
                    break;
                case DirectionalShadeCasterKind.AnimatedStatic:
                    ++movingStatics;
                    break;
                case DirectionalShadeCasterKind.EquippedChild:
                    ++equippedDescendants;
                    break;
                case DirectionalShadeCasterKind.LiveDynamic:
                    switch (proj.EntityPayload.CasterIdentity)
                    {
                        case RasterizeInvokerPersonaFlavor.LocalPlayer:
                            ++ownAvatars;
                            break;
                        case RasterizeInvokerPersonaFlavor.RemotePlayer:
                            ++distantAvatars;
                            break;
                        case RasterizeInvokerPersonaFlavor.NonPlayerCreature:
                            ++nonAvatarBeasts;
                            break;
                        default:
                            ++anotherOnlineDynamics;
                            break;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, "Unrecognized shadow caster kind");
            }
        }
    }
}
