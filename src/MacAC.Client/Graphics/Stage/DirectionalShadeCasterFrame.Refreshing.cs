namespace MacAC.Client.Graphics.Stage;

internal sealed partial class DirectionalShadeCasterFrame
{
    internal ReadOnlySpan<int> RenewInvokerSockets =>
        _renewInvokerSockets.AsSpan(0, _renewInvokerSocketTally);

    private bool RenewRequiresWholeDuplicate(in RenderStageProbe ask)
    {
        if (ask.DirectedShadeXformRev == XformRev)
            return false;
        var edits =
            ask.DuplicateDirectedShadeXformEdits(
                XformRev,
                _xformEditTemp);
        return edits.RequiresFullRefresh;
    }

    private int RenewAlteredXforms(
        in RenderStageProbe ask,
        bool tolerateStaleWiring = false)
    {
        _alteredInvokerPostureTally = 0;
        ulong current = ask.DirectedShadeXformRev;
        if (current == XformRev)
        {
            _previousXformEdits = new DirectionalShadeTransformChanges(
                current,
                0,
                false);
            _previousDensityBulkRenew = false;
            _previousBatchedProjDuplicateCalls = 0;
            return 0;
        }

        var edits =
            ask.DuplicateDirectedShadeXformEdits(
                XformRev,
                _xformEditTemp);
        _previousXformEdits = edits;
        if (edits.RequiresFullRefresh)
        {
            if (tolerateStaleWiring)
            {
                throw new InvalidOperationException(
                    "A stale-topology refresh can't perform the dense full re-copy");
            }
            _previousBatchedProjDuplicateCalls = 1;
            for (int ordinal = 0; ordinal < _renewInvokerSocketTally; ++ordinal)
            {
                int invokerOrdinal = _renewInvokerSockets[ordinal];
                _denseIdentTemp[ordinal] = _casters[invokerOrdinal].Projection.Id;
            }
            int copied = ask.DuplicateByIdent(
                _denseIdentTemp.AsSpan(0, _renewInvokerSocketTally),
                _denseCaptureTemp.AsSpan(0, _renewInvokerSocketTally));
            if (copied != _renewInvokerSocketTally)
            {
                throw new InvalidOperationException(
                    "Dense directional-shadow refresh returned an incomplete record batch");
            }
            for (int ordinal = 0; ordinal < _renewInvokerSocketTally; ++ordinal)
            {
                int invokerOrdinal = _renewInvokerSockets[ordinal];
                RenewOne(in _denseCaptureTemp[ordinal], invokerOrdinal);
                var capture =
                    DirectionalShadeTransformCapture.Capture(
                        in _denseCaptureTemp[ordinal]);
                _alteredInvokerPostures[_alteredInvokerPostureTally++] =
                    new DirectionalShadeChangedPose(invokerOrdinal, in capture);
            }
            _previousDensityBulkRenew = false;
            XformRev = edits.LatestRevision;
            return _alteredInvokerPostureTally;
        }
        _previousBatchedProjDuplicateCalls = 0;

        try
        {
            ReadOnlySpan<DirectionalShadeTransformCapture> records =
                _xformEditTemp.AsSpan(0, edits.Count);
            for (int ordinal = records.Length - 1; ordinal >= 0; --ordinal)
            {
                if (!_renewInvokerSocketByIdent.TryGetValue(
                        records[ordinal].Id,
                        out int invokerOrdinal)
                    || _alteredInvokerFlagSet[invokerOrdinal])

                    continue;
                if (tolerateStaleWiring
                    && (records[ordinal].Id != _invokerIdents[invokerOrdinal]
                        || records[ordinal].ProjClass
                            != _invokerClasses[invokerOrdinal]))

                    continue;
                _alteredInvokerFlagSet[invokerOrdinal] = true;
                VetStablePosture(in records[ordinal], invokerOrdinal);
                _alteredInvokerPostures[_alteredInvokerPostureTally++] =
                    new DirectionalShadeChangedPose(
                        invokerOrdinal,
                        in records[ordinal]);
            }
        }
        finally
        {
            for (int ordinal = 0; ordinal < _alteredInvokerPostureTally; ++ordinal)
            {
                _alteredInvokerFlagSet[
                    _alteredInvokerPostures[ordinal].InvokerOrdinal] = false;
            }
        }
        _previousDensityBulkRenew = _renewInvokerSocketTally >= 64
            && _alteredInvokerPostureTally
                >= checked((_renewInvokerSocketTally * 3) / 4);
        XformRev = edits.LatestRevision;
        return _alteredInvokerPostureTally;
    }

    private void RenewOne(
        in RenderMirrorRecord latest,
        int invokerOrdinal)
    {
        var kept = _casters[invokerOrdinal];
        if (latest.Id != kept.Projection.Id
            || latest.ProjectionClass != kept.Projection.ProjectionClass)
        {
            throw new InvalidOperationException(
                $"Stable directional-shadow topology changed caster "
                + $"{kept.Projection.Id} identity or class");
        }
        _casters[invokerOrdinal] = kept with { Projection = latest };
    }
}
