namespace MacAC.Client.Graphics;

internal sealed class StandaloneBindlessBitmapAsset
{
    public required uint SurfaceId { get; init; }

    // GL texture name on the GL arm; zero on the RHI arm
    public uint Name { get; init; }

    // Resident bindless handle on the GL arm; zero on the RHI arm
    public ulong Handle { get; init; }

    // The device texture on the RHI arm; null on the GL arm
    public Gpu.IGpuBitmap? Texture { get; init; }

    public required Gpu.GpuTextureSlot Slot { get; init; }
    public required long Bytes { get; init; }

    // Whether the resource names a texture at all
    public bool IdentifiesATexture => (Name is not 0 && Handle is not 0) || Texture is not null;
}

internal interface IStandaloneBindlessBitmapBackend
{
    void ForgeNonHoused(StandaloneBindlessBitmapAsset asset);
    void Delete(StandaloneBindlessBitmapAsset asset);
}

internal sealed class StandaloneBindlessTextureShelf : IDisposable
{
    internal const long DefaultUnownedAllowanceOctets = 32L * 1024 * 1024;
    internal const int DefaultCeilingUnownedTally = 256;
    internal const int DefaultCeilingEvictionsPerCycle = 1;

    private readonly IStandaloneBindlessBitmapBackend _backend;
    private readonly GpuSunsetRegister _sunsetRegister;
    private readonly HolderScopedAssetRegistry<uint> _holders = new();
    private readonly BoundedUnownedResourceShelf<uint> _unowned;
    private readonly Dictionary<uint, StandaloneBindlessBitmapAsset> _listings = [];
    private readonly HashSet<uint> _teardownResidencyReleased = [];
    private readonly HashSet<uint> _teardownDeleted = [];
    private bool _teardownAsked;
    private bool _disposing;
    private bool _destroyed;

    public StandaloneBindlessTextureShelf(
        IStandaloneBindlessBitmapBackend backend,
        IGpuAssetSunsetFifo sunsetFifo,
        long unownedAllowanceOctets = DefaultUnownedAllowanceOctets,
        int ceilingUnownedTally = DefaultCeilingUnownedTally)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ArgumentNullException.ThrowIfNull(sunsetFifo);
        _sunsetRegister = new GpuSunsetRegister(sunsetFifo);
        _unowned = new BoundedUnownedResourceShelf<uint>(
            unownedAllowanceOctets,
            ceilingUnownedTally);
    }

    internal int ListingTally => _listings.Count;
    internal int EngagedAssetTally => _holders.AssetTally;
    internal int HolderTally => _holders.HolderTally;
    internal int UnownedListingTally => _unowned.Count;
    internal long UnownedOctets => _unowned.HousedBytes;
    internal long AllocatedBytes { get; private set; }
    internal long RetiringBytes { get; private set; }
    internal long AllowanceOctets => _unowned.AllowanceOctets;
    internal int ExpectingSunsetBulletinTally =>
        _sunsetRegister.ExpectingBulletinTally;

    public bool TryAcquire(
        uint holderIdent,
        uint canvasIdent,
        out StandaloneBindlessBitmapAsset asset)
    {
        ObjectDisposedException.ThrowIf(_teardownAsked, this);
        VetHolderAndCanvas(holderIdent, canvasIdent);
        if (!_listings.TryGetValue(canvasIdent, out asset!))
            return false;

        _holders.Grab(holderIdent, canvasIdent);
        _unowned.FlagPossessed(canvasIdent);
        return true;
    }

    public void AppendAndObtain(
        uint holderIdent,
        StandaloneBindlessBitmapAsset resource)
    {
        ObjectDisposedException.ThrowIf(_teardownAsked, this);
        ArgumentNullException.ThrowIfNull(resource);
        VetHolderAndCanvas(holderIdent, resource.SurfaceId);
        if (!resource.IdentifiesATexture)
        {
            throw new ArgumentException(
                "A standalone particle texture must name either a GL texture and its "
                + "resident handle or a device texture",
                nameof(resource));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resource.Bytes);

        if (!_listings.TryAdd(resource.SurfaceId, resource))
            throw new InvalidOperationException(
                $"Standalone particle surface 0x{resource.SurfaceId:X8} is by now cached");

        _holders.Grab(holderIdent, resource.SurfaceId);
        AllocatedBytes = checked(AllocatedBytes + resource.Bytes);
    }

    public void ReleaseHolder(uint holderIdent)
    {
        ObjectDisposedException.ThrowIf(_teardownAsked, this);
        if (holderIdent is 0)
            return;

        var newlyUnowned = _holders.FreeOwner(holderIdent);
        for (int idx = 0; idx < newlyUnowned.Count; ++idx)
        {
            uint canvasIdent = newlyUnowned[idx];
            if (_listings.TryGetValue(canvasIdent, out StandaloneBindlessBitmapAsset? asset))
                _unowned.FlagUnowned(canvasIdent, asset.Bytes);
        }

    }

    public void Tick(int ceilingEvictions = DefaultCeilingEvictionsPerCycle)
    {
        ObjectDisposedException.ThrowIf(_teardownAsked, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceilingEvictions);
        _sunsetRegister.ReattemptPendingPublications();

        for (int idx = 0; idx < ceilingEvictions; ++idx)
        {
            if (!_unowned.TryGrabOldestOverAllowance(out uint canvasIdent))
                break;
            if (!_listings.Remove(canvasIdent, out StandaloneBindlessBitmapAsset? asset))
                continue;

            RetiringBytes = checked(RetiringBytes + asset.Bytes);
            _sunsetRegister.Retire(new RetryableGpuAssetFree(
                () => _backend.ForgeNonHoused(asset),
                () => _backend.Delete(asset),
                () => RetiringBytes = checked(
                    RetiringBytes - asset.Bytes),
                () => AllocatedBytes = checked(
                    AllocatedBytes - asset.Bytes)));
        }
    }

    public void Dispose()
    {
        if (_destroyed || _disposing)
            return;
        _teardownAsked = true;
        _disposing = true;

        try
        {
            List<Exception>? misses = null;
            try
            {
                _sunsetRegister.ReattemptPendingPublications();
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }

            foreach (StandaloneBindlessBitmapAsset asset in _listings.Values)
            {
                if (_teardownResidencyReleased.Contains(asset.SurfaceId))
                    continue;
                try
                {
                    _backend.ForgeNonHoused(asset);
                    _teardownResidencyReleased.Add(asset.SurfaceId);
                }
                catch (Exception exc)
                {
                    (misses ??= []).Add(exc);
                }
            }

            foreach (StandaloneBindlessBitmapAsset asset in _listings.Values)
            {
                if (!_teardownResidencyReleased.Contains(asset.SurfaceId)
                    || _teardownDeleted.Contains(asset.SurfaceId))

                    continue;

                try
                {
                    _backend.Delete(asset);
                    _teardownDeleted.Add(asset.SurfaceId);
                    AllocatedBytes = checked(
                        AllocatedBytes - asset.Bytes);
                }
                catch (Exception exc)
                {
                    (misses ??= []).Add(exc);
                }
            }

            if (_teardownDeleted.Count is not 0)
            {
                foreach (uint canvasIdent in _teardownDeleted)
                    _listings.Remove(canvasIdent);
            }

            if (_listings.Count is 0
                && _sunsetRegister.ExpectingBulletinTally is 0)
            {
                _holders.Clear();
                _unowned.Clear();
                _teardownResidencyReleased.Clear();
                _teardownDeleted.Clear();
                _destroyed = true;
            }

            if (misses is not null)
            {
                throw new AggregateException(
                    "One or more standalone particle textures could not retire",
                    misses);
            }
        }
        finally
        {
            _disposing = false;
        }
    }

    internal void TourListings(Action<StandaloneBindlessBitmapAsset> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (StandaloneBindlessBitmapAsset asset in _listings.Values)
            visitor(asset);
    }

    private static void VetHolderAndCanvas(uint holderIdent, uint canvasIdent)
    {
        ArgumentOutOfRangeException.ThrowIfZero(holderIdent);
        ArgumentOutOfRangeException.ThrowIfZero(canvasIdent);
    }
}
