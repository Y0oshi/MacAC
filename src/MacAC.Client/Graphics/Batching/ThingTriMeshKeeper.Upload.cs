using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Assets;
using Microsoft.Extensions.Logging;

namespace MacAC.Client.Graphics.Batching;

public partial class ThingTriMeshKeeper
{

    public ThingRasterizeBlob? PushTriMeshBlob(HarvestedMesh triMeshBlob)
    {
        bool pushAttempted = false;
        try
        {
            if (_objectReleases.ContainsKey(triMeshBlob.ObjectId)
                && !TryProceedObjectFree(triMeshBlob.ObjectId, out _))

                return null;
            if (!TryProceedPushUndo(triMeshBlob.ObjectId))
                return null;

            if (_renderData.TryGetValue(triMeshBlob.ObjectId, out var extant))
            {
                RefreshLruFollowingPush(triMeshBlob.ObjectId);
                return extant;
            }

            pushAttempted = true;
            if (triMeshBlob.IsSetup)
            {
                if (triMeshBlob.EnvCellGeometry is not null && PushTriMeshBlob(triMeshBlob.EnvCellGeometry) is null)
                {
                    throw new InvalidOperationException(
                        $"Nested EnvCell geometry 0x{triMeshBlob.EnvCellGeometry.ObjectId:X10} "
                        + $"failed while uploading setup 0x{triMeshBlob.ObjectId:X10}.");
                }

                ThingRasterizeBlob blob = new ThingRasterizeBlob
                {
                    IsSetup = true,
                    SetupParts = triMeshBlob.SetupParts,
                    ParticleEmitters = triMeshBlob.ParticleEmitters,
                    Batches = [],
                    BoundingBox = triMeshBlob.BoundingBox,
                    SortCenter = triMeshBlob.SortCenter,
                    DIDDegrade = triMeshBlob.DIDDegrade,
                    SelectionSphere = triMeshBlob.SelectionSphere,
                    MemoryDims = 1024 // Small overhead for the setup itself
                };
                List<ulong> acquiredPieces = new List<ulong>(triMeshBlob.SetupParts.Count);
                try
                {
                    foreach (var (pieceIdent, _) in triMeshBlob.SetupParts)
                    {
                        IncrementRefCount(pieceIdent);
                        acquiredPieces.Add(pieceIdent);
                        _ = ReadyTriMeshBlobAsync(pieceIdent, isRig: false);
                    }

                    if (!_renderData.TryAdd(triMeshBlob.ObjectId, blob))
                        throw new InvalidOperationException(
                            $"Setup 0x{triMeshBlob.ObjectId:X10} was published concurrently");
                    MarkRenderDataAvailabilityChanged();
                    _latestNonArenaGpuMemory = checked(
                        _latestNonArenaGpuMemory + blob.NonArenaGpuOctets);
                }
                catch (Exception rigMiss)
                {
                    var undo =
                        BuildRigPieceUndo(acquiredPieces, DecrementRefCount);
                    var attempt = undo.Advance();
                    if (!undo.IsComplete)
                    {
                        _pushRollbacks[triMeshBlob.ObjectId] = undo;
                        _pushUndoFifo.Enqueue(triMeshBlob.ObjectId);
                    }
                    if (!attempt.HasMisses)
                        throw;
                    throw new AggregateException(
                        $"Setup 0x{triMeshBlob.ObjectId:X10} upload and dependency rollback failed",
                        rigMiss,
                        attempt.ToException(
                            $"Setup 0x{triMeshBlob.ObjectId:X10} retained unfinished part references"));
                }

                RefreshLruFollowingPush(triMeshBlob.ObjectId);

                return blob;
            }

            var rasterizeBlob = PushGfxObjRefTriMeshBlob(triMeshBlob);
            if (rasterizeBlob is null)
            {
                Console.WriteLine($"[up-null] 0x{triMeshBlob.ObjectId:X10} produced a 0-vertex mesh - caching empty render data (legitimate only for degenerate/no-valid-surface models)");
                rasterizeBlob = new ThingRasterizeBlob();
            }

            rasterizeBlob.BoundingBox = triMeshBlob.BoundingBox;
            rasterizeBlob.SortCenter = triMeshBlob.SortCenter;
            rasterizeBlob.DIDDegrade = triMeshBlob.DIDDegrade;
            rasterizeBlob.SelectionSphere = triMeshBlob.SelectionSphere;
            _renderData.TryAdd(triMeshBlob.ObjectId, rasterizeBlob);
            MarkRenderDataAvailabilityChanged();
            _latestNonArenaGpuMemory = checked(
                _latestNonArenaGpuMemory + rasterizeBlob.NonArenaGpuOctets);
            RefreshLruFollowingPush(triMeshBlob.ObjectId);

            return rasterizeBlob;
        }
        catch (Exception exc)
        {
            if (pushAttempted)
                triMeshBlob.UploadAttempts++;
            _logger.LogError(exc, "Error uploading mesh data for 0x{Id:X8}", triMeshBlob.ObjectId);
            return null;
        }
    }
    internal bool PushOrRequeue(MeshUploadQueueGear gear)
    {
        var triMeshBlob = gear.Data;
        if (!_ownership.IsPossessed(triMeshBlob.ObjectId))
        {
            _linedTriMeshBlob.ConcludeOrRestageIfPossessed(gear, _ownership);
            return false;
        }
        if (PushTriMeshBlob(triMeshBlob) is not null)
        {
            _linedTriMeshBlob.Complete(gear);
            return false;                       // success (incl. legitimate 0-vertex → empty render data)
        }
        if (HasRasterizeBlob(triMeshBlob.ObjectId))
        {
            _linedTriMeshBlob.Complete(gear);
            return false;                       // raced to present by another path
        }
        if (_objectReleases.ContainsKey(triMeshBlob.ObjectId)
            || _pushRollbacks.ContainsKey(triMeshBlob.ObjectId))

            return true;
        if (triMeshBlob.UploadAttempts < UpperPushReattempts)
            return true;                        // re-stage for next frame
        _linedTriMeshBlob.Complete(gear);
        Console.WriteLine($"[up-retry] 0x{triMeshBlob.ObjectId:X10} upload failed {triMeshBlob.UploadAttempts}x - giving up (the upload error is being surfaced, not hidden)");
        return false;
    }

    private ThingRasterizeBlob? PushGfxObjRefTriMeshBlob(HarvestedMesh triMeshBlob)
    {
        if (triMeshBlob.Vertices.Length is 0) return null;

        var pushOrdering =
            SequencedPushLots(triMeshBlob);

        int sumOrdinalTally = 0;
        int nonVacantLotTally = 0;
        foreach (var (_, fmtLot) in pushOrdering)
        {
            if (fmtLot.Indices.Count is 0) continue;
            sumOrdinalTally = checked(sumOrdinalTally + fmtLot.Indices.Count);
            ++nonVacantLotTally;
        }

        ushort[] cpuOrdinals = new ushort[sumOrdinalTally];
        var ordinalSegments = new (int Offset, int Count)[nonVacantLotTally];
        {
            int populateShift = 0;
            int segmentOrdinal = 0;
            foreach (var (_, fmtLot) in pushOrdering)
            {
                int tally = fmtLot.Indices.Count;
                if (tally is 0) continue;
                CollectionsMarshal.AsSpan(fmtLot.Indices)
                    .CopyTo(cpuOrdinals.AsSpan(populateShift, tally));
                ordinalSegments[segmentOrdinal++] = (populateShift, tally);
                populateShift = checked(populateShift + tally);
            }
        }

        GlobalTriMeshAlloc? globalAlloc = null;
        var rasterizeLots = new List<ThingRasterizeLot>(nonVacantLotTally);
        var acquiredTextures = new List<(TextureAtlasKeeper Atlas, BitmapTag Key)>();

        try
        {
            if (GlobalBuf is not null && nonVacantLotTally is not 0)
            {
                globalAlloc = GlobalBuf.PushTriMesh(
                    triMeshBlob.Vertices,
                    cpuOrdinals,
                    ordinalSegments);
            }

            foreach (var (fmt, lot) in pushOrdering)
            {
                {
                    if (lot.Indices.Count is 0) continue;

                    TextureAtlasKeeper? tilesetKeeper = null;
                    int textureOrdinal = 0;
                    uint leadOrdinal = 0;
                    int lotBaseVert = 0;

                    if (!_globalTilesets.TryGetValue(fmt, out var tilesetRoster))
                    {
                        tilesetRoster = [];
                        _globalTilesets[fmt] = tilesetRoster;
                    }

                    for (int idx = 0; idx < tilesetRoster.Count; ++idx)
                    {
                        if (tilesetRoster[idx].HasTexture(lot.Key))
                        {
                            tilesetKeeper = tilesetRoster[idx];
                            break;
                        }
                    }
                    if (tilesetKeeper is null)
                    {
                        for (int idx = 0; idx < tilesetRoster.Count; ++idx)
                        {
                            if (tilesetRoster[idx].OnHandSlots > 0)
                            {
                                tilesetKeeper = tilesetRoster[idx];
                                break;
                            }
                        }
                    }
                    if (tilesetKeeper is null)
                    {
                        tilesetKeeper = new TextureAtlasKeeper(
                            _tilesetArrs,
                            fmt.Width,
                            fmt.Height,
                            fmt.Format,
                            OnTilesetGpuSafeVacant);
                        tilesetRoster.Add(tilesetKeeper);
                    }
                    tilesetKeeper.PreviousUseSeries = ++_tilesetUseSeries;

                    bool uploadsNewStratum = !tilesetKeeper.HasTexture(lot.Key);
                    try
                    {
                        textureOrdinal = tilesetKeeper.AppendTexture(lot.Key, lot.TextureData,
                            lot.UploadPixelFormat, lot.UploadPixelType);
                    }
                    catch
                    {
                        if (tilesetKeeper.IsGpuSafeVacant)
                            OnTilesetGpuSafeVacant(tilesetKeeper);
                        throw;
                    }
                    acquiredTextures.Add((tilesetKeeper, lot.Key));
                    FlagTilesetEngaged(tilesetKeeper);
                    if (uploadsNewStratum)
                        _staleTilesets.Add(tilesetKeeper);

                    var textureSocket =
                        tilesetKeeper.TextureArr.LocateSocket(lot.HasWrappingUVs);

                    rasterizeLots.Add(new ThingRasterizeLot
                    {
                        OrdinalTally = lot.Indices.Count,
                        Tileset = tilesetKeeper!,
                        TextureIndex = textureOrdinal,
                        TextureDims = (fmt.Width, fmt.Height),
                        TextureFormat = fmt.Format,
                        RetailSurfaceMask = lot.RetailSurfaceMask,
                        Translucency = lot.Translucency,
                        MaterialState = lot.MaterialState,
                        IsTransparent = lot.IsTransparent,
                        IsAdditive = lot.IsAdditive,
                        HasWrappingUVs = lot.HasWrappingUVs,
                        SurfaceOpacity = lot.SurfaceOpacity,
                        Key = lot.Key,
                        CullMode = lot.CullMode,
                        LeadIdx = leadOrdinal,
                        BaseVertex = (uint)lotBaseVert,
                        TextureSlot = textureSocket,
                    });
                }
            }

            if (globalAlloc is not null)
            {
                if (rasterizeLots.Count != globalAlloc.BatchFirstIndices.Count)
                {
                    throw new InvalidOperationException("Global mesh batch allocation count mismatch");
                }

                for (int idx = 0; idx < rasterizeLots.Count; ++idx)
                {
                    rasterizeLots[idx].BaseVertex = (uint)globalAlloc.Vertices.Offset;
                    rasterizeLots[idx].LeadIdx = (uint)globalAlloc.BatchFirstIndices[idx];
                }
            }

            long geoOctets = checked(
                (long)triMeshBlob.Vertices.Length * VertLocusNormBitmap.Size
                + (long)sumOrdinalTally * sizeof(ushort));
            bool hasCutoutSubset = false;
            for (int idx = 0; idx < rasterizeLots.Count; ++idx)
            {
                if (rasterizeLots[idx].Translucency
                    == MacAC.Mechanics.Geometry.SeeThroughKind.ClipMap)
                {
                    hasCutoutSubset = true;
                    break;
                }
            }
            Vector3[] cpuLoci = new Vector3[triMeshBlob.Vertices.Length];
            for (int idx = 0; idx < cpuLoci.Length; ++idx)
                cpuLoci[idx] = triMeshBlob.Vertices[idx].Position;
            ThingRasterizeBlob rasterizeBlob = new ThingRasterizeBlob
            {
                VertTally = triMeshBlob.Vertices.Length,
                Batches = rasterizeLots,
                HasCutoutSubset = hasCutoutSubset,
                GlobalAlloc = globalAlloc,
                ParticleEmitters = triMeshBlob.ParticleEmitters,
                DIDDegrade = triMeshBlob.DIDDegrade,
                CPULoci = cpuLoci,
                CPUOrdinals = cpuOrdinals,
                CPURimStrokes = triMeshBlob.EdgeLines,
                MemoryDims = geoOctets,
                NonArenaGpuOctets = DeriveNonArenaGeoOctets(
                    GlobalBuf is not null,
                    geoOctets),
            };

            return rasterizeBlob;
        }
        catch (Exception pushMiss)
        {
            var undo = BuildPushUndo(
                globalAlloc,
                acquiredTextures);
            var attempt = undo.Advance();
            if (!undo.IsComplete)
            {
                _pushRollbacks[triMeshBlob.ObjectId] = undo;
                _pushUndoFifo.Enqueue(triMeshBlob.ObjectId);
            }

            if (!attempt.HasMisses)
                throw;

            throw new AggregateException(
                $"Mesh 0x{triMeshBlob.ObjectId:X10} upload and rollback failed",
                pushMiss,
                attempt.ToException(
                    $"Mesh 0x{triMeshBlob.ObjectId:X10} upload rollback had unfinished resources"));
        }
    }
}
