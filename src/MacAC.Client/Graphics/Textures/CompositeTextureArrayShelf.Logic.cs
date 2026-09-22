using MacAC.Mechanics.Surfaces;

namespace MacAC.Client.Graphics;

internal sealed partial class CompositeTextureArrayShelf
{
    internal int EngagedAssetTally => _holders.AssetTally;

    internal int HolderTally => _holders.HolderTally;

    internal int StashedListingTally => _listings.Count;

    internal int UnownedListingTally => _unowned.Count;

    internal long UnownedOctets => _unowned.HousedBytes;

    internal int TilesetTally => _tilesets.Count;

    internal long AllocatedBytes { get; private set; }

    private readonly long _physicalAllowanceOctets;

    internal long PhysicalAllowanceOctets => _physicalAllowanceOctets;
    internal long UnownedAllowanceOctets => _unowned.AllowanceOctets;

    internal int CyclePushTally { get; private set; }

    internal long CyclePushOctets { get; private set; }

    private int LatestCeilingUploadsPerCycle
    {
        get
        {
            return _destUnveilPushPrecedence
            ? Math.Max(
                field,
                DestUnveilCeilingUploadsPerCycle)
            : field;
        }
    }

    public void BeginFrame(bool destUnveilPushPrecedence = false)
    {
        HurlIfUnavailable();
        _sunsetRegister.ReattemptPendingPublications();
        _destUnveilPushPrecedence =
            destUnveilPushPrecedence;
        CyclePushTally = 0;
        CyclePushOctets = 0;
        _cycleTilesetCreationTally = 0;
        CanBeginPush = false;
    }

    public void RelinquishHolder(uint holderOwnIdent)
    {
        HurlIfUnavailable();
        var unowned = _holders.FreeOwner(holderOwnIdent);
        for (int idx = 0; idx < unowned.Count; ++idx)
        {
            var tag = unowned[idx];
            if (_listings.TryGetValue(tag, out EntryUnit? listing))
                _unowned.FlagUnowned(tag, listing.Bytes);
        }
    }

    public void Tick()
    {
        HurlIfUnavailable();
        _sunsetRegister.ReattemptPendingPublications();

        bool allocPressure = HasQueuedAllocPressure();
        bool physicalOverAllowance = AllocatedBytes > _physicalAllowanceOctets;
        bool needsPhysicalRelief = allocPressure || physicalOverAllowance;

        bool servicedQueuedFree = ConcludeOneQueuedTilesetFree();

        if (needsPhysicalRelief && !servicedQueuedFree)
            EraseOneGpuSafeVacantTileset(
                _queuedTilesetAllocOctets is 0 ? null : (_queuedTilesetWidth, _queuedTilesetHeight));

        allocPressure = HasQueuedAllocPressure();
        physicalOverAllowance = AllocatedBytes > _physicalAllowanceOctets;
        needsPhysicalRelief = allocPressure || physicalOverAllowance;

        int evicted = 0;
        if (needsPhysicalRelief && _queuedTilesetAllocOctets is not 0)
        {
            evicted += EvictCompatibleUnowned(
                _queuedTilesetWidth,
                _queuedTilesetHeight,
                CeilingLogicalEvictionsPerCycle);
        }

        while (evicted < CeilingLogicalEvictionsPerCycle)
        {
            bool grab = needsPhysicalRelief
                ? _unowned.TryGrabOldest(out CompoundBitmapTag tag)
                : _unowned.TryGrabOldestOverAllowance(out tag);
            if (!grab)
                break;
            EvictListing(tag);
            ++evicted;
        }

        _queuedTilesetWidth = 0;
        _queuedTilesetHeight = 0;
        _queuedTilesetAllocOctets = 0;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _teardownAsked = true;
        _sunsetRegister.ReattemptPendingPublications();

        List<Exception>? misses = null;
        for (int idx = 0; idx < _tilesets.Count; ++idx)
        {
            Tileset tileset = _tilesets[idx];
            try { RevokeTilesetResidencyForTeardown(tileset); }
            catch (Exception exc) { (misses ??= []).Add(exc); }
        }
        if (misses is not null)
            throw new AggregateException("One or more composite-array residency releases failed", misses);

        Tileset[] tilesets = [.. _tilesets];
        for (int idx = 0; idx < tilesets.Length; ++idx)
        {
            try { EraseTileset(tilesets[idx], demandGpuSafeVacant: false); }
            catch (Exception exc) { (misses ??= []).Add(exc); }
        }
        if (misses is not null)
            throw new AggregateException("One or more composite-array deletions failed", misses);

        _listings.Clear();
        _holders.Clear();
        _unowned.Clear();
        _tilesetsByDims.Clear();
        _tilesets.Clear();
        AllocatedBytes = 0;
        _queuedTilesetAllocOctets = 0;
        _destroyed = true;
    }

    internal static int DeriveStratumCap(int width, int height, int driverCeilingStrata)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(driverCeilingStrata, 1);

        long stratumOctets = checked((long)width * height * 4L);
        long markStrata = Math.Max(1L, MarkArrOctets / stratumOctets);
        return checked((int)Math.Min(
            markStrata,
            Math.Min(driverCeilingStrata, CeilingStrataPerArr)));
    }

    internal bool CanPush(long octets)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(octets);
        return CyclePushTally >= LatestCeilingUploadsPerCycle
            ? false
            : CyclePushTally is 0
            || octets <= _ceilingPushOctetsPerCycle - CyclePushOctets;
    }

    internal bool CanReadyPush(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        long stratumOctets = checked((long)width * height * 4L);
        if (!CanPush(stratumOctets))
        {
            CanBeginPush = true;
            return false;
        }

        if (_tilesetsByDims.TryGetValue((width, height), out List<Tileset>? compatible))
        {
            for (int idx = 0; idx < compatible.Count; ++idx)
                if (compatible[idx].IsReusable && compatible[idx].OnHandStrata is not 0)
                    return true;
        }

        int cap = DeriveStratumCap(width, height, _ceilingArrStrata);
        long askedOctets = checked(stratumOctets * cap);
        if (_cycleTilesetCreationTally < CeilingTilesetCreationsPerCycle
            && CanReserveTileset(askedOctets))
            return true;

        AssignQueuedAlloc(width, height, askedOctets);
        CanBeginPush = true;
        return false;
    }

    internal Tenancy.TenancyDomainCapture GrabResidency()
    {
        long sunsettingOctets = 0;
        long consumedOctets = 0;
        long onHandOctets = 0;
        for (int idx = 0; idx < _tilesets.Count; ++idx)
        {
            Tileset tileset = _tilesets[idx];
            if (tileset.FreeAsked
                || tileset.FreeJuncture != TilesetFreeJuncture.Resident)
            {
                sunsettingOctets = checked(
                    sunsettingOctets + tileset.Resource.Octets);
                consumedOctets = checked(
                    consumedOctets + tileset.Resource.Octets);
                continue;
            }

            long stratumOctets = tileset.Resource.Octets / tileset.Slots.Capacity;
            consumedOctets = checked(
                consumedOctets
                + stratumOctets * checked(
                    tileset.ListingCount + tileset.QueuedRetirements));
            onHandOctets = checked(
                onHandOctets
                + stratumOctets * tileset.OnHandStrata);
        }

        return new Tenancy.TenancyDomainCapture(
            Tenancy.TenancyDomain.CompositeTextures,
            EntryCount: _listings.Count,
            OwnerCount: _holders.HolderTally,
            Charges: new Tenancy.TenancyCharges(
                GpuRequestedBytes: _queuedTilesetAllocOctets,
                GpuResidentBytes: checked(
                    AllocatedBytes - sunsettingOctets),
                RetiringBytes: sunsettingOctets),
            BudgetBytes: _physicalAllowanceOctets,
            CapacityBytes: AllocatedBytes,
            UsedBytes: consumedOctets,
            LargestFreeBytes: onHandOctets);
    }

    internal bool CanBeginPush
    {
        get =>
        !field
        && CyclePushTally < LatestCeilingUploadsPerCycle
        && (CyclePushTally is 0 || CyclePushOctets < _ceilingPushOctetsPerCycle); private set;
    }

    internal void TourListings(Action<uint, int, int> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach ((CompoundBitmapTag tag, EntryUnit listing) in _listings)
            visitor(tag.SurfaceId, listing.Atlas.Resource.Width, listing.Atlas.Resource.Height);
    }

    private bool FitsWithinPhysicalAllowance(long askedOctets)
    {
        return askedOctets > _physicalAllowanceOctets ? AllocatedBytes is 0 : AllocatedBytes <= _physicalAllowanceOctets - askedOctets;
    }

    private int EvictCompatibleUnowned(int width, int height, int ceiling)
    {
        _evictionTemp.Clear();
        foreach ((CompoundBitmapTag tag, EntryUnit listing) in _listings)
        {
            if (_evictionTemp.Count == ceiling)
                break;
            if (listing.Atlas.Resource.Width == width
                && listing.Atlas.Resource.Height == height
                && _unowned.Contains(tag))

                _evictionTemp.Add(tag);
        }

        int evicted = 0;
        for (int idx = 0; idx < _evictionTemp.Count; ++idx)
        {
            var tag = _evictionTemp[idx];
            if (!_unowned.TryTake(tag))
                continue;
            EvictListing(tag);
            ++evicted;
        }
        return evicted;
    }

    private void EvictListing(CompoundBitmapTag tag)
    {
        if (!_listings.Remove(tag, out EntryUnit? listing))
            return;

        Tileset tileset = listing.Atlas;
        tileset.ListingCount--;
        tileset.QueuedRetirements++;
        int stratum = listing.Layer;
        _sunsetRegister.Retire(new RetryableGpuAssetFree(
            () => tileset.Slots.Yield(stratum),
            () => tileset.QueuedRetirements--));
    }

    private void EraseOneGpuSafeVacantTileset((int Width, int Height)? preserveDims = null)
    {
        Tileset? oldest = null;
        for (int idx = 0; idx < _tilesets.Count; ++idx)
        {
            Tileset contender = _tilesets[idx];
            if (preserveDims is { } preserve
                && contender.Resource.Width == preserve.Width
                && contender.Resource.Height == preserve.Height)

                continue;
            if (contender.IsReusable
                && contender.IsGpuSafeVacant
                && (oldest is null || contender.PreviousUseSeries < oldest.PreviousUseSeries))

                oldest = contender;
        }

        if (oldest is not null)
            EraseTileset(oldest);
    }

    private void EraseTileset(Tileset tileset, bool demandGpuSafeVacant = true)
    {
        if (tileset.FreeJuncture == TilesetFreeJuncture.Accounted)
            return;
        if (demandGpuSafeVacant && !tileset.IsGpuSafeVacant)
            throw new InvalidOperationException("Can't delete a composite array while a layer is live or retiring");

        tileset.FreeAsked = true;
        if (tileset.FreeJuncture == TilesetFreeJuncture.Resident)
        {
            try
            {
                _backend.CraftNonHoused(tileset.Resource);
                tileset.FreeJuncture = TilesetFreeJuncture.NonResident;
                DropFromReusableTilesetOrdinal(tileset);
            }
            catch (GpuAssetAlterationFault problem) when (problem.AlterationCommitted)
            {
                tileset.FreeJuncture = TilesetFreeJuncture.NonResident;
                DropFromReusableTilesetOrdinal(tileset);
                throw;
            }
        }
        if (tileset.FreeJuncture == TilesetFreeJuncture.NonResident)
        {
            try
            {
                _backend.Delete(tileset.Resource);
                tileset.FreeJuncture = TilesetFreeJuncture.Deleted;
            }
            catch (GpuAssetAlterationFault problem) when (problem.AlterationCommitted)
            {
                tileset.FreeJuncture = TilesetFreeJuncture.Deleted;
                throw;
            }
        }
        if (tileset.FreeJuncture == TilesetFreeJuncture.Deleted)
        {
            AllocatedBytes = checked(AllocatedBytes - tileset.Resource.Octets);
            _tilesets.Remove(tileset);
            tileset.FreeJuncture = TilesetFreeJuncture.Accounted;
        }
    }

    private void RevokeTilesetResidencyForTeardown(Tileset tileset)
    {
        tileset.FreeAsked = true;
        if (tileset.FreeJuncture != TilesetFreeJuncture.Resident)
            return;
        try
        {
            _backend.CraftNonHoused(tileset.Resource);
            tileset.FreeJuncture = TilesetFreeJuncture.NonResident;
            DropFromReusableTilesetOrdinal(tileset);
        }
        catch (GpuAssetAlterationFault problem) when (problem.AlterationCommitted)
        {
            tileset.FreeJuncture = TilesetFreeJuncture.NonResident;
            DropFromReusableTilesetOrdinal(tileset);
            throw;
        }
    }

    private void HurlIfUnavailable() =>
        ObjectDisposedException.ThrowIf(_teardownAsked || _destroyed, this);

    private bool CanReserveTileset(long askedOctets) => FitsWithinPhysicalAllowance(askedOctets) ? true : !HasReclaimableDepot();

    private bool HasQueuedAllocPressure()
    {
        return _queuedTilesetAllocOctets is not 0
        && !FitsWithinPhysicalAllowance(_queuedTilesetAllocOctets);
    }

    private bool HasReclaimableDepot()
    {
        if (_unowned.Count is not 0)
            return true;
        for (int idx = 0; idx < _tilesets.Count; ++idx)
        {
            Tileset contender = _tilesets[idx];
            if (!contender.Deleted
                && (contender.FreeAsked
                    || contender.QueuedRetirements is not 0
                    || contender.IsGpuSafeVacant))

                return true;
        }
        return false;
    }

    private void AssignQueuedAlloc(int width, int height, long octets)
    {
        _queuedTilesetWidth = width;
        _queuedTilesetHeight = height;
        _queuedTilesetAllocOctets = octets;
    }

    private void WipeQueuedAlloc(int width, int height)
    {
        if (_queuedTilesetWidth != width || _queuedTilesetHeight != height)
            return;
        _queuedTilesetWidth = 0;
        _queuedTilesetHeight = 0;
        _queuedTilesetAllocOctets = 0;
    }

    private bool ConcludeOneQueuedTilesetFree()
    {
        for (int idx = 0; idx < _tilesets.Count; ++idx)
        {
            Tileset tileset = _tilesets[idx];
            if (!tileset.FreeAsked)
                continue;
            EraseTileset(tileset);
            return true;
        }
        return false;
    }

    private void DropFromReusableTilesetOrdinal(Tileset tileset)
    {
        var dims = (tileset.Resource.Width, tileset.Resource.Height);
        if (!_tilesetsByDims.TryGetValue(dims, out List<Tileset>? compatible))
            return;
        compatible.Remove(tileset);
        if (compatible.Count is 0)
            _tilesetsByDims.Remove(dims);
    }

    private static void VetDecodedTexture(UnpackedTexture decoded)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(decoded.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(decoded.Height, 1);
        long anticipated = checked((long)decoded.Width * decoded.Height * 4L);
        if (decoded.Rgba8.LongLength != anticipated)
            throw new ArgumentException(
                $"Decoded RGBA texture has {decoded.Rgba8.LongLength} bytes; wanted {anticipated}.",
                nameof(decoded));
    }
}
