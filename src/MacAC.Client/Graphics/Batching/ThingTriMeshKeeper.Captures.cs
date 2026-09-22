using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics.Tenancy;
using Microsoft.Extensions.Logging;

namespace MacAC.Client.Graphics.Batching;

public partial class ThingTriMeshKeeper
{

    public void AbortLinedUploads(IEnumerable<ulong> idents)
    {
        foreach (ulong ident in idents)
            AbortQueuedPrep(ident);
    }

    public bool IntersectTriMesh(ThingRasterizeBlob rasterizeBlob, Matrix4x4 xform, Vector3 rayOrigin, Vector3 rayDir, out float gap, out Vector3 norm)
    {
        return IntersectTriMeshInternal(rasterizeBlob, xform, rayOrigin, rayDir, 0, out gap, out norm);
    }

    internal TenancyDomainCapture GrabGlobalArenaResidency()
    {
        var arena = GlobalBuf;
        return new TenancyDomainCapture(
            TenancyDomain.GlobalMeshArena,
            EntryCount: arena is null ? 0 : 2,
            OwnerCount: 0,
            Charges: TenancyCharges.Zero,
            BudgetBytes: GlobalTriMeshBuffer.CeilingPhysicalArenaOctets,
            CapacityBytes: arena?.CapOctets ?? 0,
            UsedBytes: arena?.ConsumedOctets ?? 0,
            LargestFreeBytes: arena?.LargestSpareOctets ?? 0);
    }

    internal static long DeriveTilesetOctets(
        IEnumerable<IReadOnlyCollection<TextureAtlasKeeper>> tilesetClans)
    {
        ArgumentNullException.ThrowIfNull(tilesetClans);
        long octets = 0;
        foreach (IReadOnlyCollection<TextureAtlasKeeper> clan in tilesetClans)
        {
            foreach (TextureAtlasKeeper tileset in clan)
                octets = checked(octets + tileset.AllocatedBytes);
        }
        return octets;
    }

    internal static long DeriveTilesetOctets(
        IEnumerable<TextureAtlasKeeper> tilesets)
    {
        ArgumentNullException.ThrowIfNull(tilesets);
        long octets = 0;
        foreach (TextureAtlasKeeper tileset in tilesets)
            octets = checked(octets + tileset.AllocatedBytes);
        return octets;
    }

    internal static long DeriveNonArenaGeoOctets(
        bool usesGlobalArena,
        long geoOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(geoOctets);
        return usesGlobalArena ? 0 : geoOctets;
    }

    internal static long DeriveFollowedGpuOctets(
        long nonArenaOctets,
        long physicalArenaOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nonArenaOctets);
        ArgumentOutOfRangeException.ThrowIfNegative(physicalArenaOctets);
        return checked(nonArenaOctets + physicalArenaOctets);
    }

    internal static TriMeshPushPrice DeriveNewTilesetLeadPushPrice(
        int width,
        int height,
        TexelLayout fmt,
        int pushOctets,
        long srcOctets = 0) =>
        DeriveNewTilesetPushPrice(width, height, fmt, [pushOctets], srcOctets);

    internal static TriMeshPushPrice DeriveNewTilesetPushPrice(
        int width,
        int height,
        TexelLayout fmt,
        IReadOnlyList<int> pushOctets,
        long srcOctets = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(pushOctets);
        ArgumentOutOfRangeException.ThrowIfNegative(srcOctets);
        long arrOctets = TextureAtlasKeeper.DeriveArrOctets(width, height, fmt);
        for (int idx = 0; idx < pushOctets.Count; ++idx)
            ArgumentOutOfRangeException.ThrowIfNegative(pushOctets[idx]);
        return new TriMeshPushPrice(srcOctets, arrOctets, arrOctets, 1);
    }

    internal static long GuessPushOctets(HarvestedMesh triMeshBlob)
    {
        ArgumentNullException.ThrowIfNull(triMeshBlob);
        return triMeshBlob.FetchEstimatedPushOctets();
    }

    internal void AppendStaleTilesetsTo(ISet<TextureAtlasKeeper> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.UnionWith(_staleTilesets);
    }

    internal static HashSet<ushort>? GatherDrawingBspPolygIdents(PartMesh gfxObjRef)
    {
        if (gfxObjRef.DrawTree?.Root is null) return null;
        HashSet<ushort> idents = new HashSet<ushort>();
        GatherDrawingBspPolygIdents(gfxObjRef.DrawTree.Root, idents);
        return idents;
    }

    internal static RetryableAssetFreeRegister BuildRigPieceUndo(
        IReadOnlyList<ulong> acquiredPieces,
        Action<ulong> freePiece)
    {
        ArgumentNullException.ThrowIfNull(acquiredPieces);
        ArgumentNullException.ThrowIfNull(freePiece);
        var releases = new List<(string Name, Action Release)>(acquiredPieces.Count);
        for (int idx = acquiredPieces.Count - 1; idx >= 0; --idx)
        {
            int freeOrdinal = idx;
            releases.Add((
                $"setup-part-{freeOrdinal}",
                () => freePiece(acquiredPieces[freeOrdinal])));
        }
        return new RetryableAssetFreeRegister(releases);
    }

    private void AbortQueuedPrep(ulong ident)
    {
        (PreparationAsk? Pending, PreparationAsk? Active) canceled;
        lock (_queuedReqs)
            canceled = UnfastenQueuedPrepBolted(ident);
        AbortDetachedPrep(canceled);
    }

    private static void AbortDetachedPrep(
        (PreparationAsk? Pending, PreparationAsk? Active) canceled)
    {
        List<Exception>? misses = null;
        void Attempt(Action act)
        {
            try { act(); }
            catch (Exception exc) { (misses ??= []).Add(exc); }
        }
        if (canceled.Pending is { } queued)
        {
            Attempt(queued.Cancel);
            queued.Completion.TrySetCanceled(queued.Abort.Token);
            Attempt(queued.TeardownAbort);
        }
        if (canceled.Active is { } engaged)
            Attempt(engaged.Cancel);
        if (misses is not null)
            throw new AggregateException("Mesh preparation cancellation failed", misses);
    }

    private void RefreshLruFollowingPush(ulong ident)
    {
        lock (_lruRoster)
        {
            _lruRoster.Remove(ident);
            if (!_ownership.FlagPushDone(ident))
                _lruRoster.AddLast(ident);
        }
    }

    private static void GatherDrawingBspPolygIdents(DrawingBspNode joint, HashSet<ushort> idents)
    {
        if (joint.Polygons is not null)
            foreach (var pid in joint.Polygons)
                idents.Add((ushort)pid);
        if (joint.Front is not null) GatherDrawingBspPolygIdents(joint.Front, idents);
        if (joint.Back is not null) GatherDrawingBspPolygIdents(joint.Back, idents);
    }

    private RetryableAssetFreeRegister BuildPushUndo(
        GlobalTriMeshAlloc? globalAlloc,
        IReadOnlyList<(TextureAtlasKeeper Atlas, BitmapTag Key)> acquiredTextures)
    {
        var releases = new List<(string Name, Action Release)>();

        if (globalAlloc is not null)
        {
            releases.Add((
                "global-index-range",
                () => GlobalBuf!.CancelOrdinalSpan(globalAlloc)));
            releases.Add((
                "global-vertex-range",
                () => GlobalBuf!.CancelVertSpan(globalAlloc)));
        }

        for (int idx = acquiredTextures.Count - 1; idx >= 0; --idx)
        {
            int freeOrdinal = idx;
            releases.Add((
                $"atlas-texture-{freeOrdinal}",
                () => FreeTilesetTexture(
                    acquiredTextures[freeOrdinal].Atlas,
                    acquiredTextures[freeOrdinal].Key)));
        }

        return new RetryableAssetFreeRegister(releases);
    }

    private bool IntersectTriMeshInternal(ThingRasterizeBlob rasterizeBlob, Matrix4x4 xform, Vector3 rayOrigin, Vector3 rayDir, int zDepth, out float gap, out Vector3 norm)
    {
        gap = float.MaxValue;
        norm = Vector3.UnitZ;
        bool strike = false;

        if (zDepth > 32) return false;

        if (rasterizeBlob.IsSetup)
        {
            foreach (var piece in rasterizeBlob.SetupParts)
            {
                ThingRasterizeBlob? pieceBlob = TryFetchRasterizeBlob(piece.GfxObjId);
                if (pieceBlob is not null)
                {
                    if (IntersectTriMeshInternal(pieceBlob, piece.Transform * xform, rayOrigin, rayDir, zDepth + 1, out float d, out Vector3 num))
                    {
                        if (d < gap)
                        {
                            gap = d;
                            norm = num;
                            strike = true;
                        }
                    }
                }
            }
            return strike;
        }

        if (rasterizeBlob.CPULoci.Length is 0 || rasterizeBlob.CPUOrdinals.Length is 0)
        {
            if (rasterizeBlob.SelectionSphere is not null && rasterizeBlob.SelectionSphere.Radius > 0.001f)
            {
                Vector3 realmOrigin = Vector3.Transform(rasterizeBlob.SelectionSphere.Center, xform);
                float radius = rasterizeBlob.SelectionSphere.Radius * xform.Translation.Length(); // Rough scale
                if (GeoAids.RayIntersectsOrb(rayOrigin, rayDir, realmOrigin, radius, out gap))
                {
                    norm = Vector3.Normalize(rayOrigin + rayDir * gap - realmOrigin);
                    return true;
                }
            }
            return false;
        }

        if (!Matrix4x4.Invert(xform, out var invXform)) return false;
        Vector3 ownOrigin = Vector3.Transform(rayOrigin, invXform);
        Vector3 ownDir = Vector3.Normalize(Vector3.TransformNormal(rayDir, invXform));

        for (int idx = 0; idx < rasterizeBlob.CPUOrdinals.Length; idx += 3)
        {
            Vector3 v0 = rasterizeBlob.CPULoci[rasterizeBlob.CPUOrdinals[idx]];
            Vector3 v1 = rasterizeBlob.CPULoci[rasterizeBlob.CPUOrdinals[idx + 1]];
            Vector3 v2 = rasterizeBlob.CPULoci[rasterizeBlob.CPUOrdinals[idx + 2]];

            if (GeoAids.RayIntersectsTriangle(ownOrigin, ownDir, v0, v1, v2, out float t))
            {
                Vector3 strikePtOwn = ownOrigin + ownDir * t;
                Vector3 strikePtRealm = Vector3.Transform(strikePtOwn, xform);
                float realmDistance = Vector3.Distance(rayOrigin, strikePtRealm);

                if (realmDistance < gap)
                {
                    gap = realmDistance;

                    Vector3 ownNorm = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
                    norm = Vector3.Normalize(Vector3.TransformNormal(ownNorm, xform));

                    if (Vector3.Dot(norm, rayDir) > 0)

                        norm = -norm;

                    strike = true;
                }
            }
        }

        return strike;
    }

    private bool TryProceedObjectFree(ulong tag, out long reclaimedOctets)
    {
        reclaimedOctets = 0;
        var ticket = FetchOrBuildObjectFree(tag);
        if (ticket is null)
        {
            if (!_ownership.IsPossessed(tag))
                _ownership.Drop(tag);
            return false;
        }

        var attempt = ticket.Resources.Advance();
        if (!ticket.Resources.IsComplete)
        {
            if (!ticket.IsQueued)
            {
                ticket.IsQueued = true;
                _objectFreeFifo.Enqueue(tag);
            }
            TraceFreeMiss(
                tag,
                "resource release",
                ticket.Resources.LeftoverTally,
                attempt);
            return false;
        }

        if (_renderData.TryGetValue(tag, out ThingRasterizeBlob? latest)
            && ReferenceEquals(latest, ticket.Data))

            _renderData.TryRemove(tag, out _);
        _objectReleases.Remove(tag);
        MarkRenderDataAvailabilityChanged();
        if (!_ownership.IsPossessed(tag))
            _ownership.Drop(tag);
        lock (_lruRoster)
            _lruRoster.Remove(tag);
        reclaimedOctets = ticket.ReclaimableOctets;
        TraceFreeMiss(tag, "resource release", 0, attempt);
        return true;
    }

    private bool TryProceedPushUndo(ulong tag)
    {
        if (!_pushRollbacks.TryGetValue(tag, out RetryableAssetFreeRegister? undo))
            return true;

        var attempt = undo.Advance();
        if (!undo.IsComplete)
        {
            TraceFreeMiss(
                tag,
                "upload rollback",
                undo.LeftoverTally,
                attempt);
            return false;
        }

        _pushRollbacks.Remove(tag);
        TraceFreeMiss(tag, "upload rollback", 0, attempt);
        return true;
    }

    private void TraceFreeMiss(
        ulong tag,
        string op,
        int leftover,
        AssetFreeAttempt attempt)
    {
        if (!attempt.HasMisses)
            return;
        _logger.LogError(
            attempt.ToException(
                $"Mesh 0x{tag:X10} {op} reported exceptional resource outcomes"),
            "Mesh 0x{Id:X10} {Operation} completed {Completed} of {Attempted} attempted stages; {Remaining} remain",
            tag,
            op,
            attempt.CompletedCount,
            attempt.AttemptedCount,
            leftover);
    }

    private void ProgressQueuedPushRollbacks(int ceilingTally)
    {
        int attempts = Math.Min(ceilingTally, _pushUndoFifo.Count);
        for (int idx = 0; idx < attempts; ++idx)
        {
            ulong tag = _pushUndoFifo.Dequeue();
            if (!_pushRollbacks.ContainsKey(tag))
                continue;
            try
            {
                TryProceedPushUndo(tag);
            }
            finally
            {
                if (_pushRollbacks.ContainsKey(tag))
                    _pushUndoFifo.Enqueue(tag);
            }
        }
    }
}
