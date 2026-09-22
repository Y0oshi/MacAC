using MacAC.Mechanics.Geometry;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class EnvironChamberPainter
{

    public void Dispose()
    {
        if (IsDestroyed || _disposing) return;
        _disposing = true;
        try
        {
            if (_teardownAssetList is null)
            {
                var releases = new List<(string Name, Action Release)>
                {
                    ("prepare-scratch", _readyTemp.Dispose),
                };
                TeardownRhiAssetList();

                _teardownAssetList = new RetryableAssetFreeRegister(releases);
            }

            var attempt = _teardownAssetList.Advance();
            if (!_teardownAssetList.IsComplete)
            {
                throw attempt.ToException(
                    "One or more EnvCell renderer resources could not be released.");
            }

            _dynamicCycleBegun = false;
            _teardownAssetList = null;
            IsDestroyed = true;

            if (attempt.HasMisses)
            {
                throw attempt.ToException(
                    "EnvCell renderer resources released with exceptional committed outcomes.");
            }
        }
        finally
        {
            _disposing = false;
        }
    }
    internal static bool LotBelongsToPass(
        ThingRasterizeLot lot,
        BatchRenderPass rasterizePass)
    {
        return rasterizePass switch
        {
            BatchRenderPass.Opaque => !lot.IsAdditive && !lot.IsTransparent,
            BatchRenderPass.Transparent => lot.IsAdditive || lot.IsTransparent,
            BatchRenderPass.SinglePass => true,
            _ => false,
        };
    }

    internal static void AffixMdiPaintSpan(
        List<MdiPaintSpan> spans,
        int clusterOrdinal,
        int leadDirective,
        int directiveTally,
        CanonSurfaceMaterialState matlPhase)
    {
        ArgumentNullException.ThrowIfNull(spans);
        if (directiveTally <= 0)
            return;

        if (spans.Count > 0)
        {
            var earlier = spans[^1];
            if (earlier.GroupIndex == clusterOrdinal
                && earlier.MaterialState == matlPhase
                && earlier.FirstCommand + earlier.CommandCount == leadDirective)
            {
                spans[^1] = earlier with
                {
                    CommandCount = checked(earlier.CommandCount + directiveTally),
                };
                return;
            }
        }

        spans.Add(new MdiPaintSpan(clusterOrdinal, leadDirective, directiveTally, matlPhase));
    }

    private static int LocateLotClusterOrdinal(
        ThingRasterizeLot batch,
        BatchRenderPass rasterizePass)
    {
        int prune = (int)batch.CullMode;
        if ((uint)prune >= PruneClusterTally)
            throw new ArgumentOutOfRangeException(nameof(batch), batch.CullMode, "Unrecognized cell-shell cull mode");

        if (rasterizePass != BatchRenderPass.Transparent)
            return prune + (batch.IsAdditive ? AdditiveClusterBase : 0);

        if (batch.IsAdditive)
            return prune + AdditiveClusterBase;

        return (batch.RetailSurfaceMask & CanonAlphaMeshRouter.BitmaskClipLookup) is 0
            ? prune
            : prune + (batch.Key.PaletteId is not 0
            ? ClipPalettedClusterBase
            : ClipDdsClusterBase);
    }

    private List<InstanceData> GetPooledList()
    {
        lock (_rosterReservoir)
        {
            if (_poolIndex < _rosterReservoir.Count)
            {
                List<InstanceData> roster = _rosterReservoir[_poolIndex++];
                roster.Clear();
                return roster;
            }
            List<InstanceData> fresh = new List<InstanceData>();
            _rosterReservoir.Add(fresh);
            ++_poolIndex;
            return fresh;
        }
    }
}
