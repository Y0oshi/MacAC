using MacAC.Client.Graphics.Effects;

namespace MacAC.Client.Graphics.Stage;

internal sealed partial class DirectionalShadeCasterFrame
{
    public RenderStageEpoch Generation { get; private set; }

    public ulong PickSeries { get; private set; }

    public ReadOnlySpan<DirectionalShadeCaster> Casters =>
        _casters.AsSpan(0, _invokerTally);

    internal ReadOnlySpan<bool> ChosenCasters =>
        _chosenCasters.AsSpan(0, _invokerTally);

    internal ReadOnlySpan<DirectionalShadeChangedPose> AlteredInvokerPostures =>
        _alteredInvokerPostures.AsSpan(0, _alteredInvokerPostureTally);

    internal ulong XformRev { get; private set; }

    public DirectionalShadeCasterBuildStats Stats { get; private set; }

    public long KeptScratchBytes
    {
        get
        {
            return checked(
            (long)_exteriorStaticTemp.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<RenderMirrorRecord>()
            + (long)_exteriorDynamicTemp.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<RenderMirrorRecord>()
            + (long)_casters.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<DirectionalShadeCaster>()
            + _chosenCasters.Length
            + (long)_renewInvokerSockets.Length * sizeof(int)
            + (long)_alteredInvokerPostures.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<
                    DirectionalShadeChangedPose>()
            + _alteredInvokerFlagSet.Length
            + (long)_invokerIdents.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<
                    RenderMirrorId>()
            + (long)_invokerClasses.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<
                    RenderMirrorClass>()
            + (long)_denseIdentTemp.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<RenderMirrorId>()
            + (long)_denseCaptureTemp.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<RenderMirrorRecord>()
            + (long)_xformEditTemp.Length
                * System.Runtime.CompilerServices.Unsafe.SizeOf<
                    DirectionalShadeTransformCapture>()
            + (long)_renewInvokerSocketByIdent.EnsureCapacity(0)
                * (sizeof(int)
                    + System.Runtime.CompilerServices.Unsafe.SizeOf<
                        KeyValuePair<RenderMirrorId, int>>()));
        }
    }

    internal void Select(
        in CanonLandscapeVisibilityFrame vis,
        IDirectionalShadeCellMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        IReadOnlySet<uint> shown = vis.CellIds
            ?? CanonLandscapeVisibilityFrame.None.CellIds;
        int chosen = 0;
        for (int invokerOrdinal = 0; invokerOrdinal < _invokerTally; ++invokerOrdinal)
        {
            ref readonly DirectionalShadeCaster invoker =
                ref _casters[invokerOrdinal];
            bool engaged = vis.HasCompletedWorldView
                && SelectsInvoker(in invoker, shown, membership);
            _chosenCasters[invokerOrdinal] = engaged;
            if (engaged)
                ++chosen;
        }

        PickSeries = checked(PickSeries + 1);
        Stats = Stats with { ActiveSelected = chosen };
    }

    private static bool SelectsInvoker(
        in DirectionalShadeCaster invoker,
        IReadOnlySet<uint> shown,
        IDirectionalShadeCellMembership membership)
    {
        var src = invoker.Projection.Source;
        if (invoker.Kind is DirectionalShadeCasterKind.Building)
            return IsExteriorLandChamber(src.EffectCellId)
                && shown.Contains(src.EffectCellId);

        if (!membership.TryFetchCanonChamberArr(
                src.LocalEntityId,
                out IReadOnlyList<uint>? chambers)
            || chambers.Count is 0)

            return false;

        for (int chamberOrdinal = 0; chamberOrdinal < chambers.Count; ++chamberOrdinal)
        {
            uint chamberIdent = chambers[chamberOrdinal];
            if (IsExteriorLandChamber(chamberIdent) && shown.Contains(chamberIdent))
                return true;
        }
        return false;
    }

    private static bool IsExteriorLandChamber(uint chamberIdent)
    {
        uint lo = chamberIdent & 0xFFFFu;
        return lo is not 0u and < 0x0100u;
    }

    private void VetStablePosture(
        in DirectionalShadeTransformCapture latest,
        int invokerOrdinal)
    {
        if (latest.Id != _invokerIdents[invokerOrdinal]
            || latest.ProjClass != _invokerClasses[invokerOrdinal])
        {
            throw new InvalidOperationException(
                $"Stable directional-shadow topology changed caster "
                + $"{_invokerIdents[invokerOrdinal]} identity or class");
        }
    }

    private static DirectionalShadeCasterKind Classify(
        in RenderMirrorRecord proj)
    {
        return proj.EntityPayload.IsBuildingShell
            ? DirectionalShadeCasterKind.Building
            : proj.ProjectionClass switch
            {
                RenderMirrorClass.OutdoorStatic =>
                    DirectionalShadeCasterKind.OutdoorStatic,
                RenderMirrorClass.ActiveAnimatedStatic =>
                    DirectionalShadeCasterKind.AnimatedStatic,
                RenderMirrorClass.LiveDynamicRoot =>
                    DirectionalShadeCasterKind.LiveDynamic,
                RenderMirrorClass.EquippedChild =>
                    DirectionalShadeCasterKind.EquippedChild,
                _ => throw new InvalidOperationException(
                    $"Outdoor shadow index carried not supported {proj.ProjectionClass}."),
            };
    }

    private void OrderCasters()
    {
        int tally = _invokerTally;
        SecureCap(ref _orderOrdinals, tally);
        SecureCap(ref _orderTags, tally);
        SecureCap(ref _orderTemp, tally);
        for (int idx = 0; idx < tally; ++idx)
        {
            _orderTags[idx] = _casters[idx].Projection.SortKey.Value;
            _orderOrdinals[idx] = idx;
        }
        _orderOrdinals.AsSpan(0, tally).Sort(
            new InvokerOrdinalOrdering(_orderTags, _casters));
        for (int idx = 0; idx < tally; ++idx)
            _orderTemp[idx] = _casters[_orderOrdinals[idx]];
        (_casters, _orderTemp) = (_orderTemp, _casters);
    }
}
