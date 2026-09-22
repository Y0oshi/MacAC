using System.Numerics;
using System.Runtime.CompilerServices;
using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Geometry;

namespace MacAC.Client.Graphics.Batching;

internal sealed partial class DirectionalShadePreparedDraws
{
    public RenderStageEpoch SrcGen { get; private set; }

    public ulong SrcInvokerAssembleSeries { get; private set; }

    public long SrcRasterizeBlobReadinessVer { get; private set; }

    public ulong SrcSeeThroughFadeRev { get; private set; }

    public ulong SrcInvokerPickSeries { get; private set; }

    public ulong EngagedPickSeries { get; private set; }

    public int PreviousDynamicXformRenewTally { get; private set; }

    public bool PreviousDynamicXformRenewWasDense { get; private set; }

    public int SolidDirectiveTally { get; private set; }

    public int AlphaCutoutDirectiveTally => _directiveTally - SolidDirectiveTally;

    public int SolidExecTally { get; private set; }

    public ReadOnlySpan<Matrix4x4> Xforms =>
        _xforms.AsSpan(0, _srcTally);

    public ReadOnlySpan<int> DynamicXformSockets =>
        _dynamicXformSockets.AsSpan(0, _dynamicXformSocketTally);

    public ReadOnlySpan<int> AllDynamicXformSockets =>
        _allDynamicXformSockets.AsSpan(0, _allDynamicXformSocketTally);

    public ReadOnlySpan<DrawElementsIndirectDirective> Commands =>
        _commands.AsSpan(0, _directiveTally);

    public ReadOnlySpan<DrawElementsIndirectDirective> SolidDirectives =>
        _commands.AsSpan(0, SolidDirectiveTally);

    public ReadOnlySpan<DrawElementsIndirectDirective> AlphaCutoutDirectives =>
        _commands.AsSpan(SolidDirectiveTally, AlphaCutoutDirectiveTally);

    public ReadOnlySpan<DirectionalShadePreparedBatch> Batches =>
        _lots.AsSpan(0, _directiveTally);

    public ReadOnlySpan<DirectionalShadePreparedBatch> SolidLots =>
        _lots.AsSpan(0, SolidDirectiveTally);

    public ReadOnlySpan<DirectionalShadePreparedBatch> AlphaCutoutLots =>
        _lots.AsSpan(SolidDirectiveTally, AlphaCutoutDirectiveTally);

    public ReadOnlySpan<DirectionalShadePreparedRun> Runs =>
        _executions.AsSpan(0, _execTally);

    public ReadOnlySpan<DirectionalShadePreparedRun> SolidExecutions =>
        _executions.AsSpan(0, SolidExecTally);

    public ReadOnlySpan<DirectionalShadePreparedRun> AlphaCutoutExecutions =>
        _executions.AsSpan(SolidExecTally, _execTally - SolidExecTally);

    public int EngagedSolidDirectiveTally { get; private set; }

    public int EngagedAlphaCutoutDirectiveTally =>
        _engagedDirectiveTally - EngagedSolidDirectiveTally;

    public int EngagedSolidExecTally { get; private set; }

    public ReadOnlySpan<DrawElementsIndirectDirective> EngagedDirectives =>
        _engagedDirectives.AsSpan(0, _engagedDirectiveTally);

    public ReadOnlySpan<DirectionalShadePreparedBatch> EngagedLots =>
        _engagedLots.AsSpan(0, _engagedDirectiveTally);

    public ReadOnlySpan<DirectionalShadePreparedRun> EngagedSolidExecutions =>
        _engagedExecutions.AsSpan(0, EngagedSolidExecTally);

    public ReadOnlySpan<DirectionalShadePreparedRun> EngagedAlphaCutoutExecutions
    {
        get
        {
            return _engagedExecutions.AsSpan(
            EngagedSolidExecTally,
            _engagedExecTally - EngagedSolidExecTally);
        }
    }

    public DirectionalShadePreparationStats Stats { get; private set; }

    public long KeptTempOctets
    {
        get
        {
            return checked(
        (long)_src.Length * Unsafe.SizeOf<DirectionalShadeSourceDraw>()
        + (long)_xforms.Length * Unsafe.SizeOf<Matrix4x4>()
        + (long)_xformSrcs.Length
            * Unsafe.SizeOf<DirectionalShadeTransformSource>()
        + (long)_dynamicXformSockets.Length * sizeof(int)
        + (long)_allDynamicXformSockets.Length * sizeof(int)
        + (long)_leadDynamicXformByInvoker.Length * sizeof(int)
        + (long)_upcomingDynamicXform.Length * sizeof(int)
        + (long)_denseAlteredPostureByInvoker.Length * sizeof(int)
        + (long)_mappedInvokerIdents.Length * Unsafe.SizeOf<RenderMirrorId>()
        + (long)_mappedInvokerClasses.Length
            * Unsafe.SizeOf<RenderMirrorClass>()
        + _mappedInvokerPersonaPresent.Length
        + (long)_commands.Length * Unsafe.SizeOf<DrawElementsIndirectDirective>()
        + (long)_lots.Length * Unsafe.SizeOf<DirectionalShadePreparedBatch>()
        + (long)_executions.Length * Unsafe.SizeOf<DirectionalShadePreparedRun>()
        + (long)_engagedDirectives.Length * Unsafe.SizeOf<DrawElementsIndirectDirective>()
        + (long)_engagedLots.Length * Unsafe.SizeOf<DirectionalShadePreparedBatch>()
        + (long)_engagedExecutions.Length * Unsafe.SizeOf<DirectionalShadePreparedRun>()
        + (long)_paintUpcomingInCluster.Length * sizeof(int)
        + (long)_clusterFront.Length * sizeof(int)
        + (long)_clusterRear.Length * sizeof(int)
        + (long)_clusterTallyByCluster.Length * sizeof(int)
        + (long)_clusterTagHi.Length * sizeof(ulong)
        + (long)_clusterTagLo.Length * sizeof(ulong)
        + (long)_clusterLeadPaint.Length * sizeof(int)
        + (long)_clusterOrdering.Length * sizeof(int)
        + (long)_clusterByTag.EnsureCapacity(0)
            * (sizeof(int)
                + Unsafe.SizeOf<KeyValuePair<DirectionalShadeDrawKey, int>>()));
        }
    }

    public bool RequiresWiringAssemble(
        RenderStageEpoch gen,
        ulong invokerAssembleSeries,
        long rasterizeBlobReadinessVer = 0,
        ulong seeThroughFadeRev = 0)
    {
        return SrcGen != gen
        || SrcInvokerAssembleSeries != invokerAssembleSeries
        || SrcRasterizeBlobReadinessVer
            != rasterizeBlobReadinessVer
        || SrcSeeThroughFadeRev != seeThroughFadeRev
        || _reattemptTaxonomyUpcomingCycle;
    }

    public void Cancel()
    {
        _srcTally = 0;
        _directiveTally = 0;
        _execTally = 0;
        _engagedDirectiveTally = 0;
        _engagedExecTally = 0;
        _dynamicXformSocketTally = 0;
        _allDynamicXformSocketTally = 0;
        if (_mappedInvokerTally is not 0)
        {
            Array.Clear(
                _mappedInvokerPersonaPresent,
                0,
                _mappedInvokerTally);
        }
        _mappedInvokerTally = 0;
        SolidDirectiveTally = 0;
        SolidExecTally = 0;
        EngagedSolidDirectiveTally = 0;
        EngagedSolidExecTally = 0;
        Stats = default;
        SrcGen = default;
        SrcInvokerAssembleSeries = 0;
        SrcRasterizeBlobReadinessVer = 0;
        SrcSeeThroughFadeRev = 0;
        SrcInvokerPickSeries = 0;
        PreviousDynamicXformRenewTally = 0;
        PreviousDynamicXformRenewWasDense = false;
        _reattemptTaxonomyUpcomingCycle = false;
        _structure = false;
    }

    public bool TryCommence(
        RenderStageEpoch gen,
        ulong casterBuildSequence,
        int estimatedInsts,
        long rasterizeBlobReadinessVer = 0,
        ulong seeThroughFadeRev = 0)
    {
        if (_structure)
            throw new InvalidOperationException(
                "A directional-shadow draw build is by now active");
        if (casterBuildSequence is 0)
            throw new ArgumentOutOfRangeException(nameof(casterBuildSequence));
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedInsts);
        if (!RequiresWiringAssemble(
                gen,
                casterBuildSequence,
                rasterizeBlobReadinessVer,
                seeThroughFadeRev))

            return false;

        SecureCap(ref _src, estimatedInsts);
        _srcTally = 0;
        _directiveTally = 0;
        _execTally = 0;
        _engagedDirectiveTally = 0;
        _engagedExecTally = 0;
        _dynamicXformSocketTally = 0;
        _allDynamicXformSocketTally = 0;
        if (_mappedInvokerTally is not 0)
        {
            Array.Clear(
                _mappedInvokerPersonaPresent,
                0,
                _mappedInvokerTally);
        }
        _mappedInvokerTally = 0;
        SolidDirectiveTally = 0;
        SolidExecTally = 0;
        EngagedSolidDirectiveTally = 0;
        EngagedSolidExecTally = 0;
        Stats = default;
        PreviousDynamicXformRenewTally = 0;
        PreviousDynamicXformRenewWasDense = false;
        _structure = true;
        return true;
    }

    public ulong AssembleSeries { get; private set; }

    public void ChartInvokerPersona(
        int invokerOrdinal,
        RenderMirrorId ident,
        RenderMirrorClass projClass)
    {
        if (!_structure)
        {
            throw new InvalidOperationException(
                "Begin a directional-shadow draw build prior to mapping casters");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(invokerOrdinal);
        int needed = checked(invokerOrdinal + 1);
        SecureCap(ref _mappedInvokerIdents, needed);
        SecureCap(ref _mappedInvokerClasses, needed);
        SecureCap(ref _mappedInvokerPersonaPresent, needed);
        if (_mappedInvokerPersonaPresent[invokerOrdinal]
            && (_mappedInvokerIdents[invokerOrdinal] != ident
                || _mappedInvokerClasses[invokerOrdinal] != projClass))
        {
            throw new InvalidOperationException(
                $"Directional-shadow caster slot {invokerOrdinal} was mapped twice "
                + "with different projection identities");
        }
        _mappedInvokerIdents[invokerOrdinal] = ident;
        _mappedInvokerClasses[invokerOrdinal] = projClass;
        _mappedInvokerPersonaPresent[invokerOrdinal] = true;
        _mappedInvokerTally = Math.Max(_mappedInvokerTally, needed);
    }

    public void Add(
        uint leadOrdinal,
        int baseVert,
        int ordinalTally,
        GpuTextureSlot textureSocket,
        uint textureStratum,
        FaceCulling pruneManner,
        DirectionalShadeCasterMaterial matl,
        in Matrix4x4 xform,
        uint foliageFlagSet = 0u)
    {
        DirectionalShadeTransformSource src = default;
        Add(
            leadOrdinal,
            baseVert,
            ordinalTally,
            textureSocket,
            textureStratum,
            pruneManner,
            matl,
            in xform,
            in src,
            foliageFlagSet);
    }

    public void Add(
        uint leadOrdinal,
        int baseVert,
        int indexCount,
        GpuTextureSlot textureSlot,
        uint textureStratum,
        FaceCulling pruneManner,
        DirectionalShadeCasterMaterial matl,
        in Matrix4x4 xform,
        in DirectionalShadeTransformSource xformSrc,
        uint foliageFlagSet = 0u)
    {
        if (!_structure)
            throw new InvalidOperationException(
                "Begin a directional-shadow draw build prior to adding batches");
        if (indexCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount));
        if (matl is DirectionalShadeCasterMaterial.AlphaCutout
            && !textureSlot.IsAssigned)
        {
            throw new ArgumentException(
                "An alpha-cutout caster needs an assigned texture slot",
                nameof(textureSlot));
        }

        SecureCap(ref _src, checked(_srcTally + 1));
        _src[_srcTally++] = new DirectionalShadeSourceDraw(
            new DirectionalShadeDrawKey(
                leadOrdinal,
                baseVert,
                indexCount,
                matl is DirectionalShadeCasterMaterial.AlphaCutout
                    ? textureSlot
                    : GpuTextureSlot.Unassigned,
                matl is DirectionalShadeCasterMaterial.AlphaCutout
                    ? textureStratum
                    : 0u,
                pruneManner,
                matl,
                foliageFlagSet),
            xform,
            xformSrc);
    }

    internal static bool FadeExcludesInvoker(float seeThrough) =>
        !float.IsFinite(seeThrough) || seeThrough > 0f;

    internal static bool TryClassifyMatl(
        SeeThroughKind seeThrough,
        out DirectionalShadeCasterMaterial matl)
    {
        switch (seeThrough)
        {
            case SeeThroughKind.Opaque:
                matl = DirectionalShadeCasterMaterial.Opaque;
                return true;
            case SeeThroughKind.ClipMap:
                matl = DirectionalShadeCasterMaterial.AlphaCutout;
                return true;
            default:
                matl = default;
                return false;
        }
    }

    internal void ApplySelection(DirectionalShadeCasterFrame casters)
    {
        ArgumentNullException.ThrowIfNull(casters);
        if (casters.PickSeries is 0)
            return;
        if (SrcGen != casters.Generation
            || SrcInvokerAssembleSeries != casters.AssembleSequence)
        {
            throw new InvalidOperationException(
                "Directional-shadow selection doesn't match prepared topology");
        }
        if (SrcInvokerPickSeries == casters.PickSeries)
            return;

        ApplySelection(casters.ChosenCasters, casters.PickSeries);
    }

    internal void ApplySelection(
        ReadOnlySpan<bool> chosen,
        ulong casterSelectionSequence)
    {
        if (casterSelectionSequence is 0)
            throw new ArgumentOutOfRangeException(nameof(casterSelectionSequence));
        if (SrcInvokerPickSeries == casterSelectionSequence)
            return;

        _engagedDirectiveTally = 0;
        int engagedInsts = 0;
        for (int directiveOrdinal = 0; directiveOrdinal < _directiveTally; ++directiveOrdinal)
        {
            var directive = _commands[directiveOrdinal];
            int lead = checked((int)directive.BaseInst);
            int finish = checked(lead + (int)directive.InstTally);
            int execBegin = -1;
            for (int xformOrdinal = lead; xformOrdinal < finish; ++xformOrdinal)
            {
                int invokerOrdinal = _xformSrcs[xformOrdinal].CasterIndex;
                if ((uint)invokerOrdinal >= (uint)chosen.Length)
                {
                    throw new InvalidOperationException(
                        "Prepared directional-shadow instance has a stale caster slot");
                }
                bool engaged = chosen[invokerOrdinal];
                if (engaged && execBegin < 0)
                    execBegin = xformOrdinal;
                if (!engaged && execBegin >= 0)
                {
                    WriteEngaged(directiveOrdinal, in directive, execBegin, xformOrdinal - execBegin);
                    engagedInsts += xformOrdinal - execBegin;
                    execBegin = -1;
                }
            }
            if (execBegin >= 0)
            {
                WriteEngaged(directiveOrdinal, in directive, execBegin, finish - execBegin);
                engagedInsts += finish - execBegin;
            }
        }

        AssembleEngagedExecutions();
        SrcInvokerPickSeries = casterSelectionSequence;
        EngagedPickSeries = checked(EngagedPickSeries + 1);
        Stats = Stats with
        {
            ActiveInstances = engagedInsts,
            ActiveCommands = _engagedDirectiveTally,
        };
    }

    private void AssembleEngagedExecutions()
    {
        _engagedExecTally = 0;
        EngagedSolidDirectiveTally = 0;
        while (EngagedSolidDirectiveTally < _engagedDirectiveTally
               && _engagedLots[EngagedSolidDirectiveTally].Material
                    is DirectionalShadeCasterMaterial.Opaque)
        {
            ++EngagedSolidDirectiveTally;
        }

        int execBegin = 0;
        while (execBegin < _engagedDirectiveTally)
        {
            var lead = _engagedLots[execBegin];
            int execFinish = execBegin + 1;
            while (execFinish < _engagedDirectiveTally
                   && _engagedLots[execFinish].CullMode == lead.CullMode
                   && _engagedLots[execFinish].Material == lead.Material)
            {
                ++execFinish;
            }
            _engagedExecutions[_engagedExecTally++] = new DirectionalShadePreparedRun(
                execBegin,
                execFinish - execBegin,
                lead.CullMode,
                lead.Material);
            execBegin = execFinish;
        }
        EngagedSolidExecTally = 0;
        while (EngagedSolidExecTally < _engagedExecTally
               && _engagedExecutions[EngagedSolidExecTally].Material
                    is DirectionalShadeCasterMaterial.Opaque)
        {
            ++EngagedSolidExecTally;
        }
    }

    private void ReassembleEngagedAll()
    {
        SecureCap(ref _engagedDirectives, _directiveTally);
        SecureCap(ref _engagedLots, _directiveTally);
        SecureCap(ref _engagedExecutions, _execTally);
        _commands.AsSpan(0, _directiveTally).CopyTo(_engagedDirectives);
        _lots.AsSpan(0, _directiveTally).CopyTo(_engagedLots);
        _engagedDirectiveTally = _directiveTally;
        AssembleEngagedExecutions();
        SrcInvokerPickSeries = 0;
        EngagedPickSeries = checked(EngagedPickSeries + 1);
        Stats = Stats with
        {
            ActiveInstances = _srcTally,
            ActiveCommands = _engagedDirectiveTally,
        };
    }

    private void WriteEngaged(
        int srcDirectiveOrdinal,
        in DrawElementsIndirectDirective src,
        int baseInst,
        int instTally)
    {
        int dest = _engagedDirectiveTally++;
        _engagedDirectives[dest] = src with
        {
            BaseInst = checked((uint)baseInst),
            InstTally = checked((uint)instTally),
        };
        _engagedLots[dest] = _lots[srcDirectiveOrdinal];
    }

    private void VetAlteredPosture(
        in DirectionalShadeChangedPose altered,
        int invokerOrdinal)
    {
        if ((uint)invokerOrdinal >= (uint)_mappedInvokerTally
            || !_mappedInvokerPersonaPresent[invokerOrdinal])
        {
            throw new InvalidOperationException(
                "Directional-shadow changed pose has a stale or unmapped caster index");
        }

        ref readonly DirectionalShadeTransformCapture posture =
            ref altered.Capture;
        if (posture.Id != _mappedInvokerIdents[invokerOrdinal]
            || posture.ProjClass != _mappedInvokerClasses[invokerOrdinal])
        {
            throw new InvalidOperationException(
                $"Directional-shadow changed pose {posture.Id} doesn't match "
                + $"retained caster {_mappedInvokerIdents[invokerOrdinal]} at "
                + $"slot {invokerOrdinal}.");
        }
    }
}
