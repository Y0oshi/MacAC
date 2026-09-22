using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

internal sealed partial class DirectionalShadePreparedDraws
{
    public void RefreshDynamicTransforms(
        DirectionalShadeCasterFrame casters)
    {
        ArgumentNullException.ThrowIfNull(casters);
        if (casters.Stats.TransformJournalFullRefresh)
        {
            RenewAllDynamicXforms(casters.Casters);
            PreviousDynamicXformRenewWasDense = false;
            return;
        }
        RefreshDynamicTransforms(
            casters.AlteredInvokerPostures,
            casters.Stats.DensityBulkRefresh);
    }

    internal void RefreshDynamicTransforms(
        ReadOnlySpan<DirectionalShadeCaster> latestCasters)
    {
        RenewAllDynamicXforms(latestCasters);
        PreviousDynamicXformRenewWasDense = false;
    }

    internal void RenewDenseDynamicXforms(
        ReadOnlySpan<DirectionalShadeCaster> latestCasters)
    {
        RenewAllDynamicXforms(latestCasters);
        PreviousDynamicXformRenewWasDense = true;
    }

    internal void RefreshDynamicTransforms(
        ReadOnlySpan<DirectionalShadeCaster> latestCasters,
        ReadOnlySpan<int> alteredInvokerSockets)
    {
        _dynamicXformSocketTally = 0;
        for (int alteredOrdinal = 0;
             alteredOrdinal < alteredInvokerSockets.Length;
             ++alteredOrdinal)
        {
            int invokerOrdinal = alteredInvokerSockets[alteredOrdinal];
            if ((uint)invokerOrdinal >= (uint)latestCasters.Length)
            {
                throw new InvalidOperationException(
                    "Directional-shadow changed-caster slot is stale");
            }
            if ((uint)invokerOrdinal >= (uint)_mappedInvokerTally)
                continue;

            for (int xformOrdinal = _leadDynamicXformByInvoker[invokerOrdinal];
                 xformOrdinal >= 0;
                 xformOrdinal = _upcomingDynamicXform[xformOrdinal])
            {
                RenewXform(latestCasters, xformOrdinal);
                _dynamicXformSockets[_dynamicXformSocketTally++] = xformOrdinal;
            }
        }
        _dynamicXformSockets.AsSpan(0, _dynamicXformSocketTally).Sort();
        PreviousDynamicXformRenewTally = _dynamicXformSocketTally;
        PreviousDynamicXformRenewWasDense = false;
    }

    internal void RefreshDynamicTransforms(
        ReadOnlySpan<DirectionalShadeChangedPose> alteredPostures,
        bool denseRenew = false)
    {
        if (denseRenew)
        {
            RenewDenseDynamicXforms(alteredPostures);
            return;
        }

        _dynamicXformSocketTally = 0;
        for (int alteredOrdinal = 0;
             alteredOrdinal < alteredPostures.Length;
             ++alteredOrdinal)
        {
            ref readonly DirectionalShadeChangedPose altered =
                ref alteredPostures[alteredOrdinal];
            int invokerOrdinal = altered.InvokerOrdinal;
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

            for (int xformOrdinal = _leadDynamicXformByInvoker[invokerOrdinal];
                 xformOrdinal >= 0;
                 xformOrdinal = _upcomingDynamicXform[xformOrdinal])
            {
                RenewXform(
                    in posture,
                    invokerOrdinal,
                    xformOrdinal);
                _dynamicXformSockets[_dynamicXformSocketTally++] =
                    xformOrdinal;
            }
        }
        _dynamicXformSockets.AsSpan(0, _dynamicXformSocketTally).Sort();
        PreviousDynamicXformRenewTally = _dynamicXformSocketTally;
        PreviousDynamicXformRenewWasDense = denseRenew;
    }

    private void RenewAllDynamicXforms(
        ReadOnlySpan<DirectionalShadeCaster> latestCasters)
    {
        _dynamicXformSocketTally = _allDynamicXformSocketTally;
        _allDynamicXformSockets.AsSpan(0, _allDynamicXformSocketTally)
            .CopyTo(_dynamicXformSockets);
        int refreshed = 0;
        for (int dynamicOrdinal = 0;
             dynamicOrdinal < _dynamicXformSocketTally;
             ++dynamicOrdinal)
        {
            int xformOrdinal = _dynamicXformSockets[dynamicOrdinal];
            var src =
                _xformSrcs[xformOrdinal];
            if ((uint)src.CasterIndex >= (uint)latestCasters.Length)
            {
                throw new InvalidOperationException(
                    "Directional-shadow transform source has a stale caster index");
            }

            var proj =
                latestCasters[src.CasterIndex].Projection;
            var triMeshes = proj.EntityPayload.MeshRefs;
            if ((uint)src.MeshIndex >= (uint)triMeshes.Count)
            {
                throw new InvalidOperationException(
                    "Directional-shadow transform source has a stale mesh index");
            }

            TriMeshRef triMesh = triMeshes[src.MeshIndex];
            _xforms[xformOrdinal] = src.IsSetupPart
                ? RealmPaintRouter.ConstructPieceRealmMatrix(
                    proj.Transform.LocalToWorld,
                    triMesh.PartTransform,
                    src.SetupPartTransform)
                : triMesh.PartTransform * proj.Transform.LocalToWorld;
            ++refreshed;
        }

        PreviousDynamicXformRenewTally = refreshed;
    }

    private void RenewDenseDynamicXforms(
        ReadOnlySpan<DirectionalShadeChangedPose> alteredPostures)
    {
        _dynamicXformSocketTally = 0;
        int marked = 0;
        try
        {
            for (int alteredOrdinal = 0;
                 alteredOrdinal < alteredPostures.Length;
                 ++alteredOrdinal)
            {
                ref readonly DirectionalShadeChangedPose altered =
                    ref alteredPostures[alteredOrdinal];
                int invokerOrdinal = altered.InvokerOrdinal;
                VetAlteredPosture(in altered, invokerOrdinal);
                if (_denseAlteredPostureByInvoker[invokerOrdinal] is not 0)
                {
                    throw new InvalidOperationException(
                        "A dense directional-shadow refresh contains a duplicate caster slot");
                }

                _denseAlteredPostureByInvoker[invokerOrdinal] = alteredOrdinal + 1;
                ++marked;
            }

            for (int dynamicOrdinal = 0;
                 dynamicOrdinal < _allDynamicXformSocketTally;
                 ++dynamicOrdinal)
            {
                int xformOrdinal = _allDynamicXformSockets[dynamicOrdinal];
                var src =
                    _xformSrcs[xformOrdinal];
                int postureOrdinal = _denseAlteredPostureByInvoker[src.CasterIndex] - 1;
                if (postureOrdinal < 0)
                    continue;

                ref readonly DirectionalShadeChangedPose altered =
                    ref alteredPostures[postureOrdinal];
                RenewXform(
                    in altered.Capture,
                    altered.InvokerOrdinal,
                    xformOrdinal);
                _dynamicXformSockets[_dynamicXformSocketTally++] =
                    xformOrdinal;
            }
        }
        finally
        {
            if (marked is not 0)
            {
                for (int alteredOrdinal = 0;
                     alteredOrdinal < alteredPostures.Length;
                     ++alteredOrdinal)
                {
                    int invokerOrdinal = alteredPostures[alteredOrdinal].InvokerOrdinal;
                    if ((uint)invokerOrdinal < (uint)_denseAlteredPostureByInvoker.Length)
                        _denseAlteredPostureByInvoker[invokerOrdinal] = 0;
                }
            }
        }

        PreviousDynamicXformRenewTally = _dynamicXformSocketTally;
        PreviousDynamicXformRenewWasDense = true;
    }

    private void RenewXform(
        in DirectionalShadeTransformCapture posture,
        int invokerOrdinal,
        int xformOrdinal)
    {
        var src =
            _xformSrcs[xformOrdinal];
        if (src.CasterIndex != invokerOrdinal)
        {
            throw new InvalidOperationException(
                "Directional-shadow transform source maps to a different caster");
        }

        var latestTriMeshes = posture.ActorCargo.MeshRefs;
        if ((uint)src.MeshIndex >= (uint)latestTriMeshes.Count)
        {
            throw new InvalidOperationException(
                "Directional-shadow changed pose has a stale mesh index");
        }

        TriMeshRef latestTriMesh = latestTriMeshes[src.MeshIndex];
        _xforms[xformOrdinal] = src.IsSetupPart
            ? RealmPaintRouter.ConstructPieceRealmMatrix(
                posture.Transform.LocalToWorld,
                latestTriMesh.PartTransform,
                src.SetupPartTransform)
            : latestTriMesh.PartTransform * posture.Transform.LocalToWorld;
    }

    private void RenewXform(
        ReadOnlySpan<DirectionalShadeCaster> latestCasters,
        int xformOrdinal)
    {
        var src =
            _xformSrcs[xformOrdinal];
        if ((uint)src.CasterIndex >= (uint)latestCasters.Length)
        {
            throw new InvalidOperationException(
                "Directional-shadow transform source has a stale caster index");
        }

        var proj =
            latestCasters[src.CasterIndex].Projection;
        var triMeshes = proj.EntityPayload.MeshRefs;
        if ((uint)src.MeshIndex >= (uint)triMeshes.Count)
        {
            throw new InvalidOperationException(
                "Directional-shadow transform source has a stale mesh index");
        }

        TriMeshRef triMesh = triMeshes[src.MeshIndex];
        _xforms[xformOrdinal] = src.IsSetupPart
            ? RealmPaintRouter.ConstructPieceRealmMatrix(
                proj.Transform.LocalToWorld,
                triMesh.PartTransform,
                src.SetupPartTransform)
            : triMesh.PartTransform * proj.Transform.LocalToWorld;
    }
}
