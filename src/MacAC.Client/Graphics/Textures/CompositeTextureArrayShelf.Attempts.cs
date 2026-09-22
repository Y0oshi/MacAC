using MacAC.Mechanics.Surfaces;

namespace MacAC.Client.Graphics;

internal sealed partial class CompositeTextureArrayShelf
{
    public bool TryAcquire(
        uint holderOwnIdent,
        CompoundBitmapTag tag,
        out BindlessBitmapLocale locale)
    {
        HurlIfUnavailable();
        if (!_listings.TryGetValue(tag, out EntryUnit? listing))
        {
            locale = BindlessBitmapLocale.Unresolved;
            return false;
        }

        _holders.Grab(holderOwnIdent, tag);
        _unowned.FlagPossessed(tag);
        listing.Atlas.PreviousUseSeries = ++_useSeries;
        locale = new BindlessBitmapLocale(
            listing.Atlas.Resource.Slot,
            checked((uint)listing.Layer),
            listing.Atlas.Resource.RepeatSlot);
        return true;
    }

    public bool TryAppendAndObtain(
        uint holderOwnIdent,
        CompoundBitmapTag tag,
        UnpackedTexture decoded,
        out BindlessBitmapLocale locale)
    {
        HurlIfUnavailable();
        if (TryAcquire(holderOwnIdent, tag, out BindlessBitmapLocale extant))
        {
            locale = extant;
            return true;
        }

        VetDecodedTexture(decoded);
        long octets = checked((long)decoded.Width * decoded.Height * 4L);
        if (!CanPush(octets))
        {
            CanBeginPush = true;
            locale = BindlessBitmapLocale.Unresolved;
            return false;
        }

        if (!TrySeekOrBuildTileset(decoded.Width, decoded.Height, out Tileset tileset))
        {
            CanBeginPush = true;
            locale = BindlessBitmapLocale.Unresolved;
            return false;
        }

        int stratum = tileset.Slots.Rent();
        try
        {
            _backend.Upload(tileset.Resource, stratum, decoded.Rgba8);
        }
        catch (Exception pushMiss)
        {
            tileset.Slots.Yield(stratum);
            if (tileset.IsGpuSafeVacant)
            {
                try { EraseTileset(tileset); }
                catch (Exception freeMiss)
                {
                    throw new AggregateException(
                        "Composite upload and empty-atlas rollback both failed",
                        pushMiss,
                        freeMiss);
                }
            }
            throw;
        }

        EntryUnit listing = new EntryUnit { Atlas = tileset, Layer = stratum, Bytes = octets };
        _listings.Add(tag, listing);
        tileset.ListingCount++;
        tileset.PreviousUseSeries = ++_useSeries;
        _holders.Grab(holderOwnIdent, tag);
        ++CyclePushTally;
        CyclePushOctets = checked(CyclePushOctets + octets);
        locale = new BindlessBitmapLocale(tileset.Resource.Slot, checked((uint)stratum), tileset.Resource.RepeatSlot);
        return true;
    }

    private bool TrySeekOrBuildTileset(int width, int height, out Tileset tileset)
    {
        var dims = (width, height);
        if (_tilesetsByDims.TryGetValue(dims, out List<Tileset>? compatible))
        {
            for (int idx = 0; idx < compatible.Count; ++idx)
            {
                Tileset contender = compatible[idx];
                if (contender.IsReusable && contender.OnHandStrata is not 0)
                {
                    WipeQueuedAlloc(width, height);
                    tileset = contender;
                    return true;
                }
            }
        }

        int cap = DeriveStratumCap(width, height, _ceilingArrStrata);
        long askedOctets = checked((long)width * height * 4L * cap);
        if (_cycleTilesetCreationTally >= CeilingTilesetCreationsPerCycle
            || !CanReserveTileset(askedOctets))
        {
            AssignQueuedAlloc(width, height, askedOctets);
            tileset = null!;
            return false;
        }

        var asset = _backend.Create(width, height, cap);
        tileset = new Tileset
        {
            Resource = asset,
            Slots = new Batching.TextureAtlasSlotAllotter(cap),
        };
        if (compatible is null)
        {
            compatible = [];
            _tilesetsByDims.Add(dims, compatible);
        }
        compatible.Add(tileset);
        _tilesets.Add(tileset);
        AllocatedBytes = checked(AllocatedBytes + asset.Octets);
        ++_cycleTilesetCreationTally;
        WipeQueuedAlloc(width, height);
        return true;
    }
}
