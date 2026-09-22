using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics.Stage;

internal sealed partial class RenderStageShadeComparisonDriver
{
    internal void ContrastInstant() => Contrast(force: true);

    private void Contrast(bool force)
    {
        var latest = _oracle.Snapshot;
        if (latest.CompletedFrameSequence is 0
            || (!force
                && latest.CompletedFrameSequence
                    == _comparedOracleCycleSeries))
        {
            Snapshot = AssembleCapture();
            return;
        }

        _comparisonTally = checked(_comparisonTally + 1);
        _comparedOracleCycleSeries = latest.CompletedFrameSequence;
        _matchedProjTally = 0;
        string? mismatch = ContrastLatestProj();
        if (mismatch is null)
        {
            _successfulComparisonTally =
                checked(_successfulComparisonTally + 1);
        }
        else
        {
            _mismatchTally = checked(_mismatchTally + 1);
            if (_leadMismatch is null)
            {
                _leadMismatch = mismatch;
                _trace?.Invoke(
                    "[render-scene-shadow] first mismatch: " + mismatch);
            }
        }

        if (_acknowledgeStale && mismatch is null)
            _shade.PurgeStale();
        Snapshot = AssembleCapture();
    }

    private string? ContrastLatestProj()
    {
        if (_shade.QueuedDiffTally is not 0)
        {
            return $"sourceChannel=journal pendingDeltas={_shade.QueuedDiffTally}";
        }
        if (_shade.CumulativeEnact.Rejected is not 0)
        {
            return "sourceChannel=journal rejectedDeltas="
                + _shade.CumulativeEnact.Rejected;
        }

        var ask = _shade.Ask;
        _anticipatedIdents.Clear();
        var anticipated =
            _oracle.Projections;
        for (int idx = 0; idx < anticipated.Count; ++idx)
        {
            var fingerprint = anticipated[idx];
            var ident = AnticipatedIdent(in fingerprint);
            _anticipatedIdents.Add(ident);
            if (!ask.TryGet(ident, out RenderMirrorRecord actual))
            {
                return $"sourceChannel={Channel(ident)} id={ident} field=presence "
                    + "expected=present actual=missing "
                    + $"expectedClass={fingerprint.ProjectionClass} "
                    + $"serverGuid=0x{fingerprint.ServerGuid:X8} "
                    + $"localEntityId=0x{fingerprint.EntityId:X8} "
                    + $"parentCell=0x{fingerprint.ParentCellId:X8}";
            }

            string? mismatch = ContrastProj(
                in fingerprint,
                in actual);
            if (mismatch is not null)
                return mismatch;
            ++_matchedProjTally;
        }

        int sum = ask.Counts.Total;
        if (_tableauRecords.Capacity < sum)
            _tableauRecords.Capacity = sum;
        CollectionsMarshal.SetCount(_tableauRecords, sum);
        int copied = ask.CopyTo(CollectionsMarshal.AsSpan(_tableauRecords));
        if (copied != sum)
        {
            return $"sourceChannel=scene field=count expected={sum} "
                + $"actual={copied}";
        }

        for (int idx = 0; idx < copied; ++idx)
        {
            var capture = _tableauRecords[idx];
            if (_anticipatedIdents.Contains(capture.Id)
                || Domain(capture.Id) == EnvironChamberProjDomain)

                continue;

            bool insideStatic =
                capture.ProjectionClass
                    is RenderMirrorClass.IndoorCellStatic
                || (capture.ProjectionClass
                        is RenderMirrorClass.ActiveAnimatedStatic
                    && InteriorActorPartition.IsIndoorCellId(
                        capture.Source.ParentCellId));
            if (insideStatic
                || (capture.Flags & RenderMirrorFlags.Draw) == 0)

                continue;

            return $"sourceChannel={Channel(capture.Id)} id={capture.Id} "
                + "field=presence expected=absent actual=drawn";
        }

        return null;
    }

    private static string? ContrastProj(
        in CurrentRenderMirrorFingerprint anticipated,
        in RenderMirrorRecord actual)
    {
        var ident = actual.Id;
        string lane = Channel(ident);
        string? classMismatch = ContrastClass(
            anticipated.ProjectionClass,
            in actual);
        if (classMismatch is not null)
            return Stem(lane, ident, "projectionClass", classMismatch);
        if (actual.Residency.OwnerLandblockId != anticipated.LandblockId)
        {
            return Stem(
                lane,
                ident,
                "landblock",
                $"{anticipated.LandblockId:X8}/{actual.Residency.OwnerLandblockId:X8}");
        }
        if (actual.Source.LocalEntityId != anticipated.EntityId)
            return Values(lane, ident, "entityId", anticipated.EntityId, actual.Source.LocalEntityId);
        if (actual.Source.ServerGuid != anticipated.ServerGuid)
            return Values(lane, ident, "serverGuid", anticipated.ServerGuid, actual.Source.ServerGuid);
        if (actual.Source.SourceId != anticipated.SourceId)
            return Values(lane, ident, "sourceId", anticipated.SourceId, actual.Source.SourceId);
        if (actual.Source.ParentCellId != anticipated.ParentCellId)
            return Values(lane, ident, "parentCell", anticipated.ParentCellId, actual.Source.ParentCellId);
        if (actual.Source.EffectCellId != anticipated.EffectCellId)
            return Values(lane, ident, "effectCell", anticipated.EffectCellId, actual.Source.EffectCellId);
        if (actual.Source.BuildingShellAnchorCellId
            != anticipated.BuildingShellAnchorCellId)
        {
            return Values(
                lane,
                ident,
                "buildingShellAnchor",
                anticipated.BuildingShellAnchorCellId,
                actual.Source.BuildingShellAnchorCellId);
        }
        if (actual.Source.CurrentProjectionFlags != anticipated.Flags)
            return Values(lane, ident, "flags", anticipated.Flags, actual.Source.CurrentProjectionFlags);
        if (actual.MeshSet.MeshCount != anticipated.MeshCount)
            return Values(lane, ident, "meshCount", anticipated.MeshCount, actual.MeshSet.MeshCount);
        if (actual.Source.TransformFingerprint != anticipated.Transform)
            return Stem(lane, ident, "transform", $"{anticipated.Transform}/{actual.Source.TransformFingerprint}");
        if (actual.Source.GeometryFingerprint != anticipated.Geometry)
            return Stem(lane, ident, "geometry", $"{anticipated.Geometry}/{actual.Source.GeometryFingerprint}");
        return actual.Source.AppearanceFingerprint != anticipated.Appearance
            ? Stem(lane, ident, "appearance", $"{anticipated.Appearance}/{actual.Source.AppearanceFingerprint}")
            : null;
    }

    private static string? ContrastClass(
        InteriorActorPartition.ProjClass anticipated,
        in RenderMirrorRecord actual)
    {
        bool fits = anticipated switch
        {
            InteriorActorPartition.ProjClass.OutdoorStatic =>
                actual.ProjectionClass
                    is RenderMirrorClass.OutdoorStatic
                    or RenderMirrorClass.ActiveAnimatedStatic
                && !InteriorActorPartition.IsIndoorCellId(
                    actual.Source.ParentCellId),
            InteriorActorPartition.ProjClass.CellStatic =>
                actual.ProjectionClass
                    is RenderMirrorClass.IndoorCellStatic
                    or RenderMirrorClass.ActiveAnimatedStatic
                && InteriorActorPartition.IsIndoorCellId(
                    actual.Source.ParentCellId),
            InteriorActorPartition.ProjClass.Dynamic =>
                actual.ProjectionClass
                    is RenderMirrorClass.LiveDynamicRoot
                    or RenderMirrorClass.EquippedChild,
            _ => false,
        };
        return fits
            ? null
            : $"{anticipated}/{actual.ProjectionClass}";
    }
}
