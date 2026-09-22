using MacAC.Client.Graphics.Stage;

namespace MacAC.Client.Graphics.Batching;

internal sealed partial class DirectionalShadePreparedDraws
{
    public void Complete(
        RenderStageEpoch gen,
        ulong casterBuildSequence,
        in DirectionalShadePreparationStats stats,
        long rasterizeBlobReadinessVer = 0,
        ulong seeThroughFadeRev = 0)
    {
        if (!_structure)
            throw new InvalidOperationException(
                "No directional-shadow draw build is active");
        if (casterBuildSequence is 0)
            throw new ArgumentOutOfRangeException(nameof(casterBuildSequence));

        int clusterTally = 0;
        _clusterByTag.Clear();
        SecureCap(ref _paintUpcomingInCluster, _srcTally);
        SecureCap(ref _clusterFront, _srcTally);
        SecureCap(ref _clusterRear, _srcTally);
        SecureCap(ref _clusterTallyByCluster, _srcTally);
        SecureCap(ref _clusterTagHi, _srcTally);
        SecureCap(ref _clusterTagLo, _srcTally);
        SecureCap(ref _clusterLeadPaint, _srcTally);
        SecureCap(ref _clusterOrdering, _srcTally);
        for (int idx = 0; idx < _srcTally; ++idx)
        {
            var tag = _src[idx].Key;
            if (_clusterByTag.TryGetValue(tag, out int cluster))
            {
                _paintUpcomingInCluster[_clusterRear[cluster]] = idx;
                _clusterRear[cluster] = idx;
            }
            else
            {
                cluster = clusterTally++;
                _clusterByTag.Add(tag, cluster);
                _clusterFront[cluster] = idx;
                _clusterRear[cluster] = idx;
                _clusterTallyByCluster[cluster] = 0;
                _clusterLeadPaint[cluster] = idx;
                _clusterTagHi[cluster] =
                    ((ulong)(byte)tag.Material << 62)
                    | ((ulong)((uint)tag.CullMode & 0x3u) << 60)
                    | ((ulong)tag.FirstIndex << 28)
                    | ((ulong)(uint)tag.BaseVertex & 0x0FFF_FFFFul);
                _clusterTagLo[cluster] =
                    ((ulong)Math.Min((uint)tag.IndexCount, 0xF_FFFFu) << 44)
                    | ((ulong)tag.TextureSlot.Index << 12)
                    | ((ulong)Math.Min(tag.TextureLayer, 0x3FFu) << 2)
                    | (tag.FoliageFlags & 0x3u);
            }
            _paintUpcomingInCluster[idx] = -1;
            _clusterTallyByCluster[cluster]++;
        }
        for (int g = 0; g < clusterTally; ++g)
            _clusterOrdering[g] = g;
        _clusterOrdering.AsSpan(0, clusterTally).Sort(
            new ClusterOrderComparer(_clusterTagHi, _clusterTagLo, _clusterLeadPaint));
        SecureCap(ref _xforms, _srcTally);
        SecureCap(ref _xformSrcs, _srcTally);
        SecureCap(ref _dynamicXformSockets, _srcTally);
        SecureCap(ref _allDynamicXformSockets, _srcTally);
        SecureCap(ref _upcomingDynamicXform, _srcTally);
        SecureCap(ref _commands, _srcTally);
        SecureCap(ref _lots, _srcTally);
        SecureCap(ref _executions, _srcTally);
        SecureCap(ref _engagedDirectives, _srcTally);
        SecureCap(ref _engagedLots, _srcTally);
        SecureCap(ref _engagedExecutions, _srcTally);

        int upperInvokerOrdinal = -1;
        for (int ordinal = 0; ordinal < _srcTally; ++ordinal)
        {
            var xformSrc =
                _src[ordinal].TransformSource;
            if (xformSrc.Refreshable)
                upperInvokerOrdinal = Math.Max(upperInvokerOrdinal, xformSrc.CasterIndex);
        }
        _mappedInvokerTally = Math.Max(_mappedInvokerTally, upperInvokerOrdinal + 1);
        SecureCap(ref _leadDynamicXformByInvoker, _mappedInvokerTally);
        SecureCap(ref _denseAlteredPostureByInvoker, _mappedInvokerTally);
        SecureCap(ref _mappedInvokerIdents, _mappedInvokerTally);
        SecureCap(ref _mappedInvokerClasses, _mappedInvokerTally);
        SecureCap(ref _mappedInvokerPersonaPresent, _mappedInvokerTally);
        if (_mappedInvokerTally is not 0)
        {
            Array.Fill(
                _leadDynamicXformByInvoker,
                -1,
                0,
                _mappedInvokerTally);
        }

        int xformOrdinal = 0;
        int directiveOrdinal = 0;
        int solidDirectives = 0;
        for (int orderingOrdinal = 0; orderingOrdinal < clusterTally; ++orderingOrdinal)
        {
            int cluster = _clusterOrdering[orderingOrdinal];
            var tag = _src[_clusterLeadPaint[cluster]].Key;
            int instTally = _clusterTallyByCluster[cluster];
            for (int paint = _clusterFront[cluster]; paint >= 0; paint = _paintUpcomingInCluster[paint])
            {
                _xforms[xformOrdinal++] = _src[paint].Transform;
                _xformSrcs[xformOrdinal - 1] =
                    _src[paint].TransformSource;
                if (_src[paint].TransformSource.Refreshable)
                {
                    int dynamicXformOrdinal = xformOrdinal - 1;
                    var xformSrc =
                        _src[paint].TransformSource;
                    if (xformSrc.CasterIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "A refreshable directional-shadow transform has a negative caster index");
                    }
                    _allDynamicXformSockets[_allDynamicXformSocketTally++] =
                        dynamicXformOrdinal;
                    _upcomingDynamicXform[dynamicXformOrdinal] =
                        _leadDynamicXformByInvoker[xformSrc.CasterIndex];
                    _leadDynamicXformByInvoker[xformSrc.CasterIndex] =
                        dynamicXformOrdinal;
                }
            }

            _commands[directiveOrdinal] = new DrawElementsIndirectDirective
            {
                Count = checked((uint)tag.IndexCount),
                InstTally = checked((uint)instTally),
                LeadOrdinal = tag.FirstIndex,
                BaseVert = tag.BaseVertex,
                BaseInst = checked((uint)(xformOrdinal - instTally)),
            };
            _lots[directiveOrdinal] = new DirectionalShadePreparedBatch(
                tag.TextureSlot,
                tag.TextureLayer,
                tag.CullMode,
                tag.Material,
                tag.FoliageFlags);
            if (tag.Material is DirectionalShadeCasterMaterial.Opaque)
                ++solidDirectives;
            ++directiveOrdinal;
        }

        _directiveTally = directiveOrdinal;
        SolidDirectiveTally = solidDirectives;
        int execCur = 0;
        int execBegin = 0;
        while (execBegin < directiveOrdinal)
        {
            var lead = _lots[execBegin];
            int execFinish = execBegin + 1;
            while (execFinish < directiveOrdinal
                   && _lots[execFinish].CullMode == lead.CullMode
                   && _lots[execFinish].Material == lead.Material)
            {
                ++execFinish;
            }
            _executions[execCur++] = new DirectionalShadePreparedRun(
                execBegin,
                execFinish - execBegin,
                lead.CullMode,
                lead.Material);
            execBegin = execFinish;
        }
        _execTally = execCur;
        while (SolidExecTally < execCur
               && _executions[SolidExecTally].Material is DirectionalShadeCasterMaterial.Opaque)
        {
            ++SolidExecTally;
        }
        SrcGen = gen;
        SrcInvokerAssembleSeries = casterBuildSequence;
        SrcRasterizeBlobReadinessVer = rasterizeBlobReadinessVer;
        SrcSeeThroughFadeRev = seeThroughFadeRev;
        AssembleSeries = checked(AssembleSeries + 1);
        PreviousDynamicXformRenewTally = 0;
        PreviousDynamicXformRenewWasDense = false;
        _dynamicXformSocketTally = 0;
        _reattemptTaxonomyUpcomingCycle =
            stats.UnresolvedAlphaCutoutTextures != 0;
        Stats = stats with
        {
            PreparedInstances = _srcTally,
            PreparedOpaqueCommands = solidDirectives,
            PreparedAlphaCutoutCommands = directiveOrdinal - solidDirectives,
        };
        _structure = false;
        ReassembleEngagedAll();
    }
}
